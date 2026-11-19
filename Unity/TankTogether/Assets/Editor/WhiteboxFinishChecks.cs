using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEngine;
namespace TankTogether.Editor {
 public static class WhiteboxFinishChecks {
  public static string Run() {
   if(!Application.isPlaying)throw new InvalidOperationException("Play required");
   var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
   if(s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Return to home before checks");
   s.StartLocalTest();s.enabled=false;var l=s.lab;l.Testing=true;
   var r=l.GetComponent<WhiteboxCarryLevel>();var persist=r.PersistCheckpoint;bool roster=l.DynamicRoster;
   var rows=new List<string>();bool passed=true;
   void C(string name,bool ok,object detail=null){rows.Add(name+"="+ok+" "+detail);passed&=ok;}
   var resolve=typeof(WhiteboxCarryLevel).GetMethod("ResolvePhysicsStep",BindingFlags.Instance|BindingFlags.NonPublic);
   void Step(){Physics.SyncTransforms();resolve.Invoke(r,null);}
   var cp=r.checkpoints.Last();int finish=0;Action onFinish=()=>finish++;r.Finished+=onFinish;
   void Reset(){r.SelectCheckpoint("maze");l.SetSessionPaused(false);l.ResetLab();l.Tank.isKinematic=true;l.Tank.position=cp.parkingFeet+new Vector3(8,2,0);Step();}
   try {
    C("final_id_and_enabled",cp.id=="finish"&&r.FinalStationIsTestFinish);
    var platform=r.levelRoot.GetComponentsInChildren<MeshCollider>(true).Single(x=>x.name=="Group 87");
    C("finish_on_authored_top",platform.Raycast(new Ray(cp.parkingFeet+Vector3.up,Vector3.down),out var ground,2)&&Vector3.Distance(ground.point,cp.parkingFeet)<.01f,cp.parkingFeet);
    string signature=r.CompatibilitySignature;r.FinalStationIsTestFinish=false;C("finish_rule_changes_compatibility",signature!=r.CompatibilitySignature);r.FinalStationIsTestFinish=true;
    l.DynamicRoster=false;Reset();for(int i=0;i<4;i++)l.PlacePlayer(i,cp.playerFeet+new Vector3(i*.2f,.02f,0));Step();C("players_only_do_not_finish",finish==0&&r.CheckpointId=="maze");
    l.Tank.position=cp.parkingFeet+new Vector3(r.checkpointRadius+.1f,2,0);Step();C("outside_radius_rejected",finish==0);
    l.Tank.position=cp.parkingFeet+Vector3.up*(r.checkpointHeight+l.tankSize.y/2+.1f);Step();C("above_height_rejected",finish==0);
    l.Tank.position=cp.parkingFeet-Vector3.up;Step();C("below_platform_rejected",finish==0);
    l.Tank.position=cp.parkingFeet+Vector3.up*2.2f;l.Simulate=false;Step();C("proxy_cannot_finish",finish==0);l.Simulate=true;
    l.SetSessionPaused(true);Step();C("paused_cannot_finish",finish==0);l.SetSessionPaused(false);
    l.Fail("finish broken tank fixture",true);Step();C("broken_tank_cannot_finish",finish==0&&r.CheckpointId=="maze");
    Reset();l.Tank.position=cp.parkingFeet+Vector3.up*2.2f;Step();C("intact_local_arrival_finishes",finish==1&&r.CheckpointId=="finish"&&!l.Broken);C("local_finish_feedback",l.Message.Contains("已抵达测试终点"),l.Message);Step();C("no_duplicate_completion",finish==1);
    Reset();l.DynamicRoster=true;l.Tank.position=cp.parkingFeet+Vector3.up*2.2f;int writes=0;r.PersistCheckpoint=_=>{writes++;return false;};Step();C("failed_save_blocks_completion",writes==1&&finish==1&&r.CheckpointId=="maze");
    string root=Path.GetFullPath("Logs/finish-save-"+Guid.NewGuid().ToString("N"));Func<CoopCampaignSave.Progress,bool> valid=p=>r.HasCheckpoint(p.checkpointId);
    var created=CoopCampaignSave.Create(123,"slot1",r.levelId,"maze","finish-test",valid,root);C("isolated_save_created",created.success);
    r.PersistCheckpoint=id=>{var next=CoopCampaignSave.Copy(created.progress);next.checkpointId=id;return CoopCampaignSave.Save(next,valid,root).success;};Step();
    var loaded=CoopCampaignSave.Load(123,"slot1","finish-test",valid,root);C("successful_save_precedes_finish",finish==2&&r.CheckpointId=="finish"&&loaded.success&&loaded.progress.checkpointId=="finish");
    var snapshot=new SteamCarryProtocol.Snapshot{round=1,tick=1,checkpoint=5,phase=SteamCarryProtocol.Phase.Finished,rotation=Quaternion.identity};for(int i=0;i<4;i++)snapshot.actors[i]=new SteamCarryProtocol.Actor{height=1.7f,active=i<SessionRoster.RequiredPlayers};
    var decoded=SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snapshot));C("finish_snapshot_roundtrip",decoded.checkpoint==5&&decoded.phase==SteamCarryProtocol.Phase.Finished);
    snapshot.checkpoint=6;bool rejected=false;try{SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snapshot));}catch(InvalidDataException){rejected=true;}C("unknown_checkpoint_rejected",rejected);
   } finally {r.Finished-=onFinish;r.PersistCheckpoint=persist;l.DynamicRoster=roster;r.SelectCheckpoint("start");s.enabled=true;s.ReturnToLobby();}
   rows.Add("passed="+passed);rows.Add("Editor fixture calls real after-physics resolver; isolated disk save. Not continuous traversal or remote multiplayer.");File.WriteAllLines("Logs/whitebox-finish-checks.txt",rows);return string.Join("\n",rows);
  }
 }
}
