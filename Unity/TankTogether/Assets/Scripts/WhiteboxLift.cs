using UnityEngine;

namespace TankTogether.M0
{
    // The host moves the physical platform; peers only display its snapshot position.
    [DefaultExecutionOrder(-20)]
    public sealed class WhiteboxLift : MonoBehaviour
    {
        public Rigidbody platform;
        public Collider platformCollider;
        public Collider switchCollider;
        [Tooltip("脚部附近与机关表面的最大交互距离，米。")][Min(.1f)] public float interactionRange=1.8f;
        public bool GoingUp {get;private set;}
        float nextInteractionAt;
        public Collider SwitchCollider {
            get {
                if(!switchCollider && platform)foreach(var c in platform.GetComponentsInChildren<Collider>(true))if(c.name=="Group 59"){switchCollider=c;break;}
                return switchCollider;
            }
        }
        public Vector3 bottomPosition;
        [Tooltip("设计文档：垂直上升速度，米/秒。")][Min(.01f)] public float speed = 2.5f;
        [Tooltip("设计文档：从底端到顶端的上升距离，米。")][Min(0)] public float travel = 101;
        [Tooltip("旧自动模式参数，当前F开关模式不使用；保留以兼容已有配置。")][Min(0)] public float delay = 2;
        public float Distance { get; private set; }
        public float WaitTime { get; private set; }
        public bool Moving { get; private set; }
        public bool AtTop => Distance >= travel - .001f;
        M0CarryLab lab;
        void Awake() { lab = GetComponent<M0CarryLab>(); }
        public void ResetLift(bool top)
        {
            Distance = top ? travel : 0; WaitTime = 0; Moving = false; GoingUp=top;nextInteractionAt=0;
            if (platform) { platform.position = bottomPosition + Vector3.up * Distance; platform.transform.position = platform.position; }
            Physics.SyncTransforms();
        }
        public bool NearSwitch(int slot) {
            var control=SwitchCollider;
            if(!lab||!control||slot<0||slot>=lab.Players.Length)return false;
            var actor=lab.Players[slot];if(actor==null||!actor.active||!actor.body)return false;
            var origin=actor.body.position+Vector3.up*Mathf.Min(.8f,actor.height*.5f);
            var target=control.bounds.ClosestPoint(origin);var delta=target-origin;
            if(delta.sqrMagnitude>interactionRange*interactionRange)return false;
            foreach(var hit in Physics.RaycastAll(origin,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider!=control&&hit.collider.attachedRigidbody!=actor.body&&lab.IsEnvironment(hit.collider))return false;
            return true;
        }
        public bool TryInteract(int slot,out string feedback,bool grabbing=false) {
            feedback="请靠近白色升降机关";
            if(!lab||!platform||!lab.Simulate||lab.SessionPaused||lab.Broken||!NearSwitch(slot))return false;
            var actor=lab.Players[slot];
            if(grabbing||actor.held||actor.joint||actor.remoteGrip){feedback="请先松开抓取，再按交互键";return false;}
            // One toggle per press. Coalesce simultaneous players rather than reversing twice in the same step.
            if(Time.time<nextInteractionAt){feedback="机关刚刚切换";return false;}
            nextInteractionAt=Time.time+.25f;GoingUp=!GoingUp;Moving=true;WaitTime=0;
            feedback=GoingUp?"升降台上升":"升降台下降";return true;
        }
        void OnGUI() {
            if(!lab||lab.GMOpen||lab.Broken||!NearSwitch(lab.Selected))return;
            var session=GetComponent<TankTogether.Networking.SteamCarrySession>();
            if(session&&session.MenuOverlayVisible)return;
            var actor=lab.Players[lab.Selected];
            string text=actor.held||actor.joint||actor.remoteGrip?"请松开抓取后操作机关":
                TankTogether.Networking.PlayerPreferences.Binding(TankTogether.Networking.PlayerAction.Interact)+" · "+(GoingUp?"切换下降":"切换上升");
            var old=GUI.matrix;float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
            GUI.Box(new Rect(430,540,420,42),text);GUI.matrix=old;
        }
        void FixedUpdate()
        {
            if (!lab || !platform || !lab.Simulate || lab.SessionPaused || lab.Broken) return;
            if (!Moving) return;
            float destination=GoingUp?travel:0;
            Distance = Mathf.MoveTowards(Distance,destination,speed*Time.fixedDeltaTime);
            if (Mathf.Abs(Distance-destination)<.001f) { Distance=destination;Moving=false; }
            platform.MovePosition(bottomPosition + Vector3.up * Distance);
        }
        public void ApplyProxy(float distance, bool moving, bool goingUp=true)
        {
            Distance = Mathf.Clamp(distance, 0, travel); Moving = moving; GoingUp=goingUp;
            if (platform) platform.position = bottomPosition + Vector3.up * Distance;
        }
    }
}
