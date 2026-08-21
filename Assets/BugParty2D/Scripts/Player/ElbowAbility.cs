using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 肘击。锥形范围判定。
    /// 2D 俯视版新增：可以把站在桌子上的对手打下来（附带高度差惩罚）。
    /// </summary>
    public class ElbowAbility : MonoBehaviour
    {
        [Header("打击感（表现，不影响判定）")]
        [Tooltip("命中定格时长（真实秒）。0 = 关闭。\n" +
                 "格斗游戏常用手法：命中瞬间把时间放慢一小下，制造「打到了」的顿感。\n" +
                 "0.04~0.07 比较自然，超过 0.1 会明显卡顿。")]
        [Range(0f, 0.15f)] public float hitStopDuration = 0.055f;

        [Tooltip("定格期间的时间缩放。越小越顿")]
        [Range(0.02f, 1f)] public float hitStopScale = 0.12f;

        PlayerActor _actor;
        RoomConfig _cfg;
        PlayerActionFx _fx;

        float _cooldownUntil;
        float _windupUntil;
        bool _pending;

        public bool IsReady => Time.time >= _cooldownUntil;
        public float CooldownRemain => Mathf.Max(0f, _cooldownUntil - Time.time);

        public float CooldownFill01
        {
            get
            {
                if (_cfg == null || _cfg.elbowCooldown <= 0f) return 1f;
                return Mathf.Clamp01(1f - CooldownRemain / _cfg.elbowCooldown);
            }
        }

        public void Init(PlayerActor actor, RoomConfig cfg)
        {
            _actor = actor;
            _cfg = cfg;
            // 表现层可选。没挂也能跑，只是看不出动作
            _fx = actor != null ? actor.GetComponent<PlayerActionFx>() : null;
        }

        public bool TryElbow()
        {
            if (!IsReady || _pending) return false;
            if (_actor.IsStaggered || _actor.IsInPitfall) return false;

            var mgr = RoomManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;

            // 挥肘会中断自己的搜索
            if (_actor.Search != null) _actor.Search.Cancel(true);

            _cooldownUntil = Time.time + _cfg.elbowCooldown;
            _windupUntil = Time.time + _cfg.elbowWindup;
            _pending = true;

            // ★蓄力表现。放在这里而不是 Resolve，玩家按键瞬间就能看到反应，
            //   否则 elbowWindup 那 0.12 秒里画面上毫无变化，手感像掉帧
            if (_fx != null) _fx.PlayElbowWindup();

            // ★这里发 Windup 而不是 Swing。
            //   原先此处发的是 RaiseElbowSwing，但那与事件语义不符 ——
            //   OnElbowSwing 的注释写的是「挥肘瞬间」，实际却在按键瞬间触发，
            //   导致破风音比挥臂动作早响 0.12 秒。
            //   现在拆开：按键 → OnElbowWindup（预备特效/蓄力音）
            //             判定 → OnElbowSwing（挥动残影/破风音）
            RoomEvents.RaiseElbowWindup(_actor);
            return true;
        }

        void Update()
        {
            if (!_pending) return;
            if (Time.time < _windupUntil) return;

            _pending = false;
            Resolve();
        }

        void Resolve()
        {
            // ★爆发表现。无论是否命中都播，挥空也要看得出「挥了一下」
            if (_fx != null) _fx.PlayElbowStrike();

            // ★挥出事件在这里发 —— 与手臂真正挥动的时刻对齐。
            //   放在 TryElbow 里会让破风音早响 elbowWindup（0.12 秒），
            //   听起来像声音与动作脱节。
            RoomEvents.RaiseElbowSwing(_actor);

            var victims = FindVictimsInCone();
            for (int i = 0; i < victims.Count; i++)
            {
                var v = victims[i];
                var dir = v.transform.position - transform.position;
                dir.y = 0f;

                v.ReceiveElbow(_actor, dir, _cfg.elbowKnockback, _cfg.staggerDuration);
                RoomEvents.RaiseElbowHit(_actor, v);

                if (_cfg.elbowKnocksOutItem && !v.Inventory.IsEmpty)
                {
                    var popDir = dir.normalized + Vector3.up * 1.4f;
                    v.DropLatestItem(popDir);
                }
            }

            if (victims.Count > 0)
            {
                // 原先固定 0.1/0.1 太弱，几乎感觉不到。改为按命中人数递增，
                // 一次打到两个人应该明显更「重」
                float amount = 0.16f + 0.06f * (victims.Count - 1);
                RoomEvents.RaiseScreenShake(amount, 0.14f);

                // ★命中定格：极短的时间缩放，制造打击的「顿」感。
                //   这是格斗游戏常用手法，成本极低但打击感提升明显
                HitStop.Request(hitStopDuration, hitStopScale);
            }
        }

        /// <summary>锥形范围内的对手。会考虑高度差。</summary>
        public List<PlayerActor> FindVictimsInCone()
        {
            var result = new List<PlayerActor>();
            var mgr = RoomManager.Instance;
            if (mgr == null || _cfg == null) return result;

            Vector3 origin = _actor.elbowOrigin != null
                ? _actor.elbowOrigin.position
                : transform.position + Vector3.up * 0.8f;

            float rangeSqr = _cfg.elbowRange * _cfg.elbowRange;
            float cosLimit = Mathf.Cos(_cfg.elbowAngle * Mathf.Deg2Rad);

            for (int i = 0; i < mgr.players.Count; i++)
            {
                var p = mgr.players[i];
                if (p == null || p == _actor || !p.IsAlive) continue;
                if (p.IsInPitfall) continue;

                var to = p.transform.position - origin;

                // ★高度差限制：不能打到比自己高 1.2 米以上的人（他在桌上你在地下）
                if (Mathf.Abs(to.y) > 1.2f) continue;

                var flat = to;
                flat.y = 0f;
                if (flat.sqrMagnitude > rangeSqr) continue;

                var fwd = transform.forward;
                fwd.y = 0f;
                if (flat.sqrMagnitude > 0.0001f && fwd.sqrMagnitude > 0.0001f)
                {
                    if (Vector3.Dot(fwd.normalized, flat.normalized) < cosLimit) continue;
                }
                result.Add(p);
            }
            return result;
        }

        void OnDrawGizmosSelected()
        {
            if (_cfg == null || _actor == null) return;

            Vector3 origin = _actor.elbowOrigin != null
                ? _actor.elbowOrigin.position
                : transform.position + Vector3.up * 0.8f;

            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.4f);
            var fwd = transform.forward;
            var l = Quaternion.Euler(0f, -_cfg.elbowAngle, 0f) * fwd;
            var r = Quaternion.Euler(0f, _cfg.elbowAngle, 0f) * fwd;
            Gizmos.DrawLine(origin, origin + l * _cfg.elbowRange);
            Gizmos.DrawLine(origin, origin + r * _cfg.elbowRange);
        }
    }
}
