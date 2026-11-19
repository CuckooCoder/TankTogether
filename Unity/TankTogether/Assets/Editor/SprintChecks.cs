using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
public static class SprintChecks {
 public static string Result="";
 public static void Begin(){var s=Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Run(s));}
 static IEnumerator Run(SteamCarrySession s){
 s.StartLocalTest();var l=s.lab;l.Testing=true;
 var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(150,1,150);
 l.PlacePlayer(0,new Vector3(1000,.02f,0));l.SetExternalView(0,130,8);
 for(int i=0;i<20;i++)yield return new WaitForFixedUpdate();
 var a=l.Players[0];var v=a.visual.GetComponent<CarryActorVisual>();var ts=a.visual.GetComponentsInChildren<Transform>();
 var arm=ts.First(t=>t.name=="UpperArm.L");var hand=ts.First(t=>t.name=="Hand.L");var chest=ts.First(t=>t.name=="Chest");
 var last=arm.localRotation;float maxDelta=0,maxRate=0;var rows=new List<string>{"stage,time,speed,phase,armDelta,armRate,handLocalZ,chestForwardY"};
 for(int stage=0;stage<4;stage++){
 l.SetInput(0,stage==3?Vector3.zero:Vector3.forward,false,false,false,stage==1);
 float start=Time.time;
 while(Time.time-start<1.4f){yield return new WaitForEndOfFrame();float delta=Quaternion.Angle(last,arm.localRotation);last=arm.localRotation;
 maxDelta=Mathf.Max(maxDelta,delta);maxRate=Mathf.Max(maxRate,delta/Mathf.Max(Time.deltaTime,.001f));
 rows.Add($"{stage},{Time.time:F3},{a.body.linearVelocity.magnitude:F3},{v.Animation.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime:F3},{delta:F3},{delta/Mathf.Max(Time.deltaTime,.001f):F1},{a.visual.InverseTransformPoint(hand.position).z:F3},{chest.forward.y:F3}");}
 ScreenCapture.CaptureScreenshot("Logs/sprint-stage-"+stage+".png");
 }
 Result=$"maxArmFrameDelta={maxDelta};maxArmDegreesPerSecond={maxRate}";File.WriteAllLines("Logs/sprint-trace.csv",rows);File.WriteAllText("Logs/sprint-result.txt",Result);
 Object.Destroy(floor);s.ReturnToLobby();
 }
}}
