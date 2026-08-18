using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BugParty.SearchRoom.EditorTools
{
    /// <summary>
    /// 一键搭建密室搜索场景。菜单：BugParty ▸ Build Search Room Scene
    ///
    /// 会自动生成：
    ///   · 房间几何体（地板、四面墙、天花板灯）
    ///   · 一扇会在开局关闭的门
    ///   · 6 个可搜索容器（抽屉、饮水机、天花板夹层、投影仪箱、文件柜、垃圾桶）
    ///   · 4 名玩家（默认 1 真人 + 3 AI），带阵营配色
    ///   · 斜俯视相机 + 灯光 + HUD
    ///   · 全部 ScriptableObject 配置资产（Config + 三主题共 16 个道具）
    ///
    /// 生成完成后直接按 Play 即可试玩，不需要手动拖任何引用。
    /// </summary>
    public static class SearchRoomSceneBuilder
    {
        const string ConfigFolder = "Assets/BugParty/Config";
        const string ItemFolder = "Assets/BugParty/Config/Items";
        const string ConfigPath = ConfigFolder + "/SearchRoomConfig.asset";

        // 房间尺寸
        const float RoomW = 16f;
        const float RoomD = 12f;
        const float WallH = 3.4f;
        const float WallT = 0.3f;

        [MenuItem("BugParty/Build Search Room Scene", false, 0)]
        public static void BuildScene()
        {
            if (!EditorUtility.DisplayDialog(
                "搭建密室搜索场景",
                "将在当前场景中生成完整的密室搜索环节（房间、容器、四名玩家、相机、HUD 与全部配置资产）。\n\n" +
                "建议在一个空场景中执行。是否继续？",
                "开始生成", "取消"))
                return;

            var config = EnsureConfigAssets();

            // 清掉上一次生成的根节点，支持反复重建
            var old = GameObject.Find("=== SearchRoom ===");
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject("=== SearchRoom ===");
            Undo.RegisterCreatedObjectUndo(root, "Build Search Room");

            BuildRoom(root.transform);
            var door = BuildDoor(root.transform);
            var containers = BuildContainers(root.transform);
            BuildBugProps(root.transform);
            var players = BuildPlayers(root.transform);
            BuildCameraAndLights(root.transform);
            var manager = BuildManager(root.transform, config, players, containers, door);

            Selection.activeGameObject = manager.gameObject;
            EditorSceneMarkDirty();

            Debug.Log(
                "[SearchRoom] 场景生成完成！\n" +
                "  · 按 Play 直接试玩\n" +
                "  · 操作：WASD 移动，J 按住搜索，K 肘击，R 重开一轮\n" +
                "  · 数值调整：" + ConfigPath + "\n" +
                "  · 切换主题：选中 SearchRoomManager，改 Theme 字段（Fishing / Cooking / Police）");
        }

        [MenuItem("BugParty/Create Config Assets Only", false, 20)]
        public static void CreateConfigOnly()
        {
            var cfg = EnsureConfigAssets();
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
            Debug.Log("[SearchRoom] 配置资产已就绪：" + ConfigPath);
        }

        // ══════════════════════════════════════════════════
        //  配置资产
        // ══════════════════════════════════════════════════

        static SearchRoomConfig EnsureConfigAssets()
        {
            EnsureFolder("Assets/BugParty");
            EnsureFolder(ConfigFolder);
            EnsureFolder(ItemFolder);

            var config = AssetDatabase.LoadAssetAtPath<SearchRoomConfig>(ConfigPath);
            bool isNew = config == null;
            if (isNew)
            {
                config = ScriptableObject.CreateInstance<SearchRoomConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            // 只在新建时填充道具池，避免覆盖策划已调过的数值
            if (isNew || config.itemPools == null || config.itemPools.Count == 0)
            {
                config.itemPools = new List<ThemeItemPool>
                {
                    new ThemeItemPool
                    {
                        theme = RoomTheme.Fishing,
                        items = new List<ItemDefinition>
                        {
                            MakeItem("net_large",  "大渔网", ItemCategory.Fishing,     new Color(0.35f,0.75f,0.95f), 1.0f, "一次捕两条鱼，读条5秒"),
                            MakeItem("net_small",  "小渔网", ItemCategory.Fishing,     new Color(0.55f,0.85f,0.95f), 1.4f, "一次捕一条鱼，读条5秒"),
                            MakeItem("gloves",     "徒手手套", ItemCategory.Fishing,   new Color(0.85f,0.85f,0.80f), 1.2f, "一次一条，读条8秒，最弱"),
                            MakeItem("knife",      "小刀",   ItemCategory.Destruction, new Color(0.90f,0.35f,0.30f), 0.9f, "贴身打断对手正在收的渔网，耐久3次"),
                            MakeItem("mine",       "水雷",   ItemCategory.Destruction, new Color(0.95f,0.55f,0.20f), 0.7f, "隐形地雷，触发者定身5秒，可被拾取复用"),
                        }
                    },
                    new ThemeItemPool
                    {
                        theme = RoomTheme.Cooking,
                        items = new List<ItemDefinition>
                        {
                            MakeItem("chili",    "辣椒",   ItemCategory.Cooking, new Color(0.90f,0.20f,0.18f), 1.2f, "配洋葱生成火焰旋风，持续击退"),
                            MakeItem("onion",    "洋葱",   ItemCategory.Cooking, new Color(0.88f,0.78f,0.55f), 1.2f, "配辣椒生成火焰旋风"),
                            MakeItem("potato",   "土豆",   ItemCategory.Cooking, new Color(0.72f,0.58f,0.35f), 1.2f, "配平底锅形成护盾并反弹攻击"),
                            MakeItem("pan",      "平底锅", ItemCategory.Cooking, new Color(0.35f,0.35f,0.38f), 0.8f, "配土豆形成锅盖护盾"),
                            MakeItem("tomato",   "番茄",   ItemCategory.Cooking, new Color(0.92f,0.28f,0.25f), 1.3f, "配鸡蛋发射追踪番茄弹"),
                            MakeItem("egg",      "鸡蛋",   ItemCategory.Cooking, new Color(0.96f,0.94f,0.86f), 1.3f, "配番茄发射追踪弹"),
                            MakeItem("radish",   "白萝卜", ItemCategory.Cooking, new Color(0.94f,0.94f,0.90f), 1.0f, "配菜刀召唤萝卜剑士"),
                            MakeItem("cleaver",  "菜刀",   ItemCategory.Cooking, new Color(0.62f,0.66f,0.70f), 0.8f, "配萝卜召唤萝卜剑士"),
                        }
                    },
                    new ThemeItemPool
                    {
                        theme = RoomTheme.Police,
                        items = new List<ItemDefinition>
                        {
                            MakeItem("handcuff", "手铐",   ItemCategory.Police, new Color(0.78f,0.80f,0.84f), 1.3f, "抓捕时间从2秒缩短到1秒"),
                            MakeItem("scanner",  "扫描器", ItemCategory.Police, new Color(0.30f,0.80f,0.85f), 1.1f, "扇形范围内短暂标记可疑NPC"),
                            MakeItem("taser",    "电击枪", ItemCategory.Police, new Color(0.95f,0.85f,0.25f), 0.9f, "使一名玩家麻痹1.5秒"),
                            MakeItem("dog",      "警犬",   ItemCategory.Police, new Color(0.70f,0.52f,0.30f), 0.8f, "圆形范围内自动锁定小偷"),
                            MakeItem("barrier",  "路障",   ItemCategory.Police, new Color(0.95f,0.60f,0.20f), 1.0f, "封锁出口，改变人群路径"),
                        }
                    }
                };
                EditorUtility.SetDirty(config);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return config;
        }

        static ItemDefinition MakeItem(
            string id, string name, ItemCategory cat, Color col, float weight, string effect)
        {
            string path = $"{ItemFolder}/Item_{id}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item != null) return item;

            item = ScriptableObject.CreateInstance<ItemDefinition>();
            item.itemId = id;
            item.displayName = name;
            item.category = cat;
            item.placeholderColor = col;
            item.spawnWeight = weight;
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

        // ══════════════════════════════════════════════════
        //  房间
        // ══════════════════════════════════════════════════

        static void BuildRoom(Transform root)
        {
            var group = NewChild(root, "Room");

            // 地板
            var floor = Cube(group, "Floor",
                new Vector3(0f, -0.15f, 0f),
                new Vector3(RoomW, 0.3f, RoomD),
                new Color(0.24f, 0.25f, 0.30f));
            floor.isStatic = true;

            // 地毯，让视觉中心明确
            Cube(group, "Carpet",
                new Vector3(0f, 0.01f, 0f),
                new Vector3(RoomW * 0.62f, 0.02f, RoomD * 0.62f),
                new Color(0.19f, 0.28f, 0.34f));

            var wallCol = new Color(0.33f, 0.35f, 0.42f);
            float hx = RoomW * 0.5f;
            float hz = RoomD * 0.5f;

            // 北墙留门洞，拆成两段
            float doorGap = 2.6f;
            float segW = (RoomW - doorGap) * 0.5f;
            Cube(group, "Wall_N_Left",
                new Vector3(-(doorGap * 0.5f + segW * 0.5f), WallH * 0.5f, hz),
                new Vector3(segW, WallH, WallT), wallCol);
            Cube(group, "Wall_N_Right",
                new Vector3(doorGap * 0.5f + segW * 0.5f, WallH * 0.5f, hz),
                new Vector3(segW, WallH, WallT), wallCol);
            // 门楣
            Cube(group, "Wall_N_Top",
                new Vector3(0f, WallH - 0.35f, hz),
                new Vector3(doorGap, 0.7f, WallT), wallCol);

            Cube(group, "Wall_S", new Vector3(0f, WallH * 0.5f, -hz),
                new Vector3(RoomW, WallH, WallT), wallCol);
            Cube(group, "Wall_E", new Vector3(hx, WallH * 0.5f, 0f),
                new Vector3(WallT, WallH, RoomD), wallCol);
            Cube(group, "Wall_W", new Vector3(-hx, WallH * 0.5f, 0f),
                new Vector3(WallT, WallH, RoomD), wallCol);

            // 白板（漏数据流的那面）
            var board = Cube(group, "Whiteboard",
                new Vector3(0f, 1.9f, -hz + 0.2f),
                new Vector3(5.2f, 2.0f, 0.12f),
                new Color(0.86f, 0.90f, 0.92f));
            var amb = board.AddComponent<BugAmbience>();
            amb.bobAmplitude = 0.03f;
            amb.driftSpin = 0f;
            amb.glitchInterval = 2.2f;
            amb.glitchOffset = 0.06f;

            // 悬浮的会议桌与椅子
            var table = Cube(group, "MeetingTable_Floating",
                new Vector3(0f, 1.15f, 0f),
                new Vector3(5.0f, 0.16f, 2.0f),
                new Color(0.52f, 0.38f, 0.26f));
            // 桌子浮在 1.15 米高，玩家应该能从下面穿过，去掉碰撞体
            var tableCol = table.GetComponent<Collider>();
            if (tableCol != null) Object.DestroyImmediate(tableCol);

            var tAmb = table.AddComponent<BugAmbience>();
            tAmb.bobAmplitude = 0.14f;
            tAmb.bobSpeed = 0.85f;
            tAmb.driftSpin = 3f;

            var chairPos = new[]
            {
                new Vector3(-1.7f, 0.95f,  1.9f), new Vector3(-0.55f, 1.25f, 2.1f),
                new Vector3( 0.6f, 1.05f,  2.0f), new Vector3( 1.75f, 1.35f, 1.85f),
                new Vector3(-1.5f, 1.10f, -1.9f), new Vector3( 1.4f,  0.90f, -2.0f)
            };
            for (int i = 0; i < chairPos.Length; i++)
            {
                var ch = Cube(group, "Chair_Floating_" + (i + 1), chairPos[i],
                    new Vector3(0.52f, 0.52f, 0.52f),
                    new Color(0.30f, 0.32f, 0.38f));
                ch.transform.localRotation = Quaternion.Euler(
                    Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f));

                // 悬浮的椅子是纯装饰，去掉碰撞体避免卡住玩家
                var chCol = ch.GetComponent<Collider>();
                if (chCol != null) Object.DestroyImmediate(chCol);

                var a = ch.AddComponent<BugAmbience>();
                a.bobAmplitude = Random.Range(0.10f, 0.22f);
                a.bobSpeed = Random.Range(0.7f, 1.5f);
                a.driftSpin = Random.Range(-14f, 14f);
                a.glitchInterval = Random.Range(2.5f, 5.5f);
            }
        }

        static GameObject BuildDoor(Transform root)
        {
            // 门枢轴放在门洞左侧，绕它旋转开合
            var pivot = NewChild(root, "DoorPivot");
            pivot.transform.localPosition = new Vector3(-1.3f, 0f, RoomD * 0.5f);

            var leaf = Cube(pivot.transform, "DoorLeaf",
                new Vector3(1.3f, 1.5f, 0f),
                new Vector3(2.6f, 3.0f, 0.14f),
                new Color(0.42f, 0.30f, 0.22f));

            // 门框光边，关门后亮起蓝光的位置
            Cube(pivot.transform, "DoorGlow",
                new Vector3(1.3f, 1.5f, 0.09f),
                new Vector3(2.7f, 3.1f, 0.03f),
                new Color(0.25f, 0.65f, 1.0f));

            return pivot;
        }

        static void BuildBugProps(Transform root)
        {
            var group = NewChild(root, "BugProps");

            // 天花板数据裂缝
            var crack = Cube(group, "CeilingCrack",
                new Vector3(0f, WallH - 0.05f, 0f),
                new Vector3(6.5f, 0.06f, 0.5f),
                new Color(0.25f, 0.70f, 1.0f));
            var crackCol = crack.GetComponent<Collider>();
            if (crackCol != null) Object.DestroyImmediate(crackCol);

            var ca = crack.AddComponent<BugAmbience>();
            ca.bobAmplitude = 0.05f;
            ca.glitchInterval = 1.8f;
            ca.driftSpin = 0f;

            // 漂浮的像素方块群
            for (int i = 0; i < 14; i++)
            {
                var px = Cube(group, "PixelChunk_" + (i + 1),
                    new Vector3(Random.Range(-6.5f, 6.5f), Random.Range(0.6f, 3.0f), Random.Range(-4.8f, 4.8f)),
                    Vector3.one * Random.Range(0.09f, 0.22f),
                    new Color(0.28f, Random.Range(0.55f, 0.85f), 1.0f));
                // 去掉碰撞体，避免干扰玩家移动
                var col = px.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);

                var a = px.AddComponent<BugAmbience>();
                a.bobAmplitude = Random.Range(0.15f, 0.4f);
                a.bobSpeed = Random.Range(0.6f, 1.8f);
                a.driftSpin = Random.Range(-60f, 60f);
                a.glitchInterval = Random.Range(1.2f, 4f);
            }
        }

        // ══════════════════════════════════════════════════
        //  可搜容器
        // ══════════════════════════════════════════════════

        struct ContainerSpec
        {
            public string name;
            public Vector3 pos;
            public Vector3 size;
            public Color color;
            public Vector3 interactOffset;
        }

        static List<SearchContainer> BuildContainers(Transform root)
        {
            var group = NewChild(root, "Containers");
            var result = new List<SearchContainer>();

            var specs = new[]
            {
                new ContainerSpec { name = "文件柜抽屉", pos = new Vector3(-6.2f, 0.75f,  3.6f),
                    size = new Vector3(1.1f, 1.5f, 0.7f), color = new Color(0.55f,0.42f,0.30f),
                    interactOffset = new Vector3(0.9f, -0.3f, 0f) },

                new ContainerSpec { name = "饮水机", pos = new Vector3( 6.3f, 0.65f,  3.9f),
                    size = new Vector3(0.7f, 1.3f, 0.7f), color = new Color(0.75f,0.82f,0.88f),
                    interactOffset = new Vector3(-0.9f, -0.2f, 0f) },

                new ContainerSpec { name = "天花板夹层", pos = new Vector3(-4.0f, 2.55f, -3.8f),
                    size = new Vector3(1.6f, 0.35f, 1.2f), color = new Color(0.62f,0.64f,0.60f),
                    interactOffset = new Vector3(0f, -2.0f, 0.9f) },

                new ContainerSpec { name = "投影仪箱", pos = new Vector3( 4.4f, 0.45f, -4.0f),
                    size = new Vector3(1.3f, 0.9f, 0.9f), color = new Color(0.30f,0.33f,0.40f),
                    interactOffset = new Vector3(0f, 0f, 1.0f) },

                new ContainerSpec { name = "储物柜", pos = new Vector3(-6.4f, 1.0f, -2.2f),
                    size = new Vector3(0.9f, 2.0f, 0.9f), color = new Color(0.36f,0.44f,0.50f),
                    interactOffset = new Vector3(1.0f, -0.6f, 0f) },

                new ContainerSpec { name = "垃圾桶", pos = new Vector3( 6.4f, 0.42f, -1.6f),
                    size = new Vector3(0.75f, 0.85f, 0.75f), color = new Color(0.30f,0.50f,0.35f),
                    interactOffset = new Vector3(-0.95f, 0f, 0f) },
            };

            for (int i = 0; i < specs.Length; i++)
            {
                var s = specs[i];
                var go = Cube(group, "Container_" + s.name, s.pos, s.size, s.color);

                var anchor = new GameObject("InteractPoint");
                anchor.transform.SetParent(go.transform, false);
                anchor.transform.localPosition = new Vector3(
                    s.interactOffset.x / Mathf.Max(0.01f, s.size.x),
                    s.interactOffset.y / Mathf.Max(0.01f, s.size.y),
                    s.interactOffset.z / Mathf.Max(0.01f, s.size.z));

                var sc = go.AddComponent<SearchContainer>();
                sc.containerName = s.name;
                sc.interactAnchor = anchor.transform;
                sc.remainingYield = 2;
                sc.highlightRenderer = go.GetComponent<Renderer>();

                // 容器也轻微漂浮，强化 Bug 感。
                // 注意：关闭 glitch 变色，否则会和 SearchContainer 的枯竭变灰互相覆盖
                var a = go.AddComponent<BugAmbience>();
                a.bobAmplitude = 0.05f;
                a.bobSpeed = Random.Range(0.6f, 1.1f);
                a.driftSpin = 0f;
                a.enableGlitch = false;

                result.Add(sc);
            }
            return result;
        }

        // ══════════════════════════════════════════════════
        //  玩家
        // ══════════════════════════════════════════════════

        static List<PlayerActor> BuildPlayers(Transform root)
        {
            var group = NewChild(root, "Players");
            var result = new List<PlayerActor>();

            var spawns = new[]
            {
                new Vector3(-2.4f, 0.9f,  4.2f),
                new Vector3( 2.4f, 0.9f,  4.2f),
                new Vector3(-2.4f, 0.9f, -4.2f),
                new Vector3( 2.4f, 0.9f, -4.2f),
            };
            var colors = new[] { PlayerColor.Red, PlayerColor.Blue, PlayerColor.Yellow, PlayerColor.Green };

            // 红方给真人，其余三个给 AI
            var schemes = new[]
            {
                InputScheme.Player1(), InputScheme.Player2(),
                InputScheme.Player3(), InputScheme.Player4()
            };
            float[] aggression = { 0.20f, 0.10f, -0.10f, -0.18f };

            for (int i = 0; i < 4; i++)
            {
                var go = NewChild(group.transform, "Player_" + colors[i]);
                go.transform.localPosition = spawns[i];
                go.transform.localRotation = Quaternion.LookRotation(
                    new Vector3(-spawns[i].x, 0f, -spawns[i].z).normalized, Vector3.up);

                // 碰撞体
                var cap = go.AddComponent<CapsuleCollider>();
                cap.height = 1.7f;
                cap.radius = 0.36f;
                cap.center = new Vector3(0f, 0.05f, 0f);

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 1.2f;
                rb.drag = 6f;
                rb.constraints = RigidbodyConstraints.FreezeRotation;

                // 视觉体
                var visual = NewChild(go.transform, "Visual");

                var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.transform.SetParent(visual.transform, false);
                body.transform.localScale = new Vector3(0.72f, 0.62f, 0.72f);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                var bodyRenderer = body.GetComponent<Renderer>();
                SetColor(bodyRenderer, colors[i].ToColor());

                // 头
                var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                head.name = "Head";
                head.transform.SetParent(visual.transform, false);
                head.transform.localPosition = new Vector3(0f, 0.82f, 0f);
                head.transform.localScale = Vector3.one * 0.62f;
                Object.DestroyImmediate(head.GetComponent<Collider>());
                SetColor(head.GetComponent<Renderer>(), new Color(0.98f, 0.87f, 0.75f));

                // 朝向指示（鼻子），方便判断肘击方向
                var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
                nose.name = "FacingIndicator";
                nose.transform.SetParent(visual.transform, false);
                nose.transform.localPosition = new Vector3(0f, 0.82f, 0.34f);
                nose.transform.localScale = new Vector3(0.12f, 0.12f, 0.22f);
                Object.DestroyImmediate(nose.GetComponent<Collider>());
                SetColor(nose.GetComponent<Renderer>(), Color.white);

                // 挂点
                var hand = NewChild(go.transform, "HandAnchor");
                hand.transform.localPosition = new Vector3(0.34f, 0.35f, 0.28f);

                var elbow = NewChild(go.transform, "ElbowOrigin");
                elbow.transform.localPosition = new Vector3(0f, 0.35f, 0.1f);

                // 组件
                go.AddComponent<PlayerInventory>();
                go.AddComponent<SearchAbility>();
                go.AddComponent<ElbowAbility>();

                var actor = go.AddComponent<PlayerActor>();
                actor.playerColor = colors[i];
                actor.displayName = colors[i].ToLabel() + "方";
                actor.bodyRenderer = bodyRenderer;
                actor.handAnchor = hand.transform;
                actor.elbowOrigin = elbow.transform;

                if (i == 0)
                {
                    var human = go.AddComponent<HumanBrain>();
                    human.keys = schemes[i];
                    human.cameraRelative = true;
                }
                else
                {
                    var ai = go.AddComponent<AIBrain>();
                    ai.aggressionBias = aggression[i];
                    ai.noise = 0.15f;
                }

                result.Add(actor);
            }
            return result;
        }

        // ══════════════════════════════════════════════════
        //  相机、灯光、HUD、管理器
        // ══════════════════════════════════════════════════

        static Transform BuildCameraAndLights(Transform root)
        {
            var center = NewChild(root, "RoomCenter");
            center.transform.localPosition = new Vector3(0f, 0.6f, 0f);

            // 相机：复用场景已有的 Main Camera，没有就新建
            var camGo = Camera.main != null ? Camera.main.gameObject : null;
            if (camGo == null)
            {
                camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 42f;
                camGo.AddComponent<AudioListener>();
            }
            camGo.transform.SetParent(root, true);

            var rig = camGo.GetComponent<SearchRoomCamera>();
            if (rig == null) rig = camGo.AddComponent<SearchRoomCamera>();
            rig.lookTarget = center.transform;
            rig.pitch = 52f;
            rig.yaw = 0f;
            rig.distance = 13.5f;
            rig.autoFrame = true;
            rig.minDistance = 11f;
            rig.maxDistance = 18f;

            var camera = camGo.GetComponent<Camera>();
            if (camera != null)
            {
                camera.backgroundColor = new Color(0.05f, 0.06f, 0.10f);
                camera.clearFlags = CameraClearFlags.SolidColor;
            }

            // 灯光
            var lights = NewChild(root, "Lights");

            var sun = new GameObject("KeyLight");
            sun.transform.SetParent(lights.transform, false);
            sun.transform.localPosition = new Vector3(0f, 6f, 0f);
            sun.transform.localRotation = Quaternion.Euler(52f, -28f, 0f);
            var sl = sun.AddComponent<Light>();
            sl.type = LightType.Directional;
            sl.intensity = 1.05f;
            sl.color = new Color(1f, 0.97f, 0.92f);
            sl.shadows = LightShadows.Soft;

            // 蓝色补光，呼应 Bug 世界的冷色调
            var fill = new GameObject("BugFillLight");
            fill.transform.SetParent(lights.transform, false);
            fill.transform.localPosition = new Vector3(0f, 3.0f, 0f);
            var fl = fill.AddComponent<Light>();
            fl.type = LightType.Point;
            fl.range = 16f;
            fl.intensity = 1.3f;
            fl.color = new Color(0.35f, 0.62f, 1f);

            return center.transform;
        }

        static SearchRoomManager BuildManager(
            Transform root, SearchRoomConfig config,
            List<PlayerActor> players, List<SearchContainer> containers,
            GameObject door)
        {
            var go = NewChild(root, "SearchRoomManager");

            var mgr = go.AddComponent<SearchRoomManager>();
            mgr.config = config;
            mgr.theme = RoomTheme.Fishing;
            mgr.players = players;
            mgr.containers = containers;
            mgr.doorPivot = door != null ? door.transform : null;
            mgr.verboseLog = true;
            mgr.allowRestartKey = true;

            go.AddComponent<SearchRoomHUD>();
            return mgr;
        }

        // ══════════════════════════════════════════════════
        //  工具方法
        // ══════════════════════════════════════════════════

        static GameObject NewChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>接受 GameObject 作为父级的重载，省去到处写 .transform。</summary>
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

        /// <summary>接受 GameObject 作为父级的重载。</summary>
        static GameObject Cube(GameObject parent, string name, Vector3 pos, Vector3 size, Color color)
            => Cube(parent != null ? parent.transform : null, name, pos, size, color);

        static void SetColor(Renderer r, Color c)
        {
            if (r == null) return;
            // 用 sharedMaterial 的副本，避免编辑器下产生大量泄漏材质
            var mat = new Material(r.sharedMaterial);
            mat.name = "Mat_" + r.gameObject.name;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            r.sharedMaterial = mat;
        }

        static void EditorSceneMarkDirty()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
