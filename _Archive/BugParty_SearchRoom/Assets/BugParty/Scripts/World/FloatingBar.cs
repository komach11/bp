using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 世界空间悬浮进度条。纯代码生成，不需要任何 Prefab 或美术资源。
    /// 用于容器上方的搜索读条。
    ///
    /// 注意：本物体刻意 **不** 挂在目标物体下，而是自己跟随目标位置。
    /// 因为容器的 localScale 是非等比的（例如 1.1×1.5×0.7），
    /// 作为子物体会继承父级缩放导致进度条被拉扁变形。
    /// </summary>
    public class FloatingBar : MonoBehaviour
    {
        [Header("跟随")]
        public Transform followTarget;
        public Vector3 worldOffset = Vector3.up * 1.25f;

        Transform _fill;
        Renderer _fillRenderer;
        Renderer _bgRenderer;
        Camera _cam;

        const float Width = 0.9f;
        const float Height = 0.12f;

        /// <summary>
        /// 创建一个跟随 target 的进度条。offset 为世界空间偏移。
        /// </summary>
        public static FloatingBar Create(Transform target, Vector3 worldOffset)
        {
            var root = new GameObject("FloatingBar_" + (target != null ? target.name : "None"));
            // 不设父级，保持世界缩放为 1，避免被目标的非等比缩放拉变形
            root.transform.localScale = Vector3.one;

            var bar = root.AddComponent<FloatingBar>();
            bar.followTarget = target;
            bar.worldOffset = worldOffset;

            if (target != null)
                root.transform.position = target.position + worldOffset;

            // 背景
            var bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "BG";
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(Width, Height, 1f);
            SafeDestroy(bg.GetComponent<Collider>());
            bar._bgRenderer = bg.GetComponent<Renderer>();
            SetColorOn(bar._bgRenderer, new Color(0.07f, 0.07f, 0.09f, 1f));

            // 填充条：用一个锚点子物体，靠 X 缩放从左往右生长
            var fillPivot = new GameObject("FillPivot");
            fillPivot.transform.SetParent(root.transform, false);
            fillPivot.transform.localPosition = new Vector3(-Width * 0.5f, 0f, -0.01f);
            fillPivot.transform.localScale = new Vector3(0f, Height * 0.8f, 1f);

            var fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fill.name = "Fill";
            fill.transform.SetParent(fillPivot.transform, false);
            // Quad 中心在原点，往右偏半格，让缩放从左边缘开始生长
            fill.transform.localPosition = new Vector3(0.5f, 0f, 0f);
            fill.transform.localScale = Vector3.one;
            SafeDestroy(fill.GetComponent<Collider>());
            bar._fillRenderer = fill.GetComponent<Renderer>();
            SetColorOn(bar._fillRenderer, Color.white);

            bar._fill = fillPivot.transform;
            return bar;
        }

        /// <summary>运行时用 Destroy，编辑器下用 DestroyImmediate，避免报错。</summary>
        static void SafeDestroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        static void SetColorOn(Renderer r, Color c)
        {
            if (r == null) return;
            var m = r.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        void LateUpdate()
        {
            // 目标被销毁则自我清理
            if (followTarget == null)
            {
                Destroy(gameObject);
                return;
            }

            transform.position = followTarget.position + worldOffset;

            // 始终面向摄像机
            if (_cam == null) _cam = Camera.main;
            if (_cam != null)
                transform.rotation = Quaternion.LookRotation(
                    transform.position - _cam.transform.position, Vector3.up);
        }

        public void SetFill(float t)
        {
            if (_fill == null) return;
            var s = _fill.localScale;
            s.x = Mathf.Clamp01(t) * Width;
            _fill.localScale = s;
        }

        public void SetColor(Color c) => SetColorOn(_fillRenderer, c);

        public void SetVisible(bool v)
        {
            if (gameObject.activeSelf != v) gameObject.SetActive(v);
        }
    }
}
