using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 第一人称 HUD。用 OnGUI 绘制，零美术资源依赖，保证拖进来就能验证玩法。
    /// 正式版应换成 UGUI，订阅同一套 RaidEvents 即可。
    ///
    /// 包含：准星、交互提示、搜索读条、网格背包、搜刮面板、撤离进度、事件日志。
    /// </summary>
    public class RaidHUD : MonoBehaviour
    {
        [Header("显示开关")]
        public bool showCrosshair = true;
        public bool showInventory = true;
        public bool showLootPanel = true;
        public bool showEventLog = true;
        public bool showHelp = true;
        public bool showBotStatus = true;

        [Range(3, 10)] public int logLines = 5;

        readonly List<string> _log = new List<string>();
        readonly Dictionary<Color, Texture2D> _texCache = new Dictionary<Color, Texture2D>();

        GUIStyle _big, _mid, _small, _tiny;
        Texture2D _panel, _panelDark, _slotEmpty;
        float _lootFlashUntil;
        string _lootFlashText = "";

        // ══════════════════════════════════════════════

        void OnEnable()
        {
            RaidEvents.OnLootTaken += OnLootTaken;
            RaidEvents.OnLootDropped += OnLootDropped;
            RaidEvents.OnMeleeHit += OnMeleeHit;
            RaidEvents.OnLootInterrupted += OnInterrupted;
            RaidEvents.OnPhaseChanged += OnPhase;
            RaidEvents.OnExtracted += OnExtracted;
            RaidEvents.OnExtractFailed += OnExtractFailed;
        }

        void OnDisable()
        {
            RaidEvents.OnLootTaken -= OnLootTaken;
            RaidEvents.OnLootDropped -= OnLootDropped;
            RaidEvents.OnMeleeHit -= OnMeleeHit;
            RaidEvents.OnLootInterrupted -= OnInterrupted;
            RaidEvents.OnPhaseChanged -= OnPhase;
            RaidEvents.OnExtracted -= OnExtracted;
            RaidEvents.OnExtractFailed -= OnExtractFailed;
        }

        void OnDestroy()
        {
            foreach (var kv in _texCache) if (kv.Value != null) Destroy(kv.Value);
            _texCache.Clear();
            if (_panel != null) Destroy(_panel);
            if (_panelDark != null) Destroy(_panelDark);
            if (_slotEmpty != null) Destroy(_slotEmpty);
        }

        // ── 事件 ───────────────────────────────────────

        void OnLootTaken(PlayerRig r, ItemDefinition i)
        {
            Push($"{r.playerColor.ToLabel()}方 获得 {i.displayName}（{i.lootValue}分）");
            if (r.isLocalPlayer) Flash($"+ {i.displayName}");
        }

        void OnLootDropped(PlayerRig r, ItemDefinition i)
        {
            Push($"{r.playerColor.ToLabel()}方 掉落了 {i.displayName}！");
            if (r.isLocalPlayer) Flash($"− {i.displayName} 被打掉了！");
        }

        void OnMeleeHit(PlayerRig a, PlayerRig v, bool back)
        {
            string tag = back ? "背刺" : "击中";
            Push($"{a.playerColor.ToLabel()}方 {tag} {v.playerColor.ToLabel()}方");
            if (v.isLocalPlayer) Flash(back ? "被背刺！" : "被击中！");
        }

        void OnInterrupted(PlayerRig r, LootContainer c)
            => Push($"{r.playerColor.ToLabel()}方 搜索 {c.containerName} 被打断");

        void OnPhase(RoundPhase p)
        {
            switch (p)
            {
                case RoundPhase.Intro:      Push("── 进入 Bug 会议室 ──"); break;
                case RoundPhase.Looting:    Push("── 门已锁！开始搜刮 ──"); break;
                case RoundPhase.Extraction: Push("── 传送门开启！立刻撤离 ──"); break;
                case RoundPhase.Settlement: Push("── 结算 ──"); break;
            }
        }

        void OnExtracted(PlayerRig r, int value)
            => Push($"{r.playerColor.ToLabel()}方 撤离成功，带走 {value} 分");

        void OnExtractFailed(PlayerRig r, int value)
            => Push($"{r.playerColor.ToLabel()}方 撤离失败，损失 {value} 分");

        void Push(string s)
        {
            _log.Add(s);
            while (_log.Count > logLines) _log.RemoveAt(0);
        }

        void Flash(string s)
        {
            _lootFlashText = s;
            _lootFlashUntil = Time.time + 1.6f;
        }

        // ── 样式 ───────────────────────────────────────

        void EnsureStyles()
        {
            if (_big != null) return;

            _big = new GUIStyle(GUI.skin.label)
            { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _mid = new GUIStyle(GUI.skin.label)
            { fontSize = 17, fontStyle = FontStyle.Bold };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _tiny = new GUIStyle(GUI.skin.label) { fontSize = 11 };

            _panel = Tex(new Color(0f, 0f, 0f, 0.55f));
            _panelDark = Tex(new Color(0f, 0f, 0f, 0.82f));
            _slotEmpty = Tex(new Color(1f, 1f, 1f, 0.10f));
        }

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        Texture2D C(Color c)
        {
            if (_texCache.TryGetValue(c, out var t) && t != null) return t;
            t = Tex(c);
            _texCache[c] = t;
            return t;
        }

        // ══════════════════════════════════════════════

        void OnGUI()
        {
            var mgr = RaidManager.Instance;
            if (mgr == null || mgr.config == null) return;

            EnsureStyles();

            var me = mgr.LocalPlayer;

            DrawTimer(mgr);

            if (me != null)
            {
                bool panelOpen = me.Loot != null && me.Loot.IsPanelOpen;

                if (showCrosshair && !panelOpen) DrawCrosshair(me);
                if (!panelOpen) DrawInteractPrompt(me);
                DrawSearchBar(me);
                if (showLootPanel && panelOpen) DrawLootPanel(me, mgr);
                if (showInventory) DrawInventory(me, mgr);
                DrawExtractProgress(me, mgr);
                DrawStatusEffects(me);
                DrawFlash();
            }

            if (showBotStatus) DrawBotStatus(mgr);
            if (showEventLog) DrawLog();
            if (showHelp) DrawHelp(mgr);
            if (mgr.Phase == RoundPhase.Settlement || mgr.Phase == RoundPhase.Finished)
                DrawSettlement(mgr);
        }

        // ── 倒计时 ─────────────────────────────────────

        void DrawTimer(RaidManager mgr)
        {
            float w = 300f, h = 68f;
            var r = new Rect((Screen.width - w) * 0.5f, 10f, w, h);
            GUI.DrawTexture(r, _panel);

            string label, timeText;
            Color col = Color.white;

            switch (mgr.Phase)
            {
                case RoundPhase.Intro:
                    label = "准备"; timeText = "";
                    col = new Color(0.7f, 0.85f, 1f);
                    break;
                case RoundPhase.Looting:
                    label = "搜刮阶段";
                    timeText = Mathf.CeilToInt(mgr.TimeLeft).ToString();
                    col = mgr.TimeLeft <= mgr.config.urgentThreshold
                        ? Color.Lerp(new Color(1f, 0.4f, 0.2f), Color.white, Mathf.PingPong(Time.time * 4f, 1f))
                        : Color.white;
                    break;
                case RoundPhase.Extraction:
                    label = "★ 立刻撤离 ★";
                    timeText = Mathf.CeilToInt(mgr.TimeLeft).ToString();
                    col = Color.Lerp(new Color(0.25f, 0.9f, 1f), Color.white, Mathf.PingPong(Time.time * 5f, 1f));
                    break;
                default:
                    label = "结算"; timeText = "";
                    col = new Color(1f, 0.85f, 0.3f);
                    break;
            }

            var prevMid = _mid.normal.textColor;
            _mid.normal.textColor = col;
            GUI.Label(new Rect(r.x, r.y + 4f, r.width, 20f),
                      label, new GUIStyle(_mid) { alignment = TextAnchor.MiddleCenter });
            _mid.normal.textColor = prevMid;

            if (!string.IsNullOrEmpty(timeText))
            {
                var prevBig = _big.normal.textColor;
                _big.normal.textColor = col;
                GUI.Label(new Rect(r.x, r.y + 22f, r.width, 44f), timeText, _big);
                _big.normal.textColor = prevBig;
            }
        }

        // ── 准星 ───────────────────────────────────────

        void DrawCrosshair(PlayerRig me)
        {
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            var aimed = me.Loot != null ? me.Loot.GetAimedContainer() : null;
            bool canMelee = me.Melee != null && me.Melee.FindVictim() != null;

            Color col = canMelee ? new Color(1f, 0.35f, 0.3f)
                      : aimed != null ? new Color(0.4f, 1f, 0.6f)
                      : new Color(1f, 1f, 1f, 0.65f);

            float gap = canMelee ? 8f : 5f;
            float len = 7f;
            float thick = 2f;
            var tex = C(col);

            // 四段十字
            GUI.DrawTexture(new Rect(cx - gap - len, cy - thick * 0.5f, len, thick), tex);
            GUI.DrawTexture(new Rect(cx + gap, cy - thick * 0.5f, len, thick), tex);
            GUI.DrawTexture(new Rect(cx - thick * 0.5f, cy - gap - len, thick, len), tex);
            GUI.DrawTexture(new Rect(cx - thick * 0.5f, cy + gap, thick, len), tex);

            // 中心点
            GUI.DrawTexture(new Rect(cx - 1f, cy - 1f, 2f, 2f), tex);
        }

        // ── 交互提示 ───────────────────────────────────

        void DrawInteractPrompt(PlayerRig me)
        {
            if (me.Loot == null || me.Loot.IsSearching) return;

            var c = me.Loot.GetAimedContainer();
            if (c == null) return;

            string text;
            if (!c.IsSearched) text = $"[F] 搜索 {c.containerName}";
            else if (c.HasLootLeft) text = $"[F] 打开 {c.containerName}（{c.LootCount} 件）";
            else text = $"{c.containerName}（已搜空）";

            float w = 300f, h = 30f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f + 40f, w, h);
            GUI.DrawTexture(r, _panel);
            GUI.Label(r, text, new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter });
        }

        // ── 搜索读条 ───────────────────────────────────

        void DrawSearchBar(PlayerRig me)
        {
            if (me.Loot == null || !me.Loot.IsSearching) return;

            float w = 260f, h = 26f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f + 50f, w, h);

            GUI.DrawTexture(r, _panelDark);
            float p = me.Loot.Progress01;
            GUI.DrawTexture(new Rect(r.x + 2f, r.y + 2f, (r.width - 4f) * p, r.height - 4f),
                            C(me.playerColor.ToColor()));
            GUI.Label(r, $"搜索中… {Mathf.RoundToInt(p * 100f)}%",
                      new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter });
        }

        // ── ★搜刮面板 ─────────────────────────────────

        void DrawLootPanel(PlayerRig me, RaidManager mgr)
        {
            var c = me.Loot.OpenContainer;
            if (c == null) return;

            float w = 340f;
            float h = 70f + c.LootCount * 32f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);

            GUI.DrawTexture(r, _panelDark);

            // 标题
            GUI.Label(new Rect(r.x + 12f, r.y + 8f, r.width - 24f, 22f),
                      c.containerName, _mid);

            // ★危险提示：这是设计的核心，必须让玩家意识到自己是靶子
            var warnStyle = new GUIStyle(_tiny) { alignment = TextAnchor.MiddleLeft };
            warnStyle.normal.textColor = Color.Lerp(
                new Color(1f, 0.35f, 0.3f), new Color(1f, 0.8f, 0.3f),
                Mathf.PingPong(Time.time * 2f, 1f));
            GUI.Label(new Rect(r.x + 12f, r.y + 30f, r.width - 24f, 16f),
                      "⚠ 翻找时无法移动，注意背后", warnStyle);

            // 战利品列表
            float y = r.y + 50f;
            for (int i = 0; i < c.LootCount; i++)
            {
                var item = c.PeekLoot(i);
                if (item == null) continue;

                bool fits = me.Inventory.CanFit(item);
                var row = new Rect(r.x + 10f, y, r.width - 20f, 28f);

                GUI.DrawTexture(row, fits ? _panel : C(new Color(0.4f, 0.1f, 0.1f, 0.5f)));

                // 色块表示体积
                GUI.DrawTexture(new Rect(row.x + 4f, row.y + 4f, 18f * item.gridWidth, 20f),
                                C(item.isRare
                                    ? Color.Lerp(item.placeholderColor, new Color(1f, 0.85f, 0.2f), 0.5f)
                                    : item.placeholderColor));

                var nameStyle = new GUIStyle(_small);
                if (!fits) nameStyle.normal.textColor = new Color(1f, 0.5f, 0.5f);
                else if (item.isRare) nameStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);

                string suffix = fits ? "" : "  空间不足";
                GUI.Label(new Rect(row.x + 26f + 18f * item.gridWidth, row.y + 5f, row.width - 60f, 20f),
                          $"[{i + 1}] {item.displayName}  {item.gridWidth}×{item.gridHeight}  {item.lootValue}分{suffix}",
                          nameStyle);

                y += 32f;
            }

            GUI.Label(new Rect(r.x + 12f, r.yMax - 22f, r.width - 24f, 18f),
                      "数字键 取物　G 全取　Tab/F 关闭", _tiny);
        }

        // ── ★网格背包 ─────────────────────────────────

        void DrawInventory(PlayerRig me, RaidManager mgr)
        {
            var inv = me.Inventory;
            float cell = 34f;
            float pad = 3f;

            float gw = inv.Width * (cell + pad) + pad;
            float gh = inv.Height * (cell + pad) + pad;

            var r = new Rect(14f, Screen.height - gh - 44f, gw + 8f, gh + 34f);
            GUI.DrawTexture(r, _panel);

            string head = inv.SimpleMode
                ? $"背包 {inv.Count}/{mgr.config.simpleCapacity}"
                : $"背包 {inv.UsedCells}/{inv.TotalCells} 格　{inv.TotalValue} 分";
            GUI.Label(new Rect(r.x + 6f, r.y + 4f, r.width - 12f, 20f), head, _small);

            float ox = r.x + 6f;
            float oy = r.y + 26f;

            // 先画空格底
            for (int gx = 0; gx < inv.Width; gx++)
                for (int gy = 0; gy < inv.Height; gy++)
                {
                    var cr = new Rect(ox + pad + gx * (cell + pad), oy + pad + gy * (cell + pad), cell, cell);
                    GUI.DrawTexture(cr, _slotEmpty);
                }

            // 再画已放置的道具（按 entry 画整块，能看出体积）
            for (int i = 0; i < inv.Entries.Count; i++)
            {
                var e = inv.Entries[i];
                if (e.item == null) continue;

                float w = e.item.gridWidth * cell + (e.item.gridWidth - 1) * pad;
                float h = e.item.gridHeight * cell + (e.item.gridHeight - 1) * pad;

                var ir = new Rect(
                    ox + pad + e.x * (cell + pad),
                    oy + pad + e.y * (cell + pad), w, h);

                var col = e.item.isRare
                    ? Color.Lerp(e.item.placeholderColor, new Color(1f, 0.85f, 0.2f), 0.45f)
                    : e.item.placeholderColor;
                GUI.DrawTexture(ir, C(col));

                var ns = new GUIStyle(_tiny)
                { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                ns.normal.textColor = Color.black;
                GUI.Label(ir, e.item.displayName, ns);
            }
        }

        // ── 撤离进度 ───────────────────────────────────

        void DrawExtractProgress(PlayerRig me, RaidManager mgr)
        {
            if (mgr.Phase != RoundPhase.Extraction) return;

            if (me.Result == ExtractResult.Extracted)
            {
                float w = 360f;
                var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f - 40f, w, 60f);
                GUI.DrawTexture(r, _panelDark);
                var st = new GUIStyle(_mid) { alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = new Color(0.4f, 1f, 0.6f);
                GUI.Label(r, $"撤离成功！\n带走 {me.Inventory.TotalValue} 分", st);
                return;
            }

            var zone = mgr.GetZoneContaining(me);
            if (zone == null)
            {
                // 指引：告诉玩家往哪跑
                var nearest = mgr.FindNearestExtractionZone(me.transform.position);
                if (nearest == null) return;

                float dist = Vector3.Distance(me.transform.position, nearest.transform.position);
                float w = 300f;
                var r = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.5f + 80f, w, 26f);
                GUI.DrawTexture(r, _panel);
                var st = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = new Color(0.35f, 0.85f, 1f);
                GUI.Label(r, $"{nearest.zoneName}　{dist:F0} 米", st);
                return;
            }

            float p = zone.GetProgress01(me);
            float bw = 300f, bh = 30f;
            var br = new Rect((Screen.width - bw) * 0.5f, Screen.height * 0.5f + 80f, bw, bh);

            GUI.DrawTexture(br, _panelDark);
            GUI.DrawTexture(new Rect(br.x + 2f, br.y + 2f, (br.width - 4f) * p, br.height - 4f),
                            C(new Color(0.25f, 0.85f, 1f)));
            GUI.Label(br, me.IsStaggered ? "撤离被打断！" : $"撤离中… {Mathf.RoundToInt(p * 100f)}%",
                      new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter });
        }

        // ── 状态 ───────────────────────────────────────

        void DrawStatusEffects(PlayerRig me)
        {
            var lines = new List<string>();

            if (me.IsStaggered) lines.Add("硬直中");
            if (me.CurrentStance == Stance.Crouch) lines.Add("下蹲（静音）");
            else if (me.CurrentStance == Stance.Sprint) lines.Add("疾跑（噪音大）");
            if (me.Melee != null && !me.Melee.IsReady) lines.Add("近战冷却");

            if (lines.Count == 0) return;

            float w = 150f;
            float h = lines.Count * 18f + 10f;
            var r = new Rect(Screen.width - w - 14f, Screen.height - h - 14f, w, h);
            GUI.DrawTexture(r, _panel);

            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++) sb.AppendLine(lines[i]);
            GUI.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 16f, r.height - 8f), sb.ToString(), _small);
        }

        void DrawFlash()
        {
            if (Time.time > _lootFlashUntil) return;

            float alpha = Mathf.Clamp01((_lootFlashUntil - Time.time) / 1.6f);
            var st = new GUIStyle(_mid) { alignment = TextAnchor.MiddleCenter };
            st.normal.textColor = new Color(1f, 1f, 1f, alpha);

            GUI.Label(new Rect(0f, Screen.height * 0.5f - 90f, Screen.width, 26f), _lootFlashText, st);
        }

        // ── AI 状态（调试用）─────────────────────────

        void DrawBotStatus(RaidManager mgr)
        {
            float w = 190f;
            var rows = new List<PlayerRig>();
            for (int i = 0; i < mgr.players.Count; i++)
            {
                var p = mgr.players[i];
                if (p != null && !p.isLocalPlayer) rows.Add(p);
            }
            if (rows.Count == 0) return;

            float h = rows.Count * 20f + 26f;
            var r = new Rect(Screen.width - w - 14f, 88f, w, h);
            GUI.DrawTexture(r, _panel);
            GUI.Label(new Rect(r.x + 8f, r.y + 3f, r.width - 16f, 18f), "对手状态", _small);

            float y = r.y + 22f;
            for (int i = 0; i < rows.Count; i++)
            {
                var p = rows[i];
                string state = p.Result == ExtractResult.Extracted ? "已撤离"
                             : p.Result == ExtractResult.Failed ? "撤离失败"
                             : p.IsStaggered ? "硬直"
                             : (p.Loot != null && p.Loot.IsPanelOpen) ? "翻找中"
                             : (p.Loot != null && p.Loot.IsSearching) ? "搜索中"
                             : "行动中";

                var st = new GUIStyle(_small);
                st.normal.textColor = p.playerColor.ToColor();
                GUI.Label(new Rect(r.x + 8f, y, r.width - 16f, 18f),
                          $"{p.playerColor.ToLabel()}方  {state}  {p.Inventory.TotalValue}分", st);
                y += 20f;
            }
        }

        // ── 日志与帮助 ─────────────────────────────────

        void DrawLog()
        {
            if (_log.Count == 0) return;

            float w = 330f;
            float h = _log.Count * 19f + 12f;
            var r = new Rect(14f, 88f, w, h);
            GUI.DrawTexture(r, _panel);

            var sb = new StringBuilder();
            for (int i = 0; i < _log.Count; i++) sb.AppendLine(_log[i]);
            GUI.Label(new Rect(r.x + 8f, r.y + 5f, r.width - 16f, r.height - 10f), sb.ToString(), _small);
        }

        void DrawHelp(RaidManager mgr)
        {
            float w = 420f, h = 56f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 8f, w, h);
            GUI.DrawTexture(r, _panel);

            var sb = new StringBuilder();
            sb.AppendLine("WASD 移动　Shift 疾跑　Ctrl 蹲　Space 跳　鼠标 视角");
            sb.Append("F 搜刮　V/左键 肘击　G 全取　R 重开　Esc 解锁鼠标");

            GUI.Label(new Rect(r.x + 10f, r.y + 5f, r.width - 20f, r.height - 10f),
                      sb.ToString(), new GUIStyle(_small) { alignment = TextAnchor.UpperCenter });
        }

        // ── 结算 ───────────────────────────────────────

        void DrawSettlement(RaidManager mgr)
        {
            float w = 420f, h = 60f + mgr.players.Count * 26f;
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.DrawTexture(r, _panelDark);

            GUI.Label(new Rect(r.x, r.y + 8f, r.width, 24f), "本轮结算",
                      new GUIStyle(_mid) { alignment = TextAnchor.MiddleCenter });

            var sorted = new List<PlayerRig>(mgr.players);
            sorted.RemoveAll(p => p == null);
            sorted.Sort((a, b) =>
            {
                int va = a.Result == ExtractResult.Extracted ? a.Inventory.TotalValue : 0;
                int vb = b.Result == ExtractResult.Extracted ? b.Inventory.TotalValue : 0;
                return vb.CompareTo(va);
            });

            float y = r.y + 38f;
            for (int i = 0; i < sorted.Count; i++)
            {
                var p = sorted[i];
                int score = p.Result == ExtractResult.Extracted ? p.Inventory.TotalValue : 0;
                string status = p.Result == ExtractResult.Extracted ? "撤离成功"
                              : p.Result == ExtractResult.Failed ? "未撤离，战利品作废"
                              : "—";

                var st = new GUIStyle(_small);
                st.normal.textColor = p.playerColor.ToColor();
                GUI.Label(new Rect(r.x + 16f, y, r.width - 32f, 22f),
                          $"第{i + 1}名　{p.playerColor.ToLabel()}方　{score} 分　{status}", st);
                y += 26f;
            }

            GUI.Label(new Rect(r.x, r.yMax - 22f, r.width, 18f), "按 R 重开一局",
                      new GUIStyle(_tiny) { alignment = TextAnchor.MiddleCenter });
        }
    }
}
