using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor
{
 public static class CharacterVisualChecks
 {
  public static WhiteboxLiftChecks.Evidence Last;
  static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
  static void Check(bool ok,string name,string detail){Last.checks.Add(new M0CarryChecks.Check{name=name,passed=ok,evidence=detail});}
  static Bounds BoundsOf(M0CarryLab.Actor a){var rs=a.visual.GetComponentsInChildren<Renderer>();var b=rs[0].bounds;foreach(var r in rs)b.Encapsulate(r.bounds);return b;}
  public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
  static IEnumerator Run(SteamCarrySession s)
  {
   Last=new WhiteboxLiftChecks.Evidence();s.StartLocalTest();var lab=s.lab;lab.Testing=true;yield return Steps(5);
   Check(HeightExperimentChecks.HasLocalActors(lab,3)&&lab.Players.Take(3).All(a=>a.presentation&&a.body.GetComponentsInChildren<Rigidbody>().Length==1&&a.body.GetComponentsInChildren<Collider>().Length==1),"three_actor_visuals_and_physics","one Rigidbody/one capsule per player");
   for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,Vector3.left,false);yield return Steps(30);
   for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,Vector3.zero,false);yield return Steps(10);
   var actor=lab.Players[0];var b=BoundsOf(actor);
   Check(Mathf.Abs(b.size.y-1.7f)<.01f&&Mathf.Abs(b.min.y-actor.body.transform.position.y)<.01f,"standing_feet_height","height="+b.size.y+" feet="+b.min.y);
   Check(Vector3.Dot(actor.visual.forward,Vector3.left)>.95f,"visual_faces_movement_without_body_rotation",actor.visual.forward.ToString());
   lab.SetExternalView(0,90,18);yield return Steps(15);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/character-visual-standing.png");yield return null;
   for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,Vector3.zero,false,false,true);yield return Steps(25);yield return null;b=BoundsOf(actor);
   Check(Mathf.Abs(b.size.y-lab.baseline.crouchingHeight)<.01f&&Mathf.Abs(b.min.y-actor.body.transform.position.y)<.01f,"crouch_visual_tracks_capsule","height="+b.size.y);
   for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,Vector3.zero,false);yield return Steps(25);float start=actor.body.position.y;lab.SetInput(0,Vector3.zero,false,true);float peak=0;
   for(int i=0;i<50;i++){yield return Steps(1);peak=Mathf.Max(peak,actor.body.position.y-start);}
   Check(peak>.6f&&peak<.72f,"imported_actor_free_jump",peak.ToString());
   foreach(int count in new[]{2,3})
   {
    lab.DynamicRoster=true;lab.SessionTeamSize=count;lab.ConfigureSessionRoster(new[]{true,true,count==3,false},false);yield return Steps(2);
    Check(lab.Players.Count(a=>a.active&&a.body&&a.presentation)==count,"proxy_roster_"+count,"visuals follow actual roster");
    var a=lab.Players[0];a.height=1.1f;lab.UpdateActorVisual(a);yield return null;
    Check(Mathf.Abs(BoundsOf(a).size.y-1.1f)<.01f,"proxy_height_"+count,"same sizing path used by received snapshot");
   }
   s.ReturnToLobby();Last.complete=true;Last.passed=Last.checks.All(c=>c.passed);File.WriteAllText("Logs/character-visual-checks.json",JsonUtility.ToJson(Last,true));
  }
 }
}
