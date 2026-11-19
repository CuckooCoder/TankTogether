using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using UnityEngine;
using TankTogether.Networking;
namespace TankTogether.Editor {
public static class FrontEndChecks {
 const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static T F<T>(SteamCarrySession s,string n)=>(T)typeof(SteamCarrySession).GetField(n,Flags).GetValue(s);
 static void Set(SteamCarrySession s,string n,object v)=>typeof(SteamCarrySession).GetField(n,Flags).SetValue(s,v);
 static object Call(SteamCarrySession s,string n,params object[] args)=>typeof(SteamCarrySession).GetMethod(n,Flags).Invoke(s,args);
 static string Block(SteamCarrySession s)=>(string)typeof(SteamCarrySession).GetProperty("LobbyStartBlock",Flags).GetValue(s);
 public static string Result="Not run";
 public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
 static IEnumerator Run(SteamCarrySession s){
 var rows=new List<string>();bool pass=true;void C(string n,bool v){pass&=v;rows.Add(n+"="+v);}
 s.ReturnToLobby();yield return null;yield return null;int language=PlayerPreferences.Data.language;PlayerPreferences.Data.language=0;
 string previousRoot=s.EditorSaveRoot;s.EditorSaveRoot=Path.GetFullPath("Logs/frontend-save-"+Guid.NewGuid().ToString("N"));
 yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/frontend-home.png");
 C("direct_create_opens_save",s.BeginCreateFlow()&&F<bool>(s,"showSaveMenu")&&F<bool>(s,"createAfterSelecting"));
 yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/frontend-empty-save.png");
 Call(s,"CloseSaveMenu");C("cancel_create_returns_home",s.Mode==SteamCarrySession.EntryMode.Home&&!F<bool>(s,"createAfterSelecting")&&!s.Host);
 C("join_does_not_open_save",s.BeginJoinFlow()&&!F<bool>(s,"showSaveMenu"));
 yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/frontend-join.png");s.ReturnToLobby();
 C("create_after_cancel",s.BeginCreateFlow());Call(s,"SelectSaveSlot",1);float until=Time.unscaledTime+25;while(!s.Host&&Time.unscaledTime<until)yield return null;
 C("real_steam_host_created",s.Host);
 if(s.Host){
  C("single_host_block_reason",Block(s)?.Contains("2 位")??false);
  yield return new WaitForSeconds(.3f);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/frontend-host.png");
  var p=F<CoopCampaignSave.Progress>(s,"progress");string campaign=p.campaignId;
  const ulong peer=999999,secondPeer=999998;var roster=F<SessionRoster>(s,"roster");
  var ready=F<RoomReadyState>(s,"playerReady");ready.SetConnected(1,true);
  roster.Admit(123456,peer,out _,out _);F<Dictionary<ulong,int>>(s,"slots")[peer]=1;F<bool[]>(s,"connected")[1]=true;
  C("two_player_block_reason",Block(s)?.Contains("1 位")??false);
  roster.Admit(123457,secondPeer,out _,out _);F<Dictionary<ulong,int>>(s,"slots")[secondPeer]=2;F<bool[]>(s,"connected")[2]=true;ready.SetConnected(2,true);
  C("sync_before_ready_reason",Block(s)?.Contains("参数同步")??false);
  for(int i=1;i<=2;i++){F<bool[]>(s,"configReady")[i]=F<bool[]>(s,"peerReady")[i]=true;F<float[]>(s,"lastInputTime")[i]=Time.unscaledTime;}
  C("not_ready_reason",Block(s)?.Contains("点击准备")??false);
  ready.SetReady(1,true,ready.Revision);ready.SetReady(2,true,ready.Revision);C("three_ready_unblocked",Block(s)==null);
  Call(s,"ApplyLobbyCosmetic",1,2);C("cosmetic_cancels_only_self",(ready.ReadyMask&2)==0&&(ready.ReadyMask&4)!=0);
  ready.SetReady(1,true,ready.Revision);Call(s,"ApplyLobbyCosmetic",1,2);C("duplicate_cosmetic_preserves_ready",(ready.ReadyMask&2)!=0);
  Call(s,"OpenSaveMenu");Call(s,"SelectSaveSlot",2);C("switch_slot_keeps_room",s.Host&&s.PlayerCount==3&&F<CoopCampaignSave.Progress>(s,"progress").campaignId!=campaign);C("switch_resets_ready",!ready.CanStart);
  Call(s,"OpenSaveMenu");yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/frontend-existing-save.png");Call(s,"CloseSaveMenu");
  Set(s,"saveReady",false);C("save_error_reason",Block(s)?.Contains("存档尚未就绪")??false);Set(s,"saveReady",true);
  p=F<CoopCampaignSave.Progress>(s,"progress");p.run=AdventureRun.Create();p.run.finished=true;
  C("completed_save_blocked",Block(s)?.Contains("已通关")??false);
  Set(s,"pendingReplay",true);
  for(int i=1;i<=2;i++){F<bool[]>(s,"configReady")[i]=F<bool[]>(s,"peerReady")[i]=true;F<float[]>(s,"lastInputTime")[i]=Time.unscaledTime;ready.SetReady(i,true,ready.Revision);}
  C("replay_can_start",Block(s)==null&&p.run.finished);
  yield return new WaitForSeconds(.2f);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/frontend-three-ready.png");
  C("three_player_start",s.StartOrResume());C("loading_reason",Block(s)?.Contains("正在加载")??false);
  yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/frontend-loading.png");
  Set(s,"prepareAt",Time.unscaledTime-16);Call(s,"FixedUpdate");C("load_timeout_back_to_lobby",!roster.Locked&&F<SteamCarryProtocol.Phase>(s,"phase")==SteamCarryProtocol.Phase.Waiting);C("failed_load_preserves_completed_run",p.run.finished);
  PlayerPreferences.Data.language=1;yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/frontend-room-en.png");
 }
 s.ReturnToLobby();yield return null;yield return null;s.EditorSaveRoot=previousRoot;
 C("presentation_cleaned",UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length<6);
 PlayerPreferences.Data.language=1;yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/frontend-home-en.png");PlayerPreferences.Data.language=language;
 C("local_entry_kept",s.StartLocalTest());s.ReturnToLobby();
 Result=string.Join("\n",rows)+"\npassed="+pass+"\nSingle Steam host and two injected members; not remote acceptance.";File.WriteAllText("Logs/frontend-checks.txt",Result);Debug.Log(Result);
 }
}
}
