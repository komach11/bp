using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 命中冲击环。纯程序化生成的一圈扩散光环，不需要任何美术资源。
    ///
    /// 为什么不用 ParticleSystem：RoomAudioVfx.vfxElbowImpact 那条通道是给美术
    /// 填 prefab 用的，现在是空的。这个组件的作用是在美术资源到位前就让命中
    /// 有明确的视觉反馈 —— 2D 俯视视角下，水平铺开的环比向上喷的粒子清楚得多。
    ///
    /// 美术填了 vfxElbowImpact 之后可以在 PlayerActionFx 里关掉 spawnImpactRing。
    /// </summary>
    public class ImpactRing : MonoBehaviour
    {
        const int Segments = 20;

        float _life;
        float _t;
        float _maxRadius;
        Color _color;
        Transform[] _shards;
        Renderer[] _renderers;

        /// <summary>在世界坐标生成一个冲击环。</summary>
        public static ImpactRing Spawn(Vector3 worldPos, Color color,
                                       float radius, float life)
        {
            var go = new GameObject("ImpactRing");
            go.transform.position = worldPos;
            var ring = go.AddComponent<ImpactRing>();
            ring.Build(color, radius, life);
            return ring;
        }

        void Build(Color color, float radius, float life)
        {
            _color = color;
            _maxRadius = Mathf.Max(0.1f, radius);
            _life = Mathf.Max(0.05f, life);
            _t = 0f;

            _shards = new Transform[Segments];
            _renderers = new Renderer[Segments];

            // 用一圈小方片拼成环。比 LineRenderer 省心，且能各自缩放做出锐利感
            for (int i = 0; i < Segments; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Shard_" + i;
                q.transform.SetParent(transform, false);

                var col = q.GetComponent<Collider>();
                if (col != null) Destroy(col);

                // 躺平朝上，2D 俯视才看得见
                float ang = i * Mathf.PI * 2f / Segments;
                q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                q.transform.localPosition = new Vector3(
                    Mathf.Cos(ang) * 0.1f, 0f, Mathf.Sin(ang) * 0.1f);

                var r = q.GetComponent<Renderer>();
                SetupMaterial(r);

                _shards[i] = q.transform;
                _renderers[i] = r;
            }
        }

        /// <summary>
        /// 建一个半透明材质。同时兼容 Built-in 与 URP —— 本地工程是 Built-in，
        /// 内网工程是 URP，同一份代码要在两边都能看。
        /// </summary>
        void SetupMaterial(Renderer r)
        {
            if (r == null) return;

            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");

            var m = sh != null ? new Material(sh) : new Material(r.sharedMaterial);
            m.name = "Mat_ImpactRing";

            // 开透明混合
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3000;

            ApplyColor(m, _color);
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static void ApplyColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / _life);

            if (k >= 1f) { Destroy(gameObject); return; }

            // 扩散：先快后慢，像冲击波
            float rk = 1f - (1f - k) * (1f - k);
            float radius = Mathf.Lerp(0.1f, _maxRadius, rk);

            // 淡出：后半段才开始，前半段保持亮度以便看清
            float alpha = k < 0.45f ? 1f : 1f - (k - 0.45f) / 0.55f;

            // 碎片随扩散变窄变长，强化「向外炸开」的方向感
            float w = Mathf.Lerp(0.16f, 0.05f, k);
            float h = Mathf.Lerp(0.10f, 0.30f, k);

            for (int i = 0; i < Segments; i++)
            {
                if (_shards[i] == null) continue;

                float ang = i * Mathf.PI * 2f / Segments;
                _shards[i].localPosition = new Vector3(
                    Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
                _shards[i].localScale = new Vector3(w, h, 1f);

                // 让碎片朝向圆心外侧
                _shards[i].localRotation =
                    Quaternion.Euler(90f, -ang * Mathf.Rad2Deg + 90f, 0f);

                if (_renderers[i] != null)
                {
                    var c = _color; c.a = alpha;
                    ApplyColor(_renderers[i].sharedMaterial, c);
                }
            }
        }
    }
}
