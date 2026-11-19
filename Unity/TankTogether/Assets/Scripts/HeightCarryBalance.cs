using UnityEngine;
namespace TankTogether.M0 {
 // Host/local physics only. Deterministic acceleration response, never a random idle oscillator.
 public sealed class HeightCarryBalance {
  Vector3 previousVelocity,previousCenter,filtered;int previousRoster;bool ready;
  public Vector3 AppliedTorque {get;private set;}
  public float MeanHeight {get;private set;}
  public float DampingMultiplier {get;private set;}=1;
  public void Reset(){ready=false;filtered=Vector3.zero;AppliedTorque=Vector3.zero;DampingMultiplier=1;}
  public void Step(M0CarryLab l,float dt){
   AppliedTorque=Vector3.zero;if(!l.Tank||!l.Simulate)return;
   var tank=l.Tank;var b=l.baseline;tank.angularDamping=b.angularDamping;DampingMultiplier=1;
   if(!l.Stretch||!l.Stretch.Active||!l.Stretch.PhysicsEnabled||b.stretchBalanceMode<.5f||l.Broken||tank.isKinematic||tank.transform.up.y<.7071f){Reset();return;}
   int count=0,mask=17;float height=0;Vector3 velocity=Vector3.zero,center=Vector3.zero;
   foreach(var a in l.Players){if(a==null||!a.active||!a.body||!a.grounded||(!a.joint&&!a.support))continue;count++;height+=a.height;velocity+=Vector3.ProjectOnPlane(a.body.linearVelocity-a.groundVelocity,Vector3.up);center+=a.body.position;unchecked{mask=mask*31+a.body.GetEntityId().GetHashCode();}}
   if(count<2){Reset();return;}MeanHeight=height/count;velocity/=count;center/=count;
   float high=b.stretchBalanceStart<l.Stretch.maxHeight ? Mathf.SmoothStep(0,1,Mathf.InverseLerp(b.stretchBalanceStart,l.Stretch.maxHeight,MeanHeight)) : 0;
   float low=Mathf.SmoothStep(0,1,Mathf.InverseLerp(l.standingHeight,l.Stretch.minHeight,MeanHeight));
   DampingMultiplier=Mathf.Lerp(1,b.stretchBalanceLowDamping,low)*Mathf.Lerp(1,b.stretchBalanceHighDamping,high);tank.angularDamping=b.angularDamping*DampingMultiplier;
   if(!ready||mask!=previousRoster||(center-previousCenter).sqrMagnitude>1){ready=true;previousRoster=mask;previousVelocity=velocity;previousCenter=center;filtered=Vector3.zero;return;}
   var acceleration=Vector3.ClampMagnitude((velocity-previousVelocity)/Mathf.Max(dt,.001f),8);previousVelocity=velocity;previousCenter=center;
   filtered=Vector3.Lerp(filtered,acceleration,1-Mathf.Exp(-dt/ .15f));
   float massScale=tank.mass/Mathf.Max(.1f,b.tankMass);
   AppliedTorque=Vector3.ClampMagnitude(Vector3.Cross(Vector3.up,-filtered)*tank.mass*b.stretchBalanceLever*high,b.stretchBalanceTorque*massScale);
   tank.AddTorque(AppliedTorque,ForceMode.Force);
  }
 }
}
