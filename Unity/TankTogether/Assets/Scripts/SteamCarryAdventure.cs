using System;
using System.Collections.Generic;
using System.Text;
using Steamworks;
using TankTogether.M0;
using Unity.Netcode;
using UnityEngine;
namespace TankTogether.Networking
{
    public sealed partial class SteamCarrySession
    {
        const string AdventureMessage="tt.adventure.1", FinishActionMessage="tt.finish-action.1";
        readonly FinishVote finishVote=new FinishVote();
        int chosenMvp=-1,chosenBlame=-1;
        byte returnedToRoom;
        bool reportSaved,pendingReplay,exitConfirmation;
        float nextAdventureInfo,nextStatsSave;
        [Serializable] sealed class AdventureInfo
        {
            public double seconds;public int breaks,checkpoints,slot;
            public string name,behavior;public bool runFinished,pendingReplay;
            public byte finalists,submitted,returned,mvp,blame,online;
            public float voteSeconds;public bool reportSaved;
            public int[] colors=new int[4];
            public string[] names=new string[4],identities=new string[4];
        }
        AdventureInfo adventureInfo;
        bool AtFinish=>phase==SteamCarryProtocol.Phase.Finished;
        bool ReturnedFromFinish=>AtFinish&&((Host?returnedToRoom:adventureInfo?.returned??0)&(1<<localSlot))!=0;
        void RegisterAdventureMessages()
        {
            net.CustomMessagingManager.RegisterNamedMessageHandler(AdventureMessage,(sender,reader)=>{
                if(Host||sender!=0)return;
                try{
                    int length=reader.Length-reader.Position;if(length<1||length>16384)return;
                    var bytes=new byte[length];reader.ReadBytesSafe(ref bytes,length);
                    var info=JsonUtility.FromJson<AdventureInfo>(Encoding.UTF8.GetString(bytes));
                    if(info==null||info.colors==null||info.colors.Length!=4||info.names==null||info.names.Length!=4||info.identities==null||info.identities.Length!=4||info.slot<1||info.slot>5||double.IsNaN(info.seconds)||double.IsInfinity(info.seconds)||info.seconds<0)return;
                    if((info.online|info.finalists|info.submitted|info.returned|info.mvp|info.blame)>=(1<<SessionRoster.RequiredPlayers))return;
                    adventureInfo=info;
                }catch(Exception){Log("invalid_adventure_info");}
            });
            net.CustomMessagingManager.RegisterNamedMessageHandler(FinishActionMessage,(sender,reader)=>{
                if(!Host||!AtFinish||!slots.TryGetValue(sender,out int slot)||reader.Length-reader.Position!=3)return;
                reader.ReadValueSafe(out byte action);reader.ReadValueSafe(out sbyte m);reader.ReadValueSafe(out sbyte b);
                if(action==1&&finishVote.Submit(slot,m,b,Time.unscaledTime))PersistPendingVote();
                else if(action==2){finishVote.Submit(slot,-1,-1,Time.unscaledTime);returnedToRoom|=(byte)(1<<slot);PersistPendingVote();}
            });
        }
        // Start is committed only after all three players have loaded.
        bool CommitAdventureStart()
        {
            if(progress==null)return false;
            var next=CoopCampaignSave.Copy(progress);
            if(next.run==null||pendingReplay){next.run=AdventureRun.Create();next.pendingReport=null;if(pendingReplay)next.checkpointId="start";}
            next.displayName=string.IsNullOrEmpty(next.displayName)?"冒险 "+selectedSaveSlot:next.displayName;
            var saved=CoopCampaignSave.Save(next,KnownCheckpoint,SaveRoot);saveStatus=saved.message;
            if(!saved.success){saveReady=false;return false;}
            progress=saved.progress;pendingReplay=false;reportSaved=false;returnedToRoom=0;nextStatsSave=Time.unscaledTime+10;return true;
        }
        AdventureMember GetMember(int slot)
        {
            if(progress?.run==null)return null;
            string id=roster.Identity(slot).ToString();if(id=="0")return null;
            progress.run.members??=new List<AdventureMember>();
            var member=progress.run.members.Find(x=>x.steamId==id);
            if(member==null){member=new AdventureMember{steamId=id,name=PeerName(slot)};progress.run.members.Add(member);}
            return member;
        }
        string PeerName(int slot)
        {
            ulong id=roster.Identity(slot);if(id==0)return "P"+(slot+1);
            string name=slot==0?SteamClient.Name:new Friend(id).Name;
            return string.IsNullOrEmpty(name)?"P"+(slot+1):name;
        }
        void RecordPlayerEvent(int slot,string kind)
        {
            if(!Host||!IsPlaying)return;var m=GetMember(slot);if(m==null)return;
            if(kind=="jump")m.jumps++;else if(kind=="grab")m.grabs++;else if(kind=="fall")m.falls++;
        }
        bool PersistRun()
        {
            if(!Host||progress?.run==null)return true;
            var saved=CoopCampaignSave.Save(progress,KnownCheckpoint,SaveRoot);saveStatus=saved.message;saveReady=saved.success;
            if(saved.success)progress=saved.progress;else status="保存失败，内存进度保留，请重试："+saved.message;
            return saved.success;
        }
        bool IsFinalCheckpoint(string id)
        {
            var route=lab.GetComponent<WhiteboxCarryLevel>();return route&&route.FinalStationIsTestFinish&&route.checkpoints.Length>0&&id==route.checkpoints[route.checkpoints.Length-1].id;
        }
        void FreezeCompletion(CoopCampaignSave.Progress next)
        {
            if(next.run==null)next.run=AdventureRun.Create();
            next.run.finished=true;
            var report=new AdventureReport{runId=next.run.id,completedUtc=DateTime.UtcNow.ToString("O"),seconds=next.run.seconds,breaks=next.run.breaks,checkpoints=next.run.checkpoints,members=next.run.members};
            for(int i=0;i<4;i++)if(connected[i]&&lab.Players[i].active)report.finalists.Add(roster.Identity(i).ToString());
            next.pendingReport=report;
        }
        void OnRouteFinished()
        {
            if(!Host||!IsPlaying||progress?.run?.finished!=true)return;
            resetVote.Cancel();Capture(false);lab.SetSessionPaused(true);phase=SteamCarryProtocol.Phase.Finished;
            byte mask=0;for(int i=0;i<4;i++)if(connected[i]&&lab.Players[i].active)mask|=(byte)(1<<i);
            finishVote.Begin(mask,Time.unscaledTime);returnedToRoom=0;reportSaved=false;chosenMvp=chosenBlame=-1;
            status="本轮已通关，30 秒内可投 MVP / 背锅王。";nextAdventureInfo=0;nextStatsSave=0;
        }
        void PersistPendingVote()
        {
            if(progress?.pendingReport==null||reportSaved)return;
            var r=progress.pendingReport;r.mvp.Clear();r.blame.Clear();
            for(int i=0;i<4;i++){string id=roster.Identity(i).ToString();if((finishVote.Winners(true)&(1<<i))!=0)r.mvp.Add(id);if((finishVote.Winners(false)&(1<<i))!=0)r.blame.Add(id);}
            PersistRun();nextAdventureInfo=0;if(finishVote.Closed)nextStatsSave=0;
        }
        bool SaveFinalReport()
        {
            if(reportSaved)return true;if(progress?.pendingReport==null)return false;
            var next=CoopCampaignSave.Copy(progress);next.history??=new List<AdventureReport>();
            if(!next.history.Exists(r=>r.runId==next.pendingReport.runId))next.history.Add(next.pendingReport);
            next.pendingReport=null;
            var saved=CoopCampaignSave.Save(next,KnownCheckpoint,SaveRoot);saveStatus=saved.message;saveReady=saved.success;
            if(saved.success){progress=saved.progress;reportSaved=true;}else status="报告保存失败，请重试；尚未允许开始下一局。";
            return saved.success;
        }
        void UpdateAdventure()
        {
            if(!Host||progress==null)return;
            if(progress.run!=null&&!progress.run.finished&&(phase==SteamCarryProtocol.Phase.Playing||phase==SteamCarryProtocol.Phase.Failed)){
                progress.run.seconds+=Time.unscaledDeltaTime;
                for(int i=0;i<4;i++)if(connected[i]&&lab.Players[i].active)GetMember(i);
                if(Time.unscaledTime>=nextStatsSave){nextStatsSave=Time.unscaledTime+10;PersistRun();}
            }
            if(AtFinish){
                if(Time.unscaledTime>=finishVote.Deadline&&!finishVote.Closed){finishVote.Close();PersistPendingVote();}
                if(finishVote.Closed&&!reportSaved&&Time.unscaledTime>=nextStatsSave){nextStatsSave=Time.unscaledTime+2;SaveFinalReport();}
            }
            if(Time.unscaledTime<nextAdventureInfo)return;nextAdventureInfo=Time.unscaledTime+.5f;
            adventureInfo=new AdventureInfo{seconds=progress.run?.seconds??0,breaks=progress.run?.breaks??0,checkpoints=progress.run?.checkpoints??0,slot=selectedSaveSlot,name=progress.displayName,runFinished=progress.run?.finished??false,pendingReplay=pendingReplay,finalists=finishVote.Candidates,submitted=finishVote.Submitted,returned=returnedToRoom,mvp=reportSaved?finishVote.Winners(true):(byte)0,blame=reportSaved?finishVote.Winners(false):(byte)0,voteSeconds=finishVote.Closed?0:Mathf.Max(0,finishVote.Deadline-Time.unscaledTime),reportSaved=reportSaved,behavior=progress.pendingReport!=null?BehaviorSummary(progress.pendingReport):progress.history?.Count>0?BehaviorSummary(progress.history[progress.history.Count-1]):""};
            for(int i=0;i<4;i++){adventureInfo.colors[i]=cosmetics.TryGetValue(roster.Identity(i),out int color)?color:i;adventureInfo.names[i]=PeerName(i);adventureInfo.identities[i]=roster.Identity(i).ToString();if(connected[i])adventureInfo.online|=(byte)(1<<i);}
            var bytes=Encoding.UTF8.GetBytes(JsonUtility.ToJson(adventureInfo));foreach(var id in net.ConnectedClientsIds)if(id!=0)Send(AdventureMessage,id,bytes,NetworkDelivery.ReliableSequenced);
        }
        void SubmitFinish(bool returnToRoom)
        {
            if(Host){finishVote.Submit(0,returnToRoom?-1:chosenMvp,returnToRoom?-1:chosenBlame,Time.unscaledTime);if(returnToRoom)returnedToRoom|=1;PersistPendingVote();}
            else Send(FinishActionMessage,0,new byte[]{(byte)(returnToRoom?2:1),unchecked((byte)(sbyte)chosenMvp),unchecked((byte)(sbyte)chosenBlame)},NetworkDelivery.ReliableSequenced);
        }
        byte OnlineMask {get{byte mask=0;for(int i=0;i<4;i++)if(connected[i])mask|=(byte)(1<<i);return mask;}}
        bool AllReturned=>reportSaved&&(returnedToRoom&OnlineMask)==OnlineMask;
        void ReturnFinishedRoom()
        {
            if(!Host||!AtFinish||!AllReturned)return;
            roster.ReturnToRoom();playerReady.ResetForConfiguration();phase=SteamCarryProtocol.Phase.Waiting;
            nextSnapshot=nextAdventureInfo=0;status="已回到原房间。请选择存档，或确认重新挑战。";
        }
        void DrawFinish()
        {
            Panel(new Rect(210,155,860,435));
            GameText.Label(new Rect(240,173,800,42),ReturnedFromFinish?"房间大厅 · 本轮完成":"搬运成功",heading);
            GameText.Label(new Rect(240,225,800,32),"用时 "+Duration(Host?progress.run.seconds:adventureInfo?.seconds??0)+" · 碎缸 "+(Host?progress.run.breaks:adventureInfo?.breaks??0)+" 次",label);
            byte candidates=Host?finishVote.Candidates:adventureInfo?.finalists??0;
            byte submitted=Host?finishVote.Submitted:adventureInfo?.submitted??0;
            bool saved=Host?reportSaved:adventureInfo?.reportSaved??false;
            float remaining=Host?Mathf.Max(0,finishVote.Deadline-Time.unscaledTime):adventureInfo?.voteSeconds??0;
            if(!ReturnedFromFinish&&!saved){
                GameText.Label(new Rect(240,268,800,28),"投票剩余 "+Mathf.CeilToInt(remaining)+" 秒 · 可弃权，不能选自己",small);
                GUI.enabled=(submitted&(1<<localSlot))==0&&remaining>0;
                int col=0;for(int i=0;i<4;i++)if(i!=localSlot&&(candidates&(1<<i))!=0){
                    string name=adventureInfo?.names?[i]??("P"+(i+1));float x=240+col++*265;
                    if(GameText.RawButton(new Rect(x,309,255,36),(chosenMvp==i?"✓ ":"")+"MVP · "+name,button))chosenMvp=i;
                    if(GameText.RawButton(new Rect(x,355,255,36),(chosenBlame==i?"✓ ":"")+GameText.Translate("背锅 · ")+name,button))chosenBlame=i;
                }
                if(GameText.Button(new Rect(240,405,385,40),"提交投票 / 弃权",button))SubmitFinish(false);GUI.enabled=true;
            }
            if(saved)GameText.RawLabel(new Rect(240,278,800,100),GameText.Translate("报告已保存。MVP：")+WinnerText(Host?finishVote.Winners(true):adventureInfo.mvp)+GameText.Translate("\n背锅王：")+WinnerText(Host?finishVote.Winners(false):adventureInfo.blame),label);
            if(saved)GameText.RawLabel(new Rect(240,340,800,62),adventureInfo?.behavior??"",small);
            if(!ReturnedFromFinish&&GameText.Button(new Rect(655,405,385,40),"返回原房间大厅",button))SubmitFinish(true);
            if(ReturnedFromFinish&&Host){GUI.enabled=AllReturned;if(GameText.Button(new Rect(240,405,800,40),AllReturned?"所有人已返回 · 进入房间大厅":"等待队友返回及报告保存",button))ReturnFinishedRoom();GUI.enabled=true;}
            if(GameText.Button(new Rect(240,480,800,40),"离开房间…",button))exitConfirmation=true;
        }
        string WinnerText(byte mask)
        {
            var names=new List<string>();for(int i=0;i<4;i++)if((mask&(1<<i))!=0)names.Add(adventureInfo?.names?[i]??PeerName(i));
            return names.Count==0?GameText.Translate("无人当选"):string.Join("、",names);
        }
        void DrawExitConfirmation()
        {
            Panel(new Rect(280,180,720,360));
            string explanation=exitSaveFailed?Ui("保存失败，尚未退出。可重试或返回检查；放弃未保存部分会丢失最近一次成功保存之后的时间与统计。已有磁盘存档保留。","Save failed; you are still in the room. Retry or go back. Discarding loses progress since the last successful save; existing save files are kept."):Host?GameText.Translate("退出会结束整个房间。已保存进度保留；未结束的结算将先截止投票并保存。"):GameText.Translate("确认主动离队？退出后无法重连本局，其他成员继续。");
            GameText.RawLabel(new Rect(305,200,670,105),explanation,label);
            if(exitSaveFailed)GameText.RawLabel(new Rect(305,310,670,65),Ui("请检查磁盘空间、写入权限及文件占用后重试。完整错误已记入运行日志。","Check free disk space, write permissions and file locks, then retry. Full details are in the runtime log."),small);
            if(GameText.Button(new Rect(305,389,320,42),"取消",button)){exitConfirmation=exitSaveFailed=discardExitConfirmed=false;}
            if(GameText.RawButton(new Rect(655,389,320,42),exitSaveFailed?Ui("重试保存并退出","Retry save and leave"):Ui("确认退出","Confirm leave"),button)){
                if(Host&&!RetryCurrentSave()){exitSaveFailed=true;discardExitConfirmed=false;return;}
                exitConfirmation=exitSaveFailed=discardExitConfirmed=false;ReturnToLobby();
            }
            if(exitSaveFailed&&GameText.RawButton(new Rect(305,460,670,42),discardExitConfirmed?Ui("确认放弃未保存部分并退出","Confirm discard unsaved progress and leave"):Ui("放弃未保存部分并退出…","Discard unsaved progress and leave…"),FrontSmallButton)){
                if(!discardExitConfirmed)discardExitConfirmed=true;
                else{exitSaveFailed=discardExitConfirmed=false;ReturnToLobby();}
            }
        }
    }
}
