using System.Collections.Generic;
using UnityEngine;

namespace BugParty.FPS
{
    [System.Serializable]
    public class ThemeItemPool
    {
        public RoomTheme theme = RoomTheme.Fishing;
        public List<ItemDefinition> items = new List<ItemDefinition>();
    }

    /// <summary>
    /// FPS 密室搜刮环节的全部参数。
    /// Assets ▸ Create ▸ BugPartyFPS ▸ Raid Config
    /// </summary>
    [CreateAssetMenu(fileName = "RaidConfig", menuName = "BugPartyFPS/Raid Config", order = 0)]
    public class RaidConfig : ScriptableObject
    {
        // ═══════════════════════════════════════════════
        [Header("═══ 回合时间 ═══")]
        [Min(0f)] public float introDuration = 3f;

        [Tooltip("搜刮阶段时长")]
        [Min(1f)] public float lootDuration = 45f;

        [Tooltip("★撤离窗口时长。传送门开启后必须在这段时间内跑进去")]
        [Min(1f)] public float extractionWindow = 15f;

        [Min(0f)] public float settlementDuration = 3f;

        [Tooltip("最后几秒进入紧张提示")]
        [Min(0f)] public float urgentThreshold = 8f;

        // ═══════════════════════════════════════════════
        [Header("═══ 第一人称移动 ═══")]
        [Min(0.1f)] public float walkSpeed = 3.2f;
        [Min(0.1f)] public float sprintSpeed = 6.0f;
        [Min(0.1f)] public float crouchSpeed = 1.5f;

        [Tooltip("加速平滑度，越小越灵敏")]
        [Range(0.01f, 0.5f)] public float moveSmoothing = 0.09f;

        [Min(0f)] public float jumpHeight = 1.05f;
        [Min(0f)] public float gravity = 22f;

        [Tooltip("站立时相机高度")]
        [Min(0.5f)] public float standEyeHeight = 1.62f;

        [Tooltip("下蹲时相机高度")]
        [Min(0.3f)] public float crouchEyeHeight = 0.95f;

        [Tooltip("蹲起过渡速度")]
        [Min(1f)] public float stanceLerpSpeed = 9f;

        // ═══════════════════════════════════════════════
        [Header("═══ 鼠标视角 ═══")]
        [Min(0.01f)] public float mouseSensitivity = 2.2f;

        [Tooltip("俯仰角限制")]
        [Range(60f, 89f)] public float pitchClamp = 88f;

        [Tooltip("视野角。塔科夫风格建议 70～80")]
        [Range(50f, 110f)] public float fieldOfView = 76f;

        [Tooltip("疾跑时 FOV 增量，制造速度感")]
        [Range(0f, 20f)] public float sprintFovBoost = 8f;

        // ═══════════════════════════════════════════════
        [Header("═══ 头部摆动（沉浸感）═══")]
        [Tooltip("走路时相机上下摆动幅度")]
        [Range(0f, 0.15f)] public float bobAmplitude = 0.045f;

        [Range(1f, 20f)] public float bobFrequency = 9f;

        [Tooltip("疾跑时摆动倍率")]
        [Range(1f, 3f)] public float sprintBobMultiplier = 1.7f;

        // ═══════════════════════════════════════════════
        [Header("═══ ★网格背包 ═══")]
        [Tooltip("背包宽度（格）")]
        [Range(2, 8)] public int gridWidth = 4;

        [Tooltip("背包高度（格）")]
        [Range(1, 6)] public int gridHeight = 2;

        [Tooltip("勾选后忽略道具体积，退回「最多 N 件」的计数模式")]
        public bool useSimpleCountMode = false;

        [Tooltip("计数模式下的容量上限")]
        [Range(1, 6)] public int simpleCapacity = 2;

        // ═══════════════════════════════════════════════
        [Header("═══ ★搜刮容器 ═══")]
        [Tooltip("准星能触发交互提示的最大距离")]
        [Min(0.5f)] public float interactDistance = 2.4f;

        [Tooltip("搜索一个容器的读条时间")]
        [Min(0.1f)] public float searchTime = 3.0f;

        [Tooltip("被打断后容器进入的冷却")]
        [Min(0f)] public float containerCooldown = 1.5f;

        [Tooltip("每个容器生成几件战利品")]
        [Range(1, 6)] public int lootPerContainer = 3;

        [Tooltip("搜索时移动超过这个距离就中断")]
        [Min(0.1f)] public float searchBreakDistance = 1.2f;

        [Tooltip("★开着搜刮界面时是否强制禁止移动。塔科夫式紧张感的关键")]
        public bool lockMovementWhileLooting = true;

        // ═══════════════════════════════════════════════
        [Header("═══ ★近战肘击 ═══")]
        [Min(0.1f)] public float meleeRange = 2.0f;

