using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>Bug 会议室氛围：家具悬浮 + 随机故障闪烁。</summary>
    public class BugAmbience : MonoBehaviour
    {
        [Header("悬浮")]
        public float bobAmplitude = 0.12f;
        public float bobSpeed = 1.1f;
        public float driftSpin = 4f;

        [Header("故障闪烁")]
        public bool enableGlitch = true;
        public float glitchInterval = 3.5f;
        public float glitchDuration = 0.09f;
        public float glitchOffset = 0.14f;
        public Color glitchColor = new Color(0.25f, 0.7f, 1f);

        Vector3 _basePos;
        float _phase;
        float _nextGlitch;
        float _glitchEnd;
        Renderer[] _renderers;
        Color[] _baseColors;
        bool[] _hasColor;
        bool _glitching;

        void Start()
        {
            _basePos = transform.localPosition;
            _phase = Random.Range(0f, Mathf.PI * 2f);
            _nextGlitch = Time.time + Random.Range(0.5f, glitchInterval);

            // ★收集全部子 Renderer，而非只取第一个。
            // 换成美术模型后物件常由多个 mesh 组成，只改第一个会让闪烁几乎看不见。
            var found = GetComponentsInChildren<Renderer>(true);
            _renderers = found;
            _baseColors = new Color[found.Length];
            _hasColor = new bool[found.Length];

            for (int i = 0; i < found.Length; i++)
            {
                var r = found[i];
                if (r == null || r is ParticleSystemRenderer) continue;
                if (!r.enabled) continue;          // 跳过被隐藏的碰撞盒
                var m = r.material;                // 取 material 会自动实例化，不污染共享材质
                if (m.HasProperty("_BaseColor")) { _baseColors[i] = m.GetColor("_BaseColor"); _hasColor[i] = true; }
                else if (m.HasProperty("_Color")) { _baseColors[i] = m.GetColor("_Color"); _hasColor[i] = true; }
            }
        }

        void Update()
        {
            var p = _basePos;
            p.y += Mathf.Sin(Time.time * bobSpeed + _phase) * bobAmplitude;

            if (enableGlitch)
            {
                if (!_glitching && Time.time >= _nextGlitch)
                {
                    _glitching = true;
                    _glitchEnd = Time.time + glitchDuration;
                    ApplyGlitchColor(true);
                }
                else if (_glitching && Time.time >= _glitchEnd)
                {
                    _glitching = false;
                    _nextGlitch = Time.time + glitchInterval * Random.Range(0.55f, 1.6f);
                    ApplyGlitchColor(false);
                }

                if (_glitching)
                {
                    p.x += Random.Range(-glitchOffset, glitchOffset);
                    p.z += Random.Range(-glitchOffset, glitchOffset);
                }
            }

            transform.localPosition = p;

            if (driftSpin != 0f)
                transform.Rotate(Vector3.up, driftSpin * Time.deltaTime, Space.Self);
        }

        void ApplyGlitchColor(bool on)
        {
            if (_renderers == null) return;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (!_hasColor[i]) continue;
                var r = _renderers[i];
                if (r == null) continue;
                var m = r.material;
                // ★用 Lerp 混合而非直接覆盖：带贴图的模型也能看出闪烁，
                //   同时保留各自的原始底色（多 mesh 物件不会被刷成同一个色）
                var c = on ? Color.Lerp(_baseColors[i], glitchColor, 0.75f) : _baseColors[i];
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            }
        }
    }
}
