using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// AI 控制器。带视觉锥 + 听觉的简易 FPS Bot。
    ///
    /// 行为优先级：
    ///   1. 撤离窗口开启且时间紧迫 → 冲向撤离点
    ///   2. 视野内或最近听到噪音处有对手，且对手正在搜刮 → 绕后偷袭 ★最有戏
    ///   3. 视野内有对手 → 按攻击性决定打还是躲
    ///   4. 否则 → 找最近的未搜容器搜刮
    /// </summary>
    public class BotController : MonoBehaviour
    {
        enum BotState { SeekContainer, Searching, Looting, Hunt, Extract, Wander }

        [Header("性格")]
        [Tooltip("攻击倾向偏移，叠加在 RaidConfig.aiAggressiveness 上")]
        [Range(-0.5f, 0.5f)] public float aggressionBias = 0f;

        [Tooltip("决策随机抖动，让多个 Bot 不同步")]
        [Range(0f, 0.5f)] public float noise = 0.15f;

        [Tooltip("转身速度（度/秒）")]
        [Min(30f)] public float turnSpeed = 260f;

        PlayerRig _rig;
        RaidConfig _cfg;

        BotState _state = BotState.SeekContainer;
        LootContainer _targetContainer;
        PlayerRig _targetEnemy;
        Vector3 _moveGoal;
        Vector3 _lastHeardPos;
        float _lastHeardTime = -99f;

        float _nextDecision;
        float _stateEnterTime;
        float _lootPanelUntil;

        void Awake()
        {
            _rig = GetComponent<PlayerRig>();
        }

        void OnEnable()
        {
            RaidEvents.OnNoiseEmitted += HandleNoise;
        }

        void OnDisable()
        {
            RaidEvents.OnNoiseEmitted -= HandleNoise;
        }

        void Start()
        {
            _cfg = RaidManager.Instance != null ? RaidManager.Instance.config : null;
            _nextDecision = Time.time + Random.Range(0f, 0.5f);
            _moveGoal = transform.position;
        }

        // ── 听觉 ───────────────────────────────────────

        void HandleNoise(PlayerRig source, Vector3 pos, float radius)
        {
            if (source == null || source == _rig) return;
            if (!_rig.IsAlive) return;

            float dist = Vector3.Distance(transform.position, pos);
            if (dist > radius) return;

            // 听到了：记下位置，决策时会往那边走
            _lastHeardPos = pos;
            _lastHeardTime = Time.time;
        }

        bool HasFreshSound => Time.time - _lastHeardTime < 3.5f;

        // ── 视觉 ───────────────────────────────────────

        /// <summary>视野锥内能看到的最近对手。</summary>
        PlayerRig FindVisibleEnemy()
        {
            var mgr = RaidManager.Instance;
            if (mgr == null || _cfg == null) return null;

            PlayerRig best = null;
            float bestDist = _cfg.aiViewDistance;
            float cosLimit = Mathf.Cos(_cfg.aiViewAngle * 0.5f * Mathf.Deg2Rad);

            Vector3 eye = _rig.eyeAnchor != null ? _rig.eyeAnchor.position : transform.position;

            for (int i = 0; i < mgr.players.Count; i++)
            {
                var p = mgr.players[i];
                if (p == null || p == _rig || !p.IsAlive) continue;

                var to = p.transform.position - transform.position;
                float dist = to.magnitude;
                if (dist > bestDist) continue;

                var flat = to; flat.y = 0f;
                var fwd = transform.forward; fwd.y = 0f;
                if (flat.sqrMagnitude > 0.0001f && fwd.sqrMagnitude > 0.0001f)
                {
                    if (Vector3.Dot(fwd.normalized, flat.normalized) < cosLimit) continue;
                }

                // 视线遮挡检测。注意：射线起点在自己的碰撞体内部，
                // 必须逐个排除自己和目标的碰撞体，否则会误判为被遮挡
                var targetEye = p.eyeAnchor != null ? p.eyeAnchor.position : p.transform.position + Vector3.up;
                if (IsSightBlocked(eye, targetEye, p)) continue;

                bestDist = dist;
                best = p;
            }
            return best;
        }

        /// <summary>
        /// 判断两点之间是否有障碍物。会忽略自己与目标的碰撞体。
        /// </summary>
        bool IsSightBlocked(Vector3 from, Vector3 to, PlayerRig target)
        {
            var dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.01f) return false;

            var hits = Physics.RaycastAll(from, dir.normalized, dist);
            if (hits == null || hits.Length == 0) return false;

            for (int i = 0; i < hits.Length; i++)
            {
                var owner = hits[i].collider.GetComponentInParent<PlayerRig>();
                // 命中自己或目标本人都不算遮挡
                if (owner == _rig || owner == target) continue;
                // 命中掉落物不算遮挡
                if (hits[i].collider.GetComponentInParent<DroppedLoot>() != null) continue;
                return true;
            }
            return false;
        }

        // ── 主循环 ─────────────────────────────────────

        void Update()
        {
            var mgr = RaidManager.Instance;
            if (mgr == null || _cfg == null || !_rig.IsAlive)
            {
                _rig.MoveInput = Vector2.zero;
                return;
            }

            if (!mgr.CanAct)
            {
                _rig.MoveInput = Vector2.zero;
                _rig.WantSprint = _rig.WantCrouch = false;
                return;
            }

            if (Time.time >= _nextDecision)
            {
                Decide(mgr);
                _nextDecision = Time.time + _cfg.aiDecisionInterval + Random.Range(0f, noise * 0.6f);
            }
            Act(mgr);
        }

        // ── 决策 ───────────────────────────────────────

        void Decide(RaidManager mgr)
        {
            // ① 撤离优先：窗口已开，或时间快到了
            if (mgr.Phase == RoundPhase.Extraction
                || (mgr.Phase == RoundPhase.Looting && mgr.TimeLeft < _cfg.aiExtractPanicTime && !_rig.Inventory.IsEmpty))
            {
                EnterState(BotState.Extract);
                return;
            }

            // 界面开着：给自己一点时间挑东西
            if (_rig.Loot != null && _rig.Loot.IsPanelOpen)
            {
                _state = BotState.Looting;
                return;
            }

            // ② 看到对手
            var enemy = FindVisibleEnemy();
            if (enemy != null)
            {
                float weight = _cfg.aiAggressiveness + aggressionBias;

                // ★对手正在搜刮 → 极佳的偷袭时机
                bool enemyBusy = enemy.Loot != null
                                 && (enemy.Loot.IsPanelOpen || enemy.Loot.IsSearching);
                if (enemyBusy) weight += _cfg.aiLootingTargetBonus;

                // 对手身上有货 → 更值得打
                if (!enemy.Inventory.IsEmpty) weight += 0.15f;

                if (Roll(weight))
                {
                    _targetEnemy = enemy;
                    EnterState(BotState.Hunt);
                    return;
                }
            }

            // ③ 背包已满、没容器可搜时，听到声音就去搞事
            var nextContainer = mgr.FindNearestSearchableContainer(transform.position, _rig);
            bool nothingToLoot = nextContainer == null;

            if (HasFreshSound && nothingToLoot && Roll(0.6f))
            {
                _moveGoal = _lastHeardPos;
                EnterState(BotState.Wander);
                return;
            }

            // ④ 找容器搜刮
            if (nextContainer != null)
            {
                _targetContainer = nextContainer;
                EnterState(BotState.SeekContainer);
                return;
            }

            // ⑤ 没容器可搜：去听到声音的地方，或随机游荡
            if (HasFreshSound) _moveGoal = _lastHeardPos;
            else PickWanderGoal(mgr);
            EnterState(BotState.Wander);
        }

        bool Roll(float chance)
            => Random.value < Mathf.Clamp01(chance + Random.Range(-noise, noise));

        void EnterState(BotState s)
        {
            if (_state != s) _stateEnterTime = Time.time;
            _state = s;
        }

        void PickWanderGoal(RaidManager mgr)
        {
            if (mgr.containers.Count > 0)
            {
                var c = mgr.containers[Random.Range(0, mgr.containers.Count)];
                if (c != null)
                {
                    _moveGoal = c.InteractPoint + new Vector3(
                        Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
                    return;
                }
            }
            _moveGoal = transform.position + new Vector3(
                Random.Range(-5f, 5f), 0f, Random.Range(-5f, 5f));
        }

        // ── 执行 ───────────────────────────────────────

        void Act(RaidManager mgr)
        {
            switch (_state)
            {
                case BotState.SeekContainer: ActSeek(mgr); break;
                case BotState.Searching:     ActSearching(); break;
                case BotState.Looting:       ActLooting(); break;
                case BotState.Hunt:          ActHunt(); break;
                case BotState.Extract:       ActExtract(mgr); break;
                case BotState.Wander:        ActWander(mgr); break;
            }
        }

        void ActSeek(RaidManager mgr)
        {
            if (_targetContainer == null || !_targetContainer.IsAvailableFor(_rig))
            {
                PickWanderGoal(mgr);
                EnterState(BotState.Wander);
                return;
            }

            var to = _targetContainer.InteractPoint - transform.position;
            to.y = 0f;

            if (to.magnitude <= _cfg.interactDistance * 0.75f)
            {
                _rig.MoveInput = Vector2.zero;
                _rig.WantSprint = false;
                FaceTowards(_targetContainer.InteractPoint);

                if (_rig.Loot.TryBeginSearch(_targetContainer))
                    EnterState(BotState.Searching);
                else
                {
                    PickWanderGoal(mgr);
                    EnterState(BotState.Wander);
                }
                return;
            }

            MoveTowards(_targetContainer.InteractPoint, true);

            if (Time.time - _stateEnterTime > 6f)
            {
                PickWanderGoal(mgr);
                EnterState(BotState.Wander);
            }
        }

        void ActSearching()
        {
            _rig.MoveInput = Vector2.zero;
            _rig.WantSprint = false;

            if (_rig.Loot.IsPanelOpen)
            {
                // 读条完成，界面打开了，给自己一段时间挑东西
                _lootPanelUntil = Time.time + Random.Range(1.2f, 2.2f);
                EnterState(BotState.Looting);
                return;
            }

            if (!_rig.Loot.IsSearching)
                EnterState(BotState.Wander);
        }

        void ActLooting()
        {
            _rig.MoveInput = Vector2.zero;
            _rig.WantSprint = false;

            if (!_rig.Loot.IsPanelOpen)
            {
                EnterState(BotState.SeekContainer);
                return;
            }

            // 模拟「挑东西」的思考时间，然后一把拿走
            if (Time.time >= _lootPanelUntil)
            {
                _rig.Loot.TakeAllPossible();
                _rig.Loot.ClosePanel();
                EnterState(BotState.SeekContainer);
            }
        }

        void ActHunt()
        {
            if (_targetEnemy == null || !_targetEnemy.IsAlive)
            {
                EnterState(BotState.Wander);
                return;
            }

            var to = _targetEnemy.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;

            FaceTowards(_targetEnemy.transform.position);

            if (dist <= _cfg.meleeRange * 0.8f)
            {
                _rig.MoveInput = Vector2.zero;
                _rig.WantSprint = false;

                // 朝向对上了才挥，避免打空
                var fwd = transform.forward; fwd.y = 0f;
                if (to.sqrMagnitude > 0.0001f && Vector3.Dot(fwd.normalized, to.normalized) > 0.8f)
                    _rig.Melee.TrySwing();
            }
            else
            {
                // 对手在忙 → 蹲着摸过去，避免被听见 ★偷袭
                bool enemyBusy = _targetEnemy.Loot != null
                                 && (_targetEnemy.Loot.IsPanelOpen || _targetEnemy.Loot.IsSearching);

                if (enemyBusy && dist < 7f)
                {
                    _rig.WantCrouch = true;
                    _rig.WantSprint = false;
                    MoveTowards(_targetEnemy.transform.position, false);
                }
                else
                {
                    _rig.WantCrouch = false;
                    MoveTowards(_targetEnemy.transform.position, dist > 4f);
                }
            }

            if (Time.time - _stateEnterTime > 5f)
            {
                _rig.WantCrouch = false;
                EnterState(BotState.Wander);
            }
        }

        void ActExtract(RaidManager mgr)
        {
            var zone = mgr.FindNearestExtractionZone(transform.position);
            if (zone == null)
            {
                EnterState(BotState.Wander);
                return;
            }

            _rig.WantCrouch = false;

            var to = zone.transform.position - transform.position;
            to.y = 0f;

            if (to.magnitude < 1.0f)
            {
                // 已经在点里了，站住别动等进度条
                _rig.MoveInput = Vector2.zero;
                _rig.WantSprint = false;
                return;
            }

            MoveTowards(zone.transform.position, true);
        }

        void ActWander(RaidManager mgr)
        {
            _rig.WantCrouch = false;

            var to = _moveGoal - transform.position;
            to.y = 0f;

            if (to.magnitude < 0.9f)
            {
                PickWanderGoal(mgr);
                return;
            }

            FaceTowards(_moveGoal);
            MoveTowards(_moveGoal, false);
        }

        // ── 移动辅助 ───────────────────────────────────

        void MoveTowards(Vector3 worldPos, bool sprint)
        {
            var to = worldPos - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f)
            {
                _rig.MoveInput = Vector2.zero;
                return;
            }

            FaceTowards(worldPos);

            // 输入是本地空间，转向后直接往前走
            _rig.MoveInput = new Vector2(0f, 1f);
            _rig.WantSprint = sprint;
        }

        void FaceTowards(Vector3 worldPos)
        {
            var to = worldPos - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return;

            var look = Quaternion.LookRotation(to.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        void OnDrawGizmosSelected()
        {
            if (_cfg == null) return;

            // 视野锥
            Gizmos.color = new Color(1f, 1f, 0.2f, 0.4f);
            var fwd = transform.forward;
            var l = Quaternion.Euler(0f, -_cfg.aiViewAngle * 0.5f, 0f) * fwd;
            var r = Quaternion.Euler(0f, _cfg.aiViewAngle * 0.5f, 0f) * fwd;
            var eye = transform.position + Vector3.up * 1.5f;
            Gizmos.DrawLine(eye, eye + l * _cfg.aiViewDistance);
            Gizmos.DrawLine(eye, eye + r * _cfg.aiViewDistance);

            // 最近听到的位置
            if (HasFreshSound)
            {
                Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.7f);
                Gizmos.DrawWireSphere(_lastHeardPos, 0.6f);
                Gizmos.DrawLine(eye, _lastHeardPos);
            }
        }
    }
}
