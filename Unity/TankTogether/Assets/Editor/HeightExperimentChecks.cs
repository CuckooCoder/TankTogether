using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
public static class HeightExperimentChecks {
 public static string Result="";
 public static bool HasLocalActors(M0CarryLab lab,int visible){
  if(lab.Players.Length<visible)return false;
  for(int i=0;i<lab.Players.Length;i++){
   var a=lab.Players[i];
   if(i<visible){
    if(a==null||!a.active||!a.body||!a.body.gameObject.activeInHierarchy||!a.capsule||!a.capsule.enabled||
       !a.visual||!a.visual.gameObject.activeInHierarchy||!a.visual.GetComponentInChildren<Renderer>())return false;
   }else if(a!=null&&(a.active||(a.body&&a.body.gameObject.activeInHierarchy)||(i>=3&&a.body)))return false;
  }
  return true;
 }

 public static string CheckLocalLineup(){
  var s=Object.FindFirstObjectByType<SteamCarrySession>();
  if(!Application.isPlaying||!s||s.Mode!=SteamCarrySession.EntryMode.Home||!s.CanChooseMode)
   throw new System.InvalidOperationException("Start Play at the idle home menu.");
  var lab=s.lab;
  var previous=Keyboard.current;
  var previousBindings=PlayerPreferences.Data.keys;
  var settings=InputSystem.settings;
  var background=settings.backgroundBehavior;
  var editorRouting=settings.editorInputBehaviorInPlayMode;
  Keyboard keys=null;
  var update=typeof(M0CarryLab).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic);
  var passed=new List<string>();
  void Check(string name,bool ok){
   if(!ok)throw new System.InvalidOperationException("Local lineup check failed: "+name);
   passed.Add(name);
  }
  void Press(params Key[] held){
   InputSystem.QueueStateEvent(keys,new KeyboardState());InputSystem.Update();
   InputSystem.QueueStateEvent(keys,new KeyboardState(held));InputSystem.Update();
   update.Invoke(lab,null);
  }
  try{
   // Route the synthetic keyboard through the player update even when the Game View is unfocused.
   settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
   settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
   keys=InputSystem.AddDevice<Keyboard>();keys.MakeCurrent();
   PlayerPreferences.Data.keys=PlayerPreferenceData.DefaultKeys();
   PlayerPreferences.Data.keys[(int)PlayerAction.Grab]=(int)Key.G;
   Check("Stretch B menu entry",s.StartLocalStretchLevel()&&lab.Stretch.Active&&lab.Stretch.InLevel);
   Check("P1-P3 models and bodies",lab.LocalPlayerCount==3&&HasLocalActors(lab,3));
   Vector3 LocalFeet(int slot)=>Vector3.ProjectOnPlane(lab.Tank.transform.InverseTransformPoint(lab.Players[slot].body.position),Vector3.up);
   bool AtOwnEdge(int slot)=>Vector3.Distance(LocalFeet(slot),lab.CarryFormationOffset(slot))<.04f;
   bool UnderTank(){
    for(int slot=0;slot<3;slot++){
     var head=lab.Tank.transform.InverseTransformPoint(lab.Head(slot));
     if(Mathf.Abs(head.x)>lab.tankSize.x*.5f||Mathf.Abs(head.z)>lab.tankSize.z*.5f||
        Mathf.Abs(head.y+lab.tankSize.y*.5f)>.04f)return false;
    }
    return true;
   }
   Check("all three spawn beneath the tank",UnderTank());
   for(int i=1;i<=3;i++){Press(Key.Tab);Check("Tab press "+i,lab.Selected==i%3);}
   var oldTank=lab.Tank;
   Press(Key.R);Check("R recreates three actors beneath the tank",lab.Tank!=oldTank&&HasLocalActors(lab,3)&&UnderTank());
   Press(Key.Enter);Press(Key.LeftShift,Key.F4);
   Check("Shift F4 moves three to the left, right and rear pickup edges",HasLocalActors(lab,3)&&AtOwnEdge(0)&&AtOwnEdge(1)&&AtOwnEdge(2));
   var leftFeet=lab.Players[0].body.position;var rightFeet=lab.Players[1].body.position;var rearFeet=lab.Players[2].body.position;
   var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);
   try{
    obstacle.transform.SetPositionAndRotation(leftFeet+Vector3.up*.65f,lab.Tank.rotation);
    obstacle.transform.localScale=new Vector3(.45f,1.15f,.36f);
    lab.PlacePlayer(0,leftFeet+Vector3.left*8);
    Press(Key.F4);
    Vector3 left=LocalFeet(0),assigned=lab.CarryFormationOffset(0);
    Check("single F4 finds another clear point on the same edge",Mathf.Abs(left.x-assigned.x)<.04f&&Mathf.Abs(left.z-assigned.z)>.4f&&
     lab.Players[1].body.position==rightFeet&&lab.Players[2].body.position==rearFeet);
   }finally{obstacle.SetActive(false);Object.Destroy(obstacle);}
   Press(Key.F4);Check("single F4 recovers the assigned edge",AtOwnEdge(0));
   var p1=lab.Players[0];var p2=lab.Players[1];var p3=lab.Players[2];
   Press(Key.Digit1);Press(Key.Digit5,Key.D);
   Check("5 holds both teammates without grabbing for P1",lab.Mode==4&&p1.move.sqrMagnitude>.9f&&p2.move==Vector3.zero&&p3.move==Vector3.zero&&!lab.IsGripping(0)&&lab.IsGripping(1)&&lab.IsGripping(2));
   var secondGrip=p2.joint;var thirdGrip=p3.joint;
   Press(Key.D,Key.G,Key.LeftShift,Key.Space,Key.E);
   Check("P1 still grabs, moves, sprints, jumps and stretches",lab.IsGripping(0)&&p1.move.sqrMagnitude>.9f&&p1.sprint&&p1.jump&&p1.stretchInput==1);
   Check("teammates receive no active inputs",p2.move==Vector3.zero&&p3.move==Vector3.zero&&!p2.sprint&&!p3.sprint&&!p2.jump&&!p3.jump&&p2.stretchInput==0&&p3.stretchInput==0);
   var firstGrip=p1.joint;
   Press(Key.Digit5,Key.D,Key.G);
   Check("pressing 5 again preserves every grip and leaves teammates idle",p1.joint==firstGrip&&p2.joint==secondGrip&&p3.joint==thirdGrip&&p1.move.sqrMagnitude>.9f&&p2.move==Vector3.zero&&p3.move==Vector3.zero);
   Press();Check("releasing the grip key releases only P1",!lab.IsGripping(0)&&p2.joint==secondGrip&&p3.joint==thirdGrip);
   Press(Key.Tab,Key.D);
   Check("Tab gives P2 independent control while P1 takes over the grip",lab.Selected==1&&lab.IsGripping(0)&&!lab.IsGripping(1)&&p3.joint==thirdGrip&&p1.move==Vector3.zero&&p2.move.sqrMagnitude>.9f&&p3.move==Vector3.zero);
   PlayerPreferences.Data.keys[(int)PlayerAction.Grab]=(int)Key.Digit5;
   Press(Key.Digit1);Press(Key.Digit5);Check("custom Digit5 binding takes priority",lab.Mode==0&&lab.IsGripping(1)&&!lab.IsGripping(0)&&!lab.IsGripping(2));
   secondGrip=p2.joint;
   Press(Key.LeftShift,Key.Digit4,Key.Digit5,Key.D);
   Check("Shift+4 enables idle teammates and preserves the selected grip",lab.Mode==4&&p2.joint==secondGrip&&lab.IsGripping(0)&&lab.IsGripping(2)&&p1.move==Vector3.zero&&p2.move.sqrMagnitude>.9f&&p3.move==Vector3.zero);
   s.ReturnToLobby();
   Check("A menu creates three",s.StartLocalTest()&&HasLocalActors(lab,3));
   s.ReturnToLobby();
   Check("B0 keeps one visible actor",s.StartLocalStretchLevel(false)&&lab.Stretch.Active&&!lab.Stretch.InLevel&&HasLocalActors(lab,1));
   s.ReturnToLobby();
   Check("B re-entry restores three",s.StartLocalStretchLevel()&&HasLocalActors(lab,3));
   return string.Join("\n",passed);
  }finally{
   PlayerPreferences.Data.keys=previousBindings;
   if(keys!=null)InputSystem.RemoveDevice(keys);
   settings.editorInputBehaviorInPlayMode=editorRouting;
   settings.backgroundBehavior=background;
   if(previous!=null)previous.MakeCurrent();
   s.ReturnToLobby();
  }
 }
 static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
 public static void Begin(){var s=Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Run(s));}
 static IEnumerator Run(SteamCarrySession s){
 s.StartLocalTest();var l=s.lab;l.Testing=true;var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();e.Begin(l);e.ManualInput=true;
 var rows=new List<string>();bool pass=true;System.Action<string,bool,float> check=(n,b,v)=>{rows.Add(n+"="+b+" value="+v);pass&=b;};
 check("bothCancel",HeightExperiment.Combine(true,true)==0,HeightExperiment.Combine(true,true));
 yield return Wait(25);var a=l.Players[0];float radius=a.capsule.radius,mass=a.body.mass;
 e.SetDirection(1);yield return Wait(Mathf.RoundToInt(2/Time.fixedDeltaTime));check("grow2sec",Mathf.Abs(a.height-2.3f)<.015f,a.height);
 yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/height-b0-tall.png");
 e.SetDirection(0);float saved=a.height;yield return Wait(25);check("retain",Mathf.Abs(a.height-saved)<.001f,a.height);
 e.SetDirection(-1);yield return Wait(Mathf.RoundToInt(4.2f/Time.fixedDeltaTime));check("shrink",Mathf.Abs(a.height-1.1f)<.01f,a.height);
 yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/height-b0-short.png");
 check("widthMass",a.capsule.radius==radius&&a.body.mass==mass,a.capsule.radius);
 e.SetDirection(0);l.SetInput(0,Vector3.zero,false,false,true);yield return Wait(15);check("ctrlDisabled",Mathf.Abs(a.height-1.1f)<.001f,a.height);
 l.PlacePlayer(0,new Vector3(1004,.02f,0));yield return Wait(25);e.SetDirection(1);yield return Wait(120);check("ceiling",a.height>1.8f&&a.height<1.96f,a.height);check("feetStable",Mathf.Abs(a.body.position.y)<.05f,a.body.position.y);
 e.SetDirection(0);saved=a.height;l.SetInput(0,Vector3.forward,false);yield return Wait(60);l.SetInput(0,Vector3.zero,false);yield return Wait(15);check("noQueuedGrowth",Mathf.Abs(a.height-saved)<.001f,a.height);
 l.PlacePlayer(0,new Vector3(1000,.02f,0));yield return Wait(25);e.SetDirection(1);l.SetInput(0,Vector3.zero,false,true,false,false,1);float peak=0;for(int i=0;i<70;i++){yield return new WaitForFixedUpdate();peak=Mathf.Max(peak,a.body.position.y);}check("airNoExtraJump",peak>.55f&&peak<.75f,peak);check("airHeightChanged",a.height>1.7f,a.height);
 l.PlacePlayer(0,new Vector3(1000,.02f,0));check("respawnDefault",Mathf.Abs(a.height-1.7f)<.001f,a.height);check("respawnInputCleared",e.Direction==0,e.Direction);
 s.ReturnToLobby();s.StartLocalTest();l.Testing=true;l.SetInput(0,Vector3.zero,false,false,true);yield return Wait(30);check("AStillCrouches",Mathf.Abs(l.Players[0].height-l.baseline.crouchingHeight)<.01f,l.Players[0].height);
 Result=string.Join("\n",rows)+"\npassed="+pass;File.WriteAllText("Logs/height-b0-checks.txt",Result);s.ReturnToLobby();
 }
}
}
