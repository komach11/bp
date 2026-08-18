using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 调试用 HUD。用 OnGUI 绘制，不依赖任何 UI Prefab 与美术资源，
    /// 保证脚本一拖进来就能看到倒计时、道具栏和事件日志。
    /// 正式版应该换成 UGUI/UITK，这里的目的是让 Demo 立刻可玩可验证。
    /// </summary>
    public class SearchRoomHUD : MonoBehaviour
    {
        [Header("显示开关")]
        public bool showTimer = true;
        public bool showInventories = true;
        public bool showEventLog = true;
        public bool showHelp = true;

        [Header("事件日志")]
        [Range(3, 12)] public int logLines = 6;

        readonly List<string> _log = new List<string>();

        GUIStyle _bigStyle;
        GUIStyle _midStyle;
        GUIStyle _smallStyle;
        Texture2D _panelTex;
        Texture2D _slotEmptyTex;

        // 颜色纹理缓存。绝不能在 OnGUI 里现场 new Texture2D，那会每帧泄漏。
        readonly Dictionary<Color, Texture2D> _texCache = new Dictionary<Color, Texture2D>();

        void OnEnable()
        {
            SearchRoomEvents.OnItemCollected += HandleCollected;
            SearchRoomEvents.OnItemKnockedOut += HandleKnockedOut;
            SearchRoomEvents.OnElbowHit += HandleElbow;
            SearchRoomEvents.OnSearchInterrupted += HandleInterrupted;
            SearchRoomEvents.OnPhaseChanged += HandlePhase;
        }

        void OnDisable()
        {
            SearchRoomEvents.OnItemCollected -= HandleCollected;
            SearchRoomEvents.OnItemKnockedOut -= HandleKnockedOut;
            SearchRoomEvents.OnElbowHit -= HandleElbow;
            SearchRoomEvents.OnSearchInterrupted -= HandleInterrupted;
            SearchRoomEvents.OnPhaseChanged -= HandlePhase;
        }

        // ── 事件订阅 ───────────────────────────────────────

        void HandleCollected(PlayerActor a, ItemDefinition i)
            => Push($"{a.playerColor.ToLabel()}方 搜到了 {i.displayName}");

        void HandleKnockedOut(PlayerActor a, ItemDefinition i)
            => Push($"{a.playerColor.ToLabel()}方 的 {i.displayName} 被打飞了！");

        void HandleElbow(PlayerActor atk, PlayerActor vic)
            => Push($"{atk.playerColor.ToLabel()}方 肘击了 {vic.playerColor.ToLabel()}方");

        void HandleInterrupted(PlayerActor a, SearchContainer c)
            => Push($"{a.playerColor.ToLabel()}方 搜索 {c.containerName} 被打断");

        void HandlePhase(RoundPhase p)
        {
            switch (p)
            {
                case RoundPhase.Intro:      Push("── 四人掉进故障会议室 ──"); break;
                case RoundPhase.Searching:  Push("── 门已锁！开始搜索 ──"); break;
                case RoundPhase.Settlement: Push("── 时间到，锁定道具 ──"); break;
                case RoundPhase.Teleport:   Push("── 传送门开启 ──"); break;
            }
        }

        void Push(string line)
        {
            _log.Add(line);
            while (_log.Count > logLines) _log.RemoveAt(0);
        }

        // ── 绘制 ───────────────────────────────────────────

        void EnsureStyles()
        {
            if (_bigStyle != null) return;

            _bigStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter
            };
            _midStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };

            _panelTex = MakeTex(new Color(0f, 0f, 0f, 0.55f));
            _slotEmptyTex = MakeTex(new Color(1f, 1f, 1f, 0.13f));
        }

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>取得指定颜色的 1x1 纹理，带缓存。OnGUI 里必须用这个而不是 MakeTex。</summary>
        Texture2D GetTex(Color c)
        {
            if (_texCache.TryGetValue(c, out var t) && t != null) return t;
            t = MakeTex(c);
            _texCache[c] = t;
            return t;
        }

        void OnDestroy()
        {
            foreach (var kv in _texCache)
                if (kv.Value != null) Destroy(kv.Value);
            _texCache.Clear();

            if (_panelTex != null) Destroy(_panelTex);
            if (_slotEmptyTex != null) Destroy(_slotEmptyTex);
        }

        void OnGUI()
        {
            var mgr = SearchRoomManager.Instance;
            if (mgr == null || mgr.config == null) return;

            EnsureStyles();

            if (showTimer) DrawTimer(mgr);
            if (showInventories) DrawInventories(mgr);
            if (showEventLog) DrawLog();
            if (showHelp) DrawHelp(mgr);
        }

        void DrawTimer(SearchRoomManager mgr)
        {
            float w = 260f, h = 74f;
            var r = new Rect((Screen.width - w) * 0.5f, 14f, w, h);
            GUI.DrawTexture(r, _panelTex);

            string text;
            Color col = Color.white;

            switch (mgr.Phase)
            {
                case RoundPhase.Intro:
                    text = "准备"; col = new Color(0.7f, 0.85f, 1f); break;
                case RoundPhase.Searching:
                    text = Mathf.CeilToInt(mgr.TimeLeft).ToString();
                    col = mgr.TimeLeft <= mgr.config.urgentThreshold
                        ? Color.Lerp(Color.red, Color.white, Mathf.PingPong(Time.time * 5f, 1f))
                        : Color.white;
                    break;
                case RoundPhase.Settlement:
                    text = "时间到"; col = new Color(1f, 0.85f, 0.3f); break;
                default:
                    text = "传送中"; col = new Color(0.5f, 0.8f, 1f); break;
            }

            var prev = _bigStyle.normal.textColor;
            _bigStyle.normal.textColor = col;
            GUI.Label(r, text, _bigStyle);
            _bigStyle.normal.textColor = prev;
        }

        void DrawInventories(SearchRoomManager mgr)
        {
            float panelW = 210f;
            float panelH = 96f;
            float pad = 10f;

            // 四人分别放在四个屏幕角落，方便本地同屏辨识
            var slots = new Rect[4]
            {
                new Rect(pad, Screen.height - panelH - pad, panelW, panelH),                       // 左下
                new Rect(Screen.width - panelW - pad, Screen.height - panelH - pad, panelW, panelH), // 右下
                new Rect(pad, pad + 96f, panelW, panelH),                                          // 左上
                new Rect(Screen.width - panelW - pad, pad + 96f, panelW, panelH)                   // 右上
            };

            int idx = 0;
            for (int i = 0; i < mgr.players.Count && idx < 4; i++)
            {
                var p = mgr.players[i];
                if (p == null) continue;
                DrawOneInventory(slots[idx], p, mgr);
                idx++;
            }
        }

        void DrawOneInventory(Rect r, PlayerActor p, SearchRoomManager mgr)
        {
            GUI.DrawTexture(r, _panelTex);

            var teamCol = p.playerColor.ToColor();

            // 标题
            var titleRect = new Rect(r.x + 10f, r.y + 6f, r.width - 20f, 22f);
            var prevMid = _midStyle.normal.textColor;
            _midStyle.normal.textColor = teamCol;
            string tag = p.GetComponent<AIBrain>() != null ? " (AI)" : "";
            GUI.Label(titleRect, p.playerColor.ToLabel() + "方" + tag, _midStyle);
            _midStyle.normal.textColor = prevMid;

            // 道具格
            int cap = mgr.config.inventoryCapacity;
            float slotSize = 30f;
            float gap = 6f;
            float startX = r.x + 10f;
            float y = r.y + 32f;

            for (int s = 0; s < cap; s++)
            {
                var sr = new Rect(startX + s * (slotSize + gap), y, slotSize, slotSize);
                if (s < p.Inventory.Count)
                {
                    var item = p.Inventory.Items[s];
                    GUI.DrawTexture(sr, GetTex(item != null ? item.placeholderColor : Color.white));
                    if (item != null)
                        GUI.Label(new Rect(sr.x + slotSize + 4f, sr.y + 5f, 120f, 20f),
                                  item.displayName, _smallStyle);
                }
                else
                {
                    GUI.DrawTexture(sr, _slotEmptyTex);
                }
            }

            // 状态行
            string state = "待机";
            if (p.IsStaggered) state = "被撞飞！";
            else if (p.Search != null && p.Search.IsSearching)
                state = $"搜索中 {Mathf.RoundToInt(p.Search.Progress01 * 100f)}%";
            else if (p.Elbow != null && !p.Elbow.IsReady) state = "肘击冷却";

            GUI.Label(new Rect(r.x + 10f, r.y + 68f, r.width - 20f, 20f), state, _smallStyle);
        }

        void DrawLog()
        {
            if (_log.Count == 0) return;

            float w = 320f;
            float h = _log.Count * 20f + 14f;
            var r = new Rect((Screen.width - w) * 0.5f, 100f, w, h);
            GUI.DrawTexture(r, _panelTex);

            var sb = new StringBuilder();
            for (int i = 0; i < _log.Count; i++) sb.AppendLine(_log[i]);

            GUI.Label(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, r.height - 12f),
                      sb.ToString(), _smallStyle);
        }

        void DrawHelp(SearchRoomManager mgr)
        {
            float w = 300f, h = 74f;
            var r = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 10f, w, h);
            GUI.DrawTexture(r, _panelTex);

            var sb = new StringBuilder();
            sb.AppendLine("WASD 移动　　J 按住搜索　　K 肘击");
            sb.AppendLine("R 重开一轮");
            sb.Append($"主题：{mgr.theme}　容量上限：{mgr.config.inventoryCapacity}");

            GUI.Label(new Rect(r.x + 12f, r.y + 6f, r.width - 24f, r.height - 12f),
                      sb.ToString(), _smallStyle);
        }
    }
}
