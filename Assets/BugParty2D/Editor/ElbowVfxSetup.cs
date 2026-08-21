using System.IO;
using UnityEditor;
using UnityEngine;

namespace BugParty.TopDown2D.EditorTools
{
    /// <summary>
    /// 把 Assets/art/shouji 的特效 Prefab 接到肘击上。
    ///
    /// 【为什么不能直接把 shouji.prefab 拖进槽位】
    /// 那份 prefab 的 4 个 ParticleSystem 全是 looping=1 + playOnAwake=1，
    /// lengthInSec=5。直接当一次性打击特效用会有三个问题：
    ///   ① 循环播放 —— 打一拳后特效永远不停，除非被 lifetime 强杀，
    ///      而强杀时机与粒子生命周期不同步，看起来像被"剪断"
    ///   ② scalingMode=1（Local）—— 特效会跟随父级缩放。角色缩放是
    ///      0.17~0.42（见 RoomArtConfig.scaleMul），挂上去会小到看不见
    ///   ③ 没有自动销毁 —— ElbowVfxHook 的 lifetime 只是兜底 Destroy，
    ///      粒子还在发射时就被删掉会突然消失
    ///
    /// 所以本工具生成一份"打击专用"副本：改成一次性播放（looping=0）、
    /// simulationSpace=World（不跟随缩放）、挂 VfxAutoKill 在粒子播完后自毁。
    /// 原始 shouji.prefab 完全不动。
    ///
    /// 产出：Assets/BugParty2D/Art/Vfx/Vfx_ElbowHit.prefab
    ///       Assets/BugParty2D/Art/Vfx/Vfx_ElbowSwing.prefab
    /// </summary>
    public static class ElbowVfxSetup
    {
        const string SrcPrefab = "Assets/art/shouji/shouji.prefab";
        const string OutDir = "Assets/BugParty2D/Art/Vfx";

        [MenuItem("BugParty2D/接入肘击特效（art/shouji）", false, 2070)]
        [MenuItem("Tools/BugParty2D/接入肘击特效（art/shouji）", false, 2070)]
        public static void Run()
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(SrcPrefab);
            if (src == null)
            {
                EditorUtility.DisplayDialog("找不到特效",
                    $"读不到 {SrcPrefab}\n\n请确认素材在 Assets/art/shouji/ 下。", "好");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                "接入肘击特效",
                "将从 art/shouji/shouji.prefab 生成两份打击专用副本：\n\n" +
                "· Vfx_ElbowHit    命中特效（完整 4 层粒子）\n" +
                "· Vfx_ElbowSwing  挥空特效（只留短生命的那两层，更轻）\n\n" +
                "并自动填进 4 个玩家的 ElbowVfxHook 槽位。\n\n" +
                "副本会做三处调整（原 prefab 不动）：\n" +
                "① looping 关掉 —— 否则打一拳特效永远不停\n" +
                "② simulationSpace 改 World —— 否则会被角色的 0.2 倍缩放压到看不见\n" +
                "③ 挂 VfxAutoKill —— 粒子播完自动销毁\n\n" +
                "是否继续？", "开始", "取消"))
                return;

            Directory.CreateDirectory(OutDir);
            AssetDatabase.Refresh();

            var hit = MakeVariant(src, "Vfx_ElbowHit", keepAll: true);
            var swing = MakeVariant(src, "Vfx_ElbowSwing", keepAll: false);

            int wired = WireToScene(hit, swing);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("完成",
                $"已生成：\n" +
                $"  {(hit != null ? "✓" : "✗")} Vfx_ElbowHit\n" +
                $"  {(swing != null ? "✓" : "✗")} Vfx_ElbowSwing\n\n" +
                (wired > 0
                    ? $"已自动填入场景里 {wired} 个玩家的 ElbowVfxHook。\n记得 Ctrl+S 保存场景。"
                    : "场景里没找到玩家（可能未建场）。\n请先 Build Room Scene，再执行本工具。") +
                "\n\n想调特效大小：选中玩家 ▸ ElbowVfxHook ▸ Scale Mul", "好");

