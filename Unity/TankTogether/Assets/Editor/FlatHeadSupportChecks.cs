using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
public static class FlatHeadSupportChecks {
 [Serializable] public class Sample {public int mode,count;public float initialTilt,finalTilt,maxSpeed,drift,height;}
 [Serializable] public class Report {public string scope="real Unity physics fixtures and repeated inputs; not full route / remote play";public bool complete,passed;public List<Sample> samples=new List<Sample>();public List<M0CarryChecks.Check> checks=new List<M0CarryChecks.Check>();}
 public static Report Last;static M0CarryLab l;
 static void Check(string n,bool ok,string detail){Last.checks.Add(new M0CarryChecks.Check{name=n,passed=ok,evidence=detail});}
 static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
 static void Fixture(int count,float tilt=0){
  l.Tank.isKinematic=false;l.Tank.useGravity=true;l.Tank.mass=l.baseline.tankMass*count/4f;
  for(int i=0;i<l.LocalPlayerCount;i++) {
   var a=l.Players[i];a.active=true;a.body.gameObject.SetActive(true);
   float x=i==2?0:i==0?-.9f:.9f,z=count==2?0:i==2?.55f:-.275f;
   l.PlacePlayer(i,new Vector3(1000+x,.02f,z));l.SetInput(i,Vector3.zero,false);a.active=i<count;a.body.gameObject.SetActive(a.active);
  }
  l.Tank.position=new Vector3(1000,1.7f+l.tankSize.y/2,0);l.Tank.rotation=Quaternion.Euler(tilt,0,0);l.Tank.transform.SetPositionAndRotation(l.Tank.position,l.Tank.rotation);l.Tank.linearVelocity=l.Tank.angularVelocity=Vector3.zero;Physics.SyncTransforms();
 }
 public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();if(!Application.isPlaying||s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Play Home required");s.StartCoroutine(Run(s));}
 static IEnumerator Run(SteamCarrySession s){
  Last=new Report();s.StartLocalTest();l=s.lab;l.Testing=true;
  var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();e.Begin(l,true);e.ManualInput=true;
  var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Flat head comparison floor";floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(40,1,40);
  try {
   int team=l.LocalPlayerCount;
   for(int mode=0;mode<2;mode++)for(int count=2;count<=team;count++){
    l.baseline.headSupportMode=mode;Fixture(count,4);var sample=new Sample{mode=mode,count=count,initialTilt=4};
    for(int n=0;n<160;n++){yield return new WaitForFixedUpdate();sample.maxSpeed=Mathf.Max(sample.maxSpeed,l.Tank.linearVelocity.magnitude);}
    sample.finalTilt=Vector3.Angle(l.Tank.rotation*Vector3.up,Vector3.up);sample.height=l.Tank.position.y;sample.drift=Vector3.ProjectOnPlane(l.Tank.position-new Vector3(1000,0,0),Vector3.up).magnitude;Last.samples.Add(sample);
    if(mode==1)Check("flat_settled_"+count,sample.finalTilt<3&&sample.maxSpeed<3&&sample.height>1.9f&&!l.Broken,JsonUtility.ToJson(sample));
   }
   var oldTwo=Last.samples.Find(x=>x.mode==0&&x.count==2);var flatTwo=Last.samples.Find(x=>x.mode==1&&x.count==2);
   Check("two_head_tilt_improved",flatTwo.finalTilt<oldTwo.finalTilt*.6f,"old="+oldTwo.finalTilt+" flat="+flatTwo.finalTilt);
   l.baseline.headSupportMode=1;Fixture(team);yield return Wait(45);
   float baseY=l.Tank.position.y;for(int i=0;i<team;i++)l.SetInput(i,Vector3.zero,false,true);
   float peak=baseY,max=0;for(int n=0;n<100;n++){yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,l.Tank.position.y);max=Mathf.Max(max,l.Tank.linearVelocity.magnitude);}
   Check("three_head_jump_finite",peak>baseY+.1f&&max<12&&!l.Broken,"rise="+(peak-baseY)+" max="+max);
   Fixture(team);yield return Wait(40);baseY=l.Tank.position.y;
   for(int i=0;i<team;i++)l.SetInput(i,Vector3.zero,false,false,false,false,1);yield return Wait(100);
   Check("head_growth_lifts",l.Tank.position.y>baseY+.12f,"rise="+(l.Tank.position.y-baseY));
   for(int i=0;i<team;i++)l.SetInput(i,Vector3.zero,false,false,false,false,0);
   Fixture(2);yield return Wait(45);l.Tank.linearVelocity=Vector3.right*4;
   yield return Wait(100);Check("slides_off_finite_footprint",l.Tank.position.y<1.5f,"tank="+l.Tank.position.ToString("F3"));
   Fixture(team);yield return Wait(30);l.Simulate=false;l.Tank.useGravity=false;
   var a=l.Players[0];Vector3 head=a.body.position+Vector3.up*a.height;
   l.Tank.rotation=Quaternion.identity;l.Tank.position=new Vector3(head.x,head.y+l.tankSize.y/2-.08f,head.z);l.Tank.linearVelocity=Vector3.down*5;Physics.SyncTransforms();
   float force=FlatHeadSupport.Apply(l,a);Check("cap_not_multiplied",Mathf.Abs(force-l.baseline.supportMaxForce)<.001f,"force="+force);
   l.Tank.position=new Vector3(head.x,head.y+l.tankSize.y/2+.1f,head.z);Physics.SyncTransforms();
   Check("no_gap_attraction",FlatHeadSupport.Apply(l,a)==0,"gap=.1m");
   l.Tank.rotation=Quaternion.Euler(80,0,0);Physics.SyncTransforms();Check("no_overturned_support",FlatHeadSupport.Apply(l,a)==0,"tilt=80");
   l.Tank.rotation=Quaternion.identity;l.Tank.position=new Vector3(head.x,head.y+l.tankSize.y/2-.02f,head.z);l.Tank.linearVelocity=Vector3.zero;Physics.SyncTransforms();
   float half=l.baseline.playerDiameter*l.baseline.headSupportWidthRatio*.5f;
   Check("corners_inside_body",Mathf.Sqrt(2)*half<=a.capsule.radius,"corner radius="+(Mathf.Sqrt(2)*half));
   var p=CarryTuning.Capture(l);var b=UnityEngine.Object.Instantiate(l.baseline);var extra=CarryTuning.Clone(l.Tuning);
   try{CarryTuning.Read(p,b,extra,p.levelId);Check("GM_roundtrip",b.headSupportMode==1&&b.headSupportWidthRatio==.7f,"all="+p.values.Length);
    p.values=p.values.Where(x=>!x.key.StartsWith("b.headSupport")&&!x.key.StartsWith("b.stretch")).ToArray();CarryTuning.Read(p,b,extra,p.levelId);Check("legacy_preset_preserves_old_mode",b.headSupportMode==0,"mode="+b.headSupportMode);
    b.headSupportMode=.5f;Check("reject_fractional_mode",CarryTuning.Validate(b,extra)!=null,"mode=.5 rejected");
   }finally{UnityEngine.Object.Destroy(b);}
  }finally{l.Simulate=true;l.Tank.useGravity=true;for(int i=0;i<l.LocalPlayerCount;i++){var a=l.Players[i];a.active=true;a.body.gameObject.SetActive(true);}UnityEngine.Object.Destroy(floor);s.ReturnToLobby();l.Testing=false;Last.complete=true;Last.passed=Last.checks.All(x=>x.passed);File.WriteAllText("Logs/flat-head-support-checks.json",JsonUtility.ToJson(Last,true));}
 }
}
}
