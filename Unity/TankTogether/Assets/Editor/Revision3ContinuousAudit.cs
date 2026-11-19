using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;

namespace TankTogether.Editor {
// Test input driver only. No transform, velocity, gravity, time scale or checkpoint writes.
public static class Revision3ContinuousAudit {
 [Serializable] public class Snapshot {
  public float t,tilt,lift; public Vector3 tank; public Vector3[] feet,velocity,input;
  public bool[] grounded,grips; public bool broken; public string checkpoint,message;
 }
 [Serializable] public class Command {
  public string name,result;public Vector3[] targets; public float seconds,jumpX,jumpAfter;
  public bool hold,sprint;public int crouchMask;public float turn;public int mask;public Snapshot start,end;
 }
 [Serializable] public class Report {
  public string id,utc,baseline,conclusion,scope="Normal lobby LocalTest spawn; SetInput and camera only. Natural game respawns allowed and recorded. No teleport, checkpoint selection, physics override or mid-run reset.";
  public List<Command> commands=new List<Command>(); public List<Snapshot> samples=new List<Snapshot>();
 }
 static M0CarryLab lab;static WhiteboxCarryLevel route;static WhiteboxLift lift;
 static Report report;static string file;static float started;static bool active,busy;static bool holding;static int crouchHolding;
 public static bool Busy=>busy;
 public static string Status=>JsonUtility.ToJson(Read(),true);
 static Snapshot Read(){
  if(!lab)return null;var s=new Snapshot {t=Time.time-started,tank=lab.Tank.position,tilt=Vector3.Angle(lab.Tank.transform.up,Vector3.up),lift=lift?lift.Distance:0,checkpoint=route.CheckpointId,broken=lab.Broken,message=lab.Message,feet=new Vector3[4],velocity=new Vector3[4],input=new Vector3[4],grounded=new bool[4],grips=new bool[4]};
  for(int i=0;i<4;i++){var a=lab.Players[i];s.feet[i]=a.body.position;s.velocity[i]=a.body.linearVelocity;s.input[i]=a.move;s.grounded[i]=Physics.Raycast(a.body.position+Vector3.up*.08f,Vector3.down,.16f,~0,QueryTriggerInteraction.Ignore);s.grips[i]=lab.IsGripping(i);}return s;
 }
 static void Flush(){File.WriteAllText(file,JsonUtility.ToJson(report,true));}
 static void Idle(){for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,holding,false,(crouchHolding&(1<<i))!=0);}
 public static void Begin(string id){
  if(active||!EditorApplication.isPlaying)throw new Exception("Need new play session");
  var session=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
  if(!session.StartLocalTest())throw new Exception("Cannot enter local test");
  lab=session.lab;route=lab.GetComponent<WhiteboxCarryLevel>();lift=lab.GetComponent<WhiteboxLift>();
  if(route.levelId!="whitebox-route-v3"||Time.timeScale!=1)throw new Exception("Wrong map or time scale");
  lab.Testing=true;started=Time.time;active=true;holding=false;
  report=new Report{id=id,utc=DateTime.UtcNow.ToString("O"),baseline=lab.baseline.Signature};file="Logs/revision3-continuous-"+id+".json";
  EditorApplication.ExecuteMenuItem("Window/General/Game");lab.StartCoroutine(Monitor());Flush();
 }
 static IEnumerator Monitor(){int tick=0;while(active&&lab){yield return new WaitForFixedUpdate();if(++tick%5==0)report.samples.Add(Read());if(tick%250==0)Flush();}}
 public static void Go(string name,Vector3[] targets,float seconds=10,bool hold=false,bool sprint=false,float jumpX=999,float jumpAfter=-1,int mask=15){
  if(!active||busy||lab.Broken||targets.Length!=4)throw new Exception("Need idle live run and four targets");busy=true;
  lab.StartCoroutine(Moving(new Command{name=name,targets=targets,seconds=seconds,hold=hold,sprint=sprint,jumpX=jumpX,jumpAfter=jumpAfter,mask=mask,start=Read()}));
 }
 static IEnumerator Moving(Command c){
  report.commands.Add(c);holding=c.hold;crouchHolding=0;bool[] jumped=new bool[4];bool[] reached=new bool[4];
  try {
   for(int n=0;n<c.seconds/Time.fixedDeltaTime;n++){
    bool all=true;
    for(int i=0;i<4;i++){
     if((c.mask&(1<<i))==0){lab.SetInput(i,Vector3.zero,c.hold);continue;}
     var a=lab.Players[i];var d=Vector3.ProjectOnPlane(c.targets[i]-a.body.position,Vector3.up);
     reached[i]=d.magnitude<.12f;all&=reached[i];
     bool jump=!jumped[i]&&((c.jumpX!=999&&a.body.position.x<=c.jumpX)||(c.jumpAfter>=0&&n*Time.fixedDeltaTime>=c.jumpAfter));
     if(jump)jumped[i]=true;
     lab.SetInput(i,reached[i]?Vector3.zero:d.normalized*Mathf.Min(1,d.magnitude*1.5f),c.hold,jump,false,c.sprint);
    }
    int selected=0;while((c.mask&(1<<selected))==0&&selected<3)selected++;
    var direction=c.targets[selected]-lab.Players[selected].body.position;
    if(direction.sqrMagnitude>.2f)lab.SetExternalView(selected,Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,28);
    yield return new WaitForFixedUpdate();
    if(lab.Broken){c.result="tank_broken";break;}
    if(all){c.result="xz_targets_reached";break;}
   }
   Idle();for(int i=0;i<40;i++)yield return new WaitForFixedUpdate();
   c.end=Read();if(c.result==null)c.result="timeout_or_blocked";Flush();
   yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Logs/revision3-continuous-"+report.id+"-"+c.name+".png",image.EncodeToPNG());UnityEngine.Object.Destroy(image);
  }finally{Idle();busy=false;Flush();}
 }

