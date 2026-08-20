using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 玩家动作的程序化表现层。
    ///
    /// 【为什么需要这个】
    /// 肘击与搜索的判定逻辑（ElbowAbility / SearchAbility）本身是完整的，
    /// 但表现全部依赖两条尚未落地的通道：
    ///   · PlayerAnimatorBridge → 需要美术提供带 Animator 的角色模型
    ///   · RoomAudioVfx        → 需要美术提供音效与粒子
    /// 在占位胶囊体阶段这两条都是空的，结果是「按了键但什么都看不出来」。
    ///
    /// 本组件用纯代码变换 visualRoot 做出动作，不依赖任何美术资源，
    /// 且与 Animator 并存不冲突 —— 它只动 visualRoot 的 localPosition/localRotation/
    /// localScale，Animator 驱动的是模型内部骨骼，两者叠加即可。
    /// 接入真实动画后如果不想要程序化叠加，把对应开关关掉即可。
    ///
    /// 【表现内容】
    /// 肘击：预备后拉 → 爆发前冲 → 回位，命中时额外闪白 + 冲击环
    /// 搜索：身体前倾 + 上下翻找起伏 + 左右微摆，完成时向上一顿
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerActionFx : MonoBehaviour
    {
        [Header("总开关")]
        [Tooltip("关掉后本组件完全不干预 visualRoot，交给 Animator 全权负责")]
        public bool enableProceduralMotion = true;

        // ══════════════════════════════════════════════
        [Header("═══ 肘击 ═══")]

        [Tooltip("预备阶段身体后拉的距离（米）。越大蓄力感越强")]
        public float elbowWindupPull = 0.18f;

        [Tooltip("预备阶段身体侧转角度。配合后拉形成拧身蓄力")]
        public float elbowWindupTwist = 22f;

        [Tooltip("爆发瞬间前冲距离（米）")]
        public float elbowThrust = 0.42f;

        [Tooltip("爆发瞬间前倾角度")]
        public float elbowLunge = 18f;

        [Tooltip("爆发后回位所需时间。短则干脆，长则绵软")]
        [Min(0.05f)] public float elbowRecover = 0.22f;

        [Tooltip("挥空时也做动作。关掉则只有命中才有表现（不推荐，会让玩家以为没触发）")]
        public bool showOnWhiff = true;

        // ══════════════════════════════════════════════
        [Header("═══ 搜索 ═══")]

        [Tooltip("搜索时身体前倾角度，做出「弯腰翻找」的姿态")]
        public float searchLeanAngle = 26f;

        [Tooltip("翻找起伏幅度（米）")]
        public float searchBobAmount = 0.09f;

        [Tooltip("翻找频率（次/秒）。太快像抽搐，太慢没在动")]
        public float searchBobSpeed = 3.2f;

        [Tooltip("左右翻找摆动角度")]
        public float searchSwayAngle = 9f;

        [Tooltip("搜索完成时向上一顿的高度（米），给一个「找到了」的顿感")]
        public float searchPopHeight = 0.16f;

        // ══════════════════════════════════════════════
        [Header("═══ 受击 ═══")]

        [Tooltip("被肘击时身体后仰角度")]
        public float hitRecoilAngle = 30f;

        [Tooltip("受击闪白强度。0 = 不闪")]
        [Range(0f, 1f)] public float hitFlashStrength = 0.75f;

        [Tooltip("受击闪白持续时间")]
        [Min(0.02f)] public float hitFlashTime = 0.14f;

        // ══════════════════════════════════════════════
        [Header("═══ 命中特效 ═══")]

        [Tooltip("命中时生成的冲击环。纯程序化，不需要美术资源")]
        public bool spawnImpactRing = true;

        [Tooltip("冲击环颜色")]
        public Color impactColor = new Color(1f, 0.82f, 0.35f);

        [Tooltip("冲击环扩散到的最大半径（米）")]
        public float impactRingRadius = 1.15f;

        [Tooltip("冲击环存活时间")]
        [Min(0.05f)] public float impactRingLife = 0.28f;

        // ── 内部状态 ──
        PlayerActor _actor;
        RoomConfig _cfg;
        Transform _visual;

        Vector3 _restPos;
        Quaternion _restRot;
        bool _restCaptured;

        // 肘击阶段：0=无 1=预备 2=爆发回位
        int _elbowPhase;
        float _elbowT;
        float _windupDuration;

        // 搜索
        float _searchWeight;      // 0~1 姿态权重，用于平滑进出
        float _searchPhase;
        float _searchPopT = -1f;

        // 受击
        float _recoilT = -1f;
        Vector3 _recoilDir;

        // 闪白
        Renderer[] _renderers;
        Color[] _baseColors;
        bool[] _hasColor;
        float _flashT = -1f;

        void Awake()
        {
            _actor = GetComponent<PlayerActor>();
        }

        void Start()
        {
            _visual = _actor != null ? _actor.visualRoot : null;
            if (_visual == null && _actor != null)
            {
                // 建场时视觉体固定叫 Visual
                var t = transform.Find("Visual");
                if (t != null) _visual = t;
            }

            CaptureRest();
            CacheRenderers();

            var mgr = RoomManager.Instance;
            _cfg = mgr != null ? mgr.config : null;
            _windupDuration = _cfg != null ? Mathf.Max(0.02f, _cfg.elbowWindup) : 0.12f;
        }

        void CaptureRest()
        {
            if (_visual == null) return;
            _restPos = _visual.localPosition;
            _restRot = _visual.localRotation;
            _restCaptured = true;
        }

        /// <summary>
        /// 缓存渲染器与底色，用于受击闪白。
        /// 跳过隐藏的碰撞盒 —— 美术模式下碰撞盒 Renderer 是关闭的，
        /// 对它设色没有任何视觉效果（这个坑在 BugAmbience 里踩过一次）。
        /// </summary>
        void CacheRenderers()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            var keep = new System.Collections.Generic.List<Renderer>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].enabled) keep.Add(all[i]);

            _renderers = keep.ToArray();
            _baseColors = new Color[_renderers.Length];
            _hasColor = new bool[_renderers.Length];

            for (int i = 0; i < _renderers.Length; i++)
            {
                var m = _renderers[i].material;
                if (m.HasProperty("_BaseColor"))
                {
                    _baseColors[i] = m.GetColor("_BaseColor"); _hasColor[i] = true;
                }
                else if (m.HasProperty("_Color"))
                {
                    _baseColors[i] = m.GetColor("_Color"); _hasColor[i] = true;
                }
            }
        }

        void OnEnable()
        {
            RoomEvents.OnElbowHit += HandleElbowHit;
            RoomEvents.OnSearchCompleted += HandleSearchCompleted;
            RoomEvents.OnSearchInterrupted += HandleSearchInterrupted;
        }

        void OnDisable()
        {
            RoomEvents.OnElbowHit -= HandleElbowHit;
            RoomEvents.OnSearchCompleted -= HandleSearchCompleted;
            RoomEvents.OnSearchInterrupted -= HandleSearchInterrupted;
        }

        // ══════════════════════════════════════════════
        //  外部触发（由 ElbowAbility 调用）
        // ══════════════════════════════════════════════

        /// <summary>肘击开始蓄力。由 ElbowAbility.TryElbow 成功时调用。</summary>
        public void PlayElbowWindup()
        {
            if (!enableProceduralMotion) return;
            if (!showOnWhiff && _elbowPhase == 0) { /* 仍然要蓄力，命中判定在后面 */ }

            _elbowPhase = 1;
            _elbowT = 0f;
            _windupDuration = _cfg != null ? Mathf.Max(0.02f, _cfg.elbowWindup) : 0.12f;
        }

        /// <summary>肘击进入爆发。由 ElbowAbility.Resolve 调用。</summary>
        public void PlayElbowStrike()
        {
            if (!enableProceduralMotion) return;
            _elbowPhase = 2;
            _elbowT = 0f;
        }

        // ══════════════════════════════════════════════
        //  事件回调
        // ══════════════════════════════════════════════

        void HandleElbowHit(PlayerActor attacker, PlayerActor victim)
        {
            if (victim == _actor)
            {
                // 我被打：后仰 + 闪白
                var dir = attacker != null
                    ? (transform.position - attacker.transform.position)
                    : -transform.forward;
                dir.y = 0f;
                _recoilDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : -transform.forward;
                _recoilT = 0f;
                if (hitFlashStrength > 0.01f) _flashT = 0f;
            }

            if (attacker == _actor && spawnImpactRing && victim != null)
            {
                // 冲击环生在两人之间，更像「打到了」而不是「原地放了个特效」
                var mid = Vector3.Lerp(transform.position, victim.transform.position, 0.55f);
                mid.y += 0.7f;
                ImpactRing.Spawn(mid, impactColor, impactRingRadius, impactRingLife);
            }
        }

        void HandleSearchCompleted(PlayerActor who, SearchContainer c)
        {
            if (who != _actor) return;
            _searchPopT = 0f;   // 搜完向上一顿，无论有没有拿到东西
        }

        void HandleSearchInterrupted(PlayerActor who, SearchContainer c)
        {
            if (who != _actor) return;
            // 被打断时快速甩回站姿，而不是慢慢平滑 —— 打断应该有「被迫中止」的突然感
            _searchWeight = Mathf.Min(_searchWeight, 0.35f);
        }

        // ══════════════════════════════════════════════
        //  每帧驱动
        // ══════════════════════════════════════════════

        void LateUpdate()
        {
            if (!enableProceduralMotion || _visual == null || !_restCaptured) return;

            // ★让出控制权：PlayerActor 在踩空坠落与出局时会用 Rotate 累积旋转
            //   visualRoot 做翻滚（PlayerActor.cs 393 / 500 行附近），
            //   如果这里每帧覆盖 localRotation，那些翻滚动画会被完全抹掉。
            //   这两种状态本身已经有明确表现，不需要我们再叠加。
            if (_actor != null && (_actor.IsInPitfall || !_actor.IsAlive))
            {
                // 同时清掉自己的残留状态，恢复后不会突然弹一下
                _elbowPhase = 0;
                _searchWeight = 0f;
                _recoilT = -1f;
                _searchPopT = -1f;
                return;
            }

            // 从静止姿态出发逐项叠加，避免多个动作互相覆盖
            Vector3 pos = _restPos;
            Quaternion rot = _restRot;

            ApplySearch(ref pos, ref rot);
            ApplyElbow(ref pos, ref rot);
            ApplyRecoil(ref pos, ref rot);

            _visual.localPosition = pos;
            _visual.localRotation = rot;

            UpdateFlash();
        }

        void ApplySearch(ref Vector3 pos, ref Quaternion rot)
        {
            bool searching = _actor != null && _actor.IsSearching;

            // 平滑进出，避免开始/结束搜索时姿态瞬变
            float target = searching ? 1f : 0f;
            _searchWeight = Mathf.MoveTowards(_searchWeight, target, Time.deltaTime * 6f);

            if (_searchWeight > 0.001f)
            {
                _searchPhase += Time.deltaTime * searchBobSpeed * Mathf.PI * 2f;

                // 翻找起伏：用绝对值正弦，让「下探」比「回升」更有重量感
                float bob = -Mathf.Abs(Mathf.Sin(_searchPhase)) * searchBobAmount;
                float sway = Mathf.Sin(_searchPhase * 0.5f) * searchSwayAngle;

                pos += new Vector3(0f, bob, 0f) * _searchWeight;
                rot *= Quaternion.Euler(
                    searchLeanAngle * _searchWeight,
                    sway * _searchWeight,
                    0f);
            }
            else
            {
                _searchPhase = 0f;
            }

            // 完成时向上一顿
            if (_searchPopT >= 0f)
            {
                _searchPopT += Time.deltaTime;
                float d = 0.26f;
                if (_searchPopT >= d) { _searchPopT = -1f; }
                else
                {
                    float k = _searchPopT / d;
                    // 先冲上去再落回，用 sin 的前半个周期
                    pos += new Vector3(0f, Mathf.Sin(k * Mathf.PI) * searchPopHeight, 0f);
                }
            }
        }

        void ApplyElbow(ref Vector3 pos, ref Quaternion rot)
        {
            if (_elbowPhase == 0) return;

            _elbowT += Time.deltaTime;

            if (_elbowPhase == 1)
            {
                // ── 预备：后拉 + 拧身，用 ease-out 让蓄力末端稍缓 ──
                float k = Mathf.Clamp01(_elbowT / _windupDuration);
                float e = 1f - (1f - k) * (1f - k);

                // visualRoot 是玩家的子物体，所以本地 -Z 就是「身后」
                pos += new Vector3(0f, 0f, -elbowWindupPull * e);
                rot *= Quaternion.Euler(0f, -elbowWindupTwist * e, 0f);

                // 蓄力超时保护：Resolve 若因某种原因没来，别卡在预备姿态
                if (_elbowT > _windupDuration + 0.4f) _elbowPhase = 0;
            }
            else
            {
                // ── 爆发 + 回位 ──
                float k = Mathf.Clamp01(_elbowT / elbowRecover);

                // 前 25% 是爆发冲出，之后回位。这个不对称让出拳显得快、收手显得自然
                float punch;
                if (k < 0.25f) punch = k / 0.25f;
                else punch = 1f - (k - 0.25f) / 0.75f;

                // 三次曲线让爆发更冲
                float e = punch * punch * (3f - 2f * punch);

                pos += new Vector3(0f, 0f, elbowThrust * e);
                rot *= Quaternion.Euler(elbowLunge * e, 0f, 0f);

                if (k >= 1f) _elbowPhase = 0;
            }
        }

        void ApplyRecoil(ref Vector3 pos, ref Quaternion rot)
        {
            if (_recoilT < 0f) return;

            float dur = _cfg != null ? Mathf.Max(0.2f, _cfg.staggerDuration * 0.7f) : 0.5f;
            _recoilT += Time.deltaTime;
            if (_recoilT >= dur) { _recoilT = -1f; return; }

            float k = _recoilT / dur;
            // 快速后仰然后缓慢恢复
            float e = (1f - k) * (1f - k);

            // 后仰方向要转到本地空间，否则玩家转身时后仰方向会错
            var localDir = transform.InverseTransformDirection(_recoilDir);
            rot *= Quaternion.Euler(-hitRecoilAngle * e * localDir.z,
                                     0f,
                                     hitRecoilAngle * e * localDir.x);
            pos += new Vector3(0f, -0.06f * e, 0f);
        }

        void UpdateFlash()
        {
            if (_flashT < 0f || _renderers == null) return;

            _flashT += Time.deltaTime;
            bool done = _flashT >= hitFlashTime;
            float k = done ? 0f : 1f - _flashT / hitFlashTime;

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] == null || !_hasColor[i]) continue;
                var m = _renderers[i].material;
                var c = Color.Lerp(_baseColors[i], Color.white, k * hitFlashStrength);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            }

            if (done) _flashT = -1f;
        }
    }
}
