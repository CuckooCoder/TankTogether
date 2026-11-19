using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;
namespace TankTogether.Networking
{
    public sealed partial class SteamCarrySession
    {
#if UNITY_EDITOR
        [NonSerialized] public string EditorSaveRoot;
        string SaveRoot => EditorSaveRoot;
#elif DEVELOPMENT_BUILD
        string SaveRoot { get {var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,"-test-save-root");return i>=0&&i+1<a.Length?a[i+1]:null;} }
#else
        string SaveRoot => null;
#endif
        int selectedSaveSlot=1, saveMenuSlot=1;
        bool createAfterSelecting, showSaveMenu, saveMenuInRoom, confirmDelete, confirmReplay, showReports;
        string renameDraft="",saveMenuMessage="";
        Vector2 reportScroll;
        readonly CoopCampaignSave.Result[] saveSlots=new CoopCampaignSave.Result[5];
        string SlotId(int slot)=>CoopCampaignSave.SlotCampaign(LevelId,slot);
        void OpenSaveMenu()
        {
            if(!initialized || (Running && (!Host||roster.Locked)))return;
            createAfterSelecting=false;Capture(false);showSaveMenu=true;saveMenuInRoom=Host;saveMenuSlot=selectedSaveSlot;confirmDelete=false;showReports=false;saveMenuMessage="";RefreshSaveSlots();
        }
        void RefreshSaveSlots()
        {
            for(int i=0;i<5;i++)saveSlots[i]=CoopCampaignSave.Load(SteamClient.SteamId.Value,SlotId(i+1),ContentVersion,KnownCheckpoint,SaveRoot);
            renameDraft=SaveDisplayName(saveSlots[saveMenuSlot-1]?.progress?.displayName,saveMenuSlot);
        }
        bool SelectSaveSlot(int slot)
        {
            if(slot<1||slot>5 || (Running && (!Host||roster.Locked)))return false;
            int old=selectedSaveSlot;var oldProgress=progress;bool oldReady=saveReady;bool oldReport=reportSaved;selectedSaveSlot=slot;
            if(Host && !LoadCampaign()){selectedSaveSlot=old;progress=oldProgress;saveReady=oldReady;reportSaved=oldReport;saveMenuMessage=saveStatus;return false;}
            pendingReplay=false;confirmReplay=false;
            if(Host){playerReady.ResetForConfiguration();PrepareRound();nextAdventureInfo=0;}
            showSaveMenu=false;if(createAfterSelecting){createAfterSelecting=false;CreateRoom();}return true;
        }
        void DrawSaveMenu()
        {
            Panel(new Rect(110,70,1060,590));
            GameText.Label(new Rect(140,90,950,40),createAfterSelecting?"选择存档并创建房间":"房主的五个冒险存档",heading);
            for(int i=0;i<5;i++) {
                var save=saveSlots[i];string title=save?.success==true?SaveDisplayName(save.progress.displayName,i+1):save?.status==CoopCampaignSave.Status.Missing?GameText.Translate("空存档"): GameText.Translate("读取异常");
                if(GameText.RawButton(new Rect(140,150+i*82,290,70),(saveMenuSlot==i+1?"✓ ":"")+(i+1)+" · "+title+(save?.success==true?"\n"+CheckpointDisplay(save.progress.checkpointId)+" · "+Duration(save.progress.run?.seconds??0)+" · "+GameText.Translate(save.progress.run?.finished==true?"已通关":"冒险中"):""),button)){
                    saveMenuSlot=i+1;confirmDelete=false;showReports=false;saveMenuMessage="";renameDraft=SaveDisplayName(save?.progress?.displayName,saveMenuSlot);
                }
            }
            var selected=saveSlots[saveMenuSlot-1];var data=selected?.progress;
            if(selected?.success==true){
                GameText.Label(new Rect(465,150,650,75),"检查点："+CheckpointDisplay(data.checkpointId)+" · "+(data.run?.finished==true?"已通关":"冒险中")+"\n累计 "+Duration(data.run?.seconds??0)+" · 历史通关 "+(data.history?.Count??0)+" 次\n"+GameText.Translate("最好用时：")+BestTime(data.history),label);
                GUI.SetNextControlName("save-name");renameDraft=GUI.TextField(new Rect(465,240,450,38),renameDraft,32,field);
                if(GameText.Button(new Rect(930,240,190,38),"保存名称",button)){
                    if(string.IsNullOrWhiteSpace(renameDraft))saveMenuMessage=Ui("名称不能为空。","Name cannot be empty.");else{
                        var next=CoopCampaignSave.Copy(data);next.displayName=renameDraft.Trim();var result=CoopCampaignSave.Save(next,KnownCheckpoint,SaveRoot);saveMenuMessage=result.message;
                        if(result.success && Host && selectedSaveSlot==saveMenuSlot){progress=result.progress;nextAdventureInfo=0;}RefreshSaveSlots();
                    }
                }
                if(GameText.Button(new Rect(465,300,655,44),createAfterSelecting?"使用此存档创建房间":"选择此存档",button))SelectSaveSlot(saveMenuSlot);
                if(GameText.Button(new Rect(465,360,315,40),showReports?"收起历史":"查看历次通关报告",button)){showReports=!showReports;confirmDelete=false;}
                GUI.enabled=!saveMenuInRoom;
                if(GameText.Button(new Rect(800,360,320,40),confirmDelete?"确认永久删除进度与历史":"删除此存档…",button)){
                    if(!confirmDelete){confirmDelete=true;showReports=false;}
                    else {saveMenuMessage=CoopCampaignSave.Delete(SteamClient.SteamId.Value,SlotId(saveMenuSlot),ContentVersion,data.revision,KnownCheckpoint,SaveRoot).message;confirmDelete=false;RefreshSaveSlots();}
                }
                GUI.enabled=true;
                if(confirmDelete)GameText.Label(new Rect(465,410,650,45),"再次点击才会删除；选择其他存档或返回即可取消。",label);
                if(showReports){
                    string text=GameText.Translate("尚无历史通关记录。");
                    if(data.history!=null&&data.history.Count>0){var rows=new List<string>();foreach(var r in data.history){rows.Add(r.completedUtc+" · "+Duration(r.seconds)+GameText.Translate(" · 碎缸 ")+r.breaks+"\nMVP: "+ReportNames(r,r.mvp)+GameText.Translate(" / 背锅王：")+ReportNames(r,r.blame)+"\n"+BehaviorSummary(r));}text=string.Join("\n\n",rows);}
                    var box=new Rect(465,410,650,135);float contentHeight=Mathf.Max(125,label.CalcHeight(new GUIContent(text),610));
                    reportScroll=GUI.BeginScrollView(box,reportScroll,new Rect(0,0,620,contentHeight));GameText.RawLabel(new Rect(0,0,610,contentHeight),text,label);GUI.EndScrollView();
                }
            }else if(selected?.status==CoopCampaignSave.Status.Missing){
                GameText.Label(new Rect(465,160,650,80),"空存档。新冒险从起点开始，其他存档保留。",label);
                if(GameText.Button(new Rect(465,270,650,48),"新建冒险并选择",button)){
                    var created=CoopCampaignSave.Create(SteamClient.SteamId.Value,SlotId(saveMenuSlot),LevelId,"start",ContentVersion,KnownCheckpoint,SaveRoot);saveMenuMessage=created.message;
                    if(created.success){RefreshSaveSlots();SelectSaveSlot(saveMenuSlot);}
                }
            }else GameText.Label(new Rect(465,160,650,200),selected?.message??"读取中",label);
            GameText.Label(new Rect(465,550,650,42),saveMenuMessage,small);
            if(GameText.Button(new Rect(140,595,980,42),"返回",button))CloseSaveMenu();
        }
        static string Duration(double seconds){var t=TimeSpan.FromSeconds(Math.Max(0,seconds));return ((long)t.TotalHours).ToString("00")+t.ToString(@"\:mm\:ss");}
        static string BestTime(List<AdventureReport> reports){if(reports==null||reports.Count==0)return "—";double best=double.MaxValue;foreach(var r in reports)best=Math.Min(best,r.seconds);return Duration(best);}
        static string BehaviorSummary(AdventureReport report)
        {
            if(report?.members==null||report.members.Count==0)return "";
            var lines=new List<string>();
            for(int kind=0;kind<3;kind++){
                int max=0;var names=new List<string>();
                foreach(var m in report.members){int n=kind==0?m.jumps:kind==1?m.grabs:m.falls;if(n>max){max=n;names.Clear();}if(n==max&&n>0)names.Add(m.name??m.steamId);}
                if(max>0)lines.Add(GameText.Translate(new[]{"跳跃最多：","抓取最多：","坠落最多："}[kind])+string.Join(" / ",names)+" ("+max+")");
            }
            return string.Join("\n",lines);
        }
        static string ReportNames(AdventureReport report,List<string> ids)
        {
            if(ids==null||ids.Count==0)return GameText.Translate("无人当选");
            return string.Join("、",ids.ConvertAll(id=>report.members?.Find(m=>m.steamId==id)?.name??id));
        }
    }
}