        [Tooltip("命中判定的球体半径，越大越容易打中")]
        [Min(0.1f)] public float meleeRadius = 0.55f;

        [Min(0f)] public float meleeCooldown = 0.85f;

        [Tooltip("挥击前摇")]
        [Min(0f)] public float meleeWindup = 0.15f;

        [Tooltip("被击中后的硬直时间，期间无法行动")]
        [Min(0f)] public float staggerDuration = 1.1f;

        [Tooltip("被击中后的视角摇晃强度")]
        [Range(0f, 30f)] public float hitCameraShake = 14f;

        [Min(0f)] public float knockbackForce = 7f;

        [Tooltip("★命中背对自己的目标时的伤害倍率。鼓励背刺")]
        [Range(1f, 4f)] public float backstabMultiplier = 2f;

        [Tooltip("命中时打落几件战利品")]
        [Range(0, 4)] public int itemsKnockedPerHit = 1;

        [Tooltip("★背刺时打落几件。数值更高，让偷袭更有回报")]
        [Range(0, 6)] public int itemsKnockedOnBackstab = 2;

        [Min(0f)] public float droppedItemPopForce = 4f;
        [Min(0f)] public float droppedItemPickupDelay = 0.5f;

        // ═══════════════════════════════════════════════
        [Header("═══ ★噪音系统（潜行核心）═══")]
        [Tooltip("站立行走的噪音半径")]
        [Min(0f)] public float walkNoiseRadius = 8f;

        [Tooltip("疾跑的噪音半径。AI 在这个范围内能听到你")]
        [Min(0f)] public float sprintNoiseRadius = 16f;

        [Tooltip("下蹲移动的噪音半径。接近 0 表示几乎无声")]
        [Min(0f)] public float crouchNoiseRadius = 2f;

        [Tooltip("搜刮容器时发出的噪音半径。翻箱子是有声音的")]
        [Min(0f)] public float lootNoiseRadius = 6f;

        [Tooltip("噪音衰减时间")]
        [Min(0.05f)] public float noiseDecay = 0.5f;

        // ═══════════════════════════════════════════════
        [Header("═══ ★撤离点 ═══")]
        [Tooltip("站在撤离点内需要持续多久才算撤离成功")]
        [Min(0f)] public float extractHoldTime = 2.5f;

        [Tooltip("离开撤离点是否立刻重置进度")]
        public bool resetExtractOnLeave = true;

        // ═══════════════════════════════════════════════
        [Header("═══ AI ═══")]
        [Min(0.05f)] public float aiDecisionInterval = 0.4f;

        [Tooltip("AI 的视野角度")]
        [Range(30f, 180f)] public float aiViewAngle = 110f;

        [Tooltip("AI 的视野距离")]
        [Min(1f)] public float aiViewDistance = 14f;

        [Tooltip("AI 攻击倾向")]
        [Range(0f, 1f)] public float aiAggressiveness = 0.5f;

        [Tooltip("AI 反应延迟，越大越笨")]
        [Min(0f)] public float aiReactionDelay = 0.3f;

        [Tooltip("★AI 发现你在搜刮时的额外攻击权重。这是最有戏的时机")]
        [Range(0f, 1f)] public float aiLootingTargetBonus = 0.4f;

        [Tooltip("AI 在剩余多少秒时开始往撤离点跑")]
        [Min(1f)] public float aiExtractPanicTime = 12f;

        // ═══════════════════════════════════════════════
        [Header("═══ 道具池 ═══")]
        public List<ThemeItemPool> itemPools = new List<ThemeItemPool>();

        // ── 查询 ──────────────────────────────────────

        public List<ItemDefinition> GetPool(RoomTheme theme)
        {
            for (int i = 0; i < itemPools.Count; i++)
                if (itemPools[i] != null && itemPools[i].theme == theme && itemPools[i].items.Count > 0)
                    return itemPools[i].items;

            for (int i = 0; i < itemPools.Count; i++)
                if (itemPools[i] != null && itemPools[i].items.Count > 0)
                    return itemPools[i].items;

            return new List<ItemDefinition>();
        }

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

        /// <summary>按姿态取得移速。</summary>
        public float GetSpeed(Stance stance)
        {
            switch (stance)
            {
                case Stance.Sprint: return sprintSpeed;
                case Stance.Crouch: return crouchSpeed;
                default:            return walkSpeed;
            }
        }

        /// <summary>按姿态取得噪音半径。</summary>
        public float GetNoiseRadius(Stance stance)
        {
            switch (stance)
            {
                case Stance.Sprint: return sprintNoiseRadius;
                case Stance.Crouch: return crouchNoiseRadius;
                default:            return walkNoiseRadius;
            }
        }
    }
}
