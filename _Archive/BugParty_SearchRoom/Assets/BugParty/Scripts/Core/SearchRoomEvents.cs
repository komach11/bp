using System;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 全局事件总线。让 HUD、音效、特效、AI 都能订阅玩法事件，
    /// 而不需要互相持有引用。后期接音效和特效只要在这里订阅即可。
    /// </summary>
    public static class SearchRoomEvents
    {
        /// <summary>阶段切换。参数：新阶段</summary>
        public static event Action<RoundPhase> OnPhaseChanged;

        /// <summary>倒计时每帧更新。参数：剩余秒数</summary>
        public static event Action<float> OnTimerTick;

        /// <summary>某人成功搜到一件道具。参数：拾取者、道具</summary>
        public static event Action<PlayerActor, ItemDefinition> OnItemCollected;

        /// <summary>某人的道具被打落。参数：失主、道具</summary>
        public static event Action<PlayerActor, ItemDefinition> OnItemKnockedOut;

        /// <summary>肘击命中。参数：攻击者、受害者</summary>
        public static event Action<PlayerActor, PlayerActor> OnElbowHit;

        /// <summary>搜索进程被打断。参数：被打断者、容器</summary>
        public static event Action<PlayerActor, SearchContainer> OnSearchInterrupted;

        /// <summary>搜索开始。参数：搜索者、容器</summary>
        public static event Action<PlayerActor, SearchContainer> OnSearchStarted;

        /// <summary>背包内容发生任何变化，HUD 应刷新。参数：该玩家</summary>
        public static event Action<PlayerActor> OnInventoryChanged;

        // ── 触发器（仅供逻辑层调用）──────────────────────────

        public static void RaisePhaseChanged(RoundPhase p) => OnPhaseChanged?.Invoke(p);
        public static void RaiseTimerTick(float t) => OnTimerTick?.Invoke(t);
        public static void RaiseItemCollected(PlayerActor a, ItemDefinition i) => OnItemCollected?.Invoke(a, i);
        public static void RaiseItemKnockedOut(PlayerActor a, ItemDefinition i) => OnItemKnockedOut?.Invoke(a, i);
        public static void RaiseElbowHit(PlayerActor atk, PlayerActor vic) => OnElbowHit?.Invoke(atk, vic);
        public static void RaiseSearchInterrupted(PlayerActor a, SearchContainer c) => OnSearchInterrupted?.Invoke(a, c);
        public static void RaiseSearchStarted(PlayerActor a, SearchContainer c) => OnSearchStarted?.Invoke(a, c);
        public static void RaiseInventoryChanged(PlayerActor a) => OnInventoryChanged?.Invoke(a);

        /// <summary>
        /// 场景卸载时清空所有订阅，防止编辑器下事件残留导致空引用。
        /// 由 SearchRoomManager.OnDestroy 调用。
        /// </summary>
        public static void ClearAll()
        {
            OnPhaseChanged = null;
            OnTimerTick = null;
            OnItemCollected = null;
            OnItemKnockedOut = null;
            OnElbowHit = null;
            OnSearchInterrupted = null;
            OnSearchStarted = null;
            OnInventoryChanged = null;
        }
    }
}
