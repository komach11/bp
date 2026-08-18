using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 一套按键映射。支持本地四人同屏，每人一组键。
    /// </summary>
    [System.Serializable]
    public class InputScheme
    {
        public KeyCode up = KeyCode.W;
        public KeyCode down = KeyCode.S;
        public KeyCode left = KeyCode.A;
        public KeyCode right = KeyCode.D;

        [Tooltip("按住搜索，松手取消")]
        public KeyCode search = KeyCode.J;

        [Tooltip("按一下肘击")]
        public KeyCode elbow = KeyCode.K;

        public Vector2 ReadMove()
        {
            float x = 0f, y = 0f;
            if (Input.GetKey(left)) x -= 1f;
            if (Input.GetKey(right)) x += 1f;
            if (Input.GetKey(down)) y -= 1f;
            if (Input.GetKey(up)) y += 1f;
            return new Vector2(x, y);
        }

        // ── 四套预设，建场工具直接调用 ──────────────────────

        public static InputScheme Player1() => new InputScheme
        {
            up = KeyCode.W, down = KeyCode.S, left = KeyCode.A, right = KeyCode.D,
            search = KeyCode.J, elbow = KeyCode.K
        };

        public static InputScheme Player2() => new InputScheme
        {
            up = KeyCode.UpArrow, down = KeyCode.DownArrow,
            left = KeyCode.LeftArrow, right = KeyCode.RightArrow,
            search = KeyCode.Keypad1, elbow = KeyCode.Keypad2
        };

        public static InputScheme Player3() => new InputScheme
        {
            up = KeyCode.T, down = KeyCode.G, left = KeyCode.F, right = KeyCode.H,
            search = KeyCode.V, elbow = KeyCode.B
        };

        public static InputScheme Player4() => new InputScheme
        {
            up = KeyCode.I, down = KeyCode.K, left = KeyCode.J, right = KeyCode.L,
            search = KeyCode.N, elbow = KeyCode.M
        };
    }

    /// <summary>
    /// 真人控制器。键盘驱动。
    /// </summary>
    public class HumanBrain : PlayerBrain
    {
        [Header("按键")]
        public InputScheme keys = new InputScheme();

        [Header("摄像机相对操作")]
        [Tooltip("勾选后 WASD 按屏幕方向走，而不是世界坐标轴方向。俯视视角建议勾上")]
        public bool cameraRelative = true;

        Transform _cam;

        protected override void Start()
        {
            base.Start();
            if (Camera.main != null) _cam = Camera.main.transform;
        }

        protected override void Think()
        {
            // ── 移动 ──
            var raw = keys.ReadMove();
            Actor.MoveInput = cameraRelative ? ToCameraSpace(raw) : raw;

            // ── 搜索：按住持续，松手取消 ──
            if (Input.GetKeyDown(keys.search))
                Actor.Search.TryBegin();
            else if (Input.GetKeyUp(keys.search) && Actor.Search.IsSearching)
                Actor.Search.Cancel(false);

            // ── 肘击 ──
            if (Input.GetKeyDown(keys.elbow))
                Actor.Elbow.TryElbow();
        }

        /// <summary>把输入从屏幕空间转到世界空间，让俯视视角操作符合直觉。</summary>
        Vector2 ToCameraSpace(Vector2 raw)
        {
            if (_cam == null || raw.sqrMagnitude < 0.0001f) return raw;

            var fwd = _cam.forward; fwd.y = 0f;
            var right = _cam.right; right.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) return raw;

            fwd.Normalize();
            right.Normalize();

            var world = fwd * raw.y + right * raw.x;
            return new Vector2(world.x, world.z);
        }
    }
}
