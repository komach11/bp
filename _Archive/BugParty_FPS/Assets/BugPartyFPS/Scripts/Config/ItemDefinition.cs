using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 道具定义。相比俯视版增加了「体积」概念——塔科夫式网格背包的核心。
    /// Assets ▸ Create ▸ BugPartyFPS ▸ Item Definition
    /// </summary>
    [CreateAssetMenu(fileName = "Item_", menuName = "BugPartyFPS/Item Definition", order = 10)]
    public class ItemDefinition : ScriptableObject
    {
        [Header("身份")]
        public string displayName = "新道具";
        public string itemId = "new_item";
        public ItemCategory category = ItemCategory.Fishing;

        [Header("★ 网格体积（塔科夫式背包）")]
        [Tooltip("占用宽度（格）")]
        [Range(1, 4)] public int gridWidth = 1;

        [Tooltip("占用高度（格）")]
        [Range(1, 4)] public int gridHeight = 1;

        [Header("价值")]
        [Tooltip("战利品评分。撤离成功后计入总分，用于排名")]
        [Min(0)] public int lootValue = 100;

        [Header("外观")]
        public GameObject worldPrefab;
        public Color placeholderColor = Color.white;
        public Vector3 placeholderSize = new Vector3(0.3f, 0.3f, 0.3f);

        [Header("搜刮权重")]
        [Min(0.01f)] public float spawnWeight = 1f;

        [Tooltip("稀有道具：搜到时会有额外的音效与高亮提示")]
        public bool isRare = false;

        [Header("下一关效果说明（供策划核对）")]
        [TextArea(2, 4)] public string effectSummary = "";

        /// <summary>该道具占用的格子总数。</summary>
        public int GridArea => gridWidth * gridHeight;

        /// <summary>每格的价值密度。AI 判断「值不值得占位」时用。</summary>
        public float ValueDensity => GridArea > 0 ? (float)lootValue / GridArea : 0f;
    }
}
