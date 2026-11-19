using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEngine;
namespace TankTogether.Editor {
 public static class FormalWhiteboxChecks {
  static List<string> rows;static bool passed;
  static void C(string n,bool ok,object detail){rows.Add(n+"="+ok+" "+detail);passed&=ok;}
  static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
  public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Run(s));}
  static IEnumerator Run(SteamCarrySession s){
   rows=new List<string>();passed=true;s.StartLocalTest();var l=s.lab;l.Testing=true;var route=l.GetComponent<WhiteboxCarryLevel>();var lift=l.GetComponent<WhiteboxLift>();
   var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();e.Begin(l,true);e.ManualInput=true;
   C("separate_level",route.levelId=="formal-whitebox-v1",route.levelId);C("no_premature_finish",!route.FinalStationIsTestFinish,route.FinalStationIsTestFinish);
   C("tank_dimensions",l.tankSize==new Vector3(4.2f,.95f,3.2f),l.tankSize);C("configuration_valid",CarryTuning.Validate(l.baseline,l.Tuning)==null,CarryTuning.Validate(l.baseline,l.Tuning));
   C("max_height",e.maxHeight==l.baseline.stretchMaxHeight,e.maxHeight);C("authored_meshes",route.levelRoot.GetComponentsInChildren<MeshCollider>().Length>=26,route.levelRoot.GetComponentsInChildren<MeshCollider>().Length);
   C("old_group37_now_wall",route.levelRoot.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Group 37").gameObject.activeSelf,"");
   C("extended_route_last_column",route.levelRoot.GetComponentsInChildren<Transform>().Any(t=>t.name=="Group 57"),"");
   yield return Wait(50);C("spawn_safe",!l.Broken,l.Message);C("tank_reference_bottom",Mathf.Abs(l.Tank.position.y-l.tankSize.y/2+27.8f)<.04f,l.Tank.position);
   C("players_on_start",l.Players.All(a=>Vector3.Distance(a.body.position,route.checkpoints[0].playerFeet)<1.1f),string.Join(";",l.Players.Select(a=>a.body.position.ToString("F2"))));
   var a=l.Players[0];float diameter=a.capsule.radius*2,mass=a.body.mass;e.SetDirection(1);yield return Wait(600);e.SetDirection(0);
   C("actual_growth_to_cap",Mathf.Abs(a.height-e.maxHeight)<.01f,a.height);C("capsule_tracks_height",Mathf.Abs(a.capsule.height-e.maxHeight)<.01f,a.capsule.height);C("width_mass_preserved",a.capsule.radius*2==diameter&&a.body.mass==mass,diameter+"/"+mass);
   var headView=Camera.main.WorldToViewportPoint(l.Head(0));C("max_height_head_framed",headView.z>0&&headView.y>.05f&&headView.y<.95f,headView);
   C("feet_stay_grounded",Mathf.Abs(a.body.position.y)<.06f,a.body.position);
   yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/formal-whitebox-max-height.png");
   var snap=new SteamCarryProtocol.Snapshot{round=1,tick=1,rotation=Quaternion.identity};for(int i=0;i<4;i++)snap.actors[i]=new SteamCarryProtocol.Actor{active=i<l.LocalPlayerCount,height=5,position=l.Players[i].body?l.Players[i].body.position:Vector3.zero};
   C("network_5m_roundtrip",SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snap)).actors.All(x=>x.height==5),SteamCarryProtocol.Version);
   snap.actors[0].height=5.1f;bool rejected=false;try{SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snap));}catch(InvalidDataException){rejected=true;}C("network_above5_rejected",rejected,"");
   var profile=CarryTuning.Capture(l);var copy=UnityEngine.Object.Instantiate(l.baseline);var extras=CarryTuning.Clone(l.Tuning);CarryTuning.Read(CarryTuning.Parse(JsonUtility.ToJson(profile)),copy,extras,route.levelId);C("GM_roundtrip_height_cap",copy.stretchMaxHeight==l.baseline.stretchMaxHeight&&copy.tankSize==l.tankSize,profile.values.Length);UnityEngine.Object.Destroy(copy);
   l.PlacePlayer(0,route.checkpoints[0].playerFeet+new Vector3(4,.02f,0));var ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);ceiling.name="Isolated height ceiling check";ceiling.transform.position=route.checkpoints[0].playerFeet+new Vector3(4,2.1f,0);ceiling.transform.localScale=new Vector3(3,.3f,3);Physics.SyncTransforms();yield return Wait(25);e.SetDirection(1);yield return Wait(120);e.SetDirection(0);C("ceiling_still_blocks",a.height>1.8f&&a.height<1.96f,a.height);UnityEngine.Object.Destroy(ceiling);
   for(int i=0;i<4;i++)l.PlacePlayer(i,route.checkpoints[0].tankFeet+new Vector3(i%2==0?-.9f:.9f,.02f,i<2?-.55f:.55f));
   for(int i=0;i<4;i++)l.SetInput(i,Vector3.zero,false);yield return Wait(25);for(int i=0;i<4;i++)l.SetInput(i,Vector3.zero,true);yield return Wait(5);C("enlarged_tank_four_grips",Enumerable.Range(0,4).All(l.IsGripping),Enumerable.Range(0,4).Count(l.IsGripping));C("grabbing_no_failure",!l.Broken,l.Message);
   for(int i=0;i<4;i++){l.SetInput(i,Vector3.zero,false);l.PlacePlayer(i,route.checkpoints[0].playerFeet+new Vector3(i*1.5f,.02f,0));}
   var park=route.checkpoints.Single(c=>c.id=="upper").parkingFeet;int finished=0;Action onFinish=()=>finished++;route.Finished+=onFinish;
   l.Tank.position=park+Vector3.up*(l.baseline.carryBottomHeight+l.tankSize.y/2+.02f);l.Tank.rotation=Quaternion.identity;l.Tank.linearVelocity=Vector3.zero;l.Tank.angularVelocity=Vector3.zero;
   yield return Wait(70);C("upper_yellow_zone_save",route.CheckpointId=="upper"&&!l.Broken,route.CheckpointId+"/"+l.Message);C("upper_not_finish",finished==0,finished);
   l.ResetLab();yield return Wait(50);C("upper_respawn_safe",!l.Broken&&route.CheckpointId=="upper",l.Message);C("lift_restored_top",lift.AtTop,lift.Distance);
   C("no_duplicate_spawn_posts",UnityEngine.Object.FindObjectsByType<M1SupportPost>(FindObjectsSortMode.None).Length==0,UnityEngine.Object.FindObjectsByType<M1SupportPost>(FindObjectsSortMode.None).Length);
   C("upper_tank_at_marker",Vector3.Distance(l.Tank.position,park+Vector3.up*(l.baseline.carryBottomHeight+l.tankSize.y/2))<.06f,l.Tank.position);
   route.Finished-=onFinish;route.SelectCheckpoint("start");s.ReturnToLobby();
   rows.Add("passed="+passed);rows.Add("Scope: actual local simulation and injected snapshot serialization; fixture placement for grips/checkpoint, not continuous route or remote multiplayer.");File.WriteAllText("Logs/formal-whitebox-checks.txt",string.Join("\n",rows));Debug.Log("Formal whitebox checks passed="+passed);
  }
 }
}
