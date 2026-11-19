using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Steamworks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using TankTogether.Networking;
using TankTogether.M0;
namespace TankTogether.Editor {
public static class SessionRecoveryChecks {
 const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static T F<T>(SteamCarrySession s,string n)=>(T)typeof(SteamCarrySession).GetField(n,Flags).GetValue(s);
 static void Set(SteamCarrySession s,string n,object v)=>typeof(SteamCarrySession).GetField(n,Flags).SetValue(s,v);
 static object Call(SteamCarrySession s,string n,params object[] a)=>typeof(SteamCarrySession).GetMethod(n,Flags).Invoke(s,a);
 public static string Result="Not run";
 public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
 static void Add(SteamCarrySession s,ulong identity,ulong conn){
  F<SessionRoster>(s,"roster").Admit(identity,conn,out int slot,out _);
  Call(s,"OnConnected",conn);F<bool[]>(s,"configReady")[slot]=F<bool[]>(s,"peerReady")[slot]=true;
  F<float[]>(s,"lastInputTime")[slot]=Time.unscaledTime;var r=F<RoomReadyState>(s,"playerReady");r.SetReady(slot,true,r.Revision);
 }
 static void LoadGuests(SteamCarrySession s){
  for(int i=1;i<=2;i++){F<bool[]>(s,"peerReady")[i]=true;F<float[]>(s,"lastInputTime")[i]=Time.unscaledTime;}
  Call(s,"FixedUpdate");
 }
 static void Input(SteamCarrySession s,ulong conn,int seq,bool held){
  var input=new SteamCarryProtocol.Input{configRevision=s.ConfigRevision,round=F<int>(s,"round"),sequence=seq,held=held,sentAt=Time.unscaledTime};
  byte[] bytes=SteamCarryProtocol.Encode(input);using var w=new FastBufferWriter(bytes.Length,Allocator.Temp);w.WriteBytesSafe(bytes);using var r=new FastBufferReader(w,Allocator.Temp);Call(s,"ReceiveInput",conn,r);
 }
 static IEnumerator Run(SteamCarrySession s){
  var rows=new List<string>();bool passed=true;void C(string n,bool v){rows.Add(n+"="+v);passed&=v;}
  string previous=s.EditorSaveRoot;string root=Path.GetFullPath("Logs/recovery-save-"+Guid.NewGuid().ToString("N"));
  try{
   s.ReturnToLobby();yield return null;yield return null;s.EditorSaveRoot=root;s.OpenSteamMenu();s.CreateRoom();float end=Time.unscaledTime+25;while(!s.Host&&Time.unscaledTime<end)yield return null;C("actual_host",s.Host);if(!s.Host)yield break;
   ulong owner=SteamClient.SteamId.Value;var route=s.lab.GetComponent<WhiteboxCarryLevel>();string cp=route.checkpoints[1].id;
   Add(s,123456,9001);Add(s,234567,9006);C("trio_starts",s.StartOrResume());LoadGuests(s);C("actual_run_committed",s.IsPlaying);
   var p=F<CoopCampaignSave.Progress>(s,"progress");p.checkpointId=cp;p.run.seconds=7321;p.run.breaks=3;p.run.checkpoints=1;p.run.members.Add(new AdventureMember{steamId="123456",name="Yesterday",jumps=7});
   string run=p.run.id,campaign=p.campaignId;C("save_cross_session_fixture",(bool)Call(s,"PersistRun"));
   s.ReturnToLobby();end=Time.unscaledTime+8;while(!s.CanChooseMode&&Time.unscaledTime<end)yield return null;
   s.OpenSteamMenu();s.CreateRoom();end=Time.unscaledTime+25;while(!s.Host&&Time.unscaledTime<end)yield return null;C("second_room",s.Host);if(!s.Host)yield break;
   p=F<CoopCampaignSave.Progress>(s,"progress");C("reload_same_run_time_cp",p.run.id==run&&p.run.seconds==7321&&p.checkpointId==cp&&p.run.breaks==3);
   C("world_uses_saved_checkpoint",route.checkpoints[route.CurrentCheckpoint].id==cp);
   string path=Path.Combine(root,owner.ToString(),campaign+".json");string unchanged=File.ReadAllText(path);
   string bad=Path.Combine(root,owner.ToString(),CoopCampaignSave.SlotCampaign(s.LevelId,2)+".json");File.WriteAllText(bad,"corrupt fixture");
   C("bad_slot_rejected",!(bool)Call(s,"SelectSaveSlot",2));C("old_slot_memory_preserved",ReferenceEquals(p,F<CoopCampaignSave.Progress>(s,"progress"))&&F<int>(s,"selectedSaveSlot")==1&&F<bool>(s,"saveReady"));
   C("bad_file_not_replaced",File.ReadAllText(bad)=="corrupt fixture"&&File.ReadAllText(path)==unchanged);
   Add(s,654321,9002);Add(s,234567,9006);C("new_team_starts",s.StartOrResume());LoadGuests(s);
   p=F<CoopCampaignSave.Progress>(s,"progress");C("continued_run_not_recreated",p.run.id==run&&p.run.members.Exists(m=>m.steamId=="123456"&&m.jumps==7));Call(s,"RecordPlayerEvent",1,"jump");C("new_member_stats_separate",p.run.members.Exists(m=>m.steamId=="654321"&&m.jumps==1));
   var tank=s.lab.Tank;Vector3 pos=tank.position,vel=new Vector3(.2f,.1f,.3f),spin=new Vector3(.1f,.2f,.3f);Quaternion rot=tank.rotation;tank.linearVelocity=vel;tank.angularVelocity=spin;
   Call(s,"OnDisconnected",(ulong)9002);C("dropout_remaining_pair_continues",s.IsPlaying&&s.PlayerCount==2&&!s.lab.Players[1].active&&!s.lab.Players[1].body);C("dropout_tank_untouched",tank.position==pos&&tank.rotation==rot&&tank.linearVelocity==vel&&tank.angularVelocity==spin);
   var roster=F<SessionRoster>(s,"roster");C("stranger_rejected",!roster.Admit(777777,9010,out _,out _));
   C("original_identity_reconnects",roster.Admit(654321,9003,out int seat,out _)&&seat==1);Call(s,"OnConnected",(ulong)9003);C("no_body_before_config",!s.lab.Players[1].active);
   C("duplicate_identity_rejected",!roster.Admit(654321,9004,out _,out _));
   Input(s,9003,1,true);C("input_before_config_rejected",!s.lab.Players[1].active);
   F<bool[]>(s,"configReady")[1]=true;Input(s,9003,2,true);
   C("reconnect_at_checkpoint",s.lab.Players[1].active&&Vector3.Distance(s.lab.Players[1].body.position,route.RespawnPoint(1))<.05f);
   Call(s,"FixedUpdate");C("held_reconnect_stays_disarmed",F<bool[]>(s,"rearm")[1]);Input(s,9003,3,true);Call(s,"FixedUpdate");C("held_heartbeat_stays_disarmed",F<bool[]>(s,"rearm")[1]);Input(s,9003,4,false);Call(s,"FixedUpdate");C("real_release_rearms",!F<bool[]>(s,"rearm")[1]);
   C("reconnect_no_grip",!s.lab.IsGripping(1));C("reconnect_keeps_tank",tank.position==pos&&tank.rotation==rot&&tank.linearVelocity==vel&&tank.angularVelocity==spin);
   roster.Disconnect(9003,true);Call(s,"OnDisconnected",(ulong)9003);C("voluntary_cannot_rejoin",!roster.Admit(654321,9005,out _,out _));
   Call(s,"PauseSession","save lock fixture");p=F<CoopCampaignSave.Progress>(s,"progress");p.run.seconds=7444;double expected=p.run.seconds;string before=File.ReadAllText(path);
   using(var guard=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
    C("write_lock_fails_safely",!(bool)Call(s,"RetryCurrentSave"));C("failed_save_keeps_disk_and_memory",File.ReadAllText(path)==before&&F<CoopCampaignSave.Progress>(s,"progress").run.seconds==expected);
    Set(s,"exitSaveFailed",true);Set(s,"exitConfirmation",true);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/recovery-save-error.png");
   }
   Call(s,"ClosePersonalMenus");C("retry_after_unlock",(bool)Call(s,"RetryCurrentSave"));
   C("retry_saves_once",CoopCampaignSave.Load(owner,campaign,"coop-prototype-20261013-1",null,root).progress.run.seconds==expected);
   var guest=CoopCampaignSave.Load(654321,campaign,"coop-prototype-20261013-1",null,root);C("guest_does_not_inherit_save",guest.status==CoopCampaignSave.Status.Missing);
   s.ReturnToLobby();yield return null;yield return null;
   // Emulate a client callback at the session boundary; no second remote client is claimed.
   Set(s,"reconnectRoom",(ulong)12345);Call(s,"ConnectionLost","fixture");yield return null;yield return null;
   C("connection_loss_returns_home",s.Mode==SteamCarrySession.EntryMode.Home&&s.CanChooseMode);C("retry_hint_kept",F<ulong>(s,"reconnectRoom")==12345);
   yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/recovery-home.png");
   Set(s,"exitPending",true);Call(s,"ConnectionLost","fixture voluntary");Set(s,"exitPending",false);yield return null;yield return null;C("voluntary_clears_retry",F<ulong>(s,"reconnectRoom")==0);
   s.OpenSteamMenu();Set(s,"busy",true);Set(s,"connectAt",Time.unscaledTime-26);int operation=F<int>(s,"operation");Call(s,"CheckConnectionHealth");yield return null;yield return null;
   C("steam_request_timeout_unlocks",s.Mode==SteamCarrySession.EntryMode.Home&&s.CanChooseMode&&F<int>(s,"operation")>operation);
  }finally{
   s.ReturnToLobby();s.EditorSaveRoot=previous;Set(s,"reconnectRoom",(ulong)0);
   Result=string.Join("\n",rows)+"\npassed="+passed+"\nNetwork: one real Steam host with injected peers; same-process disk reload, not remote or process-restart acceptance.";File.WriteAllText("Logs/session-recovery-checks.txt",Result);Debug.Log(Result);
  }
 }
}
}
