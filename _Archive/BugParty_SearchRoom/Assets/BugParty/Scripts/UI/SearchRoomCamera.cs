using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 固定斜俯视相机。可选自动取景，让四个人始终都在画面里。
    /// 对应 PV 分镜里"斜俯视广角，房间全貌可读"的机位要求。
    /// </summary>
    public class SearchRoomCamera : MonoBehaviour
    {
        [Header("固定机位")]
        [Tooltip("俯视角度")]
        [Range(20f, 89f)] public float pitch = 52f;

        [Tooltip("绕 Y 轴的水平角度")]
        public float yaw = 0f;

        [Tooltip("相机到房间中心的距离")]
        [Min(1f)] public float distance = 13.5f;

        [Tooltip("注视点，通常是房间中心")]
        public Transform lookTarget;

        [Header("自动取景")]
        [Tooltip("勾选后相机会自动拉远，保证所有玩家都在画面内")]
        public bool autoFrame = true;

        [Tooltip("自动取景时的最小距离")]
        [Min(1f)] public float minDistance = 11f;

        [Tooltip("自动取景时的最大距离")]
        [Min(1f)] public float maxDistance = 18f;

        [Tooltip("取景边缘留白系数，越大越松")]
        [Range(1f, 2.5f)] public float framePadding = 1.45f;

        [Tooltip("镜头移动的平滑度")]
        [Min(0.01f)] public float smoothTime = 0.35f;

        Vector3 _pivot;
        Vector3 _pivotVel;
        float _dist;
        float _distVel;

        void Start()
        {
            _pivot = lookTarget != null ? lookTarget.position : Vector3.zero;
            _dist = distance;
            ApplyTransform(_pivot, _dist);
        }

        void LateUpdate()
        {
            Vector3 targetPivot = lookTarget != null ? lookTarget.position : Vector3.zero;
            float targetDist = distance;

            if (autoFrame)
            {
                var mgr = SearchRoomManager.Instance;
                if (mgr != null && mgr.players.Count > 0)
                {
                    var min = new Vector3(float.MaxValue, 0f, float.MaxValue);
                    var max = new Vector3(float.MinValue, 0f, float.MinValue);
                    int n = 0;

                    for (int i = 0; i < mgr.players.Count; i++)
                    {
                        var p = mgr.players[i];
                        if (p == null || !p.IsAlive) continue;
                        var pos = p.transform.position;
                        min.x = Mathf.Min(min.x, pos.x); min.z = Mathf.Min(min.z, pos.z);
                        max.x = Mathf.Max(max.x, pos.x); max.z = Mathf.Max(max.z, pos.z);
                        n++;
                    }

                    if (n > 0)
                    {
                        // 取景中心与房间中心做混合，避免镜头被单个跑远的人拽走
                        var crowdCenter = new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);
                        targetPivot = Vector3.Lerp(targetPivot, crowdCenter, 0.55f);

                        float spread = Mathf.Max(max.x - min.x, max.z - min.z);
                        targetDist = Mathf.Clamp(spread * framePadding, minDistance, maxDistance);
                    }
                }
            }

            _pivot = Vector3.SmoothDamp(_pivot, targetPivot, ref _pivotVel, smoothTime);
            _dist = Mathf.SmoothDamp(_dist, targetDist, ref _distVel, smoothTime);

            ApplyTransform(_pivot, _dist);
        }

        void ApplyTransform(Vector3 pivot, float dist)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = pivot - rot * Vector3.forward * dist;
            transform.rotation = rot;
        }
    }
}