 public static void ReplayFromStartToTank(string id){Begin(id);lab.StartCoroutine(WalkAllToTank());}
 static IEnumerator WalkAllToTank(){
  // Each actor walks the entire route. Sequential traversal avoids conflating teammate collision with terrain access.
  for(int i=0;i<4;i++){
   var points=new[]{
    new Vector3(-4,0,0),new Vector3(-22,-.5f,-1.3f),new Vector3(-31,-.5f,-1.3f),new Vector3(-43,-.5f,-1.3f),
    new Vector3(-49,-.5f,-1.3f),new Vector3(-53,-.5f,1),new Vector3(-58,-.5f,1),new Vector3(-67,-2.5f,1),
    new Vector3(-87,-12.5f,-5),new Vector3(-96,-15.5f,-12),new Vector3(-103,-18.5f,-17),
    new Vector3(-108,-20,-26),new Vector3(-119,-21.5f,-32),new Vector3(-133,-23,-34),new Vector3(-140,-27.5f,-25),
    new Vector3(-136,-27.5f,-19),new Vector3(-135.6f,-27.5f,-17.7f),new Vector3(-131,-30.5f,-12),
    new Vector3(-136.5f,-30.5f,-7),new Vector3(-137.5f,-30.5f,-2.9f),new Vector3(-137.5f,-30.38f,0),
    new Vector3(-131,-29.5f,0),new Vector3(i%2==0?-130:-129,-29.5f,i<2?-2:2)};
   float[] jumpX={999,999,-24.6f,-37.5f,999,999,999,-60.5f,999,999,999,-104.8f,-111.2f,-124.5f,-135.8f,999,999,999,999,999,999,999,999};
   float[] jumpAfter={-1,-1,-1,-1,-1,.1f,-1,-1,-1,-1,-1,-1,-1,-1,-1,.1f,-1,.1f,-1,-1,-1,-1,-1};
   for(int n=0;n<points.Length;n++){
    var targets=new Vector3[4];targets[i]=points[n];
    bool sprint=n==1||jumpX[n]!=999||jumpAfter[n]>=0;
    Go("walk_p"+(i+1)+"_"+n,targets,10,false,sprint,jumpX[n],jumpAfter[n],1<<i);
    while(busy)yield return new WaitForFixedUpdate();
    var feet=lab.Players[i].body.position;
    if(lab.Broken||Vector3.ProjectOnPlane(feet-points[n],Vector3.up).magnitude>(n==7||n==8?3f:1.2f)||feet.y<points[n].y-1.5f){
     report.conclusion="Walk halted at player "+(i+1)+" step "+n+"; inspect actual trajectory, no skip/placement.";Flush();yield break;
    }
   }
  }
  report.conclusion="All four walked from initial spawn to repaired tank dais; carry section remains.";Flush();
 }
 public static void Carry(string name,Vector3 target,float seconds=12,bool sprint=false,float jumpFrontX=999,float jumpAfter=-1,float turn=0,bool crouch=false,int crouchMask=0){
  if(!active||busy||lab.Broken)throw new Exception("Need idle live run");busy=true;holding=true;crouchHolding=crouch?15:crouchMask;
  lab.StartCoroutine(Carrying(name,target,seconds,sprint,jumpFrontX,jumpAfter,turn,crouch));
 }
 static IEnumerator Carrying(string name,Vector3 target,float seconds,bool sprint,float jumpFrontX,float jumpAfter,float turn,bool crouch){
  var c=new Command{name=name,targets=new[]{target},seconds=seconds,sprint=sprint,hold=true,jumpX=jumpFrontX,jumpAfter=jumpAfter,crouchMask=crouchHolding,turn=turn,start=Read()};report.commands.Add(c);bool jumped=false;
  try{
   for(int n=0;n<seconds/Time.fixedDeltaTime;n++){
    var delta=Vector3.ProjectOnPlane(target-lab.Tank.position,Vector3.up);float distance=delta.magnitude;
    if(distance<.16f&&n>2){c.result="tank_xz_target_reached";break;}
    float front=float.MaxValue;for(int i=0;i<4;i++)front=Mathf.Min(front,lab.Players[i].body.position.x);
    bool jump=!jumped&&((jumpFrontX!=999&&front<=jumpFrontX)||(jumpAfter>=0&&n*Time.fixedDeltaTime>=jumpAfter));if(jump)jumped=true;
    var movement=delta.normalized*Mathf.Min(1,distance*.8f);
    for(int i=0;i<4;i++){
     var radial=Vector3.ProjectOnPlane(lab.Players[i].body.position-lab.Tank.position,Vector3.up).normalized;
     lab.SetInput(i,Vector3.ClampMagnitude(movement+Vector3.Cross(Vector3.up,radial)*turn,1),true,jump,(crouchHolding&(1<<i))!=0,sprint);
    }
    lab.SetExternalView(0,Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,28);
    yield return new WaitForFixedUpdate();if(lab.Broken){c.result="tank_broken";break;}
   }
   Idle();for(int i=0;i<40;i++)yield return new WaitForFixedUpdate();c.end=Read();if(c.result==null)c.result="timeout_or_blocked";Flush();
   yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes("Logs/revision3-continuous-"+report.id+"-"+name+".png",image.EncodeToPNG());UnityEngine.Object.Destroy(image);
  }finally{Idle();busy=false;Flush();}
 }

 public static void Finish(string conclusion){if(busy)throw new Exception("Wait for command");Idle();report.samples.Add(Read());report.conclusion=conclusion;active=false;Flush();lab.Testing=false;}
}
}
