using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 搜索能力。负责读条、独占容器、被打断时回滚。
    /// </summary>
    public class SearchAbility : MonoBehaviour
    {
        PlayerActor _actor;
        SearchRoomConfig _cfg;

        SearchContainer _target;
        float _progress;

        public bool IsSearching => _target != null;
        public float Progress01 => _cfg != null && _cfg.searchTime > 0f
            ? Mathf.Clamp01(_progress / _cfg.searchTime) : 0f;
        public SearchContainer CurrentTarget => _target;

        public void Init(PlayerActor actor, SearchRoomConfig cfg)
        {
            _actor = actor;
            _cfg = cfg;
        }

        /// <summary>
        /// 找出面前可以搜索的容器。给 Brain 和 HUD 判断"现在能不能按搜索"。
        /// </summary>
        public SearchContainer FindTargetInRange()
        {
            if (_cfg == null) return null;
            if (_actor.Inventory.IsFull) return null;

            var mgr = SearchRoomManager.Instance;
            if (mgr == null) return null;

            SearchContainer best = null;
            float bestSqr = _cfg.searchRange * _cfg.searchRange;

            for (int i = 0; i < mgr.containers.Count; i++)
            {
                var c = mgr.containers[i];
                if (c == null || !c.IsAvailableFor(_actor)) continue;

                float d = (c.InteractPoint - transform.position).sqrMagnitude;
                if (d <= bestSqr) { bestSqr = d; best = c; }
            }
            return best;
        }

        /// <summary>尝试开始搜索。成功返回 true。</summary>
        public bool TryBegin(SearchContainer container = null)
        {
            if (IsSearching) return false;
            if (_actor.IsStaggered) return false;
            if (_actor.Inventory.IsFull) return false;

            var mgr = SearchRoomManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;

            var c = container != null ? container : FindTargetInRange();
            if (c == null) return false;
            if (!c.TryClaim(_actor)) return false;

            _target = c;
            _progress = 0f;
            SearchRoomEvents.RaiseSearchStarted(_actor, c);
            return true;
        }

        /// <summary>取消搜索。interrupted=true 表示被打断（触发容器冷却）。</summary>
        public void Cancel(bool interrupted)
        {
            if (_target == null) return;

            var c = _target;
            _target = null;
            _progress = 0f;

            c.Release(_actor, interrupted);
            if (interrupted) SearchRoomEvents.RaiseSearchInterrupted(_actor, c);
        }

        void Update()
        {
            if (!IsSearching) return;

            var mgr = SearchRoomManager.Instance;
            if (mgr == null || !mgr.CanAct) { Cancel(false); return; }

            // 走远了自动放弃
            float distSqr = (_target.InteractPoint - transform.position).sqrMagnitude;
            float maxSqr = (_cfg.searchRange * 1.35f) * (_cfg.searchRange * 1.35f);
            if (distSqr > maxSqr) { Cancel(true); return; }

            _progress += Time.deltaTime;
            if (_progress >= _cfg.searchTime)
                Complete();
        }

        void Complete()
        {
            var c = _target;
            _target = null;
            _progress = 0f;

            var item = c.ExtractItem();
            c.Release(_actor, false);

            if (item != null && _actor.Inventory.TryAdd(item))
                SearchRoomEvents.RaiseItemCollected(_actor, item);
        }
    }
}
