using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 近战攻击（肘击 / 撬棍）。第一人称下用球形射线判定，比锥形角度更符合 FPS 直觉。
    ///
    /// ★背刺机制：命中背对自己的目标时，硬直更长、打落更多且优先打掉最值钱的。
    /// 这直接鼓励「趁对手翻箱子时从背后阴他」——本环节最有戏的时刻。
    /// </summary>
    public class MeleeAction : MonoBehaviour
    {
        PlayerRig _rig;
        RaidConfig _cfg;

        float _cooldownUntil;
        float _windupUntil;
        bool _pending;

        /// <summary>挥击动画进度 0~1，供手部视觉模型使用。</summary>
        public float SwingProgress01 { get; private set; }

        public bool IsReady => Time.time >= _cooldownUntil;
        public float CooldownRemain => Mathf.Max(0f, _cooldownUntil - Time.time);

        public float CooldownFill01
        {
            get
            {
                if (_cfg == null || _cfg.meleeCooldown <= 0f) return 1f;
                return Mathf.Clamp01(1f - CooldownRemain / _cfg.meleeCooldown);
            }
        }

        public void Init(PlayerRig rig, RaidConfig cfg)
        {
            _rig = rig;
            _cfg = cfg;
        }

        /// <summary>尝试挥击。</summary>
        public bool TrySwing()
        {
            if (!IsReady || _pending) return false;
            if (_rig.IsStaggered) return false;

            var mgr = RaidManager.Instance;
            if (mgr == null || !mgr.CanAct) return false;

            // 挥击会强制关掉自己的搜刮界面——不能一边翻包一边打人
            if (_rig.Loot != null) _rig.Loot.ForceAbort();

            _cooldownUntil = Time.time + _cfg.meleeCooldown;
            _windupUntil = Time.time + _cfg.meleeWindup;
            _pending = true;

            // 挥击本身有声音
            _rig.EmitNoise(_cfg.walkNoiseRadius * 0.8f);
            return true;
        }

        void Update()
        {
            // 挥击动画进度
            if (_cfg != null && _cfg.meleeCooldown > 0f)
            {
                float since = Time.time - (_cooldownUntil - _cfg.meleeCooldown);
                SwingProgress01 = Mathf.Clamp01(since / Mathf.Max(0.01f, _cfg.meleeCooldown * 0.45f));
            }

            if (!_pending) return;
            if (Time.time < _windupUntil) return;

            _pending = false;
            Resolve();
        }

        void Resolve()
        {
            var victim = FindVictim();

            if (victim == null)
            {
                RaidEvents.RaiseMeleeMiss(_rig);
                return;
            }

            var dir = victim.transform.position - transform.position;
            dir.y = 0f;

            bool isBackstab = IsBackstab(victim);
            victim.ReceiveMelee(_rig, dir, isBackstab);
            RaidEvents.RaiseMeleeHit(_rig, victim, isBackstab);
        }

        /// <summary>
        /// 球形射线找目标。比单线射线宽容，符合近战手感。
        /// </summary>
        public PlayerRig FindVictim()
        {
            if (_cfg == null) return null;

            Transform origin = _rig.eyeAnchor != null ? _rig.eyeAnchor : transform;
            Vector3 dir = origin.forward;

            var look = GetComponent<FirstPersonLook>();
            if (look != null && look.cameraTransform != null)
                dir = look.cameraTransform.forward;

            // 水平化：不允许对着地板挥中人
            dir.y = Mathf.Clamp(dir.y, -0.4f, 0.4f);
            dir.Normalize();

            var hits = Physics.SphereCastAll(
                origin.position, _cfg.meleeRadius, dir, _cfg.meleeRange);

            if (hits == null || hits.Length == 0) return null;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var other = hits[i].collider.GetComponentInParent<PlayerRig>();
                if (other == null || other == _rig || !other.IsAlive) continue;
                return other;
            }
            return null;
        }

        /// <summary>
        /// 判断是否为背刺：攻击者位于目标身后的半球内。
        /// </summary>
        public bool IsBackstab(PlayerRig victim)
        {
            if (victim == null) return false;

            var victimFwd = victim.transform.forward;
            victimFwd.y = 0f;

            var toAttacker = transform.position - victim.transform.position;
            toAttacker.y = 0f;

            if (victimFwd.sqrMagnitude < 0.0001f || toAttacker.sqrMagnitude < 0.0001f)
                return false;

            // 攻击者在目标背后 → 两向量点积为负
            float dot = Vector3.Dot(victimFwd.normalized, toAttacker.normalized);
            return dot < -0.15f;
        }

        void OnDrawGizmosSelected()
        {
            if (_cfg == null || _rig == null) return;

            Transform origin = _rig.eyeAnchor != null ? _rig.eyeAnchor : transform;
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.5f);
            Gizmos.DrawWireSphere(origin.position + origin.forward * _cfg.meleeRange, _cfg.meleeRadius);
            Gizmos.DrawLine(origin.position, origin.position + origin.forward * _cfg.meleeRange);
        }
    }
}
