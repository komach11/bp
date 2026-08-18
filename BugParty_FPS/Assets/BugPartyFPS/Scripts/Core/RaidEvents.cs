using System;

namespace BugParty.FPS
{
    /// <summary>
    /// 全局事件总线。接音效、特效、震动都从这里订阅，不要改玩法脚本。
    /// </summary>
    public static class RaidEvents
    {
        public static event Action<RoundPhase> OnPhaseChanged;
        public static event Action<float> OnTimerTick;

        /// <summary>搜到战利品。参数：玩家、道具</summary>
        public static event Action<PlayerRig, ItemDefinition> OnLootTaken;

        /// <summary>战利品被打落。参数：失主、道具</summary>
        public static event Action<PlayerRig, ItemDefinition> OnLootDropped;

        /// <summary>近战命中。参数：攻击者、受害者、是否背刺</summary>
        public static event Action<PlayerRig, PlayerRig, bool> OnMeleeHit;

        /// <summary>近战挥空。参数：攻击者</summary>
        public static event Action<PlayerRig> OnMeleeMiss;

        /// <summary>开始搜刮某容器</summary>
        public static event Action<PlayerRig, LootContainer> OnLootStarted;

        /// <summary>搜刮被打断</summary>
        public static event Action<PlayerRig, LootContainer> OnLootInterrupted;

        /// <summary>搜刮界面开启 / 关闭。参数：玩家、容器、是否开启</summary>
        public static event Action<PlayerRig, LootContainer, bool> OnLootPanelToggled;

        /// <summary>背包变化</summary>
        public static event Action<PlayerRig> OnInventoryChanged;

        /// <summary>发出噪音。参数：来源、世界位置、半径。AI 听觉与音效系统订阅它</summary>
        public static event Action<PlayerRig, UnityEngine.Vector3, float> OnNoiseEmitted;

        /// <summary>撤离进度更新。参数：玩家、0~1 进度</summary>
        public static event Action<PlayerRig, float> OnExtractProgress;

        /// <summary>撤离完成。参数：玩家、带走的总价值</summary>
        public static event Action<PlayerRig, int> OnExtracted;

        /// <summary>撤离失败（超时未离场）。参数：玩家、丢失的总价值</summary>
        public static event Action<PlayerRig, int> OnExtractFailed;

        // ── 触发器 ─────────────────────────────────────

        public static void RaisePhaseChanged(RoundPhase p) => OnPhaseChanged?.Invoke(p);
        public static void RaiseTimerTick(float t) => OnTimerTick?.Invoke(t);
        public static void RaiseLootTaken(PlayerRig r, ItemDefinition i) => OnLootTaken?.Invoke(r, i);
        public static void RaiseLootDropped(PlayerRig r, ItemDefinition i) => OnLootDropped?.Invoke(r, i);
        public static void RaiseMeleeHit(PlayerRig a, PlayerRig v, bool back) => OnMeleeHit?.Invoke(a, v, back);
        public static void RaiseMeleeMiss(PlayerRig a) => OnMeleeMiss?.Invoke(a);
        public static void RaiseLootStarted(PlayerRig r, LootContainer c) => OnLootStarted?.Invoke(r, c);
        public static void RaiseLootInterrupted(PlayerRig r, LootContainer c) => OnLootInterrupted?.Invoke(r, c);
        public static void RaiseLootPanelToggled(PlayerRig r, LootContainer c, bool open) => OnLootPanelToggled?.Invoke(r, c, open);
        public static void RaiseInventoryChanged(PlayerRig r) => OnInventoryChanged?.Invoke(r);
        public static void RaiseNoise(PlayerRig r, UnityEngine.Vector3 pos, float radius) => OnNoiseEmitted?.Invoke(r, pos, radius);
        public static void RaiseExtractProgress(PlayerRig r, float t) => OnExtractProgress?.Invoke(r, t);
        public static void RaiseExtracted(PlayerRig r, int value) => OnExtracted?.Invoke(r, value);
        public static void RaiseExtractFailed(PlayerRig r, int value) => OnExtractFailed?.Invoke(r, value);

        public static void ClearAll()
        {
            OnPhaseChanged = null;
            OnTimerTick = null;
            OnLootTaken = null;
            OnLootDropped = null;
            OnMeleeHit = null;
            OnMeleeMiss = null;
            OnLootStarted = null;
            OnLootInterrupted = null;
            OnLootPanelToggled = null;
            OnInventoryChanged = null;
            OnNoiseEmitted = null;
            OnExtractProgress = null;
            OnExtracted = null;
            OnExtractFailed = null;
        }
    }
}
