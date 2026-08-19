using System.Collections.Generic;
using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 玩家背包。容量上限由 Config 控制（团队已锁定为 2）。
    /// </summary>
    public class PlayerInventory : MonoBehaviour
    {
        readonly List<ItemDefinition> _items = new List<ItemDefinition>();

        PlayerActor _actor;
        int _capacity = 2;

        public IReadOnlyList<ItemDefinition> Items => _items;
        public int Count => _items.Count;
        public int Capacity => _capacity;
        public bool IsFull => _items.Count >= _capacity;
        public bool IsEmpty => _items.Count == 0;

        public void Init(PlayerActor actor, int capacity)
        {
            _actor = actor;
            _capacity = Mathf.Max(1, capacity);
            _items.Clear();
        }

        public bool TryAdd(ItemDefinition item)
        {
            if (item == null || IsFull) return false;
            _items.Add(item);
            SearchRoomEvents.RaiseInventoryChanged(_actor);
            return true;
        }

        /// <summary>
        /// 移除最后拿到的一件道具并返回它。肘击打落时调用。
        /// 打掉最新拿到的那件，喜剧效果最好——刚拿到就被抢走。
        /// </summary>
        public ItemDefinition PopLatest()
        {
            if (IsEmpty) return null;
            int last = _items.Count - 1;
            var item = _items[last];
            _items.RemoveAt(last);
            SearchRoomEvents.RaiseInventoryChanged(_actor);
            return item;
        }

        public void Clear()
        {
            if (_items.Count == 0) return;
            _items.Clear();
            SearchRoomEvents.RaiseInventoryChanged(_actor);
        }

        /// <summary>供跨关卡传递用：导出 itemId 列表。</summary>
        public List<string> ExportIds()
        {
            var ids = new List<string>(_items.Count);
            for (int i = 0; i < _items.Count; i++)
                if (_items[i] != null) ids.Add(_items[i].itemId);
            return ids;
        }
    }
}
