using System.Collections.Generic;
using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 可搜刮容器。塔科夫式两段流程：
    ///   1. 未搜状态：需要读条搜索
    ///   2. 已搜状态：内容公开，任何人可以直接开界面拿剩下的东西
    ///
    /// 第二点很重要——被别人搜过的箱子里可能还剩他装不下的好东西。
    /// </summary>
    public class LootContainer : MonoBehaviour
    {
        [Header("身份")]
        public string containerName = "文件柜";

        [Tooltip("交互点，玩家需要靠近这里。留空用自身位置")]
        public Transform interactAnchor;

        [Header("战利品")]
        [Tooltip("这个容器最多生成几件。0 表示用 Config 的默认值")]
        [Range(0, 6)] public int lootCountOverride = 0;

        [Tooltip("稀有度加成：越高越容易出好东西")]
        [Range(0f, 2f)] public float rarityBonus = 0f;

        [Header("视觉")]
        public Renderer highlightRenderer;

        [Tooltip("未搜过的颜色偏亮，提示可交互")]
        public Color unsearchedTint = new Color(0.62f, 0.55f, 0.38f);

        [Tooltip("已搜空的颜色")]
        public Color emptiedColor = new Color(0.28f, 0.28f, 0.31f);

        // ── 运行时 ─────────────────────────────────────
        readonly List<ItemDefinition> _loot = new List<ItemDefinition>();

        PlayerRig _claimer;     // 正在读条搜索的人
        PlayerRig _viewer;      // 正在开着界面看的人
        float _cooldownUntil;
        bool _searched;
        Color _baseColor;
        bool _colorCached;

        public bool IsSearched => _searched;
        public bool HasLootLeft => _loot.Count > 0;
        public int LootCount => _loot.Count;
        public PlayerRig Claimer => _claimer;
        public PlayerRig Viewer => _viewer;
        public bool IsClaimed => _claimer != null;
        public bool IsBeingViewed => _viewer != null;
        public bool IsCoolingDown => Time.time < _cooldownUntil;

        public Vector3 InteractPoint =>
            interactAnchor != null ? interactAnchor.position : transform.position;

        /// <summary>容器内战利品的总价值。AI 挑目标时参考。</summary>
        public int TotalLootValue
        {
            get
            {
                int v = 0;
                for (int i = 0; i < _loot.Count; i++)
                    if (_loot[i] != null) v += _loot[i].lootValue;
                return v;
            }
        }

        void Awake()
        {
            if (highlightRenderer == null)
                highlightRenderer = GetComponentInChildren<Renderer>();
            CacheBaseColor();
        }

        void CacheBaseColor()
        {
            if (_colorCached || highlightRenderer == null) return;
            var m = highlightRenderer.material;
            if (m.HasProperty("_BaseColor")) _baseColor = m.GetColor("_BaseColor");
            else if (m.HasProperty("_Color")) _baseColor = m.GetColor("_Color");
            else _baseColor = Color.white;
            _colorCached = true;
        }

        // ── 可用性 ─────────────────────────────────────

        /// <summary>这个玩家现在能不能开始搜索（读条阶段）。</summary>
        public bool IsAvailableFor(PlayerRig asker)
        {
            if (_searched) return false;               // 已搜过就不需要再读条
            if (IsCoolingDown) return false;
            if (IsClaimed && _claimer != asker) return false;
            if (IsBeingViewed && _viewer != asker) return false;
            return true;
        }

        /// <summary>能不能直接开界面（已搜过且还有东西）。</summary>
        public bool CanOpenDirectly(PlayerRig asker)
        {
            if (!_searched) return false;
            if (!HasLootLeft) return false;
            if (IsBeingViewed && _viewer != asker) return false;
            return true;
        }

        public bool TryClaim(PlayerRig rig)
        {
            if (!IsAvailableFor(rig)) return false;
            _claimer = rig;
            return true;
        }

        public void Release(PlayerRig rig, bool interrupted)
        {
            if (_claimer != rig) return;
            _claimer = null;

            if (interrupted)
            {
                var cfg = RaidManager.Instance != null ? RaidManager.Instance.config : null;
                if (cfg != null) _cooldownUntil = Time.time + cfg.containerCooldown;
            }
        }

        public void SetViewer(PlayerRig rig, bool viewing)
        {
            if (viewing)
            {
                if (_viewer == null) _viewer = rig;
            }
            else if (_viewer == rig)
            {
                _viewer = null;
            }
        }

        // ── 战利品生成 ─────────────────────────────────

        /// <summary>
        /// 读条完成时调用，随机生成这个容器的内容。
        /// 延迟到搜索完成才生成，玩家在搜之前完全不知道里面有什么——这是紧张感的一半。
        /// </summary>
        public void GenerateLoot()
        {
            if (_searched) return;
            _searched = true;
            _loot.Clear();

            var mgr = RaidManager.Instance;
            if (mgr == null || mgr.config == null) return;

            int count = lootCountOverride > 0 ? lootCountOverride : mgr.config.lootPerContainer;

            for (int i = 0; i < count; i++)
            {
                var item = mgr.config.RollItem(mgr.theme);
                if (item == null) continue;

                // 稀有度加成：有几率把普通道具替换成稀有的
                if (rarityBonus > 0f && Random.value < rarityBonus * 0.3f)
                {
                    var rare = FindRareItem(mgr);
                    if (rare != null) item = rare;
                }
                _loot.Add(item);
            }

            ApplyTint();
        }

        ItemDefinition FindRareItem(RaidManager mgr)
        {
            var pool = mgr.config.GetPool(mgr.theme);
            var rares = new List<ItemDefinition>();
            for (int i = 0; i < pool.Count; i++)
                if (pool[i] != null && pool[i].isRare) rares.Add(pool[i]);

            return rares.Count > 0 ? rares[Random.Range(0, rares.Count)] : null;
        }

        public ItemDefinition PeekLoot(int index)
        {
            if (index < 0 || index >= _loot.Count) return null;
            return _loot[index];
        }

        public bool RemoveLootAt(int index)
        {
            if (index < 0 || index >= _loot.Count) return false;
            _loot.RemoveAt(index);
            if (_loot.Count == 0) ApplyTint();
            return true;
        }

        // ── 视觉 ───────────────────────────────────────

        void ApplyTint()
        {
            if (highlightRenderer == null) return;

            Color c;
            if (!_searched) c = unsearchedTint;
            else if (HasLootLeft) c = Color.Lerp(unsearchedTint, emptiedColor, 0.45f);
            else c = emptiedColor;

            var m = highlightRenderer.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public void ResetForNewRound()
        {
            _loot.Clear();
            _claimer = null;
            _viewer = null;
            _cooldownUntil = 0f;
            _searched = false;

            if (highlightRenderer != null && _colorCached)
            {
                var m = highlightRenderer.material;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", unsearchedTint);
                if (m.HasProperty("_Color")) m.SetColor("_Color", unsearchedTint);
            }
        }

        void Start()
        {
            ApplyTint();
        }

        void OnDrawGizmos()
        {
            Gizmos.color = !_searched ? new Color(0.4f, 0.95f, 0.5f)
                         : (HasLootLeft ? new Color(0.95f, 0.8f, 0.3f) : Color.gray);
            Gizmos.DrawWireSphere(InteractPoint, 0.3f);
        }
    }
}
