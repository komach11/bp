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
    /// 【本版解决的两个问题】
    ///
    /// ① T-pose —— 动画播不上去
    ///   模型骨骼：Polydactyl_Metarig / hips / spine / chest …（65 骨，无前缀）
    ///   动画骨骼：PolyAnim / root / ORG-hips / ORG-spine …（506 节点，Rigify 装配）
    ///   Generic rig 靠「transform 路径名」绑定，路径对不上 → 动画在播但驱动不了骨骼。
    ///   ★关键发现：把 ORG- 前缀去掉后，78 根骨骼与模型 100% 同名。
    ///   所以改用 Humanoid rig —— Humanoid 通过 Avatar 做「骨骼语义映射」，
    ///   不依赖路径名，正好绕过前缀问题。Unity 的 Avatar 自动映射能识别
    ///   hips/spine/chest/upper_arm/forearm/thigh/shin 这类标准命名。
    ///
    /// ② 悬空 —— 角色浮在地面上方 0.725 米
    ///   ArtResolver 把「渲染包围盒底面」贴到 y=0，而 SkinnedMeshRenderer.bounds
    ///   是 T-pose 下的静态 AABB，min.y 算出来是 -0.680（脚骨其实在 +0.847）。
    ///   为了把 -0.680 抬到 0，整个模型被 +0.680，叠加缩放后净抬升 0.725。
    ///   ★改法：Prefab 生成时就按「脚部骨骼」把模型对齐到 y=0，
    ///   并把 ArtSlot.fitToSize 关掉、改用固定 scaleMul —— 不再让包围盒参与定位。
    ///
    /// 【产出】
    ///   Assets/BugParty2D/Art/Characters/Materials/     4 个材质
    ///   Assets/BugParty2D/Art/Characters/CharacterAnimSet.asset   ★动作映射表
    ///   Assets/BugParty2D/Art/Characters/PlayerAnim.controller
    ///   Assets/BugParty2D/Art/Characters/Char_*.prefab            4 个角色
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
        const string AnimSetPath = OutDir + "/CharacterAnimSet.asset";

        /// <summary>四个玩家的配置：模型 + 纹理。顺序对应 红/蓝/黄/绿。</summary>
        static readonly (string model, string tex, string label)[] Roster =
        {
            ("Bear.fbx", "Tex_Bear_A_Suit_A",   "Red_Bear_Suit"),
            ("Bear.fbx", "Tex_Bear_A_Thief",    "Blue_Bear_Thief"),
            ("Bear.fbx", "Tex_Bear_B_Casual_E", "Yellow_Bear_Casual"),
            ("Cat.fbx",  "Tex_Cat_B_Casual_B",  "Green_Cat_Casual"),
        };

        // ══════════════════════════════════════════════
        //  菜单入口
        // ══════════════════════════════════════════════

        [MenuItem("BugParty2D/接入 SCP 角色（完整：模型+动画+纹理）", false, 2060)]
        [MenuItem("Tools/BugParty2D/接入 SCP 角色（完整：模型+动画+纹理）", false, 2060)]
        public static void RunFull()
        {
            if (!EditorUtility.DisplayDialog(
                "接入 SCP 角色",
                "将执行：\n" +
                "① 把 8 个 fbx 的 rig 统一为 Generic 并重新导入\n" +
                "② 重绑定动画曲线路径（剥掉 Rigify 的 ORG- 前缀，映射到模型骨架）\n" +
                "③ 生成动作映射表 CharacterAnimSet（闲置/搜索/跑步/肘击 槽位可换）\n" +
                "④ 生成 4 个材质 + 共用 AnimatorController\n" +
                "⑤ 生成 4 个角色 Prefab，按蒙皮网格最低点对齐地面\n" +
                "⑥ 写入 RoomArtConfig.characters[0..3]\n\n" +
                "步骤 ①② 会重新导入并生成 .anim，需要一两分钟。\n" +
                "完成后需重新执行 Build Room Scene。是否继续？",
                "开始", "取消"))
                return;

            Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();

            // ① rig 统一为 Generic
            if (!PrepareRigs()) return;

            // ② ★重绑定动画路径 —— 这是 T-pose 的正解
            //    实测 Humanoid 自动映射对 SCP 完全失败（humanDescription 里 0 条映射，
            //    Avatar 是空壳），所以不再依赖它，直接改写曲线路径。
            var rebound = SCPAnimRebind.RebindAll(out string rebindReport);
            Debug.Log("[SCP] " + rebindReport);

            // ③ 动作映射表：优先用重绑定后的 clip
            var clips = rebound != null && rebound.Count > 0 ? rebound : CollectClips();
            var set = EnsureAnimSet(clips);

            // ④ Controller
            var controller = BuildController(set);

            // ⑤ 角色 Prefab
            var prefabs = new GameObject[4];
            for (int i = 0; i < 4; i++) prefabs[i] = BuildCharacterPrefab(i, controller);

            // ⑥ 写配置
            WriteToConfig(prefabs);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int ok = 0;
            for (int i = 0; i < 4; i++) if (prefabs[i] != null) ok++;

            EditorUtility.DisplayDialog("完成",
                $"已生成 {ok}/4 个角色 Prefab。\n" +
                $"重绑定动画：{(rebound != null ? rebound.Count : 0)} 个\n" +
                $"动作映射表已填 {set.FilledCount}/9 个槽位。\n\n" +
                "下一步：BugParty2D ▸ Build Room Scene\n\n" +
                "想换某个动作：打开 CharacterAnimSet 换 clip，\n" +
                "再执行「BugParty2D ▸ 重建角色动画」即可（不用重新建场）。", "好");

            Selection.activeObject = set;
            EditorGUIUtility.PingObject(set);
        }

        [MenuItem("BugParty2D/重建角色动画（改完动作表后用）", false, 2061)]
        [MenuItem("Tools/BugParty2D/重建角色动画（改完动作表后用）", false, 2061)]
        public static void RebuildAnimOnly()
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimSet>(AnimSetPath);
            if (set == null)
            {
                EditorUtility.DisplayDialog("找不到动作表",
                    "请先执行「接入 SCP 角色（完整）」生成 CharacterAnimSet。", "好");
                return;
            }
            if (!set.HasAny)
            {
                EditorUtility.DisplayDialog("动作表是空的",
                    "CharacterAnimSet 里一个 clip 都没填，无法生成状态机。", "好");
                return;
            }

            BuildController(set);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("完成",
                $"AnimatorController 已按动作表重建（{set.FilledCount}/9 个槽位）。\n\n" +
                "Prefab 引用的是同一个 Controller，所以不需要重新建场 —— " +
                "直接 Play 就能看到新动作。", "好");
        }

        // ══════════════════════════════════════════════
        //  ① rig 设置
        // ══════════════════════════════════════════════

        /// <summary>
        /// 把模型与动画 fbx 全部改成 Humanoid rig。
        ///
        /// ★这是修 T-pose 的关键一步。原因：
        /// Generic rig 按 transform 路径名绑定动画曲线。模型的骨骼叫 hips，
        /// 动画里叫 ORG-hips，路径 "Polydactyl_Metarig/hips" 与
        /// "PolyAnim/root/ORG-hips" 完全不同 → 曲线找不到目标。
        ///
        /// Humanoid 则是通过 Avatar 把骨骼映射到「人体语义槽位」（Hips/Spine/Chest/
        /// LeftUpperArm…），动画和模型各自映射到同一套语义，就能互通 —— 名字不必相同。
        /// Unity 的自动映射（avatarSetup = CreateFromThisModel）能识别
        /// hips/spine/chest/upper_arm/forearm/thigh/shin 这类常见命名，SCP 正好符合。
        /// </summary>
        static bool PrepareRigs()
        {
            var paths = new List<string>();

            // 模型
            foreach (var (model, _, _) in Roster)
            {
                string p = ModelDir + "/" + model;
                if (!paths.Contains(p)) paths.Add(p);
            }
            // 动画
            if (Directory.Exists(AnimDir))
            {
                foreach (var raw in Directory.GetFiles(AnimDir, "*.fbx", SearchOption.TopDirectoryOnly))
                    paths.Add(raw.Replace('\\', '/'));
            }

            int changed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int i = 0; i < paths.Count; i++)
                {
                    var imp = AssetImporter.GetAtPath(paths[i]) as ModelImporter;
                    if (imp == null)
                    {
                        Debug.LogWarning($"[SCP] 不是模型资源，跳过：{paths[i]}");
                        continue;
                    }

                    bool dirty = false;

                    // ★Generic，不是 Humanoid。
                    //   实测 Humanoid 对 SCP 的自动映射完全失败：重新导入后
                    //   humanDescription 里 human 映射 0 条、skeleton 0 条，
                    //   生成的 Avatar 是空壳 —— Animator 拿到空 Avatar 只能播 root motion，
                    //   角色停在导入姿态（T-pose）。原因是熊/猫是拟人角色，
                    //   骨骼比例不符合人体，Unity 认不出标准人体槽位。
                    //   改用 Generic + 曲线路径重绑定（SCPAnimRebind），完全可控。
                    if (imp.animationType != ModelImporterAnimationType.Generic)
                    {
                        imp.animationType = ModelImporterAnimationType.Generic;
                        dirty = true;
                    }

                    // ★Generic 必须有 Avatar 才能播动画。CreateFromThisModel 在 Generic 下
                    //   只是记录骨架层级，不做人体语义映射，一定成功
                    if (imp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
                    {
                        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                        dirty = true;
                    }

                    // ★动画 fbx 必须导入动画，模型 fbx 不需要（少一份冗余 clip）
                    bool isAnim = paths[i].StartsWith(AnimDir);
                    if (imp.importAnimation != isAnim)
                    {
                        imp.importAnimation = isAnim;
                        dirty = true;
                    }

                    // ★关掉 optimizeGameObjects：开启后骨骼节点会被折叠进 Avatar，
                    //   重绑定需要遍历真实 transform 层级来建路径表
                    if (imp.optimizeGameObjects)
                    {
                        imp.optimizeGameObjects = false;
                        dirty = true;
                    }

                    if (dirty)
                    {
                        EditorUtility.SetDirty(imp);
                        imp.SaveAndReimport();
                        changed++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[SCP] rig 已统一为 Generic（{changed}/{paths.Count} 个 fbx 被重新导入）");

            // 校验：模型的 Avatar 是否生成（Generic 下应当总是成功）
            foreach (var (model, _, _) in Roster)
            {
                string p = ModelDir + "/" + model;
                if (FindAvatar(p) == null)
                    Debug.LogWarning($"[SCP] {model} 没有生成 Avatar，动画可能不正常");
            }
            return true;
        }

        /// <summary>取 fbx 生成的 Avatar 子资产。</summary>
        static Avatar FindAvatar(string fbxPath)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
                if (o is Avatar a) return a;
            return null;
        }

        // ══════════════════════════════════════════════
        //  ② 动作映射表
        // ══════════════════════════════════════════════

        /// <summary>
        /// 读取 6 个动画 fbx 里的全部 AnimationClip，按「去掉 PolyAnim| 前缀的名字」建索引。
        /// </summary>
        static Dictionary<string, AnimationClip> CollectClips()
        {
            var map = new Dictionary<string, AnimationClip>();
            if (!Directory.Exists(AnimDir)) return map;

            foreach (var raw in Directory.GetFiles(AnimDir, "*.fbx", SearchOption.TopDirectoryOnly))
            {
                string path = raw.Replace('\\', '/');
                foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var clip = obj as AnimationClip;
                    if (clip == null) continue;
                    if (clip.name.StartsWith("__preview__")) continue;

                    string key = clip.name;
                    int bar = key.LastIndexOf('|');
                    if (bar >= 0) key = key.Substring(bar + 1);

                    if (!map.ContainsKey(key)) map[key] = clip;
                }
            }
            Debug.Log($"[SCP] 读到 {map.Count} 个 AnimationClip");
            return map;
        }

        static AnimationClip Pick(Dictionary<string, AnimationClip> m, params string[] names)
        {
            foreach (var n in names)
                if (m.TryGetValue(n, out var c) && c != null) return c;
            return null;
        }

        /// <summary>
        /// 创建或更新动作映射表。已存在的槽位不覆盖 —— 用户手改过的 clip 要保留。
        /// </summary>
        static CharacterAnimSet EnsureAnimSet(Dictionary<string, AnimationClip> m)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimSet>(AnimSetPath);
            bool created = false;
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<CharacterAnimSet>();
                AssetDatabase.CreateAsset(set, AnimSetPath);
                created = true;
            }

            // 只填空槽位，不动用户手改过的
            if (set.idle == null) set.idle = Pick(m, "Idle_A", "Idle_B", "Idle_C", "Idle_D");
            if (set.walk == null) set.walk = Pick(m, "Walk_Forward", "Walk_Forward_Slow");
            if (set.run == null) set.run = Pick(m, "Run_Forward", "Run_Forward_Fast");
            // SCP 没有翻箱动作，Fight_Idle 是半蹲戒备姿态，最接近「专注地在做什么」
            if (set.search == null) set.search = Pick(m, "Fight_Idle", "Idle_B", "Idle_A");
            if (set.elbow == null) set.elbow = Pick(m, "Fight_Punch_Right", "Fight_Punch_Left", "Fight_Punch_A");
            if (set.jump == null) set.jump = Pick(m, "Jump_A", "Jump_B", "Run_Jump");
            if (set.fall == null) set.fall = Pick(m, "Falling", "Jump_B");
            if (set.getHit == null) set.getHit = Pick(m, "Fight_Hit_A", "Fight_Hit_B", "Fight_Hit_C");
            if (set.stagger == null) set.stagger = Pick(m, "Fight_Hit_B", "Fight_Hit_C", "Fight_Hit_A");

            EditorUtility.SetDirty(set);
            Debug.Log($"[SCP] 动作映射表{(created ? "已创建" : "已更新")}：{AnimSetPath}（{set.FilledCount}/9 槽位）");
            return set;
        }

        // ══════════════════════════════════════════════
        //  ③ Animator Controller
        // ══════════════════════════════════════════════

        /// <summary>
        /// 按动作表生成状态机。参数名与 PlayerAnimatorBridge 的常量严格一致。
        ///
        ///   Locomotion（Blend Tree: idle→walk→run，按 Speed）
        ///     ├─ Jump      Trigger Jump
        ///     ├─ Fall      Grounded=false 且下落中
        ///     ├─ Search    Searching=true
        ///     ├─ Elbow     Trigger Elbow
        ///     ├─ GetHit    Trigger GetHit
        ///     ├─ Stagger   Staggered=true
        ///     └─ Pitfall   Trigger Pitfall
        /// </summary>
        static AnimatorController BuildController(CharacterAnimSet set)
        {
            var old = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (old != null) AssetDatabase.DeleteAsset(ControllerPath);

            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

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

            // ★Grounded 默认 true —— 否则出生瞬间 Fall 的条件成立，角色一开场就播下落。
            //   注意 ctrl.parameters 返回数组副本，必须整体赋回才生效。
            var ps = ctrl.parameters;
            for (int i = 0; i < ps.Length; i++)
                if (ps[i].name == "Grounded") ps[i].defaultBool = true;
            ctrl.parameters = ps;

            var sm = ctrl.layers[0].stateMachine;

            // ── Locomotion ──
            // ★阈值取自动作表，是归一化值（0~1）不是 m/s：
            //   PlayerAnimatorBridge 的 speedNormalizeMax 默认 6，
            //   会把 HorizontalSpeed（moveSpeed 5.5）压到 0~0.92 再输出。
            AnimatorState loco;
            if (set.walk != null || set.run != null)
            {
                BlendTree tree;
                loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
                tree.blendParameter = "Speed";
                tree.blendType = BlendTreeType.Simple1D;
                tree.useAutomaticThresholds = false;
                if (set.idle != null) tree.AddChild(set.idle, 0f);
                if (set.walk != null) tree.AddChild(set.walk, set.walkThreshold);
                if (set.run != null) tree.AddChild(set.run, set.runThreshold);
            }
            else
            {
                loco = sm.AddState("Locomotion");
                loco.motion = set.idle;
            }
            sm.defaultState = loco;

            // ── 跳跃 / 下落 ──
            AnimatorState jump = null, fall = null;
            if (set.jump != null)
            {
                jump = sm.AddState("Jump");
                jump.motion = set.jump;
                var t = loco.AddTransition(jump);
                t.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
                t.hasExitTime = false;
                t.duration = 0.05f;
            }
            if (set.fall != null)
            {
                fall = sm.AddState("Fall");
                fall.motion = set.fall;

                var t1 = sm.AddAnyStateTransition(fall);
                t1.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
                t1.AddCondition(AnimatorConditionMode.Less, -0.6f, "VerticalSpeed");
                t1.hasExitTime = false;
                t1.duration = 0.1f;
                t1.canTransitionToSelf = false;

                var t2 = fall.AddTransition(loco);
                t2.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");
                t2.hasExitTime = false;
                t2.duration = 0.1f;
            }
            if (jump != null)
            {
                var back = jump.AddTransition(fall != null ? fall : loco);
                back.hasExitTime = true;
                back.exitTime = 0.85f;
                back.duration = 0.1f;
            }

            // ── 搜索 ──
            if (set.search != null)
            {
                var search = sm.AddState("Search");
                search.motion = set.search;

                var t1 = sm.AddAnyStateTransition(search);
                t1.AddCondition(AnimatorConditionMode.If, 0f, "Searching");
                t1.hasExitTime = false;
                t1.duration = set.searchBlend;
                t1.canTransitionToSelf = false;

                var t2 = search.AddTransition(loco);
                t2.AddCondition(AnimatorConditionMode.IfNot, 0f, "Searching");
                t2.hasExitTime = false;
                t2.duration = set.searchBlend;
            }

            // ── 肘击 ──
            if (set.elbow != null)
            {
                var elbow = sm.AddState("Elbow");
                elbow.motion = set.elbow;

                var t1 = sm.AddAnyStateTransition(elbow);
                t1.AddCondition(AnimatorConditionMode.If, 0f, "Elbow");
                t1.hasExitTime = false;
                t1.duration = set.elbowBlendIn;
                // 允许打自己 —— 连续挥肘时要能重新触发
                t1.canTransitionToSelf = true;

                var t2 = elbow.AddTransition(loco);
                t2.hasExitTime = true;
                t2.exitTime = set.elbowExitTime;
                t2.duration = 0.1f;
            }

            // ── 被击中 ──
            if (set.getHit != null)
            {
                var hit = sm.AddState("GetHit");
                hit.motion = set.getHit;

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

            // ── 硬直 ──
            if (set.stagger != null)
            {
                var stag = sm.AddState("Stagger");
                stag.motion = set.stagger;

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

            // ── 踩空：复用下落动画 ──
            if (set.fall != null)
            {
                var pit = sm.AddState("Pitfall");
                pit.motion = set.fall;

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
            Debug.Log($"[SCP] AnimatorController 已生成：{sm.states.Length} 个状态");
            return ctrl;
        }

        // ══════════════════════════════════════════════
        //  ④ 材质 + Prefab
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

            var mat = EnsureMaterial(texName, label);
            if (mat == null) return null;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (inst == null)
            {
                Debug.LogError($"[SCP] 实例化失败：{modelPath}");
                return null;
            }
            PrefabUtility.UnpackPrefabInstance(
                inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            inst.name = "Char_" + label;

            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int k = 0; k < mats.Length; k++) mats[k] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                // ★SkinnedMeshRenderer 必须开 updateWhenOffscreen，
                //   否则骨骼动画大幅位移时会被静态 AABB 错误剔除（角色突然消失）
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }

            foreach (var c in inst.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);

            // ── Animator ──
            var anim = inst.GetComponent<Animator>();
            if (anim == null) anim = inst.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;

            // ★Avatar 必须显式指定。Generic rig 的 Animator 没有 Avatar 时
            //   只能播 root motion，骨骼曲线全部被忽略 → 角色停在 T-pose。
            //   这是上一版 T-pose 的直接原因之一（m_Avatar: {fileID: 0}）。
            var avatar = FindAvatar(modelPath);
            if (avatar != null) anim.avatar = avatar;
            else Debug.LogError($"[SCP] {modelName} 没有 Avatar，动画将无法播放");

            anim.applyRootMotion = false;   // 位移由 CharacterController 驱动
            anim.updateMode = AnimatorUpdateMode.Normal;
            // ★不能用 CullUpdateTransforms —— 那会在离屏时停止更新 transform，
            //   而我们的落地判断依赖骨骼位置。AlwaysAnimate 对 4 个角色没有性能压力
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // ── ★按网格最低点把模型对齐到 y=0（修悬空）──
            AlignFeetToOrigin(inst);

            string prefabPath = OutDir + "/Char_" + label + ".prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
            Object.DestroyImmediate(inst);

            Debug.Log($"[SCP] {label} → {prefabPath}");
            return saved;
        }

        static Material EnsureMaterial(string texName, string label)
        {
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
                var sh = PipelineMat.Lit;
                mat = sh != null ? new Material(sh) : new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// 把模型整体平移，让「网格最低点」落在 y=0。
        ///
        /// ★为什么不用 Renderer.bounds：
        /// SkinnedMeshRenderer.bounds 是导入时预计算的静态 AABB，SCP 的模型
        /// 根节点带 scale=100 的补偿，实测 bounds.min.y = -0.0074（几乎是 0），
        /// 完全反映不了真实网格范围 —— 用它对齐必然错。
        ///
        /// ★为什么不用脚骨位置：
        /// Rigify 骨架的 foot/toe/heel 带旋转，骨骼 pivot 未必在脚底表面，
        /// 而且实测 toe(1.0153) 竟然比 foot(0.8398) 高 —— 骨骼链方向不是竖直的。
        ///
        /// 唯一可靠的是蒙皮网格顶点本身：用 BakeMesh 取 T-pose 下的实际顶点，
        /// 变换到 root 局部空间求最低 y。这就是真正的脚底。
        /// </summary>
        static void AlignFeetToOrigin(GameObject root)
        {
            float lowest = float.MaxValue;
            float highest = float.MinValue;
            int sampled = 0;

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;

                var baked = new Mesh();
                try
                {
                    // BakeMesh 输出当前姿态下的顶点，坐标在 smr 自身空间
                    smr.BakeMesh(baked, true);
                    var verts = baked.vertices;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var wp = smr.transform.TransformPoint(verts[i]);
                        float y = root.transform.InverseTransformPoint(wp).y;
                        if (y < lowest) lowest = y;
                        if (y > highest) highest = y;
                        sampled++;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(baked);
                }
            }

            // 没有蒙皮网格就退回普通 MeshRenderer 的顶点
            if (sampled == 0)
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var verts = mf.sharedMesh.vertices;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var wp = mf.transform.TransformPoint(verts[i]);
                        float y = root.transform.InverseTransformPoint(wp).y;
                        if (y < lowest) lowest = y;
                        if (y > highest) highest = y;
                        sampled++;
                    }
                }
            }

            if (sampled == 0)
            {
                Debug.LogWarning($"[SCP] {root.name} 找不到任何网格，无法对齐地面");
                return;
            }

            // 记录实际身高，供 ComputeScale 用（写进临时组件读不到，改为返回值太侵入，
            // 所以 ComputeScale 也用同一套 BakeMesh 逻辑）
            float shift = -lowest;
            if (Mathf.Abs(shift) > 1e-5f)
            {
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    var c = root.transform.GetChild(i);
                    c.localPosition += new Vector3(0f, shift, 0f);
                }
            }

            Debug.Log($"[SCP] {root.name} 网格 y 范围 [{lowest:F4}, {highest:F4}]" +
                      $"（{sampled} 顶点）→ 上移 {shift:F4}，高度 {highest - lowest:F4}");
        }

        /// <summary>
        /// 取模型在自身局部空间的实际网格高度（BakeMesh 顶点范围）。
        /// AlignFeetToOrigin 与 ComputeScale 共用，避免两处算法不一致。
        /// </summary>
        static bool TryMeshExtentY(GameObject root, out float minY, out float maxY)
        {
            minY = float.MaxValue; maxY = float.MinValue;
            int n = 0;

            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                var baked = new Mesh();
                try
                {
                    smr.BakeMesh(baked, true);
                    var verts = baked.vertices;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var wp = smr.transform.TransformPoint(verts[i]);
                        float y = root.transform.InverseTransformPoint(wp).y;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        n++;
                    }
                }
                finally { Object.DestroyImmediate(baked); }
            }

            if (n == 0)
            {
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var verts = mf.sharedMesh.vertices;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        var wp = mf.transform.TransformPoint(verts[i]);
                        float y = root.transform.InverseTransformPoint(wp).y;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                        n++;
                    }
                }
            }
            return n > 0;
        }

        // ══════════════════════════════════════════════
        //  ⑤ 写入配置
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
                Debug.LogError("[SCP] 找不到 RoomArtConfig2D.asset。" +
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
                slot.yawOffset = 0f;
                slot.yOffset = 0f;

                // ★关掉 fitToSize，改用固定缩放。
                //   fitToSize 会用渲染包围盒重新定位模型，把我们在 AlignFeetToOrigin
                //   里做好的接地对齐给覆盖掉 —— 那正是悬空的成因。
                //   Prefab 里模型已经是「脚在 y=0」的正确状态，直接等比缩放即可。
                slot.fitToSize = false;
                slot.scaleMul = ComputeScale(prefabs[i], art.characterHeight);
            }

            EditorUtility.SetDirty(art);
            Debug.Log("[SCP] 已写入 RoomArtConfig.characters[0..3]");
        }

        /// <summary>
        /// 按实际网格高度算缩放，使角色高度等于 characterHeight。
        ///
        /// ★用 BakeMesh 顶点，不用骨骼位置也不用 Renderer.bounds：
        /// - 骨骼：head_end 在骨骼链末端，位置可能远超网格顶部（实测 1.916 而
        ///   head 只有 1.117），拿它当身高会把角色缩得过小
        /// - Renderer.bounds：SCP 根节点带 scale=100 补偿，静态 AABB 完全失真
        ///
        /// 这个方法必须在 AlignFeetToOrigin 之后调用（此时脚底已在 y=0）。
        /// </summary>
        static float ComputeScale(GameObject prefab, float targetHeight)
        {
            if (prefab == null || targetHeight <= 0.01f) return 1f;

            // Prefab 资产不能直接 BakeMesh（需要实例），临时实例化一份
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (inst == null) return 1f;

            float h;
            try
            {
                if (!TryMeshExtentY(inst, out float minY, out float maxY))
                {
                    Debug.LogWarning($"[SCP] {prefab.name} 取不到网格范围，缩放取 1");
                    return 1f;
                }
                h = maxY - minY;
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }

            if (h < 0.01f)
            {
                Debug.LogWarning($"[SCP] {prefab.name} 网格高度异常（{h:F4}），缩放取 1");
                return 1f;
            }

            float scale = targetHeight / h;
            Debug.Log($"[SCP] {prefab.name} 网格身高 {h:F4} → 缩放 {scale:F4}（目标 {targetHeight}）");
            return scale;
        }
    }
}
