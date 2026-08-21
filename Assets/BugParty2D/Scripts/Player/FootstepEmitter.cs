using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 按移动距离触发脚步声。挂在玩家身上，建场时自动添加。
    ///
    /// 【为什么按距离而不是按时间】
    /// 固定时间间隔（比如每 0.35 秒一步）在两种情况下会明显不对：
    ///   · 玩家贴着墙推 —— 原地不动却一直在响
    ///   · 被击退滑行 —— 没有走的动作却在响
    /// 按累计水平位移触发，走多远响多少步，与实际动作天然同步；
    /// 而且走快走慢的步频会自动不同，不需要额外调参。
    ///
    /// 声音本体在 RoomAudioVfx.sfxFootsteps 槽位（可放多个随机取），
    /// 这里只负责「什么时候该响一声」。槽位留空就完全不响，不报错。
    /// </summary>
    [DisallowMultipleComponent]
    public class FootstepEmitter : MonoBehaviour
    {
        [Tooltip("走多远算一步。角色身高 1.5 米时 0.55~0.75 比较自然；\n" +
                 "太小会像小碎步，太大会像巨人")]
        [Min(0.1f)] public float strideLength = 0.62f;

        [Tooltip("低于这个速度不计步。避免被击退滑行、或摇杆微小偏移时响脚步")]
        [Min(0f)] public float minSpeed = 0.6f;

        [Tooltip("站在平台（会议桌/矮柜）上时改用 sfxFootstepsPlatform。\n" +
                 "判定阈值：脚下高度超过这个值就算在平台上")]
        [Min(0.05f)] public float platformHeight = 0.3f;

        [Tooltip("关掉就完全不发脚步声（也可以直接禁用本组件）")]
        public bool emitFootsteps = true;

        PlayerActor _actor;
        float _accum;
        Vector3 _lastPos;

        void Awake()
        {
            _actor = GetComponent<PlayerActor>();
            _lastPos = transform.position;
        }

        void Update()
        {
            if (!emitFootsteps || _actor == null) return;

            var pos = transform.position;

            // 只算水平位移 —— 跳跃时的上下移动不该计步（那是 sfxJump/sfxLand 的事）
            var delta = pos - _lastPos;
            delta.y = 0f;
            _lastPos = pos;

            // ★以下四种状态都不该响脚步：
            //   离地（跳跃/坠落）、被击退硬直、正在搜索（弯腰不动）、已出局
            if (!_actor.IsGrounded || _actor.IsStaggered ||
                _actor.IsSearching || !_actor.IsAlive)
            {
                _accum = 0f;
                return;
            }

            // 速度门限：用实际速度而非位移/dt，后者在低帧率下会抖
            if (_actor.HorizontalSpeed < minSpeed)
            {
                _accum = 0f;
                return;
            }

            _accum += delta.magnitude;
            if (_accum < strideLength) return;
            _accum -= strideLength;

            var bus = RoomAudioVfx.Instance;
            if (bus == null) return;

            // 脚下是不是平台：地板顶面在 y=0，站在平台上会明显更高
            bool onPlatform = pos.y > platformHeight;
            bus.PlayFootstep(pos, onPlatform);
        }
    }
}
