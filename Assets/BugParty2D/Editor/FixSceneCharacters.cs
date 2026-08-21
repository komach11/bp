using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BugParty.TopDown2D.EditorTools
{
    /// <summary>
    /// 一键修复已建好场景里的角色问题 —— 不必重新建场。
    ///
    /// 处理三件事：
    /// ① Animator 的 Controller 显示 Missing / Clip Count: 0
    ///    → 重新指定 Controller 与 Avatar
    /// ② 角色模型悬空
    ///    → 按网格 AABB（矩阵变换，不用 BakeMesh）重新对齐到脚底 y=0
    /// ③ 玩家身上缺 CapsuleCollider / FootstepEmitter / 动作表引用
    ///    → 补齐
    ///
    /// 为什么需要它：重新 Build Room Scene 会清掉场景里手工调过的东西
    /// （相机位置、混进来的美术、RoomManager 的衔接配置）。能原地修就不该重建。
    /// </summary>
    public static class FixSceneCharacters
    {
        const string OutDir = "Assets/BugParty2D/Art/Characters";
        const string ControllerPath = OutDir + "/PlayerAnim.controller";
        const string AnimSetPath = OutDir + "/CharacterAnimSet.asset";

        [MenuItem("BugParty2D/修复场景角色（Animator+悬空+碰撞）", false, 2065)]
        [MenuItem("Tools/BugParty2D/修复场景角色（Animator+悬空+碰撞）", false, 2065)]
        public static void Run()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (ctrl == null)
            {
                EditorUtility.DisplayDialog("找不到 Controller",
                    $"读不到 {ControllerPath}\n\n" +
                    "请先执行「BugParty2D ▸ 接入 SCP 角色（完整）」。", "好");
                return;
            }

            var animSet = AssetDatabase.LoadAssetAtPath<CharacterAnimSet>(AnimSetPath);

            var actors = Object.FindObjectsOfType<PlayerActor>(true);
            if (actors.Length == 0)
            {
                EditorUtility.DisplayDialog("场景里没有玩家",
                    "找不到任何 PlayerActor。请先 Build Room Scene。", "好");
                return;
            }

            int fixedAnim = 0, fixedAlign = 0, addedCol = 0, addedStep = 0, wiredSet = 0;
            var log = new System.Text.StringBuilder();

            foreach (var actor in actors)
            {
                // ── ① Animator ──
                var anim = actor.GetComponentInChildren<Animator>(true);
                if (anim != null)
                {
                    bool need = anim.runtimeAnimatorController == null
                                || anim.runtimeAnimatorController != ctrl;
                    if (need)
                    {
                        anim.runtimeAnimatorController = ctrl;
                        fixedAnim++;
                    }

                    // Avatar 从模型的 fbx 上取。Generic rig 没有 Avatar 只播 root motion
                    if (anim.avatar == null || !anim.avatar.isValid)
                    {
                        var av = FindAvatarFor(anim.gameObject);
                        if (av != null) anim.avatar = av;
                        else log.AppendLine($"  ⚠ {actor.name} 找不到 Avatar");
                    }

                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    EditorUtility.SetDirty(anim);
                }

                // ── ② 模型接地对齐 ──
                // Model 是 ArtResolver 生成的包装节点，下面挂 fbx 的 Prefab 实例
                var model = FindModelRoot(actor);
                if (model != null && AlignToGround(model, out float shift))
                {
                    if (Mathf.Abs(shift) > 1e-4f)
                    {
                        fixedAlign++;
                        log.AppendLine($"  {actor.name} 模型上移 {shift:F4}");
                    }
                }

                // ── ③ 补组件 ──
                if (actor.GetComponent<CapsuleCollider>() == null)
                {
                    var col = actor.gameObject.AddComponent<CapsuleCollider>();
                    col.height = 1.45f;
                    col.radius = 0.34f;
                    col.center = new Vector3(0f, 0.73f, 0f);
                    col.direction = 1;
                    addedCol++;
                }

                if (actor.GetComponent<FootstepEmitter>() == null)
                {
                    actor.gameObject.AddComponent<FootstepEmitter>();
                    addedStep++;
                }

                // 动作表引用（只是给个 Inspector 入口，运行时不读）
                var bridge = actor.GetComponent<PlayerAnimatorBridge>();
                if (bridge == null && anim != null)
                {
                    bridge = actor.gameObject.AddComponent<PlayerAnimatorBridge>();
                    bridge.animator = anim;
                }
                if (bridge != null && animSet != null && bridge.animSet != animSet)
                {
                    bridge.animSet = animSet;
                    wiredSet++;
                    EditorUtility.SetDirty(bridge);
                }

                EditorUtility.SetDirty(actor.gameObject);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            string detail = log.Length > 0 ? "\n\n" + log.ToString() : "";
            Debug.Log($"[FixScene] 玩家 {actors.Length} 个｜Controller 修 {fixedAnim}｜" +
                      $"对齐 {fixedAlign}｜加碰撞 {addedCol}｜加脚步 {addedStep}｜接动作表 {wiredSet}{detail}");

            EditorUtility.DisplayDialog("修复完成",
                $"处理了 {actors.Length} 个玩家：\n\n" +
                $"· Controller 重新指定：{fixedAnim}\n" +
                $"· 模型接地对齐：{fixedAlign}\n" +
                $"· 补 CapsuleCollider：{addedCol}\n" +
                $"· 补 FootstepEmitter：{addedStep}\n" +
                $"· 接上动作表：{wiredSet}\n\n" +
                "★记得 Ctrl+S 保存场景。\n\n" +
                "选中任意玩家 ▸ PlayerAnimatorBridge ▸ Anim Set\n" +
                "点开就能换 闲置/搜索/跑步/肘击 动作。", "好");

            Selection.activeGameObject = actors[0].gameObject;
        }

        /// <summary>
        /// 找 Visual 下面那个包装节点（ArtResolver 命名为 Model）。
        /// 找不到就返回 null —— 不能退回 Visual 本身，因为 AlignToGround 会
        /// 改传入节点的 localPosition，而 Visual 的位置是建场时定的，动它会
        /// 让整个角色（含挂点）偏移。
        /// </summary>
        static Transform FindModelRoot(PlayerActor actor)
        {
            var visual = actor.visualRoot != null
                ? actor.visualRoot
                : actor.transform.Find("Visual");
            if (visual == null) return null;

            var model = visual.Find("Model");
            if (model != null) return model;

            // 占位体模式（Body/Head/Facing）没有 Model 节点，也不需要对齐
            return null;
        }

        /// <summary>
        /// 把 model 节点自身抬高，使其下方网格的最低点落在 Visual 的 y=0。
        ///
        /// ★改 model 自己的 localPosition，不改它子层 ——
        /// 子层是 fbx 的 Prefab 实例，动它会产生 Prefab override，
        /// 下次 Prefab 更新时可能被冲掉，而且 Inspector 里会显示一堆修改标记。
        ///
        /// ★用 mesh.bounds 的 8 个角点 + 矩阵变换，不用 BakeMesh ——
        /// 实测 BakeMesh 在编辑态不能正确反映骨骼层级的 scale
        /// （SCP 模型带 scale=100 的单位补偿，BakeMesh 会漏掉它，
        /// 算出的最低点是 -0.0016 而真实值是 -0.7376）。
        /// </summary>
        static bool AlignToGround(Transform model, out float shift)
        {
            shift = 0f;
            var parent = model.parent;
            if (parent == null) return false;

            float minY = float.MaxValue;
            bool any = false;

            // 在 model 的父级（Visual）空间里测量 —— 那才是「离脚下多高」的参考系
            var toParent = parent.worldToLocalMatrix;

            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                if (mesh == null) continue;

                var b = mesh.bounds;
                var m = toParent * r.transform.localToWorldMatrix;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z);
                    float y = m.MultiplyPoint3x4(corner).y;
                    if (y < minY) minY = y;
                    any = true;
                }
            }

            if (!any) return false;

            shift = -minY;
            if (Mathf.Abs(shift) <= 1e-4f) return true;

            model.localPosition += new Vector3(0f, shift, 0f);
            EditorUtility.SetDirty(model);
            return true;
        }

        /// <summary>从 Animator 所在物体反查它的源 fbx，取出 Avatar。</summary>
        static Avatar FindAvatarFor(GameObject go)
        {
            // 先试 Prefab 源
            var src = PrefabUtility.GetCorrespondingObjectFromSource(go);
            string path = src != null ? AssetDatabase.GetAssetPath(src) : null;

            if (string.IsNullOrEmpty(path))
            {
                // 退一步：从 SkinnedMeshRenderer 的 mesh 反查 fbx
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr != null && smr.sharedMesh != null)
                    path = AssetDatabase.GetAssetPath(smr.sharedMesh);
            }

            if (string.IsNullOrEmpty(path)) return null;

            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is Avatar a && a.isValid) return a;
            return null;
        }
    }
}
