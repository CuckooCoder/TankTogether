using System.Collections.Generic;
using UnityEngine;

namespace TankTogether.M0
{
    // Visual-only secondary motion, contact IK and legacy fallback. Never writes physics.
    public sealed class CarryProceduralPose
    {
        readonly Transform root;
        readonly CarryPlayfulMotion playful;
        readonly CarryMotionStyle carryStyle=Resources.Load<CarryMotionStyle>("CharacterAnimation/MotionStyle");
        public CarryPlayfulMotion Playful => playful;
        readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        readonly Dictionary<Transform,Vector3> shoulderOffsets=new Dictionary<Transform,Vector3>();
        readonly Dictionary<string, Quaternion> rest = new Dictionary<string, Quaternion>();
        float phase, motion, air, grip, landing;
        float lean, leanVelocity, turnLag, previousYaw;
        Vector3 pelvisRest, leftFootRest, rightFootRest;
        bool hasYaw;
        bool wasGrounded = true;
        Vector2 carryLean;
        Vector3 lastRoot;
        readonly FootContact[] contacts={new FootContact(),new FootContact()};
        sealed class FootContact { public Transform surface; public Vector3 localPoint; public float weight; public bool planted, exhausted; }
        public float ContactWeight {get;private set;}
        public Vector2 CarryLean => carryLean;

