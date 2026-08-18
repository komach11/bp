using UnityEngine;

namespace BugParty.SearchRoom
{
    /// <summary>
    /// 控制器基类。真人和 AI 都继承它，PlayerActor 不关心自己被谁驱动。
    /// 这样"1 真人 + 3 AI"和"本地 4 人"可以随时互换。
    /// </summary>
    [RequireComponent(typeof(PlayerActor))]
    public abstract class PlayerBrain : MonoBehaviour
    {
        protected PlayerActor Actor { get; private set; }
        protected SearchRoomConfig Cfg { get; private set; }

        protected virtual void Awake()
        {
            Actor = GetComponent<PlayerActor>();
        }

        protected virtual void Start()
        {
            Cfg = SearchRoomManager.Instance != null ? SearchRoomManager.Instance.config : null;
        }

        protected virtual void Update()
        {
            var mgr = SearchRoomManager.Instance;
            if (mgr == null || !mgr.CanAct || !Actor.IsAlive || Actor.IsStaggered)
            {
                Actor.MoveInput = Vector2.zero;
                return;
            }
            Think();
        }

        /// <summary>每帧决策。子类实现。</summary>
        protected abstract void Think();
    }
}
