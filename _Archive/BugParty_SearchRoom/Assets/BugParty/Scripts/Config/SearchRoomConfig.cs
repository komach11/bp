using System.Collections.Generic;
using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 一个主题的道具池。三个主题各一份。
    /// </summary>
    [System.Serializable]
    public class ThemeItemPool
    {
        public RoomTheme theme = RoomTheme.Fishing;

        [Tooltip("该主题下会掉落 / 可被搜出的道具")]
        public List<ItemDefinition> items = new List<ItemDefinition>();
    }

    /// <summary>
    /// 密室搜索环节的全部可调参数。
    /// 通过 Assets ▸ Create ▸ BugParty ▸ Search Room Config 创建。
    /// </summary>
    [CreateAssetMenu(fileName = "SearchRoomConfig", menuName = "BugParty/Search Room Config", order = 0)]
    public class SearchRoomConfig : ScriptableObject
    {
        [Header("═══ 回合时间 ═══")]
        [Tooltip("入场演出时长：四人掉落、门关闭，此期间不可操作")]
        [Min(0f)] public float introDuration = 2.5f;

        [Tooltip("搜索阶段总时长（秒）。PV 分镜对应约 18 秒，Demo 建议 25～30 秒更好玩")]
        [Min(1f)] public float searchDuration = 25f;

        [Tooltip("结算展示时长：锁定道具、展示各人结果")]
        [Min(0f)] public float settlementDuration = 2f;

        [Tooltip("传送阶段时长：四人被依次吸走")]
        [Min(0f)] public float teleportDuration = 2f;

        [Tooltip("最后几秒进入紧张状态：HUD 变红、滴答加速")]
        [Min(0f)] public float urgentThreshold = 5f;

        [Header("═══ 背包规则 ═══")]
        [Tooltip("每人最多携带的道具数。团队已锁定为 2")]
        [Range(1, 4)] public int inventoryCapacity = 2;

        [Header("═══ 移动 ═══")]
        [Min(0.1f)] public float moveSpeed = 4.5f;

        [Tooltip("转身速度（度/秒）")]
        [Min(1f)] public float turnSpeed = 720f;

        [Tooltip("被肘击后的硬直时间，期间无法移动与操作")]
        [Min(0f)] public float staggerDuration = 0.7f;

        [Header("═══ 搜索行为 ═══")]
        [Tooltip("搜索一个容器需要的读条时间")]
        [Min(0.1f)] public float searchTime = 2.2f;

        [Tooltip("可以开始搜索的最大距离")]
        [Min(0.1f)] public float searchRange = 1.6f;

        [Tooltip("搜索被打断后，该容器进入的冷却时间")]
        [Min(0f)] public float containerCooldown = 1.2f;

        [Tooltip("每个容器可以被成功搜出几件道具后枯竭")]
        [Range(1, 5)] public int containerYield = 2;

        [Header("═══ 肘击（核心笑点）═══")]
        [Tooltip("肘击的有效距离")]
        [Min(0.1f)] public float elbowRange = 1.5f;

        [Tooltip("肘击的有效角度（以自身朝向为中心的锥形半角）")]
        [Range(10f, 180f)] public float elbowAngle = 70f;

        [Tooltip("肘击冷却时间，防止连续按")]
        [Min(0f)] public float elbowCooldown = 0.9f;

        [Tooltip("肘击前摇：挥肘动作到判定生效的延迟")]
        [Min(0f)] public float elbowWindup = 0.12f;

        [Tooltip("被肘击者受到的击退力度")]
        [Min(0f)] public float elbowKnockback = 6f;

        [Tooltip("命中时是否打落对方一件已收集的道具")]
        public bool elbowKnocksOutItem = true;

        [Tooltip("被打落的道具飞出的力度")]
        [Min(0f)] public float itemPopForce = 4.5f;

        [Tooltip("被打落的道具落地后多久可以重新拾取")]
        [Min(0f)] public float droppedItemPickupDelay = 0.4f;

        [Header("═══ 道具池 ═══")]
        public List<ThemeItemPool> itemPools = new List<ThemeItemPool>();

        [Header("═══ AI ═══")]
        [Tooltip("AI 重新决策的间隔")]
        [Min(0.05f)] public float aiDecisionInterval = 0.35f;

        [Tooltip("AI 在多近的距离内会考虑肘击对手")]
        [Min(0.1f)] public float aiAggroRange = 2.2f;

        [Tooltip("AI 每次决策时选择攻击而非继续搜索的概率")]
        [Range(0f, 1f)] public float aiAggressiveness = 0.45f;

        [Tooltip("AI 反应延迟，数值越大越笨")]
        [Min(0f)] public float aiReactionDelay = 0.25f;

        /// <summary>
        /// 按主题取出道具池。找不到时回退到第一个非空池。
        /// </summary>
        public List<ItemDefinition> GetPool(RoomTheme theme)
        {
            for (int i = 0; i < itemPools.Count; i++)
            {
                if (itemPools[i] != null && itemPools[i].theme == theme && itemPools[i].items.Count > 0)
                    return itemPools[i].items;
            }
            for (int i = 0; i < itemPools.Count; i++)
            {
                if (itemPools[i] != null && itemPools[i].items.Count > 0)
                    return itemPools[i].items;
            }
            return new List<ItemDefinition>();
        }

        /// <summary>
        /// 按 spawnWeight 加权随机抽一件道具。
        /// </summary>
        public ItemDefinition RollItem(RoomTheme theme)
        {
            var pool = GetPool(theme);
            if (pool.Count == 0) return null;

            float total = 0f;
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null) total += Mathf.Max(0.01f, pool[i].spawnWeight);

            if (total <= 0f) return pool[0];

            float roll = Random.Range(0f, total);
            float acc = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null) continue;
                acc += Mathf.Max(0.01f, pool[i].spawnWeight);
                if (roll <= acc) return pool[i];
            }
            return pool[pool.Count - 1];
        }
    }
}
