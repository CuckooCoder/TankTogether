using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
public static class AnimatorFlowChecks {
 public static string Result="";
 public static void Begin(){var s=Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
 static IEnumerator Run(SteamCarrySession s){
 s.StartLocalTest();var l=s.lab;l.Testing=true;
 var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(50,1,50);
 l.PlacePlayer(0,new Vector3(1000,.02f,0));l.SetExternalView(0,170,10);
 for(int i=0;i<25;i++)yield return new WaitForFixedUpdate();
 var a=l.Players[0];var visual=a.visual.GetComponent<CarryActorVisual>();var anim=visual.Animation.Animator;
 var thigh=a.visual.GetComponentsInChildren<Transform>().First(t=>t.name=="Thigh.L");var rest=thigh.localRotation;
 var lines=new List<string>{"time,y,vy,grounded,driver,current,next,transition,angle"};var seen=new HashSet<string>();
 l.SetInput(0,Vector3.zero,false,true);float start=Time.time;
 for(int i=0;i<65;i++){
 yield return new WaitForFixedUpdate();yield return null;
 var state=visual.Animation.State.ToString();var info=anim.GetCurrentAnimatorStateInfo(0);var next=anim.GetNextAnimatorStateInfo(0);
 lines.Add($"{Time.time-start:F3},{a.body.position.y:F3},{a.body.linearVelocity.y:F3},{a.grounded},{state},{info.shortNameHash},{next.shortNameHash},{anim.IsInTransition(0)},{Quaternion.Angle(rest,thigh.localRotation):F2}");
 if(seen.Add(state)){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/anim-flow-"+state+".png");}
 }
 bool jumpPass=seen.Count==6 && Quaternion.Angle(rest,thigh.localRotation)<2;
 var repeated=new HashSet<string>();l.SetInput(0,Vector3.forward,false,true,false,true);
 for(int i=0;i<60;i++){yield return new WaitForFixedUpdate();yield return null;repeated.Add(visual.Animation.State.ToString());}
 bool repeatPass=repeated.Contains("Takeoff")&&repeated.Contains("Fall")&&repeated.Contains("Land")&&repeated.Contains("Locomotion");
 l.SetInput(0,Vector3.zero,false);l.PlacePlayer(0,new Vector3(1024,.02f,0));
 for(int i=0;i<20;i++)yield return new WaitForFixedUpdate();
 var edgeStates=new HashSet<string>();l.SetInput(0,Vector3.right,false);
 for(int i=0;i<40;i++){yield return new WaitForFixedUpdate();yield return null;edgeStates.Add(visual.Animation.State.ToString());}
 bool edgePass=edgeStates.Contains("Fall")&&!edgeStates.Contains("Takeoff")&&!edgeStates.Contains("Rise");
 Result="jump="+jumpPass+"; repeatMovingJump="+repeatPass+"; walkOff="+edgePass+"; states="+string.Join(",",seen)+"; passed="+(jumpPass&&repeatPass&&edgePass);File.WriteAllLines("Logs/anim-flow.csv",lines);File.WriteAllText("Logs/anim-flow-result.txt",Result);
 Object.Destroy(floor);s.ReturnToLobby();
 }
}}


