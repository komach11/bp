using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 一局密室搜刮的总控。
    /// 流程：Intro → Looting → Extraction（撤离窗口）→ Settlement → Finished
    /// </summary>
    public class RaidManager : MonoBehaviour
    {
        public static RaidManager Instance { get; private set; }

        [Header("配置")]
        public RaidConfig config;
        public RoomTheme theme = RoomTheme.Fishing;

        [Header("场景引用（建场工具自动填）")]
        public List<PlayerRig> players = new List<PlayerRig>();
        public List<LootContainer> containers = new List<LootContainer>();
        public List<ExtractionZone> extractionZones = new List<ExtractionZone>();
        public Transform doorPivot;

        [Header("调试")]
        public bool allowRestartKey = true;
        public bool verboseLog = true;

        // ── 状态 ───────────────────────────────────────
        public RoundPhase Phase { get; private set; } = RoundPhase.Intro;
        public float TimeLeft { get; private set; }

        /// <summary>玩家现在能否自由行动。</summary>
        public bool CanAct => Phase == RoundPhase.Looting || Phase == RoundPhase.Extraction;

        /// <summary>本地真人玩家，HUD 用。</summary>
        public PlayerRig LocalPlayer { get; private set; }

        Coroutine _flow;
        Quaternion _doorOpen;
        Quaternion _doorClosed;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Raid] 场景中存在多个 RaidManager，已销毁多余的。", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (config == null)
            {
                Debug.LogError("[Raid] 未指定 RaidConfig。", this);
                enabled = false;
                return;
            }

            if (doorPivot != null)
            {
                _doorClosed = Quaternion.identity;
                _doorOpen = Quaternion.Euler(0f, 105f, 0f);
                doorPivot.localRotation = _doorOpen;
            }
        }

        void Start()
        {
            CollectSceneRefs();
            StartRound();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                RaidEvents.ClearAll();
            }
        }

        void CollectSceneRefs()
        {
            if (players == null) players = new List<PlayerRig>();
            players.RemoveAll(p => p == null);
            if (players.Count == 0) players.AddRange(FindObjectsOfType<PlayerRig>());

            if (containers == null) containers = new List<LootContainer>();
            containers.RemoveAll(c => c == null);
            if (containers.Count == 0) containers.AddRange(FindObjectsOfType<LootContainer>());

            if (extractionZones == null) extractionZones = new List<ExtractionZone>();
            extractionZones.RemoveAll(z => z == null);
            if (extractionZones.Count == 0) extractionZones.AddRange(FindObjectsOfType<ExtractionZone>());

            // 找出本地玩家
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && players[i].isLocalPlayer)
                {
                    LocalPlayer = players[i];
                    break;
                }

            if (verboseLog)
                Debug.Log($"[Raid] 就绪：{players.Count} 名玩家，{containers.Count} 个容器，" +
                          $"{extractionZones.Count} 个撤离点，主题 {theme}");
        }

        public void StartRound()
        {
            if (_flow != null) StopCoroutine(_flow);
            _flow = StartCoroutine(Flow());
        }

        IEnumerator Flow()
        {
            // 等一帧确保所有 Start 执行完
            yield return null;

            // ═══ Intro ═══
            SetPhase(RoundPhase.Intro);
            ResetAll();

            float t = 0f;
            while (t < config.introDuration)
            {
                t += Time.deltaTime;
                if (doorPivot != null)
                {
                    float k = Mathf.InverseLerp(config.introDuration * 0.4f, config.introDuration, t);
                    doorPivot.localRotation = Quaternion.Slerp(
                        _doorOpen, _doorClosed, Mathf.SmoothStep(0f, 1f, k));
                }
                yield return null;
            }
            if (doorPivot != null) doorPivot.localRotation = _doorClosed;

            // ═══ Looting ═══
            SetPhase(RoundPhase.Looting);
            TimeLeft = config.lootDuration;
            while (TimeLeft > 0f)
            {
                TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
                RaidEvents.RaiseTimerTick(TimeLeft);
                yield return null;
            }

            // ═══ Extraction ★ ═══
            SetPhase(RoundPhase.Extraction);
            for (int i = 0; i < extractionZones.Count; i++)
                if (extractionZones[i] != null) extractionZones[i].SetActive(true);

            if (doorPivot != null) doorPivot.localRotation = _doorOpen;

            TimeLeft = config.extractionWindow;
            while (TimeLeft > 0f)
            {
                TimeLeft = Mathf.Max(0f, TimeLeft - Time.deltaTime);
                RaidEvents.RaiseTimerTick(TimeLeft);

                // 所有人都撤离了就提前结束
                if (AllPlayersResolved()) break;
                yield return null;
            }

            // ═══ Settlement ═══
            SetPhase(RoundPhase.Settlement);

            // 还在场内的人全部判定撤离失败，战利品作废
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;
                if (p.Loot != null) p.Loot.ForceAbort();
                if (p.Result == ExtractResult.InRaid) p.OnExtractFail();
            }

            if (verboseLog) LogSettlement();
            yield return new WaitForSeconds(config.settlementDuration);

            SetPhase(RoundPhase.Finished);
            if (verboseLog) Debug.Log("[Raid] 本局结束。按 R 重开。");
        }

        bool AllPlayersResolved()
        {
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;
                if (p.Result == ExtractResult.InRaid) return false;
            }
            return true;
        }

        void ResetAll()
        {
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null)
                {
                    players[i].ResetForNewRound();
                    var look = players[i].GetComponent<FirstPersonLook>();
                    if (look != null) look.ResetLook();
                }

            for (int i = 0; i < containers.Count; i++)
                if (containers[i] != null) containers[i].ResetForNewRound();

            for (int i = 0; i < extractionZones.Count; i++)
                if (extractionZones[i] != null) extractionZones[i].ResetForNewRound();

            // 清掉地上残留的掉落物
            var loose = FindObjectsOfType<DroppedLoot>();
            for (int i = 0; i < loose.Length; i++)
                if (loose[i] != null) Destroy(loose[i].gameObject);
        }

        void SetPhase(RoundPhase p)
        {
            Phase = p;
            RaidEvents.RaisePhaseChanged(p);
            if (verboseLog) Debug.Log($"[Raid] 阶段 → {p}");
        }

        void LogSettlement()
        {
            var sb = new System.Text.StringBuilder("[Raid] ═══ 结算 ═══\n");

            var sorted = new List<PlayerRig>(players);
            sorted.RemoveAll(p => p == null);
            sorted.Sort((a, b) =>
            {
                int va = a.Result == ExtractResult.Extracted ? a.Inventory.TotalValue : 0;
                int vb = b.Result == ExtractResult.Extracted ? b.Inventory.TotalValue : 0;
                return vb.CompareTo(va);
            });

            for (int i = 0; i < sorted.Count; i++)
            {
                var p = sorted[i];
                string status = p.Result == ExtractResult.Extracted ? "撤离成功"
                              : p.Result == ExtractResult.Failed ? "撤离失败（战利品作废）"
                              : "未结算";
                int score = p.Result == ExtractResult.Extracted ? p.Inventory.TotalValue : 0;

                sb.Append($"  第{i + 1}名 {p.playerColor.ToLabel()}方 | {status} | 得分 {score} | {p.Inventory.Describe()}\n");
            }
            Debug.Log(sb.ToString());
        }

        void Update()
        {
            if (allowRestartKey && Input.GetKeyDown(KeyCode.R))
                StartRound();
        }

        // ── 查询接口（AI 用）───────────────────────────

        /// <summary>找最近的、还能读条搜索的容器。</summary>
        public LootContainer FindNearestSearchableContainer(Vector3 from, PlayerRig asker)
        {
            LootContainer best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < containers.Count; i++)
            {
                var c = containers[i];
                if (c == null) continue;

                // 未搜过的，或已搜过但还有剩货的，都算目标
                bool usable = c.IsAvailableFor(asker) || c.CanOpenDirectly(asker);
                if (!usable) continue;

                float d = (c.InteractPoint - from).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = c; }
            }
            return best;
        }

        public ExtractionZone FindNearestExtractionZone(Vector3 from)
        {
            ExtractionZone best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < extractionZones.Count; i++)
            {
                var z = extractionZones[i];
                if (z == null) continue;
                float d = (z.transform.position - from).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = z; }
            }
            return best;
        }

        /// <summary>本地玩家当前所在的撤离点，HUD 显示进度用。</summary>
        public ExtractionZone GetZoneContaining(PlayerRig rig)
        {
            if (rig == null) return null;
            for (int i = 0; i < extractionZones.Count; i++)
            {
                var z = extractionZones[i];
                if (z != null && z.IsActive && z.Contains(rig.transform.position)) return z;
            }
            return null;
        }
    }
}
