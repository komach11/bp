using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 玩家主体。第一人称移动、姿态切换、噪音发出、受击反馈。
    /// 使用 CharacterController 而非 Rigidbody——FPS 手感更可控、不会被物理推走。
    ///
    /// 真人与 AI 共用本类，区别只在挂 HumanController 还是 BotController。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(GridInventory))]
    [RequireComponent(typeof(LootAction))]
    [RequireComponent(typeof(MeleeAction))]
    public class PlayerRig : MonoBehaviour
    {
        [Header("身份")]
        public PlayerColor playerColor = PlayerColor.Red;
        public string displayName = "";

        [Tooltip("勾选表示这是本地真人玩家，会启用第一人称相机与 HUD")]
        public bool isLocalPlayer = false;

        [Header("引用（建场工具自动填）")]
        [Tooltip("相机挂点，位于眼部高度")]
        public Transform eyeAnchor;

        [Tooltip("第三人称可见的身体渲染器，本地玩家会隐藏它避免遮挡视线")]
        public Renderer bodyRenderer;

        [Tooltip("身体视觉根节点，本地玩家会关掉")]
        public GameObject visualRoot;

        // ── 组件 ───────────────────────────────────────
        public GridInventory Inventory { get; private set; }
        public LootAction Loot { get; private set; }
        public MeleeAction Melee { get; private set; }
        public CharacterController Controller { get; private set; }

        RaidConfig _cfg;
        Vector3 _spawnPos;
        Quaternion _spawnRot;

        // ── 运行时状态 ─────────────────────────────────
        Vector3 _velocity;              // 水平速度（平滑后）
        Vector3 _velocitySmoothing;
        float _verticalVelocity;
        Stance _stance = Stance.Stand;
        float _currentEyeHeight;
        float _staggerUntil;
        float _noiseTimer;
        float _lastNoiseRadius;

        public Stance CurrentStance => _stance;
        public bool IsStaggered => Time.time < _staggerUntil;
        public bool IsGrounded => Controller != null && Controller.isGrounded;
        public bool IsAlive { get; private set; } = true;
        public ExtractResult Result { get; set; } = ExtractResult.InRaid;

        /// <summary>本帧水平移动的实际速率，用于头部摆动与噪音计算。</summary>
        public float HorizontalSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;

        /// <summary>当前发出的噪音半径，AI 听觉用。</summary>
        public float CurrentNoiseRadius => _lastNoiseRadius;

        // ── 由控制器写入的输入 ─────────────────────────

        /// <summary>移动输入，-1~1 的本地空间 XZ。</summary>
        public Vector2 MoveInput { get; set; }

        /// <summary>是否按住疾跑。</summary>
        public bool WantSprint { get; set; }

        /// <summary>是否按住下蹲。</summary>
        public bool WantCrouch { get; set; }

        /// <summary>本帧是否请求跳跃。</summary>
        public bool WantJump { get; set; }

        void Awake()
        {
            Controller = GetComponent<CharacterController>();
            Inventory = GetComponent<GridInventory>();
            Loot = GetComponent<LootAction>();
            Melee = GetComponent<MeleeAction>();

            _spawnPos = transform.position;
            _spawnRot = transform.rotation;

            if (string.IsNullOrEmpty(displayName))
                displayName = playerColor.ToLabel() + "方";
        }

        void Start()
        {
            _cfg = RaidManager.Instance != null ? RaidManager.Instance.config : null;
            if (_cfg == null)
            {
                Debug.LogError($"[{displayName}] 找不到 RaidConfig，无法运行。", this);
                enabled = false;
                return;
            }

            Inventory.Init(this, _cfg);
            Loot.Init(this, _cfg);
            Melee.Init(this, _cfg);

            _currentEyeHeight = _cfg.standEyeHeight;
            ApplyTeamColor();

            // 本地玩家隐藏自己的身体，避免第一人称下遮挡视线
            if (isLocalPlayer && visualRoot != null)
                visualRoot.SetActive(false);
        }

        public void ApplyTeamColor()
        {
            if (bodyRenderer == null) return;
            var c = playerColor.ToColor();
            var mat = bodyRenderer.material;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }

        void Update()
        {
            if (!IsAlive || _cfg == null) return;

            UpdateStance();
            UpdateMovement();
            UpdateEyeHeight();
            UpdateNoise();
        }

        // ── 姿态 ───────────────────────────────────────

        void UpdateStance()
        {
            // 搜刮时强制站立不动
            if (Loot != null && Loot.IsPanelOpen && _cfg.lockMovementWhileLooting)
            {
                _stance = Stance.Stand;
                return;
            }

            if (WantCrouch)
                _stance = Stance.Crouch;
            else if (WantSprint && MoveInput.sqrMagnitude > 0.1f && !IsStaggered)
                _stance = Stance.Sprint;
            else
            {
                // 想站起来但头顶有东西 → 保持蹲姿，避免卡进天花板/桌底
                if (_stance == Stance.Crouch && IsBlockedAbove())
                    _stance = Stance.Crouch;
                else
                    _stance = Stance.Stand;
            }
        }

        /// <summary>头顶是否有障碍物，用于判断能否站起。</summary>
        bool IsBlockedAbove()
        {
            if (Controller == null) return false;

            // 从当前胶囊顶部往上探测站立所需的额外高度
            float need = 1.8f - Controller.height;
            if (need <= 0.02f) return false;

            Vector3 top = transform.position + Vector3.up * Controller.height;
            var hits = Physics.SphereCastAll(
                top, Controller.radius * 0.9f, Vector3.up, need + 0.05f);

            for (int i = 0; i < hits.Length; i++)
            {
                // 忽略自己和掉落物
                if (hits[i].collider.GetComponentInParent<PlayerRig>() == this) continue;
                if (hits[i].collider.GetComponentInParent<DroppedLoot>() != null) continue;
                return true;
            }
            return false;
        }

        void UpdateEyeHeight()
        {
            if (eyeAnchor == null) return;

            float target = _stance == Stance.Crouch ? _cfg.crouchEyeHeight : _cfg.standEyeHeight;
            _currentEyeHeight = Mathf.Lerp(
                _currentEyeHeight, target, Time.deltaTime * _cfg.stanceLerpSpeed);

            var lp = eyeAnchor.localPosition;
            lp.y = _currentEyeHeight;
            eyeAnchor.localPosition = lp;

            // 碰撞体高度同步，蹲下能钻过低矮空间
            if (Controller != null)
            {
                float h = _stance == Stance.Crouch ? 1.15f : 1.8f;
                Controller.height = Mathf.Lerp(Controller.height, h, Time.deltaTime * _cfg.stanceLerpSpeed);
                Controller.center = new Vector3(0f, Controller.height * 0.5f, 0f);
            }
        }

        // ── 移动 ───────────────────────────────────────

        void UpdateMovement()
        {
            bool canMove = CanMove();

            Vector3 targetVel = Vector3.zero;
            if (canMove)
            {
                var dir = new Vector3(MoveInput.x, 0f, MoveInput.y);
                if (dir.sqrMagnitude > 1f) dir.Normalize();
                // 输入是本地空间，转到世界空间（跟随视角朝向）
                dir = transform.TransformDirection(dir);
                targetVel = dir * _cfg.GetSpeed(_stance);
            }

            _velocity = Vector3.SmoothDamp(
                _velocity, targetVel, ref _velocitySmoothing, _cfg.moveSmoothing);

            // 重力与跳跃
            if (Controller.isGrounded)
            {
                if (_verticalVelocity < 0f) _verticalVelocity = -1f;
                if (WantJump && canMove && _stance != Stance.Crouch)
                {
                    _verticalVelocity = Mathf.Sqrt(2f * _cfg.gravity * _cfg.jumpHeight);
                    EmitNoise(_cfg.walkNoiseRadius * 1.3f);
                }
            }
            else
            {
                _verticalVelocity -= _cfg.gravity * Time.deltaTime;
            }
            WantJump = false;

            var motion = _velocity;
            motion.y = _verticalVelocity;
            Controller.Move(motion * Time.deltaTime);
        }

        bool CanMove()
        {
            if (IsStaggered) return false;
            if (!IsAlive) return false;

            var mgr = RaidManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;

            // 搜刮界面开着时锁定移动——这是紧张感的来源
            if (_cfg.lockMovementWhileLooting && Loot != null && Loot.IsPanelOpen)
                return false;

            return true;
        }

        // ── 噪音 ───────────────────────────────────────

        void UpdateNoise()
        {
            // 移动中持续发出噪音
            if (HorizontalSpeed > 0.35f)
            {
                _noiseTimer -= Time.deltaTime;
                if (_noiseTimer <= 0f)
                {
                    EmitNoise(_cfg.GetNoiseRadius(_stance));
                    // 走得越快，脚步越密
                    _noiseTimer = _stance == Stance.Sprint ? 0.28f
                                : _stance == Stance.Crouch ? 0.75f : 0.45f;
                }
            }

            // 噪音衰减
            if (_lastNoiseRadius > 0f)
            {
                _lastNoiseRadius -= _cfg.GetNoiseRadius(Stance.Sprint) / Mathf.Max(0.05f, _cfg.noiseDecay) * Time.deltaTime;
                if (_lastNoiseRadius < 0f) _lastNoiseRadius = 0f;
            }
        }

        /// <summary>发出一次噪音。AI 听觉与音效系统会收到事件。</summary>
        public void EmitNoise(float radius)
        {
            if (radius <= 0f) return;
            _lastNoiseRadius = Mathf.Max(_lastNoiseRadius, radius);
            RaidEvents.RaiseNoise(this, transform.position, radius);
        }

        // ── 受击 ───────────────────────────────────────

        /// <summary>
        /// 受到近战攻击。isBackstab 为 true 时损失更惨重。
        /// </summary>
        public void ReceiveMelee(PlayerRig attacker, Vector3 knockDir, bool isBackstab)
        {
            if (!IsAlive || _cfg == null) return;

            float stagger = _cfg.staggerDuration * (isBackstab ? _cfg.backstabMultiplier : 1f);
            _staggerUntil = Time.time + stagger;

            // 强制关掉搜刮界面并中断读条
            if (Loot != null) Loot.ForceAbort();

            // 击退：直接给水平速度，CharacterController 下最可靠
            knockDir.y = 0f;
            if (knockDir.sqrMagnitude < 0.0001f) knockDir = -transform.forward;
            _velocity = knockDir.normalized * _cfg.knockbackForce;

            // 掉落战利品
            int drops = isBackstab ? _cfg.itemsKnockedOnBackstab : _cfg.itemsKnockedPerHit;
            for (int i = 0; i < drops; i++)
            {
                // 背刺优先打掉最值钱的，普通命中打掉最新拿到的
                var item = isBackstab ? Inventory.PopMostValuable() : Inventory.PopLatest();
                if (item == null) break;

                var popDir = knockDir.normalized + Vector3.up * 1.5f
                             + new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.4f, 0.4f));
                var origin = eyeAnchor != null
                    ? eyeAnchor.position
                    : transform.position + Vector3.up * 1.2f;

                DroppedLoot.Spawn(item, origin, popDir, _cfg);
                RaidEvents.RaiseLootDropped(this, item);
            }

            // 本地玩家受击时晃动视角
            if (isLocalPlayer)
            {
                var look = GetComponent<FirstPersonLook>();
                if (look != null)
                    look.AddShake(_cfg.hitCameraShake * (isBackstab ? 1.6f : 1f));
            }
        }

        // ── 回合控制 ───────────────────────────────────

        public void ResetForNewRound()
        {
            IsAlive = true;
            Result = ExtractResult.InRaid;
            _staggerUntil = 0f;
            _velocity = Vector3.zero;
            _velocitySmoothing = Vector3.zero;
            _verticalVelocity = 0f;
            _stance = Stance.Stand;
            MoveInput = Vector2.zero;
            WantSprint = WantCrouch = WantJump = false;
            _lastNoiseRadius = 0f;

            if (Controller != null)
            {
                Controller.enabled = false;
                transform.SetPositionAndRotation(_spawnPos, _spawnRot);
                Controller.enabled = true;
            }

            Inventory.Clear();
            if (Loot != null) Loot.ForceAbort();

            if (!isLocalPlayer && visualRoot != null) visualRoot.SetActive(true);
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        /// <summary>撤离成功：离场并保留战利品。</summary>
        public void OnExtractSuccess()
        {
            Result = ExtractResult.Extracted;
            IsAlive = false;
            MoveInput = Vector2.zero;
            _velocity = Vector3.zero;

            RaidEvents.RaiseExtracted(this, Inventory.TotalValue);

            // 非本地玩家直接隐身；本地玩家保留相机以便看结算
            if (!isLocalPlayer && visualRoot != null) visualRoot.SetActive(false);
        }

        /// <summary>撤离失败：战利品全部作废。</summary>
        public void OnExtractFail()
        {
            if (Result != ExtractResult.InRaid) return;

            Result = ExtractResult.Failed;
            int lost = Inventory.TotalValue;
            Inventory.Clear();
            RaidEvents.RaiseExtractFailed(this, lost);
        }
    }
}
