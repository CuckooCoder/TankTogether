using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using UnityEngine;
using TankTogether.Networking;
using TankTogether.M0;
namespace TankTogether.Editor
{
 public static class FormalAdventureChecks
 {
    const BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
    static T F<T>(SteamCarrySession s,string n)=>(T)typeof(SteamCarrySession).GetField(n,Flags).GetValue(s);
    static void Set(SteamCarrySession s,string n,object v)=>typeof(SteamCarrySession).GetField(n,Flags).SetValue(s,v);
    static object Call(SteamCarrySession s,string n,params object[] args)=>typeof(SteamCarrySession).GetMethod(n,Flags).Invoke(s,args);
    public static string Result="Not run";
    public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
    static IEnumerator Run(SteamCarrySession s)
    {
        var rows=new List<string>();bool pass=true;
        void C(string n,bool ok){rows.Add(n+"="+ok);pass&=ok;}
        s.ReturnToLobby();float deadline=Time.unscaledTime+8;while(!s.CanChooseMode&&Time.unscaledTime<deadline)yield return null;
        s.EditorSaveRoot=Path.GetFullPath("Logs/adventure-save-"+Guid.NewGuid().ToString("N"));s.OpenSteamMenu();Call(s,"OpenSaveMenu");Set(s,"createAfterSelecting",true);Call(s,"SelectSaveSlot",1);deadline=Time.unscaledTime+25;
        while(!s.Host&&Time.unscaledTime<deadline)yield return null;C("actual_steam_host",s.Host);
        if(s.Host){
            var roster=F<SessionRoster>(s,"roster");var ready=F<RoomReadyState>(s,"playerReady");
            void LoadGuests(){for(int i=1;i<=2;i++){F<bool[]>(s,"peerReady")[i]=true;F<float[]>(s,"lastInputTime")[i]=Time.unscaledTime;}}
            for(int i=1;i<=2;i++){
                ulong peer=999999UL+(ulong)i;roster.Admit(123456UL+(ulong)i,peer,out _,out _);
                F<Dictionary<ulong,int>>(s,"slots")[peer]=i;F<bool[]>(s,"connected")[i]=true;F<bool[]>(s,"configReady")[i]=true;
                ready.SetConnected(i,true);ready.SetReady(i,true,ready.Revision);
            }
            LoadGuests();C("three_ready_start",s.StartOrResume());
            Set(s,"prepareAt",Time.unscaledTime-16);Call(s,"FixedUpdate");
            C("loading_timeout_returns_lobby",!roster.Locked&&F<SteamCarryProtocol.Phase>(s,"phase")==SteamCarryProtocol.Phase.Waiting);
            C("failed_loading_does_not_create_run",F<CoopCampaignSave.Progress>(s,"progress").run==null);
            LoadGuests();ready.SetReady(1,true,ready.Revision);ready.SetReady(2,true,ready.Revision);
            C("retry_loading_after_timeout",s.StartOrResume());LoadGuests();
            yield return new WaitForFixedUpdate();yield return null;
            C("loaded_trio_playing",s.IsPlaying);
            var initial=F<CoopCampaignSave.Progress>(s,"progress");C("run_committed_on_entry",initial.run!=null&&!string.IsNullOrEmpty(initial.run.id));string runId=initial.run?.id;
            Call(s,"OpenSettings");C("settings_does_not_pause_online_world",s.IsPlaying&&!s.lab.SessionPaused);Call(s,"ClosePersonalMenus");
            s.lab.Fail("technical bounds fixture",false);var failure=F<CoopCampaignSave.Progress>(s,"progress");
            C("technical_failure_not_glass_break",failure.run.breaks==0&&failure.run.techResets==1);
            yield return new WaitForSeconds(.7f);C("failed_attempt_retry",s.StartOrResume());
            LoadGuests();
            s.lab.SetInput(0,Vector3.zero,false,false,false,false,0);
            var route=s.lab.GetComponent<WhiteboxCarryLevel>();var cp=route.checkpoints[route.checkpoints.Length-1];
            yield return new WaitForSeconds(.1f);
            // Arm arrival after leaving all zones, then enter the actual final platform zone.
            s.lab.Tank.isKinematic=true;s.lab.Tank.position=cp.parkingFeet+new Vector3(route.checkpointRadius+1,2.2f,0);Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();yield return null;
            s.lab.Tank.position=cp.parkingFeet+Vector3.up*(s.lab.baseline.carryBottomHeight+s.lab.baseline.tankSize.y/2+.002f);s.lab.Tank.rotation=Quaternion.Euler(0,cp.tankYaw,0);s.lab.Tank.linearVelocity=Vector3.zero;s.lab.Tank.angularVelocity=Vector3.zero;Physics.SyncTransforms();
            yield return new WaitForSeconds(.25f);
            C("physical_finish_enters_results",F<SteamCarryProtocol.Phase>(s,"phase")==SteamCarryProtocol.Phase.Finished&&s.lab.SessionPaused);
            C("finish_persisted_before_vote",F<CoopCampaignSave.Progress>(s,"progress").run.finished&&F<CoopCampaignSave.Progress>(s,"progress").pendingReport!=null);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/formal-finish.png");
            var vote=F<FinishVote>(s,"finishVote");C("exact_online_candidates",vote.Candidates==7);C("self_vote_denied",!vote.Submit(0,0,-1,Time.unscaledTime));
            vote.Submit(1,0,0,Time.unscaledTime);vote.Submit(2,-1,-1,Time.unscaledTime);Set(s,"chosenMvp",1);Set(s,"chosenBlame",1);Call(s,"SubmitFinish",false);
            C("all_submitted_close",vote.Closed);C("save_report",(bool)Call(s,"SaveFinalReport"));
            var done=F<CoopCampaignSave.Progress>(s,"progress");C("one_report",done.history.Count==1&&done.history[0].runId==runId&&done.history[0].mvp.Count==2);
            Call(s,"SaveFinalReport");C("duplicate_report_suppressed",F<CoopCampaignSave.Progress>(s,"progress").history.Count==1);
            Call(s,"SubmitFinish",true);C("host_return_not_force_peer",F<byte>(s,"returnedToRoom")==1&&F<SteamCarryProtocol.Phase>(s,"phase")==SteamCarryProtocol.Phase.Finished);
            Call(s,"ReturnFinishedRoom");C("next_lobby_waits_for_peer",F<SteamCarryProtocol.Phase>(s,"phase")==SteamCarryProtocol.Phase.Finished);
            Set(s,"returnedToRoom",(byte)7);Call(s,"ReturnFinishedRoom");C("all_back_unlock_lobby",!roster.Locked&&F<SteamCarryProtocol.Phase>(s,"phase")==SteamCarryProtocol.Phase.Waiting);
            Set(s,"pendingReplay",true);C("replay_intent_keeps_old_run",F<CoopCampaignSave.Progress>(s,"progress").run.id==runId&&F<CoopCampaignSave.Progress>(s,"progress").run.finished);
            C("commit_replay",(bool)Call(s,"CommitAdventureStart"));var replay=F<CoopCampaignSave.Progress>(s,"progress");C("new_run_old_history",replay.run.id!=runId&&!replay.run.finished&&replay.run.seconds==0&&replay.history.Count==1&&replay.checkpointId=="start");
            Call(s,"OpenSaveMenu");yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/formal-saves.png");Set(s,"showSaveMenu",false);
            Call(s,"OpenSettings");yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/formal-settings.png");Set(s,"showSettings",false);
            int language=PlayerPreferences.Data.language;PlayerPreferences.Data.language=1;
            Call(s,"OpenSettings");yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/formal-settings-en.png");Set(s,"showSettings",false);
            PlayerPreferences.Data.language=language;
            Call(s,"OpenCosmetic");yield return new WaitForSeconds(.1f);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/formal-cosmetic.png");Set(s,"showCosmetic",false);Call(s,"DestroyPreview");
        }
        s.ReturnToLobby();Result=string.Join("\n",rows)+"\npassed="+pass+"\nnetwork=one real Steam host plus two injected members; fixture placement, not route traversal";File.WriteAllText("Logs/formal-adventure-integration.txt",Result);Debug.Log(Result);
    }
 }
}
