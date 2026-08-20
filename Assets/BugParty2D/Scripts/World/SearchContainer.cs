using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>可搜索容器。同一时间只允许一人搜（独占锁）。</summary>
    public class SearchContainer : MonoBehaviour
    {
        [Header("身份")]
        public string containerName = "抽屉";
        public Transform interactAnchor;

        [Tooltip("★放在高台上的容器。会在 HUD 提示需要跳上去")]
        public bool isElevated = false;

        [Header("产出")]
        public int remainingYield = 2;

        [Tooltip("稀有度加成")]
        [Range(0f, 2f)] public float rarityBonus = 0f;

        [Header("视觉")]
        public Renderer highlightRenderer;
        public Color depletedColor = new Color(0.32f, 0.32f, 0.34f);

        // ── 运行时 ─────────────────────────────────────
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
            if (highlightRenderer == null) highlightRenderer = GetComponentInChildren<Renderer>();
            CacheBaseColor();
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

        void Start()
        {
            _bar = FloatingBar.Create(transform, Vector3.up * (GetTopY() + 0.4f));
            if (_bar != null) _bar.SetVisible(false);
        }

        float GetTopY()
        {
            var r = GetComponentInChildren<Renderer>();
            return r != null ? r.bounds.extents.y : 0.8f;
        }

        void OnDestroy()
        {
            if (_bar != null) Destroy(_bar.gameObject);
        }

        public bool IsAvailableFor(PlayerActor asker)
        {
            if (IsDepleted || IsCoolingDown) return false;
            if (IsOccupied && _occupant != asker) return false;

            // ★脚下地板塌了的容器不能再搜
            var mgr = RoomManager.Instance;
            if (mgr != null && mgr.floorGrid != null && mgr.floorGrid.IsHoleAt(transform.position))
                return false;

            return true;
        }

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

        public void Release(PlayerActor actor, bool interrupted)
        {
            if (_occupant != actor) return;
            _occupant = null;
            if (_bar != null) _bar.SetVisible(false);

            if (interrupted)
            {
                var cfg = RoomManager.Instance != null ? RoomManager.Instance.config : null;
                if (cfg != null) _cooldownUntil = Time.time + cfg.containerCooldown;
            }
        }

        public ItemDefinition ExtractItem()
        {
            if (IsDepleted) return null;

            var mgr = RoomManager.Instance;
            if (mgr == null || mgr.config == null) return null;

            remainingYield--;
            if (IsDepleted) ApplyColor(depletedColor);

            var item = mgr.config.RollItem(mgr.theme);

            // 稀有度加成：高台容器更容易出好东西，作为跳跃的回报
            if (rarityBonus > 0f && Random.value < rarityBonus * 0.35f)
            {
                var pool = mgr.config.GetPool(mgr.theme);
                for (int i = 0; i < pool.Count; i++)
                    if (pool[i] != null && pool[i].isRare) { item = pool[i]; break; }
            }
            return item;
        }

        void ApplyColor(Color c)
        {
            if (highlightRenderer == null) return;
            var m = highlightRenderer.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public void ResetForNewRound()
        {
            var mgr = RoomManager.Instance;
            remainingYield = mgr != null && mgr.config != null
                ? mgr.config.containerYield : _initialYield;

            _occupant = null;
            _cooldownUntil = 0f;
            if (_colorCached) ApplyColor(_baseColor);
            if (_bar != null) _bar.SetVisible(false);
        }

        void Update()
        {
            if (_bar != null && _occupant != null && _occupant.Search != null)
                _bar.SetFill(_occupant.Search.Progress01);

            UpdateSearchShake();
        }

        // ══════════════════════════════════════════════
        //  被翻找时的晃动
        //  原先搜索期间容器一动不动，只有一根读条，看不出「正在被翻」。
        //  这里让容器自己抖起来 —— 视觉体在 ArtPivot/Art 子节点上，
        //  抖它不会影响碰撞盒（碰撞盒是父节点自己的 BoxCollider）。
        // ══════════════════════════════════════════════

        [Header("搜索晃动（表现）")]
        [Tooltip("被翻找时容器晃动幅度（米）。0 = 关闭")]
        public float shakeAmount = 0.035f;

        [Tooltip("晃动频率（次/秒）")]
        public float shakeSpeed = 13f;

        [Tooltip("被翻找时的倾斜角度，比纯位移更有「被扒开」的感觉")]
        public float shakeTilt = 2.2f;

        Transform _shakeTarget;
        Vector3 _shakeRestPos;
        Quaternion _shakeRestRot;
        bool _shakeRestCaptured;
        float _shakePhase;

        void UpdateSearchShake()
        {
            if (shakeAmount <= 0.0001f && shakeTilt <= 0.01f) return;

            if (!_shakeRestCaptured)
            {
                // 优先抖视觉子节点。美术模式下是 ArtPivot，占位模式下没有子节点就抖自己
                var pivot = transform.Find("ArtPivot");
                _shakeTarget = pivot != null ? pivot : transform;
                _shakeRestPos = _shakeTarget.localPosition;
                _shakeRestRot = _shakeTarget.localRotation;
                _shakeRestCaptured = true;
            }
            if (_shakeTarget == null) return;

            bool beingSearched = _occupant != null
                && _occupant.Search != null
                && _occupant.Search.IsSearching
                && _occupant.Search.CurrentTarget == this;

            if (beingSearched)
            {
                _shakePhase += Time.deltaTime * shakeSpeed;

                // 两个不同频率叠加，避免看起来像规律的机械振动
                float sx = Mathf.Sin(_shakePhase) * shakeAmount;
                float sz = Mathf.Sin(_shakePhase * 1.37f + 1.1f) * shakeAmount * 0.7f;
                float tilt = Mathf.Sin(_shakePhase * 0.83f) * shakeTilt;

                _shakeTarget.localPosition = _shakeRestPos + new Vector3(sx, 0f, sz);
                _shakeTarget.localRotation = _shakeRestRot * Quaternion.Euler(tilt, 0f, tilt * 0.6f);
            }
            else if (_shakePhase != 0f)
            {
                // 平滑归位，避免松手瞬间跳回
                _shakePhase = 0f;
                _shakeTarget.localPosition = _shakeRestPos;
                _shakeTarget.localRotation = _shakeRestRot;
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.color = IsDepleted ? Color.gray
                : (IsOccupied ? Color.yellow : new Color(0.3f, 0.9f, 0.5f));
            Gizmos.DrawWireSphere(InteractPoint, 0.3f);
        }
    }
}
