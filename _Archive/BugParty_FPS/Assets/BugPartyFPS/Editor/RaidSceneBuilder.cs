using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BugParty.FPS.EditorTools
{
    /// <summary>
    /// 一键搭建第一人称密室搜刮场景。菜单：BugPartyFPS ▸ Build Raid Scene
    ///
    /// 相比俯视版，房间做了三处针对 FPS 的改动：
    ///   1. 面积放大到 24×18，并加入内部隔断墙 → 形成视线遮挡与迷宫感
    ///   2. 容器数量增加到 10 个，分散在各个角落 → 需要探索
    ///   3. 加入 2 个撤离点，位于对角 → 撤离时会发生遭遇战
    /// </summary>
    public static class RaidSceneBuilder
    {
        const string ConfigFolder = "Assets/BugPartyFPS/Config";
        const string ItemFolder = "Assets/BugPartyFPS/Config/Items";
        const string ConfigPath = ConfigFolder + "/RaidConfig.asset";

        // 房间尺寸：FPS 需要更大的空间
        const float RoomW = 24f;
        const float RoomD = 18f;
        const float WallH = 3.6f;
        const float WallT = 0.35f;

        [MenuItem("BugPartyFPS/Build Raid Scene", false, 0)]
        public static void BuildScene()
        {
            if (!EditorUtility.DisplayDialog(
                "搭建第一人称密室搜刮场景",
                "将生成完整的 FPS 搜刮场景：\n" +
                "· 24×18 房间 + 内部隔断（视线遮挡）\n" +
                "· 10 个可搜容器\n" +
                "· 2 个对角撤离点\n" +
                "· 1 名真人（红方）+ 3 名 AI\n" +
                "· 相机、灯光、HUD 与全部配置资产\n\n" +
                "建议在空场景中执行。是否继续？",
                "开始生成", "取消"))
                return;

            var config = EnsureConfigAssets();

            var old = GameObject.Find("=== FPS Raid ===");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("=== FPS Raid ===");
            Undo.RegisterCreatedObjectUndo(root, "Build Raid Scene");

            BuildRoom(root.transform);
            var door = BuildDoor(root.transform);
            BuildInteriorWalls(root.transform);
            BuildBugProps(root.transform);
            var containers = BuildContainers(root.transform);
            var zones = BuildExtractionZones(root.transform);
            var players = BuildPlayers(root.transform);
            BuildLights(root.transform);
            var mgr = BuildManager(root.transform, config, players, containers, zones, door);

            Selection.activeGameObject = mgr.gameObject;
            MarkDirty();

            Debug.Log(
                "[FPS Raid] 场景生成完成！\n" +
                "  · 按 Play 直接试玩（鼠标会被锁定，Esc 解锁）\n" +
                "  · WASD 移动　Shift 疾跑　Ctrl 蹲　F 搜刮　V/左键 肘击　G 全取　R 重开\n" +
                "  · 数值调整：" + ConfigPath + "\n" +
                "  · 切换主题：选中 RaidManager 改 Theme 字段");
        }

        [MenuItem("BugPartyFPS/Create Config Assets Only", false, 20)]
        public static void CreateConfigOnly()
        {
            var cfg = EnsureConfigAssets();
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
            Debug.Log("[FPS Raid] 配置资产已就绪：" + ConfigPath);
        }

        // ══════════════════════════════════════════════
        //  配置资产
        // ══════════════════════════════════════════════

        static RaidConfig EnsureConfigAssets()
        {
            EnsureFolder("Assets/BugPartyFPS");
            EnsureFolder(ConfigFolder);
            EnsureFolder(ItemFolder);

            var config = AssetDatabase.LoadAssetAtPath<RaidConfig>(ConfigPath);
            bool isNew = config == null;
            if (isNew)
            {
                config = ScriptableObject.CreateInstance<RaidConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
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
                            // 参数依次：id, 名称, 类别, 宽, 高, 价值, 颜色, 权重, 稀有, 说明
                            MkItem("net_large", "大渔网", ItemCategory.Fishing, 2, 2, 420,
                                new Color(0.35f,0.75f,0.95f), 0.9f, false, "一次捕两条鱼，读条5秒。占位大，是最经典的贪心陷阱"),
                            MkItem("net_small", "小渔网", ItemCategory.Fishing, 2, 1, 200,
                                new Color(0.55f,0.85f,0.95f), 1.4f, false, "一次一条鱼，读条5秒"),
                            MkItem("gloves", "徒手手套", ItemCategory.Fishing, 1, 1, 80,
                                new Color(0.85f,0.85f,0.80f), 1.5f, false, "一次一条，读条8秒，保底选择"),
                            MkItem("knife", "小刀", ItemCategory.Destruction, 1, 1, 260,
                                new Color(0.90f,0.35f,0.30f), 1.0f, false, "贴身打断对手渔网，耐久3次"),
                            MkItem("mine", "水雷", ItemCategory.Destruction, 1, 2, 340,
                                new Color(0.95f,0.55f,0.20f), 0.7f, false, "隐形地雷，触发者定身5秒"),
                            MkItem("golden_chip", "金色Debug芯片", ItemCategory.Fishing, 1, 1, 900,
                                new Color(1f,0.82f,0.15f), 0.18f, true, "★稀有。直接计入最终排名的硬通货"),
                        }
                    },
                    new ThemeItemPool
                    {
                        theme = RoomTheme.Cooking,
                        items = new List<ItemDefinition>
                        {
                            MkItem("chili", "辣椒", ItemCategory.Cooking, 1, 1, 150,
                                new Color(0.90f,0.20f,0.18f), 1.3f, false, "配洋葱生成火焰旋风"),
                            MkItem("onion", "洋葱", ItemCategory.Cooking, 1, 1, 150,
                                new Color(0.88f,0.78f,0.55f), 1.3f, false, "配辣椒生成火焰旋风"),
                            MkItem("potato", "土豆", ItemCategory.Cooking, 1, 1, 160,
                                new Color(0.72f,0.58f,0.35f), 1.3f, false, "配平底锅形成护盾"),
                            MkItem("pan", "平底锅", ItemCategory.Cooking, 2, 2, 400,
                                new Color(0.35f,0.35f,0.38f), 0.8f, false, "配土豆形成锅盖护盾，占位很大"),
                            MkItem("tomato", "番茄", ItemCategory.Cooking, 1, 1, 150,
                                new Color(0.92f,0.28f,0.25f), 1.4f, false, "配鸡蛋发射追踪番茄弹"),
                            MkItem("egg", "鸡蛋", ItemCategory.Cooking, 1, 1, 140,
                                new Color(0.96f,0.94f,0.86f), 1.4f, false, "配番茄发射追踪弹"),
                            MkItem("radish", "白萝卜", ItemCategory.Cooking, 1, 2, 260,
                                new Color(0.94f,0.94f,0.90f), 1.0f, false, "配菜刀召唤萝卜剑士"),
                            MkItem("cleaver", "菜刀", ItemCategory.Cooking, 2, 1, 240,
                                new Color(0.62f,0.66f,0.70f), 0.9f, false, "配萝卜召唤萝卜剑士"),
                            MkItem("truffle", "故障松露", ItemCategory.Cooking, 1, 1, 880,
                                new Color(0.55f,0.25f,0.65f), 0.15f, true, "★稀有。据说吃了能直接跳关"),
                        }
                    },
                    new ThemeItemPool
                    {
                        theme = RoomTheme.Police,
                        items = new List<ItemDefinition>
                        {
                            MkItem("handcuff", "手铐", ItemCategory.Police, 1, 1, 220,
                                new Color(0.78f,0.80f,0.84f), 1.4f, false, "抓捕时间 2秒→1秒"),
                            MkItem("scanner", "扫描器", ItemCategory.Police, 2, 1, 320,
                                new Color(0.30f,0.80f,0.85f), 1.1f, false, "扇形范围标记可疑NPC"),
                            MkItem("taser", "电击枪", ItemCategory.Police, 1, 2, 300,
                                new Color(0.95f,0.85f,0.25f), 1.0f, false, "使一名玩家麻痹1.5秒"),
                            MkItem("dog", "警犬", ItemCategory.Police, 2, 2, 450,
                                new Color(0.70f,0.52f,0.30f), 0.7f, false, "圆形范围自动锁定小偷。占位大但很强"),
                            MkItem("barrier", "路障", ItemCategory.Police, 2, 1, 180,
                                new Color(0.95f,0.60f,0.20f), 1.2f, false, "封锁出口，改变人群路径"),
                            MkItem("warrant", "空白逮捕令", ItemCategory.Police, 1, 1, 860,
                                new Color(1f,0.95f,0.85f), 0.16f, true, "★稀有。可以指定一名NPC直接判定为小偷"),
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
            string id, string name, ItemCategory cat, int w, int h, int value,
            Color col, float weight, bool rare, string effect)
        {
            string path = $"{ItemFolder}/Item_{id}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item != null) return item;

            item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = name;
            item.category = cat;
            item.gridWidth = w;
            item.gridHeight = h;
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
        //  房间
        // ══════════════════════════════════════════════

        static void BuildRoom(Transform root)
        {
            var g = NewChild(root, "Room");

            Cube(g, "Floor", new Vector3(0f, -0.2f, 0f),
                new Vector3(RoomW, 0.4f, RoomD), new Color(0.22f, 0.23f, 0.27f));

            // 天花板：FPS 里必须有，否则抬头是空的
            Cube(g, "Ceiling", new Vector3(0f, WallH + 0.15f, 0f),
                new Vector3(RoomW, 0.3f, RoomD), new Color(0.16f, 0.17f, 0.21f));

            var wallCol = new Color(0.31f, 0.33f, 0.39f);
            float hx = RoomW * 0.5f;
            float hz = RoomD * 0.5f;

            // 北墙留门洞
            float gap = 3.0f;
            float seg = (RoomW - gap) * 0.5f;
            Cube(g, "Wall_N_L", new Vector3(-(gap * 0.5f + seg * 0.5f), WallH * 0.5f, hz),
                new Vector3(seg, WallH, WallT), wallCol);
            Cube(g, "Wall_N_R", new Vector3(gap * 0.5f + seg * 0.5f, WallH * 0.5f, hz),
                new Vector3(seg, WallH, WallT), wallCol);
            Cube(g, "Wall_N_Top", new Vector3(0f, WallH - 0.4f, hz),
                new Vector3(gap, 0.8f, WallT), wallCol);

            Cube(g, "Wall_S", new Vector3(0f, WallH * 0.5f, -hz),
                new Vector3(RoomW, WallH, WallT), wallCol);
            Cube(g, "Wall_E", new Vector3(hx, WallH * 0.5f, 0f),
                new Vector3(WallT, WallH, RoomD), wallCol);
            Cube(g, "Wall_W", new Vector3(-hx, WallH * 0.5f, 0f),
                new Vector3(WallT, WallH, RoomD), wallCol);

            // 白板：漏数据流
            var board = Cube(g, "Whiteboard", new Vector3(0f, 1.9f, -hz + 0.25f),
                new Vector3(6.0f, 2.1f, 0.12f), new Color(0.84f, 0.89f, 0.92f));
            var ba = board.AddComponent<BugAmbience>();
            ba.bobAmplitude = 0.02f; ba.driftSpin = 0f;
            ba.glitchInterval = 2.2f; ba.glitchOffset = 0.05f;
        }

        /// <summary>
        /// ★内部隔断墙。这是 FPS 版最关键的关卡设计——
        /// 制造视线遮挡，让「听声辨位」和「绕后偷袭」成立。
        /// </summary>
        static void BuildInteriorWalls(Transform root)
        {
            var g = NewChild(root, "InteriorWalls");
            var col = new Color(0.27f, 0.29f, 0.35f);

            // 中央十字隔断，但留出通行缺口
            Cube(g, "Div_Center_A", new Vector3(-3.5f, WallH * 0.42f, 0f),
                new Vector3(0.3f, WallH * 0.85f, 7.0f), col);
            Cube(g, "Div_Center_B", new Vector3(3.5f, WallH * 0.42f, 0f),
                new Vector3(0.3f, WallH * 0.85f, 7.0f), col);

            // 横向半墙：只挡视线不挡通行（可以蹲着看过去，站着挡住）
            Cube(g, "Div_Half_N", new Vector3(0f, 0.75f, 4.2f),
                new Vector3(9.0f, 1.5f, 0.3f), col);
            Cube(g, "Div_Half_S", new Vector3(0f, 0.75f, -4.2f),
                new Vector3(9.0f, 1.5f, 0.3f), col);

            // 四角小隔间：藏容器的地方
            Cube(g, "Nook_NE", new Vector3(7.5f, WallH * 0.42f, 3.0f),
                new Vector3(0.3f, WallH * 0.85f, 5.0f), col);
            Cube(g, "Nook_NW", new Vector3(-7.5f, WallH * 0.42f, 3.0f),
                new Vector3(0.3f, WallH * 0.85f, 5.0f), col);
            Cube(g, "Nook_SE", new Vector3(7.5f, WallH * 0.42f, -3.0f),
                new Vector3(0.3f, WallH * 0.85f, 5.0f), col);
            Cube(g, "Nook_SW", new Vector3(-7.5f, WallH * 0.42f, -3.0f),
                new Vector3(0.3f, WallH * 0.85f, 5.0f), col);

            // 悬浮会议桌：装饰，无碰撞（玩家可穿过）
            var table = Cube(g, "Table_Floating", new Vector3(0f, 1.35f, 0f),
                new Vector3(4.5f, 0.16f, 1.8f), new Color(0.50f, 0.37f, 0.25f));
            KillCollider(table);
            var ta = table.AddComponent<BugAmbience>();
            ta.bobAmplitude = 0.16f; ta.bobSpeed = 0.8f; ta.driftSpin = 3f;
        }

        static GameObject BuildDoor(Transform root)
        {
            var pivot = NewChild(root, "DoorPivot");
            pivot.transform.localPosition = new Vector3(-1.5f, 0f, RoomD * 0.5f);

            Cube(pivot, "DoorLeaf", new Vector3(1.5f, 1.55f, 0f),
                new Vector3(3.0f, 3.1f, 0.16f), new Color(0.40f, 0.29f, 0.21f));

            var glow = Cube(pivot, "DoorGlow", new Vector3(1.5f, 1.55f, 0.10f),
                new Vector3(3.1f, 3.2f, 0.03f), new Color(0.25f, 0.65f, 1.0f));
            KillCollider(glow);

            return pivot;
        }

        static void BuildBugProps(Transform root)
        {
            var g = NewChild(root, "BugProps");

            // 天花板裂缝
            var crack = Cube(g, "CeilingCrack", new Vector3(0f, WallH - 0.08f, 0f),
                new Vector3(8.0f, 0.08f, 0.6f), new Color(0.25f, 0.70f, 1.0f));
            KillCollider(crack);
            var ca = crack.AddComponent<BugAmbience>();
            ca.bobAmplitude = 0.04f; ca.glitchInterval = 1.7f; ca.driftSpin = 0f;

            // 漂浮像素块
            for (int i = 0; i < 22; i++)
            {
                var px = Cube(g, "Pixel_" + (i + 1),
                    new Vector3(Random.Range(-10.5f, 10.5f), Random.Range(0.8f, 3.2f), Random.Range(-7.5f, 7.5f)),
                    Vector3.one * Random.Range(0.10f, 0.24f),
                    new Color(0.28f, Random.Range(0.55f, 0.9f), 1.0f));
                KillCollider(px);
                var a = px.AddComponent<BugAmbience>();
                a.bobAmplitude = Random.Range(0.18f, 0.45f);
                a.bobSpeed = Random.Range(0.6f, 1.9f);
                a.driftSpin = Random.Range(-70f, 70f);
                a.glitchInterval = Random.Range(1.2f, 4.5f);
            }

            // 悬浮椅子（装饰，无碰撞）
            for (int i = 0; i < 8; i++)
            {
                var ch = Cube(g, "Chair_" + (i + 1),
                    new Vector3(Random.Range(-9f, 9f), Random.Range(0.9f, 1.7f), Random.Range(-6.5f, 6.5f)),
                    new Vector3(0.55f, 0.55f, 0.55f), new Color(0.29f, 0.31f, 0.37f));
                ch.transform.localRotation = Quaternion.Euler(
                    Random.Range(-30f, 30f), Random.Range(0f, 360f), Random.Range(-30f, 30f));
                KillCollider(ch);
                var a = ch.AddComponent<BugAmbience>();
                a.bobAmplitude = Random.Range(0.10f, 0.24f);
                a.bobSpeed = Random.Range(0.7f, 1.5f);
                a.driftSpin = Random.Range(-16f, 16f);
            }
        }

        // ══════════════════════════════════════════════
        //  容器
        // ══════════════════════════════════════════════

        struct CSpec
        {
            public string name;
            public Vector3 pos;
            public Vector3 size;
            public Color color;
            public Vector3 anchorWorldOffset;
            public float rarity;
            public int lootOverride;
        }

        static List<LootContainer> BuildContainers(Transform root)
        {
            var g = NewChild(root, "Containers");
            var list = new List<LootContainer>();

            var specs = new[]
            {
                // 四角隔间：最值钱，但最深最危险
                new CSpec{ name="档案柜", pos=new Vector3(-10.2f,0.9f,6.2f), size=new Vector3(1.2f,1.8f,0.8f),
                    color=new Color(0.55f,0.42f,0.30f), anchorWorldOffset=new Vector3(1.0f,-0.2f,0f), rarity=0.9f, lootOverride=4 },
                new CSpec{ name="服务器机柜", pos=new Vector3(10.2f,1.0f,6.2f), size=new Vector3(1.0f,2.0f,0.9f),
                    color=new Color(0.24f,0.28f,0.34f), anchorWorldOffset=new Vector3(-1.0f,-0.3f,0f), rarity=1.0f, lootOverride=4 },
                new CSpec{ name="保险箱", pos=new Vector3(-10.2f,0.5f,-6.2f), size=new Vector3(0.9f,1.0f,0.8f),
                    color=new Color(0.36f,0.36f,0.40f), anchorWorldOffset=new Vector3(1.0f,0f,0f), rarity=1.4f, lootOverride=3 },
                new CSpec{ name="杂物堆", pos=new Vector3(10.2f,0.45f,-6.2f), size=new Vector3(1.4f,0.9f,1.2f),
                    color=new Color(0.42f,0.40f,0.32f), anchorWorldOffset=new Vector3(-1.1f,0f,0f), rarity=0.5f, lootOverride=4 },

                // 中层：正常收益
                new CSpec{ name="饮水机", pos=new Vector3(-5.5f,0.7f,7.6f), size=new Vector3(0.7f,1.4f,0.7f),
                    color=new Color(0.74f,0.82f,0.88f), anchorWorldOffset=new Vector3(0f,-0.2f,-1.0f), rarity=0.3f, lootOverride=2 },
                new CSpec{ name="投影仪箱", pos=new Vector3(5.5f,0.5f,7.6f), size=new Vector3(1.3f,1.0f,0.9f),
                    color=new Color(0.30f,0.33f,0.40f), anchorWorldOffset=new Vector3(0f,0f,-1.1f), rarity=0.4f, lootOverride=3 },
                new CSpec{ name="储物柜", pos=new Vector3(-5.5f,1.0f,-7.6f), size=new Vector3(0.9f,2.0f,0.8f),
                    color=new Color(0.34f,0.42f,0.48f), anchorWorldOffset=new Vector3(0f,-0.4f,1.0f), rarity=0.6f, lootOverride=3 },
                new CSpec{ name="打印机", pos=new Vector3(5.5f,0.55f,-7.6f), size=new Vector3(1.1f,1.1f,0.8f),
                    color=new Color(0.68f,0.68f,0.64f), anchorWorldOffset=new Vector3(0f,0f,1.0f), rarity=0.3f, lootOverride=2 },

                // 中央开阔区：容易拿但极其暴露
                new CSpec{ name="纸箱", pos=new Vector3(-1.6f,0.4f,1.4f), size=new Vector3(0.9f,0.8f,0.9f),
                    color=new Color(0.60f,0.48f,0.32f), anchorWorldOffset=new Vector3(0f,0f,1.0f), rarity=0.2f, lootOverride=2 },
                new CSpec{ name="垃圾桶", pos=new Vector3(1.6f,0.4f,-1.4f), size=new Vector3(0.75f,0.85f,0.75f),
                    color=new Color(0.28f,0.48f,0.34f), anchorWorldOffset=new Vector3(0f,0f,-1.0f), rarity=0.2f, lootOverride=2 },
            };

            for (int i = 0; i < specs.Length; i++)
            {
                var s = specs[i];
                var go = Cube(g, "Loot_" + s.name, s.pos, s.size, s.color);

                // 交互点用世界偏移换算成 localPosition（父级有非等比缩放）
                var anchor = new GameObject("InteractPoint");
                anchor.transform.SetParent(go.transform, false);
                anchor.transform.localPosition = new Vector3(
                    s.anchorWorldOffset.x / Mathf.Max(0.01f, s.size.x),
                    s.anchorWorldOffset.y / Mathf.Max(0.01f, s.size.y),
                    s.anchorWorldOffset.z / Mathf.Max(0.01f, s.size.z));

                var lc = go.AddComponent<LootContainer>();
                lc.containerName = s.name;
                lc.interactAnchor = anchor.transform;
                lc.rarityBonus = s.rarity;
                lc.lootCountOverride = s.lootOverride;
                lc.highlightRenderer = go.GetComponent<Renderer>();

                list.Add(lc);
            }
            return list;
        }

        // ══════════════════════════════════════════════
        //  撤离点
        // ══════════════════════════════════════════════

        static List<ExtractionZone> BuildExtractionZones(Transform root)
        {
            var g = NewChild(root, "ExtractionZones");
            var list = new List<ExtractionZone>();

            // 对角布置，制造「往哪边跑」的抉择与遭遇战
            var spots = new[]
            {
                new Vector3(0f, 0f, RoomD * 0.5f - 1.2f),   // 北门口
                new Vector3(0f, 0f, -RoomD * 0.5f + 1.6f),  // 南侧
            };
            var names = new[] { "主传送门", "备用传送门" };

            for (int i = 0; i < spots.Length; i++)
            {
                var go = NewChild(g, "ExtractZone_" + (i + 1));
                go.transform.localPosition = spots[i];

                // 地面光圈
                var visual = NewChild(go, "Visual");

                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Disc";
                disc.transform.SetParent(visual.transform, false);
                disc.transform.localScale = new Vector3(4.4f, 0.04f, 4.4f);
                disc.transform.localPosition = new Vector3(0f, 0.03f, 0f);
                Object.DestroyImmediate(disc.GetComponent<Collider>());
                var discRenderer = disc.GetComponent<Renderer>();
                SetColor(discRenderer, new Color(0.3f, 0.3f, 0.34f));

                // 竖直光柱，远处也能看到
                var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beam.name = "Beam";
                beam.transform.SetParent(visual.transform, false);
                beam.transform.localScale = new Vector3(0.5f, 1.6f, 0.5f);
                beam.transform.localPosition = new Vector3(0f, 1.6f, 0f);
                Object.DestroyImmediate(beam.GetComponent<Collider>());
                SetColor(beam.GetComponent<Renderer>(), new Color(0.3f, 0.3f, 0.34f));

                var z = go.AddComponent<ExtractionZone>();
                z.zoneName = names[i];
                z.radius = 2.2f;
                z.zoneRenderer = discRenderer;

                list.Add(z);
            }
            return list;
        }

        // ══════════════════════════════════════════════
        //  玩家
        // ══════════════════════════════════════════════

        static List<PlayerRig> BuildPlayers(Transform root)
        {
            var g = NewChild(root, "Players");
            var list = new List<PlayerRig>();

            // 四角出生，互相看不见（有隔断挡着）
            var spawns = new[]
            {
                new Vector3(-8.5f, 1.0f,  5.0f),
                new Vector3( 8.5f, 1.0f,  5.0f),
                new Vector3(-8.5f, 1.0f, -5.0f),
                new Vector3( 8.5f, 1.0f, -5.0f),
            };
            var colors = new[] { PlayerColor.Red, PlayerColor.Blue, PlayerColor.Yellow, PlayerColor.Green };
            float[] aggro = { 0f, 0.15f, -0.05f, -0.15f };

            for (int i = 0; i < 4; i++)
            {
                bool isLocal = (i == 0);

                var go = NewChild(g, (isLocal ? "LocalPlayer_" : "Bot_") + colors[i]);
                go.transform.localPosition = spawns[i];
                go.transform.localRotation = Quaternion.LookRotation(
                    new Vector3(-spawns[i].x, 0f, -spawns[i].z).normalized, Vector3.up);

                // CharacterController：FPS 标配
                var cc = go.AddComponent<CharacterController>();
                cc.height = 1.8f;
                cc.radius = 0.35f;
                cc.center = new Vector3(0f, 0.9f, 0f);
                cc.slopeLimit = 50f;
                cc.stepOffset = 0.35f;
                cc.skinWidth = 0.03f;

                // 身体视觉（第三人称可见，本地玩家会隐藏）
                var visual = NewChild(go, "Visual");

                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(visual.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.7f, 0.62f, 0.7f);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                var bodyRenderer = body.GetComponent<Renderer>();
                SetColor(bodyRenderer, colors[i].ToColor());

                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "Head";
                head.transform.SetParent(visual.transform, false);
                head.transform.localPosition = new Vector3(0f, 1.68f, 0f);
                head.transform.localScale = Vector3.one * 0.42f;
                Object.DestroyImmediate(head.GetComponent<Collider>());
                SetColor(head.GetComponent<Renderer>(), new Color(0.97f, 0.86f, 0.74f));

                // 朝向指示：从背后能看出他面朝哪，便于判断背刺时机
                var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
                nose.name = "Facing";
                nose.transform.SetParent(visual.transform, false);
                nose.transform.localPosition = new Vector3(0f, 1.68f, 0.26f);
                nose.transform.localScale = new Vector3(0.10f, 0.10f, 0.18f);
                Object.DestroyImmediate(nose.GetComponent<Collider>());
                SetColor(nose.GetComponent<Renderer>(), Color.white);

                // 眼部挂点
                var eye = NewChild(go, "EyeAnchor");
                eye.transform.localPosition = new Vector3(0f, 1.62f, 0f);

                // 组件
                go.AddComponent<GridInventory>();
                go.AddComponent<LootAction>();
                go.AddComponent<MeleeAction>();

                var rig = go.AddComponent<PlayerRig>();
                rig.playerColor = colors[i];
                rig.displayName = colors[i].ToLabel() + "方";
                rig.isLocalPlayer = isLocal;
                rig.eyeAnchor = eye.transform;
                rig.bodyRenderer = bodyRenderer;
                rig.visualRoot = visual;

                if (isLocal)
                {
                    // 相机作为眼部子物体，只负责 pitch
                    var camGo = Camera.main != null ? Camera.main.gameObject : null;
                    if (camGo == null)
                    {
                        camGo = new GameObject("Main Camera");
                        camGo.tag = "MainCamera";
                        camGo.AddComponent<Camera>();
                        camGo.AddComponent<AudioListener>();
                    }
                    camGo.transform.SetParent(eye.transform, false);
                    camGo.transform.localPosition = Vector3.zero;
                    camGo.transform.localRotation = Quaternion.identity;

                    var cam = camGo.GetComponent<Camera>();
                    cam.nearClipPlane = 0.05f;
                    cam.fieldOfView = 76f;
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.04f, 0.05f, 0.09f);

                    var look = go.AddComponent<FirstPersonLook>();
                    look.cameraTransform = camGo.transform;
                    look.cam = cam;
                    look.lockCursor = true;

                    go.AddComponent<HumanController>();
                }
                else
                {
                    var bot = go.AddComponent<BotController>();
                    bot.aggressionBias = aggro[i];
                    bot.noise = 0.15f;
                }

                list.Add(rig);
            }
            return list;
        }

        // ══════════════════════════════════════════════
        //  灯光与管理器
        // ══════════════════════════════════════════════

        static void BuildLights(Transform root)
        {
            var g = NewChild(root, "Lights");

            var sun = NewChild(g, "KeyLight");
            sun.transform.localRotation = Quaternion.Euler(48f, -30f, 0f);
            var sl = sun.AddComponent<Light>();
            sl.type = LightType.Directional;
            sl.intensity = 0.75f;
            sl.color = new Color(1f, 0.97f, 0.92f);
            sl.shadows = LightShadows.Soft;

            // 天花板灯：FPS 里需要局部光源制造明暗层次
            var spots = new[]
            {
                new Vector3(-7f, 3.2f,  5f), new Vector3( 7f, 3.2f,  5f),
                new Vector3(-7f, 3.2f, -5f), new Vector3( 7f, 3.2f, -5f),
                new Vector3( 0f, 3.2f,  0f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                var lp = NewChild(g, "CeilingLamp_" + (i + 1));
                lp.transform.localPosition = spots[i];
                var l = lp.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 11f;
                l.intensity = 1.25f;
                l.color = new Color(1f, 0.95f, 0.85f);
            }

            // 蓝色 Bug 补光
            var fill = NewChild(g, "BugFill");
            fill.transform.localPosition = new Vector3(0f, 2.6f, 0f);
            var fl = fill.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.range = 22f;
            fl.intensity = 0.8f;
            fl.color = new Color(0.32f, 0.58f, 1f);
        }

        static RaidManager BuildManager(
            Transform root, RaidConfig config,
            List<PlayerRig> players, List<LootContainer> containers,
            List<ExtractionZone> zones, GameObject door)
        {
            var go = NewChild(root, "RaidManager");

            var mgr = go.AddComponent<RaidManager>();
            mgr.config = config;
            mgr.theme = RoomTheme.Fishing;
            mgr.players = players;
            mgr.containers = containers;
            mgr.extractionZones = zones;
            mgr.doorPivot = door != null ? door.transform : null;
            mgr.verboseLog = true;
            mgr.allowRestartKey = true;

            go.AddComponent<RaidHUD>();
            return mgr;
        }

        // ══════════════════════════════════════════════
        //  工具方法（两种父级重载，避免 CS1503）
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
