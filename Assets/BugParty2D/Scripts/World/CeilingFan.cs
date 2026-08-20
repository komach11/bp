using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 天花板吊扇。持续旋转，并随房间的紧张状态加速。
    ///
    /// 为什么不用 BugAmbience：
    ///   BugAmbience 的 driftSpin 会连同上下浮动与位置抖动一起作用，
    ///   吊扇是固定在天花板上的，只该转、不该飘。
    ///
    /// 俯视视角下的注意事项：
    ///   转速过快会与帧率产生频闪（看起来像倒转或卡顿），
    ///   真实吊扇约 6~9 秒一圈，本组件默认 50 度/秒（7.2 秒一圈）。
    /// </summary>
    public class CeilingFan : MonoBehaviour
    {
        [Header("旋转")]
        [Tooltip("常态转速（度/秒）。50 ≈ 7.2 秒一圈，接近真实吊扇的慵懒感。\n" +
                 "★不建议超过 180：俯视下叶片会与帧率产生频闪，看着像倒转。")]
        [Range(5f, 180f)] public float spinSpeed = 50f;

        [Tooltip("紧张状态（剩余时间不足）时的转速倍率")]
        [Range(1f, 6f)] public float urgentMultiplier = 2.6f;

        [Tooltip("转速变化的平滑时间，避免瞬间变速显得突兀")]
        [Min(0.05f)] public float speedSmooth = 1.2f;

        [Tooltip("旋转轴。吊扇绕自身 Y 轴转")]
        public Vector3 axis = Vector3.up;

        [Header("故障感")]
        [Tooltip("★偶尔卡顿一下，呼应 Bug 主题。0 = 关闭")]
        [Range(0f, 1f)] public float stutterChance = 0.35f;

        [Tooltip("平均多久检定一次卡顿")]
        [Min(0.5f)] public float stutterInterval = 4f;

        [Tooltip("单次卡顿持续多久")]
        [Min(0.02f)] public float stutterDuration = 0.22f;

        [Header("引用")]
        [Tooltip("要旋转的节点。留空则转自己。\n" +
                 "用美术模型时应指向叶片所在的子节点，避免连吊杆一起转。")]
        public Transform spinTarget;

        // ── 运行时 ─────────────────────────────────────
        float _currentSpeed;
        float _targetSpeed;
        float _nextStutterCheck;
        float _stutterEnd;
        bool _urgent;

        void Awake()
        {
            if (spinTarget == null) spinTarget = transform;
            _currentSpeed = spinSpeed;
            _targetSpeed = spinSpeed;
        }

        void OnEnable()
        {
            RoomEvents.OnPhaseChanged += HandlePhase;
        }

        void OnDisable()
        {
            RoomEvents.OnPhaseChanged -= HandlePhase;
        }

        void Start()
        {
            _nextStutterCheck = Time.time + Random.Range(0f, stutterInterval);
        }

        void HandlePhase(RoundPhase phase)
        {
            // 塌陷阶段疯转，制造"系统失控"的观感
            if (phase == RoundPhase.Collapse)
            {
                _urgent = true;
                _targetSpeed = spinSpeed * urgentMultiplier * 1.8f;
            }
            else if (phase == RoundPhase.Searching)
            {
                _urgent = false;
                _targetSpeed = spinSpeed;
            }
        }

        void Update()
        {
            // 搜索阶段末期逐渐加速，与警报加剧同步
            if (!_urgent) UpdateUrgency();

            _currentSpeed = Mathf.Lerp(_currentSpeed, _targetSpeed,
                Time.deltaTime / Mathf.Max(0.05f, speedSmooth));

            // ── 故障卡顿 ──
            if (stutterChance > 0f)
            {
                if (Time.time >= _nextStutterCheck)
                {
                    _nextStutterCheck = Time.time + stutterInterval * Random.Range(0.6f, 1.5f);
                    if (Random.value < stutterChance)
                        _stutterEnd = Time.time + stutterDuration;
                }
            }

            float speed = _currentSpeed;
            if (Time.time < _stutterEnd) speed = 0f;   // 卡住不转

            if (spinTarget != null && Mathf.Abs(speed) > 0.01f)
                spinTarget.Rotate(axis.normalized, speed * Time.deltaTime, Space.Self);
        }

        /// <summary>搜索阶段剩余时间不足时逐渐提速。</summary>
        void UpdateUrgency()
        {
            var mgr = RoomManager.Instance;
            if (mgr == null || mgr.config == null) return;
            if (mgr.Phase != RoundPhase.Searching) return;

            float threshold = mgr.config.urgentThreshold;
            if (threshold <= 0f) return;

            if (mgr.TimeLeft <= threshold)
            {
                float k = 1f - Mathf.Clamp01(mgr.TimeLeft / threshold);
                _targetSpeed = Mathf.Lerp(spinSpeed, spinSpeed * urgentMultiplier, k);
            }
            else
            {
                _targetSpeed = spinSpeed;
            }
        }
    }
}
