using System.Collections.Generic;
using UnityEngine;
namespace TankTogether.M0 {
 // Presentation only: never writes the physics root or gameplay state.
 public sealed class CarryPlayfulMotion {
  readonly Transform root;readonly CarryMotionStyle style;
  readonly Dictionary<string,Transform> bones=new Dictionary<string,Transform>();
  readonly List<SoftBone> arms=new List<SoftBone>();
  sealed class SoftBone {public Transform bone;public Quaternion value;public Vector3 velocity;public float stiffness,damping;public bool ready;}
  float phase,weight,weightVelocity,roll,rollVelocity;
  public float TallRoll=>roll;
  public float TallWeight=>weight;
  public float MaxArmLag {get;private set;}
  public CarryPlayfulMotion(Transform visual){
   root=visual;style=Resources.Load<CarryMotionStyle>("CharacterAnimation/MotionStyle");
   foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))foreach(var b in r.bones)if(b&&!bones.ContainsKey(b.name))bones.Add(b.name,b);
   foreach(var side in new[]{"L","R"}){Add("UpperArm."+side,250,27);Add("Forearm."+side,180,21);Add("Hand."+side,145,19);}
  }
  void Add(string name,float stiffness,float damping){if(bones.TryGetValue(name,out var b))arms.Add(new SoftBone{bone=b,stiffness=stiffness,damping=damping});}
  public void Reset(){weight=weightVelocity=roll=rollVelocity=0;phase=0;foreach(var arm in arms){arm.ready=false;arm.velocity=Vector3.zero;}}
  static Quaternion Advance(SoftBone arm,Quaternion target,float dt,float softness){
   if(!arm.ready||softness<.001f){arm.ready=true;arm.velocity=Vector3.zero;return arm.value=target;}
   float remaining=Mathf.Min(dt,.1f),factor=1/Mathf.Max(.25f,softness);
   while(remaining>0){float h=Mathf.Min(remaining,1f/120);remaining-=h;
    var delta=target*Quaternion.Inverse(arm.value);if(delta.w<0)delta=new Quaternion(-delta.x,-delta.y,-delta.z,-delta.w);
    delta.ToAngleAxis(out float angle,out Vector3 axis);if(angle>180)angle-=360;
    Vector3 error=Mathf.Abs(angle)<.0001f?Vector3.zero:axis*angle;
    arm.velocity=Vector3.ClampMagnitude(arm.velocity+(error*arm.stiffness*factor-arm.velocity*arm.damping*Mathf.Sqrt(factor))*h,720);
    float speed=arm.velocity.magnitude;if(speed>.0001f)arm.value=Quaternion.Normalize(Quaternion.AngleAxis(speed*h,arm.velocity/speed)*arm.value);
   }return arm.value;
  }
  void Rotate(string name,Vector3 axis,float degrees){if(bones.TryGetValue(name,out var bone))bone.rotation=Quaternion.AngleAxis(degrees,axis)*bone.rotation;}
  public void Apply(M0CarryLab.Actor actor,Animator animator,float grip,float dt,bool teleport){
   if(dt<=0||!animator)return;if(teleport)Reset();
   float softness=style?style.armSoftness:1;MaxArmLag=0;
   foreach(var arm in arms){var target=arm.bone.localRotation;var smooth=Advance(arm,target,dt,softness);MaxArmLag=Mathf.Max(MaxArmLag,Quaternion.Angle(target,smooth));arm.bone.localRotation=Quaternion.Slerp(target,smooth,1-grip);}
   float speed=animator.GetFloat("Speed");bool walking=animator.GetInteger("MotionState")==0;
   float start=style?style.tallStartHeight:2.3f,full=style?style.tallFullHeight:4.5f;
   float height=Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,Mathf.Max(start+.1f,full),actor.height));
   float desired=height*(walking?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.1f,3,speed)):0)*(1-grip)*(actor.support?0:1);
   float settle=style?style.settleSeconds:.22f;weight=Mathf.SmoothDamp(weight,desired,ref weightVelocity,settle,Mathf.Infinity,Mathf.Min(dt,.1f));
   phase=Mathf.Repeat(phase+Mathf.PI*2*(style?style.tallSwayHz:1.1f)*Mathf.Lerp(.8f,1.15f,Mathf.InverseLerp(0,6,speed))*Mathf.Min(dt,.1f),Mathf.PI*2);
   float amplitude=style?style.tallSwayDegrees:3;
   roll=Mathf.SmoothDamp(roll,Mathf.Sin(phase)*amplitude*weight,ref rollVelocity,.09f,Mathf.Infinity,Mathf.Min(dt,.1f));
   Rotate("Spine",root.forward,roll*.65f);Rotate("Chest",root.forward,-roll*.25f);Rotate("Head",root.forward,-roll*.4f);
   Rotate("Chest",root.right,Mathf.Sin(phase-.6f)*amplitude*.22f*weight);
   Rotate("Head",root.right,-Mathf.Sin(phase-.6f)*amplitude*.22f*weight);
  }
 }
}
