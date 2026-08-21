using System.Collections.Generic;
using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 单个美术槽位。prefab 为空时建场工具回退到程序生成的占位体。
    /// </summary>
    [System.Serializable]
    public class ArtSlot
    {
        [Tooltip("美术模型。留空则用程序生成的占位方块。")]
        public GameObject prefab;

        [Tooltip("额外缩放系数。1 = 自动适配到关卡尺寸后不再调整。\n" +
                 "低多边形素材包常常不是米制单位，自动适配已处理，这里只做微调。")]
        [Min(0.01f)] public float scaleMul = 1f;

        [Tooltip("绕 Y 轴的额外旋转（度）。模型朝向与 +Z 不一致时用它纠正。")]
        [Range(-180f, 180f)] public float yawOffset = 0f;

        [Tooltip("垂直微调。模型脚底不在 y=0 时用它补偿。")]
        public float yOffset = 0f;

        [Tooltip("勾选后按渲染包围盒把模型等比缩放并把底面贴到 y=0（适合静态家具）。\n\n" +
                 "★带骨骼动画的角色务必取消勾选 —— SkinnedMeshRenderer 的包围盒是\n" +
                 "T-pose 下的静态 AABB，与真实骨骼位置能差出半米，用它对齐会让角色悬空。\n" +
                 "取消后只应用 scaleMul 与 yOffset，位置以 Prefab 内的摆放为准。")]
        public bool fitToSize = true;

        public bool HasArt => prefab != null;
    }

    /// <summary>
    /// 按名字指定容器外观。名字需与 RoomSceneBuilder2D 里的容器名一致
    /// （文件柜 / 饮水机 / 投影仪箱 / 打印机 / 纸箱堆 / 垃圾桶 / 杂物架 / 工具箱 …）
    /// </summary>
    [System.Serializable]
    public class NamedArtSlot
    {
        [Tooltip("容器名，需与建场代码里的名字完全一致")]
        public string key = "";
        public ArtSlot art = new ArtSlot();
    }

    /// <summary>
    /// 全部美术资源槽位。与 RoomConfig（玩法数值）分离，
    /// 让策划与美术各改各的文件，避免同时编辑同一个 .asset 造成 YAML 冲突。
    ///
    /// Assets ▸ Create ▸ BugParty2D ▸ Room Art Config
    /// </summary>
    [CreateAssetMenu(fileName = "RoomArtConfig2D", menuName = "BugParty2D/Room Art Config", order = 1)]
    public class RoomArtConfig : ScriptableObject
    {
        // ═══════════════════════════════════════════════
        [Header("═══ 角色 ═══")]

        [Tooltip("★四个角色模型，按 红/蓝/黄/绿 顺序。\n" +
                 "要求：根节点朝 +Z，脚底在 y=0。\n" +
                 "带 Animator 时建场工具会自动挂 PlayerAnimatorBridge。")]
        public ArtSlot[] characters = new ArtSlot[4]
        {
            new ArtSlot(), new ArtSlot(), new ArtSlot(), new ArtSlot()
        };

        [Tooltip("角色身高（米）。用于把模型自动缩放到与 CharacterController 一致。")]
        [Min(0.5f)] public float characterHeight = 1.5f;

        [Tooltip("落地阴影材质。留空则用纯黑半透明方片。")]
        public Material shadowMaterial;

        // ═══════════════════════════════════════════════
        [Header("═══ 家具地形（可跳上去的平台）═══")]

        [Tooltip("会议桌。注意场景里的桌子是 11×4 米的超大尺寸，\n" +
                 "建议勾掉 fitToSize 并用多张桌子拼接，否则单张会严重变形。")]
        public ArtSlot desk = new ArtSlot();

        [Tooltip("桌边椅子，作为上桌的踏板")]
        public ArtSlot chair = new ArtSlot();

        [Tooltip("两侧矮柜排")]
        public ArtSlot cabinet = new ArtSlot();

        [Tooltip("四角高台")]
        public ArtSlot highPlatform = new ArtSlot();

        [Tooltip("斜坡台阶")]
        public ArtSlot rampStep = new ArtSlot();

        [Tooltip("★平台顶面贴面。这层略亮的贴面是 2D 俯视下判断\n" +
                 "「这里能站」的视觉线索。换成美术模型后若顶面已经\n" +
                 "足够清晰，可勾掉下面的开关。")]
        public bool keepPlatformTopFace = true;

        // ═══════════════════════════════════════════════
        [Header("═══ 容器（可搜索）═══")]

        [Tooltip("所有容器的默认外观。未在下面单独指定的容器都用它。")]
        public ArtSlot containerDefault = new ArtSlot();

        [Tooltip("★按名字单独指定。key 要与建场代码里的容器名一致，\n" +
                 "例如「文件柜」「饮水机」「打印机」「垃圾桶」。")]
        public List<NamedArtSlot> containerOverrides = new List<NamedArtSlot>();

        // ═══════════════════════════════════════════════
        [Header("═══ 地板 ═══")]

        [Tooltip("地板砖。必须是 1×1 单位，建场时按格子尺寸缩放。")]
        public ArtSlot floorTile = new ArtSlot();

        public Material floorMatSolid;

        [Tooltip("开裂预警状态。留空则用代码染红。")]
        public Material floorMatCracking;

        // ═══════════════════════════════════════════════
        [Header("═══ 墙体与门 ═══")]

        public ArtSlot wallSegment = new ArtSlot();
        public ArtSlot whiteboard = new ArtSlot();
        public ArtSlot doorLeaf = new ArtSlot();

        // ═══════════════════════════════════════════════
        [Header("═══ 故障氛围道具 ═══")]

        [Tooltip("天花板碎片")]
        public ArtSlot debris = new ArtSlot();

        [Tooltip("★悬浮家具。BugAmbience 会让它上下浮动 + 青蓝闪烁，\n" +
                 "所以这里应该填「正常的家具模型」，不要填自带发光的。")]
        public ArtSlot floatingProp = new ArtSlot();

        [Tooltip("★天花板吊扇（房间正中央）。留空则用程序生成的四叶占位扇。\n\n" +
                 "注意：吊扇模型通常是「顶面对齐 y=0、向下延伸」建模的，\n" +
                 "建场时会挂到吊点下方并按直径缩放到约 2.2 米，\n" +
                 "不走常规的底面贴地对齐，所以 fitToSize 对它无效。\n" +
                 "Kenney 包里的 ceilingFan 可直接用。")]
        public ArtSlot ceilingFan = new ArtSlot();

        // ── 查询 ──────────────────────────────────────

        /// <summary>取容器外观。先查名字覆盖，再退到默认槽。</summary>
        public ArtSlot GetContainerArt(string containerName)
        {
            if (containerOverrides != null)
            {
                for (int i = 0; i < containerOverrides.Count; i++)
                {
                    var o = containerOverrides[i];
                    if (o != null && o.art != null
                        && !string.IsNullOrEmpty(o.key)
                        && o.key == containerName)
                        return o.art;
                }
            }
            return containerDefault;
        }

        /// <summary>取角色外观。索引越界或未配置时返回 null，调用方回退到占位体。</summary>
        public ArtSlot GetCharacterArt(int index)
        {
            if (characters == null || index < 0 || index >= characters.Length) return null;
            return characters[index];
        }
    }
}
