using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 一名玩家从密室带出的东西。
    /// slotIndex 与捕鱼场景的大厅槽位对应，是两个场景之间唯一可靠的身份纽带
    /// （PlayerColor 只是密室内部的表现，捕鱼侧不认识）。
    /// </summary>
    [System.Serializable]
    public class CarriedLoadout
    {
        [Tooltip("大厅槽位索引 0~3。捕鱼场景按它把道具发给对应玩家。")]
        public int slotIndex;

        [Tooltip("是否 AI。密室的 AIBrain 对应捕鱼侧的 PartyPlayerAI。")]
        public bool isBot;

        [Tooltip("密室内部的队伍配色，仅用于结算界面显示")]
        public PlayerColor color;

        [Tooltip("带出的道具 id，顺序即背包顺序")]
        public List<string> itemIds = new List<string>();

        [Tooltip("道具中文名，仅用于结算界面显示")]
        public List<string> itemNames = new List<string>();

        public int Count => itemIds != null ? itemIds.Count : 0;
    }

    /// <summary>
    /// 密室 → 下一个玩法场景的跨场景交接。
    ///
    /// 设计要点：
    ///   · 按 slotIndex 索引而非 PlayerColor —— 捕鱼场景的 LanLobbyManager
    ///     用 slotIndex 标识玩家，这是两边唯一对得上的键。
    ///   · 只存 string id 而非 ScriptableObject 引用 —— 两个场景的道具资产
    ///     是各自独立的（密室是 ItemDefinition，捕鱼是 ItemDataSO），
    ///     不能直接传对象引用。
    ///   · 静态类在单机与「主机自己读自己写」的场合够用；
    ///     联机时需由服务端在生成玩家后注入，客户端不读本类。
    /// </summary>
    public static class CarryOverData
    {
        static readonly Dictionary<int, CarriedLoadout> _bySlot
            = new Dictionary<int, CarriedLoadout>();

        /// <summary>从哪个场景带出来的，便于下一关判断上一环节是什么玩法。</summary>
        public static string SourceScene = "";

        public static void Set(CarriedLoadout loadout)
        {
            if (loadout == null) return;
            _bySlot[loadout.slotIndex] = loadout;
        }

        public static CarriedLoadout Get(int slotIndex)
            => _bySlot.TryGetValue(slotIndex, out var v) ? v : null;

        /// <summary>取某槽位带出的道具 id。没有数据时返回空列表而非 null。</summary>
        public static List<string> GetItemIds(int slotIndex)
        {
            var l = Get(slotIndex);
            return l != null && l.itemIds != null ? l.itemIds : new List<string>();
        }

        public static IEnumerable<CarriedLoadout> All => _bySlot.Values;

        public static int SlotCount => _bySlot.Count;

        public static bool HasData => _bySlot.Count > 0;

        public static void Clear()
        {
            _bySlot.Clear();
            SourceScene = "";
        }

        // ══════════════════════════════════════════════
        //  与下一关的道具体系映射
        // ══════════════════════════════════════════════

        /// <summary>
        /// 密室道具 id → 捕鱼场景 ItemKind 的名字。
        ///
        /// 捕鱼场景的 PartyGame.ItemKind 只有 4 个值：
        ///   SmallNet / LargeNet / Knife / Mine
        /// 这里用字符串而不直接引用枚举，是为了让本工程不依赖捕鱼场景的程序集；
        /// 捕鱼侧读取时用 System.Enum.TryParse 转回枚举即可。
        /// </summary>
        static readonly Dictionary<string, string> _toNextLevelKind
            = new Dictionary<string, string>
            {
                { "net_large", "LargeNet" },
                { "net_small", "SmallNet" },
                { "knife",     "Knife"    },
                { "mine",      "Mine"     },
            };

        /// <summary>
        /// 取某道具在下一关对应的 ItemKind 名。
        /// 返回 null 表示下一关没有对应实现，调用方应跳过。
        /// </summary>
        public static string ToNextLevelKind(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            return _toNextLevelKind.TryGetValue(itemId, out var k) ? k : null;
        }

        /// <summary>某槽位带出的道具，转成下一关的 ItemKind 名列表（已剔除无对应的）。</summary>
        public static List<string> GetNextLevelKinds(int slotIndex)
        {
            var result = new List<string>();
            foreach (var id in GetItemIds(slotIndex))
            {
                var k = ToNextLevelKind(id);
                if (!string.IsNullOrEmpty(k)) result.Add(k);
            }
            return result;
        }

        /// <summary>调试用：把全部交接数据打成可读文本。</summary>
        public static string Dump()
        {
            if (!HasData) return "（无交接数据）";
            var sb = new System.Text.StringBuilder();
            foreach (var l in _bySlot.Values)
            {
                sb.Append($"  slot{l.slotIndex} {(l.isBot ? "[AI]" : "[玩家]")} " +
                          $"{l.color.ToLabel()}方 → ");
                sb.Append(l.Count > 0 ? string.Join("、", l.itemNames) : "空手");
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