        public CarryProceduralPose(Transform visual)
        {
            root = visual;lastRoot=visual.position;playful=new CarryPlayfulMotion(visual);
            foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var bone in renderer.bones)
                    if (bone && !bones.ContainsKey(bone.name)) { bones.Add(bone.name, bone); rest.Add(bone.name, bone.localRotation); }
            if(bones.ContainsKey("Pelvis")) pelvisRest=bones["Pelvis"].localPosition;
            if(bones.ContainsKey("Foot.L")) leftFootRest=root.InverseTransformPoint(bones["Foot.L"].position);
            if(bones.ContainsKey("Foot.R")) rightFootRest=root.InverseTransformPoint(bones["Foot.R"].position);
        }
        void Rotate(string name, float degrees)
        {
            Transform b;
            if (bones.TryGetValue(name, out b)) b.rotation = Quaternion.AngleAxis(degrees, root.right) * b.rotation;
        }
        void RotateAxis(string name, Vector3 axis, float degrees)
        {
            Transform b;
            if (bones.TryGetValue(name, out b)) b.rotation=Quaternion.AngleAxis(degrees,axis)*b.rotation;
        }
        // Animator supplies base poses; contact, balance and grasp are applied in this order.
        public void ApplyGrip(M0CarryLab lab,M0CarryLab.Actor actor,float dt)
        {
            grip=Mathf.Lerp(grip,actor.joint||actor.remoteGrip?1:0,1-Mathf.Exp(-dt*18));
            // Small inertial response on top of authored clips, never on the physics root.
            float yaw=root.eulerAngles.y;
            float turn=hasYaw?Mathf.DeltaAngle(previousYaw,yaw)/Mathf.Max(dt,.001f):0;
            previousYaw=yaw;hasYaw=true;
            turnLag=Mathf.Lerp(turnLag,Mathf.Clamp(-turn*.025f,-12,12),1-Mathf.Exp(-dt*10));
            float speed=Vector3.ProjectOnPlane(actor.body.linearVelocity-actor.groundVelocity,Vector3.up).magnitude;
            float leanTarget=lab.Simulate&&actor.grounded?Mathf.Min(speed,6)*.6f:0;
            float remaining=Mathf.Min(dt,.1f);
            while(remaining>0){float h=Mathf.Min(remaining,1f/120);remaining-=h;leanVelocity+=((leanTarget-lean)*150-leanVelocity*12)*h;lean=Mathf.Clamp(lean+leanVelocity*h,-3,5);}
            float freedom=1-grip*.8f;
            Rotate("Chest",lean*freedom);Rotate("Head",-lean*.4f*freedom);
            RotateAxis("Chest",root.up,turnLag*.5f*freedom);
            RotateAxis("Head",root.up,turnLag*.2f*freedom);
            var displacement=root.position-lastRoot;lastRoot=root.position;
            bool teleported=displacement.sqrMagnitude>4;
            if(teleported){carryLean=Vector2.zero;foreach(var f in contacts){f.planted=false;f.weight=0;}}
            playful.Apply(actor,root.GetComponent<Animator>(),grip,dt,teleported);
            GroundFeet(lab,actor,dt,teleported);
            if(grip<.001f||!lab.Tank){KeepShortHandsClear(actor);carryLean=Vector2.Lerp(carryLean,Vector2.zero,1-Mathf.Exp(-dt*12));return;}
            var point=actor.joint?actor.anchor:lab.Tank.transform.InverseTransformPoint(actor.body.transform.position+Vector3.up*actor.height);
            point.x=Mathf.Clamp(point.x,-lab.tankSize.x*.5f,lab.tankSize.x*.5f);
            point.z=Mathf.Clamp(point.z,-lab.tankSize.z*.5f,lab.tankSize.z*.5f);point.y=-lab.tankSize.y*.5f;
            var target=lab.Tank.transform.TransformPoint(point);
            // Counterbalance anchor stretch and tank tilt, with bounded angles.
            var stretch=root.InverseTransformDirection(target-(actor.body.position+Vector3.up*actor.height));
            var tilt=root.InverseTransformDirection(lab.Tank.transform.up);
            var desired=new Vector2(Mathf.Clamp(-stretch.z*22-tilt.z*8,-9,9),Mathf.Clamp(stretch.x*22+tilt.x*8,-9,9))*grip;
            carryLean=Vector2.Lerp(carryLean,desired,1-Mathf.Exp(-dt*9));
            Rotate("Chest",carryLean.x);RotateAxis("Chest",root.forward,carryLean.y);
            Rotate("Head",-carryLean.x*.35f);RotateAxis("Head",root.forward,-carryLean.y*.35f);
            // Hands are solved last so torso reaction does not pull them away from the tank.
            CarryArms(lab,target,grip);
        }
        void GroundFeet(M0CarryLab lab,M0CarryLab.Actor actor,float dt,bool teleport)
        {
            var animator=root.GetComponent<Animator>();
            if(!animator)return;
            var state=animator.GetCurrentAnimatorStateInfo(0);
            float cycle=Mathf.Repeat(state.normalizedTime,1);
            float speed=animator.GetFloat("Speed");
            // Never pin a foot through takeoff, a fall, or a controller blend.
            bool onGround=actor.grounded&&actor.body.linearVelocity.y-actor.groundVelocity.y<.5f;
            bool locomotion=state.IsName("Locomotion")&&!animator.IsInTransition(0);
            ContactWeight=0;
            for(int i=0;i<2;i++)
            {
                string side=i==0?"L":"R";var contact=contacts[i];
                if(!bones.TryGetValue("Foot."+side,out var foot))continue;
                bool stance=speed<.15f||Mathf.Sin((cycle+i*.5f)*Mathf.PI*2)>=0;
                if(!stance||speed<.15f)contact.exhausted=false;
                bool active=onGround&&locomotion&&stance&&!teleport&&!contact.exhausted;
                var original=foot.position;var rotation=foot.rotation;
                RaycastHit best=new RaycastHit();bool found=false;
                foreach(var hit in Physics.RaycastAll(original+Vector3.up*.25f,Vector3.down,.55f,~0,QueryTriggerInteraction.Ignore))
                    if(lab.IsEnvironment(hit.collider)&&hit.normal.y>.65f&&(!found||hit.distance<best.distance)){best=hit;found=true;}
                active &= found;
                if(active)
                {
                    float sole=Mathf.Max(.025f,(i==0?leftFootRest.y:rightFootRest.y)*root.lossyScale.y);
                    var target=best.point+Vector3.up*sole;
                    if(!contact.planted||contact.surface!=best.collider.transform)
                    {contact.surface=best.collider.transform;contact.localPoint=contact.surface.InverseTransformPoint(target);contact.planted=true;}
                    var planted=contact.surface.TransformPoint(contact.localPoint);
                    // Short legs have limited reach: release smoothly rather than stretch or snap.
                    float drift=Vector3.ProjectOnPlane(planted-original,Vector3.up).magnitude;
                    if(drift>.10f){contact.planted=false;contact.exhausted=true;active=false;}
                    else target=planted;
                    contact.weight=Mathf.MoveTowards(contact.weight,active?1:0,dt*18);
                    var upper=bones["Thigh."+side];var lower=bones["Shin."+side];
                    target=original+Vector3.ClampMagnitude(target-original,.12f);
                    Solve(upper,lower,foot,target,contact.weight);
                    var tilt=Quaternion.FromToRotation(Vector3.up,best.normal);
                    foot.rotation=Quaternion.Slerp(rotation,tilt*rotation,contact.weight*.75f);
                }
                else {contact.planted=false;contact.weight=Mathf.MoveTowards(contact.weight,0,dt*20);}
                ContactWeight=Mathf.Max(ContactWeight,contact.weight);
            }
        }
        public void Tick(M0CarryLab lab, M0CarryLab.Actor actor, Vector3 delta, float dt)
        {
            if (dt <= 0) return;
            foreach (var pair in bones) pair.Value.localRotation = rest[pair.Key];
            bool teleport = delta.sqrMagnitude > 4;
            float speed = teleport ? 0 : delta.magnitude / dt;
            if (lab.Simulate) speed = Vector3.ProjectOnPlane(actor.body.linearVelocity-actor.groundVelocity, Vector3.up).magnitude;
            bool grounded = actor.grounded;
            if (!lab.Simulate)
            {
                grounded = false;
                foreach (var h in Physics.RaycastAll(actor.body.transform.position+Vector3.up*.08f,Vector3.down,.18f,~0,QueryTriggerInteraction.Ignore))
                    if (lab.IsEnvironment(h.collider) && h.normal.y>.55f) { grounded=true; break; }
            }
            if (grounded && !wasGrounded && !teleport) landing=.14f;
            wasGrounded=grounded;
            landing=Mathf.MoveTowards(landing,0,dt);
            float blend=1-Mathf.Exp(-dt*14);
            motion=Mathf.Lerp(motion,grounded?Mathf.Clamp01(speed/4):0,blend);
            air=Mathf.Lerp(air,grounded?0:1,blend);
            grip=Mathf.Lerp(grip,actor.joint || actor.remoteGrip ? 1:0,blend);
            // Bounded spring motion belongs to the skeleton only, never the controller.
            float yaw=root.eulerAngles.y;
            float turn=hasYaw && !teleport ? Mathf.DeltaAngle(previousYaw,yaw)/Mathf.Max(dt,.001f) : 0;
            previousYaw=yaw; hasYaw=true;
            turnLag=Mathf.Lerp(turnLag,Mathf.Clamp(-turn*.055f,-22,22),1-Mathf.Exp(-dt*9));
            if(teleport) { lean=0;leanVelocity=0;turnLag=0; }
            float remaining=Mathf.Min(dt,.1f);
            while(remaining>0)
            {
                float h=Mathf.Min(remaining,1f/120);remaining-=h;
                float target=grounded ? Mathf.Min(speed,6)*1.5f : 0;
                leanVelocity+=((target-lean)*170-leanVelocity*11)*h;
                lean=Mathf.Clamp(lean+leanVelocity*h,-6,14);
            }
            phase+=Mathf.Min(speed,8)*dt*5.5f;
            float step=Mathf.Sin(phase)*24*motion;
            Rotate("Thigh.L", step-18*air); Rotate("Thigh.R", -step-18*air);
            Rotate("Shin.L", Mathf.Max(0,-step)*.65f+35*air);
            Rotate("Shin.R", Mathf.Max(0,step)*.65f+35*air);
            float freedom=(1-grip*.8f)*(1-Mathf.Clamp01((lab.standingHeight-actor.height)/.6f)*.6f);
            float wobble=Mathf.Sin(phase)*3*motion*freedom;
            if(bones.ContainsKey("Pelvis"))
            {
                var pelvis=bones["Pelvis"];pelvis.localPosition=pelvisRest;
                pelvis.position+=root.up*(-.025f*motion+.012f*Mathf.Abs(Mathf.Sin(phase))*motion-landing*.10f)*freedom;
            }
            RotateAxis("Pelvis",root.forward,wobble+turnLag*.12f*freedom);
            RotateAxis("Chest",Vector3.up,turnLag*.45f*freedom);
            RotateAxis("Head",root.forward,-wobble*.15f);
            RotateAxis("Head",Vector3.up,turnLag*.15f*freedom);
            Rotate("Chest", lean*freedom+landing*55);
            Rotate("Head", -lean*.45f*freedom-landing*25);
            Rotate("UpperArm.L", -step*.35f*(1-grip)-12*air*(1-grip));
            Rotate("UpperArm.R", step*.35f*(1-grip)-12*air*(1-grip));
            RotateAxis("UpperArm.L",root.forward,-12*(1-grip));
            RotateAxis("UpperArm.R",root.forward,12*(1-grip));
            if(grounded && motion>.01f)
            {
                StepFoot("L",phase,leftFootRest,motion);
                StepFoot("R",phase+Mathf.PI,rightFootRest,motion);
            }
            if (grip>.001f && lab.Tank)
            {
                var local=actor.joint ? actor.anchor : lab.Tank.transform.InverseTransformPoint(actor.body.transform.position+Vector3.up*actor.height);
                local.x=Mathf.Clamp(local.x,-lab.tankSize.x*.5f,lab.tankSize.x*.5f);
                local.z=Mathf.Clamp(local.z,-lab.tankSize.z*.5f,lab.tankSize.z*.5f);
                local.y=-lab.tankSize.y*.5f;
                Vector3 target=lab.Tank.transform.TransformPoint(local);
                CarryArms(lab,target,grip);
            }
        }
        // Alternating stance sweep and lifted recovery; visual approximation, not world foot locking.
        void StepFoot(string side,float angle,Vector3 restPoint,float weight)
        {
            Transform upper,lower,foot;
            if(!bones.TryGetValue("Thigh."+side,out upper)||!bones.TryGetValue("Shin."+side,out lower)||!bones.TryGetValue("Foot."+side,out foot))return;
            float cycle=Mathf.Repeat(angle/(2*Mathf.PI),1);
            float sweep=cycle<.6f ? Mathf.Lerp(.045f,-.045f,cycle/.6f) : Mathf.Lerp(-.045f,.045f,(cycle-.6f)/.4f);
            float lift=cycle<.6f ? 0 : Mathf.Sin((cycle-.6f)/.4f*Mathf.PI)*.055f;
            Vector3 target=root.TransformPoint(restPoint+new Vector3(0,lift,sweep)*weight);
            Quaternion footRotation=foot.rotation;
            Solve(upper,lower,foot,target,weight);
            foot.rotation=footRotation;
        }
        void KeepShortHandsClear(M0CarryLab.Actor actor)
        {
            if(actor.height>=1.7f)return;
            foreach(var side in new[]{"L","R"})
            {
                if(!bones.TryGetValue("Hand."+side,out var hand))continue;
                float minimum=root.position.y+.10f;
                if(hand.position.y>=minimum)continue;
                var target=hand.position;target.y=minimum;
                Arm(side,target,1);
            }
        }
        // Remove additive shoulder lift before Animator / stretch evaluation, never accumulate offsets.
        public void RestoreOffsets(){foreach(var pair in shoulderOffsets)if(pair.Key)pair.Key.localPosition-=pair.Value;shoulderOffsets.Clear();}
        void CarryArms(M0CarryLab lab,Vector3 target,float weight){
            RestoreOffsets();
            var style=carryStyle;
            float spacing=style?style.carryHandHalfWidth:.32f,lift=style?style.carryShoulderLift:.22f;
            foreach(string side in new[]{"L","R"}){
                float sign=side=="L"?-1:1;
                if(bones.TryGetValue("Clavicle."+side,out var shoulder)){
                    var offset=shoulder.parent.InverseTransformVector(root.up*(lift*weight));shoulder.localPosition+=offset;shoulderOffsets[shoulder]=offset;
                }
                var handTarget=target+Vector3.ProjectOnPlane(root.right*sign*spacing+root.forward*.045f,lab.Tank.transform.up);
                var local=lab.Tank.transform.InverseTransformPoint(handTarget);
                local.x=Mathf.Clamp(local.x,-lab.tankSize.x*.5f+.06f,lab.tankSize.x*.5f-.06f);
                local.z=Mathf.Clamp(local.z,-lab.tankSize.z*.5f+.06f,lab.tankSize.z*.5f-.06f);
                local.y=-lab.tankSize.y*.5f-.065f; // Wrist sits below the contact plane; rounded palm meets the glass.
                Arm(side,lab.Tank.transform.TransformPoint(local),weight,true);
            }
        }
        void Arm(string side, Vector3 target, float weight, bool carrying=false)
        {
            Transform upper, lower, hand;
            if (!bones.TryGetValue("UpperArm."+side,out upper) || !bones.TryGetValue("Forearm."+side,out lower) || !bones.TryGetValue("Hand."+side,out hand)) return;
            Solve(upper,lower,hand,target,weight,carrying?(Vector3?)(root.right*(side=="L"?-1:1)+root.forward*.35f):null);
        }
        void Solve(Transform upper,Transform lower,Transform hand,Vector3 target,float weight,Vector3? bendHint=null)
        {
            Quaternion upperStart=upper.localRotation, lowerStart=lower.localRotation;
            float a=Vector3.Distance(upper.position,lower.position), b=Vector3.Distance(lower.position,hand.position);
            Vector3 direction=target-upper.position;
            float distance=Mathf.Clamp(direction.magnitude,Mathf.Abs(a-b)+.001f,a+b-.001f);
            if (direction.sqrMagnitude<.000001f) return;
            direction.Normalize(); target=upper.position+direction*distance;
            Vector3 pole=Vector3.ProjectOnPlane(bendHint??root.forward,direction).normalized;
            if(pole.sqrMagnitude<.01f) pole=Vector3.ProjectOnPlane(root.right,direction).normalized;
            float along=(a*a-b*b+distance*distance)/(2*distance);
            Vector3 elbow=upper.position+direction*along+pole*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,elbow-upper.position)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,target-lower.position)*lower.rotation;
            upper.localRotation=Quaternion.Slerp(upperStart,upper.localRotation,weight);
            lower.localRotation=Quaternion.Slerp(lowerStart,lower.localRotation,weight);
        }
    }
}
