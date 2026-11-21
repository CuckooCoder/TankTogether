using UnityEngine;

namespace TankTogether.M0
{
    // Presentation only: never moves the actor Rigidbody or changes physics/colliders.
    [DefaultExecutionOrder(100)]
    public sealed class CarryActorVisual : MonoBehaviour
    {
        M0CarryLab lab;
        M0CarryLab.Actor actor;
        Vector3 previousPosition;
        CarryProceduralPose pose;
        CarryStretchVisual stretchVisual;
        // Constant animation channels are not guaranteed to overwrite our previous additive pose.
        // Restore the complete pre-additive pose before Animator, then capture its evaluated result.
        struct BonePose { public Transform bone; public Vector3 position, scale; public Quaternion rotation; }
        BonePose[] basePose;
        void CaptureBasePose() {
            for(int i=0;i<basePose.Length;i++){var b=basePose[i].bone;if(!b)continue;basePose[i].position=b.localPosition;basePose[i].rotation=b.localRotation;basePose[i].scale=b.localScale;}
        }
        void RestoreBasePose() {
            if(basePose==null)return;
            foreach(var p in basePose)if(p.bone){p.bone.localPosition=p.position;p.bone.localRotation=p.rotation;p.bone.localScale=p.scale;}
        }
        public CarryStretchVisual StretchVisual => stretchVisual;
        public CarryPlayfulMotion Playful => pose?.Playful;
        public CarryAnimatorDriver Animation { get; private set; }
        public float FootContactWeight => pose==null?0:pose.ContactWeight;
        public Vector2 CarryLean => pose==null?Vector2.zero:pose.CarryLean;
        public void Initialize(M0CarryLab owner, M0CarryLab.Actor target)
        {
            lab = owner; actor = target; previousPosition = actor.body.transform.position;
            // Imported art may contain preview cameras; gameplay owns rendering and audio.
            foreach (var camera in GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            foreach (var listener in GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
            foreach (var animator in GetComponentsInChildren<Animator>(true))
            { animator.applyRootMotion = false; animator.enabled = false; }
            var transforms=GetComponentsInChildren<Transform>(true);
            basePose=new BonePose[transforms.Length-1];int boneIndex=0;
            foreach(var bone in transforms)if(bone!=transform)basePose[boneIndex++].bone=bone;
            CaptureBasePose();
            stretchVisual = new CarryStretchVisual(transform);
            RefreshSize();
            pose = new CarryProceduralPose(transform);
            Animation = new CarryAnimatorDriver(transform, lab, actor);
        }
        public void RefreshSize()
        {
            if (actor == null || !lab) return;
            // Prefab is authored feet-at-zero, total height 1.7m, including the cap.
            transform.localPosition = Vector3.zero;
            transform.localScale = new Vector3(lab.baseline.playerDiameter/.6f, 1, lab.baseline.playerDiameter/.6f);
        }
        void Update() { pose?.RestoreOffsets(); stretchVisual?.Restore(); RestoreBasePose(); if(Animation!=null)Animation.Tick(Time.deltaTime); }
        void LateUpdate()
        {
            if (actor == null || !actor.body) return;
            RefreshSize(); // Includes remote snapshot height and local crouch.
            CaptureBasePose();
            stretchVisual?.Apply(actor.height);
            var position = actor.body.transform.position;
            var delta = Vector3.ProjectOnPlane(position-previousPosition,Vector3.up);
            previousPosition = position;
            if (lab.SessionPaused || lab.GMOpen || lab.Broken) return;
            // On peers derive facing from displayed motion; no extra network state required.
            var facing = lab.Simulate ? actor.move : delta;
            if (facing.sqrMagnitude >= (lab.Simulate ? .001f : .000001f) && delta.sqrMagnitude <= 4)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(facing,Vector3.up),720*Time.deltaTime);
            if (pose != null)
            {
                if(Animation != null && Animation.Animator && Animation.Animator.runtimeAnimatorController)
                    pose.ApplyGrip(lab, actor, Animation, Time.deltaTime);
                else pose.Tick(lab, actor, delta, Time.deltaTime);
            }
        }
    }
}
