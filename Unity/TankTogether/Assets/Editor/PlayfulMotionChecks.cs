using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
 public static class PlayfulMotionChecks {
  static List<string> rows;static bool passed;
  static void C(string n,bool ok,object v){rows.Add(n+"="+ok+" "+v);passed&=ok;}
  static IEnumerator Wait(float seconds){yield return new WaitForSeconds(seconds);}
  public static void Begin(){var s=Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Run(s));}
  static void Capture(Camera cam,string file){var previous=cam.targetTexture;var active=RenderTexture.active;var rt=new RenderTexture(800,800,24);var tex=new Texture2D(800,800,TextureFormat.RGB24,false);try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,800,800),0,0);tex.Apply();File.WriteAllBytes(file,tex.EncodeToPNG());}finally{cam.targetTexture=previous;RenderTexture.active=active;rt.Release();Object.Destroy(rt);Object.Destroy(tex);}}
  static IEnumerator Run(SteamCarrySession s){
   rows=new List<string>();passed=true;s.StartLocalTest();s.enabled=false;var l=s.lab;l.Testing=true;
   var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Presentation check floor";floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(400,1,400);
   var exp=l.GetComponent<HeightExperiment>();if(!exp)exp=l.gameObject.AddComponent<HeightExperiment>();exp.Begin(l,true);exp.ManualInput=true;
   var captureGo=new GameObject("Presentation evidence camera");var cam=captureGo.AddComponent<Camera>();cam.enabled=false;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.07f,.10f,.13f);cam.nearClipPlane=.05f;cam.farClipPlane=25;cam.orthographic=true;
   var a=l.Players[0];l.PlacePlayer(0,new Vector3(1000,.02f,0));yield return Wait(.3f);
   var visual=a.visual.GetComponent<CarryActorVisual>();var animator=visual.Animation.Animator;
   var bones=a.visual.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(x=>x.bones).Where(x=>x).Distinct().ToDictionary(x=>x.name);
   float armLength=Vector3.Distance(bones["UpperArm.L"].position,bones["Forearm.L"].position);float boneError=0,maxStep=0;float minRoll=0,maxRoll=0,maxLag=0;
   Directory.CreateDirectory("Logs/playful-motion");
   foreach(float height in new[]{1.7f,2.3f,5f}){
    a.height=height;a.capsule.height=height;a.capsule.center=Vector3.up*height/2;
    foreach(bool run in new[]{false,true}){
     l.SetInput(0,Vector3.forward,false,false,false,run);yield return Wait(.8f);
     C(height+"_"+run+"_speed",Mathf.Abs(a.body.linearVelocity.z-(run?6:4))<.05f,a.body.linearVelocity);
     C(height+"_"+run+"_anim_speed",animator.GetFloat("Speed")>3.5f,animator.GetFloat("Speed"));
     var previous=bones["UpperArm.L"].localRotation;float start=Time.time,min=100,max=-100,lag=0;
     int frame=0;while(Time.time-start<1.4f){yield return new WaitForEndOfFrame();
      min=Mathf.Min(min,visual.Playful.TallRoll);max=Mathf.Max(max,visual.Playful.TallRoll);lag=Mathf.Max(lag,visual.Playful.MaxArmLag);
      boneError=Mathf.Max(boneError,Mathf.Abs(Vector3.Distance(bones["UpperArm.L"].position,bones["Forearm.L"].position)-armLength));
      maxStep=Mathf.Max(maxStep,Quaternion.Angle(previous,bones["UpperArm.L"].localRotation));previous=bones["UpperArm.L"].localRotation;
      if(frame%3==0&&height!=2.3f){cam.orthographicSize=height*.62f;var center=a.body.transform.position+Vector3.up*height*.48f;cam.transform.position=center+new Vector3(5,1,8);cam.transform.LookAt(center);Capture(cam,"Logs/playful-motion/"+height+"-"+(run?"run":"walk")+"-"+(frame/3).ToString("D3")+".png");}frame++;
     }
     C(height+"_"+run+"_wobble",height<=2.3f?Mathf.Abs(min)+Mathf.Abs(max)<.01f:max-min>2&&max<3.1f&&min> -3.1f,min+".."+max);
     C(height+"_"+run+"_arm_softness",lag>.1f&&lag<100,lag);minRoll=min;maxRoll=max;maxLag=lag;
    }
   }
   l.SetInput(0,Vector3.zero,false);yield return Wait(1.2f);C("stop_settles",Mathf.Abs(visual.Playful.TallRoll)<.08f&&visual.Playful.TallWeight<.02f,visual.Playful.TallRoll+"/"+visual.Playful.TallWeight);
   C("arm_length_preserved",boneError<.002f,boneError);C("arm_frame_step",maxStep<70,maxStep);
   // Same evaluated pose, verify the presentation call cannot move physics or alter capsule.
   var p=a.body.position;var r=a.body.rotation;var v=a.body.linearVelocity;var av=a.body.angularVelocity;float ch=a.capsule.height;var tp=l.Tank.position;
   visual.Playful.Apply(a,animator,0,.016f,false);
   C("physics_untouched",a.body.position==p&&a.body.rotation==r&&a.body.linearVelocity==v&&a.body.angularVelocity==av&&a.capsule.height==ch&&l.Tank.position==tp,p);
   l.Tank.isKinematic=true;l.Tank.position=a.body.position+Vector3.up*(a.height+l.tankSize.y/2);l.Tank.rotation=Quaternion.identity;yield return Wait(.1f);l.SetInput(0,Vector3.zero,true);yield return Wait(.7f);yield return new WaitForEndOfFrame();
   C("grip_height",Mathf.Abs(a.height-5)<.01f,a.height);
   C("grip_preserved",a.joint,a.joint);C("grip_hands_above_shoulders",bones["Hand.L"].position.y>bones["UpperArm.L"].position.y&&bones["Hand.R"].position.y>bones["UpperArm.R"].position.y,bones["Hand.L"].position);
   C("grip_quiet",Mathf.Abs(visual.Playful.TallRoll)<.1f,visual.Playful.TallRoll);
   l.SetInput(0,Vector3.zero,false);l.Tank.isKinematic=false;Object.Destroy(floor);Object.Destroy(captureGo);s.enabled=true;s.ReturnToLobby();
   File.WriteAllText("Logs/playful-motion-checks.txt",string.Join("\n",rows)+"\npassed="+passed+"\nscope=local fixture, no remote/full-route or subjective acceptance");
  }
 }
}
