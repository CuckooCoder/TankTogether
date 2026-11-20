using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace TankTogether.M0 {
// Experimental B: bounded height actuation; existing hard grip limit is the passive safety constraint.
public sealed class HeightExperiment : MonoBehaviour {
 public bool Active { get; private set; }
 public float minHeight=1.1f,maxHeight=2.3f,growRate=.3f,shrinkRate=.3f;
 public int Direction { get; private set; }
 public string BlockReason { get; private set; }="无";
 public bool ManualInput;
 public bool InLevel { get; private set; }
 public bool PhysicsEnabled => InLevel;
 public bool NetworkMode;
 public float gripStiffness=3000,gripDamping=100,gripMaxForce=240,heightCompliance=.08f;
 public readonly string[] Reasons=new string[4];
 public readonly float[] Forces=new float[4];
 M0CarryLab lab; GameObject room; M0CarryLab.Actor arranged; bool released;
 public void Begin(M0CarryLab value, bool inLevel=false){lab=value;lab.Stretch=this;ApplyConfiguration(lab.baseline);Active=true;InLevel=inLevel;lab.ChangeMode(0);ResetInput();if(!InLevel)BuildRoom();Arrange();}
 public void ResetInput(){Direction=0;released=false;}
 public static int Combine(bool grow,bool shrink)=>(grow?1:0)-(shrink?1:0);
 public void SetDirection(int direction){Direction=Math.Sign(direction);if(lab!=null){lab.Players[lab.Selected].stretchInput=Direction;lab.Players[lab.Selected].stretchArmed=true;}}
 public void ReadKeys(Keyboard k){if(ManualInput)return;if(!TankTogether.Networking.PlayerPreferences.Held(TankTogether.Networking.PlayerAction.Grow)&&!TankTogether.Networking.PlayerPreferences.Held(TankTogether.Networking.PlayerAction.Shrink))released=true;Direction=released?Combine(TankTogether.Networking.PlayerPreferences.Held(TankTogether.Networking.PlayerAction.Grow),TankTogether.Networking.PlayerPreferences.Held(TankTogether.Networking.PlayerAction.Shrink)):0;}
 void BuildRoom(){room=new GameObject("B0 height sandbox");Block("安全平台",new Vector3(1000,-.5f,0),new Vector3(24,1,24));Block("低顶：底面1.95米",new Vector3(1004,2.1f,0),new Vector3(3,.3f,4));Block("侧墙",new Vector3(994,2,0),new Vector3(.3f,4,8));}
 void Block(string name,Vector3 pos,Vector3 size){var g=GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(room.transform);g.transform.position=pos;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=lab.SafeSupportMaterial;}
 bool HasReadyActor => lab && lab.Players != null && lab.Selected >= 0 && lab.Selected < lab.Players.Length && lab.Players[lab.Selected] != null && lab.Players[lab.Selected].body;
 void Arrange(){if(!HasReadyActor || lab.Players[0] == null || !lab.Players[0].body)return;arranged=lab.Players[0];if(InLevel){ResetInput();return;}lab.PlacePlayer(0,new Vector3(1000,.02f,0));lab.SetExternalView(0,90,18);for(int i=1;i<lab.LocalPlayerCount;i++){lab.Players[i].active=false;if(lab.Players[i].body)lab.Players[i].body.gameObject.SetActive(false);}lab.Tank.isKinematic=true;lab.Tank.position=new Vector3(1100,10,0);ResetInput();}
 public void ApplyConfiguration(M0CarryBaseline b){minHeight=b.stretchMinHeight;maxHeight=Mathf.Min(b.stretchMaxHeight,M0CarryLab.MaximumPlayerHeight);growRate=b.stretchGrowSpeed;shrinkRate=b.stretchShrinkSpeed;gripStiffness=b.stretchGripStiffness;gripDamping=b.stretchGripDamping;gripMaxForce=b.stretchGripMaxForce;heightCompliance=b.stretchCompliance;}
 void Update(){if(!Active || !HasReadyActor)return;if(arranged!=lab.Players[0])Arrange();if(NetworkMode)return;if(!ManualInput&&(lab.GMOpen||Cursor.lockState!=CursorLockMode.Locked))Direction=0;}
 public float Step(M0CarryLab.Actor a,float dt,float stepCeiling){
  if(!Active)return a.height;
  int direction=a.stretchInput;
  float desired=Mathf.Clamp(a.height+direction*(direction>0?growRate:shrinkRate)*dt,minHeight,maxHeight);
  float next=Mathf.MoveTowards(a.height,desired,(desired>a.height?growRate:shrinkRate)*dt);BlockReason="无";
  if(direction!=0&&Mathf.Abs(next-a.height)<.00001f)BlockReason="高度极限";
  if(PhysicsEnabled && lab.Tank){
   Vector3 head=a.body.position+Vector3.up*a.height;
   Vector3 local=lab.Tank.transform.InverseTransformPoint(head);
   bool under=Mathf.Abs(local.x)<lab.tankSize.x/2 && Mathf.Abs(local.z)<lab.tankSize.z/2 && lab.Tank.transform.up.y>.3f;
   Vector3 target=lab.Tank.transform.TransformPoint(a.joint?a.anchor:new Vector3(local.x,-lab.tankSize.y/2,local.z));
   bool contact = under && Mathf.Abs(head.y-target.y)<.25f;
   if(!a.joint && lab.baseline.headSupportMode >= .5f){
    contact=FlatHeadSupport.LowestContact(lab,a,out float bottomY);
    if(contact)target.y=bottomY;
   }
   if(a.joint || contact){
    float error=a.body.position.y+next-target.y;
    // Bound only ACTIVE height changes. Never overwrite a passive body's position or velocity.
    if(next>a.height && error>heightCompliance){next=Mathf.Max(a.height,target.y+heightCompliance-a.body.position.y);BlockReason="上顶受力受限";}
    if(a.joint && next<a.height && error< -heightCompliance){next=Mathf.Min(a.height,target.y-heightCompliance-a.body.position.y);BlockReason="下拉受力受限";}
   }
  }
  if(next>a.height){
   float radius=a.capsule.radius;Vector3 top=a.body.position+Vector3.up*(a.height-radius);
   float allowed=next-a.height;
   foreach(var hit in Physics.SphereCastAll(top,radius,Vector3.up,allowed+.006f,~0,QueryTriggerInteraction.Ignore)){
    if(hit.collider.attachedRigidbody==a.body || PhysicsEnabled && hit.collider.attachedRigidbody==lab.Tank)continue;
    allowed=Mathf.Min(allowed,Mathf.Max(0,hit.distance-.006f));BlockReason="顶部受阻："+hit.collider.name;
   }
   next=a.height+allowed;
  }
  if(next>stepCeiling){next=stepCeiling;BlockReason="倾倒承托高度上限";}
  int index=Array.IndexOf(lab.Players,a);if(index>=0)Reasons[index]=BlockReason;
  return next;
 }
 public void ApplyGripForce(M0CarryLab.Actor a){
  int i=Array.IndexOf(lab.Players,a);if(i>=0)Forces[i]=0;
  if(!a.joint || !lab.Tank)return;
  Vector3 target=lab.Tank.transform.TransformPoint(a.anchor),head=a.body.position+Vector3.up*a.height;
  float relative=a.body.GetPointVelocity(head).y-lab.Tank.GetPointVelocity(target).y;
  float force=Mathf.Clamp((head.y-target.y)*gripStiffness+relative*gripDamping,-gripMaxForce,gripMaxForce);
  lab.Tank.AddForceAtPosition(Vector3.up*force,target);a.body.AddForce(Vector3.down*force);
  a.support=force>1;if(i>=0)Forces[i]=force;
 }
 public void BeginNetwork(M0CarryLab value){NetworkMode=true;Begin(value,true);}
 public void End(){if(!Active)return;Active=false;NetworkMode=false;Direction=0;ManualInput=false;arranged=null;if(room){room.SetActive(false);Destroy(room);}if(lab&&lab.Tank)lab.Tank.isKinematic=false;}
 void OnDestroy(){End();}
 void OnGUI(){if(!Active || !HasReadyActor)return;var session=GetComponent<TankTogether.Networking.SteamCarrySession>();if(lab.GMOpen || session&&session.MenuOverlayVisible)return;var old=GUI.matrix;float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);GUI.matrix=Matrix4x4.Scale(Vector3.one*scale);
  GUI.Box(new Rect(20,218,380,92),"");GUI.Label(new Rect(30,225,360,25),InLevel?"B 伸缩受力实验（待多人实机验收）":"B0 单人伸缩沙盒（鱼缸/联网未接入）");GUI.Label(new Rect(30,250,360,25),$"身高 {lab.Players[lab.Selected].height:F3} m · 输入 {lab.Players[lab.Selected].stretchInput} · {Reasons[lab.Selected]}");GUI.Label(new Rect(30,275,360,25),TankTogether.Networking.PlayerPreferences.Binding(TankTogether.Networking.PlayerAction.Shrink)+" 变矮 / "+TankTogether.Networking.PlayerPreferences.Binding(TankTogether.Networking.PlayerAction.Grow)+" 长高 / F2 伸缩参数");
  GUI.matrix=old;
 }
}
}
