using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
public static class StretchForceChecks {
 public static string Result=""; static M0CarryLab l;static HeightExperiment e;static List<string> rows;static bool pass;
 static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
 static void Check(string name,bool ok,string detail){pass&=ok;rows.Add(name+"="+ok+" "+detail);}
 static void Fixture(bool grips){
 l.Tank.isKinematic=false;l.Tank.position=new Vector3(1000,1.7f+l.tankSize.y/2,0);l.Tank.rotation=Quaternion.identity;l.Tank.transform.SetPositionAndRotation(l.Tank.position,Quaternion.identity);Physics.SyncTransforms();l.Tank.linearVelocity=l.Tank.angularVelocity=Vector3.zero;
 for(int i=0;i<l.LocalPlayerCount;i++){l.PlacePlayer(i,new Vector3(1000+(i==2?0:i==0?-.9f:.9f),.02f,i<2?-.55f:.55f));l.SetInput(i,Vector3.zero,false);if(grips){l.SetInput(i,Vector3.zero,true);Check("fixtureGrip"+i,l.Players[i].joint,"feedback="+l.Players[i].feedback);}}
 Physics.SyncTransforms();
 }
 public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Run(s));}
 static IEnumerator Run(SteamCarrySession s){
 pass=true;rows=new List<string>();s.StartLocalStretchLevel();s.enabled=false;l=s.lab;l.Testing=true;e=l.Stretch;e.ManualInput=true;
 var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(30,1,30);
 Fixture(true);yield return Wait(40);float before=l.Tank.position.y;
 for(int i=0;i<l.LocalPlayerCount;i++)l.SetInput(i,Vector3.zero,true,false,false,false,1);
 yield return Wait(130);float up=l.Tank.position.y-before;
 Check("threeGrow",up>.15f&&!l.Broken,"tankDelta="+up+" height="+l.Players[0].height);
 yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/stretch-three-grow.png");
 for(int i=0;i<l.LocalPlayerCount;i++)l.SetInput(i,Vector3.zero,true,false,false,false,-1);
 before=l.Tank.position.y;yield return Wait(90);Check("threeShrink",l.Tank.position.y<before-.1f,"delta="+(l.Tank.position.y-before));
 Fixture(true);yield return Wait(40);float speed=0,error=0;
 for(int i=0;i<l.LocalPlayerCount;i++)l.SetInput(i,Vector3.zero,true,false,false,false,i%2==0?1:-1);
 for(int n=0;n<200;n++){yield return new WaitForFixedUpdate();speed=Mathf.Max(speed,l.Tank.linearVelocity.magnitude);for(int i=0;i<l.LocalPlayerCount;i++)error=Mathf.Max(error,Vector3.Distance(l.Players[i].body.position+Vector3.up*l.Players[i].height,l.Tank.transform.TransformPoint(l.Players[i].anchor)));}
 Check("opposedFinite",speed<8&&error<.3f&&!l.Broken,"maxSpeed="+speed+" maxError="+error);
 Fixture(false);yield return Wait(40);before=l.Tank.position.y;for(int i=0;i<l.LocalPlayerCount;i++)l.SetInput(i,Vector3.zero,false,false,false,false,1);yield return Wait(130);
 Check("headOnlyGrow",l.Tank.position.y>before+.1f,"delta="+(l.Tank.position.y-before));
 for(int i=0;i<l.LocalPlayerCount;i++)l.SetInput(i,Vector3.zero,false,false,false,false,-1);yield return Wait(20);bool noPull=true;foreach(float force in e.Forces)noPull&=force==0;Check("headOnlyNoActivePull",noPull,"forces="+string.Join(",",e.Forces));
 Fixture(true);yield return Wait(40);l.Tank.useGravity=false;for(int i=0;i<l.LocalPlayerCount;i++)l.Players[i].body.useGravity=false;
 l.Tank.position+=Vector3.up*8;for(int i=0;i<l.LocalPlayerCount;i++){l.Players[i].body.position+=Vector3.up*8;l.Players[i].body.linearVelocity=Vector3.zero;}l.Tank.linearVelocity=Vector3.zero;float startMomentum=Momentum();
 for(int n=0;n<240;n++){for(int i=0;i<l.LocalPlayerCount;i++)l.SetInput(i,Vector3.zero,true,false,false,false,(n/30)%2==0?1:-1);yield return new WaitForFixedUpdate();}
 Check("airCycleMomentum",Mathf.Abs(Momentum()-startMomentum)<2,"deltaMomentum="+(Momentum()-startMomentum));
 l.Tank.useGravity=true;for(int i=0;i<l.LocalPlayerCount;i++)l.Players[i].body.useGravity=true;
 var packet=new SteamCarryProtocol.Input{round=1,sequence=1,stretch=-1};var decoded=SteamCarryProtocol.DecodeInput(SteamCarryProtocol.Encode(packet));Check("protocolRoundtrip",decoded.stretch==-1,"version="+SteamCarryProtocol.Version);
 bool rejects=false;packet.stretch=2;try{SteamCarryProtocol.DecodeInput(SteamCarryProtocol.Encode(packet));}catch(InvalidDataException){rejects=true;}Check("protocolRejectsInvalid",rejects,"");

 Fixture(true);yield return Wait(40);before=l.Tank.position.y;
 l.SetInput(0,Vector3.zero,true,false,false,false,1);yield return Wait(100);
 Check("singleGrowFinite",!l.Broken&&l.Tank.linearVelocity.magnitude<4,"height="+l.Players[0].height+" other="+l.Players[1].height+" tilt="+Vector3.Angle(l.Tank.transform.up,Vector3.up));
 Check("neutralHeightPreserved",Mathf.Abs(l.Players[1].height-1.7f)<.001f,"height="+l.Players[1].height);
 Fixture(true);yield return Wait(30);l.Tank.isKinematic=true;floor.GetComponent<Collider>().enabled=false;yield return Wait(80);
 float hanging=Vector3.Distance(l.Players[0].body.position+Vector3.up*l.Players[0].height,l.Tank.transform.TransformPoint(l.Players[0].anchor));
 Check("finiteHang",l.Players[0].joint&&hanging<.3f,"error="+hanging);floor.GetComponent<Collider>().enabled=true;l.Tank.isKinematic=false;

 Fixture(false);for(int i=0;i<l.LocalPlayerCount;i++){l.PlacePlayer(i,new Vector3(1000+(i==2?0:i==0?-.34f:.34f),.02f,i<2?-.34f:.34f));l.SetInput(i,Vector3.zero,false);l.SetInput(i,Vector3.zero,true);}
 yield return Wait(40);l.SetInput(0,Vector3.zero,true,false,false,false,1);speed=0;
 for(int n=0;n<160;n++){yield return new WaitForFixedUpdate();speed=Mathf.Max(speed,l.Tank.linearVelocity.magnitude);}
 Check("clusteredFinite",speed<5&&!l.Broken,"maxSpeed="+speed+" height="+l.Players[0].height);
 Fixture(true);for(int i=2;i<l.LocalPlayerCount;i++){l.Release(i);l.Players[i].active=false;l.Players[i].body.gameObject.SetActive(false);}for(int i=0;i<2;i++){l.PlacePlayer(i,new Vector3(1000+(i==0?-.9f:.9f),.02f,0));l.SetInput(i,Vector3.zero,false);l.SetInput(i,Vector3.zero,true);}l.DynamicRoster=true;l.SessionTeamSize=2;yield return Wait(30);
 bool held=l.IsGripping(0)&&l.IsGripping(1),touchedFloor=false;float maxTilt=0;
 var safeFloor=floor.GetComponent<Collider>();
 for(int i=0;i<2;i++)l.SetInput(i,Vector3.zero,true,false,false,false,1);speed=0;
 for(int n=0;n<500;n++){
  yield return new WaitForFixedUpdate();
  speed=Mathf.Max(speed,l.Tank.linearVelocity.magnitude);maxTilt=Mathf.Max(maxTilt,Vector3.Angle(l.Tank.rotation*Vector3.up,Vector3.up));
  held&=l.IsGripping(0)&&l.IsGripping(1);
  touchedFloor|=l.TankContacts.Touches(safeFloor);
 }
 Check("twoPlayerStretchReachesSafeFloorHolding",held&&touchedFloor&&!l.Broken&&speed<8&&maxTilt>18,"maxSpeed="+speed+" maxTilt="+maxTilt+" touchedFloor="+touchedFloor+" held="+held);
 l.DynamicRoster=false;l.SessionTeamSize=SessionRoster.RequiredPlayers;for(int i=2;i<l.LocalPlayerCount;i++){l.Players[i].active=true;l.Players[i].body.gameObject.SetActive(true);}
 Fixture(false);l.Tank.isKinematic=true;var a=l.Players[0];a.height=1.1f;l.SetInput(0,Vector3.zero,true);Check("shortCannotGrip",!a.joint,"");a.height=1.7f;l.SetInput(0,Vector3.zero,true);Check("noAutoRegrip",!a.joint,"");l.SetInput(0,Vector3.zero,false);l.SetInput(0,Vector3.zero,true);Check("repressGrips",a.joint,"");
 l.PlacePlayer(0,new Vector3(999.1f,.02f,-.55f));l.SetInput(0,Vector3.zero,true,false,false,false,1);Check("respawnHeldBlocked",!a.joint&&a.stretchInput==0,"height="+a.height);
 l.Tank.isKinematic=false;Fixture(true);yield return Wait(40);
 var ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);ceiling.name="Stretch dangerous ceiling";ceiling.transform.position=l.Tank.position+Vector3.up*(l.tankSize.y/2+.25f);ceiling.transform.localScale=new Vector3(8,.2f,8);ceiling.AddComponent<M0Danger>();
 for(int i=0;i<l.LocalPlayerCount;i++)l.SetInput(i,Vector3.zero,true,false,false,false,1);yield return Wait(100);Check("dangerContactBreaks",l.Broken,"failures="+l.FailureCount);
 UnityEngine.Object.Destroy(ceiling);
 Result=string.Join("\n",rows)+"\npassed="+pass;File.WriteAllText("Logs/stretch-force-checks.txt",Result);
 UnityEngine.Object.Destroy(floor);s.enabled=true;s.ReturnToLobby();
 }
 static float Momentum(){float p=l.Tank.mass*l.Tank.linearVelocity.y;foreach(var a in l.Players)if(a.active&&a.body)p+=a.body.mass*a.body.linearVelocity.y;return p;}
}
}
