using System.Collections;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// ★让家具/容器随地板塌陷一起掉下去。
    ///
    /// 问题背景：地板块自己用协程做下坠动画，而家具是独立的静态物体，
    /// 两者没有任何关联。结果地板塌光之后，桌子、柜子、容器全都悬在空中。
    ///
    /// 处理方式分两种情形：
    ///   · 单块塌陷（搜索阶段）—— 只有脚下那块地板塌了才掉。
    ///     用 FloorGrid.GetTileAt() 判断自己站在哪块上。
    ///   · 终局全塌 —— 所有挂了本组件的物件一起掉，按到震中的距离错开时间，
    ///     与地板的波浪扩散节奏保持一致。
    ///
    /// 掉落用 Rigidbody 而不是协程：物件之间会互相碰撞堆叠，
    /// 比整齐划一地平移下沉更有"整个房间塌了"的说服力。
    /// </summary>
    public class FallingProp : MonoBehaviour
    {
        [Header("触发条件")]
        [Tooltip("脚下地板塌陷时是否跟着掉。\n关掉则只在终局全塌时掉。")]
        public bool fallWithTileBelow = true;

        [Tooltip("终局全塌时是否掉落")]
        public bool fallOnFinalCollapse = true;

        [Tooltip("往下探多远来判定「脚下那块地板」。\n平台类物件本身有高度，需要从底面往下探。")]
        [Min(0.05f)] public float groundProbe = 0.6f;

        [Header("掉落表现")]
        [Tooltip("掉落前的迟滞。让家具比地板晚一点点掉，视觉上更像\"失去支撑\"而不是同步下沉。")]
        [Min(0f)] public float fallDelay = 0.12f;

        [Tooltip("终局时按到震中的距离额外延迟（秒/米），与地板波浪同步")]
        [Min(0f)] public float waveDelayPerMeter = 0.035f;

        [Tooltip("掉落时施加的随机翻滚力度")]
        [Min(0f)] public float tumbleTorque = 2.5f;

        [Tooltip("质量。大件家具重一些，掉起来更有分量")]
        [Min(0.1f)] public float mass = 1.4f;

        [Tooltip("坠到这个深度后销毁自己，避免物理体一直在场景里跑")]
        public float despawnDepth = -28f;

        // ── 运行时 ─────────────────────────────────────
        bool _falling;
        Rigidbody _rb;
        Collider[] _cols;
        Vector3 _originPos;
        Quaternion _originRot;
        FloorGrid _grid;

        public bool IsFalling => _falling;

        void Awake()
        {
            _originPos = transform.position;
            _originRot = transform.rotation;
            _cols = GetComponentsInChildren<Collider>(true);
        }

        void OnEnable()
        {
            RoomEvents.OnTileCollapsed += HandleTileCollapsed;
            RoomEvents.OnFinalCollapseStarted += HandleFinalCollapse;
        }

        void OnDisable()
        {
            RoomEvents.OnTileCollapsed -= HandleTileCollapsed;
            RoomEvents.OnFinalCollapseStarted -= HandleFinalCollapse;
        }

        void Start()
        {
            _grid = RoomManager.Instance != null ? RoomManager.Instance.floorGrid : null;
            if (_grid == null) _grid = FindObjectOfType<FloorGrid>();
        }

        // ══════════════════════════════════════════════

        /// <summary>单块地板塌了：只有塌的正好是自己脚下那块才掉。</summary>
        void HandleTileCollapsed(FloorTile tile)
        {
            if (_falling || !fallWithTileBelow || tile == null) return;
            if (!IsStandingOn(tile)) return;

            StartCoroutine(FallAfter(fallDelay));
        }

        /// <summary>终局全塌：按到震中的距离错开，跟上地板的波浪节奏。</summary>
        void HandleFinalCollapse()
        {
            if (_falling || !fallOnFinalCollapse) return;

            // 震中与 RoomManager.FinalCollapseRoutine 保持一致：房间中心格
            Vector3 epicenter = Vector3.zero;
            if (_grid != null)
                epicenter = _grid.GridToWorld(new Vector2Int(_grid.columns / 2, _grid.rows / 2));

            var a = new Vector2(transform.position.x, transform.position.z);
            var b = new Vector2(epicenter.x, epicenter.z);
            float dist = Vector2.Distance(a, b);

            StartCoroutine(FallAfter(fallDelay + dist * waveDelayPerMeter));
        }

        /// <summary>
        /// 判断这块地板是不是自己的支撑。
        /// 从物件底面中心往下探，取到的格子与传入的比对。
        /// </summary>
        bool IsStandingOn(FloorTile tile)
        {
            if (_grid == null) return false;

            var p = transform.position;

            // 从包围盒底面往下探一点，避免物件本身高度造成误判
            float bottom = p.y;
            if (_cols != null && _cols.Length > 0)
            {
                bool init = false;
                var b = new Bounds();
                foreach (var c in _cols)
                {
                    if (c == null || !c.enabled) continue;
                    if (!init) { b = c.bounds; init = true; }
                    else b.Encapsulate(c.bounds);
                }
                if (init) bottom = b.min.y;
            }

            var probe = new Vector3(p.x, bottom - groundProbe, p.z);
            var below = _grid.GetTileAt(probe);
            return below == tile;
        }

        // ══════════════════════════════════════════════

        IEnumerator FallAfter(float delay)
        {
            _falling = true;
            if (delay > 0f) yield return new WaitForSeconds(delay);
            BeginFall();
        }

        /// <summary>切换成物理体开始掉落。</summary>
        public void BeginFall()
        {
            _falling = true;

            // ★掉落中的家具不能再把玩家顶飞或挡路。
            //   CharacterController 与 Rigidbody 相撞会产生很怪的推挤，
            //   而且玩家此时正在剧情性坠落，被家具卡住会很难受。
            //   改成 Trigger：保留渲染与下坠，但不再与玩家碰撞。
            //   家具彼此之间的堆叠效果由 Rigidbody 之间的重力接触实现即可。
            if (_cols != null)
            {
                foreach (var c in _cols)
                    if (c != null) c.isTrigger = true;
            }

            _rb = GetComponent<Rigidbody>();
            if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();

            _rb.mass = mass;
            _rb.useGravity = true;
            _rb.isKinematic = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;

            // 给一点随机初速与翻滚，避免整齐划一地垂直下落
            _rb.AddForce(new Vector3(
                Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f)),
                ForceMode.VelocityChange);
            _rb.AddTorque(Random.insideUnitSphere * tumbleTorque, ForceMode.VelocityChange);

            StartCoroutine(DespawnWhenDeep());
        }

        IEnumerator DespawnWhenDeep()
        {
            while (transform.position.y > despawnDepth)
                yield return new WaitForSeconds(0.4f);

            // 沉得够深就关掉渲染与物理，省开销。不 Destroy，留给 ResetProp 复位。
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = false;
            if (_rb != null) _rb.isKinematic = true;
        }

        // ══════════════════════════════════════════════

        /// <summary>重开一局时复位。与 FloorTile.ResetTile 对应。</summary>
        public void ResetProp()
        {
            StopAllCoroutines();
            _falling = false;

            if (_rb != null)
            {
                _rb.isKinematic = true;
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                Destroy(_rb);
                _rb = null;
            }

            // 还原碰撞体，重新变回可以站立的实体
            if (_cols != null)
            {
                foreach (var c in _cols)
                    if (c != null) c.isTrigger = false;
            }

            transform.SetPositionAndRotation(_originPos, _originRot);

            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r != null) r.enabled = true;
        }
    }
}
