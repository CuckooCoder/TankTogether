using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
public static class CarryArmPoseChecks {
 static Transform B(M0CarryLab.Actor a,string n)=>a.visual.GetComponentsInChildren<Transform>().First(t=>t.name==n);
 static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
 static void Shot(Camera cam,string path){var old=RenderTexture.active;var rt=new RenderTexture(800,800,24);var tex=new Texture2D(800,800,TextureFormat.RGB24,false);try{cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,800,800),0,0);tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());}finally{cam.targetTexture=null;RenderTexture.active=old;rt.Release();Object.Destroy(rt);Object.Destroy(tex);}}
 public static void Begin(string label){var s=Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Run(s,label));}
 static IEnumerator Run(SteamCarrySession s,string label){
  var rows=new List<string>();s.StartLocalTest();s.enabled=false;var l=s.lab;l.Testing=true;
  var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();e.Begin(l,true);e.ManualInput=true;
  var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(30,1,30);
  var go=new GameObject("Arm pose evidence camera");var cam=go.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.orthographicSize=1.1f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.07f,.1f,.13f);
  Directory.CreateDirectory("Logs/carry-arm-pose");
  try{
   for(int i=1;i<4;i++){l.Release(i);l.Players[i].active=false;l.Players[i].body.gameObject.SetActive(false);}
   var a=l.Players[0];l.PlacePlayer(0,new Vector3(1000,.02f,0));yield return Wait(20);
   var upper=B(a,"UpperArm.L");var elbow=B(a,"Forearm.L");var hand=B(a,"Hand.L");var right=B(a,"Hand.R");
   float u=Vector3.Distance(upper.position,elbow.position),v=Vector3.Distance(elbow.position,hand.position);
   foreach(float height in new[]{1.1f,1.7f,5f}){
    l.SetInput(0,Vector3.zero,false);a.height=height;a.capsule.height=height;a.capsule.center=Vector3.up*height/2;
    l.Tank.isKinematic=true;l.Tank.rotation=Quaternion.identity;l.Tank.position=a.body.position+Vector3.up*(height+l.tankSize.y/2);yield return Wait(4);l.SetInput(0,Vector3.zero,true);
    float maxAngle=0;var previous=upper.localRotation;for(int n=0;n<45;n++){yield return new WaitForEndOfFrame();maxAngle=Mathf.Max(maxAngle,Quaternion.Angle(previous,upper.localRotation));previous=upper.localRotation;}
    var center=a.body.position+Vector3.up*(height-.55f);cam.transform.position=center+a.visual.forward*6+Vector3.up*.12f;cam.transform.LookAt(center);Shot(cam,"Logs/carry-arm-pose/"+label+"-"+height+"-front.png");
    cam.transform.position=center+(a.visual.forward+a.visual.right*.65f).normalized*6+Vector3.up*.12f;cam.transform.LookAt(center);Shot(cam,"Logs/carry-arm-pose/"+label+"-"+height+"-angle.png");
    float width=Vector3.Distance(hand.position,right.position),clearance=Mathf.Abs(Vector3.Dot(elbow.position-upper.position,a.visual.right));
    bool lengths=Mathf.Abs(Vector3.Distance(upper.position,elbow.position)-u)<.001f&&Mathf.Abs(Vector3.Distance(elbow.position,hand.position)-v)<.001f;
    rows.Add(height+" grip="+(bool)a.joint+" width="+width+" elbowOut="+clearance+" boneLengths="+lengths+" acquireMaxFrameDeg="+maxAngle+" handY="+(hand.position.y-a.body.position.y)+" shoulderY="+(upper.position.y-a.body.position.y));
    l.SetInput(0,Vector3.zero,false);previous=upper.localRotation;maxAngle=0;for(int n=0;n<45;n++){yield return new WaitForEndOfFrame();maxAngle=Mathf.Max(maxAngle,Quaternion.Angle(previous,upper.localRotation));previous=upper.localRotation;}
    rows.Add(height+" released="+(!a.joint)+" releaseMaxFrameDeg="+maxAngle);
   }
  }finally{Object.Destroy(floor);Object.Destroy(go);s.enabled=true;s.ReturnToLobby();File.WriteAllText("Logs/carry-arm-pose/"+label+".txt",string.Join("\n",rows));}
 }
}
}
