using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 可搜索容器：抽屉、饮水机、天花板夹层、投影仪箱等。
    /// 核心规则：同一时间只允许一个人搜（独占锁）。被打断后进入冷却。
    /// </summary>
    public class SearchContainer : MonoBehaviour
    {
        [Header("身份")]
        [Tooltip("显示名，用于调试与提示")]
        public string containerName = "抽屉";

        [Tooltip("交互点。留空则用自身位置。建议放在容器正面一点点")]
        public Transform interactAnchor;

        [Header("产出")]
        [Tooltip("还能被成功搜出几件道具。0 表示已枯竭。由 Manager 在开局重置")]
        public int remainingYield = 2;

        [Header("视觉反馈")]
        [Tooltip("被搜索时高亮的渲染器，留空自动取自身")]
        public Renderer highlightRenderer;

        [Tooltip("枯竭后的颜色")]
        public Color depletedColor = new Color(0.32f, 0.32f, 0.34f);

        // ── 运行时 ─────────────────────────────────────────
        PlayerActor _occupant;
        float _cooldownUntil;
        Color _baseColor;
        bool _colorCached;
        FloatingBar _bar;
        int _initialYield;

        public PlayerActor Occupant => _occupant;
        public bool IsOccupied => _occupant != null;
        public bool IsDepleted => remainingYield <= 0;
        public bool IsCoolingDown => Time.time < _cooldownUntil;

        public Vector3 InteractPoint =>
            interactAnchor != null ? interactAnchor.position : transform.position;

        void Awake()
        {
            _initialYield = Mathf.Max(0, remainingYield);

            if (highlightRenderer == null)
                highlightRenderer = GetComponentInChildren<Renderer>();

            CacheBaseColor();
        }

        void Start()
        {
            // 创建世界空间读条。刻意不作为子物体，避免继承容器的非等比缩放
            _bar = FloatingBar.Create(transform, Vector3.up * (GetTopY() + 0.35f));
            if (_bar != null) _bar.SetVisible(false);
        }

        /// <summary>取得容器顶部相对自身原点的高度，让读条浮在物体上方而不是穿进去。</summary>
        float GetTopY()
        {
            var r = GetComponentInChildren<Renderer>();
            if (r != null) return r.bounds.extents.y;
            return 0.8f;
        }

        void OnDestroy()
        {
            // 读条不是子物体，需要手动清理
            if (_bar != null) Destroy(_bar.gameObject);
        }

        void CacheBaseColor()
        {
            if (_colorCached || highlightRenderer == null) return;
            var m = highlightRenderer.material;
            if (m.HasProperty("_BaseColor")) _baseColor = m.GetColor("_BaseColor");
            else if (m.HasProperty("_Color")) _baseColor = m.GetColor("_Color");
            else _baseColor = Color.white;
            _colorCached = true;
        }

        /// <summary>该玩家现在能不能搜这个容器。</summary>
        public bool IsAvailableFor(PlayerActor asker)
        {
            if (IsDepleted) return false;
            if (IsCoolingDown) return false;
            if (IsOccupied && _occupant != asker) return false;
            return true;
        }

        /// <summary>占用容器。成功返回 true。</summary>
        public bool TryClaim(PlayerActor actor)
        {
            if (!IsAvailableFor(actor)) return false;
            _occupant = actor;
            if (_bar != null)
            {
                _bar.SetVisible(true);
                _bar.SetColor(actor.playerColor.ToColor());
                _bar.SetFill(0f);
            }
            return true;
        }

        /// <summary>释放容器。interrupted=true 会触发冷却。</summary>
        public void Release(PlayerActor actor, bool interrupted)
        {
            if (_occupant != actor) return;
            _occupant = null;

            if (_bar != null) _bar.SetVisible(false);

            if (interrupted)
            {
                var cfg = SearchRoomManager.Instance != null
                    ? SearchRoomManager.Instance.config : null;
                if (cfg != null) _cooldownUntil = Time.time + cfg.containerCooldown;
            }
        }

        /// <summary>取出一件道具，消耗一次产出额度。</summary>
        public ItemDefinition ExtractItem()
        {
            if (IsDepleted) return null;

            var mgr = SearchRoomManager.Instance;
            if (mgr == null || mgr.config == null) return null;

            remainingYield--;
            if (IsDepleted) ApplyDepletedLook();

            return mgr.config.RollItem(mgr.theme);
        }

        void ApplyDepletedLook()
        {
            if (highlightRenderer == null) return;
            var m = highlightRenderer.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", depletedColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", depletedColor);
        }

        void RestoreLook()
        {
            if (highlightRenderer == null || !_colorCached) return;
            var m = highlightRenderer.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", _baseColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", _baseColor);
        }

        public void ResetForNewRound()
        {
            var mgr = SearchRoomManager.Instance;
            remainingYield = mgr != null && mgr.config != null
                ? mgr.config.containerYield
                : _initialYield;

            _occupant = null;
            _cooldownUntil = 0f;
            RestoreLook();
            if (_bar != null) _bar.SetVisible(false);
        }

        void Update()
        {
            // 同步读条进度
            if (_bar != null && _occupant != null && _occupant.Search != null)
                _bar.SetFill(_occupant.Search.Progress01);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsDepleted
                ? Color.gray
                : (IsOccupied ? Color.yellow : new Color(0.3f, 0.9f, 0.5f));
            Gizmos.DrawWireSphere(InteractPoint, 0.35f);

            var cfg = SearchRoomManager.Instance != null ? SearchRoomManager.Instance.config : null;
            if (cfg != null)
            {
                Gizmos.color = new Color(0.3f, 0.9f, 0.5f, 0.15f);
                Gizmos.DrawWireSphere(InteractPoint, cfg.searchRange);
            }
        }
    }
}
