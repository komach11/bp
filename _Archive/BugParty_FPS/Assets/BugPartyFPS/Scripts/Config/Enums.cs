using UnityEngine;

namespace BugParty.FPS
{
    public enum PlayerColor { Red = 0, Blue = 1, Yellow = 2, Green = 3 }

    public enum ItemCategory { Fishing = 0, Destruction = 1, Cooking = 2, Police = 3 }

    public enum RoomTheme { Fishing = 0, Cooking = 1, Police = 2 }

    /// <summary>
    /// 回合阶段。相比俯视版多了 Extraction（撤离窗口）。
    /// </summary>
    public enum RoundPhase
    {
        /// <summary>入场：门关闭，不可操作</summary>
        Intro = 0,
        /// <summary>搜刮：可自由行动</summary>
        Looting = 1,
        /// <summary>撤离窗口：传送门开启，必须跑到门里才算带走战利品 ★塔科夫核心</summary>
        Extraction = 2,
        /// <summary>结算</summary>
        Settlement = 3,
        /// <summary>结束</summary>
        Finished = 4
    }

    /// <summary>玩家的行动姿态，影响移速、噪音与视高。</summary>
    public enum Stance
    {
        /// <summary>站立：正常速度，正常噪音</summary>
        Stand = 0,
        /// <summary>疾跑：最快，噪音最大，无法搜刮</summary>
        Sprint = 1,
        /// <summary>下蹲：慢，几乎无声，视线降低 ★潜行核心</summary>
        Crouch = 2
    }

    /// <summary>撤离结果，用于结算展示。</summary>
    public enum ExtractResult
    {
        /// <summary>还在场内</summary>
        InRaid = 0,
        /// <summary>成功撤离，战利品保留</summary>
        Extracted = 1,
        /// <summary>倒计时结束仍未撤离，战利品全部丢失 ★惩罚</summary>
        Failed = 2
    }

    public static class PlayerColorExtensions
    {
        public static Color ToColor(this PlayerColor c)
        {
            switch (c)
            {
                case PlayerColor.Red:    return new Color(0.90f, 0.24f, 0.24f);
                case PlayerColor.Blue:   return new Color(0.21f, 0.54f, 0.87f);
                case PlayerColor.Yellow: return new Color(0.94f, 0.75f, 0.18f);
                case PlayerColor.Green:  return new Color(0.36f, 0.72f, 0.30f);
                default:                 return Color.gray;
            }
        }

        public static string ToLabel(this PlayerColor c)
        {
            switch (c)
            {
                case PlayerColor.Red:    return "红";
                case PlayerColor.Blue:   return "蓝";
                case PlayerColor.Yellow: return "黄";
                case PlayerColor.Green:  return "绿";
                default:                 return "?";
            }
        }
    }
}
