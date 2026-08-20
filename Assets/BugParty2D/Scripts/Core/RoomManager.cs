using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 2D 俯视密室搜刮总控。
    /// 流程：Intro → Searching（含随机塌陷）→ ★Collapse（全塌陷）→ ★Transition（穿越）→ Finished
    /// </summary>
    public class RoomManager : MonoBehaviour
    {
        public static RoomManager Instance { get; private set; }

        [Header("配置")]
        public RoomConfig config;
        public RoomTheme theme = RoomTheme.Fishing;

        [Header("场景引用（建场工具自动填）")]
        public List<PlayerActor> players = new List<PlayerActor>();
        public List<SearchContainer> containers = new List<SearchContainer>();

        [Tooltip("★地板网格，塌陷系统的核心")]
        public FloorGrid floorGrid;

        public Transform doorPivot;
        public CeilingDebrisSpawner debrisSpawner;

        [Header("★衔接下一关")]
        [Tooltip("穿越完成后要加载的场景名。留空则只打印日志，方便单独测试本环节。\n" +
                 "衔接海岛捕鱼时填：GameScene_PartyFishing")]
        public string nextSceneName = "";

        [Tooltip("结算清单停留时长。玩家要看清自己带走了什么再进下一关")]
        [Min(0f)] public float settlementDisplayTime = 4f;

        [Tooltip("勾选后穿越结束自动重开本环节，方便反复测试。\n" +
                 "★配了 nextSceneName 时应关掉，否则会先重开而不是跳场景")]
        public bool loopForTesting = true;

        [Header("调试")]
        public bool allowRestartKey = true;
        public bool verboseLog = true;

        // ── 状态 ───────────────────────────────────────
        public RoundPhase Phase { get; private set; } = RoundPhase.Intro;
        public float TimeLeft { get; private set; }

        /// <summary>玩家能否自由行动。</summary>
        public bool CanAct => Phase == RoundPhase.Searching;

        /// <summary>本地真人玩家，HUD 用。</summary>
        public PlayerActor LocalPlayer { get; private set; }

        /// <summary>★当前的警报提示文案，HUD 轮播用。</summary>
        public string AlarmMessage { get; private set; } = "";

        Coroutine _flow;
        Quaternion _doorOpen;
        Quaternion _doorClosed;
        readonly List<Vector3> _avoidPoints = new List<Vector3>();

        // 警报文案轮播
        static readonly string[] AlarmTexts =
        {
            "⚠ 正在穿越中…",
            "⚠ 请尽快修复 BUG",
            "⚠ 地板结构不稳定",
            "⚠ 数据完整性下降",
        };
        int _alarmTextIndex;
        float _nextAlarmTextTime;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[Room2D] 场景中存在多个 RoomManager，已销毁多余的。", this);
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (config == null)
            {
                Debug.LogError("[Room2D] 未指定 RoomConfig。", this);
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
            CollectRefs();
            StartRound();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                RoomEvents.ClearAll();
            }
        }

        void CollectRefs()
        {
            if (players == null) players = new List<PlayerActor>();
            players.RemoveAll(p => p == null);
            if (players.Count == 0) players.AddRange(FindObjectsOfType<PlayerActor>());

            if (containers == null) containers = new List<SearchContainer>();
            containers.RemoveAll(c => c == null);
            if (containers.Count == 0) containers.AddRange(FindObjectsOfType<SearchContainer>());

            if (floorGrid == null) floorGrid = FindObjectOfType<FloorGrid>();
            if (debrisSpawner == null) debrisSpawner = FindObjectOfType<CeilingDebrisSpawner>();

            // 找本地玩家：挂了 HumanBrain 的那个
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] == null) continue;
                if (players[i].GetComponent<HumanBrain>() != null) { LocalPlayer = players[i]; break; }
            }
            if (LocalPlayer == null && players.Count > 0) LocalPlayer = players[0];

            // 建立塌陷保护点：容器与出生点附近不塌，避免玩法被破坏
            _avoidPoints.Clear();
            for (int i = 0; i < containers.Count; i++)
                if (containers[i] != null) _avoidPoints.Add(containers[i].transform.position);
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null) _avoidPoints.Add(players[i].transform.position);

            if (verboseLog)
                Debug.Log($"[Room2D] 就绪：{players.Count} 玩家，{containers.Count} 容器，" +
                          $"{(floorGrid != null ? floorGrid.AllTiles.Count : 0)} 块地板，主题 {theme}");
        }

        public void StartRound()
        {
            if (_flow != null) StopCoroutine(_flow);

            // 诊断计数清零，否则重开后数字会累加
            _statCollected = 0;
            _statPitfallLost = 0;
            _statKnockedOut = 0;

            _flow = StartCoroutine(Flow());
        }

        // ══════════════════════════════════════════════
        //  主流程
        // ══════════════════════════════════════════════

        IEnumerator Flow()
        {
            yield return null;   // 等一帧，让所有 Start 执行完

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

            // ═══ Searching（含随机塌陷调度）═══
            SetPhase(RoundPhase.Searching);
            TimeLeft = config.searchDuration;

            var collapseSchedule = BuildCollapseSchedule();
            int nextCollapse = 0;
            float elapsed = 0f;

            while (TimeLeft > 0f)
            {
                float dt = Time.deltaTime;
                TimeLeft = Mathf.Max(0f, TimeLeft - dt);
                elapsed += dt;

                RoomEvents.RaiseTimerTick(TimeLeft);

                // 到点触发一块地板塌陷
                while (nextCollapse < collapseSchedule.Count
                       && elapsed >= collapseSchedule[nextCollapse].time)
                {
                    var tile = collapseSchedule[nextCollapse].tile;
                    if (tile != null) tile.BeginCollapseSequence();
                    nextCollapse++;
                }

                UpdateAlarmMessage();
                UpdateAmbientShake();
                yield return null;
            }

            // ═══ ★Collapse：全塌陷 ═══
            yield return StartCoroutine(FinalCollapseRoutine());

            // ═══ ★Transition：穿越 ═══
            SetPhase(RoundPhase.Transition);
            AlarmMessage = "穿越中…";
            yield return new WaitForSeconds(config.transitionDuration);

            // ═══ Finished ═══
            SetPhase(RoundPhase.Finished);
            LogSettlement();

            // ★先让结算界面停留一会儿，玩家要看清自己带走了什么，
            //   否则一进 Finished 就切场景，清单一闪而过。
            yield return new WaitForSeconds(settlementDisplayTime);

            HandoffToNextLevel();

            // ★配了下一关就不再循环重开，否则两者会打架
            if (loopForTesting && string.IsNullOrEmpty(nextSceneName))
            {
                yield return new WaitForSeconds(1.5f);
                StartRound();
            }
        }

        // ══════════════════════════════════════════════
        //  ★随机塌陷调度
        // ══════════════════════════════════════════════

        struct CollapseEvent
        {
            public float time;
            public FloorTile tile;
        }

        /// <summary>
        /// 预先排好搜索阶段的塌陷时间表。
        /// 数量少（默认 5 块）、位置避开容器与出生点、时间均匀分布。
        /// </summary>
        List<CollapseEvent> BuildCollapseSchedule()
        {
            var list = new List<CollapseEvent>();
            if (floorGrid == null || config.randomCollapseCount <= 0) return list;

            var tiles = floorGrid.PickRandomCollapseCandidates(
                config.randomCollapseCount, _avoidPoints, config.collapseSafeRadius);

            if (tiles.Count == 0) return list;

            float startT = config.searchDuration * config.firstCollapseAt;
            float endT = config.searchDuration * config.lastCollapseAt;
            // 预留预警时间，保证最后一块塌陷能在搜索结束前完成
            endT = Mathf.Min(endT, config.searchDuration - config.crackWarningTime - 0.5f);

            for (int i = 0; i < tiles.Count; i++)
            {
                float k = tiles.Count > 1 ? i / (float)(tiles.Count - 1) : 0f;
                float time = Mathf.Lerp(startT, endT, k);
                // 加少量随机，避免节奏机械
                time += Random.Range(-0.6f, 0.6f);
                time = Mathf.Clamp(time, 0.5f, config.searchDuration - 0.2f);

                list.Add(new CollapseEvent { time = time, tile = tiles[i] });
            }

            list.Sort((a, b) => a.time.CompareTo(b.time));

            if (verboseLog)
                Debug.Log($"[Room2D] 已排定 {list.Count} 处随机塌陷");

            return list;
        }

        // ══════════════════════════════════════════════
        //  ★终局全塌陷 + 掉落
        // ══════════════════════════════════════════════

        IEnumerator FinalCollapseRoutine()
        {
            SetPhase(RoundPhase.Collapse);
            AlarmMessage = "⚠ 地板全面塌陷！";
            RoomEvents.RaiseFinalCollapseStarted();

            // 中断所有玩家的搜索
            for (int i = 0; i < players.Count; i++)
                if (players[i] != null && players[i].Search != null)
                    players[i].Search.Cancel(false);

            // 震中取房间中心，波浪从中间向四周扩散
            Vector3 epicenter = floorGrid != null
                ? floorGrid.GridToWorld(new Vector2Int(floorGrid.columns / 2, floorGrid.rows / 2))
                : Vector3.zero;

            if (floorGrid != null)
                floorGrid.TriggerFinalCollapse(epicenter, config.collapseDuration);

            // 门也打开，形成"出口出现"的暗示
            if (doorPivot != null) doorPivot.localRotation = _doorOpen;

            // 等一小段让地板先塌，玩家再掉下去，顺序才对
            yield return new WaitForSeconds(config.collapseDuration * 0.45f);

            for (int i = 0; i < players.Count; i++)
                if (players[i] != null) players[i].BeginFallToNextLevel();

            yield return new WaitForSeconds(config.collapseDuration * 0.55f);
        }

        // ══════════════════════════════════════════════
        //  警报文案与环境抖动
        // ══════════════════════════════════════════════

        void UpdateAlarmMessage()
        {
            if (Time.time < _nextAlarmTextTime) return;

            bool urgent = TimeLeft <= config.urgentThreshold;

            AlarmMessage = AlarmTexts[_alarmTextIndex % AlarmTexts.Length];
            _alarmTextIndex++;

            // 紧张时文案切换更快，制造焦躁感
            _nextAlarmTextTime = Time.time + (urgent ? 1.4f : 3.2f);
        }

        float _nextAmbientShake;

        void UpdateAmbientShake()
        {
            if (Time.time < _nextAmbientShake) return;

            bool urgent = TimeLeft <= config.urgentThreshold;
            float interval = urgent
                ? config.screenShakeIntervalUrgent
                : config.screenShakeInterval;

            // 紧张时抖得更狠
            float amount = config.screenShakeAmount * (urgent ? 1.5f : 1f);
            RoomEvents.RaiseScreenShake(amount, config.screenShakeDuration);

            _nextAmbientShake = Time.time + interval * Random.Range(0.75f, 1.25f);
        }

        // ══════════════════════════════════════════════

        void ResetAll()
        {
            AlarmMessage = "";
            _alarmTextIndex = 0;
            _nextAlarmTextTime = 0f;
            _nextAmbientShake = 0f;

            for (int i = 0; i < players.Count; i++)
                if (players[i] != null) players[i].ResetForNewRound();

            for (int i = 0; i < containers.Count; i++)
                if (containers[i] != null) containers[i].ResetForNewRound();

            if (floorGrid != null) floorGrid.ResetAll();
            if (debrisSpawner != null) debrisSpawner.ClearAll();

            // ★把塌陷时掉下去的家具/容器复位，否则重开后房间是空的
            var props = FindObjectsOfType<FallingProp>(true);
            for (int i = 0; i < props.Length; i++)
                if (props[i] != null) props[i].ResetProp();

            var loose = FindObjectsOfType<WorldItem>();
            for (int i = 0; i < loose.Length; i++)
                if (loose[i] != null) Destroy(loose[i].gameObject);
        }

        void SetPhase(RoundPhase p)
        {
            Phase = p;
            RoomEvents.RaisePhaseChanged(p);
            if (verboseLog) Debug.Log($"[Room2D] 阶段 → {p}");
        }

        void LogSettlement()
        {
            if (!verboseLog) return;

            var sb = new System.Text.StringBuilder("[Room2D] ═══ 本环节结算 ═══\n");

            // ★诊断信息：全员空手时能立刻看出是「没搜到」还是「搜到又丢了」
            sb.Append($"  主题 {theme} | 道具池 {config.GetPool(theme).Count} 种可掉落 | " +
                      $"随机塌陷 {config.randomCollapseCount} 块 | " +
                      $"踩空扣 {config.pitfallItemLoss} 件(保底留 {config.pitfallKeepAtLeast})\n");
            sb.Append($"  全场累计：搜到 {_statCollected} 件 | 踩空丢 {_statPitfallLost} 件 | " +
                      $"被肘击打掉 {_statKnockedOut} 件\n");

            var sorted = new List<PlayerActor>(players);
            sorted.RemoveAll(p => p == null);
            sorted.Sort((a, b) => ((int)a.playerColor).CompareTo((int)b.playerColor));

            for (int i = 0; i < sorted.Count; i++)
            {
                var p = sorted[i];
                sb.Append($"  {p.playerColor.ToLabel()}方 | " +
                          $"{p.Inventory.Count}/{config.inventoryCapacity} 件 | " +
                          $"{p.Inventory.Describe()}\n");
            }
            Debug.Log(sb.ToString());
        }

        // ── 诊断计数（只用于结算日志）──────────────────
        int _statCollected, _statPitfallLost, _statKnockedOut;

        void OnEnable()
        {
            RoomEvents.OnItemCollected += CountCollected;
            RoomEvents.OnItemKnockedOut += CountKnocked;
            RoomEvents.OnPlayerPitfall += CountPitfall;
        }

        void OnDisable()
        {
            RoomEvents.OnItemCollected -= CountCollected;
            RoomEvents.OnItemKnockedOut -= CountKnocked;
            RoomEvents.OnPlayerPitfall -= CountPitfall;
        }

        void CountCollected(PlayerActor p, ItemDefinition i) => _statCollected++;
        void CountKnocked(PlayerActor p, ItemDefinition i) => _statKnockedOut++;
        void CountPitfall(PlayerActor p) => _statPitfallLost++;

        /// <summary>
        /// ★衔接下一关。把各玩家带走的道具导出到 CarryOverData，
        /// 供下一个玩法场景（当前是海岛捕鱼）的服务端读取并发放。
        /// </summary>
        void HandoffToNextLevel()
        {
            CarryOverData.Clear();
            CarryOverData.SourceScene =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;

                var loadout = new CarriedLoadout
                {
                    // PlayerColor 的 0~3 正好对应大厅槽位 0~3
                    slotIndex = (int)p.playerColor,
                    isBot = p.GetComponent<AIBrain>() != null,
                    color = p.playerColor,
                };

                var items = p.Inventory.Items;
                for (int k = 0; k < items.Count; k++)
                {
                    var it = items[k];
                    if (it == null) continue;
                    loadout.itemIds.Add(it.itemId);
                    loadout.itemNames.Add(it.displayName);
                }

                CarryOverData.Set(loadout);
            }

            if (verboseLog)
                Debug.Log("[Room2D] 携带进入下一关：\n" + CarryOverData.Dump());

            PushToNextLevelReceiver();
            LoadNextScene();
        }

        /// <summary>
        /// 把交接数据推给下一关的接收端。
        ///
        /// 用反射而非直接引用：密室与捕鱼在不同程序集，
        /// 密室要能脱离捕鱼工程单独编译与测试。
        /// 找不到接收端时静默跳过（单独测试本环节的情形）。
        /// </summary>
        void PushToNextLevelReceiver()
        {
            var t = System.Type.GetType("PartyGame.Net.CarryOverReceiver");
            if (t == null) return;   // 不在捕鱼工程里，正常

            var clear = t.GetMethod("Clear",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var push = t.GetMethod("Push",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (push == null) return;

            clear?.Invoke(null, null);

            foreach (var l in CarryOverData.All)
            {
                if (l == null) continue;
                push.Invoke(null, new object[] { l.slotIndex, l.itemIds });
            }

            if (verboseLog)
                Debug.Log($"[Room2D] 已推送 {CarryOverData.SlotCount} 个槽位的携带数据给下一关");
        }

        /// <summary>
        /// 加载下一个场景。
        ///
        /// ★联网时必须走 NetworkSceneManager 而非 SceneManager.LoadScene，
        ///   否则只有主机切场景、客户端留在原地。
        ///   这里用反射探测 Netcode 是否在运行，让本工程不硬依赖 Netcode 程序集——
        ///   密室既要能单机独立测试，也要能作为联网流程的一环。
        /// </summary>
        void LoadNextScene()
        {
            if (string.IsNullOrEmpty(nextSceneName))
            {
                if (verboseLog)
                    Debug.Log("[Room2D] nextSceneName 为空，停留在本场景（单独测试模式）");
                return;
            }

            if (TryNetworkSceneLoad(nextSceneName)) return;

            if (verboseLog) Debug.Log($"[Room2D] 单机加载下一关：{nextSceneName}");
            UnityEngine.SceneManagement.SceneManager.LoadScene(nextSceneName);
        }

        /// <summary>
        /// 若 Netcode 正在以服务端身份运行，用它的 SceneManager 切场景（全端同步）。
        /// 返回是否已接管。
        /// </summary>
        bool TryNetworkSceneLoad(string sceneName)
        {
            var nmType = System.Type.GetType("Unity.Netcode.NetworkManager, Unity.Netcode.Runtime");
            if (nmType == null) return false;   // 工程未装 Netcode，走单机分支

            var singletonProp = nmType.GetProperty("Singleton",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var nm = singletonProp?.GetValue(null);
            if (nm == null) return false;

            bool isListening = (bool)(nmType.GetProperty("IsListening")?.GetValue(nm) ?? false);
            bool isServer = (bool)(nmType.GetProperty("IsServer")?.GetValue(nm) ?? false);
            if (!isListening) return false;

            if (!isServer)
            {
                // 客户端不切场景，等服务端广播
                if (verboseLog)
                    Debug.Log("[Room2D] 联网客户端：等待服务端切换场景");
                return true;
            }

            var sceneMgr = nmType.GetProperty("SceneManager")?.GetValue(nm);
            if (sceneMgr == null) return false;

            var loadMethod = sceneMgr.GetType().GetMethod("LoadScene");
            if (loadMethod == null) return false;

            if (verboseLog)
                Debug.Log($"[Room2D] 联网服务端：广播加载 {sceneName}");
            loadMethod.Invoke(sceneMgr,
                new object[] { sceneName, UnityEngine.SceneManagement.LoadSceneMode.Single });
            return true;
        }

        void Update()
        {
            if (allowRestartKey && Input.GetKeyDown(KeyCode.R)) StartRound();
        }

        // ── 查询接口（AI 用）───────────────────────────

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

        public PlayerActor FindNearestOpponent(PlayerActor self, float maxRange)
        {
            PlayerActor best = null;
            float bestSqr = maxRange * maxRange;

            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p == self || !p.IsAlive || p.IsInPitfall) continue;
                float d = (p.transform.position - self.transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p; }
            }
            return best;
        }
    }
}
