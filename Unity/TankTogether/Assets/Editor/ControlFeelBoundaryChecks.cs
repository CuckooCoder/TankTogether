using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor
{
 public static class ControlFeelBoundaryChecks
 {
  public static WhiteboxLiftChecks.Evidence Last;
  static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
  static void Check(bool ok,string name,string detail){Last.checks.Add(new M0CarryChecks.Check{name=name,passed=ok,evidence=detail});}
  [MenuItem("Tank Together/Tests/Check 3C Boundaries")]
  public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();if(!Application.isPlaying||!s||s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Play Home first");s.StartCoroutine(Run(s));}
  static IEnumerator Run(SteamCarrySession s)
  {
   Last=new WhiteboxLiftChecks.Evidence();s.StartLocalTest();var lab=s.lab;lab.Testing=true;var lift=lab.GetComponent<WhiteboxLift>();
   for(int count=2;count<=4;count++){
    lab.DynamicRoster=true;lab.SessionTeamSize=count;lab.ConfigureSessionRoster(new[]{true,true,count>=3,count==4},true);lab.SetSessionPaused(false);lift.ResetLift(false);
    var b=lift.platformCollider.bounds;
    for(int i=0;i<count;i++)lab.PlacePlayer(i,new Vector3(b.center.x+(i%2==0?1.3f:3.1f),b.max.y+.02f,b.center.z+(i<2?-3:3)));
    yield return Steps(145);int grounded=0;for(int n=0;n<30;n++){yield return Steps(1);if(lab.Players[0].grounded)grounded++;}
    lab.SetInput(0,Vector3.zero,false,true);float peak=0;
    for(int n=0;n<50;n++){yield return Steps(1);peak=Mathf.Max(peak,lab.Players[0].body.position.y-lift.platformCollider.bounds.max.y);}
    Check(lift.Moving&&grounded>=29&&peak>.55f&&peak<.8f,"authored_lift_free_jump_"+count,"grounded="+grounded+"/30 peak relative="+peak);
   }
   // Stop and descend using an isolated floor; do not silently retune the authored lift.
   var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.position=new Vector3(1800,9.5f,0);go.transform.localScale=new Vector3(20,1,20);
   var body=go.AddComponent<Rigidbody>();body.isKinematic=true;var d=go.AddComponent<ControlFeelMovingFloor>();d.body=body;
   lab.PlacePlayer(0,new Vector3(1800,10.02f,0));yield return Steps(20);d.velocity=Vector3.down;yield return Steps(80);
   Check(lab.Players[0].grounded&&Mathf.Abs(lab.Players[0].body.position.y-body.position.y-.5f)<.04f,"descending_platform_support",lab.Players[0].body.linearVelocity.ToString());
   d.velocity=Vector3.zero;yield return Steps(30);
   Check(lab.Players[0].grounded&&Mathf.Abs(lab.Players[0].groundVelocity.y)<.01f,"platform_stop_clears_ground_velocity",lab.Players[0].groundVelocity.ToString());
   float start=lab.Players[0].body.position.y;lab.SetInput(0,Vector3.zero,false,true);yield return Steps(8);lab.SetInput(0,Vector3.zero,false,true);float max=0;
   for(int n=0;n<60;n++){yield return Steps(1);max=Mathf.Max(max,lab.Players[0].body.position.y-start);}
   Check(max>.60f&&max<.72f,"no_extra_air_jump_or_platform_impulse",max.ToString());
   UnityEngine.Object.Destroy(go);s.ReturnToLobby();Last.complete=true;Last.passed=Last.checks.All(c=>c.passed);File.WriteAllText("Logs/control-feel-boundaries.json",JsonUtility.ToJson(Last,true));Debug.Log("3C boundaries complete "+Last.passed);
  }
 }
}
