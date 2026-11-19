using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
public static class FeedbackPoseChecks {
 public static string Output="before";
 public static void Begin(){var s=Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
 static IEnumerator Wait(float seconds){float end=Time.time+seconds;while(Time.time<end)yield return null;}
 static IEnumerator Run(SteamCarrySession s){
 var rows=new List<string>();bool passed=true;
 s.StartLocalTest();s.enabled=false;var l=s.lab;l.Testing=true;
 var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();e.Begin(l,true);e.ManualInput=true;
 var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(100,1,100);
 l.PlacePlayer(0,new Vector3(1000,.02f,0));var a=l.Players[0];
 var bones=a.visual.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Distinct().ToDictionary(b=>b.name);
 var camgo=new GameObject("Feedback evidence camera");var cam=camgo.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;cam.orthographicSize=3.4f;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.08f,.12f,.15f);
 Directory.CreateDirectory("Logs/feedback-20261017");
 l.SetInput(0,Vector3.zero,false);yield return Wait(.5f);
 // Exercise ordinary height input through the controller, including tall walking and return to normal.
 foreach(float height in new[]{5f,1.1f,1.7f,5f,1.7f}){
 l.SetInput(0,Vector3.zero,false,false,false,false,height>a.height?1:-1);
 float deadline=Time.time+15;while(Mathf.Abs(a.height-height)>.009f&&Time.time<deadline)yield return new WaitForFixedUpdate();
 l.SetInput(0,Vector3.forward,false,false,false,true);yield return Wait(1);
 l.SetInput(0,Vector3.zero,false);yield return Wait(1.5f);yield return new WaitForEndOfFrame();
 var head=a.visual.InverseTransformPoint(bones["Head"].position);var pelvis=a.visual.InverseTransformPoint(bones["Pelvis"].position);
 float offset=new Vector2(head.x-pelvis.x,head.z-pelvis.z).magnitude;
 bool ok=offset<(height<2?.06f:.25f)&&Mathf.Abs(a.height-height)<.02f;passed&=ok;
 rows.Add("height="+height+" actual="+a.height+" head_horizontal_offset="+offset+" passed="+ok);
 cam.transform.position=a.body.transform.position+new Vector3(7,3,9);cam.transform.LookAt(a.body.transform.position+Vector3.up*2.3f);
 var rt=RenderTexture.GetTemporary(700,700,24);cam.targetTexture=rt;cam.Render();var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(700,700,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,700,700),0,0);tex.Apply();File.WriteAllBytes("Logs/feedback-20261017/"+Output+"-"+rows.Count+".png",tex.EncodeToPNG());Object.Destroy(tex);RenderTexture.active=old;cam.targetTexture=null;RenderTexture.ReleaseTemporary(rt);
 }
 File.WriteAllText("Logs/feedback-20261017/pose-"+Output+".txt",string.Join("\n",rows)+"\npassed="+passed);
 Object.Destroy(floor);Object.Destroy(camgo);s.enabled=true;s.ReturnToLobby();
 }
}
}
