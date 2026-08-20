using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>掉落在地上的道具。可被任何人拾取。</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class WorldItem : MonoBehaviour
    {
        public ItemDefinition definition;

        [Header("表现")]
        public float spinSpeed = 100f;
        public float bobAmplitude = 0.07f;
        public float bobSpeed = 2.3f;
        public float pickupRadius = 0.95f;

        Rigidbody _rb;
        Transform _visual;
        float _visualBaseY;
        float _pickupTime;
        float _spawnTime;
        bool _settled;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _spawnTime = Time.time;
        }

        public static WorldItem SpawnDropped(
            ItemDefinition def, Vector3 origin, Vector3 popDir, RoomConfig cfg)
        {
            if (def == null) return null;

            GameObject go;
            if (def.worldPrefab != null)
            {
                go = Instantiate(def.worldPrefab, origin, Random.rotation);
            }
            else
            {
                go = new GameObject("Item_" + def.itemId);
                go.transform.position = origin;

                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Visual";
                visual.transform.SetParent(go.transform, false);
                visual.transform.localScale = def.placeholderSize;

                var vc = visual.GetComponent<Collider>();
                if (vc != null) Destroy(vc);

                var r = visual.GetComponent<Renderer>();
                if (r != null)
                {
                    var m = r.material;
                    var col = def.isRare
                        ? Color.Lerp(def.placeholderColor, new Color(1f, 0.85f, 0.2f), 0.5f)
                        : def.placeholderColor;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", col);
                }
            }

            go.name = "DroppedItem_" + def.itemId;

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            rb.drag = 0.45f;

            var sc = go.GetComponent<SphereCollider>();
            if (sc == null) sc = go.AddComponent<SphereCollider>();
            sc.radius = 0.26f;

            var wi = go.GetComponent<WorldItem>();
            if (wi == null) wi = go.AddComponent<WorldItem>();
            wi.definition = def;
            wi._pickupTime = Time.time + (cfg != null ? cfg.droppedItemPickupDelay : 0.4f);

            float force = cfg != null ? cfg.itemPopForce : 5f;
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
            var mgr = RoomManager.Instance;

            // ★掉出场景的道具要救回来，而不是销毁。
            //
            // 原先是直接 Destroy —— 但搜索阶段会随机塌 5 块地板，掉落的道具
            // 落在当时的安全地板上，那块地板之后也可能塌，道具就跟着沉进虚空。
            // 玩家辛苦搜到的东西反复凭空消失，而且最终交接给捕鱼场景的
            // CarryOverData 可能一件不剩。
            //
            // 现在改为传送回最近的安全地板。只有终局全塌（此时已无安全地板可言、
            // 玩家也都在坠落）才真正销毁。
            if (transform.position.y < -12f)
            {
                bool finalPhase = mgr != null
                    && (mgr.Phase == RoundPhase.Collapse || mgr.Phase == RoundPhase.Transition
                        || mgr.Phase == RoundPhase.Finished);

                if (finalPhase || mgr == null || mgr.floorGrid == null)
                {
                    Destroy(gameObject);
                    return;
                }

                Rescue(mgr);
                return;
            }

            if (!_settled && Time.time - _spawnTime > 0.85f && _rb.velocity.magnitude < 0.38f)
            {
                _settled = true;
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            if (!_settled) return;

            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

            if (_visual != null)
            {
                var lp = _visual.localPosition;
                lp.y = _visualBaseY + Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
                _visual.localPosition = lp;
            }

            TryPickup();
        }

        void TryPickup()
        {
            if (Time.time < _pickupTime) return;

            var mgr = RoomManager.Instance;
            if (mgr == null || !mgr.CanAct) return;

            float bestSqr = pickupRadius * pickupRadius;
            PlayerActor best = null;

            for (int i = 0; i < mgr.players.Count; i++)
            {
                var p = mgr.players[i];
                if (p == null || !p.IsAlive || p.IsInPitfall) continue;
                if (p.Inventory.IsFull) continue;

                // 3D 距离：站在桌上捡不到地上的东西
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p; }
            }

            if (best != null && best.Inventory.TryAdd(definition))
            {
                RoomEvents.RaiseItemCollected(best, definition);
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 把掉出场景的道具传送回最近的安全地板，而不是销毁。
        /// 搜索阶段地板会不断塌，道具落点随时可能变成洞 —— 让玩家搜到的东西
        /// 因此凭空消失是很糟糕的体验，也会导致最终交接数据为空。
        /// </summary>
        void Rescue(RoomManager mgr)
        {
            // 用受保护的落点，否则救回来的道具可能又落在会塌的地板上，
            // 反复掉、反复救，看起来像在闪现
            var safe = mgr.floorGrid.FindNearestSafePosition(transform.position, true);
            safe.y += 1.2f;   // 抬高一点，靠自身重力落到地板上

            transform.position = safe;

            if (_rb != null)
            {
                _rb.velocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }

            // 重置沉降状态，让它重新走一遍落地流程（否则会悬在空中转圈）
            _settled = false;
            _spawnTime = Time.time;
        }
    }
}
