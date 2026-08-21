using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 粒子播完后自动销毁自己。
    ///
    /// 为什么需要它而不是直接 Destroy(go, lifetime)：
    /// 固定时长的 Destroy 与粒子实际生命周期不同步 —— 时间短了粒子被"剪断"
    /// 突然消失，时间长了空对象堆在场景里。一次肘击可能生成多个特效，
    /// 累积起来会拖慢帧率。
    ///
    /// 这个组件按「最长的粒子系统实际还剩多久」来判断，播完才删。
    /// </summary>
    [DisallowMultipleComponent]
    public class VfxAutoKill : MonoBehaviour
    {
        [Tooltip("粒子播完后再等多久才销毁。留一点余量让最后一批粒子淡完")]
        [Min(0f)] public float extraDelay = 0.3f;

        [Tooltip("兜底上限。即使粒子判定一直没结束，超过这个时间也强制销毁。\n" +
                 "防止误配成 looping 的特效永久残留")]
        [Min(0.5f)] public float hardLimit = 8f;

        float _born;
        float _doneAt = -1f;        // 粒子全部结束的时刻，-1 = 还没结束
        ParticleSystem[] _systems;
        AudioSource[] _audios;

        void Awake()
        {
            _born = Time.unscaledTime;
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            _audios = GetComponentsInChildren<AudioSource>(true);
        }

        void Update()
        {
            // ★用 unscaledTime：命中定格会把 timeScale 压到 0.12，
            //   若用 Time.time，定格期间存活判断也会变慢，看起来像特效卡住
            float now = Time.unscaledTime;

            if (now - _born > hardLimit)
            {
                Destroy(gameObject);
                return;
            }

            // 起步宽限：Awake 当帧粒子可能还没开始发射，IsAlive 会返回 false
            if (now - _born < 0.15f) return;

            if (IsStillPlaying())
            {
                _doneAt = -1f;      // 又活了（多层粒子有延迟发射的情况）
                return;
            }

            if (_doneAt < 0f)
            {
                _doneAt = now;
                return;
            }

            if (now - _doneAt >= extraDelay) Destroy(gameObject);
        }

        bool IsStillPlaying()
        {
            if (_systems != null)
            {
                for (int i = 0; i < _systems.Length; i++)
                {
                    var ps = _systems[i];
                    if (ps == null) continue;
                    // withChildren=false —— 子系统在数组里各自会被检查，
                    // 传 true 会重复遍历整棵树，粒子多时开销明显
                    if (ps.IsAlive(false)) return true;
                }
            }

            if (_audios != null)
            {
                for (int i = 0; i < _audios.Length; i++)
                    if (_audios[i] != null && _audios[i].isPlaying) return true;
            }
            return false;
        }
    }
}
