using System.Collections;
using System.Linq;
using System.IO;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor
{
 public static class CharacterMotionChecks
 {
  public static string Result="";
  static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
  static Transform Bone(M0CarryLab.Actor a,string name){return a.visual.GetComponentsInChildren<Transform>().First(t=>t.name==name);}
  public static void Begin(){var s=Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
  static IEnumerator Run(SteamCarrySession s)
  {
   s.StartLocalTest();var l=s.lab;l.Testing=true;
   var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Animation isolated test floor";floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(50,1,50);
   l.PlacePlayer(0,new Vector3(1000,.02f,0));l.SetExternalView(0,150,15);yield return Wait(20);
   var a=l.Players[0];var thigh=Bone(a,"Thigh.L");var rest=thigh.localRotation;
   var presentation=a.visual.GetComponent<CarryActorVisual>();float contact=presentation.FootContactWeight;
   l.SetInput(0,Vector3.forward,false);yield return Wait(18);yield return null;
   float walking=Quaternion.Angle(rest,thigh.localRotation);
   yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/character-motion-walk.png");
   l.SetInput(0,Vector3.right,false);yield return Wait(8);yield return null;
   yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/character-motion-turn.png");
   l.SetInput(0,Vector3.zero,false);yield return Wait(8);yield return null;
   yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/character-motion-stop.png");
   yield return Wait(35);
   l.SetInput(0,Vector3.zero,false,true);yield return Wait(10);yield return null;
   float airborne=Quaternion.Angle(rest,thigh.localRotation);float airContact=presentation.FootContactWeight;
   yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/character-motion-jump.png");
   yield return Wait(50);
   // Isolated grasp fixture, not route evidence: static tank at normal standing head height.
   l.Tank.isKinematic=true;l.Tank.position=a.body.position+Vector3.up*(a.height+l.tankSize.y/2);l.Tank.rotation=Quaternion.identity;
   yield return Wait(3);l.SetInput(0,Vector3.zero,true);yield return Wait(25);yield return null;
   var hand=Bone(a,"Hand.L");var shoulder=Bone(a,"UpperArm.L");
   bool lifted=hand.position.y>shoulder.position.y;
   yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/character-motion-grip.png");
   l.Tank.rotation=Quaternion.Euler(0,0,9);yield return Wait(20);yield return null;
   float effort=presentation.CarryLean.magnitude;
   yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/character-motion-effort.png");
   Result="contact="+contact+"; airborneContact="+airContact+"; carryLean="+effort+"; scope=isolated-local-animation; walkAngle="+walking+"; airAngle="+airborne+"; grip="+(bool)a.joint+"; handAboveShoulder="+lifted+"; enabledCameras="+Camera.allCamerasCount+"; passed="+(walking>2&&airborne>5&&a.joint&&lifted&&Camera.allCamerasCount==1&&contact>.5f&&airContact<.01f&&effort>.25f&&effort<13);
   File.WriteAllText("Logs/character-motion-checks.txt",Result);
   l.SetInput(0,Vector3.zero,false);l.Tank.isKinematic=false;Object.Destroy(floor);s.ReturnToLobby();
  }
 }
}

