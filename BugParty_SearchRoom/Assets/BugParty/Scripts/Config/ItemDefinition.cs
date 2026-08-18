using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 单个道具的定义。策划改数值不需要动代码。
    /// 通过 Assets ▸ Create ▸ BugParty ▸ Item Definition 创建。
    /// </summary>
    [CreateAssetMenu(fileName = "Item_", menuName = "BugParty/Item Definition", order = 10)]
    public class ItemDefinition : ScriptableObject
    {
        [Header("身份")]
        [Tooltip("显示名，用于 HUD 与调试日志")]
        public string displayName = "新道具";

        [Tooltip("唯一 ID，用于存档与跨关卡传递。建议用英文，如 net_large")]
        public string itemId = "new_item";

        public ItemCategory category = ItemCategory.Fishing;

        [Header("外观")]
        [Tooltip("留空则由建场工具生成占位方块")]
        public GameObject worldPrefab;

        [Tooltip("占位体的颜色（未指定 Prefab 时使用）")]
        public Color placeholderColor = Color.white;

        [Tooltip("占位体的尺寸（未指定 Prefab 时使用）")]
        public Vector3 placeholderSize = new Vector3(0.35f, 0.35f, 0.35f);

        [Header("搜索权重")]
        [Tooltip("在道具池里被抽中的相对权重。越大越常见")]
        [Min(0.01f)]
        public float spawnWeight = 1f;

        [Header("下一关效果（占位，供后续关卡读取）")]
        [Tooltip("一句话描述它在下一关会做什么，仅用于策划沟通与 Demo 提示")]
        [TextArea(2, 4)]
        public string effectSummary = "";
    }
}
