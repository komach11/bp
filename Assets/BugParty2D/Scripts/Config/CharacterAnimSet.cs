using UnityEngine;

namespace BugParty.TopDown2D
{
    /// <summary>
    /// 角色动作映射表 —— 把「玩法状态」和「具体动画 clip」解耦。
    ///
    /// 【为什么需要它】
    /// 之前 clip 是在生成工具里按名字硬编码挑的（Idle_A / Run_Forward / Fight_Punch_Right …）。
    /// 换一套素材、或者想把肘击换成另一个挥拳动作，都得改代码重新编译。
    /// 现在这些槽位全部暴露在 Inspector 里：拖一个 clip 进去，重新生成 Controller 即可。
    ///
    /// 【用法】
    /// 1. 首次执行「BugParty2D ▸ 接入 SCP 角色」时会自动创建并按名字填好默认 clip
    /// 2. 之后想换动作，直接在这个资产上换 clip，再执行「BugParty2D ▸ 重建角色动画」
    /// 3. 留空的槽位会被跳过，Controller 里不生成对应状态 —— 玩法不会因此报错
    ///
    /// 【槽位与 PlayerAnimatorBridge 的对应】
    ///   idle / walk / run  →  Speed（Blend Tree 三档）
    ///   search             →  Searching = true
    ///   elbow              →  Trigger Elbow
    ///   jump / fall        →  Trigger Jump / Grounded = false
    ///   getHit / stagger   →  Trigger GetHit / Staggered = true
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterAnimSet", menuName = "BugParty2D/角色动作表", order = 30)]
    public class CharacterAnimSet : ScriptableObject
    {
        [Header("═══ 核心四动作 ═══")]
        [Tooltip("闲置。站着不动时播，也是 Blend Tree 的 Speed=0 档")]
        public AnimationClip idle;

        [Tooltip("搜索。翻箱倒柜的动作。\nSCP 素材里没有专门的翻找动作，默认用 Fight_Idle（半蹲戒备），\n配合 PlayerActionFx 的程序化弯腰叠加")]
        public AnimationClip search;

        [Tooltip("跑步。Blend Tree 的高速档")]
        public AnimationClip run;

        [Tooltip("肘击。挥出去的那一下，不论是否命中都播")]
        public AnimationClip elbow;

        [Header("═══ 移动补充 ═══")]
        [Tooltip("走路。Blend Tree 的中速档。留空则从 idle 直接过渡到 run")]
        public AnimationClip walk;

        [Header("═══ 空中与受击 ═══")]
        [Tooltip("起跳瞬间")]
        public AnimationClip jump;

        [Tooltip("下落。踩空掉洞也复用这个")]
        public AnimationClip fall;

        [Tooltip("被肘击命中的瞬间反应")]
        public AnimationClip getHit;

        [Tooltip("硬直。被击退后的僵直姿态，比 getHit 持续更久")]
        public AnimationClip stagger;

        [Header("═══ Blend Tree 阈值 ═══")]
        [Tooltip("走路动画对应的 Speed 值。\n★注意这是归一化值（0~1），不是 m/s ——\nPlayerAnimatorBridge 的 speedNormalizeMax 默认 6，\n会把 moveSpeed 5.5 压成 0.92 再喂给 Animator")]
        [Range(0f, 1f)] public float walkThreshold = 0.35f;

        [Tooltip("跑步动画对应的 Speed 值（同为归一化值）")]
        [Range(0f, 1f)] public float runThreshold = 0.85f;

        [Header("═══ 过渡时长（秒）═══")]
        [Tooltip("进入肘击的过渡。打击动作要快，太长会显得软")]
        [Range(0f, 0.3f)] public float elbowBlendIn = 0.04f;

        [Tooltip("肘击播到多少比例时切回移动。1 = 播完整段")]
        [Range(0.3f, 1f)] public float elbowExitTime = 0.8f;

        [Tooltip("进入/退出搜索的过渡")]
        [Range(0f, 0.4f)] public float searchBlend = 0.12f;

        /// <summary>至少有一个 clip 才值得生成 Controller。</summary>
        public bool HasAny =>
            idle != null || walk != null || run != null || search != null ||
            elbow != null || jump != null || fall != null ||
            getHit != null || stagger != null;

        /// <summary>已填槽位数，用于工具里给出提示。</summary>
        public int FilledCount
        {
            get
            {
                int n = 0;
                if (idle != null) n++;
                if (walk != null) n++;
                if (run != null) n++;
                if (search != null) n++;
                if (elbow != null) n++;
                if (jump != null) n++;
                if (fall != null) n++;
                if (getHit != null) n++;
                if (stagger != null) n++;
                return n;
            }
        }
    }
}
