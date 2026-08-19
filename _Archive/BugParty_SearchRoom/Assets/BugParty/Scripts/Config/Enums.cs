using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 玩家阵营颜色。全片四色识别贯穿服装、道具光效、UI。
    /// </summary>
    public enum PlayerColor
    {
        Red = 0,
        Blue = 1,
        Yellow = 2,
        Green = 3
    }

    /// <summary>
    /// 道具类别。决定它进入下一关后的作用方向。
    /// </summary>
    public enum ItemCategory
    {
        /// <summary>捕鱼类：大渔网、小渔网、徒手手套</summary>
        Fishing = 0,
        /// <summary>破坏类：小刀、水雷</summary>
        Destruction = 1,
        /// <summary>料理类：辣椒、土豆、番茄、萝卜、菜刀、平底锅</summary>
        Cooking = 2,
        /// <summary>警察类：手铐、扫描器、电击枪、警犬、路障</summary>
        Police = 3
    }

    /// <summary>
    /// 搜索房间的主题。三轮各一个，只换掉落物，房间结构不变。
    /// </summary>
    public enum RoomTheme
    {
        Fishing = 0,
        Cooking = 1,
        Police = 2
    }

    /// <summary>
    /// 回合阶段状态机。
    /// </summary>
    public enum RoundPhase
    {
        /// <summary>入场：四人掉落进房间，门未关，不可操作</summary>
        Intro = 0,
        /// <summary>搜索：门已关，倒计时进行中，可搜可肘击</summary>
        Searching = 1,
        /// <summary>结算：倒计时归零，锁定各人道具，展示结果</summary>
        Settlement = 2,
        /// <summary>传送：传送门依次吸走四人，本环节结束</summary>
        Teleport = 3
    }

    public static class PlayerColorExtensions
    {
        /// <summary>取得该阵营的显示色，用于角色材质、道具光效与 HUD。</summary>
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
