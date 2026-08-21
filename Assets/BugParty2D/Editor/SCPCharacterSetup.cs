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
                "① 把模型与动画 fbx 的 rig 统一改为 Humanoid（关键：绕过骨骼前缀不匹配）\n" +
                "② 生成动作映射表 CharacterAnimSet（闲置/搜索/跑步/肘击 等槽位可在 Inspector 换）\n" +
                "③ 生成 4 个材质 + 共用 AnimatorController\n" +
                "④ 生成 4 个角色 Prefab，按脚部骨骼对齐到地面\n" +
                "⑤ 写入 RoomArtConfig.characters[0..3]\n\n" +
                "步骤 ① 会重新导入 8 个 fbx，可能需要一两分钟。\n" +
                "完成后需重新执行 Build Room Scene。是否继续？",
                "开始", "取消"))
                return;

            Directory.CreateDirectory(MatDir);
            AssetDatabase.Refresh();

            // ① rig 统一为 Humanoid
            if (!PrepareRigs()) return;

            // ② 动作映射表
            var clips = CollectClips();
            var set = EnsureAnimSet(clips);

            // ③ Controller
            var controller = BuildController(set);

            // ④ 角色 Prefab
            var prefabs = new GameObject[4];
            for (int i = 0; i < 4; i++) prefabs[i] = BuildCharacterPrefab(i, controller);

            // ⑤ 写配置
            WriteToConfig(prefabs);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int ok = 0;
            for (int i = 0; i < 4; i++) if (prefabs[i] != null) ok++;

            EditorUtility.DisplayDialog("完成",
                $"已生成 {ok}/4 个角色 Prefab。\n" +
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

                    if (imp.animationType != ModelImporterAnimationType.Human)
                    {
                        imp.animationType = ModelImporterAnimationType.Human;
                        // CreateFromThisModel = 让 Unity 用自动映射生成 Avatar
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
                    //   我们需要遍历 foot/heel 骨骼来做落地对齐
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

            Debug.Log($"[SCP] rig 已统一为 Humanoid（{changed}/{paths.Count} 个 fbx 被重新导入）");

            // 校验：模型的 Avatar 是否真的生成了
            foreach (var (model, _, _) in Roster)
            {
                string p = ModelDir + "/" + model;
                var av = FindAvatar(p);
                if (av == null)
                {
                    EditorUtility.DisplayDialog("Avatar 生成失败",
                        $"{model} 没能生成 Humanoid Avatar。\n\n" +
                        "说明 Unity 的自动骨骼映射没认出这套骨架。\n" +
                        "需要手工配置：选中 fbx ▸ Rig ▸ Configure，\n" +
                        "把 Hips/Spine/Chest/Head 与四肢逐个指到对应骨骼。", "好");
                    return false;
                }
                if (!av.isValid)
                {
                    Debug.LogWarning($"[SCP] {model} 的 Avatar 存在但 isValid=false，动画可能仍不正常");
                }
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

            // ★Avatar 必须显式指定。Humanoid rig 的动画靠 Avatar 做骨骼语义映射，
            //   avatar 为空时 Animator 会退化成「只播 root motion」→ 角色停在 T-pose。
            //   这正是上一版 T-pose 的直接原因（m_Avatar: {fileID: 0}）。
            var avatar = FindAvatar(modelPath);
            if (avatar != null) anim.avatar = avatar;
            else Debug.LogError($"[SCP] {modelName} 没有 Avatar，动画将无法播放");

            anim.applyRootMotion = false;   // 位移由 CharacterController 驱动
            anim.updateMode = AnimatorUpdateMode.Normal;
            // ★不能用 CullUpdateTransforms —— 那会在离屏时停止更新 transform，
            //   而我们的落地判断依赖骨骼位置。AlwaysAnimate 对 4 个角色没有性能压力
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // ── ★按脚部骨骼把模型对齐到 y=0（修悬空）──
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
        /// 把模型整体平移，让「脚底」落在 y=0。
        ///
        /// ★为什么不用渲染包围盒：
        /// SkinnedMeshRenderer.bounds 是 T-pose 下预计算的静态 AABB，
        /// 实测 Bear 的 min.y = -0.680，而脚骨（foot.L/R）其实在 +0.847。
        /// 用包围盒对齐会把模型抬高 0.68，叠加缩放后就是那 0.725 米悬空。
        ///
        /// 脚部骨骼是真实的接地参考点：优先 toe（脚尖，最低）→ heel → foot。
        /// 找不到任何脚骨时才退回包围盒，并打警告。
        /// </summary>
        static void AlignFeetToOrigin(GameObject root)
        {
            float? lowest = null;
            string usedBone = null;

            // 名字里含这些关键词的骨骼视为脚部，按优先级排列
            string[] footKeys = { "toe", "heel", "foot", "ankle" };

            foreach (var key in footKeys)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (!n.Contains(key)) continue;
                    // _end 是骨骼末端辅助节点，位置可能超出实际网格，跳过
                    if (n.EndsWith("_end")) continue;

                    float y = root.transform.InverseTransformPoint(t.position).y;
                    if (lowest == null || y < lowest.Value)
                    {
                        lowest = y;
                        usedBone = t.name;
                    }
                }
                if (lowest != null) break;   // 找到优先级最高的一类就够
            }

            if (lowest == null)
            {
                Debug.LogWarning($"[SCP] {root.name} 找不到脚部骨骼，退回包围盒对齐（可能悬空）");
                var rends = root.GetComponentsInChildren<Renderer>(true);
                if (rends.Length == 0) return;
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                lowest = root.transform.InverseTransformPoint(new Vector3(0f, b.min.y, 0f)).y;
                usedBone = "(bounds)";
            }

            // 把所有子物体整体上移，使 lowest 落到 0。
            // ★不动 root 自己的 localPosition —— 那个值由 ArtResolver 在建场时设置，
            //   在这里改会被覆盖。所以改的是子层（模型骨架根）。
            float shift = -lowest.Value;
            if (Mathf.Abs(shift) < 1e-5f)
            {
                Debug.Log($"[SCP] {root.name} 脚部已在 y=0（参考骨骼 {usedBone}），无需调整");
                return;
            }

            for (int i = 0; i < root.transform.childCount; i++)
            {
                var c = root.transform.GetChild(i);
                c.localPosition += new Vector3(0f, shift, 0f);
            }

            Debug.Log($"[SCP] {root.name} 按骨骼 {usedBone} 对齐地面，上移 {shift:F4}");
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
        /// 按「脚底到头顶」的骨骼距离算缩放，使角色高度等于 characterHeight。
        /// 不用包围盒 —— T-pose 下手臂横伸会让 AABB 的 y 范围失真。
        /// </summary>
        static float ComputeScale(GameObject prefab, float targetHeight)
        {
            if (prefab == null || targetHeight <= 0.01f) return 1f;

            float? top = null;
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                // head_end 是头顶末端节点，正是我们要的「头顶」
                if (n != "head_end" && n != "head") continue;
                float y = prefab.transform.InverseTransformPoint(t.position).y;
                if (top == null || y > top.Value) top = y;
            }

            if (top == null || top.Value < 0.01f)
            {
                Debug.LogWarning($"[SCP] {prefab.name} 找不到头部骨骼，缩放取 1");
                return 1f;
            }

            // AlignFeetToOrigin 已保证脚底在 y=0，所以 top 就是身高
            float scale = targetHeight / top.Value;
            Debug.Log($"[SCP] {prefab.name} 骨骼身高 {top.Value:F4} → 缩放 {scale:F4}（目标 {targetHeight}）");
            return scale;
        }
    }
}
