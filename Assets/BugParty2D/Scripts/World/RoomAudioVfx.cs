using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 音效与特效总线。挂在场景里任意一个空物体上即可，只需一个实例。
    ///
    /// 【设计意图】
    /// 全部通过订阅 RoomEvents 工作，玩法脚本零改动。
    /// 所有槽位都可留空——留空就是不播，不会报错。
    /// 想加新音效不用改玩法代码，只要 RoomEvents 里有对应事件。
    ///
    /// 【用法】
    /// 1. 场景里新建空物体，挂上本组件
    /// 2. 把音频与粒子 Prefab 拖进对应槽位
    /// 3. 完成，不需要任何其他接线
    /// </summary>
    [DisallowMultipleComponent]
    public class RoomAudioVfx : MonoBehaviour
    {
        [Header("═══ 音效 ═══")]
        [Tooltip("留空会自动创建一个 AudioSource")]
        public AudioSource sfxSource;

        // ── 肘击 ──────────────────────────────────────
        [Header("── 肘击 ──")]
        [Tooltip("肘击起手的蓄力音。比 sfxElbowSwing 早 elbowWindup（默认 0.12 秒）")]
        public AudioClip sfxElbowWindup;
        [Tooltip("★挥肘的破风音。不论是否命中都播，命中时再叠 sfxElbowHit。\n" +
                 "没有它挥空毫无声音反馈，玩家会以为技能没触发")]
        public AudioClip sfxElbowSwing;
        [Tooltip("命中的撞击音。与 sfxElbowSwing 叠加播放")]
        public AudioClip sfxElbowHit;
        [Tooltip("把对方手里的道具打掉时播")]
        public AudioClip sfxItemKnockedOut;

        // ── 搜索 ──────────────────────────────────────
        [Header("── 搜索 ──")]
        [Tooltip("开始翻找容器")]
        public AudioClip sfxSearchStart;
        [Tooltip("★搜索期间的循环翻找音。搜索开始播、结束/打断时停。\n" +
                 "留空则不播。它是「正在搜」最直接的听觉反馈")]
        public AudioClip sfxSearchLoop;
        [Tooltip("被打断（挨了肘击 / 松手 / 踩空）")]
        public AudioClip sfxSearchInterrupt;
        [Tooltip("★搜索完成（读条走满）。即使容器已空也会播 —— 玩家需要知道\n" +
                 "「搜完了」，否则不清楚该不该继续按着不动")]
        public AudioClip sfxSearchComplete;
        [Tooltip("拿到道具")]
        public AudioClip sfxItemCollected;
        [Tooltip("搜到稀有道具时优先播这个")]
        public AudioClip sfxItemRare;

        // ── 脚步与跳跃 ─────────────────────────────────
        [Header("── 脚步与跳跃 ──")]
        [Tooltip("★脚步声。可放多个，每步随机取一个避免机械重复。\n" +
                 "由 FootstepEmitter 按移动距离触发，不是按时间 —— 走快走慢的\n" +
                 "步频会自然不同")]
        public AudioClip[] sfxFootsteps;
        [Tooltip("在会议桌/矮柜等平台上走的脚步。留空则统一用 sfxFootsteps")]
        public AudioClip[] sfxFootstepsPlatform;
        [Range(0f, 1f)] public float footstepVolume = 0.45f;
        [Tooltip("脚步音调随机范围，避免连续踩踏像复读机")]
        [Range(0f, 0.4f)] public float footstepPitchJitter = 0.12f;

        [Space(4)]
        public AudioClip sfxJump;
        public AudioClip sfxLand;
        [Tooltip("从高处落下才播，阈值见 heavyLandHeight")]
        public AudioClip sfxLandHeavy;

        [Space(4)]
        [Tooltip("地板开裂预警。这是玩家躲开塌陷的关键听觉线索，建议一定要有")]
        public AudioClip sfxTileCracking;
        public AudioClip sfxTileCollapsed;
        public AudioClip sfxPitfall;
        public AudioClip sfxFinalCollapse;

        // ══════════════════════════════════════════════
        [Header("═══ 背景音乐（按阶段自动切换）═══")]

        [Tooltip("留空会自动创建。BGM 用独立 AudioSource，这样音效音量与\n" +
                 "音乐音量能分开调，也便于将来接入设置菜单")]
        public AudioSource musicSource;

        [Tooltip("开场介绍阶段（Intro）。氛围铺垫，可留空")]
        public AudioClip bgmIntro;

        [Tooltip("★搜索阶段主曲。这是玩家听最久的一首，最重要")]
        public AudioClip bgmSearching;

        [Tooltip("终局塌陷阶段。应该更紧张、更快")]
        public AudioClip bgmCollapse;

        [Tooltip("结算与穿越阶段。可留空，留空则静音")]
        public AudioClip bgmSettlement;

        [Range(0f, 1f)] public float musicVolume = 0.55f;

        [Tooltip("切曲的交叉淡化时长。0 = 硬切。\n" +
                 "搜索→塌陷这一下建议短一点（0.3~0.6），突然变紧张更有冲击力")]
        [Min(0f)] public float musicFadeTime = 0.8f;

        [Tooltip("搜索阶段末尾随倒计时提速。1 = 不变速。\n" +
                 "配合警报循环音一起加剧，比单纯调音量更能制造紧迫感")]
        [Range(1f, 1.5f)] public float musicEndPitchUp = 1.08f;

        [Header("═══ 循环音 ═══")]
        [Tooltip("警报循环音。会随倒计时推进自动升高音调与音量")]
        public AudioSource alarmLoop;
        [Tooltip("警报音调范围：搜索刚开始 → 即将结束")]
        public Vector2 alarmPitchRange = new Vector2(0.85f, 1.35f);
        public Vector2 alarmVolumeRange = new Vector2(0.25f, 0.85f);

        [Header("═══ 粒子特效 Prefab ═══")]
        [Tooltip("肘击命中的撞击星星。\n" +
                 "★这是「场景级、不跟随角色」的特效。若需要跟随手部或朝向正确的\n" +
                 "冲击波，用玩家身上的 ElbowVfxHook —— 两者可以同时用。")]
        public GameObject vfxElbowImpact;

        [Tooltip("肘击挥出（不论是否命中）。适合放留在原地的破风弧线。\n" +
                 "★挥空也会播 —— 原先挥空只有音效，视觉上毫无反馈。")]
        public GameObject vfxElbowSwing;

        [Tooltip("肘击蓄力起手。elbowWindup 那 0.12 秒的预备提示。")]
        public GameObject vfxElbowWindup;

        public GameObject vfxItemPickup;
        [Tooltip("地板开裂的碎屑与红光")]
        public GameObject vfxTileCracking;
        [Tooltip("地板塌陷的坠落尘土")]
        public GameObject vfxTileCollapse;
        public GameObject vfxLandDust;

        [Header("═══ 参数 ═══")]
        [Tooltip("落地高度超过这个值才算重落地")]
        public float heavyLandHeight = 1.2f;

        [Tooltip("特效自动销毁时间")]
        public float vfxLifetime = 2.5f;

        [Range(0f, 1f)] public float sfxVolume = 0.9f;

        float _searchTotal = 1f;
        float _searchRemain = 1f;

        /// <summary>
        /// 场景唯一实例。FootstepEmitter 等每帧调用方需要拿到它，
        /// 用 FindObjectOfType 每帧找开销太大。
        /// </summary>
        public static RoomAudioVfx Instance { get; private set; }

        void Awake()
        {
            Instance = this;

            if (sfxSource == null)
            {
                sfxSource = gameObject.AddComponent<AudioSource>();
                sfxSource.playOnAwake = false;
                sfxSource.spatialBlend = 0f;   // 2D 俯视用 2D 音效更清晰
            }

            // BGM 用独立 AudioSource：与音效分开才能各自调音量，
            // 也避免 PlayOneShot 的音调抖动影响到音乐
            if (musicSource == null)
            {
                var mgo = new GameObject("MusicSource");
                mgo.transform.SetParent(transform, false);
                musicSource = mgo.AddComponent<AudioSource>();
                musicSource.playOnAwake = false;
                musicSource.loop = true;
                musicSource.spatialBlend = 0f;
                musicSource.volume = musicVolume;
            }
        }

        void OnEnable()
        {
            RoomEvents.OnSearchStarted += OnSearchStarted;
            RoomEvents.OnSearchInterrupted += OnSearchInterrupted;
            RoomEvents.OnSearchCompleted += OnSearchCompleted;
            RoomEvents.OnItemCollected += OnItemCollected;
            RoomEvents.OnItemKnockedOut += OnItemKnockedOut;
            RoomEvents.OnElbowHit += OnElbowHit;
            RoomEvents.OnElbowSwing += OnElbowSwing;
            RoomEvents.OnElbowWindup += OnElbowWindup;
            RoomEvents.OnJump += OnJump;
            RoomEvents.OnLand += OnLand;
            RoomEvents.OnTileCracking += OnTileCracking;
            RoomEvents.OnTileCollapsed += OnTileCollapsed;
            RoomEvents.OnPlayerPitfall += OnPitfall;
            RoomEvents.OnFinalCollapseStarted += OnFinalCollapse;
            RoomEvents.OnTimerTick += OnTimerTick;
            RoomEvents.OnPhaseChanged += OnPhaseChanged;
        }

        void OnDestroy()
        {
            // 场景切换后 Instance 若不清空会是野指针，
            // FootstepEmitter 拿到已销毁对象会抛 MissingReferenceException
            if (Instance == this) Instance = null;
        }

        void OnDisable()
        {
            RoomEvents.OnSearchStarted -= OnSearchStarted;
            RoomEvents.OnSearchInterrupted -= OnSearchInterrupted;
            RoomEvents.OnSearchCompleted -= OnSearchCompleted;
            RoomEvents.OnItemCollected -= OnItemCollected;
            RoomEvents.OnItemKnockedOut -= OnItemKnockedOut;
            RoomEvents.OnElbowHit -= OnElbowHit;
            RoomEvents.OnElbowSwing -= OnElbowSwing;
            RoomEvents.OnElbowWindup -= OnElbowWindup;
            RoomEvents.OnJump -= OnJump;
            RoomEvents.OnLand -= OnLand;
            RoomEvents.OnTileCracking -= OnTileCracking;
            RoomEvents.OnTileCollapsed -= OnTileCollapsed;
            RoomEvents.OnPlayerPitfall -= OnPitfall;
            RoomEvents.OnFinalCollapseStarted -= OnFinalCollapse;
            RoomEvents.OnTimerTick -= OnTimerTick;
            RoomEvents.OnPhaseChanged -= OnPhaseChanged;
        }

        // ══════════════════════════════════════════════
        //  播放helper
        // ══════════════════════════════════════════════

        void Play(AudioClip clip, Vector3 at, float pitchJitter = 0.06f)
        {
            if (clip == null || sfxSource == null) return;

            // 轻微随机音调，避免同一音效连播时的机械感
            sfxSource.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            sfxSource.PlayOneShot(clip, sfxVolume);
        }

        void Spawn(GameObject prefab, Vector3 at)
        {
            Spawn(prefab, at, Quaternion.identity);
        }

        /// <summary>
        /// 带朝向的生成。
        /// ★锥形冲击波、平面刀光这类有方向性的特效必须传朝向 ——
        /// 用 Quaternion.identity 会让它们永远朝着世界 +Z，与实际攻击方向无关。
        /// </summary>
        void Spawn(GameObject prefab, Vector3 at, Quaternion rot)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, at, rot);
            if (vfxLifetime > 0f) Destroy(go, vfxLifetime);
        }

        // ══════════════════════════════════════════════
        //  事件回调
        // ══════════════════════════════════════════════

        void OnSearchStarted(PlayerActor p, SearchContainer c)
        {
            Play(sfxSearchStart, p != null ? p.transform.position : Vector3.zero);
            StartSearchLoop(p);
        }

        void OnSearchInterrupted(PlayerActor p, SearchContainer c)
        {
            Play(sfxSearchInterrupt, p != null ? p.transform.position : Vector3.zero);
            StopSearchLoop(p);
        }

        void OnSearchCompleted(PlayerActor p, SearchContainer c)
        {
            Play(sfxSearchComplete, p != null ? p.transform.position : Vector3.zero);
            StopSearchLoop(p);
        }

        // ── 搜索循环音 ─────────────────────────────────
        // ★为什么要按玩家分别管理：4 个人可能同时在搜不同的柜子，
        //   共用一个 AudioSource 的话后开始的会掐断先开始的。
        //   每人一个 AudioSource 挂在自己身上，还能顺带获得 3D 空间感。
        readonly Dictionary<PlayerActor, AudioSource> _searchLoops =
            new Dictionary<PlayerActor, AudioSource>();

        void StartSearchLoop(PlayerActor p)
        {
            if (sfxSearchLoop == null || p == null) return;

            if (!_searchLoops.TryGetValue(p, out var src) || src == null)
            {
                src = p.gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = true;
                src.spatialBlend = 0f;   // 2D 俯视，用 2D 音更清晰
                _searchLoops[p] = src;
            }
            src.clip = sfxSearchLoop;
            src.volume = sfxVolume * 0.7f;
            if (!src.isPlaying) src.Play();
        }

        void StopSearchLoop(PlayerActor p)
        {
            if (p == null) return;
            if (_searchLoops.TryGetValue(p, out var src) && src != null && src.isPlaying)
                src.Stop();
        }

        // ── 脚步声 ─────────────────────────────────────

        /// <summary>
        /// 播一步脚步声。由 FootstepEmitter 按累计移动距离调用 ——
        /// 不是按固定时间间隔，所以走快走慢的步频会自然不同。
        /// </summary>
        /// <param name="onPlatform">是否站在平台（会议桌/矮柜）上，用另一组音效</param>
        public void PlayFootstep(Vector3 at, bool onPlatform = false)
        {
            var bank = onPlatform && sfxFootstepsPlatform != null && sfxFootstepsPlatform.Length > 0
                ? sfxFootstepsPlatform
                : sfxFootsteps;

            if (bank == null || bank.Length == 0 || sfxSource == null) return;

            var clip = bank[Random.Range(0, bank.Length)];
            if (clip == null) return;

            sfxSource.pitch = 1f + Random.Range(-footstepPitchJitter, footstepPitchJitter);
            sfxSource.PlayOneShot(clip, sfxVolume * footstepVolume);
        }

        void OnElbowSwing(PlayerActor p)
        {
            if (p == null) return;
            // 破风音音调抖动大一些，连续挥肘才不会像复读机
            Play(sfxElbowSwing, p.transform.position, 0.12f);

            // 挥出特效朝向角色正面 —— 破风弧线得对着挥击方向
            Spawn(vfxElbowSwing,
                  p.transform.position + Vector3.up * 0.9f,
                  Quaternion.LookRotation(p.transform.forward, Vector3.up));
        }

        void OnElbowWindup(PlayerActor p)
        {
            if (p == null) return;
            Play(sfxElbowWindup, p.transform.position, 0.08f);
            Spawn(vfxElbowWindup,
                  p.transform.position + Vector3.up * 0.9f,
                  Quaternion.LookRotation(p.transform.forward, Vector3.up));
        }

        void OnItemCollected(PlayerActor p, ItemDefinition item)
        {
            var pos = p != null ? p.transform.position : Vector3.zero;

            // 稀有道具用专属音效，这是给玩家的即时正反馈
            var clip = (item != null && item.isRare && sfxItemRare != null)
                ? sfxItemRare : sfxItemCollected;

            Play(clip, pos);
            Spawn(vfxItemPickup, pos + Vector3.up);
        }

        void OnItemKnockedOut(PlayerActor p, ItemDefinition item)
        {
            Play(sfxItemKnockedOut, p != null ? p.transform.position : Vector3.zero);
        }

        void OnElbowHit(PlayerActor attacker, PlayerActor victim)
        {
            if (victim == null) return;
            var pos = victim.transform.position + Vector3.up * 0.9f;
            Play(sfxElbowHit, pos, 0.1f);

            // 撞击特效朝向「攻击者 → 受击者」，冲击波方向才对
            var rot = Quaternion.identity;
            if (attacker != null)
            {
                var dir = victim.transform.position - attacker.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-4f)
                    rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            }
            Spawn(vfxElbowImpact, pos, rot);
        }

        void OnJump(PlayerActor p)
        {
            Play(sfxJump, p != null ? p.transform.position : Vector3.zero);
        }

        void OnLand(PlayerActor p, float fallHeight)
        {
            if (p == null) return;

            bool heavy = fallHeight > heavyLandHeight;
            var clip = (heavy && sfxLandHeavy != null) ? sfxLandHeavy : sfxLand;
            Play(clip, p.transform.position);

            if (heavy) Spawn(vfxLandDust, p.transform.position);
        }

        void OnTileCracking(FloorTile t)
        {
            if (t == null) return;
            Play(sfxTileCracking, t.transform.position, 0.03f);
            Spawn(vfxTileCracking, t.transform.position + Vector3.up * 0.1f);
        }

        void OnTileCollapsed(FloorTile t)
        {
            if (t == null) return;
            Play(sfxTileCollapsed, t.transform.position);
            Spawn(vfxTileCollapse, t.transform.position);
        }

        void OnPitfall(PlayerActor p)
        {
            Play(sfxPitfall, p != null ? p.transform.position : Vector3.zero);
        }

        void OnFinalCollapse()
        {
            Play(sfxFinalCollapse, Vector3.zero, 0f);
        }

        void OnPhaseChanged(RoundPhase phase)
        {
            // ── 警报循环音 ──
            if (alarmLoop != null)
            {
                bool shouldPlay = phase == RoundPhase.Searching;
                if (shouldPlay && !alarmLoop.isPlaying)
                {
                    alarmLoop.loop = true;
                    alarmLoop.Play();
                }
                else if (!shouldPlay && alarmLoop.isPlaying)
                {
                    alarmLoop.Stop();
                }
            }

            // ── 背景音乐 ──
            SwitchMusic(PickBgm(phase));
        }

        AudioClip PickBgm(RoundPhase phase)
        {
            switch (phase)
            {
                case RoundPhase.Intro:      return bgmIntro;
                case RoundPhase.Searching:  return bgmSearching;
                case RoundPhase.Collapse:   return bgmCollapse;
                default:                    return bgmSettlement;
            }
        }

        // ══════════════════════════════════════════════
        //  背景音乐切换
        // ══════════════════════════════════════════════

        AudioClip _pendingBgm;
        Coroutine _fadeRoutine;

        void SwitchMusic(AudioClip next)
        {
            if (musicSource == null) return;

            // 同一首就不重启，否则每次阶段变化音乐都会从头播
            if (musicSource.clip == next && musicSource.isPlaying) return;

            _pendingBgm = next;

            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);

            if (musicFadeTime <= 0.01f)
            {
                ApplyBgmImmediate(next);
                return;
            }
            _fadeRoutine = StartCoroutine(CrossFade(next));
        }

        void ApplyBgmImmediate(AudioClip next)
        {
            musicSource.Stop();
            musicSource.clip = next;
            musicSource.pitch = 1f;
            musicSource.volume = musicVolume;
            if (next != null) musicSource.Play();
        }

        System.Collections.IEnumerator CrossFade(AudioClip next)
        {
            float half = musicFadeTime * 0.5f;

            // 淡出旧曲。用 unscaledDeltaTime —— 命中定格会把 timeScale 压到 0.12，
            // 那时候音乐淡化不该跟着变慢
            if (musicSource.isPlaying)
            {
                float from = musicSource.volume;
                float t = 0f;
                while (t < half)
                {
                    t += Time.unscaledDeltaTime;
                    musicSource.volume = Mathf.Lerp(from, 0f, t / half);
                    yield return null;
                }
            }

            musicSource.Stop();
            musicSource.clip = next;
            musicSource.pitch = 1f;
            musicSource.volume = 0f;

            if (next == null) { _fadeRoutine = null; yield break; }

            musicSource.Play();

            // 淡入新曲
            float t2 = 0f;
            while (t2 < half)
            {
                t2 += Time.unscaledDeltaTime;
                musicSource.volume = Mathf.Lerp(0f, musicVolume, t2 / half);
                yield return null;
            }
            musicSource.volume = musicVolume;
            _fadeRoutine = null;
        }

        void OnTimerTick(float remain)
        {
            // 记录首次收到的剩余时间作为总时长基准
            if (remain > _searchTotal) _searchTotal = remain;
            _searchRemain = remain;

            float t = 1f - Mathf.Clamp01(_searchRemain / Mathf.Max(0.01f, _searchTotal));

            // 越接近结束，警报越急越响
            if (alarmLoop != null && alarmLoop.isPlaying)
            {
                alarmLoop.pitch = Mathf.Lerp(alarmPitchRange.x, alarmPitchRange.y, t);
                alarmLoop.volume = Mathf.Lerp(alarmVolumeRange.x, alarmVolumeRange.y, t);
            }

            // ★BGM 末尾提速。只在最后 35% 才开始，前面保持原速 ——
            //   全程渐变的话玩家察觉不到，集中在末段才有「时间要到了」的压迫感。
            //   淡化过程中不要动 pitch，否则会和 CrossFade 打架
            if (musicSource != null && musicSource.isPlaying
                && _fadeRoutine == null && musicEndPitchUp > 1.001f)
            {
                float k = Mathf.InverseLerp(0.65f, 1f, t);
                musicSource.pitch = Mathf.Lerp(1f, musicEndPitchUp, k);
            }
        }
    }
}
