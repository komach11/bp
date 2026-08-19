using System.Collections.Generic;
using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 撤离点。★塔科夫最核心的设计：搜到东西不算赢，带出去才算。
    ///
    /// 只在 Extraction 阶段开启。玩家必须站在区域内持续一段时间，
    /// 期间被打断（被击中或走出去）进度就重置。
    /// </summary>
    public class ExtractionZone : MonoBehaviour
    {
        [Header("配置")]
        [Tooltip("区域半径")]
        [Min(0.5f)] public float radius = 2.2f;

        [Tooltip("显示名，HUD 提示用")]
        public string zoneName = "传送门";

        [Header("视觉")]
        [Tooltip("激活时的颜色")]
        public Color activeColor = new Color(0.25f, 0.85f, 1f);

        [Tooltip("未激活时的颜色")]
        public Color inactiveColor = new Color(0.3f, 0.3f, 0.34f);

        public Renderer zoneRenderer;

        [Tooltip("激活时的旋转速度，让它显眼")]
        public float spinSpeed = 40f;

        // 每个玩家各自的撤离进度
        readonly Dictionary<PlayerRig, float> _progress = new Dictionary<PlayerRig, float>();

        RaidConfig _cfg;
        bool _isActive;
        Transform _visual;

        public bool IsActive => _isActive;

        void Awake()
        {
            if (zoneRenderer == null) zoneRenderer = GetComponentInChildren<Renderer>();
            _visual = transform.Find("Visual");
        }

        void Start()
        {
            _cfg = RaidManager.Instance != null ? RaidManager.Instance.config : null;
            SetActive(false);
        }

        public void SetActive(bool active)
        {
            _isActive = active;
            if (!active) _progress.Clear();
            ApplyColor(active ? activeColor : inactiveColor);
        }

        void ApplyColor(Color c)
        {
            if (zoneRenderer == null) return;
            var m = zoneRenderer.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>取得某玩家的撤离进度 0~1。HUD 用。</summary>
        public float GetProgress01(PlayerRig rig)
        {
            if (_cfg == null || _cfg.extractHoldTime <= 0f) return 0f;
            if (rig == null || !_progress.TryGetValue(rig, out float t)) return 0f;
            return Mathf.Clamp01(t / _cfg.extractHoldTime);
        }

        public bool Contains(Vector3 worldPos)
        {
            var flat = worldPos - transform.position;
            flat.y = 0f;
            return flat.sqrMagnitude <= radius * radius;
        }

        void Update()
        {
            if (_visual != null && _isActive)
                _visual.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.Self);

            if (!_isActive || _cfg == null) return;

            var mgr = RaidManager.Instance;
            if (mgr == null || mgr.Phase != RoundPhase.Extraction) return;

            for (int i = 0; i < mgr.players.Count; i++)
            {
                var p = mgr.players[i];
                if (p == null || !p.IsAlive) continue;
                if (p.Result != ExtractResult.InRaid) continue;

                bool inside = Contains(p.transform.position);

                // 硬直期间进度暂停——被打断就撤不了 ★
                bool blocked = p.IsStaggered;

                if (inside && !blocked)
                {
                    float t = _progress.TryGetValue(p, out float cur) ? cur : 0f;
                    t += Time.deltaTime;
                    _progress[p] = t;

                    RaidEvents.RaiseExtractProgress(p, GetProgress01(p));

                    if (t >= _cfg.extractHoldTime)
                    {
                        _progress.Remove(p);
                        p.OnExtractSuccess();
                    }
                }
                else if (_progress.ContainsKey(p))
                {
                    if (_cfg.resetExtractOnLeave)
                    {
                        _progress.Remove(p);
                        RaidEvents.RaiseExtractProgress(p, 0f);
                    }
                }
            }
        }

        public void ResetForNewRound()
        {
            _progress.Clear();
            SetActive(false);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = _isActive
                ? new Color(0.25f, 0.85f, 1f, 0.5f)
                : new Color(0.5f, 0.5f, 0.5f, 0.3f);

            // 画一个圆环表示范围
            const int seg = 32;
            Vector3 prev = transform.position + new Vector3(radius, 0.05f, 0f);
            for (int i = 1; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                var next = transform.position + new Vector3(
                    Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
