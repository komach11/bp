using System.Collections.Generic;
using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 肘击能力。这是本环节最重要的机制：
    /// 打断对手的搜索进程，并把他刚拿到的道具直接打飞到地上。
    /// </summary>
    public class ElbowAbility : MonoBehaviour
    {
        PlayerActor _actor;
        SearchRoomConfig _cfg;

        float _cooldownUntil;
        float _windupUntil;
        bool _pendingHit;

        public bool IsReady => Time.time >= _cooldownUntil;
        public float CooldownRemain => Mathf.Max(0f, _cooldownUntil - Time.time);

        /// <summary>0~1 的冷却填充度，1 表示已就绪。HUD 用。</summary>
        public float CooldownFill01
        {
            get
            {
                if (_cfg == null || _cfg.elbowCooldown <= 0f) return 1f;
                return Mathf.Clamp01(1f - CooldownRemain / _cfg.elbowCooldown);
            }
        }

        public void Init(PlayerActor actor, SearchRoomConfig cfg)
        {
            _actor = actor;
            _cfg = cfg;
        }

        /// <summary>尝试挥肘。成功进入前摇则返回 true。</summary>
        public bool TryElbow()
        {
            if (!IsReady || _pendingHit) return false;
            if (_actor.IsStaggered) return false;

            var mgr = SearchRoomManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;

            // 挥肘会中断自己的搜索——这是设计上的取舍，防止边搜边打
            _actor.AbortSearch();

            _cooldownUntil = Time.time + _cfg.elbowCooldown;
            _windupUntil = Time.time + _cfg.elbowWindup;
            _pendingHit = true;
            return true;
        }

        void Update()
        {
            if (!_pendingHit) return;
            if (Time.time < _windupUntil) return;

            _pendingHit = false;
            ResolveHit();
        }

        /// <summary>前摇结束，做锥形范围判定。</summary>
        void ResolveHit()
        {
            var victims = FindVictimsInCone();
            for (int i = 0; i < victims.Count; i++)
            {
                var v = victims[i];
                var dir = v.transform.position - transform.position;
                dir.y = 0f;

                v.ReceiveElbow(_actor, dir, _cfg.elbowKnockback, _cfg.staggerDuration);
                SearchRoomEvents.RaiseElbowHit(_actor, v);

                // 打落对方最新拿到的一件道具
                if (_cfg.elbowKnocksOutItem && !v.Inventory.IsEmpty)
                {
                    var popDir = dir.normalized + Vector3.up * 1.4f;
                    v.DropLatestItem(popDir);
                }
            }
        }

        /// <summary>取得锥形范围内的所有对手。</summary>
        public List<PlayerActor> FindVictimsInCone()
        {
            var result = new List<PlayerActor>();
            var mgr = SearchRoomManager.Instance;
            if (mgr == null || _cfg == null) return result;

            Vector3 origin = _actor.elbowOrigin != null
                ? _actor.elbowOrigin.position
                : transform.position + Vector3.up * 0.9f;

            float rangeSqr = _cfg.elbowRange * _cfg.elbowRange;
            float cosLimit = Mathf.Cos(_cfg.elbowAngle * Mathf.Deg2Rad);

            for (int i = 0; i < mgr.players.Count; i++)
            {
                var p = mgr.players[i];
                if (p == null || p == _actor || !p.IsAlive) continue;

                var to = p.transform.position - origin;
                to.y = 0f;
                if (to.sqrMagnitude > rangeSqr) continue;

                var fwd = transform.forward;
                fwd.y = 0f;
                if (to.sqrMagnitude > 0.0001f && fwd.sqrMagnitude > 0.0001f)
                {
                    float dot = Vector3.Dot(fwd.normalized, to.normalized);
                    if (dot < cosLimit) continue;
                }
                result.Add(p);
            }
            return result;
        }

        void OnDrawGizmosSelected()
        {
            if (_cfg == null) return;
            Vector3 origin = _actor != null && _actor.elbowOrigin != null
                ? _actor.elbowOrigin.position
                : transform.position + Vector3.up * 0.9f;

            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(origin, _cfg.elbowRange);

            var fwd = transform.forward;
            var l = Quaternion.Euler(0f, -_cfg.elbowAngle, 0f) * fwd;
            var r = Quaternion.Euler(0f, _cfg.elbowAngle, 0f) * fwd;
            Gizmos.DrawLine(origin, origin + l * _cfg.elbowRange);
            Gizmos.DrawLine(origin, origin + r * _cfg.elbowRange);
        }
    }
}
