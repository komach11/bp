using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// AI 控制器。行为很简单但足够产生节目效果：
    /// 找最近的容器去搜 → 搜到就换下一个 → 路上遇到对手就有概率肘击。
    /// 关键设定：AI 会优先攻击"正在搜索的对手"，因为那样最容易打断并抢到便宜。
    /// </summary>
    public class AIBrain : PlayerBrain
    {
        enum AIState { SeekContainer, Searching, Chase, Wander }

        [Header("性格微调（在 Config 基础上叠加）")]
        [Tooltip("攻击倾向的个体偏移。红方可以调高，绿方调低")]
        [Range(-0.5f, 0.5f)] public float aggressionBias = 0f;

        [Tooltip("每次决策时的随机抖动，让四个 AI 不完全同步")]
        [Range(0f, 0.5f)] public float noise = 0.15f;

        AIState _state = AIState.SeekContainer;
        SearchContainer _targetContainer;
        PlayerActor _targetOpponent;

        float _nextDecisionTime;
        float _stateEnterTime;
        Vector3 _wanderPoint;

        protected override void Start()
        {
            base.Start();
            // 错开首次决策时间，避免四个 AI 同帧决策
            _nextDecisionTime = Time.time + Random.Range(0f, 0.4f);
            PickWanderPoint();
        }

        protected override void Think()
        {
            if (Time.time >= _nextDecisionTime)
            {
                Decide();
                float interval = Cfg != null ? Cfg.aiDecisionInterval : 0.35f;
                _nextDecisionTime = Time.time + interval + Random.Range(0f, noise * 0.5f);
            }
            Act();
        }

        // ── 决策 ───────────────────────────────────────────

        void Decide()
        {
            if (Cfg == null) return;

            var mgr = SearchRoomManager.Instance;
            if (mgr == null) return;

            // 背包满了：改为纯攻击模式，专门去打别人（很欠揍，但很好玩）
            if (Actor.Inventory.IsFull)
            {
                var prey = mgr.FindNearestOpponent(Actor, Cfg.aiAggroRange * 2f);
                if (prey != null) { EnterChase(prey); return; }
                EnterWander();
                return;
            }

            // 正在搜索：只有对手贴得很近才考虑放弃去打人
            if (Actor.Search.IsSearching)
            {
                _state = AIState.Searching;
                var threat = mgr.FindNearestOpponent(Actor, Cfg.elbowRange);
                if (threat != null && Roll(Cfg.aiAggressiveness * 0.5f))
                {
                    EnterChase(threat);
                }
                return;
            }

            // 附近有对手：按攻击性决定是打人还是继续搜
            var opponent = mgr.FindNearestOpponent(Actor, Cfg.aiAggroRange);
            if (opponent != null)
            {
                // 正在搜索的对手是最优目标——打断他能同时抢时间和打掉道具
                float weight = Cfg.aiAggressiveness + aggressionBias;
                if (opponent.IsSearching) weight += 0.35f;
                if (!opponent.Inventory.IsEmpty) weight += 0.15f;

                if (Roll(weight)) { EnterChase(opponent); return; }
            }

            // 默认：去找最近的可搜容器
            var c = mgr.FindNearestAvailableContainer(transform.position, Actor);
            if (c != null) { EnterSeek(c); return; }

            EnterWander();
        }

        bool Roll(float chance)
        {
            return Random.value < Mathf.Clamp01(chance + Random.Range(-noise, noise));
        }

        void EnterSeek(SearchContainer c)
        {
            _state = AIState.SeekContainer;
            _targetContainer = c;
            _targetOpponent = null;
            _stateEnterTime = Time.time;
        }

        void EnterChase(PlayerActor p)
        {
            _state = AIState.Chase;
            _targetOpponent = p;
            _targetContainer = null;
            _stateEnterTime = Time.time;
        }

        void EnterWander()
        {
            if (_state != AIState.Wander) PickWanderPoint();
            _state = AIState.Wander;
            _targetContainer = null;
            _targetOpponent = null;
        }

        // ── 执行 ───────────────────────────────────────────

        void Act()
        {
            switch (_state)
            {
                case AIState.SeekContainer: ActSeek(); break;
                case AIState.Searching:     ActSearching(); break;
                case AIState.Chase:         ActChase(); break;
                case AIState.Wander:        ActWander(); break;
            }
        }

        void ActSeek()
        {
            if (_targetContainer == null || !_targetContainer.IsAvailableFor(Actor))
            {
                EnterWander();
                return;
            }

            var to = _targetContainer.InteractPoint - transform.position;
            to.y = 0f;

            float range = Cfg != null ? Cfg.searchRange : 1.6f;
            if (to.magnitude <= range * 0.85f)
            {
                Actor.MoveInput = Vector2.zero;
                if (Actor.Search.TryBegin(_targetContainer))
                    _state = AIState.Searching;
                else
                    EnterWander();
                return;
            }

            Actor.MoveInput = new Vector2(to.x, to.z).normalized;

            // 卡住超过 4 秒就换目标
            if (Time.time - _stateEnterTime > 4f) EnterWander();
        }

        void ActSearching()
        {
            Actor.MoveInput = Vector2.zero;
            if (!Actor.Search.IsSearching)
                EnterWander();
        }

        void ActChase()
        {
            if (_targetOpponent == null || !_targetOpponent.IsAlive)
            {
                EnterWander();
                return;
            }

            var to = _targetOpponent.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;

            float elbowRange = Cfg != null ? Cfg.elbowRange : 1.5f;

            if (dist <= elbowRange * 0.9f)
            {
                // 进入攻击距离：停下并朝向目标，然后挥肘
                Actor.MoveInput = Vector2.zero;
                if (to.sqrMagnitude > 0.0001f)
                {
                    var look = Quaternion.LookRotation(to.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(
                        transform.rotation, look, 900f * Time.deltaTime);
                }

                // 朝向大致对上了才挥，避免打空
                float dot = Vector3.Dot(transform.forward, to.normalized);
                if (dot > 0.75f) Actor.Elbow.TryElbow();
            }
            else
            {
                Actor.MoveInput = new Vector2(to.x, to.z).normalized;
            }

            // 追太久放弃，防止一直咬着一个人不放
            if (Time.time - _stateEnterTime > 3.5f) EnterWander();
        }

        void ActWander()
        {
            var to = _wanderPoint - transform.position;
            to.y = 0f;

            if (to.magnitude < 0.6f)
            {
                PickWanderPoint();
                return;
            }
            Actor.MoveInput = new Vector2(to.x, to.z).normalized;
        }

        void PickWanderPoint()
        {
            var mgr = SearchRoomManager.Instance;
            // 优先在某个容器附近游荡，看起来更像在找东西
            if (mgr != null && mgr.containers.Count > 0)
            {
                var c = mgr.containers[Random.Range(0, mgr.containers.Count)];
                if (c != null)
                {
                    _wanderPoint = c.InteractPoint + new Vector3(
                        Random.Range(-1.2f, 1.2f), 0f, Random.Range(-1.2f, 1.2f));
                    return;
                }
            }
            _wanderPoint = transform.position + new Vector3(
                Random.Range(-3f, 3f), 0f, Random.Range(-3f, 3f));
        }
    }
}
