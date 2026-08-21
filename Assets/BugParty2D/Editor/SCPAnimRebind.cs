using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BugParty.TopDown2D.EditorTools
{
    /// <summary>
    /// 把 SCP 的动画重绑定到模型骨架上 —— Generic rig 方案。
    ///
    /// 【为什么需要这个】
    /// 模型骨骼：Polydactyl_Metarig / hips / spine / chest …（65 骨）
    /// 动画骨骼：PolyAnim / root / ORG-hips / ORG-spine …（506 节点，Rigify 装配）
    ///
    /// Generic rig 按 transform 路径名绑定动画曲线，路径对不上 → 动画播了但骨骼不动
    /// → 角色停在 T-pose。
    ///
    /// ★关键事实：把 ORG- 前缀去掉后，78 根骨骼与模型 100% 同名，一个不差。
    /// 说明动画数据本身是完整可用的，只差一层路径转换。
    ///
    /// 【与 Humanoid 方案的取舍】
    /// Humanoid 靠 Avatar 做人体语义映射，不依赖路径名，配置更省事 —— 但 SCP 是
    /// 熊/猫这类拟人角色，骨骼比例不符合人体，Unity 的自动映射有可能失败或变形。
    /// 本方案是纯路径重写，结果完全可预测，且不引入 Humanoid 的高度归一化。
    ///
    /// 【做法】
    /// 1. 从模型 fbx 建立「骨骼名 → 相对模型根的完整路径」表
    /// 2. 逐条读原 clip 的 EditorCurveBinding，把 ORG- 路径翻译成模型路径
    /// 3. 写成新的 .anim 资产（原 fbx 完全不动）
    ///
    /// 产出：Assets/BugParty2D/Art/Characters/Clips/*.anim
    /// </summary>
    public static class SCPAnimRebind
    {
        const string ModelPath =
            "Assets/art/SCP Models (.FBX)/Simple Character Pack Models (.FBX)/Bear.fbx";
        const string AnimDir = "Assets/art/SCP Animations (.fbx)/Animation";
        const string OutDir = "Assets/BugParty2D/Art/Characters/Clips";

        [MenuItem("BugParty2D/重绑定 SCP 动画到模型骨架（Generic 方案）", false, 2062)]
        [MenuItem("Tools/BugParty2D/重绑定 SCP 动画到模型骨架（Generic 方案）", false, 2062)]
        public static void Run()
        {
            if (!EditorUtility.DisplayDialog(
                "重绑定 SCP 动画",
                "把动画 fbx 里的 clip 复制成独立 .anim，并把骨骼路径从\n" +
                "  PolyAnim/root/ORG-hips/ORG-spine/…\n" +
                "翻译成模型的\n" +
                "  Polydactyl_Metarig/hips/spine/…\n\n" +
                "这是 Generic rig 的 T-pose 解法，不改动原 fbx。\n" +
                "完成后会自动重建 AnimatorController。\n\n" +
                "是否继续？", "开始", "取消"))
                return;

            var map = RebindAll(out string report);
            Debug.Log("[Rebind] " + report);

            if (map.Count == 0)
            {
                EditorUtility.DisplayDialog("没有生成任何动画",
                    "所有 clip 的骨骼路径都无法映射到模型骨架。\n\n" + report, "好");
                return;
            }

            EditorUtility.DisplayDialog("完成",
                $"已生成 {map.Count} 个重绑定动画到\n{OutDir}\n\n" +
                "下一步：\n" +
                "① 打开 CharacterAnimSet，把槽位换成 Clips/ 目录下的新 .anim\n" +
                "② 执行「BugParty2D ▸ 重建角色动画」\n\n" +
                "或者直接执行「BugParty2D ▸ 自动填充动作表（用重绑定动画）」。", "好");
        }

        /// <summary>
        /// 执行全部重绑定，返回「干净名 → 新 clip」映射。
        /// 供 SCPCharacterSetup 的完整流程直接调用，不弹任何对话框。
        /// </summary>
        public static Dictionary<string, AnimationClip> RebindAll(out string report)
        {
            var outMap = new Dictionary<string, AnimationClip>();

            // ① 模型骨骼路径表
            var bonePaths = BuildBonePathMap();
            if (bonePaths.Count == 0)
            {
                report = $"读不到模型骨架：{ModelPath}";
                return outMap;
            }

            // ② 逐个 clip 重绑定
            Directory.CreateDirectory(OutDir);
            AssetDatabase.Refresh();

            int made = 0, skipped = 0;
            var lines = new List<string>();

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var raw in Directory.GetFiles(AnimDir, "*.fbx", SearchOption.TopDirectoryOnly))
                {
                    string fbx = raw.Replace('\\', '/');
                    foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(fbx))
                    {
                        var src = obj as AnimationClip;
                        if (src == null) continue;
                        if (src.name.StartsWith("__preview__")) continue;

                        string clean = src.name;
                        int bar = clean.LastIndexOf('|');
                        if (bar >= 0) clean = clean.Substring(bar + 1);
                        clean = Sanitize(clean);

                        var res = Rebind(src, bonePaths, clean);
                        if (res.matched == 0) { skipped++; continue; }

                        made++;
                        lines.Add($"{clean}: {res.matched}/{res.total}");
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            // ★重要：StopAssetEditing 之后才能可靠地 Load 出刚创建的资产
            foreach (var f in Directory.GetFiles(OutDir, "*.anim", SearchOption.TopDirectoryOnly))
            {
                var p = f.Replace('\\', '/');
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                if (c == null) continue;
                string key = Path.GetFileNameWithoutExtension(p);
                outMap[key] = c;
            }

            report = $"模型骨骼 {bonePaths.Count} 根，生成 {made} 个 .anim，" +
                     $"跳过 {skipped} 个（无可映射曲线）；载入 {outMap.Count} 个";
            foreach (var l in lines) Debug.Log("[Rebind]   " + l);
            return outMap;
        }

        [MenuItem("BugParty2D/自动填充动作表（用重绑定动画）", false, 2063)]
        [MenuItem("Tools/BugParty2D/自动填充动作表（用重绑定动画）", false, 2063)]
        public static void FillAnimSetFromRebound()
        {
            const string setPath = "Assets/BugParty2D/Art/Characters/CharacterAnimSet.asset";
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimSet>(setPath);
            if (set == null)
            {
                EditorUtility.DisplayDialog("找不到动作表",
                    "请先执行「接入 SCP 角色（完整）」生成 CharacterAnimSet。", "好");
                return;
            }
            if (!Directory.Exists(OutDir))
            {
                EditorUtility.DisplayDialog("找不到重绑定动画",
                    "请先执行「重绑定 SCP 动画到模型骨架」。", "好");
                return;
            }

            var map = new Dictionary<string, AnimationClip>();
            foreach (var g in AssetDatabase.FindAssets("t:AnimationClip", new[] { OutDir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                if (c != null && !map.ContainsKey(c.name)) map[c.name] = c;
            }
            if (map.Count == 0)
            {
                EditorUtility.DisplayDialog("目录是空的",
                    $"{OutDir} 下没有 AnimationClip。", "好");
                return;
            }

            // 强制覆盖 —— 这个菜单的语义就是「换成重绑定版」
            set.idle = Pick(map, "Idle_A", "Idle_B", "Idle_C");
            set.walk = Pick(map, "Walk_Forward");
            set.run = Pick(map, "Run_Forward");
            set.search = Pick(map, "Fight_Idle", "Idle_B", "Idle_A");
            set.elbow = Pick(map, "Fight_Punch_Right", "Fight_Punch_Left");
            set.jump = Pick(map, "Jump_A", "Jump_B");
            set.fall = Pick(map, "Falling", "Jump_B");
            set.getHit = Pick(map, "Fight_Hit_A", "Fight_Hit_B");
            set.stagger = Pick(map, "Fight_Hit_B", "Fight_Hit_A");

            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();

            SCPCharacterSetup.RebuildAnimOnly();
        }

        static AnimationClip Pick(Dictionary<string, AnimationClip> m, params string[] names)
        {
            foreach (var n in names)
                if (m.TryGetValue(n, out var c) && c != null) return c;
            return null;
        }

        // ══════════════════════════════════════════════
        //  骨骼路径表
        // ══════════════════════════════════════════════

        /// <summary>
        /// 建「骨骼名 → 相对模型根的路径」表。
        ///
        /// AnimationClip 的 EditorCurveBinding.path 是相对 Animator 所在节点的路径。
        /// 我们的 Prefab 把 Animator 挂在模型根上（Char_xxx），所以路径应形如
        /// "Polydactyl_Metarig/hips/spine"。
        /// </summary>
        static Dictionary<string, string> BuildBonePathMap()
        {
            var map = new Dictionary<string, string>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefab == null) return map;

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (inst == null) return map;

            try
            {
                var root = inst.transform;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                {
                    if (t == root) continue;
                    string path = AnimationUtility.CalculateTransformPath(t, root);
                    if (string.IsNullOrEmpty(path)) continue;
                    // 同名骨骼取第一个（SCP 骨架里没有重名）
                    if (!map.ContainsKey(t.name)) map[t.name] = path;
                }
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
            return map;
        }

        // ══════════════════════════════════════════════
        //  重绑定
        // ══════════════════════════════════════════════

        struct Result { public int total, matched; }

        /// <summary>
        /// 把一个 clip 的全部曲线路径翻译到模型骨架，写成新的 .anim。
        /// </summary>
        static Result Rebind(AnimationClip src, Dictionary<string, string> bonePaths, string outName)
        {
            var res = new Result();

            var dst = new AnimationClip();
            dst.name = outName;
            dst.frameRate = src.frameRate;

            // 保留 loop 设置 —— Idle/Walk/Run 必须循环，否则播一遍就停
            var srcSettings = AnimationUtility.GetAnimationClipSettings(src);
            var dstSettings = new AnimationClipSettings
            {
                loopTime = ShouldLoop(outName),
                loopBlend = srcSettings.loopBlend,
                cycleOffset = srcSettings.cycleOffset,
                startTime = srcSettings.startTime,
                stopTime = srcSettings.stopTime,
                // ★关掉所有 root 相关的 bake —— 位移由 CharacterController 驱动，
                //   动画不该产生任何位移
                loopBlendOrientation = true,
                loopBlendPositionY = true,
                loopBlendPositionXZ = true,
                keepOriginalOrientation = false,
                keepOriginalPositionY = true,
                keepOriginalPositionXZ = false,
            };

            // ── 浮点曲线（骨骼 TRS）──
            foreach (var b in AnimationUtility.GetCurveBindings(src))
            {
                res.total++;

                string leaf = LeafName(b.path);
                if (leaf == null) continue;

                if (!bonePaths.TryGetValue(leaf, out var newPath)) continue;

                var curve = AnimationUtility.GetEditorCurve(src, b);
                if (curve == null) continue;

                var nb = new EditorCurveBinding
                {
                    path = newPath,
                    type = b.type,
                    propertyName = b.propertyName,
                };
                AnimationUtility.SetEditorCurve(dst, nb, curve);
                res.matched++;
            }

            if (res.matched == 0) return res;

            AnimationUtility.SetAnimationClipSettings(dst, dstSettings);

            string path = OutDir + "/" + outName + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null)
            {
                // 覆盖已有资产，保持 guid 不变 —— 否则动作表里的引用会断
                EditorUtility.CopySerialized(dst, existing);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(dst, path);
            }
            return res;
        }

        /// <summary>
        /// 从曲线路径里取末端骨骼名，并剥掉 Rigify 的 ORG- 前缀。
        ///
        /// 只接受 ORG- 骨骼与无前缀骨骼：
        ///   MCH- 是机械控制骨（IK 目标、拉伸修正等），不直接驱动网格
        ///   VIS- 是视口辅助显示骨
        ///   .fk / .ik 是 FK/IK 切换用的镜像骨
        /// 这些都不该映射到模型骨架 —— 映射过去会互相打架。
        /// </summary>
        static string LeafName(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            int slash = path.LastIndexOf('/');
            string leaf = slash >= 0 ? path.Substring(slash + 1) : path;

            if (leaf.StartsWith("MCH-") || leaf.StartsWith("VIS-") || leaf.StartsWith("WGT-"))
                return null;
            if (leaf.Contains(".fk.") || leaf.Contains(".ik.") ||
                leaf.EndsWith(".fk") || leaf.EndsWith(".ik"))
                return null;
            if (leaf.Contains("_hose") || leaf.Contains("_target") || leaf.Contains("_roll"))
                return null;

            if (leaf.StartsWith("ORG-")) leaf = leaf.Substring(4);
            return leaf;
        }

        /// <summary>循环动作：待机、走、跑、以及戒备姿态。一次性动作不循环。</summary>
        static bool ShouldLoop(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.StartsWith("idle")) return true;
            if (n.StartsWith("walk")) return true;
            if (n.StartsWith("run")) return true;
            if (n == "falling") return true;              // 下落要一直播
            if (n == "fight_idle") return true;           // 搜索用它，需要循环
            return false;
        }

        static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }
    }
}