            if (hit != null)
            {
                Selection.activeObject = hit;
                EditorGUIUtility.PingObject(hit);
            }
        }

        /// <summary>
        /// 生成一份打击专用副本。
        /// keepAll = false 时只保留生命周期短的子发射器（适合挥空的一闪）。
        /// </summary>
        static GameObject MakeVariant(GameObject src, string name, bool keepAll)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            if (inst == null)
            {
                Debug.LogError($"[ElbowVfx] 实例化失败：{SrcPrefab}");
                return null;
            }

            PrefabUtility.UnpackPrefabInstance(
                inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = name;

            float maxLife = 0f;

            var systems = inst.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
            {
                var main = ps.main;

                // ① 一次性播放
                main.loop = false;
                main.playOnAwake = true;

                // ② ★世界空间模拟 + Shape 缩放模式。
                //    原来是 Local + scalingMode=1(Local)，会随父级缩放 ——
                //    ElbowVfxHook 生成的特效虽然不挂在角色下（World 空间），
                //    但 Local 模拟会让已发射的粒子跟着发射器移动，打击特效
                //    应该留在原地炸开而不是被拖着走。
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                //    Shape：只有发射形状受 transform.scale 影响，粒子自身大小不变。
                //    这样 ElbowVfxHook.scaleMul 能整体调大小，又不会因角色的
                //    0.2 倍缩放把粒子压到看不见
                main.scalingMode = ParticleSystemScalingMode.Shape;

                // ③ duration 压到实际粒子生命，避免 5 秒的空转
                float life = main.startLifetime.mode == ParticleSystemCurveMode.TwoConstants
                    ? Mathf.Max(main.startLifetime.constantMin, main.startLifetime.constantMax)
                    : main.startLifetime.constant;
                if (life <= 0.01f) life = 0.3f;
                main.duration = Mathf.Max(0.1f, life);

                if (life > maxLife) maxLife = life;

                // 挥空版只留短命的那些层（长尾的拖影留给命中）
                if (!keepAll && life > 0.4f && ps.gameObject != inst)
                {
                    Object.DestroyImmediate(ps.gameObject);
                    continue;
                }

                // 打击特效不需要投影
                var r = ps.GetComponent<ParticleSystemRenderer>();
                if (r != null)
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
            }

            // ③ 自动销毁。留一点余量让最后一批粒子淡完
            var kill = inst.GetComponent<VfxAutoKill>();
            if (kill == null) kill = inst.AddComponent<VfxAutoKill>();
            kill.extraDelay = 0.35f;

            string path = OutDir + "/" + name + ".prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(inst, path);
            Object.DestroyImmediate(inst);

            Debug.Log($"[ElbowVfx] {name} → {path}（{systems.Length} 层粒子，最长生命 {maxLife:F2}s）");
            return saved;
        }

        /// <summary>把生成的特效填进当前场景所有玩家的 ElbowVfxHook。</summary>
        static int WireToScene(GameObject hit, GameObject swing)
        {
            int n = 0;
            var hooks = Object.FindObjectsOfType<ElbowVfxHook>(true);
            foreach (var h in hooks)
            {
                if (hit != null) h.hitVfx = hit;
                if (swing != null) h.swingVfx = swing;

                // ★命中特效在受击者胸口高度生成，朝向攻击方向 —— 打击感最强
                h.hitHeight = 0.9f;
                h.hitFaceAttackDir = true;

                // ★缩放：角色本体被缩到 0.2 倍左右，但特效走 World 空间不受影响，
                //   所以这里用 1 就是原始大小。素材偏大时往下调
                h.scaleMul = 1f;
                h.lifetime = 2.5f;

                EditorUtility.SetDirty(h);
                n++;
            }
            if (n > 0)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return n;
        }
    }
}
