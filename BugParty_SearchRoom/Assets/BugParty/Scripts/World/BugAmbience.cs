using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// Bug 会议室的环境氛围：家具悬浮漂动 + 随机故障闪烁。
    /// 挂在任意物体上即可，用来营造"房间被数据侵蚀"的感觉。
    /// </summary>
    public class BugAmbience : MonoBehaviour
    {
        [Header("悬浮")]
        [Tooltip("上下漂浮幅度（米）")]
        public float bobAmplitude = 0.12f;

        [Tooltip("漂浮速度")]
        public float bobSpeed = 1.1f;

        [Tooltip("缓慢自转速度（度/秒），0 表示不转")]
        public float driftSpin = 4f;

        [Header("故障闪烁")]
        [Tooltip("是否启用随机 glitch")]
        public bool enableGlitch = true;

        [Tooltip("平均多少秒闪一次")]
        public float glitchInterval = 3.5f;

        [Tooltip("每次闪烁持续时间")]
        public float glitchDuration = 0.09f;

        [Tooltip("闪烁时的位移抖动幅度")]
        public float glitchOffset = 0.14f;

        [Tooltip("闪烁时叠加的颜色")]
        public Color glitchColor = new Color(0.25f, 0.7f, 1f);

        Vector3 _basePos;
        float _phase;
        float _nextGlitch;
        float _glitchEnd;
        Renderer _renderer;
        Color _baseColor;
        bool _hasColor;
        bool _glitching;

        void Start()
        {
            _basePos = transform.localPosition;
            _phase = Random.Range(0f, Mathf.PI * 2f);
            _nextGlitch = Time.time + Random.Range(0.5f, glitchInterval);

            _renderer = GetComponentInChildren<Renderer>();
            if (_renderer != null)
            {
                var m = _renderer.material;
                if (m.HasProperty("_BaseColor")) { _baseColor = m.GetColor("_BaseColor"); _hasColor = true; }
                else if (m.HasProperty("_Color")) { _baseColor = m.GetColor("_Color"); _hasColor = true; }
            }
        }

        void Update()
        {
            // ── 悬浮漂动 ──
            var p = _basePos;
            p.y += Mathf.Sin(Time.time * bobSpeed + _phase) * bobAmplitude;

            // ── 故障闪烁 ──
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
            if (_renderer == null || !_hasColor) return;
            var m = _renderer.material;
            var c = on ? Color.Lerp(_baseColor, glitchColor, 0.75f) : _baseColor;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }
}
