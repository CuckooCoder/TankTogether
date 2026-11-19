using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
 public static class HeightBalanceChecks {
  [Serializable] public class Sample {public int count,mode;public float height,peakTilt,slowTilt,settledTilt,maxTorque,maxSpeed,maxError,damping;public bool broken;}
  [Serializable] public class Report {public bool complete,passed;public List<Sample> samples=new List<Sample>();public List<string> checks=new List<string>();}
  public static Report Last;static M0CarryLab l;
  static void C(string name,bool ok,object v){Last.checks.Add(name+"="+ok+" "+v);Last.passed&=ok;}
  static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
  static void Input(Vector3 v){for(int i=0;i<l.LocalPlayerCount;i++)l.SetInput(i,v,true);}
  static void Fixture(float height,int mode){
   int count=l.LocalPlayerCount;
   l.DynamicRoster=true;l.SessionTeamSize=count;l.baseline.stretchBalanceMode=mode;l.HeightBalance.Reset();
   l.Tank.isKinematic=true;l.Tank.mass=l.baseline.tankMass*count/4f;
   var spacing=l.baseline.spawnHalfSpacing;
   for(int i=0;i<count;i++){
    var a=l.Players[i];var feet=new Vector3(1000+(i==2?0:i==0?-spacing.x:spacing.x),.02f,i==2?spacing.y:-spacing.y*.5f);
    l.PlacePlayer(i,feet);a.height=height;a.capsule.height=height;a.capsule.center=Vector3.up*height/2;
   }
   l.Tank.position=new Vector3(1000,height+.02f+l.tankSize.y/2,0);l.Tank.rotation=Quaternion.identity;l.Tank.transform.SetPositionAndRotation(l.Tank.position,Quaternion.identity);l.Tank.isKinematic=false;l.Tank.linearVelocity=l.Tank.angularVelocity=Vector3.zero;Physics.SyncTransforms();for(int i=0;i<count;i++)l.SetInput(i,Vector3.zero,false);Input(Vector3.zero);
  }
  static float Tilt()=>Vector3.Angle(l.Tank.transform.up,Vector3.up);
  public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Run(s));}
  public static void BeginBoundary(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Boundary(s));}
  static IEnumerator Boundary(SteamCarrySession s){
   Last=new Report{passed=true};s.StartLocalTest();s.enabled=false;l=s.lab;l.Testing=true;
   var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();e.Begin(l,true);e.ManualInput=true;
   var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(200,1,200);
   try{
    int count=l.LocalPlayerCount;
    float high=e.maxHeight;
    Fixture(l.standingHeight,1);yield return Wait(60);
    for(int i=0;i<count;i++)l.SetInput(i,Vector3.zero,true,stretch:1);
    int growTicks=Mathf.CeilToInt((high-l.standingHeight)/(e.growRate*Time.fixedDeltaTime))+120;
    yield return Wait(growTicks);Input(Vector3.zero);
    float lowest=Enumerable.Range(0,count).Min(i=>l.Players[i].height);
    C("wide_grips_can_grow_to_configured_max",lowest>=high-.025f&&Enumerable.Range(0,count).All(l.IsGripping)&&!l.Broken,"height="+lowest+" configured="+high);
    foreach(bool sprint in new[]{false,true}){
    Fixture(high,1);yield return Wait(60);float keyboardPeak=0;
    for(int n=0;n<400;n++){
     Vector3 direction=n<150?Vector3.forward:n<250?Vector3.right:Vector3.zero;
     for(int i=0;i<count;i++)l.SetInput(i,direction,true,sprint:sprint);
     yield return new WaitForFixedUpdate();keyboardPeak=Mathf.Max(keyboardPeak,Tilt());
    }
    C("keyboard_start_turn_stop_"+sprint,keyboardPeak<30&&!l.Broken&&Tilt()<8,"peak="+keyboardPeak+" settled="+Tilt());
    }
    Input(Vector3.forward);yield return Wait(150);
    C("keyboard_full_walk_settles",Tilt()<8&&l.HeightBalance.AppliedTorque.magnitude<2&&!l.Broken,"tilt="+Tilt()+" torque="+l.HeightBalance.AppliedTorque.magnitude);
    // Actual airborne bodies: extra balance must stop, independent of their horizontal velocity.
    l.Tank.useGravity=false;l.Tank.position+=Vector3.up*8;
    for(int i=0;i<count;i++){l.Players[i].body.useGravity=false;l.Players[i].body.position+=Vector3.up*8;}
    Physics.SyncTransforms();yield return Wait(8);
    C("airborne_no_extra_torque",Enumerable.Range(0,count).All(i=>!l.Players[i].grounded)&&l.HeightBalance.AppliedTorque==Vector3.zero&&l.Tank.angularDamping==l.baseline.angularDamping,l.HeightBalance.AppliedTorque);
    l.Tank.useGravity=true;for(int i=0;i<count;i++)l.Players[i].body.useGravity=true;
    Fixture(high,1);yield return Wait(60);Input(Vector3.right);float peak=0,torque=0;
    for(int n=0;n<100;n++){yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,Tilt());torque=Mathf.Max(torque,l.HeightBalance.AppliedTorque.magnitude);}
    C("three_players_bounded",Enumerable.Range(0,3).All(l.IsGripping)&&peak<30&&!l.Broken&&torque>1&&torque<=l.baseline.stretchBalanceTorque*.75f+.01f,"tilt="+peak+" torque="+torque);
    // Same-frame teleport and mode gates, with a live grounded fixture.
    l.enabled=false;foreach(var a in l.Players)if(a.active)a.body.position+=Vector3.right*10;l.Tank.position+=Vector3.right*10;
    l.HeightBalance.Step(l,Time.fixedDeltaTime);C("teleport_does_not_kick",l.HeightBalance.AppliedTorque==Vector3.zero,l.HeightBalance.AppliedTorque);
    l.baseline.stretchBalanceMode=0;l.HeightBalance.Step(l,Time.fixedDeltaTime);C("switch_off_restores_damping",l.Tank.angularDamping==l.baseline.angularDamping&&l.HeightBalance.AppliedTorque==Vector3.zero,l.Tank.angularDamping);
    l.Simulate=false;l.HeightBalance.Step(l,Time.fixedDeltaTime);C("proxy_no_added_force",l.HeightBalance.AppliedTorque==Vector3.zero,l.HeightBalance.AppliedTorque);l.Simulate=true;
   }finally{l.enabled=true;UnityEngine.Object.Destroy(floor);s.enabled=true;s.ReturnToLobby();Last.complete=true;File.WriteAllText("Logs/height-balance-boundary.json",JsonUtility.ToJson(Last,true));}
  }
  static IEnumerator Run(SteamCarrySession s){
   Last=new Report{passed=true};s.StartLocalTest();s.enabled=false;l=s.lab;l.Testing=true;
   var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();e.Begin(l,true);e.ManualInput=true;
   var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Height balance comparative fixture";floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(200,1,200);
   try{
    int count=l.LocalPlayerCount;
    foreach(float h in new[]{e.minHeight,l.standingHeight,e.maxHeight})foreach(int mode in new[]{0,1}){
     Fixture(h,mode);yield return Wait(60);C("fixture_"+count+"_"+h+"_"+mode,Enumerable.Range(0,count).All(l.IsGripping)&&!l.Broken,"grips="+Enumerable.Range(0,count).Count(l.IsGripping));
     var sample=new Sample{count=count,height=h,mode=mode};
     for(int n=0;n<280;n++){
      // Slow deliberate ramp, then abrupt start, a right-angle turn and stop.
      Vector3 v=n<100?Vector3.forward*(Mathf.Min(n/70f,1)*.35f):n<150?Vector3.forward:n<200?Vector3.right:Vector3.zero;Input(v);
      yield return new WaitForFixedUpdate();float tilt=Tilt();if(n<100)sample.slowTilt=Mathf.Max(sample.slowTilt,tilt);else sample.peakTilt=Mathf.Max(sample.peakTilt,tilt);
      sample.maxTorque=Mathf.Max(sample.maxTorque,l.HeightBalance.AppliedTorque.magnitude);sample.maxSpeed=Mathf.Max(sample.maxSpeed,l.Tank.linearVelocity.magnitude);sample.damping=l.HeightBalance.DampingMultiplier;
      for(int i=0;i<count;i++)if(l.Players[i].joint)sample.maxError=Mathf.Max(sample.maxError,Vector3.Distance(l.Head(i),l.Tank.transform.TransformPoint(l.Players[i].anchor)));
     }
     yield return Wait(100);sample.settledTilt=Tilt();sample.broken=l.Broken;Last.samples.Add(sample);File.WriteAllText("Logs/height-balance-checks.json",JsonUtility.ToJson(Last,true));
     C("controllable_"+count+"_"+h+"_"+mode,!sample.broken&&sample.peakTilt<30&&sample.maxError<.4f&&sample.maxSpeed<8,JsonUtility.ToJson(sample));
    }
    var tall=Last.samples.Single(x=>x.height==e.maxHeight&&x.mode==1);
    var normal=Last.samples.Single(x=>x.height==e.maxHeight&&x.mode==0);
    var low=Last.samples.Single(x=>x.height==e.minHeight&&x.mode==1);
    C("tall_more_sensitive_"+count,tall.peakTilt>normal.peakTilt*1.12f,tall.peakTilt+" vs "+normal.peakTilt);C("tall_slow_is_safer_"+count,tall.slowTilt<tall.peakTilt*.85f,tall.slowTilt+" vs "+tall.peakTilt);C("short_more_stable_"+count,low.peakTilt<tall.peakTilt,low.peakTilt+" vs "+tall.peakTilt);C("tall_recovers_"+count,tall.settledTilt<8,tall.settledTilt);
    Fixture(e.maxHeight,1);yield return Wait(50);C("idle_no_torque",l.HeightBalance.AppliedTorque.magnitude<.1f,l.HeightBalance.AppliedTorque);
    for(int i=0;i<count;i++)l.SetInput(i,Vector3.zero,false);l.Tank.position+=Vector3.up*3;yield return Wait(1);C("no_contact_no_extra",l.HeightBalance.AppliedTorque==Vector3.zero&&Mathf.Abs(l.Tank.angularDamping-l.baseline.angularDamping)<.001f,l.HeightBalance.AppliedTorque);
    var profile=CarryTuning.Capture(l);var copy=UnityEngine.Object.Instantiate(l.baseline);var extras=CarryTuning.Clone(l.Tuning);try{CarryTuning.Read(profile,copy,extras,profile.levelId);C("all70_roundtrip",profile.values.Length==70&&copy.stretchBalanceMode==1&&copy.stretchBalanceTorque==l.baseline.stretchBalanceTorque,profile.values.Length);var old=CarryTuning.Parse(JsonUtility.ToJson(profile));old.values=old.values.Where(x=>!x.key.StartsWith("b.stretchBalance")).ToArray();CarryTuning.Read(old,copy,extras,old.levelId);C("old64_disables_new_rule",copy.stretchBalanceMode==0,copy.stretchBalanceMode);profile.values.Single(x=>x.key=="b.stretchBalanceTorque").value=float.NaN;bool rejected=false;try{CarryTuning.Read(profile,copy,extras,profile.levelId);}catch(InvalidDataException){rejected=true;}C("invalid_config_atomic",rejected&&copy.stretchBalanceMode==0,"");}finally{UnityEngine.Object.Destroy(copy);}
   }finally{UnityEngine.Object.Destroy(floor);s.enabled=true;s.ReturnToLobby();Last.complete=true;File.WriteAllText("Logs/height-balance-checks.json",JsonUtility.ToJson(Last,true));}
  }
 }
}
