using UnityEngine;
namespace TankTogether.M0
{
    public enum CarryMotionState { Locomotion, Takeoff, Rise, Apex, Fall, Land }
    // Reads gameplay facts; never writes rigidbodies, input, save data, or network state.
    public sealed class CarryAnimatorDriver
    {
        readonly Animator animator;
        readonly M0CarryLab lab;
        readonly M0CarryLab.Actor actor;
        Vector3 previous;
        bool initialized, wasGrounded;
        float age, vertical, lastDownward;
        public CarryMotionState State { get; private set; }
        public float Impact { get; private set; }
        public Animator Animator => animator;
        public CarryAnimatorDriver(Transform root,M0CarryLab owner,M0CarryLab.Actor target)
        {
            lab=owner;actor=target;previous=target.body.transform.position;
            animator=root.GetComponent<Animator>();
            if(!animator)animator=root.gameObject.AddComponent<Animator>();
            var profile=root.GetComponent<CarryVisualProfile>();
            animator.runtimeAnimatorController=profile&&profile.controller?profile.controller:Resources.Load<RuntimeAnimatorController>("CharacterAnimation/CarryCharacter");
            animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            animator.enabled=animator.runtimeAnimatorController!=null;
            if(!animator.enabled)Debug.LogWarning("Character Animator Controller not generated yet; keeping legacy presentation. Run Tank Together/Animation/Rebuild Character Clips.");
        }
        public void Tick(float dt)
        {
            if(!animator || !animator.enabled || !actor.body || dt<=0)return;
            bool paused=lab.SessionPaused||lab.GMOpen||lab.Broken;
            animator.speed=paused?0:1;
            var position=actor.body.transform.position;
            var displacement=position-previous;previous=position;
            if(paused)return;
            bool teleport=displacement.sqrMagnitude>4;
            var velocity=lab.Simulate?actor.body.linearVelocity-actor.groundVelocity:displacement/dt;
            // On peers smooth interpolation noise; do not delay the authoritative local jump.
            vertical=lab.Simulate?velocity.y:Mathf.Lerp(vertical,velocity.y,1-Mathf.Exp(-dt*20));
            bool grounded=actor.grounded;
            if(!lab.Simulate)
            {
                grounded=false;
                foreach(var h in Physics.RaycastAll(position+Vector3.up*.1f,Vector3.down,.2f,~0,QueryTriggerInteraction.Ignore))
                    if(lab.IsEnvironment(h.collider)&&h.normal.y>.55f){grounded=true;break;}
            }
            if(vertical>.5f)grounded=false;
            float speed=Vector3.ProjectOnPlane(velocity,Vector3.up).magnitude;
            if(!initialized||teleport)
            {
                initialized=true;wasGrounded=grounded;lastDownward=0;age=0;
                State=grounded?CarryMotionState.Locomotion:vertical>.7f?CarryMotionState.Rise:CarryMotionState.Fall;
                animator.Play(State.ToString(),0,0);
            }
            age+=dt;
            var next=State;
            if(grounded)
            {
                if(!wasGrounded){Impact=Mathf.InverseLerp(2,9,lastDownward);next=CarryMotionState.Land;}
                else if(State!=CarryMotionState.Land||age>Mathf.Lerp(.14f,.24f,Impact)||speed>.3f&&age>.055f)next=CarryMotionState.Locomotion;
                lastDownward=0;
            }
            else
            {
                lastDownward=Mathf.Max(lastDownward,-vertical);
                if(wasGrounded)next=vertical>.7f?CarryMotionState.Takeoff:CarryMotionState.Fall;
                else if(State==CarryMotionState.Takeoff&&age<.075f&&vertical>0)next=CarryMotionState.Takeoff;
                else next=vertical>.7f?CarryMotionState.Rise:vertical<-.7f?CarryMotionState.Fall:CarryMotionState.Apex;
            }
            if(next!=State){State=next;age=0;}
            wasGrounded=grounded;
            animator.SetInteger("MotionState",(int)State);
            animator.SetFloat("Speed",Mathf.Min(speed,8),.12f,dt);
            animator.SetFloat("Impact",Impact);
            float nominal=Mathf.Lerp(4,6,Mathf.InverseLerp(4,6,speed));
            animator.SetFloat("StrideRate",speed<.05f?1:Mathf.Clamp(speed/nominal,.25f,1.5f));
        }
    }
}

