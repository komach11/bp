using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 密室搜索环节的总控。管理阶段状态机、倒计时、结算与传送。
    /// 场景里只应存在一个。
    /// </summary>
    public class SearchRoomManager : MonoBehaviour
    {
        public static SearchRoomManager Instance { get; private set; }

        [Header("配置")]
        [Tooltip("必填。所有数值都在这个资产里调")]
        public SearchRoomConfig config;

        [Tooltip("本轮房间主题，决定掉落物")]
        public RoomTheme theme = RoomTheme.Fishing;

        [Header("场景引用（建场工具会自动填）")]
        public List<PlayerActor> players = new List<PlayerActor>();
        public List<SearchContainer> containers = new List<SearchContainer>();

        [Tooltip("门的 Transform，入场结束时会播关门动作")]
        public Transform doorPivot;

        [Header("调试")]
        [Tooltip("勾选后按 R 可以立即重开一轮")]
        public bool allowRestartKey = true;

        [Tooltip("在 Console 打印玩法事件")]
        public bool verboseLog = true;

        // ── 运行时状态 ─────────────────────────────────────
        public RoundPhase Phase { get; private set; } = RoundPhase.Intro;
        public float TimeLeft { get; private set; }
        public bool CanAct => Phase == RoundPhase.Searching;

        Coroutine _roundRoutine;
        Quaternion _doorOpenRot;
        Quaternion _doorClosedRot;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SearchRoom] 场景中存在多个 SearchRoomManager，已销毁多余的。", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (config == null)
            {
                Debug.LogError("[SearchRoom] 未指定 SearchRoomConfig，环节无法运行。", this);
                enabled = false;
                return;
            }

            if (doorPivot != null)
            {
                // 门初始为打开状态（绕 Y 轴转开 100 度），入场结束后转回 0 度关闭
                _doorClosedRot = Quaternion.Euler(0f, 0f, 0f);
                _doorOpenRot = Quaternion.Euler(0f, 100f, 0f);
                doorPivot.localRotation = _doorOpenRot;
            }
        }

        void Start()
        {
            AutoCollectSceneRefs();
            StartRound();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                SearchRoomEvents.ClearAll();
            }
        }

        /// <summary>
        /// 场景引用为空时自动搜集，容错用。手动搭场景忘了拖引用也能跑。
        /// </summary>
        void AutoCollectSceneRefs()
        {
            if (players == null) players = new List<PlayerActor>();
            players.RemoveAll(p => p == null);
            if (players.Count == 0)
                players.AddRange(FindObjectsOfType<PlayerActor>());

            if (containers == null) containers = new List<SearchContainer>();
            containers.RemoveAll(c => c == null);
            if (containers.Count == 0)
                containers.AddRange(FindObjectsOfType<SearchContainer>());

            if (verboseLog)
                Debug.Log($"[SearchRoom] 已就绪：{players.Count} 名玩家，{containers.Count} 个可搜容器，主题 {theme}");
        }

        public void StartRound()
        {
            if (_roundRoutine != null) StopCoroutine(_roundRoutine);
            _roundRoutine = StartCoroutine(RoundFlow());
        }

        IEnumerator RoundFlow()
        {
            // 等一帧，确保所有 PlayerActor.Start 都已执行完（背包容量、能力初始化）
            yield return null;

            // ═══ 阶段 1：入场 ═══
            SetPhase(RoundPhase.Intro);
            ResetAll();

            float t = 0f;
            while (t < config.introDuration)
            {
                t += Time.deltaTime;
                // 门在入场的后半段关闭
                if (doorPivot != null)
                {
                    float k = Mathf.InverseLerp(config.introDuration * 0.45f, config.introDuration, t);
                    doorPivot.localRotation = Quaternion.Slerp(_doorOpenRot, _doorClosedRot, Mathf.SmoothStep(0f, 1f, k));
                }
                yield return null;
            }
            // introDuration 为 0 时上面的循环不会执行，这里兜底确保门一定关上
            if (doorPivot != null) doorPivot.localRotation = _doorClosedRot;

            // ═══ 阶段 2：搜索 ═══
            SetPhase(RoundPhase.Searching);
            TimeLeft = config.searchDuration;
            while (TimeLeft > 0f)
            {
                TimeLeft -= Time.deltaTime;
                if (TimeLeft < 0f) TimeLeft = 0f;
                SearchRoomEvents.RaiseTimerTick(TimeLeft);
                yield return null;
            }

            // ═══ 阶段 3：结算 ═══
            SetPhase(RoundPhase.Settlement);
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null) players[i].AbortSearch();

            if (verboseLog) LogSettlement();
            yield return new WaitForSeconds(config.settlementDuration);

            // ═══ 阶段 4：传送 ═══
            SetPhase(RoundPhase.Teleport);
            yield return StartCoroutine(TeleportSequence());

            if (verboseLog) Debug.Log("[SearchRoom] 本环节结束。按 R 重开一轮。");
        }

        IEnumerator TeleportSequence()
        {
            // 四人按 红→蓝→黄→绿 的顺序依次被吸走
            var ordered = new List<PlayerActor>(players);
            ordered.RemoveAll(p => p == null);
            ordered.Sort((a, b) => a.playerColor.CompareTo(b.playerColor));

            float per = ordered.Count > 0 ? config.teleportDuration / ordered.Count : 0f;
            for (int i = 0; i < ordered.Count; i++)
            {
                ordered[i].PlayTeleportOut();
                yield return new WaitForSeconds(per);
            }
        }

        void ResetAll()
        {
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null) players[i].ResetForNewRound();

            for (int i = 0; i < containers.Count; i++)
                if (containers[i] != null) containers[i].ResetForNewRound();

            // 清掉上一轮掉落在地上的道具
            var loose = FindObjectsOfType<WorldItem>();
            for (int i = 0; i < loose.Length; i++)
                if (loose[i] != null) Destroy(loose[i].gameObject);
        }

        void SetPhase(RoundPhase p)
        {
            Phase = p;
            SearchRoomEvents.RaisePhaseChanged(p);
            if (verboseLog) Debug.Log($"[SearchRoom] 阶段 → {p}");
        }

        void LogSettlement()
        {
            var sb = new System.Text.StringBuilder("[SearchRoom] 结算结果：\n");
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;
                sb.Append($"  {p.playerColor.ToLabel()}方 携带 {p.Inventory.Count}/{config.inventoryCapacity}：");
                if (p.Inventory.Count == 0) sb.Append("（空手）");
                else
                {
                    for (int k = 0; k < p.Inventory.Count; k++)
                        sb.Append(p.Inventory.Items[k].displayName + (k < p.Inventory.Count - 1 ? "、" : ""));
                }
                sb.Append('\n');
            }
            Debug.Log(sb.ToString());
        }

        void Update()
        {
            if (allowRestartKey && Input.GetKeyDown(KeyCode.R))
                StartRound();
        }

        /// <summary>
        /// 找出距离指定位置最近的、当前还可以被搜索的容器。AI 用。
        /// </summary>
        public SearchContainer FindNearestAvailableContainer(Vector3 from, PlayerActor asker)
        {
            SearchContainer best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < containers.Count; i++)
            {
                var c = containers[i];
                if (c == null || !c.IsAvailableFor(asker)) continue;
                float d = (c.InteractPoint - from).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = c; }
            }
            return best;
        }

        /// <summary>
        /// 找出距离指定玩家最近的对手。AI 用。
        /// </summary>
        public PlayerActor FindNearestOpponent(PlayerActor self, float maxRange)
        {
            PlayerActor best = null;
            float bestSqr = maxRange * maxRange;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p == self || !p.IsAlive) continue;
                float d = (p.transform.position - self.transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p; }
            }
            return best;
        }
    }
}
