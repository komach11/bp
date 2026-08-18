using UnityEngine;

namespace BugParty.FPS
{
    /// <summary>
    /// 第一人称视角控制。鼠标转向、头部摆动、受击晃动、疾跑 FOV。
    /// 只挂在本地真人玩家身上。
    ///
    /// 分工：水平旋转（yaw）作用于 PlayerRig 本体，使移动方向跟随视线；
    ///       垂直旋转（pitch）只作用于相机，避免身体前后倾倒。
    /// </summary>
    public class FirstPersonLook : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("相机 Transform，通常是 eyeAnchor 的子物体")]
        public Transform cameraTransform;

        [Tooltip("相机组件，用于 FOV 调整")]
        public Camera cam;

        [Header("光标")]
        [Tooltip("是否自动锁定并隐藏鼠标光标")]
        public bool lockCursor = true;

        PlayerRig _rig;
        RaidConfig _cfg;

        float _yaw;
        float _pitch;

        // 头部摆动
        float _bobTimer;
        Vector3 _bobOffset;

        // 受击晃动
        float _shakeAmount;
        Vector3 _shakeOffset;

        float _baseFov;
        Vector3 _camBaseLocalPos;

        void Awake()
        {
            _rig = GetComponent<PlayerRig>();
        }

        void Start()
        {
            _cfg = RaidManager.Instance != null ? RaidManager.Instance.config : null;

            _yaw = transform.eulerAngles.y;
            _pitch = 0f;

            if (cameraTransform != null)
                _camBaseLocalPos = cameraTransform.localPosition;

            if (cam != null && _cfg != null)
            {
                cam.fieldOfView = _cfg.fieldOfView;
                _baseFov = _cfg.fieldOfView;
            }

            SetCursor(lockCursor);
        }

        void OnDestroy()
        {
            SetCursor(false);
        }

        void SetCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            if (_cfg == null) return;

            // Esc 解锁光标，方便在编辑器里退出
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                lockCursor = !lockCursor;
                SetCursor(lockCursor);
            }

            UpdateLook();
            UpdateBob();
            UpdateShake();
            UpdateFov();
            ApplyCameraOffset();
        }

        void UpdateLook()
        {
            var mgr = RaidManager.Instance;
            bool canLook = mgr != null && mgr.Phase != RoundPhase.Intro && _rig.IsAlive;

            // 搜刮界面开着时仍允许转头——你需要能回头看有没有人来
            if (!canLook || !lockCursor) return;

            float mx = Input.GetAxisRaw("Mouse X") * _cfg.mouseSensitivity;
            float my = Input.GetAxisRaw("Mouse Y") * _cfg.mouseSensitivity;

            // 硬直期间视角操控被削弱，制造「被打晕」的感觉
            if (_rig.IsStaggered) { mx *= 0.25f; my *= 0.25f; }

            _yaw += mx;
            _pitch -= my;
            _pitch = Mathf.Clamp(_pitch, -_cfg.pitchClamp, _cfg.pitchClamp);

            // 身体只转 yaw
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);

            // 相机负责 pitch
            if (cameraTransform != null)
                cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void UpdateBob()
        {
            if (cameraTransform == null) return;

            float speed = _rig.HorizontalSpeed;
            bool moving = speed > 0.4f && _rig.IsGrounded;

            if (moving)
            {
                float mult = _rig.CurrentStance == Stance.Sprint ? _cfg.sprintBobMultiplier
                           : _rig.CurrentStance == Stance.Crouch ? 0.5f : 1f;

                _bobTimer += Time.deltaTime * _cfg.bobFrequency * mult;

                float amp = _cfg.bobAmplitude * mult;
                // 竖直摆动频率是水平的两倍，形成 8 字轨迹，更像真实步态
                _bobOffset = new Vector3(
                    Mathf.Cos(_bobTimer) * amp * 0.6f,
                    Mathf.Sin(_bobTimer * 2f) * amp,
                    0f);
            }
            else
            {
                _bobTimer = 0f;
                _bobOffset = Vector3.Lerp(_bobOffset, Vector3.zero, Time.deltaTime * 8f);
            }
        }

        void UpdateShake()
        {
            if (_shakeAmount <= 0.01f)
            {
                _shakeAmount = 0f;
                _shakeOffset = Vector3.Lerp(_shakeOffset, Vector3.zero, Time.deltaTime * 10f);
                return;
            }

            _shakeOffset = new Vector3(
                Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * _shakeAmount * 0.01f;

            _shakeAmount = Mathf.Lerp(_shakeAmount, 0f, Time.deltaTime * 6f);
        }

        void UpdateFov()
        {
            if (cam == null) return;

            float target = _baseFov;
            if (_rig.CurrentStance == Stance.Sprint && _rig.HorizontalSpeed > 1f)
                target += _cfg.sprintFovBoost;

            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, Time.deltaTime * 6f);
        }

        void ApplyCameraOffset()
        {
            if (cameraTransform == null) return;
            cameraTransform.localPosition = _camBaseLocalPos + _bobOffset + _shakeOffset;
        }

        /// <summary>外部调用：添加一次受击晃动。</summary>
        public void AddShake(float amount)
        {
            _shakeAmount = Mathf.Max(_shakeAmount, amount);
        }

        /// <summary>重置视角，用于回合重开。</summary>
        public void ResetLook()
        {
            _yaw = transform.eulerAngles.y;
            _pitch = 0f;
            _shakeAmount = 0f;
            _bobTimer = 0f;
            _bobOffset = Vector3.zero;
            _shakeOffset = Vector3.zero;
        }
    }
}
