using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 掉落在地上的道具实体。被肘击打掉的道具会变成这个，可以被重新拾取。
    /// 这就是"击落已收集的道具"这条规则的落地表现。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(SphereCollider))]
    public class WorldItem : MonoBehaviour
    {
        [Header("数据")]
        public ItemDefinition definition;

        [Header("表现")]
        [Tooltip("落地后的旋转速度，让它显眼一点")]
        public float spinSpeed = 90f;

        [Tooltip("上下浮动幅度")]
        public float bobAmplitude = 0.08f;

        public float bobSpeed = 2.2f;

        Rigidbody _rb;
        float _pickupAvailableTime;
        float _spawnTime;
        Transform _visual;
        float _visualBaseY;
        bool _settled;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _spawnTime = Time.time;
        }

        /// <summary>
        /// 生成一个掉落道具。被肘击打飞时由 PlayerActor 调用。
        /// </summary>
        public static WorldItem SpawnDropped(
            ItemDefinition def, Vector3 origin, Vector3 popDir, SearchRoomConfig cfg)
        {
            if (def == null) return null;

            GameObject go;
            if (def.worldPrefab != null)
            {
                go = Instantiate(def.worldPrefab, origin, Random.rotation);
            }
            else
            {
                // 无 Prefab 时生成占位方块，颜色取道具定义
                go = new GameObject("Item_" + def.itemId);
                go.transform.position = origin;

                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Visual";
                visual.transform.SetParent(go.transform, false);
                visual.transform.localScale = def.placeholderSize;
                // 移除自带碰撞体，统一由父物体的 SphereCollider 负责
                var c = visual.GetComponent<Collider>();
                if (c != null) Destroy(c);

                var r = visual.GetComponent<Renderer>();
                if (r != null)
                {
                    var m = r.material;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", def.placeholderColor);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", def.placeholderColor);
                }
            }

            go.name = "DroppedItem_" + def.itemId;

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.drag = 0.4f;

            var sc = go.GetComponent<SphereCollider>();
            if (sc == null) sc = go.AddComponent<SphereCollider>();
            sc.radius = 0.28f;
            sc.isTrigger = false;

            var wi = go.GetComponent<WorldItem>();
            if (wi == null) wi = go.AddComponent<WorldItem>();
            wi.definition = def;

            float delay = cfg != null ? cfg.droppedItemPickupDelay : 0.4f;
            wi._pickupAvailableTime = Time.time + delay;

            // 打飞
            float force = cfg != null ? cfg.itemPopForce : 4.5f;
            var dir = popDir.sqrMagnitude > 0.0001f ? popDir.normalized : Vector3.up;
            rb.AddForce(dir * force, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 3f, ForceMode.Impulse);

            return wi;
        }

        void Start()
        {
            _visual = transform.Find("Visual");
            if (_visual != null) _visualBaseY = _visual.localPosition.y;
        }

        void Update()
        {
            // 落地静止后开始转圈浮动，提示"我可以被捡"
            if (!_settled && Time.time - _spawnTime > 0.8f && _rb.velocity.magnitude < 0.35f)
            {
                _settled = true;
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.useGravity = true;
            }

            if (!_settled) return;

            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

            if (_visual != null)
            {
                var lp = _visual.localPosition;
                lp.y = _visualBaseY + Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
                _visual.localPosition = lp;
            }

            TryAutoPickup();
        }

        /// <summary>
        /// 走过去自动捡。不需要额外按键，节奏更快、更适合派对游戏。
        /// </summary>
        void TryAutoPickup()
        {
            if (Time.time < _pickupAvailableTime) return;

            var mgr = SearchRoomManager.Instance;
            if (mgr == null || !mgr.CanAct) return;

            const float pickRange = 0.85f;
            float bestSqr = pickRange * pickRange;
            PlayerActor best = null;

            for (int i = 0; i < mgr.players.Count; i++)
            {
                var p = mgr.players[i];
                if (p == null || !p.IsAlive) continue;
                if (p.Inventory.IsFull) continue;

                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p; }
            }

            if (best != null && best.Inventory.TryAdd(definition))
            {
                SearchRoomEvents.RaiseItemCollected(best, definition);
                Destroy(gameObject);
            }
        }
    }
}
