using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 肘击专属特效层 —— 把「蓄力 / 挥出 / 命中 / 被击中」四个时机全部做成可挂槽位。
    ///
    /// 【为什么单独做一个组件】
    /// RoomAudioVfx 是全局的场景级特效层（地板塌陷、道具拾取、警报…），它按世界坐标
    /// 生成特效、不区分是哪个玩家、也不跟随角色移动。肘击不一样：
    ///   · 特效要挂在挥击者的手上并跟着角色转身（拖尾、挥动残影）
    ///   · 要区分「挥空」与「命中」—— 挥空也该有破风视觉，否则玩家以为技能没触发
    ///   · 冲击波朝向必须对着受击者，Quaternion.identity 会让锥形特效朝向错乱
    ///   · 蓄力那 0.12 秒需要预备光效，否则按键后画面毫无变化
    /// 这些都要求「按角色实例」处理，所以做成挂在玩家身上的组件。
    ///
    /// 【与现有通道的关系】
    ///   RoomAudioVfx.vfxElbowImpact  仍然有效 —— 那是「不跟随、纯世界坐标」的一次性特效
    ///   ImpactRing                   程序化冲击环，不依赖资源，可与本组件的特效叠加
    ///   本组件                        资源驱动，负责跟随角色与朝向正确的那部分
    /// 三者互不冲突，可以只用其中一两个。全部留空则退回纯程序化表现。
    ///
    /// 【用法】
    /// 建场时自动挂载。选中任意玩家，在 Inspector 里把粒子 Prefab 拖进对应槽位即可。
    /// 四个玩家各有一份，所以可以给不同角色配不同特效（比如红方拳风偏红）。
    /// </summary>
    [DisallowMultipleComponent]
    public class ElbowVfxHook : MonoBehaviour
    {
        // ══════════════════════════════════════════════
        //  槽位
        // ══════════════════════════════════════════════

        [Header("═══ ① 蓄力 ═══")]
        [Tooltip("按下肘击键的瞬间生成，持续 elbowWindup（默认 0.12 秒）。\n" +
                 "挂在手部挂点上并跟随角色 —— 适合放拳套发光、能量汇聚这类预备特效。\n" +
                 "★挥出时会自动销毁，不需要自带生命周期。")]
        public GameObject windupVfx;

        [Tooltip("蓄力特效的挂点。留空用 HandAnchor")]
        public Transform windupAnchor;

        [Header("═══ ② 挥出（不论是否命中）═══")]
        [Tooltip("挥出瞬间生成。适合放挥动残影、破风弧线。\n" +
                 "★这是「挥空也能看到」的关键 —— 原先挥空只有音效，玩家会以为技能没触发。")]
        public GameObject swingVfx;

        [Tooltip("挥出特效是否跟随角色。\n" +
                 "拖尾类勾选（跟着转身），一次性爆发类取消（留在原地扩散）")]
        public bool swingFollowsActor = true;

        [Tooltip("挥出特效的挂点。留空用 ElbowOrigin")]
        public Transform swingAnchor;

        [Header("═══ ③ 命中 ═══")]
        [Tooltip("命中时在受击者身上生成。适合放撞击火花、冲击波。\n" +
                 "★朝向会自动对齐「攻击者 → 受击者」方向，锥形/平面特效不会朝错。")]
        public GameObject hitVfx;

        [Tooltip("命中特效的生成高度（相对受击者脚底，米）")]
        [Range(0f, 2.5f)] public float hitHeight = 0.9f;

        [Tooltip("命中特效朝向：勾选=面向攻击方向（锥形冲击波用），\n" +
                 "取消=保持 Prefab 原始朝向（球形爆炸、向上喷的粒子用）")]
        public bool hitFaceAttackDir = true;

        [Header("═══ ④ 被击中（受害方视角）═══")]
        [Tooltip("自己被打到时生成在自己身上。适合放星星乱转、眩晕圈。\n" +
                 "与 hitVfx 的区别：hitVfx 由攻击者触发一次，这个由受害者自己触发 ——\n" +
                 "所以可以给不同角色配不同的受击反应。")]
        public GameObject getHitVfx;

        [Header("═══ 通用 ═══")]
        [Tooltip("特效自动销毁时间（秒）。0 = 不自动销毁，由特效自己的 Stop Action 处理。\n" +
                 "★用 ParticleSystem 且勾了 Stop Action = Destroy 时，这里填 0")]
        [Min(0f)] public float lifetime = 2f;

        [Tooltip("生成时给特效附加的额外缩放。批量调整特效大小用，不必改 Prefab")]
        [Range(0.1f, 5f)] public float scaleMul = 1f;

        [Tooltip("是否用队伍色给特效染色。\n" +
                 "会设置 ParticleSystem 的 startColor 与所有材质的 _Color/_BaseColor/_TintColor")]
        public bool tintByTeamColor = false;

        // ══════════════════════════════════════════════

        PlayerActor _actor;
        GameObject _windupInstance;   // 蓄力特效需要在挥出时销毁，必须持有引用

        void Awake()
        {
            _actor = GetComponent<PlayerActor>();

            // 挂点留空时用建场生成的标准挂点
            if (windupAnchor == null && _actor != null) windupAnchor = _actor.handAnchor;
            if (swingAnchor == null && _actor != null) swingAnchor = _actor.elbowOrigin;
        }

        void OnEnable()
        {
            RoomEvents.OnElbowWindup += HandleWindup;
            RoomEvents.OnElbowSwing += HandleSwing;
            RoomEvents.OnElbowHit += HandleHit;
        }

        void OnDisable()
        {
            RoomEvents.OnElbowWindup -= HandleWindup;
            RoomEvents.OnElbowSwing -= HandleSwing;
            RoomEvents.OnElbowHit -= HandleHit;

            // ★组件被禁用（角色出局、场景卸载）时清掉蓄力特效，
            //   否则它会因为没人销毁而一直挂在场景里
            KillWindup();
        }

        // ══════════════════════════════════════════════
        //  事件处理
        // ══════════════════════════════════════════════

        void HandleWindup(PlayerActor who)
        {
            if (who != _actor || windupVfx == null) return;

            // 上一次的还没清掉就先清 —— 冷却期内不该重复触发，但防一手
            KillWindup();

            var anchor = windupAnchor != null ? windupAnchor : transform;
            _windupInstance = Spawn(windupVfx, anchor.position, anchor.rotation, anchor);
            // ★不设 lifetime —— 蓄力特效的生命周期由「挥出」决定，不是定时
        }

        void HandleSwing(PlayerActor who)
        {
            if (who != _actor) return;

            // 蓄力结束，无论是否有 swingVfx 都要清掉预备特效
            KillWindup();

            if (swingVfx == null) return;

            var anchor = swingAnchor != null ? swingAnchor : transform;
            // 朝向取角色正面 —— 挥击方向就是面朝方向
            var rot = Quaternion.LookRotation(transform.forward, Vector3.up);
            var go = Spawn(swingVfx, anchor.position, rot,
                           swingFollowsActor ? anchor : null);
            AutoDestroy(go);
        }

        void HandleHit(PlayerActor attacker, PlayerActor victim)
        {
            // 同一个事件里分别处理「我打人」与「我被打」
            if (attacker == _actor && victim != null && hitVfx != null)
            {
                var pos = victim.transform.position + Vector3.up * hitHeight;

                Quaternion rot;
                if (hitFaceAttackDir)
                {
                    var dir = victim.transform.position - transform.position;
                    dir.y = 0f;
                    // 两人完全重合时 LookRotation 会报错，退回角色朝向
                    rot = dir.sqrMagnitude > 1e-4f
                        ? Quaternion.LookRotation(dir.normalized, Vector3.up)
                        : Quaternion.LookRotation(transform.forward, Vector3.up);
                }
                else
                {
                    rot = hitVfx.transform.rotation;
                }

                // ★不挂父级 —— 受击者会被击退，特效跟着走反而削弱打击感
                AutoDestroy(Spawn(hitVfx, pos, rot, null));
            }

            if (victim == _actor && getHitVfx != null)
            {
                var pos = transform.position + Vector3.up * hitHeight;
                // 受击反应挂在自己身上，跟着被击退一起动才对
                AutoDestroy(Spawn(getHitVfx, pos, transform.rotation, transform));
            }
        }

        // ══════════════════════════════════════════════
        //  生成与清理
        // ══════════════════════════════════════════════

        GameObject Spawn(GameObject prefab, Vector3 pos, Quaternion rot, Transform parent)
        {
            if (prefab == null) return null;

            var go = Instantiate(prefab, pos, rot);
            if (parent != null)
            {
                // worldPositionStays = true：保持刚才算好的世界位置与朝向
                go.transform.SetParent(parent, true);
            }

            if (!Mathf.Approximately(scaleMul, 1f))
                go.transform.localScale *= scaleMul;

            if (tintByTeamColor && _actor != null)
                Tint(go, _actor.playerColor.ToColor());

            return go;
        }

        void AutoDestroy(GameObject go)
        {
            if (go == null) return;
            // lifetime = 0 表示交给特效自己（ParticleSystem 的 Stop Action = Destroy）
            if (lifetime > 0f) Destroy(go, lifetime);
        }

        void KillWindup()
        {
            if (_windupInstance == null) return;
            Destroy(_windupInstance);
            _windupInstance = null;
        }

        /// <summary>
        /// 给特效染上队伍色。粒子与普通材质分开处理 ——
        /// ParticleSystem 的颜色在 main.startColor 上，改材质对它无效。
        /// </summary>
        static void Tint(GameObject go, Color c)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = c;
            }

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                // 用 material（实例）而非 sharedMaterial —— 否则会污染 Prefab 的材质，
                // 四个玩家会互相把对方的颜色改掉
                var m = r.material;
                if (m == null) continue;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                // 粒子常用的 shader 用 _TintColor
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c);
                // 自发光特效走 _EmissionColor 才看得出染色
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c);
            }
        }
    }
}
