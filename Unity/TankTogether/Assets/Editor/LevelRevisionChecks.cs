using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;
namespace TankTogether.Editor {
 public static class LevelRevisionChecks {
  static readonly List<string> rows=new List<string>();static bool passed;
  static void C(string n,bool ok,object v){rows.Add(n+"="+ok+" "+v);passed&=ok;}
  static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
  static void Picture(M0CarryLab l,string name,Vector3 target,Vector3 offset){
   var go=new GameObject("Level revision evidence camera");var c=go.AddComponent<Camera>();c.CopyFrom(Camera.main);go.transform.position=target+offset;go.transform.LookAt(target);
   var rt=new RenderTexture(1280,720,24);c.targetTexture=rt;c.Render();var old=RenderTexture.active;RenderTexture.active=rt;var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes("Logs/level-revision-"+name+".png",image.EncodeToPNG());RenderTexture.active=old;c.targetTexture=null;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(go);
  }
  public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Run(s));}
  static IEnumerator Run(SteamCarrySession s){
   rows.Clear();passed=true;s.StartLocalTest();s.enabled=false;var l=s.lab;l.Testing=true;var r=l.GetComponent<WhiteboxCarryLevel>();var lift=l.GetComponent<WhiteboxLift>();var persist=r.PersistCheckpoint;
   var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();e.Begin(l,true);e.ManualInput=true;
   C("checkpoint_order",string.Join(",",r.checkpoints.Select(x=>x.id))=="start,pickup,pickup_exit,upper,maze,finish",string.Join(",",r.checkpoints.Select(x=>x.id)));
   C("temporary_finish_enabled",r.FinalStationIsTestFinish,"");
   var modelDependencies=AssetDatabase.GetDependencies(FormalWhiteboxSetup.ScenePath).Where(x=>x.EndsWith(".skp")).ToArray();
   C("only_current_model_dependency",modelDependencies.Length==1&&modelDependencies[0]==WhiteboxV2Setup.ModelPath,string.Join(",",modelDependencies));
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(WhiteboxV2Setup.ModelPath);
   foreach(var source in prefab.GetComponentsInChildren<MeshFilter>(true)){
    var actual=r.levelRoot.GetComponentsInChildren<MeshFilter>(true).Single(x=>x.name==source.name);
    var expected=source.name=="Group 31"?actual.GetComponent<MeshCollider>().sharedMesh:source.sharedMesh;
    C("mesh_current_"+source.name,actual.sharedMesh==expected,actual.sharedMesh.vertexCount);
   }
   var routeMesh=r.levelRoot.GetComponentsInChildren<MeshFilter>(true).Single(x=>x.name=="Group 31");
   var pedestal=routeMesh.transform.Find("Authored central pedestal safe contact");
   long IndexCount(Mesh mesh)=>Enumerable.Range(0,mesh.subMeshCount).Sum(i=>(long)mesh.GetIndexCount(i));
   long total=IndexCount(prefab.GetComponentsInChildren<MeshFilter>(true).Single(x=>x.name=="Group 31").sharedMesh),rest=IndexCount(routeMesh.GetComponent<MeshCollider>().sharedMesh),safe=IndexCount(pedestal.GetComponent<MeshCollider>().sharedMesh);
   C("collision_triangles_partition",total==rest+safe&&safe>0,total+"="+rest+"+"+safe);
   C("pedestal_visual_uses_moved_collision_mesh",pedestal.GetComponent<MeshFilter>().sharedMesh==pedestal.GetComponent<MeshCollider>().sharedMesh,pedestal.position);
   var control=r.levelRoot.GetComponentsInChildren<Transform>(true).Single(x=>x.name=="Group 51");C("switch_moves_with_lift",control.parent==lift.platform.transform,control.position);
   C("lift_rules_retained",lift.delay==2&&lift.speed==2.5f&&lift.travel==101,lift.delay+"/"+lift.speed+"/"+lift.travel);
   r.SelectCheckpoint("start");l.ResetLab();
   var fish=r.levelRoot.GetComponentsInChildren<MeshRenderer>(true).Single(x=>x.name=="Group 29");
   C("initial_matches_authored_marker",Vector3.Distance(l.Tank.position,fish.bounds.center)<.04f,l.Tank.position);
   yield return Wait(100);
   C("initial_tank_safe",!l.Broken,l.Message);C("no_spawn_auto_checkpoint",r.CheckpointId=="start",r.CheckpointId);
   Picture(l,"pickup",l.Tank.position-Vector3.up*.6f,new Vector3(6,4,7));
   Picture(l,"start",r.checkpoints[0].playerFeet,new Vector3(15,12,20));
   var snap=new SteamCarryProtocol.Snapshot{round=1,tick=1,checkpoint=3,rotation=Quaternion.identity};for(int i=0;i<4;i++)snap.actors[i]=new SteamCarryProtocol.Actor{height=1.7f,active=i<l.LocalPlayerCount};C("network_upper_index3_roundtrip",SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snap)).checkpoint==3,SteamCarryProtocol.Version);
   snap.checkpoint=6;bool rejects=false;try{SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snap));}catch(InvalidDataException){rejects=true;}C("network_index6_rejected",rejects,"");
   // The fish is already on the pickup marker at game start: approaching alone does not activate it.
   var pickupFeet=r.checkpoints.Single(x=>x.id=="pickup").tankFeet;
   for(int i=0;i<l.LocalPlayerCount;i++){l.PlacePlayer(i,pickupFeet+new Vector3(i%2==0?-.9f:.9f,.02f,i<2?-.55f:.55f));l.SetInput(i,Vector3.zero,false);}
   yield return Wait(15);C("players_at_pickup_do_not_save",r.CheckpointId=="start",r.CheckpointId);
   l.DynamicRoster=true;int pickupWrites=0;r.PersistCheckpoint=id=>{pickupWrites++;return false;};
   for(int i=0;i<l.LocalPlayerCount;i++)l.SetInput(i,Vector3.zero,true);yield return Wait(10);
   C("pickup_first_grip_failure_keeps_start",pickupWrites>0&&r.CheckpointId=="start"&&!l.Broken,pickupWrites);
   var firstRoot=Path.GetFullPath("Logs/level-first-grip-save-"+Guid.NewGuid().ToString("N"));Func<CoopCampaignSave.Progress,bool> firstValid=p=>r.HasCheckpoint(p.checkpointId);
   var firstSave=CoopCampaignSave.Create(123,"slot1",r.levelId,"start","revision-test",firstValid,firstRoot);pickupWrites=0;
   r.PersistCheckpoint=id=>{pickupWrites++;var next=CoopCampaignSave.Copy(firstSave.progress);next.checkpointId=id;return CoopCampaignSave.Save(next,firstValid,firstRoot).success;};yield return Wait(5);
   var firstLoaded=CoopCampaignSave.Load(123,"slot1","revision-test",firstValid,firstRoot);
   C("pickup_first_grip_saves_once",r.CheckpointId=="pickup"&&pickupWrites==1&&firstLoaded.success&&firstLoaded.progress.checkpointId=="pickup",r.CheckpointId+" writes="+pickupWrites);
   l.DynamicRoster=true;int writes=0;r.PersistCheckpoint=id=>{writes++;return true;};
   foreach(string id in new[]{"pickup","pickup_exit","upper","maze"})for(int count=2;count<=l.LocalPlayerCount;count++){
    r.SelectCheckpoint(id);l.ConfigureSessionRoster(new[]{true,true,count==3,false},true);l.SessionTeamSize=count;l.SetSessionPaused(false);l.ResetLab();yield return Wait(150);
    C(id+"_safe_"+count,!l.Broken,l.Message);C(id+"_no_save_on_respawn_"+count,writes==0,writes);
    C(id+"_lift_"+count,lift.AtTop==(id=="upper"||id=="maze"),lift.Distance);
    C(id+"_no_generated_columns_"+count,UnityEngine.Object.FindObjectsByType<M1SupportPost>(FindObjectsSortMode.None).Length==0,"");
    for(int i=0;i<count;i++)l.SetInput(i,Vector3.zero,false);yield return Wait(2);for(int i=0;i<count;i++)l.SetInput(i,Vector3.zero,true);yield return Wait(10);
    C(id+"_grip_"+count,Enumerable.Range(0,count).All(l.IsGripping)&&!l.Broken,Enumerable.Range(0,count).Count(l.IsGripping));
    if(count==2)Picture(l,id+"-2p",l.Tank.position-Vector3.up*.6f,new Vector3(6,4,7));
   }
   var upper=r.checkpoints.Single(x=>x.id=="upper");Picture(l,"lift",lift.platformCollider.bounds.center,new Vector3(12,8,12));
   // Persistence retains stable IDs even though the upper point moved to network index 3.
   var root=Path.GetFullPath("Logs/level-revision-save-"+Guid.NewGuid().ToString("N"));Func<CoopCampaignSave.Progress,bool> valid=p=>r.HasCheckpoint(p.checkpointId);
   var created=CoopCampaignSave.Create(123,"slot1",r.levelId,"upper","revision-test",valid,root);var loaded=CoopCampaignSave.Load(123,"slot1","revision-test",valid,root);C("upper_save_id_compatible",loaded.success&&r.SelectCheckpoint(loaded.progress.checkpointId)&&r.CurrentCheckpoint==3,loaded.status);
   l.DynamicRoster=false;r.SelectCheckpoint("start");l.ResetLab();yield return Wait(5);l.Tank.isKinematic=true;
   var pickup=r.checkpoints.Single(x=>x.id=="pickup").parkingFeet;
   l.Tank.position=pickup+new Vector3(3,2.2f,0);yield return Wait(3);l.Tank.position=pickup+Vector3.up*2.2f;yield return Wait(3);C("pickup_arrival_activates",r.CheckpointId=="pickup",r.CheckpointId);
   l.Tank.position=upper.parkingFeet+Vector3.up*2.2f;yield return Wait(3);C("upper_arrival_activates",r.CheckpointId=="upper",r.CheckpointId);
   l.Tank.position=pickup+Vector3.up*2.2f;yield return Wait(3);C("no_progress_rollback",r.CheckpointId=="upper",r.CheckpointId);
   r.PersistCheckpoint=persist;r.SelectCheckpoint("start");s.enabled=true;s.ReturnToLobby();
   rows.Add("passed="+passed);rows.Add("Scope: editor fixture, real physics and isolated disk save. Not continuous route or remote multiplayer.");File.WriteAllLines("Logs/level-v2-revision-checks.txt",rows);Debug.Log("Level revision checks passed="+passed);
  }
 }
}
