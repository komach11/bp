using UnityEngine;
using UnityEditor;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 美术资源解析层（Editor 建场期使用）。
    /// 统一「有 prefab 就用 prefab，没有就回退占位体」的规则。
    ///
    /// 设计原则 —— 碰撞与视觉彻底分离：
    ///   · 碰撞体永远是规整的程序化 Cube（美术模式下关掉渲染），保证跳跃落点、
    ///     CharacterController.stepOffset、平台顶面高度 topY 的行为完全不变；
    ///   · 美术模型只作为视觉子物体挂在下面，自带的 Collider 会被清除。
    /// 这样换任何模型都不影响玩法手感，桌面容器也不会浮空或陷入桌子。
    ///
    /// 同时自动处理低多边形素材包的两个常见问题：
    ///   · 模型不是米制单位（例如 Kenney 家具包的桌子有 7.3 单位宽）
    ///   · 轴心不在几何中心（Kenney 多数模型的轴心在角落）
    /// </summary>
    public static class ArtResolver
    {
        /// <summary>
        /// 在 parent 下生成视觉体。
        /// </summary>
        /// <param name="slot">美术槽位，可为 null</param>
        /// <param name="parent">挂载父节点</param>
        /// <param name="targetSize">目标尺寸（米）。fitToSize 时按此等比缩放</param>
        /// <param name="fallbackColor">回退占位体的颜色</param>
        /// <param name="visualName">视觉体名字</param>
        /// <returns>用于染色/闪烁的 Renderer，可能为 null</returns>
        public static Renderer BuildVisual(
            ArtSlot slot, Transform parent, Vector3 targetSize,
            Color fallbackColor, string visualName = "Visual")
        {
            if (slot != null && slot.HasArt)
                return InstantiateArt(slot, parent, targetSize, visualName);

            return BuildPlaceholder(parent, targetSize, fallbackColor, visualName);
        }

        /// <summary>实例化美术 prefab 并对齐到目标尺寸与位置。</summary>
        public static Renderer InstantiateArt(
            ArtSlot slot, Transform parent, Vector3 targetSize, string visualName)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(slot.prefab);
            if (go == null) return null;

            go.name = visualName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, slot.yawOffset, 0f);
            go.transform.localScale = Vector3.one;

            // 美术模型不承担碰撞，碰撞交给外层的程序化 Box
            StripColliders(go);

            AlignToBounds(go.transform, targetSize, slot);

            return FindColorTarget(go);
        }

        /// <summary>
        /// 按包围盒把模型缩放并居中到目标尺寸。
        /// 解决「模型不是米制」与「轴心不在中心」两个问题。
        /// </summary>
        static void AlignToBounds(Transform t, Vector3 targetSize, ArtSlot slot)
        {
            if (!TryGetLocalBounds(t, out var b)) return;

            float scale = slot.scaleMul;

            if (slot.fitToSize && b.size.x > 1e-4f && b.size.y > 1e-4f && b.size.z > 1e-4f)
            {
                // 等比缩放：取三轴中最保守的比例，避免模型被拉变形
                float sx = targetSize.x / b.size.x;
                float sy = targetSize.y / b.size.y;
                float sz = targetSize.z / b.size.z;
                scale *= Mathf.Min(sx, Mathf.Min(sy, sz));
            }

            t.localScale = Vector3.one * scale;

            if (slot.fitToSize)
            {
                // 重新取缩放后的包围盒，把 XZ 居中、底面贴到 y=0
                if (TryGetLocalBounds(t, out var b2))
                {
                    var offset = new Vector3(-b2.center.x, -b2.min.y, -b2.center.z);
                    t.localPosition += offset + new Vector3(0f, slot.yOffset, 0f);
                }
            }
            else
            {
                // ★fitToSize = false 表示「Prefab 里已经摆好了」—— 只应用 yOffset，
                //   绝不再用包围盒重定位。
                //
                //   这一分支是为骨骼动画角色加的。SkinnedMeshRenderer.bounds 是 T-pose
                //   下预计算的静态 AABB，与真实骨骼位置能差出半米：实测 Bear 的
                //   bounds.min.y = -0.680，而脚骨（foot.L/R）其实在 +0.847。
                //   用它把「底面贴到 y=0」等于把角色整体抬高 0.68 —— 悬空就是这么来的。
                //
                //   带骨骼的 Prefab 由 SCPCharacterSetup.AlignFeetToOrigin 按
                //   toe/heel/foot 骨骼预先对齐过，这里保持原样才正确。
                t.localPosition += new Vector3(0f, slot.yOffset, 0f);
            }
        }

        /// <summary>取相对于 t 自身父空间的包围盒（含全部子 Renderer）。</summary>
        static bool TryGetLocalBounds(Transform t, out Bounds bounds)
        {
            bounds = new Bounds();
            var rends = t.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return false;

            bool init = false;
            var parent = t.parent;
            foreach (var r in rends)
            {
                if (r is ParticleSystemRenderer) continue;
                var wb = r.bounds;
                // 世界包围盒 → 父空间
                var c = parent != null ? parent.InverseTransformPoint(wb.center) : wb.center;
                var e = wb.extents;
                if (parent != null)
                {
                    var ls = parent.lossyScale;
                    e = new Vector3(
                        ls.x != 0f ? e.x / Mathf.Abs(ls.x) : e.x,
                        ls.y != 0f ? e.y / Mathf.Abs(ls.y) : e.y,
                        ls.z != 0f ? e.z / Mathf.Abs(ls.z) : e.z);
                }
                var bb = new Bounds(c, e * 2f);
                if (!init) { bounds = bb; init = true; }
                else bounds.Encapsulate(bb);
            }
            return init;
        }

        /// <summary>生成程序化占位体（原本的彩色方块行为）。</summary>
        public static Renderer BuildPlaceholder(
            Transform parent, Vector3 size, Color color, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = size;
            StripColliders(go);
            var r = go.GetComponent<Renderer>();
            SetColor(r, color);
            return r;
        }

        /// <summary>
        /// 创建「碰撞盒 + 视觉体」组合。这是家具/平台的标准构建方式。
        /// 碰撞盒始终是规整 Cube，尺寸严格等于 size，保证玩法行为不变。
        /// </summary>
        /// <param name="hideColliderMesh">有美术模型时隐藏碰撞盒的渲染</param>
        public static GameObject BuildSolid(
            ArtSlot slot, Transform parent, string name,
            Vector3 localPos, Vector3 size, Color fallbackColor,
            out Renderer colorTarget)
        {
            // 外层：碰撞盒（规整 Cube，尺寸就是逻辑尺寸）
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPos;
            box.transform.localScale = size;

            var boxR = box.GetComponent<Renderer>();

            bool hasArt = slot != null && slot.HasArt;
            if (hasArt)
            {
                // 碰撞盒不渲染，只留碰撞
                boxR.enabled = false;

                // 视觉体挂在碰撞盒下。注意碰撞盒自身有非等比 scale，
                // 直接挂子物体会被拉伸，所以插一层反向缩放的中间节点。
                var pivot = new GameObject("ArtPivot");
                pivot.transform.SetParent(box.transform, false);
                pivot.transform.localPosition = new Vector3(0f, -0.5f, 0f); // 到碰撞盒底面
                pivot.transform.localScale = new Vector3(
                    size.x != 0f ? 1f / size.x : 1f,
                    size.y != 0f ? 1f / size.y : 1f,
                    size.z != 0f ? 1f / size.z : 1f);

                colorTarget = InstantiateArt(slot, pivot.transform, size, "Art");
                if (colorTarget == null) colorTarget = boxR;
            }
            else
            {
                SetColor(boxR, fallbackColor);
                colorTarget = boxR;
            }
            return box;
        }

        /// <summary>清除模型自带的全部 Collider（建场期用）。</summary>
        public static void StripColliders(GameObject go)
        {
            if (go == null) return;
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                Object.DestroyImmediate(cols[i]);
        }

        /// <summary>
        /// 找用于染色与故障闪烁的 Renderer。
        /// 优先 SkinnedMeshRenderer（角色），否则第一个 MeshRenderer。
        /// </summary>
        public static Renderer FindColorTarget(GameObject go)
        {
            if (go == null) return null;
            var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr != null) return smr;
            return go.GetComponentInChildren<Renderer>();
        }

        /// <summary>
        /// 给 Renderer 设置颜色。
        /// ★走 PipelineMat 而不是复制 sharedMaterial —— CreatePrimitive 自带的是
        ///   Built-in Standard 材质，URP 工程里该 shader 不存在，复制它只会得到
        ///   一个同样坏掉的洋红材质。PipelineMat 会按当前管线选正确的 shader。
        /// </summary>
        public static void SetColor(Renderer r, Color c)
        {
            PipelineMat.Apply(r, c);
        }
    }
}
