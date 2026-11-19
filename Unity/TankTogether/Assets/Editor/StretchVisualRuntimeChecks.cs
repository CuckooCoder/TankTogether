using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
 public static class StretchVisualRuntimeChecks {
  static List<string> rows;static bool passed;
  static void C(string n,bool ok,object v){rows.Add(n+"="+ok+" "+v);passed&=ok;}
  static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
  static Transform Bone(M0CarryLab.Actor a,string n)=>a.visual.GetComponentsInChildren<Transform>().First(t=>t.name==n);
  public static void Begin(){var s=Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
  static IEnumerator Run(SteamCarrySession s){
   rows=new List<string>();passed=true;s.StartLocalTest();s.enabled=false;var l=s.lab;l.Testing=true;
   var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();e.Begin(l,true);e.ManualInput=true;
   var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Stretch animation fixture";floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(80,1,80);
   l.PlacePlayer(0,new Vector3(1000,.02f,0));l.SetExternalView(0,150,15);yield return Wait(20);
   var a=l.Players[0];var visual=a.visual.GetComponent<CarryActorVisual>();float upper=Vector3.Distance(Bone(a,"UpperArm.L").position,Bone(a,"Forearm.L").position);float lower=Vector3.Distance(Bone(a,"Forearm.L").position,Bone(a,"Hand.L").position);
   foreach(float h in new[]{1.1f,5f}){
    a.height=h;a.capsule.height=h;a.capsule.center=Vector3.up*h/2;
    foreach(string mode in new[]{"idle","walk","run","jump"}){
     l.SetInput(0,mode=="idle"?Vector3.zero:Vector3.forward,false,mode=="jump",false,mode=="run");yield return Wait(mode=="jump"?8:30);yield return new WaitForEndOfFrame();
     float up=Vector3.Distance(Bone(a,"UpperArm.L").position,Bone(a,"Forearm.L").position),lo=Vector3.Distance(Bone(a,"Forearm.L").position,Bone(a,"Hand.L").position);
     if(mode=="walk"||mode=="run")C(h+"_"+mode+"_moving",a.body.linearVelocity.magnitude>3,a.body.linearVelocity);if(mode=="jump")C(h+"_airborne",!a.grounded&&a.body.position.y>.1f,a.body.position.y);
     C(h+"_"+mode+"_limbs",Mathf.Abs(up-upper)<.001f&&Mathf.Abs(lo-lower)<.001f,up+"/"+lo);
     C(h+"_"+mode+"_scale",Mathf.Abs(a.visual.localScale.y-1)<.001f,a.visual.localScale);
     C(h+"_"+mode+"_extension",Mathf.Abs(visual.StretchVisual.AppliedExtension-(h-1.7f))<.001f,visual.StretchVisual.AppliedExtension);
     if(mode=="idle"&&h<1.7f)C("short_hands_above_floor",Bone(a,"Hand.L").position.y>a.body.position.y+.04f&&Bone(a,"Hand.R").position.y>a.body.position.y+.04f,Bone(a,"Hand.L").position.y-a.body.position.y);
     ScreenCapture.CaptureScreenshot("Logs/stretch-runtime-"+h+"-"+mode+".png");
    }
    l.SetInput(0,Vector3.zero,false);yield return Wait(60);
   }
   l.Tank.isKinematic=true;l.Tank.position=a.body.position+Vector3.up*(a.height+l.tankSize.y/2);l.Tank.rotation=Quaternion.identity;yield return Wait(3);l.SetInput(0,Vector3.zero,true);yield return Wait(25);yield return new WaitForEndOfFrame();
   C("5m_grip",a.joint,a.joint);C("5m_hand_reaches_up",Bone(a,"Hand.L").position.y>Bone(a,"UpperArm.L").position.y,Bone(a,"Hand.L").position.y-Bone(a,"UpperArm.L").position.y);
   ScreenCapture.CaptureScreenshot("Logs/stretch-runtime-5-grip.png");
   l.SetInput(0,Vector3.zero,false);l.Tank.isKinematic=false;Object.Destroy(floor);s.enabled=true;s.ReturnToLobby();
   File.WriteAllText("Logs/stretch-runtime-checks.txt",string.Join("\n",rows)+"\npassed="+passed+"\nscope=isolated local animation fixture, not route or remote multiplayer");
  }
 }
}
