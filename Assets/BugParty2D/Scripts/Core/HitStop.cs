using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 命中定格（hit stop）。命中瞬间把时间放慢极短一下，制造打击的顿感。
    ///
    /// 【为什么要单独一个管理器】
    /// 定格改的是全局 Time.timeScale。如果由每个 ElbowAbility 自己管理恢复，
    /// 4 个玩家同时命中时会互相踩：A 的恢复逻辑可能把 B 刚设的定格取消掉，
    /// 或者各自记录的「原始 timeScale」已经是别人改过的值，恢复后 timeScale 越来越小。
    ///
    /// 这里用一个自动创建的单例统一管理：
    ///   · 原始 timeScale 只在「从无到有」时记录一次
    ///   · 多次请求取最晚的结束时间，而不是叠加缩放
    ///   · 用 unscaledDeltaTime 计时，否则 timeScale 变小会让恢复本身也变慢
    /// </summary>
    [DisallowMultipleComponent]
    public class HitStop : MonoBehaviour
    {
        static HitStop _instance;

        float _origScale = 1f;
        float _endUnscaled;
        bool _active;

        /// <summary>请求一次定格。duration 是真实秒，scale 是定格期间的 timeScale 比例。</summary>
        public static void Request(float duration, float scale)
        {
            if (duration <= 0.0001f) return;

            EnsureInstance();
            if (_instance == null) return;
            _instance.Begin(duration, Mathf.Clamp(scale, 0.01f, 1f));
        }

        static void EnsureInstance()
        {
            if (_instance != null) return;

            var go = new GameObject("~HitStop");
            _instance = go.AddComponent<HitStop>();
            DontDestroyOnLoad(go);
        }

        void Begin(float duration, float scale)
        {
            if (!_active)
            {
                // 只在从无到有时记录原始值，避免嵌套请求把已缩放的值当成原始值
                _origScale = Time.timeScale;
                _active = true;
            }

            // 取最晚的结束时刻，而不是叠加时长
            _endUnscaled = Mathf.Max(_endUnscaled, Time.unscaledTime + duration);
            Time.timeScale = _origScale * scale;
        }

        void Update()
        {
            if (!_active) return;

            if (Time.unscaledTime >= _endUnscaled)
            {
                Time.timeScale = _origScale;
                _active = false;
            }
        }

        void OnDisable()
        {
            // 保险：组件被禁用或场景卸载时一定要把时间恢复，
            // 否则整个游戏会卡在慢动作里
            if (_active)
            {
                Time.timeScale = _origScale;
                _active = false;
            }
        }
    }
}
