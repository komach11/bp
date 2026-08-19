using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 搜刮行为。两段式流程，这是塔科夫紧张感的核心：
    ///
    ///   阶段一：读条搜索（可移动打断）
    ///   阶段二：★打开搜刮界面挑东西（视野被占、移动被锁 → 你是活靶子）
    ///
    /// 关键设计：真正危险的不是读条，而是「低头翻包」的那几秒。
    /// </summary>
    public class LootAction : MonoBehaviour
    {
        PlayerRig _rig;
        RaidConfig _cfg;

        LootContainer _target;
        float _progress;
        Vector3 _searchStartPos;

        // 搜刮界面
        LootContainer _openContainer;
        float _noiseTimer;

        public bool IsSearching => _target != null;
        public bool IsPanelOpen => _openContainer != null;
        public LootContainer OpenContainer => _openContainer;
        public LootContainer SearchTarget => _target;

        public float Progress01 => _cfg != null && _cfg.searchTime > 0f
            ? Mathf.Clamp01(_progress / _cfg.searchTime) : 0f;

        public void Init(PlayerRig rig, RaidConfig cfg)
        {
            _rig = rig;
            _cfg = cfg;
        }

        // ══════════════════════════════════════════════
        //  瞄准检测
        // ══════════════════════════════════════════════

        /// <summary>
        /// 用准星射线找出正在看的容器。HUD 的交互提示与按键判断都用它。
        /// </summary>
        public LootContainer GetAimedContainer()
        {
            if (_cfg == null) return null;

            Transform origin = _rig.eyeAnchor;
            if (origin == null) return null;

            // 本地玩家用相机朝向（含 pitch），AI 用身体朝向
            Vector3 dir = origin.forward;
            var look = GetComponent<FirstPersonLook>();
            if (look != null && look.cameraTransform != null)
                dir = look.cameraTransform.forward;

            var hits = Physics.RaycastAll(origin.position, dir, _cfg.interactDistance);
            if (hits == null || hits.Length == 0) return null;

            // 按距离排序，取第一个"实体"命中
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                // 忽略自己的碰撞体（射线起点在自己身体内）
                if (hits[i].collider.GetComponentInParent<PlayerRig>() == _rig) continue;

                // 忽略地上的掉落物，它们不该挡住后面的容器
                if (hits[i].collider.GetComponentInParent<DroppedLoot>() != null) continue;

                var c = hits[i].collider.GetComponentInParent<LootContainer>();
                if (c != null) return c;

                // ★命中了容器以外的实体（墙、隔断、别的玩家）→ 视线被挡住，
                // 不能穿墙搜刮，直接返回 null
                return null;
            }
            return null;
        }

        // ══════════════════════════════════════════════
        //  阶段一：读条搜索
        // ══════════════════════════════════════════════

        /// <summary>尝试开始搜索。container 为 null 时自动用准星瞄到的。</summary>
        public bool TryBeginSearch(LootContainer container = null)
        {
            if (IsSearching || IsPanelOpen) return false;
            if (_rig.IsStaggered) return false;

            var mgr = RaidManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;

            var c = container != null ? container : GetAimedContainer();
            if (c == null) return false;

            // 已经搜过的容器直接开界面，不用重复读条
            if (c.IsSearched)
            {
                if (c.HasLootLeft) return OpenPanel(c);
                return false;
            }

            if (!c.IsAvailableFor(_rig)) return false;
            if (!c.TryClaim(_rig)) return false;

            _target = c;
            _progress = 0f;
            _searchStartPos = transform.position;
            RaidEvents.RaiseLootStarted(_rig, c);
            return true;
        }

        public void CancelSearch(bool interrupted)
        {
            if (_target == null) return;

            var c = _target;
            _target = null;
            _progress = 0f;

            c.Release(_rig, interrupted);
            if (interrupted) RaidEvents.RaiseLootInterrupted(_rig, c);
        }

        void CompleteSearch()
        {
            var c = _target;
            _target = null;
            _progress = 0f;

            // 生成这个容器的战利品清单
            c.GenerateLoot();
            c.Release(_rig, false);

            // 读条完成后自动打开搜刮界面
            OpenPanel(c);
        }

        // ══════════════════════════════════════════════
        //  阶段二：★搜刮界面（危险窗口）
        // ══════════════════════════════════════════════

        public bool OpenPanel(LootContainer c)
        {
            if (c == null || !c.HasLootLeft) return false;
            if (IsPanelOpen) return false;

            var mgr = RaidManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;

            _openContainer = c;
            c.SetViewer(_rig, true);
            RaidEvents.RaiseLootPanelToggled(_rig, c, true);
            return true;
        }

        public void ClosePanel()
        {
            if (_openContainer == null) return;

            var c = _openContainer;
            _openContainer = null;
            c.SetViewer(_rig, false);
            RaidEvents.RaiseLootPanelToggled(_rig, c, false);
        }

        /// <summary>
        /// 从打开的容器里拿走第 index 件战利品。
        /// 装不下会返回 false，HUD 应提示「空间不足」。
        /// </summary>
        public bool TakeLoot(int index)
        {
            if (_openContainer == null) return false;

            var item = _openContainer.PeekLoot(index);
            if (item == null) return false;

            if (!_rig.Inventory.CanFit(item)) return false;

            if (!_openContainer.RemoveLootAt(index)) return false;
            if (!_rig.Inventory.TryAdd(item)) return false;

            RaidEvents.RaiseLootTaken(_rig, item);

            // 拿东西也有声音
            _rig.EmitNoise(_cfg.lootNoiseRadius * 0.6f);

            // 容器掏空后自动关界面
            if (!_openContainer.HasLootLeft) ClosePanel();
            return true;
        }

        /// <summary>一键拿走所有装得下的东西。方便快速游玩。</summary>
        public int TakeAllPossible()
        {
            if (_openContainer == null) return 0;

            int taken = 0;
            // 从后往前遍历，避免移除时索引错位
            for (int i = _openContainer.LootCount - 1; i >= 0; i--)
            {
                var item = _openContainer.PeekLoot(i);
                if (item == null) continue;
                if (!_rig.Inventory.CanFit(item)) continue;

                if (_openContainer.RemoveLootAt(i) && _rig.Inventory.TryAdd(item))
                {
                    RaidEvents.RaiseLootTaken(_rig, item);
                    taken++;
                }
            }

            if (taken > 0) _rig.EmitNoise(_cfg.lootNoiseRadius);
            if (!_openContainer.HasLootLeft) ClosePanel();
            return taken;
        }

        /// <summary>强制中断一切搜刮行为。被击中时调用。</summary>
        public void ForceAbort()
        {
            CancelSearch(true);
            ClosePanel();
        }

        // ══════════════════════════════════════════════

        void Update()
        {
            var mgr = RaidManager.Instance;
            if (mgr == null || !mgr.CanAct)
            {
                if (IsSearching || IsPanelOpen) ForceAbort();
                return;
            }

            UpdateSearching();
            UpdatePanelNoise();
        }

        void UpdateSearching()
        {
            if (!IsSearching) return;

            // 走开就中断——不能边跑边搜
            float moved = Vector3.Distance(transform.position, _searchStartPos);
            if (moved > _cfg.searchBreakDistance)
            {
                CancelSearch(true);
                return;
            }

            // 目标被别人抢走或失效
            if (_target == null || _target.Viewer != null && _target.Viewer != _rig)
            {
                CancelSearch(true);
                return;
            }

            _progress += Time.deltaTime;

            // 搜索过程持续发出噪音——翻箱子是有声音的
            _noiseTimer -= Time.deltaTime;
            if (_noiseTimer <= 0f)
            {
                _rig.EmitNoise(_cfg.lootNoiseRadius);
                _noiseTimer = 0.6f;
            }

            if (_progress >= _cfg.searchTime)
                CompleteSearch();
        }

        void UpdatePanelNoise()
        {
            if (!IsPanelOpen) return;

            // 开着界面翻找也会持续发出噪音，让 AI 能找到你
            _noiseTimer -= Time.deltaTime;
            if (_noiseTimer <= 0f)
            {
                _rig.EmitNoise(_cfg.lootNoiseRadius * 0.7f);
                _noiseTimer = 0.9f;
            }
        }
    }
}
