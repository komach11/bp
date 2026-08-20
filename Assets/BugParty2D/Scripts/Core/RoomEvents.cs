using System;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>全局事件总线。接音效、特效、震动的唯一入口。</summary>
    public static class RoomEvents
    {
        public static event Action<RoundPhase> OnPhaseChanged;
        public static event Action<float> OnTimerTick;

        public static event Action<PlayerActor, ItemDefinition> OnItemCollected;
        public static event Action<PlayerActor, ItemDefinition> OnItemKnockedOut;
        public static event Action<PlayerActor, PlayerActor> OnElbowHit;

        /// <summary>
        /// ★挥肘瞬间（不论是否命中）。与 OnElbowHit 的区别：
        /// 挥空也需要挥臂动作与破风音，否则玩家按了键却毫无反馈，会以为技能没触发。
        /// 参数：出手者
        /// </summary>
        public static event Action<PlayerActor> OnElbowSwing;

        /// <summary>★搜索完成的那一刻。参数：玩家、容器</summary>
        public static event Action<PlayerActor, SearchContainer> OnSearchCompleted;

        public static event Action<PlayerActor, SearchContainer> OnSearchStarted;
        public static event Action<PlayerActor, SearchContainer> OnSearchInterrupted;
        public static event Action<PlayerActor> OnInventoryChanged;

        /// <summary>玩家跳跃。参数：玩家</summary>
        public static event Action<PlayerActor> OnJump;

        /// <summary>玩家落地。参数：玩家、落地高度</summary>
        public static event Action<PlayerActor, float> OnLand;

        /// <summary>★地板开始开裂预警。参数：地板</summary>
        public static event Action<FloorTile> OnTileCracking;

        /// <summary>★地板塌陷。参数：地板</summary>
        public static event Action<FloorTile> OnTileCollapsed;

        /// <summary>★玩家掉进洞里。参数：玩家</summary>
        public static event Action<PlayerActor> OnPlayerPitfall;

        /// <summary>★玩家从洞里被救回。参数：玩家</summary>
        public static event Action<PlayerActor> OnPlayerRecovered;

        /// <summary>★终局全塌陷开始</summary>
        public static event Action OnFinalCollapseStarted;

        /// <summary>★玩家在终局掉落，准备穿越。参数：玩家</summary>
        public static event Action<PlayerActor> OnPlayerFallingToNextLevel;

        /// <summary>请求一次画面抖动。参数：强度、时长</summary>
        public static event Action<float, float> OnScreenShakeRequested;

        // ── 触发器 ─────────────────────────────────────

        public static void RaisePhaseChanged(RoundPhase p) => OnPhaseChanged?.Invoke(p);
        public static void RaiseTimerTick(float t) => OnTimerTick?.Invoke(t);
        public static void RaiseItemCollected(PlayerActor a, ItemDefinition i) => OnItemCollected?.Invoke(a, i);
        public static void RaiseItemKnockedOut(PlayerActor a, ItemDefinition i) => OnItemKnockedOut?.Invoke(a, i);
        public static void RaiseElbowHit(PlayerActor a, PlayerActor v) => OnElbowHit?.Invoke(a, v);
        public static void RaiseElbowSwing(PlayerActor a) => OnElbowSwing?.Invoke(a);
        public static void RaiseSearchCompleted(PlayerActor a, SearchContainer c) => OnSearchCompleted?.Invoke(a, c);
        public static void RaiseSearchStarted(PlayerActor a, SearchContainer c) => OnSearchStarted?.Invoke(a, c);
        public static void RaiseSearchInterrupted(PlayerActor a, SearchContainer c) => OnSearchInterrupted?.Invoke(a, c);
        public static void RaiseInventoryChanged(PlayerActor a) => OnInventoryChanged?.Invoke(a);
        public static void RaiseJump(PlayerActor a) => OnJump?.Invoke(a);
        public static void RaiseLand(PlayerActor a, float h) => OnLand?.Invoke(a, h);
        public static void RaiseTileCracking(FloorTile t) => OnTileCracking?.Invoke(t);
        public static void RaiseTileCollapsed(FloorTile t) => OnTileCollapsed?.Invoke(t);
        public static void RaisePlayerPitfall(PlayerActor a) => OnPlayerPitfall?.Invoke(a);
        public static void RaisePlayerRecovered(PlayerActor a) => OnPlayerRecovered?.Invoke(a);
        public static void RaiseFinalCollapseStarted() => OnFinalCollapseStarted?.Invoke();
        public static void RaisePlayerFallingToNextLevel(PlayerActor a) => OnPlayerFallingToNextLevel?.Invoke(a);
        public static void RaiseScreenShake(float amount, float duration) => OnScreenShakeRequested?.Invoke(amount, duration);

        public static void ClearAll()
        {
            OnPhaseChanged = null;
            OnTimerTick = null;
            OnItemCollected = null;
            OnItemKnockedOut = null;
            OnElbowHit = null;
            OnSearchStarted = null;
            OnSearchInterrupted = null;
            OnInventoryChanged = null;
            OnJump = null;
            OnLand = null;
            OnTileCracking = null;
            OnTileCollapsed = null;
            OnPlayerPitfall = null;
            OnPlayerRecovered = null;
            OnFinalCollapseStarted = null;
            OnPlayerFallingToNextLevel = null;
            OnScreenShakeRequested = null;
        }
    }
}
