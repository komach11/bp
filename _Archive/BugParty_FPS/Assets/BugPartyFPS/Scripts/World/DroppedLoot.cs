using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 掉落在地上的战利品。被打落后生成，任何人靠近可拾取。
    /// 这是「打劫」成立的关键——打掉对手的东西自己才能捡走。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class DroppedLoot : MonoBehaviour
    {
        public ItemDefinition definition;

        [Header("表现")]
        public float spinSpeed = 110f;
        public float bobAmplitude = 0.06f;
        public float bobSpeed = 2.4f;

        [Tooltip("拾取半径")]
        public float pickupRadius = 1.1f;

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

        /// <summary>生成一个掉落物。</summary>
        public static DroppedLoot Spawn(
            ItemDefinition def, Vector3 origin, Vector3 popDir, RaidConfig cfg)
        {
            if (def == null) return null;

            GameObject go;
            if (def.worldPrefab != null)
            {
                go = Instantiate(def.worldPrefab, origin, Random.rotation);
            }
            else
            {
                go = new GameObject("Loot_" + def.itemId);
                go.transform.position = origin;

                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Visual";
                visual.transform.SetParent(go.transform, false);
                // 用道具的网格体积决定占位体形状，视觉上能看出大小差异
                visual.transform.localScale = new Vector3(
                    def.placeholderSize.x * def.gridWidth,
                    def.placeholderSize.y * def.gridHeight,
                    def.placeholderSize.z);

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

            go.name = "DroppedLoot_" + def.itemId;

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.35f;
            rb.drag = 0.5f;

            var sc = go.GetComponent<SphereCollider>();
            if (sc == null) sc = go.AddComponent<SphereCollider>();
            sc.radius = 0.25f;

            var dl = go.GetComponent<DroppedLoot>();
            if (dl == null) dl = go.AddComponent<DroppedLoot>();
            dl.definition = def;
            dl._pickupTime = Time.time + (cfg != null ? cfg.droppedItemPickupDelay : 0.5f);

            float force = cfg != null ? cfg.droppedItemPopForce : 4f;
            var dir = popDir.sqrMagnitude > 0.0001f ? popDir.normalized : Vector3.up;
            rb.AddForce(dir * force, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 3f, ForceMode.Impulse);

            return dl;
        }

        void Start()
        {
            _visual = transform.Find("Visual");
            if (_visual != null) _visualBaseY = _visual.localPosition.y;
        }

        void Update()
        {
            if (!_settled && Time.time - _spawnTime > 0.9f && _rb.velocity.magnitude < 0.4f)
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

        /// <summary>走近自动拾取。装不下就捡不起来。</summary>
        void TryPickup()
        {
            if (Time.time < _pickupTime) return;

            var mgr = RaidManager.Instance;
            if (mgr == null || !mgr.CanAct) return;

            float bestSqr = pickupRadius * pickupRadius;
            PlayerRig best = null;

            for (int i = 0; i < mgr.players.Count; i++)
            {
                var p = mgr.players[i];
                if (p == null || !p.IsAlive) continue;
                if (!p.Inventory.CanFit(definition)) continue;

                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = p; }
            }

            if (best != null && best.Inventory.TryAdd(definition))
            {
                RaidEvents.RaiseLootTaken(best, definition);
                Destroy(gameObject);
            }
        }
    }
}
