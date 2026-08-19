using System.Collections.Generic;
using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>背包里一件已放置的道具，记录它占据的左上角坐标。</summary>
    public class GridEntry
    {
        public ItemDefinition item;
        public int x;
        public int y;

        public GridEntry(ItemDefinition item, int x, int y)
        {
            this.item = item;
            this.x = x;
            this.y = y;
        }

        public bool Covers(int cx, int cy)
        {
            return cx >= x && cx < x + item.gridWidth
                && cy >= y && cy < y + item.gridHeight;
        }
    }

    /// <summary>
    /// 塔科夫式网格背包。道具有体积，需要找到连续空间才能装下。
    /// 这让「拿一个大渔网还是四把小刀」变成真正的取舍。
    ///
    /// 可通过 RaidConfig.useSimpleCountMode 退回到简单计数模式。
    /// </summary>
    public class GridInventory : MonoBehaviour
    {
        readonly List<GridEntry> _entries = new List<GridEntry>();

        PlayerRig _owner;
        RaidConfig _cfg;
        int _w = 4;
        int _h = 2;
        bool _simpleMode;
        int _simpleCap = 2;

        /// <summary>占用标记表，true 表示该格已被占。</summary>
        bool[,] _occupied;

        public IReadOnlyList<GridEntry> Entries => _entries;
        public int Width => _w;
        public int Height => _h;
        public int Count => _entries.Count;
        public bool IsEmpty => _entries.Count == 0;
        public bool SimpleMode => _simpleMode;

        /// <summary>已用格子数。</summary>
        public int UsedCells
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _entries.Count; i++)
                    if (_entries[i].item != null) n += _entries[i].item.GridArea;
                return n;
            }
        }

        public int TotalCells => _w * _h;

        /// <summary>战利品总价值。撤离成功后作为得分。</summary>
        public int TotalValue
        {
            get
            {
                int v = 0;
                for (int i = 0; i < _entries.Count; i++)
                    if (_entries[i].item != null) v += _entries[i].item.lootValue;
                return v;
            }
        }

        public void Init(PlayerRig owner, RaidConfig cfg)
        {
            _owner = owner;
            _cfg = cfg;

            _simpleMode = cfg.useSimpleCountMode;
            _simpleCap = cfg.simpleCapacity;
            _w = Mathf.Max(1, cfg.gridWidth);
            _h = Mathf.Max(1, cfg.gridHeight);

            _occupied = new bool[_w, _h];
            _entries.Clear();
        }

        /// <summary>该格是否已被占用。</summary>
        public bool IsCellOccupied(int x, int y)
        {
            if (_occupied == null) return false;
            if (x < 0 || y < 0 || x >= _w || y >= _h) return true;
            return _occupied[x, y];
        }

        /// <summary>取得占据该格的道具，没有则返回 null。UI 绘制时用。</summary>
        public ItemDefinition GetItemAt(int x, int y)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Covers(x, y)) return _entries[i].item;
            return null;
        }

        /// <summary>判断某个位置能否放下这件道具。</summary>
        bool CanPlaceAt(ItemDefinition item, int px, int py)
        {
            if (item == null) return false;
            if (px < 0 || py < 0) return false;
            if (px + item.gridWidth > _w) return false;
            if (py + item.gridHeight > _h) return false;

            for (int dx = 0; dx < item.gridWidth; dx++)
                for (int dy = 0; dy < item.gridHeight; dy++)
                    if (_occupied[px + dx, py + dy]) return false;

            return true;
        }

        /// <summary>
        /// 自动寻找第一个能放下的位置。从左上角开始逐行扫描。
        /// </summary>
        public bool TryFindSlot(ItemDefinition item, out int outX, out int outY)
        {
            outX = outY = -1;
            if (item == null) return false;

            for (int y = 0; y <= _h - item.gridHeight; y++)
                for (int x = 0; x <= _w - item.gridWidth; x++)
                    if (CanPlaceAt(item, x, y))
                    {
                        outX = x; outY = y;
                        return true;
                    }
            return false;
        }

        /// <summary>这件道具现在装得下吗。搜刮界面用它显示「空间不足」。</summary>
        public bool CanFit(ItemDefinition item)
        {
            if (item == null) return false;
            if (_simpleMode) return _entries.Count < _simpleCap;
            return TryFindSlot(item, out _, out _);
        }

        /// <summary>尝试装入一件道具。</summary>
        public bool TryAdd(ItemDefinition item)
        {
            if (item == null) return false;

            if (_simpleMode)
            {
                if (_entries.Count >= _simpleCap) return false;
                _entries.Add(new GridEntry(item, _entries.Count, 0));
                RaidEvents.RaiseInventoryChanged(_owner);
                return true;
            }

            if (!TryFindSlot(item, out int x, out int y)) return false;

            var entry = new GridEntry(item, x, y);
            _entries.Add(entry);
            MarkCells(entry, true);
            RaidEvents.RaiseInventoryChanged(_owner);
            return true;
        }

        void MarkCells(GridEntry e, bool value)
        {
            if (_simpleMode || e.item == null || _occupied == null) return;
            for (int dx = 0; dx < e.item.gridWidth; dx++)
                for (int dy = 0; dy < e.item.gridHeight; dy++)
                {
                    int cx = e.x + dx, cy = e.y + dy;
                    if (cx >= 0 && cy >= 0 && cx < _w && cy < _h)
                        _occupied[cx, cy] = value;
                }
        }

        /// <summary>
        /// 移除最后装入的一件并返回。被肘击时调用。
        /// 打掉最新拿到的那件，喜剧与挫败感最强。
        /// </summary>
        public ItemDefinition PopLatest()
        {
            if (_entries.Count == 0) return null;

            int last = _entries.Count - 1;
            var entry = _entries[last];
            _entries.RemoveAt(last);
            MarkCells(entry, false);
            RaidEvents.RaiseInventoryChanged(_owner);
            return entry.item;
        }

        /// <summary>
        /// 移除价值最高的一件并返回。用于背刺——被偷袭损失最惨重。
        /// </summary>
        public ItemDefinition PopMostValuable()
        {
            if (_entries.Count == 0) return null;

            int best = 0;
            int bestValue = -1;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].item == null) continue;
                if (_entries[i].item.lootValue > bestValue)
                {
                    bestValue = _entries[i].item.lootValue;
                    best = i;
                }
            }

            var entry = _entries[best];
            _entries.RemoveAt(best);
            MarkCells(entry, false);
            RaidEvents.RaiseInventoryChanged(_owner);
            return entry.item;
        }

        public void Clear()
        {
            if (_entries.Count == 0) return;
            _entries.Clear();
            if (_occupied != null) _occupied = new bool[_w, _h];
            RaidEvents.RaiseInventoryChanged(_owner);
        }

        /// <summary>供跨关卡传递：导出 itemId 列表。</summary>
        public List<string> ExportIds()
        {
            var ids = new List<string>(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].item != null) ids.Add(_entries[i].item.itemId);
            return ids;
        }

        /// <summary>调试用摘要。</summary>
        public string Describe()
        {
            if (_entries.Count == 0) return "（空手）";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].item == null) continue;
                sb.Append(_entries[i].item.displayName);
                if (i < _entries.Count - 1) sb.Append('、');
            }
            return sb.ToString();
        }
    }
}
