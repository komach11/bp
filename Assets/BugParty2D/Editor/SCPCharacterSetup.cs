using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BugParty.TopDown2D.EditorTools
{
    /// <summary>
    /// 把 Simple Character Pack（SCP）的模型 + 动画 + 纹理，一键组装成四个可用的
    /// 玩家角色 Prefab，并写进 RoomArtConfig，替换掉建场时的占位胶囊体。
    ///
    /// 【为什么需要这个工具】
    /// 手工做要重复 4 遍：建材质 → 拖纹理 → 建 AnimatorController → 从 6 个 fbx 里
    /// 找出 20 多个 clip 连状态机 → 做 Prefab → 填配置。而且 SCP 的动画 clip 名带
    /// PolyAnim| 前缀、分散在 Fight/Idle/Jump/Run/Turn/Walk 六个 fbx 里，手点极易漏。
    ///
    /// 【产出】
    ///   Assets/BugParty2D/Art/Characters/Materials/  4 个材质（各带一张纹理）
    ///   Assets/BugParty2D/Art/Characters/            PlayerAnim.controller（共用）
    ///   Assets/BugParty2D/Art/Characters/            4 个角色 Prefab
    ///   RoomArtConfig.characters[0..3] 自动指向这四个 Prefab
    ///
    /// 【之后】
    /// 重新执行 BugParty2D ▸ Build Room Scene，占位方块就换成带动画的角色。
    /// PlayerAnimatorBridge 由建场工具自动挂载（它检测到 Animator 就会挂）。
    /// </summary>
    public static class SCPCharacterSetup
    {
        // ── 资源路径 ──
        const string ModelDir = "Assets/art/SCP Models (.FBX)/Simple Character Pack Models (.FBX)";
        const string TexDir = "Assets/art/SCP Textures (.png)";
        const string AnimDir = "Assets/art/SCP Animations (.fbx)/Animation";

        const string OutDir = "Assets/BugParty2D/Art/Characters";
        const string MatDir = OutDir + "/Materials";
        const string ControllerPath = OutDir + "/PlayerAnim.controller";

        /// <summary>四个玩家的配置：模型 + 纹理。顺序对应 红/蓝/黄/绿。</summary>
        static readonly (string model, string tex, string label)[] Roster =
        {
            ("Bear.fbx", "Tex_Bear_A_Suit_A",   "Red_Bear_Suit"),
            ("Bear.fbx", "Tex_Bear_A_Thief",    "Blue_Bear_Thief"),
            ("Bear.fbx", "Tex_Bear_B_Casual_E", "Yellow_Bear_Casual"),
            ("Cat.fbx",  "Tex_Cat_B_Casual_B",  "Green_Cat_Casual"),
        };

        [MenuItem("BugParty2D/接入 SCP 角色（模型+动画+纹理）", false, 2060)]
        [MenuItem("Tools/BugParty2D/接入 SCP 角色（模型+动画+纹理）", false, 2060)]
        public static void Run()
        {
            if (!EditorUtility.DisplayDialog(
                "接入 SCP 角色",
                "将执行：\n" +
                "· 把 6 个动画 fbx 的 rig 统一为 Generic，并按 fbx 内的 take 切分 clip\n" +
                "· 生成 4 个材质，分别贴上指定纹理\n" +
                "· 生成共用的 PlayerAnim.controller（Idle/Walk/Run/Jump/Fall/Search/Elbow/Hit/Stagger）\n" +
                "· 生成 4 个角色 Prefab（Bear×3 + Cat×1）\n" +
                "· 写入 RoomArtConfig.characters[0..3]\n\n" +
                "完成后需重新执行 Build Room Scene。是否继续？",
                "开始", "取消"))
                return;

            Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();

            // ① 动画 fbx：确保 rig=Generic 且 clip 已切分
            var clips = PrepareAnimations();
            if (clips.Count == 0)
            {
                EditorUtility.DisplayDialog("失败",
                    "没能从动画 fbx 里读到任何 AnimationClip。\n" +
                    "请确认 " + AnimDir + " 下的 6 个 fbx 已被 Unity 导入。", "好");
                return;
            }
            Debug.Log($"[SCP] 读到 {clips.Count} 个 AnimationClip");

            // ② Animator Controller（四个角色共用一份）
            var controller = BuildController(clips);

            // ③ 逐个角色：材质 + Prefab
            var prefabs = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                prefabs[i] = BuildCharacterPrefab(i, controller);
            }

            // ④ 写入 RoomArtConfig
            WriteToConfig(prefabs);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int ok = 0;
            for (int i = 0; i < 4; i++) if (prefabs[i] != null) ok++;

            EditorUtility.DisplayDialog("完成",
                $"已生成 {ok}/4 个角色 Prefab，并写入 RoomArtConfig。\n\n" +
                "下一步：BugParty2D ▸ Build Room Scene\n" +
                "占位方块会被替换成带动画的角色。", "好");
        }

        // ══════════════════════════════════════════════
        //  ① 动画
        // ══════════════════════════════════════════════

        /// <summary>
        /// 读取 6 个动画 fbx 里的全部 AnimationClip。
        ///
        /// SCP 的 clip 名形如 "PolyAnim|Run_Forward" —— 竖线前是导出工具的命名空间。
        /// 这里按「去掉前缀后的名字」建索引，方便后面用 Run_Forward 这种直观名查找。
        /// </summary>
        static Dictionary<string, AnimationClip> PrepareAnimations()
        {
            var map = new Dictionary<string, AnimationClip>();

            var files = Directory.Exists(AnimDir)
                ? Directory.GetFiles(AnimDir, "*.fbx", SearchOption.TopDirectoryOnly)
                : new string[0];

            foreach (var raw in files)
            {
                string path = raw.Replace('\\', '/');

                // rig 必须是 Generic，否则 clip 无法被 Generic 模型复用
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp != null && imp.animationType != ModelImporterAnimationType.Generic)
                {
                    imp.animationType = ModelImporterAnimationType.Generic;
                    imp.SaveAndReimport();
                }

                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var clip = obj as AnimationClip;
                    if (clip == null) continue;
                    // Unity 会给每个 fbx 附一个名为 __preview__ 的隐藏 clip，跳过
                    if (clip.name.StartsWith("__preview__")) continue;

                    string key = clip.name;
                    int bar = key.LastIndexOf('|');
                    if (bar >= 0) key = key.Substring(bar + 1);

                    if (!map.ContainsKey(key)) map[key] = clip;
                }
            }
            return map;
        }

        /// <summary>按优先级找第一个存在的 clip。SCP 的命名有些变体（Run_Left3/Walk_RightQ 之类）。</summary>
        static AnimationClip Pick(Dictionary<string, AnimationClip> m, params string[] names)
        {
            foreach (var n in names)
                if (m.TryGetValue(n, out var c) && c != null) return c;
            return null;
        }

        // ══════════════════════════════════════════════
        //  ② Animator Controller
        // ══════════════════════════════════════════════

        /// <summary>
        /// 建一个与 PlayerAnimatorBridge 参数完全对应的状态机。
        ///
        /// 结构（刻意做扁平，便于后续在 Inspector 里手改）：
        ///   Locomotion（Blend Tree: Idle→Walk→Run，按 Speed）
        ///     ├─ Jump      Trigger Jump
        ///     ├─ Fall      Grounded=false
        ///     ├─ Search    Searching=true（循环）
        ///     ├─ Elbow     Trigger Elbow
        ///     ├─ GetHit    Trigger GetHit
        ///     └─ Stagger   Staggered=true
        /// </summary>
        static AnimatorController BuildController(Dictionary<string, AnimationClip> m)
        {
            // 已存在就重建，保证参数与 clip 是最新的
            var old = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (old != null) AssetDatabase.DeleteAsset(ControllerPath);

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            // ── 参数：名字必须与 PlayerAnimatorBridge 里的常量一致 ──
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("VerticalSpeed", AnimatorControllerParameterType.Float);
            ctrl.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Searching", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Staggered", AnimatorControllerParameterType.Bool);
            ctrl.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Land", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Elbow", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("GetHit", AnimatorControllerParameterType.Trigger);
            ctrl.AddParameter("Pitfall", AnimatorControllerParameterType.Trigger);

            // ★Grounded 默认 true —— 否则一开场 Fall 的 AnyState 条件立刻成立，
            //   角色会在出生瞬间播下落动画。
            //   注意 ctrl.parameters 返回的是数组副本，必须整体赋回才生效。
            var ps = ctrl.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == "Grounded") ps[i].defaultBool = true;
            ctrl.parameters = ps;

            var sm = ctrl.layers[0].stateMachine;

            var idle = Pick(m, "Idle_A", "Idle_B", "Idle_C");
            var walk = Pick(m, "Walk_Forward");
            var run = Pick(m, "Run_Forward");

            // ── Locomotion：Blend Tree ──
            // ★阈值用 0~1 归一化值，不是真实 m/s。
            //   PlayerAnimatorBridge 默认 speedNormalizeMax = 6，会把
            //   HorizontalSpeed（moveSpeed = 5.5 m/s）压到 0~0.92 再喂给 Speed。
            //   所以这里按 0 / 0.35 / 0.85 分档：慢速走、接近满速跑。
            AnimatorState loco;
            if (walk != null || run != null)
            {
                BlendTree tree;
                loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
                tree.blendParameter = "Speed";
                tree.blendType = BlendTreeType.Simple1D;
                tree.useAutomaticThresholds = false;
                if (idle != null) tree.AddChild(idle, 0f);
                if (walk != null) tree.AddChild(walk, 0.35f);
                if (run != null) tree.AddChild(run, 0.85f);
            }
            else
            {
                loco = sm.AddState("Locomotion");
                loco.motion = idle;
            }
            sm.defaultState = loco;

            // ── 跳跃 / 下落 ──
            var jumpClip = Pick(m, "Jump_A", "Jump_B");
            var fallClip = Pick(m, "Falling");
            var landClip = Pick(m, "Falling_to_Landing");

            AnimatorState jump = null, fall = null;
            if (jumpClip != null)
            {
                jump = sm.AddState("Jump");
                jump.motion = jumpClip;
                var t = loco.AddTransition(jump);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
                t.hasExitTime = false;
                t.duration = 0.05f;
            }
            if (fallClip != null)
            {
                fall = sm.AddState("Fall");
                fall.motion = fallClip;

                var t1 = sm.AddAnyStateTransition(fall);
                t1.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                t1.AddCondition(AnimatorConditionMode.Less, -0.6f, "VerticalSpeed");
                t1.hasExitTime = false;
                t1.duration = 0.1f;
                t1.canTransitionToSelf = false;

                var t2 = fall.AddTransition(loco);
                t2.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                t2.hasExitTime = false;
                t2.duration = landClip != null ? 0.12f : 0.08f;
            }
            if (jump != null)
            {
                var back = jump.AddTransition(fall != null ? fall : loco);
                back.hasExitTime = true;
                back.exitTime = 0.85f;
                back.duration = 0.1f;
            }

            // ── 搜索：Bool 驱动，持续状态 ──
            // SCP 没有专门的"翻箱"动作，用 Fight_Idle 代替 —— 那是半蹲戒备姿态，
            // 配合 PlayerActionFx 的程序化弯腰叠加，观感上接近在柜子里掏东西
            var searchClip = Pick(m, "Fight_Idle", "Idle_B", "Idle_A");
            if (searchClip != null)
            {
                var search = sm.AddState("Search");
                search.motion = searchClip;

                var t1 = sm.AddAnyStateTransition(search);
                t1.AddCondition(AnimatorConditionMode.If, 0f, "Searching");
                t1.hasExitTime = false;
                t1.duration = 0.12f;
                t1.canTransitionToSelf = false;

                var t2 = search.AddTransition(loco);
                t2.AddCondition(AnimatorConditionMode.IfNot, 0f, "Searching");
                t2.hasExitTime = false;
                t2.duration = 0.12f;
            }

            // ── 肘击 ──
            var elbowClip = Pick(m, "Fight_Punch_Right", "Fight_Punch_Left");
            if (elbowClip != null)
            {
                var elbow = sm.AddState("Elbow");
                elbow.motion = elbowClip;

                var t1 = sm.AddAnyStateTransition(elbow);
                t1.AddCondition(AnimatorConditionMode.If, 0f, "Elbow");
                t1.hasExitTime = false;
                t1.duration = 0.04f;
                t1.canTransitionToSelf = true;

                var t2 = elbow.AddTransition(loco);
                t2.hasExitTime = true;
                t2.exitTime = 0.8f;
                t2.duration = 0.1f;
            }

            // ── 被击中 ──
            var hitClip = Pick(m, "Fight_Hit_A", "Fight_Hit_B", "Fight_Hit_C");
            if (hitClip != null)
            {
                var hit = sm.AddState("GetHit");
                hit.motion = hitClip;

                var t1 = sm.AddAnyStateTransition(hit);
                t1.AddCondition(AnimatorConditionMode.If, 0f, "GetHit");
                t1.hasExitTime = false;
                t1.duration = 0.04f;
                t1.canTransitionToSelf = true;

                var t2 = hit.AddTransition(loco);
                t2.hasExitTime = true;
                t2.exitTime = 0.75f;
                t2.duration = 0.12f;
            }

            // ── 硬直：Staggered 为真时停在受击姿态，避免立刻切回跑动 ──
            var stagClip = Pick(m, "Fight_Hit_B", "Fight_Hit_A");
            if (stagClip != null)
            {
                var stag = sm.AddState("Stagger");
                stag.motion = stagClip;

                var t1 = sm.AddAnyStateTransition(stag);
                t1.AddCondition(AnimatorConditionMode.If, 0f, "Staggered");
                t1.hasExitTime = false;
                t1.duration = 0.06f;
                t1.canTransitionToSelf = false;

                var t2 = stag.AddTransition(loco);
                t2.AddCondition(AnimatorConditionMode.IfNot, 0f, "Staggered");
                t2.hasExitTime = false;
                t2.duration = 0.15f;
            }

            // ── 踩空：复用 Falling ──
            if (fallClip != null)
            {
                var pit = sm.AddState("Pitfall");
                pit.motion = fallClip;

                var t1 = sm.AddAnyStateTransition(pit);
                t1.AddCondition(AnimatorConditionMode.If, 0f, "Pitfall");
                t1.hasExitTime = false;
                t1.duration = 0.05f;
                t1.canTransitionToSelf = true;

                var t2 = pit.AddTransition(loco);
                t2.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                t2.hasExitTime = false;
                t2.duration = 0.15f;
            }

            EditorUtility.SetDirty(ctrl);
            Debug.Log($"[SCP] AnimatorController 已生成：{ControllerPath}（{sm.states.Length} 个状态）");
            return ctrl;
        }

        // ══════════════════════════════════════════════
        //  ③ 材质 + Prefab
        // ══════════════════════════════════════════════

        static GameObject BuildCharacterPrefab(int index, AnimatorController controller)
        {
            var (modelName, texName, label) = Roster[index];

            string modelPath = ModelDir + "/" + modelName;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null)
            {
                Debug.LogError($"[SCP] 找不到模型：{modelPath}");
                return null;
            }

            // ── 材质 ──
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexDir + "/" + texName + ".png");
            if (tex == null)
            {
                Debug.LogError($"[SCP] 找不到纹理：{TexDir}/{texName}.png");
                return null;
            }

            string matPath = MatDir + "/Mat_" + label + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                // 走 PipelineMat 的同一套判断：Built-in 下是 Standard，URP 下是 URP/Lit
                var sh = PipelineMat.Lit;
                mat = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            // 主贴图槽在两个管线下名字不同，都设一遍
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            // SCP 是 low-poly 风格，关掉高光更接近原始观感
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);

            // ── 实例化模型并组装 ──
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (inst == null)
            {
                Debug.LogError($"[SCP] 实例化失败：{modelPath}");
                return null;
            }
            // 断开与 fbx 的 prefab 连接，才能改材质与加组件
            PrefabUtility.UnpackPrefabInstance(
                inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            inst.name = "Char_" + label;

            // 全部 Renderer 换成我们的材质
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int k = 0; k < mats.Length; k++) mats[k] = mat;
                r.sharedMaterials = mats;
                // 2D 俯视用不到实时阴影，关掉省性能；落地阴影由建场的 Quad 负责
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            // 模型不参与碰撞（碰撞由玩家根节点的 CharacterController 负责）
            foreach (var c in inst.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);

            // Animator：fbx 导入时通常已有一个，没有就加
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;          // ★位移由 CharacterController 驱动
            anim.updateMode = AnimatorUpdateMode.Normal;
            anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // ── 存成 Prefab ──
            string prefabPath = OutDir + "/Char_" + label + ".prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Object.DestroyImmediate(inst);

            Debug.Log($"[SCP] {label} → {prefabPath}");
            return saved;
        }

        // ══════════════════════════════════════════════
        //  ④ 写入配置
        // ══════════════════════════════════════════════

        static void WriteToConfig(GameObject[] prefabs)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<RoomConfig>(
                "Assets/BugParty2D/Config/RoomConfig2D.asset");
            var art = cfg != null ? cfg.art : null;
            if (art == null)
            {
                art = AssetDatabase.LoadAssetAtPath<RoomArtConfig>(
                    "Assets/BugParty2D/Config/RoomArtConfig2D.asset");
            }
            if (art == null)
            {
                Debug.LogError("[SCP] 找不到 RoomArtConfig2D.asset，无法写入角色配置。" +
                               "请先执行 BugParty2D ▸ Create Config Assets Only。");
                return;
            }

            if (art.characters == null || art.characters.Length < 4)
                art.characters = new[] { new ArtSlot(), new ArtSlot(), new ArtSlot(), new ArtSlot() };

            for (int i = 0; i < 4; i++)
            {
                if (prefabs[i] == null) continue;
                var slot = art.characters[i] ?? (art.characters[i] = new ArtSlot());
                slot.prefab = prefabs[i];
                slot.scaleMul = 1f;
                slot.yawOffset = 0f;
                slot.yOffset = 0f;
                // ★SCP 模型不是米制单位，必须让 ArtResolver 按包围盒缩放到 characterHeight
                slot.fitToSize = true;
            }

            EditorUtility.SetDirty(art);
            Debug.Log("[SCP] 已写入 RoomArtConfig.characters[0..3]");
        }
    }
}
