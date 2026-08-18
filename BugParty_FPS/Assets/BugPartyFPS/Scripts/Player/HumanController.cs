using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 本地真人玩家的输入。塔科夫 / 三角洲式键位。
    /// </summary>
    public class HumanController : MonoBehaviour
    {
        [Header("移动")]
        public KeyCode forward = KeyCode.W;
        public KeyCode back = KeyCode.S;
        public KeyCode left = KeyCode.A;
        public KeyCode right = KeyCode.D;
        public KeyCode sprint = KeyCode.LeftShift;
        public KeyCode crouch = KeyCode.LeftControl;
        public KeyCode jump = KeyCode.Space;

        [Header("交互")]
        [Tooltip("搜刮 / 打开容器")]
        public KeyCode interact = KeyCode.F;

        [Tooltip("关闭搜刮界面")]
        public KeyCode closePanel = KeyCode.Tab;

        [Tooltip("一键拿走所有装得下的")]
        public KeyCode takeAll = KeyCode.G;

        [Header("战斗")]
        [Tooltip("近战。也支持鼠标左键")]
        public KeyCode melee = KeyCode.V;

        public bool allowMouseMelee = true;

        PlayerRig _rig;

        void Awake()
        {
            _rig = GetComponent<PlayerRig>();
        }

        void Update()
        {
            var mgr = RaidManager.Instance;
            if (mgr == null || !_rig.IsAlive)
            {
                _rig.MoveInput = Vector2.zero;
                return;
            }

            // 结算阶段之后不再接受操作
            if (mgr.Phase == RoundPhase.Settlement || mgr.Phase == RoundPhase.Finished)
            {
                _rig.MoveInput = Vector2.zero;
                _rig.WantSprint = _rig.WantCrouch = false;
                return;
            }

            ReadMovement();
            ReadInteraction();
            ReadCombat();
        }

        void ReadMovement()
        {
            float x = 0f, y = 0f;
            if (Input.GetKey(left)) x -= 1f;
            if (Input.GetKey(right)) x += 1f;
            if (Input.GetKey(back)) y -= 1f;
            if (Input.GetKey(forward)) y += 1f;

            _rig.MoveInput = new Vector2(x, y);
            _rig.WantSprint = Input.GetKey(sprint);
            _rig.WantCrouch = Input.GetKey(crouch);
            if (Input.GetKeyDown(jump)) _rig.WantJump = true;
        }

        void ReadInteraction()
        {
            var loot = _rig.Loot;
            if (loot == null) return;

            // 搜刮界面开着时：数字键取物、Tab 关闭、G 全取
            if (loot.IsPanelOpen)
            {
                if (Input.GetKeyDown(closePanel) || Input.GetKeyDown(interact))
                {
                    loot.ClosePanel();
                    return;
                }

                if (Input.GetKeyDown(takeAll))
                {
                    loot.TakeAllPossible();
                    return;
                }

                // 数字键 1~6 对应容器里的第 N 件
                for (int i = 0; i < 6; i++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                    {
                        loot.TakeLoot(i);
                        break;
                    }
                }
                return;
            }

            // 界面没开：按 F 开始搜索
            if (Input.GetKeyDown(interact))
                loot.TryBeginSearch();

            // 松开 F 或走开会自动中断，这里额外支持主动取消
            if (loot.IsSearching && Input.GetKeyDown(closePanel))
                loot.CancelSearch(false);
        }

        void ReadCombat()
        {
            // 搜刮界面开着时不能挥击（要先关界面），避免误操作
            if (_rig.Loot != null && _rig.Loot.IsPanelOpen) return;

            bool pressed = Input.GetKeyDown(melee)
                        || (allowMouseMelee && Input.GetMouseButtonDown(0));

            if (pressed && _rig.Melee != null)
                _rig.Melee.TrySwing();
        }
    }
}
