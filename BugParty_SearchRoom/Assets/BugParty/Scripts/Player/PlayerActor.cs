using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 玩家角色主体。持有移动、搜索、肘击与背包。
    /// 真人和 AI 共用这一个类，区别只在挂哪个 Brain。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(PlayerInventory))]
    [RequireComponent(typeof(SearchAbility))]
    [RequireComponent(typeof(ElbowAbility))]
    public class PlayerActor : MonoBehaviour
    {
        [Header("身份")]
        public PlayerColor playerColor = PlayerColor.Red;

        [Tooltip("显示用名字，留空则自动用颜色命名")]
        public string displayName = "";

        [Header("视觉引用（建场工具会自动填）")]
        [Tooltip("躯干渲染器，用于染成阵营色")]
        public Renderer bodyRenderer;

        [Tooltip("手持道具的挂点")]
        public Transform handAnchor;

        [Tooltip("肘击判定的起点，通常在胸口高度")]
        public Transform elbowOrigin;

        // ── 组件 ───────────────────────────────────────────
        public PlayerInventory Inventory { get; private set; }
        public SearchAbility Search { get; private set; }
        public ElbowAbility Elbow { get; private set; }
        Rigidbody _rb;

        SearchRoomConfig _cfg;
        Vector3 _spawnPos;
        Quaternion _spawnRot;

        // ── 状态 ───────────────────────────────────────────
        float _staggerUntil;
        bool _teleportedOut;

        public bool IsStaggered => Time.time < _staggerUntil;
        public bool IsAlive => !_teleportedOut && gameObject.activeInHierarchy;
        public bool IsSearching => Search != null && Search.IsSearching;

        /// <summary>本帧的移动输入，由 Brain 写入。范围 -1~1 的 XZ 向量。</summary>
        public Vector2 MoveInput { get; set; }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            // Unity 2022 用 drag；如升级到 6.0 需改为 linearDamping
            _rb.drag = 6f;
            _rb.mass = 1.2f;

            Inventory = GetComponent<PlayerInventory>();
            Search = GetComponent<SearchAbility>();
            Elbow = GetComponent<ElbowAbility>();

            _spawnPos = transform.position;
            _spawnRot = transform.rotation;

            if (string.IsNullOrEmpty(displayName))
                displayName = playerColor.ToLabel() + "方";
        }

        void Start()
        {
            _cfg = SearchRoomManager.Instance != null ? SearchRoomManager.Instance.config : null;
            if (_cfg == null)
            {
                Debug.LogError($"[{displayName}] 找不到 SearchRoomConfig。", this);
                enabled = false;
                return;
            }

            Inventory.Init(this, _cfg.inventoryCapacity);
            if (Search != null) Search.Init(this, _cfg);
            if (Elbow != null) Elbow.Init(this, _cfg);

            ApplyTeamColor();
        }

        /// <summary>把躯干染成阵营色。</summary>
        public void ApplyTeamColor()
        {
            if (bodyRenderer == null) return;
            var c = playerColor.ToColor();

            // 用实例材质避免污染共享材质
            var mat = bodyRenderer.material;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c); // URP
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);         // Built-in
        }

        void FixedUpdate()
        {
            if (!IsAlive) return;

            bool canMove = SearchRoomManager.Instance != null
                           && SearchRoomManager.Instance.CanAct
                           && !IsStaggered
                           && !IsSearching;

            if (!canMove)
            {
                MoveInput = Vector2.zero;
                return;
            }

            var dir = new Vector3(MoveInput.x, 0f, MoveInput.y);
            if (dir.sqrMagnitude > 1f) dir.Normalize();

            if (dir.sqrMagnitude > 0.0001f)
            {
                // 位移
                var target = _rb.position + dir * _cfg.moveSpeed * Time.fixedDeltaTime;
                _rb.MovePosition(target);

                // 转向
                var look = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, look, _cfg.turnSpeed * Time.fixedDeltaTime);
            }
        }

        /// <summary>受到肘击：进入硬直并被击退。</summary>
        public void ReceiveElbow(PlayerActor attacker, Vector3 knockDir, float force, float staggerTime)
        {
            if (!IsAlive) return;

            _staggerUntil = Time.time + staggerTime;
            AbortSearch();

            knockDir.y = 0f;
            if (knockDir.sqrMagnitude < 0.0001f) knockDir = -transform.forward;
            _rb.AddForce(knockDir.normalized * force, ForceMode.Impulse);
        }

        /// <summary>中断当前搜索进程。</summary>
        public void AbortSearch()
        {
            if (Search != null) Search.Cancel(true);
        }

        /// <summary>丢出一件道具到地上（被肘击打落时调用）。</summary>
        public void DropLatestItem(Vector3 popDir)
        {
            if (_cfg == null) return;

            var item = Inventory.PopLatest();
            if (item == null) return;

            var origin = handAnchor != null ? handAnchor.position : transform.position + Vector3.up * 1f;
            WorldItem.SpawnDropped(item, origin, popDir, _cfg);
            SearchRoomEvents.RaiseItemKnockedOut(this, item);
        }

        public void ResetForNewRound()
        {
            _teleportedOut = false;
            _staggerUntil = 0f;
            MoveInput = Vector2.zero;

            transform.SetPositionAndRotation(_spawnPos, _spawnRot);
            _rb.velocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            Inventory.Clear();
            AbortSearch();

            if (!gameObject.activeSelf) gameObject.SetActive(true);
            var vis = transform.Find("Visual");
            if (vis != null) vis.gameObject.SetActive(true);
        }

        /// <summary>传送离场：隐藏视觉体并停止响应。</summary>
        public void PlayTeleportOut()
        {
            _teleportedOut = true;
            MoveInput = Vector2.zero;
            _rb.velocity = Vector3.zero;

            var vis = transform.Find("Visual");
            if (vis != null) vis.gameObject.SetActive(false);
            else gameObject.SetActive(false);
        }
    }
}
