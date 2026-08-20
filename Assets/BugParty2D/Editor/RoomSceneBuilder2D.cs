using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BugParty.TopDown2D.EditorTools
{
    /// <summary>
    /// 一键搭建 2D 俯视密室搜刮场景。菜单：BugParty2D ▸ Build Room Scene
    ///
    /// 相比之前版本的四项改动（对应需求）：
    ///   ★1 场景扩大：24×18 → 34×26，地板拆成 17×13 = 221 块可独立塌陷的方格
    ///   ★2 地形起伏：中央长桌、两侧矮柜、角落高台、斜坡，玩家可跳上去
    ///   ★3 故障元素：天花板碎片掉落 + 画面抖动 + 红色警报灯
    ///   ★4 地板塌陷：搜索中少量随机塌陷，终局全塌并掉落衔接下一关
    /// </summary>
    public static class RoomSceneBuilder2D
    {
        const string ConfigFolder = "Assets/BugParty2D/Config";
        const string ItemFolder = "Assets/BugParty2D/Config/Items";
        const string ConfigPath = ConfigFolder + "/RoomConfig2D.asset";
        const string ArtConfigPath = ConfigFolder + "/RoomArtConfig2D.asset";

        // ★场景尺寸：比之前大得多
        const int GridCols = 17;
        const int GridRows = 13;
        const float TileSize = 2f;

        static float RoomW => GridCols * TileSize;   // 34
        static float RoomD => GridRows * TileSize;   // 26
        const float WallH = 4.2f;
        const float WallT = 0.4f;

        // 高度层级：跳跃初速 7.2 / 重力 22 → 最大跳高约 1.18 米
        const float H_LOW = 0.55f;    // 矮柜、台阶，轻松跳上
        const float H_MID = 0.95f;    // 会议桌，需要正常跳
        const float H_HIGH = 1.45f;   // 高台，必须先跳矮的再跳上去（两段式）

        // ★菜单同时注册在两个位置，双保险：
        //   ① 顶级菜单 BugParty2D（priority 用 2000，避免挤进 File 左侧的保留区域）
        //   ② Tools 菜单下（顶级菜单万一被布局遮挡，这里一定能找到）
        [MenuItem("BugParty2D/Build Room Scene", false, 2000)]
        [MenuItem("Tools/BugParty2D/Build Room Scene", false, 2000)]
        public static void BuildScene()
        {
            if (!EditorUtility.DisplayDialog(
                "搭建 2D 俯视密室搜刮场景",
                "将生成：\n" +
                $"· {RoomW}×{RoomD} 大房间（{GridCols}×{GridRows}={GridCols * GridRows} 块可塌陷地板）\n" +
                "· 多层高度地形（中央长桌 / 矮柜 / 高台 / 斜坡）\n" +
                "· 12 个搜索容器（含 4 个需要跳上去的高台容器）\n" +
                "· 红色警报灯 + 天花板碎片系统\n" +
                "· 正交 2D 俯视相机（70° 俯角）\n" +
                "· 1 真人 + 3 AI、HUD 与全部配置资产\n\n" +
                "建议在空场景中执行。是否继续？",
                "开始生成", "取消"))
                return;

            var config = EnsureConfigAssets();

            // 旧版把美术字段直接放在 RoomConfig 上，这里一次性搬到 RoomArtConfig
            if (config != null && config.MigrateLegacyArtFields())
            {
                EditorUtility.SetDirty(config);
                if (config.art != null) EditorUtility.SetDirty(config.art);
                AssetDatabase.SaveAssets();
                Debug.Log("[Room2D] 已把旧版美术字段迁移到 RoomArtConfig。");
            }

            var old = GameObject.Find("=== Room2D ===");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("=== Room2D ===");
            Undo.RegisterCreatedObjectUndo(root, "Build Room2D");

            var grid = BuildFloorGrid(root.transform, config);
            BuildWalls(root.transform);
            var door = BuildDoor(root.transform);
            var platforms = BuildTerrain(root.transform, config);
            var containers = BuildContainers(root.transform, platforms, config);
            BuildBugProps(root.transform, config);
            var alarm = BuildAlarmSystem(root.transform);
            var debris = BuildDebrisSpawner(root.transform);
            var players = BuildPlayers(root.transform, config);
            BuildCamera(root.transform);
            BuildLights(root.transform);

            // 音效与特效总线：槽位全空也无妨，等美术资源到位再填
            var av = NewChild(root.transform, "AudioVfx");
            av.AddComponent<RoomAudioVfx>();

            var mgr = BuildManager(root.transform, config, players, containers, grid, door, debris);

            // 建完后重扫一次网格索引
            grid.Rebuild();

            Selection.activeGameObject = mgr.gameObject;
            MarkDirty();

            Debug.Log(
                "[Room2D] 场景生成完成！\n" +
                $"  · 房间 {RoomW}×{RoomD}，共 {GridCols * GridRows} 块可塌陷地板\n" +
                "  · 按 Play 试玩：WASD 移动，Space 跳（可跳上桌子），J 搜索，K 肘击，R 重开\n" +
                "  · 数值调整：" + ConfigPath + "\n" +
                "  · 俯角调整：RoomConfig 的 cameraPitch（70=推荐，85=更平，55=更立体）\n" +
                "  · 衔接下一关：RoomManager 的 nextSceneName 填目标场景名");
        }

        [MenuItem("BugParty2D/Create Config Assets Only", false, 2020)]
        [MenuItem("Tools/BugParty2D/Create Config Assets Only", false, 2020)]
        public static void CreateConfigOnly()
        {
            var cfg = EnsureConfigAssets();
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
        }

        /// <summary>
        /// 自检入口。菜单栏找不到 BugParty2D 时，用这个确认脚本到底有没有被编译进来。
        /// 位置：Tools ▸ BugParty2D ▸ 自检（确认脚本已加载）
        /// </summary>
        [MenuItem("Tools/BugParty2D/自检（确认脚本已加载）", false, 2040)]
        public static void SelfCheck()
        {
            int cs = 0;
            var files = Directory.Exists("Assets/BugParty2D")
                ? Directory.GetFiles("Assets/BugParty2D", "*.cs", SearchOption.AllDirectories)
                : new string[0];
            cs = files.Length;

            var msg =
                "✔ 编辑器脚本已正确加载并编译。\n\n" +
                $"Assets/BugParty2D 下的 .cs 文件数：{cs}（应为 22）\n" +
                $"Unity 版本：{Application.unityVersion}\n" +
                $"项目路径：{Directory.GetCurrentDirectory()}\n\n" +
                "如果你能看到这个弹窗，说明代码没问题。\n" +
                "顶级菜单 BugParty2D 若仍不显示，请用\n" +
                "Tools ▸ BugParty2D ▸ Build Room Scene 即可。";

            EditorUtility.DisplayDialog("BugParty2D 自检", msg, "好");
            Debug.Log("[BugParty2D] 自检通过。" + msg.Replace("\n", " "));
        }

        // ══════════════════════════════════════════════
        //  配置资产
        // ══════════════════════════════════════════════

        static RoomConfig EnsureConfigAssets()
        {
            EnsureFolder("Assets/BugParty2D");
            EnsureFolder(ConfigFolder);
            EnsureFolder(ItemFolder);

            var config = AssetDatabase.LoadAssetAtPath<RoomConfig>(ConfigPath);
            bool isNew = config == null;
            if (isNew)
            {
                config = ScriptableObject.CreateInstance<RoomConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            // ★美术配置独立成资产，让策划与美术改不同文件
            if (config.art == null)
            {
                var art = AssetDatabase.LoadAssetAtPath<RoomArtConfig>(ArtConfigPath);
                if (art == null)
                {
                    art = ScriptableObject.CreateInstance<RoomArtConfig>();
                    AssetDatabase.CreateAsset(art, ArtConfigPath);
                    Debug.Log("[Room2D] 已创建美术配置：" + ArtConfigPath +
                              "\n把家具/角色模型拖到它的槽位上即可替换外观，留空则用占位体。");
                }
                config.art = art;
                EditorUtility.SetDirty(config);
            }

            // 预填容器名，美术照名字拖模型即可（值留空不影响运行）
            if (config.art != null && (config.art.containerOverrides == null
                || config.art.containerOverrides.Count == 0))
            {
                config.art.containerOverrides = new List<NamedArtSlot>();
                foreach (var n in new[]
                {
                    "文件柜","饮水机","投影仪箱","打印机","纸箱堆","垃圾桶","杂物架","工具箱",
                    "桌上笔记本","桌上文件夹",
                    "顶柜保险箱","顶柜档案盒","顶柜服务器","顶柜工具包",
                })
                    config.art.containerOverrides.Add(new NamedArtSlot { key = n });
                EditorUtility.SetDirty(config.art);
            }

            if (isNew || config.itemPools == null || config.itemPools.Count == 0)
            {
                config.itemPools = new List<ThemeItemPool>
                {
                    new ThemeItemPool
                    {
                        theme = RoomTheme.Fishing,
                        items = new List<ItemDefinition>
                        {
                            MkItem("net_large", "大渔网", ItemCategory.Fishing, 420,
                                new Color(0.35f,0.75f,0.95f), 0.9f, false, "一次捕两条鱼，读条5秒"),
                            MkItem("net_small", "小渔网", ItemCategory.Fishing, 200,
                                new Color(0.55f,0.85f,0.95f), 1.4f, false, "一次一条鱼，读条5秒"),
                            MkItem("gloves", "徒手手套", ItemCategory.Fishing, 80,
                                new Color(0.85f,0.85f,0.80f), 1.5f, false, "一次一条，读条8秒"),
                            MkItem("knife", "小刀", ItemCategory.Destruction, 260,
                                new Color(0.90f,0.35f,0.30f), 1.0f, false, "贴身打断对手渔网，耐久3次"),
                            MkItem("mine", "水雷", ItemCategory.Destruction, 340,
                                new Color(0.95f,0.55f,0.20f), 0.7f, false, "隐形地雷，触发者定身5秒"),
                            MkItem("golden_chip", "金色Debug芯片", ItemCategory.Fishing, 900,
                                new Color(1f,0.82f,0.15f), 0.2f, true, "★稀有。多在高台容器出现"),
                        }
                    },
                    new ThemeItemPool
                    {
                        theme = RoomTheme.Cooking,
                        items = new List<ItemDefinition>
                        {
                            MkItem("chili", "辣椒", ItemCategory.Cooking, 150,
                                new Color(0.90f,0.20f,0.18f), 1.3f, false, "配洋葱生成火焰旋风"),
                            MkItem("onion", "洋葱", ItemCategory.Cooking, 150,
                                new Color(0.88f,0.78f,0.55f), 1.3f, false, "配辣椒生成火焰旋风"),
                            MkItem("potato", "土豆", ItemCategory.Cooking, 160,
                                new Color(0.72f,0.58f,0.35f), 1.3f, false, "配平底锅形成护盾"),
                            MkItem("pan", "平底锅", ItemCategory.Cooking, 400,
                                new Color(0.35f,0.35f,0.38f), 0.8f, false, "配土豆形成锅盖护盾"),
                            MkItem("tomato", "番茄", ItemCategory.Cooking, 150,
                                new Color(0.92f,0.28f,0.25f), 1.4f, false, "配鸡蛋发射追踪番茄弹"),
                            MkItem("egg", "鸡蛋", ItemCategory.Cooking, 140,
                                new Color(0.96f,0.94f,0.86f), 1.4f, false, "配番茄发射追踪弹"),
                            MkItem("radish", "白萝卜", ItemCategory.Cooking, 260,
                                new Color(0.94f,0.94f,0.90f), 1.0f, false, "配菜刀召唤萝卜剑士"),
                            MkItem("cleaver", "菜刀", ItemCategory.Cooking, 240,
                                new Color(0.62f,0.66f,0.70f), 0.9f, false, "配萝卜召唤萝卜剑士"),
                            MkItem("truffle", "故障松露", ItemCategory.Cooking, 880,
                                new Color(0.55f,0.25f,0.65f), 0.18f, true, "★稀有"),
                        }
                    },
                    new ThemeItemPool
                    {
                        theme = RoomTheme.Police,
                        items = new List<ItemDefinition>
                        {
                            MkItem("handcuff", "手铐", ItemCategory.Police, 220,
                                new Color(0.78f,0.80f,0.84f), 1.4f, false, "抓捕时间 2秒→1秒"),
                            MkItem("scanner", "扫描器", ItemCategory.Police, 320,
                                new Color(0.30f,0.80f,0.85f), 1.1f, false, "扇形范围标记可疑NPC"),
                            MkItem("taser", "电击枪", ItemCategory.Police, 300,
                                new Color(0.95f,0.85f,0.25f), 1.0f, false, "使一名玩家麻痹1.5秒"),
                            MkItem("dog", "警犬", ItemCategory.Police, 450,
                                new Color(0.70f,0.52f,0.30f), 0.7f, false, "圆形范围自动锁定小偷"),
                            MkItem("barrier", "路障", ItemCategory.Police, 180,
                                new Color(0.95f,0.60f,0.20f), 1.2f, false, "封锁出口，改变人群路径"),
                            MkItem("warrant", "空白逮捕令", ItemCategory.Police, 860,
                                new Color(1f,0.95f,0.85f), 0.18f, true, "★稀有"),
                        }
                    }
                };
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return config;
        }

        static ItemDefinition MkItem(
            string id, string name, ItemCategory cat, int value,
            Color col, float weight, bool rare, string effect)
        {
            string path = $"{ItemFolder}/Item_{id}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item != null) return item;

            item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = name;
            item.category = cat;
            item.lootValue = value;
            item.placeholderColor = col;
            item.spawnWeight = weight;
            item.isRare = rare;
            item.effectSummary = effect;
            AssetDatabase.CreateAsset(item, path);
            return item;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ══════════════════════════════════════════════
        //  ★地板网格（可塌陷）
        // ══════════════════════════════════════════════

        static FloorGrid BuildFloorGrid(Transform root, RoomConfig config)
        {
            var go = NewChild(root, "FloorGrid");
            var grid = go.AddComponent<FloorGrid>();
            var art = config != null ? config.art : null;
            var tileArt = art != null ? art.floorTile : null;

            grid.columns = GridCols;
            grid.rows = GridRows;
            grid.tileSize = TileSize;

            // 网格原点：让整个地板以世界原点为中心
            float ox = -(GridCols - 1) * TileSize * 0.5f;
            float oz = -(GridRows - 1) * TileSize * 0.5f;
            grid.origin = new Vector3(ox, 0f, oz);

            // 两种交替色，形成棋盘感，2D 俯视下更容易看清地形
            var colA = new Color(0.235f, 0.245f, 0.295f);
            var colB = new Color(0.205f, 0.215f, 0.265f);

            for (int x = 0; x < GridCols; x++)
            {
                for (int z = 0; z < GridRows; z++)
                {
                    var pos = new Vector3(ox + x * TileSize, -0.15f, oz + z * TileSize);
                    var col = (x + z) % 2 == 0 ? colA : colB;
                    var size = new Vector3(TileSize * 0.96f, 0.3f, TileSize * 0.96f);

                    // 留一点缝隙，让每块地板的边界可见 —— 玩家才能预判哪块会塌
                    var tileGo = ArtResolver.BuildSolid(
                        tileArt, go.transform, $"Tile_{x}_{z}", pos, size, col, out var tr);

                    var tile = tileGo.AddComponent<FloorTile>();
                    tile.gridPos = new Vector2Int(x, z);
                    // ★用美术模型时碰撞盒 Renderer 已关闭，须指向真正可见的 Renderer，
                    //   否则塌陷预警的染红看不见
                    tile.tileRenderer = tr != null ? tr : tileGo.GetComponent<Renderer>();
                    tile.solidColor = col;

                    // 材质可被美术替换
                    if (art != null && art.floorMatSolid != null && tile.tileRenderer != null)
                        tile.tileRenderer.sharedMaterial = art.floorMatSolid;

                    // 四角出生区受保护，不随机塌陷（终局仍会塌）
                    bool nearCorner =
                        (x <= 1 || x >= GridCols - 2) && (z <= 1 || z >= GridRows - 2);
                    tile.isProtected = nearCorner;
                }
            }

            grid.Rebuild();
            return grid;
        }

        // ══════════════════════════════════════════════
        //  墙体
        // ══════════════════════════════════════════════

        static void BuildWalls(Transform root)
        {
            var g = NewChild(root, "Walls");
            var wallCol = new Color(0.30f, 0.32f, 0.38f);

            float hx = RoomW * 0.5f;
            float hz = RoomD * 0.5f;

            // 北墙留门洞
            float gap = 4f;
            float seg = (RoomW - gap) * 0.5f;
            Cube(g, "Wall_N_L", new Vector3(-(gap * 0.5f + seg * 0.5f), WallH * 0.5f, hz),
                new Vector3(seg, WallH, WallT), wallCol);
            Cube(g, "Wall_N_R", new Vector3(gap * 0.5f + seg * 0.5f, WallH * 0.5f, hz),
                new Vector3(seg, WallH, WallT), wallCol);

            Cube(g, "Wall_S", new Vector3(0f, WallH * 0.5f, -hz),
                new Vector3(RoomW, WallH, WallT), wallCol);
            Cube(g, "Wall_E", new Vector3(hx, WallH * 0.5f, 0f),
                new Vector3(WallT, WallH, RoomD), wallCol);
            Cube(g, "Wall_W", new Vector3(-hx, WallH * 0.5f, 0f),
                new Vector3(WallT, WallH, RoomD), wallCol);

            // 白板（漏数据流）
            var board = Cube(g, "Whiteboard", new Vector3(0f, 2.1f, -hz + 0.3f),
                new Vector3(7f, 2.3f, 0.14f), new Color(0.84f, 0.89f, 0.92f));
            KillCollider(board);
            var ba = board.AddComponent<BugAmbience>();
            ba.bobAmplitude = 0.02f; ba.driftSpin = 0f;
            ba.glitchInterval = 2f; ba.glitchOffset = 0.05f;
        }

        static GameObject BuildDoor(Transform root)
        {
            var pivot = NewChild(root, "DoorPivot");
            pivot.transform.localPosition = new Vector3(-2f, 0f, RoomD * 0.5f);

            Cube(pivot, "DoorLeaf", new Vector3(2f, 1.8f, 0f),
                new Vector3(4f, 3.6f, 0.18f), new Color(0.40f, 0.29f, 0.21f));

            var glow = Cube(pivot, "DoorGlow", new Vector3(2f, 1.8f, 0.11f),
                new Vector3(4.1f, 3.7f, 0.03f), new Color(0.25f, 0.65f, 1.0f));
            KillCollider(glow);

            return pivot;
        }

        // ══════════════════════════════════════════════
        //  ★地形起伏（可跳上去的平台）
        // ══════════════════════════════════════════════

        struct PlatformInfo
        {
            public string name;
            public Vector3 center;
            public float topY;
        }

        static List<PlatformInfo> BuildTerrain(Transform root, RoomConfig config)
        {
            var g = NewChild(root, "Terrain");
            var list = new List<PlatformInfo>();
            var art = config != null ? config.art : null;

            var deskCol = new Color(0.52f, 0.38f, 0.26f);
            var cabCol = new Color(0.34f, 0.42f, 0.48f);
            var highCol = new Color(0.44f, 0.36f, 0.52f);
            var rampCol = new Color(0.38f, 0.40f, 0.44f);

            var deskArt = art != null ? art.desk : null;
            var chairArt = art != null ? art.chair : null;
            var cabArt = art != null ? art.cabinet : null;
            var highArt = art != null ? art.highPlatform : null;
            var rampArt = art != null ? art.rampStep : null;

            // ── ★中央长会议桌：本场景的地形核心 ──
            // 用两块拼成 L 形，制造"桌上追逐"的空间
            list.Add(AddPlatform(g, "MeetingDesk_Main",
                new Vector3(0f, 0f, 1.5f), new Vector3(11f, H_MID, 4f), deskCol, deskArt, art));
            list.Add(AddPlatform(g, "MeetingDesk_Wing",
                new Vector3(4.5f, 0f, -2.5f), new Vector3(4f, H_MID, 4f), deskCol, deskArt, art));

            // 桌边的椅子当踏板，让上桌有多条路线
            list.Add(AddPlatform(g, "Step_Chair_A",
                new Vector3(-6.5f, 0f, 1.5f), new Vector3(1.6f, H_LOW, 1.6f), cabCol, chairArt, art));
            list.Add(AddPlatform(g, "Step_Chair_B",
                new Vector3(6.5f, 0f, 3.2f), new Vector3(1.6f, H_LOW, 1.6f), cabCol, chairArt, art));
            list.Add(AddPlatform(g, "Step_Chair_C",
                new Vector3(1.5f, 0f, -4.6f), new Vector3(1.6f, H_LOW, 1.6f), cabCol, chairArt, art));

            // ── 两侧矮柜排：可以跳上去走"高架路线" ──
            for (int i = 0; i < 3; i++)
            {
                float z = -6f + i * 5.5f;
                list.Add(AddPlatform(g, $"Cabinet_W_{i}",
                    new Vector3(-12.5f, 0f, z), new Vector3(2.4f, H_LOW, 3.4f), cabCol, cabArt, art));
                list.Add(AddPlatform(g, $"Cabinet_E_{i}",
                    new Vector3(12.5f, 0f, z), new Vector3(2.4f, H_LOW, 3.4f), cabCol, cabArt, art));
            }

            // ── ★四角高台：需要两段跳（先矮柜再高台），回报是稀有道具 ──
            var highSpots = new[]
            {
                new Vector3(-14f, 0f,  10f),
                new Vector3( 14f, 0f,  10f),
                new Vector3(-14f, 0f, -10f),
                new Vector3( 14f, 0f, -10f),
            };
            for (int i = 0; i < highSpots.Length; i++)
            {
                // 先放一级踏板，保证高台可达
                var stepPos = highSpots[i] + new Vector3(
                    highSpots[i].x > 0 ? -2.6f : 2.6f, 0f, 0f);
                list.Add(AddPlatform(g, $"HighStep_{i}",
                    stepPos, new Vector3(2.2f, H_LOW, 2.2f), cabCol, cabArt, art));

                list.Add(AddPlatform(g, $"HighPlatform_{i}",
                    highSpots[i], new Vector3(4f, H_HIGH, 4f), highCol, highArt, art));
            }

            // ── 斜坡：不跳也能上矮柜，给不熟练的玩家留路 ──
            AddRamp(g, "Ramp_W", new Vector3(-9.5f, 0f, 8.5f), H_LOW, rampCol, false, rampArt);
            AddRamp(g, "Ramp_E", new Vector3(9.5f, 0f, -8.5f), H_LOW, rampCol, true, rampArt);

            return list;
        }

        static PlatformInfo AddPlatform(
            GameObject parent, string name, Vector3 basePos, Vector3 size, Color col,
            ArtSlot art = null, RoomArtConfig artCfg = null)
        {
            // basePos 是地面位置，方块中心要抬到 size.y/2
            var pos = new Vector3(basePos.x, size.y * 0.5f, basePos.z);

            // ★碰撞盒尺寸严格等于 size，所以 topY 与跳跃手感完全不受换模型影响
            var go = ArtResolver.BuildSolid(
                art, parent.transform, name, pos, size, col, out _);

            bool hasArt = art != null && art.HasArt;
            bool keepTop = artCfg == null || artCfg.keepPlatformTopFace;

            // 顶面加一层略亮的贴面，2D 俯视下更容易识别"这是可以站的平台"。
            // 用了美术模型且配置里关掉时就不再加。
            if (!hasArt || keepTop)
            {
                var top = Cube(go, "TopFace", new Vector3(0f, 0.51f, 0f),
                    new Vector3(0.94f, 0.06f, 0.94f),
                    Color.Lerp(col, Color.white, 0.22f));
                KillCollider(top);
            }

            // ★地板塌了之后家具要跟着掉，否则会悬在空中
            var fall = go.AddComponent<FallingProp>();
            fall.mass = Mathf.Clamp(size.x * size.z * 0.35f, 0.8f, 6f);   // 大件更重

            return new PlatformInfo { name = name, center = basePos, topY = size.y };
        }

        /// <summary>用几级台阶模拟斜坡。CharacterController 的 stepOffset 能自动爬上去。</summary>
        static void AddRamp(
            GameObject parent, string name, Vector3 basePos, float targetH, Color col, bool flip,
            ArtSlot art = null)
        {
            const int steps = 4;
            var g = NewChild(parent, name);

            for (int i = 0; i < steps; i++)
            {
                float h = targetH * (i + 1) / steps;
                float offset = (flip ? -1f : 1f) * (i * 0.8f);
                var pos = new Vector3(basePos.x + offset, h * 0.5f, basePos.z);
                var size = new Vector3(0.8f, h, 3f);
                var step = ArtResolver.BuildSolid(art, g.transform, $"{name}_Step{i}", pos, size, col, out _);

                var fall = step.AddComponent<FallingProp>();
                fall.mass = 1.2f;
            }
        }

        // ══════════════════════════════════════════════
        //  容器
        // ══════════════════════════════════════════════

        static List<SearchContainer> BuildContainers(
            Transform root, List<PlatformInfo> platforms, RoomConfig config)
        {
            var g = NewChild(root, "Containers");
            var list = new List<SearchContainer>();
            var art = config != null ? config.art : null;

            // ── 地面容器（8 个）──
            var groundSpecs = new[]
            {
                new { name = "文件柜",     pos = new Vector3(-15.2f, 0f,  4.5f), rarity = 0.4f },
                new { name = "饮水机",     pos = new Vector3( 15.2f, 0f,  4.5f), rarity = 0.3f },
                new { name = "投影仪箱",   pos = new Vector3(-15.2f, 0f, -4.5f), rarity = 0.4f },
                new { name = "打印机",     pos = new Vector3( 15.2f, 0f, -4.5f), rarity = 0.3f },
                new { name = "纸箱堆",     pos = new Vector3(-6.5f,  0f, -9f),   rarity = 0.2f },
                new { name = "垃圾桶",     pos = new Vector3( 6.5f,  0f, -9f),   rarity = 0.2f },
                new { name = "杂物架",     pos = new Vector3(-6.5f,  0f,  9.5f), rarity = 0.5f },
                new { name = "工具箱",     pos = new Vector3( 6.5f,  0f,  9.5f), rarity = 0.5f },
            };

            for (int i = 0; i < groundSpecs.Length; i++)
            {
                var s = groundSpecs[i];
                var slot = art != null ? art.GetContainerArt(s.name) : null;
                var go = ArtResolver.BuildSolid(
                    slot, g.transform, "Box_" + s.name,
                    new Vector3(s.pos.x, 0.5f, s.pos.z),
                    new Vector3(1.1f, 1.0f, 1.0f),
                    new Color(0.55f, 0.44f, 0.32f), out var gr);

                var sc = AttachContainer(go, s.name, s.rarity, false, 1.0f, gr);
                list.Add(sc);
            }

            // ── ★桌面容器（2 个）：必须跳上桌子才能搜 ──
            var deskSpecs = new[]
            {
                new { name = "桌上笔记本", pos = new Vector3(-3f, H_MID, 1.5f) },
                new { name = "桌上文件夹", pos = new Vector3( 3f, H_MID, 2.5f) },
            };
            for (int i = 0; i < deskSpecs.Length; i++)
            {
                var s = deskSpecs[i];
                var slot = art != null ? art.GetContainerArt(s.name) : null;
                var go = ArtResolver.BuildSolid(
                    slot, g.transform, "Box_" + s.name,
                    new Vector3(s.pos.x, s.pos.y + 0.32f, s.pos.z),
                    new Vector3(0.9f, 0.5f, 0.7f),
                    new Color(0.62f, 0.52f, 0.38f), out var dr);

                var sc = AttachContainer(go, s.name, 0.9f, true, 0.5f, dr);
                list.Add(sc);
            }

            // ── ★高台容器（4 个）：两段跳才能到，稀有度最高 ──
            var highSpots = new[]
            {
                new Vector3(-14f, H_HIGH,  10f),
                new Vector3( 14f, H_HIGH,  10f),
                new Vector3(-14f, H_HIGH, -10f),
                new Vector3( 14f, H_HIGH, -10f),
            };
            var highNames = new[] { "顶柜保险箱", "顶柜档案盒", "顶柜服务器", "顶柜工具包" };

            for (int i = 0; i < highSpots.Length; i++)
            {
                var slot = art != null ? art.GetContainerArt(highNames[i]) : null;
                var go = ArtResolver.BuildSolid(
                    slot, g.transform, "Box_" + highNames[i],
                    new Vector3(highSpots[i].x, highSpots[i].y + 0.42f, highSpots[i].z),
                    new Vector3(1.1f, 0.8f, 1.0f),
                    new Color(0.50f, 0.40f, 0.58f), out var hr);

                var sc = AttachContainer(go, highNames[i], 1.5f, true, 0.8f, hr);
                list.Add(sc);
            }

            return list;
        }

        static SearchContainer AttachContainer(
            GameObject go, string name, float rarity, bool elevated, float ownHeight,
            Renderer highlightTarget = null)
        {
            var anchor = new GameObject("InteractPoint");
            anchor.transform.SetParent(go.transform, false);
            // 交互点放在容器正前方一点，localPosition 需按父级缩放换算
            anchor.transform.localPosition = new Vector3(0f, 0f, 0.9f);

            var sc = go.AddComponent<SearchContainer>();
            sc.containerName = name;
            sc.interactAnchor = anchor.transform;
            sc.remainingYield = 2;
            sc.rarityBonus = rarity;
            sc.isElevated = elevated;
            // ★用美术模型时碰撞盒的 Renderer 已关闭，必须改指向真正可见的 Renderer，
            //   否则搜索高亮不会显示
            sc.highlightRenderer = highlightTarget != null
                ? highlightTarget
                : go.GetComponent<Renderer>();

            // ★塌陷时跟着掉。架高的容器（桌面/高台）不直接坐在地板上，
            //   所以只在终局全塌时掉，避免脚下那格地板塌了它就凭空坠落。
            var fall = go.AddComponent<FallingProp>();
            fall.fallWithTileBelow = !elevated;
            fall.mass = elevated ? 0.7f : 1.0f;
            fall.tumbleTorque = 3.5f;     // 小件翻滚更明显

            return sc;
        }

        // ══════════════════════════════════════════════
        //  故障氛围道具
        // ══════════════════════════════════════════════

        static void BuildBugProps(Transform root, RoomConfig config)
        {
            var g = NewChild(root, "BugProps");
            var art = config != null ? config.art : null;
            var floatArt = art != null ? art.floatingProp : null;

            // ═══ 天花板数据裂缝 ═══
            // ★挪到房间靠后的墙边，不再横在会议桌正上方。
            //   相机俯角 70° 而非垂直，高处物体在画面上会向下偏移压住玩法区，
            //   原本 12 米长的裂缝正好盖住桌面与两个桌面容器。
            // ★底色改成暗青。原先底色与 BugAmbience.glitchColor 是同一个值
            //   (0.25,0.70,1.0)，Lerp 混合后毫无变化，故障闪烁完全看不出来。
            float crackZ = -(GridRows * TileSize) * 0.5f + 2.2f;
            var crackBase = new Color(0.10f, 0.26f, 0.42f);
            var crack = Cube(g, "CeilingCrack", new Vector3(0f, WallH - 0.15f, crackZ),
                new Vector3(4.5f, 0.1f, 0.5f), crackBase);
            KillCollider(crack);
            var ca = crack.AddComponent<BugAmbience>();
            ca.bobAmplitude = 0.05f;
            ca.glitchInterval = 1.6f;
            ca.driftSpin = 0f;
            ca.glitchColor = new Color(0.45f, 0.90f, 1.0f);   // 比底色亮得多，闪烁才可见

            // ═══ ★中央吊扇 ═══
            // 取代原先横贯画面的裂缝。圆形紧凑，占地远小于 12 米长条，
            // 旋转本身即是持续动态，比静止长条更适合做氛围。
            BuildCeilingFan(g, config);

            // 漂浮像素块（数量随场景放大而增加）
            for (int i = 0; i < 34; i++)
            {
                var px = Cube(g, "Pixel_" + (i + 1),
                    new Vector3(Random.Range(-15f, 15f), Random.Range(1f, 3.8f), Random.Range(-11f, 11f)),
                    Vector3.one * Random.Range(0.10f, 0.26f),
                    new Color(0.28f, Random.Range(0.55f, 0.9f), 1.0f));
                KillCollider(px);
                var a = px.AddComponent<BugAmbience>();
                a.bobAmplitude = Random.Range(0.2f, 0.5f);
                a.bobSpeed = Random.Range(0.6f, 1.9f);
                a.driftSpin = Random.Range(-70f, 70f);
                a.glitchInterval = Random.Range(1.2f, 4.5f);
            }

            // 悬浮椅子（装饰，无碰撞）。★填了 floatingProp 就用真家具模型，
            // 故障感由 BugAmbience 在运行时叠加，所以这里该填「正常的家具」。
            for (int i = 0; i < 10; i++)
            {
                var pos = new Vector3(Random.Range(-13f, 13f), Random.Range(1.4f, 2.6f), Random.Range(-10f, 10f));
                var size = new Vector3(0.6f, 0.6f, 0.6f);
                GameObject ch;
                if (floatArt != null && floatArt.HasArt)
                {
                    ch = NewChild(g, "FloatChair_" + (i + 1));
                    ch.transform.localPosition = pos;
                    ArtResolver.InstantiateArt(floatArt, ch.transform, size, "Art");
                }
                else
                {
                    ch = Cube(g, "FloatChair_" + (i + 1), pos, size, new Color(0.29f, 0.31f, 0.37f));
                    KillCollider(ch);
                }
                ch.transform.localRotation = Quaternion.Euler(
                    Random.Range(-30f, 30f), Random.Range(0f, 360f), Random.Range(-30f, 30f));

                var a = ch.AddComponent<BugAmbience>();
                a.bobAmplitude = Random.Range(0.12f, 0.28f);
                a.bobSpeed = Random.Range(0.7f, 1.5f);
                a.driftSpin = Random.Range(-18f, 18f);
            }
        }

        /// <summary>
        /// 天花板吊扇。放在房间正中央，取代原先横贯画面的数据裂缝。
        ///
        /// ★与其他家具不同，吊扇模型是「顶面对齐 y=0、向下延伸」建模的
        ///   （Kenney ceilingFan 的 Y 范围是 -1.34~0），
        ///   不能走 ArtResolver 的底面贴地对齐，否则会被推到天花板上面去。
        ///   这里手动挂到吊点下方。
        /// </summary>
        static void BuildCeilingFan(GameObject parent, RoomConfig config)
        {
            var art = config != null ? config.art : null;
            var fanArt = art != null ? art.ceilingFan : null;

            // 吊点：房间正中央，略低于天花板
            var pivot = NewChild(parent, "CeilingFan");
            pivot.transform.localPosition = new Vector3(0f, WallH - 0.45f, 0f);

            var fan = pivot.AddComponent<CeilingFan>();

            if (fanArt != null && fanArt.HasArt)
            {
                // ── 美术模型 ──
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fanArt.prefab);
                model.name = "Art";
                model.transform.SetParent(pivot.transform, false);
                model.transform.localPosition = new Vector3(0f, fanArt.yOffset, 0f);
                model.transform.localRotation = Quaternion.Euler(0f, fanArt.yawOffset, 0f);
                ArtResolver.StripColliders(model);

                // 按目标直径等比缩放。吊扇模型的自然直径约 4.5~5.2 单位，
                // 缩到 2.2 米左右在俯视下体量合适，也不会盖住会议桌
                float target = 2.2f;
                var r = model.GetComponentInChildren<Renderer>();
                if (r != null)
                {
                    var sz = r.bounds.size;
                    float diameter = Mathf.Max(sz.x, sz.z);
                    if (diameter > 1e-3f)
                        model.transform.localScale = Vector3.one * (target / diameter) * fanArt.scaleMul;
                }
                fan.spinTarget = model.transform;
            }
            else
            {
                // ── 占位体：中心轴 + 四片扇叶 ──
                var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                hub.name = "Art";
                hub.transform.SetParent(pivot.transform, false);
                hub.transform.localPosition = Vector3.zero;
                hub.transform.localScale = new Vector3(0.22f, 0.09f, 0.22f);
                KillCollider(hub);
                SetColor(hub.GetComponent<Renderer>(), new Color(0.32f, 0.34f, 0.40f));

                for (int i = 0; i < 4; i++)
                {
                    var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    blade.name = "Blade_" + (i + 1);
                    blade.transform.SetParent(hub.transform, false);
                    // hub 有非等比 scale，扇叶的 localScale 需按比例换算
                    blade.transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
                    blade.transform.localPosition =
                        blade.transform.localRotation * new Vector3(0f, 0f, 2.6f);
                    blade.transform.localScale = new Vector3(1.5f, 0.35f, 4.6f);
                    KillCollider(blade);
                    SetColor(blade.GetComponent<Renderer>(), new Color(0.42f, 0.44f, 0.50f));
                }
                fan.spinTarget = hub.transform;
            }

            // 吊杆：从天花板连到扇体，让它看起来是"吊"着的
            var rod = Cube(pivot, "Rod", new Vector3(0f, 0.28f, 0f),
                new Vector3(0.08f, 0.56f, 0.08f), new Color(0.28f, 0.30f, 0.35f));
            KillCollider(rod);
        }

        // ══════════════════════════════════════════════
        //  ★警报系统
        // ══════════════════════════════════════════════

        static AlarmSystem BuildAlarmSystem(Transform root)
        {
            var g = NewChild(root, "AlarmSystem");
            var alarm = g.AddComponent<AlarmSystem>();

            var lights = new List<Light>();
            var lamps = new List<Renderer>();

            // 六个警报灯沿墙分布，保证整个房间都能被红光覆盖
            var spots = new[]
            {
                new Vector3(-14f, 3.6f,  11f), new Vector3( 14f, 3.6f,  11f),
                new Vector3(-14f, 3.6f, -11f), new Vector3( 14f, 3.6f, -11f),
                new Vector3(  0f, 3.6f,  12f), new Vector3(  0f, 3.6f, -12f),
            };

            for (int i = 0; i < spots.Length; i++)
            {
                var holder = NewChild(g, "AlarmLamp_" + (i + 1));
                holder.transform.localPosition = spots[i];

                // 灯罩实体
                var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lamp.name = "Lamp";
                lamp.transform.SetParent(holder.transform, false);
                lamp.transform.localScale = Vector3.one * 0.5f;
                Object.DestroyImmediate(lamp.GetComponent<Collider>());
                var lampR = lamp.GetComponent<Renderer>();
                SetColor(lampR, new Color(0.28f, 0.10f, 0.10f));
                lamps.Add(lampR);

                // 红光
                var lt = holder.AddComponent<Light>();
                lt.type = LightType.Point;
                lt.range = 16f;
                lt.intensity = 0f;
                lt.color = new Color(1f, 0.15f, 0.12f);
                lt.enabled = false;
                lights.Add(lt);
            }

            alarm.alarmLights = lights.ToArray();
            alarm.alarmLampRenderers = lamps.ToArray();
            return alarm;
        }

        static CeilingDebrisSpawner BuildDebrisSpawner(Transform root)
        {
            var g = NewChild(root, "CeilingDebris");
            var d = g.AddComponent<CeilingDebrisSpawner>();
            d.area = new Vector2(RoomW - 2f, RoomD - 2f);
            d.spawnHeight = WallH - 0.3f;
            d.poolSize = 48;
            return d;
        }

        // ══════════════════════════════════════════════
        //  玩家
        // ══════════════════════════════════════════════

        static List<PlayerActor> BuildPlayers(Transform root, RoomConfig config)
        {
            var g = NewChild(root, "Players");
            var list = new List<PlayerActor>();

            // 四角出生，间距很大（场景变大的直接好处）
            var spawns = new[]
            {
                new Vector3(-13f, 1.2f,  9f),
                new Vector3( 13f, 1.2f,  9f),
                new Vector3(-13f, 1.2f, -9f),
                new Vector3( 13f, 1.2f, -9f),
            };
            var colors = new[] { PlayerColor.Red, PlayerColor.Blue, PlayerColor.Yellow, PlayerColor.Green };
            float[] aggro = { 0.18f, 0.08f, -0.08f, -0.16f };

            for (int i = 0; i < 4; i++)
            {
                bool isHuman = (i == 0);

                var go = NewChild(g, (isHuman ? "Player_" : "AI_") + colors[i]);
                go.transform.localPosition = spawns[i];
                go.transform.localRotation = Quaternion.LookRotation(
                    new Vector3(-spawns[i].x, 0f, -spawns[i].z).normalized, Vector3.up);

                var cc = go.AddComponent<CharacterController>();
                cc.height = 1.5f;
                cc.radius = 0.38f;
                cc.center = new Vector3(0f, 0.75f, 0f);
                cc.slopeLimit = 55f;
                // ★stepOffset 要够大才能自动爬上斜坡台阶
                cc.stepOffset = 0.42f;
                cc.skinWidth = 0.03f;

                // ═══ 视觉体 ═══
                // ★通过 ArtResolver 统一处理：RoomArtConfig 填了模型就用美术资源，
                //   否则退回程序生成的占位胶囊体。两种情况挂点结构完全一致。
                var visual = NewChild(go, "Visual");
                Renderer bodyR = null;

                var artCfg = config != null ? config.art : null;
                var charSlot = artCfg != null ? artCfg.GetCharacterArt(i) : null;
                float charH = artCfg != null ? artCfg.characterHeight : 1.5f;

                if (charSlot != null && charSlot.HasArt)
                {
                    // ── 美术资源模式 ──
                    // 按角色身高自动缩放，避免不同来源的模型大小不一
                    bodyR = ArtResolver.InstantiateArt(
                        charSlot, visual.transform,
                        new Vector3(charH * 0.5f, charH, charH * 0.5f), "Model");

                    // 有 Animator 就自动挂桥接层，不需要手动拖
                    var anim = visual.GetComponentInChildren<Animator>();
                    if (anim != null)
                    {
                        var bridge = go.AddComponent<PlayerAnimatorBridge>();
                        bridge.animator = anim;
                    }
                }
                else
                {
                    // ── 占位体模式 ──
                    var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    body.name = "Body";
                    body.transform.SetParent(visual.transform, false);
                    body.transform.localPosition = new Vector3(0f, 0.75f, 0f);
                    body.transform.localScale = new Vector3(0.72f, 0.52f, 0.72f);
                    Object.DestroyImmediate(body.GetComponent<Collider>());
                    bodyR = body.GetComponent<Renderer>();
                    SetColor(bodyR, colors[i].ToColor());

                    var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    head.name = "Head";
                    head.transform.SetParent(visual.transform, false);
                    head.transform.localPosition = new Vector3(0f, 1.42f, 0f);
                    head.transform.localScale = Vector3.one * 0.56f;
                    Object.DestroyImmediate(head.GetComponent<Collider>());
                    SetColor(head.GetComponent<Renderer>(), new Color(0.97f, 0.86f, 0.74f));

                    // 朝向指示
                    var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    nose.name = "Facing";
                    nose.transform.SetParent(visual.transform, false);
                    nose.transform.localPosition = new Vector3(0f, 1.42f, 0.32f);
                    nose.transform.localScale = new Vector3(0.12f, 0.12f, 0.2f);
                    Object.DestroyImmediate(nose.GetComponent<Collider>());
                    SetColor(nose.GetComponent<Renderer>(), Color.white);
                }

                // ★落地阴影：2D 俯视下判断高度的唯一线索。
                // 刻意不作为玩家子物体——否则会跟着角色旋转，方形阴影会明显打转
                var shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                shadow.name = "Shadow_" + colors[i];
                shadow.transform.SetParent(g.transform, false);
                shadow.transform.position = spawns[i];
                shadow.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                shadow.transform.localScale = Vector3.one;
                Object.DestroyImmediate(shadow.GetComponent<Collider>());

                // 阴影材质可被美术替换（软边圆形阴影比方片好看很多）
                var shadowR = shadow.GetComponent<Renderer>();
                if (artCfg != null && artCfg.shadowMaterial != null)
                    shadowR.sharedMaterial = artCfg.shadowMaterial;
                else
                    SetColor(shadowR, new Color(0f, 0f, 0f, 0.42f));

                // 挂点
                var hand = NewChild(go, "HandAnchor");
                hand.transform.localPosition = new Vector3(0.35f, 0.85f, 0.3f);

                var elbow = NewChild(go, "ElbowOrigin");
                elbow.transform.localPosition = new Vector3(0f, 0.8f, 0.1f);

                // 组件
                go.AddComponent<PlayerInventory>();
                go.AddComponent<SearchAbility>();
                go.AddComponent<ElbowAbility>();

                var actor = go.AddComponent<PlayerActor>();
                actor.playerColor = colors[i];
                actor.displayName = colors[i].ToLabel() + "方";
                actor.bodyRenderer = bodyR;
                actor.visualRoot = visual.transform;
                actor.handAnchor = hand.transform;
                actor.elbowOrigin = elbow.transform;
                actor.shadowQuad = shadow.transform;

                if (isHuman)
                {
                    var hb = go.AddComponent<HumanBrain>();
                    hb.keys = InputScheme.Player1();
                    hb.cameraRelative = true;
                }
                else
                {
                    var ai = go.AddComponent<AIBrain>();
                    ai.aggressionBias = aggro[i];
                    ai.noise = 0.15f;
                }

                list.Add(actor);
            }
            return list;
        }

        // ══════════════════════════════════════════════
        //  相机与灯光
        // ══════════════════════════════════════════════

        static void BuildCamera(Transform root)
        {
            var camGo = Camera.main != null ? Camera.main.gameObject : null;
            if (camGo == null)
            {
                camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
            }
            camGo.transform.SetParent(root, true);

            var center = NewChild(root, "RoomCenter");
            center.transform.localPosition = Vector3.zero;

            var rig = camGo.GetComponent<TopDownCamera2D>();
            if (rig == null) rig = camGo.AddComponent<TopDownCamera2D>();
            rig.lookTarget = center.transform;
            rig.overrideConfig = false;

            var cam = camGo.GetComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 11.5f;
            cam.nearClipPlane = -30f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.09f);
        }

        static void BuildLights(Transform root)
        {
            var g = NewChild(root, "Lights");

            var sun = NewChild(g, "KeyLight");
            sun.transform.localRotation = Quaternion.Euler(58f, -28f, 0f);
            var sl = sun.AddComponent<Light>();
            sl.type = LightType.Directional;
            sl.intensity = 0.95f;
            sl.color = new Color(1f, 0.97f, 0.93f);
            // 2D 俯视下软阴影能帮助判断高度
            sl.shadows = LightShadows.Soft;

            // 天花板灯，覆盖大房间
            var spots = new[]
            {
                new Vector3(-10f, 3.8f,  8f), new Vector3( 10f, 3.8f,  8f),
                new Vector3(-10f, 3.8f, -8f), new Vector3( 10f, 3.8f, -8f),
                new Vector3(  0f, 3.8f,  0f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var lp = NewChild(g, "CeilingLamp_" + (i + 1));
                lp.transform.localPosition = spots[i];
                var l = lp.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 15f;
                l.intensity = 1.1f;
                l.color = new Color(1f, 0.95f, 0.86f);
            }

            var fill = NewChild(g, "BugFill");
            fill.transform.localPosition = new Vector3(0f, 3f, 0f);
            var fl = fill.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.range = 30f;
            fl.intensity = 0.7f;
            fl.color = new Color(0.32f, 0.58f, 1f);
        }

        // ══════════════════════════════════════════════

        static RoomManager BuildManager(
            Transform root, RoomConfig config,
            List<PlayerActor> players, List<SearchContainer> containers,
            FloorGrid grid, GameObject door, CeilingDebrisSpawner debris)
        {
            var go = NewChild(root, "RoomManager");

            var mgr = go.AddComponent<RoomManager>();
            mgr.config = config;
            mgr.theme = RoomTheme.Fishing;
            mgr.players = players;
            mgr.containers = containers;
            mgr.floorGrid = grid;
            mgr.doorPivot = door != null ? door.transform : null;
            mgr.debrisSpawner = debris;
            mgr.nextSceneName = "";      // 留空＝单独测试本环节
            mgr.loopForTesting = true;
            mgr.verboseLog = true;
            mgr.allowRestartKey = true;

            go.AddComponent<RoomHUD>();
            return mgr;
        }

        // ══════════════════════════════════════════════
        //  工具方法（两种父级重载）
        // ══════════════════════════════════════════════

        static GameObject NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject NewChild(GameObject parent, string name)
            => NewChild(parent != null ? parent.transform : null, name);

        static GameObject Cube(Transform parent, string name, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            SetColor(go.GetComponent<Renderer>(), color);
            return go;
        }

        static GameObject Cube(GameObject parent, string name, Vector3 pos, Vector3 size, Color color)
            => Cube(parent != null ? parent.transform : null, name, pos, size, color);

        static void KillCollider(GameObject go)
        {
            if (go == null) return;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);
        }

        static void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            var mat = new Material(r.sharedMaterial);
            mat.name = "Mat_" + r.gameObject.name;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            r.sharedMaterial = mat;
        }

        static void MarkDirty()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
