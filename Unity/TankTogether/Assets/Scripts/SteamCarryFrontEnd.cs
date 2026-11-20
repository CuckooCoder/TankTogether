using System;
using Steamworks;
using TankTogether.M0;
using UnityEngine;
using static TankTogether.Networking.SessionRoster;
namespace TankTogether.Networking {
// Presentation only: permissions, saves and network transitions remain in the session owners.
public sealed partial class SteamCarrySession {
 int frontIntent, kickCandidate=-1; bool localTools;
 [NonSerialized] GUIStyle frontSmallButton;
 GUIStyle FrontSmallButton=>frontSmallButton??(frontSmallButton=new GUIStyle(GUI.skin.button){font=font,fontSize=15,wordWrap=true,normal={textColor=Color.white}});
 string SaveDisplayName(string value,int slot)=>string.IsNullOrWhiteSpace(value)?Ui("冒险 ","Adventure ")+slot:value;
 string CheckpointDisplay(string id){var route=lab.GetComponent<WhiteboxCarryLevel>();var cp=route?Array.Find(route.checkpoints,x=>x.id==id):null;return GameText.Translate(cp?.label??(id=="start"?"起点":id));}
 string frontNotice=""; float frontNoticeUntil;
 readonly LobbyCharacterPreview[] lobbyPortraits=new LobbyCharacterPreview[RequiredPlayers];
 static string Ui(string zh,string en)=>PlayerPreferences.Data.language==0?zh:en;
 void FrontNotice(string value){frontNotice=value;frontNoticeUntil=Time.unscaledTime+4;}
 public bool BeginCreateFlow(){if(!OpenSteamMenu())return false;frontIntent=1;if(initialized){OpenSaveMenu();createAfterSelecting=true;}return true;}
 public bool BeginJoinFlow(){if(!OpenSteamMenu())return false;frontIntent=2;return true;}
 void RetryFrontSteam(){InitializeSteam();if(initialized&&frontIntent==1){OpenSaveMenu();createAfterSelecting=true;}}
 void CloseSaveMenu(){bool home=createAfterSelecting&&!Running;showSaveMenu=false;confirmDelete=false;showReports=false;createAfterSelecting=false;if(home)ReturnToLobby();}
 string LobbyStartBlock {
 get {
  if(!Host)return Ui("等待房主开始","Waiting for host");
  if(phase==SteamCarryProtocol.Phase.Preparing)return Ui("正在加载，等待三位玩家就绪","Loading: waiting for all three players");
  if(!saveReady)return Ui("存档尚未就绪，请检查存档或重试","Save unavailable. Check the save or retry.");
  if(progress?.run?.finished==true&&!pendingReplay)return Ui("此存档已通关，请确认重新挑战或更换存档","Completed save: confirm replay or choose another save");
  if(PlayerCount<RequiredPlayers){int missing=RequiredPlayers-PlayerCount;return Ui($"还需要 {missing} 位队友加入（三人到齐才可开始）",$"Waiting for {missing} more to join (three players required)");}
  if(lab.GMOpen)return Ui("请先关闭 GM 参数面板","Close the GM panel first");
  for(int i=1;i<RequiredPlayers;i++)if(connected[i]&&(!configReady[i]||!peerReady[i]||Time.unscaledTime-lastInputTime[i]>2))return Ui("等待 P","Waiting for P")+(i+1)+Ui(" 完成连接与参数同步"," to connect and sync settings");
  if(!PlayersConsented)return Ui("等待所有队友点击准备","Waiting for every guest to ready up");
  if(lab.Broken&&!lab.FailureReady)return Ui("正在恢复检查点","Restoring checkpoint");
  return null;
 }}
 void FrontHeader(string title,string subtitle){Panel(new Rect(40,30,1200,100));GameText.RawLabel(new Rect(65,45,1100,38),title,heading);GameText.RawLabel(new Rect(65,90,1100,30),subtitle,small);}
 void DrawHome(){
  FrontHeader("搬鱼缸 / TANK TOGETHER",Ui("与朋友一起，把这一缸平安搬到终点。","Carry it together. Bring the tank home in one piece."));
  Panel(new Rect(40,150,740,405));
  GameText.RawLabel(new Rect(70,177,680,40),Ui("开始合作冒险","Start a co-op adventure"),heading);
  GameText.RawLabel(new Rect(70,230,680,58),Ui("三人合作。房主选择存档，两位队友加入。\n共同进度保存在房主账号下。","Three-player co-op. The host chooses a save and two guests join.\nShared progress belongs to the host."),label);
  GUI.enabled=CanChooseMode;
  if(GameText.RawButton(new Rect(70,315,325,66),Ui("创建游戏","Create game"),button))BeginCreateFlow();
  if(GameText.RawButton(new Rect(415,315,335,66),Ui("加入游戏","Join game"),button))BeginJoinFlow();
  if(GameText.Button(new Rect(70,405,325,46),"个人设置",button))OpenSettings();
  if(GameText.Button(new Rect(415,405,335,46),"角色装扮",button))OpenCosmetic();
  if(reconnectRoom!=0){if(GameText.RawButton(new Rect(70,478,680,48),Ui("重试上次房间（须原房主仍在线）","Retry last room (host must still be online)"),FrontSmallButton))RetryLastRoom();}
  else GameText.RawLabel(new Rect(70,478,680,50),Ui("创建：选择存档 → 邀请好友 → 准备 → 出发","Create: choose save → invite friends → ready → start"),small);
  Panel(new Rect(805,150,435,405));
  GameText.RawLabel(new Rect(835,180,375,35),Ui("本地测试","Local testing"),heading);
  GameText.RawLabel(new Rect(835,230,375,75),Ui("一个人调手感、跑关卡。\n无需 Steam，不写入合作存档。","Test movement and levels solo.\nNo Steam needed. Co-op saves are untouched."),label);
  if(GameText.RawButton(new Rect(835,323,375,54),Ui("进入关卡 · 伸缩 B","Play level · Stretch B"),button))StartLocalStretchLevel();
  if(GameText.RawButton(new Rect(835,392,375,38),localTools?Ui("收起对照工具","Hide comparison tools"):Ui("更多测试工具…","More test tools…"),button))localTools=!localTools;
  if(localTools){if(GameText.RawButton(new Rect(835,448,180,42),"A · "+Ui("旧版对照","Original"),button))StartLocalTest();if(GameText.RawButton(new Rect(1025,448,185,42),"B0 · "+Ui("伸缩沙盒","Sandbox"),button))StartLocalStretchLevel(false);}
  GUI.enabled=true;
  stretchNetwork=GameText.Toggle(new Rect(65,579,920,30),stretchNetwork,"Steam 启用伸缩实验 B（所有队友选择一致；Q/E 变高变矮）");
  if(GameText.Button(new Rect(1010,575,205,44),"退出游戏",button))Application.Quit();
  GameText.RawLabel(new Rect(65,643,1150,48),CanChooseMode?(!string.IsNullOrEmpty(status)&&status!="选择本地模拟或 Steam 好友联机。"?GameText.Translate(status):Ui("内部测试版本 · 请与队友使用相同版本","Internal test · All players must use the same build")):Ui("正在结束上一段连接，请稍候…","Closing the previous connection…"),small);
 }
 void DrawSteamEntry(){
  FrontHeader(Ui("好友合作","Play with friends"),GameText.Translate(status));
  Panel(new Rect(190,155,900,490));
  GameText.RawLabel(new Rect(225,180,830,40),frontIntent==1?Ui("创建游戏 · 选择冒险存档","Create game · Choose adventure"):Ui("加入朋友的房间","Join your friend's room"),heading);
  GameText.RawLabel(new Rect(225,238,830,55),Ui("先登录桌面 Steam。凭房间码加入，无须先互加好友。\n加入朋友的旅程不会覆盖你自己的存档。","Sign into the Steam desktop app. Join by room ID; friendship is optional.\nJoining does not overwrite your own saves."),label);
  bool available=initialized&&!busy&&!leaving&&net==null;GUI.enabled=available;
  GameText.RawLabel(new Rect(225,315,830,28),Ui("房间码","Room ID"),small);
  GUI.SetNextControlName("room-code");roomInput=GUI.TextField(new Rect(225,349,530,46),roomInput,24,field);
  if(GameText.RawButton(new Rect(770,349,120,46),Ui("粘贴","Paste"),button))roomInput=GUIUtility.systemCopyBuffer.Trim();
  GUI.enabled=available&&ulong.TryParse(roomInput.Trim(),out ulong parsed)&&parsed>0;
  if(GameText.Button(new Rect(905,349,150,46),"加入房间",button)){roomInput=roomInput.Trim();JoinRoom();}GUI.enabled=available;
  if(!string.IsNullOrEmpty(queuedInvite)&&GameText.Button(new Rect(225,415,830,42),"加入刚才邀请的房间",button)){roomInput=queuedInvite;queuedInvite="";JoinRoom();}
  if(GameText.Button(new Rect(225,477,400,42),"创建房间 · 选择存档",button)){frontIntent=1;OpenSaveMenu();createAfterSelecting=true;}
  if(GameText.Button(new Rect(645,477,410,42),"管理 / 选择五个存档",button)){frontIntent=3;OpenSaveMenu();}
  GUI.enabled=true;
  if(!initialized&&!busy&&!leaving&&GameText.Button(new Rect(225,415,830,42),"重新连接 Steam",button))RetryFrontSteam();
  if((busy||net!=null)&&GameText.Button(new Rect(225,535,830,42),"取消连接",button))Leave();
  else if(GameText.RawButton(new Rect(225,582,830,40),Ui("返回主菜单","Back to main menu"),button))ReturnToLobby();
 }
 void DrawRoomLobby(){
  bool loading=phase==SteamCarryProtocol.Phase.Preparing;
  FrontHeader(Ui("好友大厅","Friends lobby")+"  ·  "+PlayerCount+" / "+RequiredPlayers,Ui("邀请好友 → 两位队友准备 → 房主开始","Invite friends → both guests ready → host starts"));
  for(int i=0;i<RequiredPlayers;i++){
   float x=40+(1200-RequiredPlayers*291-(RequiredPlayers-1)*12)/2f+i*303;Panel(new Rect(x,148,291,300));
   bool present=connected[i];string name=present?(Host?PeerName(i):adventureInfo?.names?[i]??("P"+(i+1))):Ui("等待加入","Waiting for player");
   DrawAvatar(i,new Rect(x+15,160,28,28));
   var nameStyle=new GUIStyle(label){wordWrap=false,clipping=TextClipping.Clip};GUI.Label(new Rect(x+50,158,225,32),new GUIContent(name,name),nameStyle);
   if(present){
    if(lobbyPortraits[i]==null)lobbyPortraits[i]=new LobbyCharacterPreview(lab.playerVisualPrefab,i);
    int color=Host?(cosmetics.TryGetValue(roster.Identity(i),out int c)?c:i):adventureInfo?.colors?[i]??i;
    ApplyCosmetic(lobbyPortraits[i].Model,color);GUI.DrawTexture(new Rect(x+18,196,255,175),lobbyPortraits[i].Texture,ScaleMode.ScaleToFit);
   }else{if(lobbyPortraits[i]!=null){lobbyPortraits[i].Dispose();lobbyPortraits[i]=null;}GameText.RawLabel(new Rect(x+85,260,180,40),"+",heading);}
   string state=!present?Ui("空位","Empty slot"):loading?Ui("加载中","Loading"):i==0?Ui("房主","Host"):(DisplayReadyMask&(1<<i))!=0?Ui("已准备","Ready"):Ui("未准备","Not ready");
   GameText.RawLabel(new Rect(x+16,372,259,30),"P"+(i+1)+" · "+state+(i==localSlot?Ui(" · 你"," · You"):""),label);
   GUI.enabled=present&&i==localSlot&&!loading&&(Host?!roster.Locked:ReadyMutable);
   if(i==localSlot&&GameText.Button(new Rect(x+16,408,259,30),"角色装扮",FrontSmallButton))OpenCosmetic();GUI.enabled=true;
   if(Host&&!roster.Locked&&i>0&&present&&GameText.RawButton(new Rect(x+16,408,259,30),kickCandidate==i?Ui("确认移出此成员","Confirm removal"):Ui("移出成员…","Remove player…"),FrontSmallButton)){if(kickCandidate==i){KickLobbyMember(i);kickCandidate=-1;}else kickCandidate=i;}
  }
  Panel(new Rect(40,462,740,226));Panel(new Rect(797,462,443,226));
  string saveName=Host?progress?.displayName:adventureInfo?.name;
  GameText.RawLabel(new Rect(60,475,700,28),Ui("房主存档","Host save")+" "+(Host?selectedSaveSlot:adventureInfo?.slot??1)+" · "+SaveDisplayName(saveName,Host?selectedSaveSlot:adventureInfo?.slot??1),label);
  bool replay=Host?pendingReplay:adventureInfo?.pendingReplay??false;
  string cp=lab.GetComponent<WhiteboxCarryLevel>()?lab.GetComponent<WhiteboxCarryLevel>().CheckpointLabel:GameText.Translate("起点");
  GameText.RawLabel(new Rect(60,509,700,28),replay?Ui("本场计划从起点重新挑战 · 旧记录保留","Replay queued from Start · previous records kept"):GameText.Translate("检查点："+cp)+" · "+Duration(Host?progress?.run?.seconds??0:adventureInfo?.seconds??0),small);
  GUI.enabled=Host&&!roster.Locked&&!loading;
  if(GameText.RawButton(new Rect(60,546,210,34),Ui("切换 / 管理存档","Choose / manage save"),FrontSmallButton))OpenSaveMenu();
  bool finished=Host?progress?.run?.finished==true:adventureInfo?.runFinished==true;
  if(finished&&Host&&GameText.RawButton(new Rect(285,546,475,34),replay?Ui("取消待重新挑战","Cancel planned replay"):confirmReplay?Ui("确认重新挑战（历史保留）","Confirm replay (keep history)"):Ui("重新挑战…","Replay…"),FrontSmallButton)){
   if(replay){pendingReplay=false;confirmReplay=false;playerReady.ResetForConfiguration();nextAdventureInfo=0;}
   else if(!confirmReplay)confirmReplay=true;
   else{pendingReplay=true;confirmReplay=false;playerReady.ResetForConfiguration();nextAdventureInfo=0;}
  }GUI.enabled=true;
  GameText.RawLabel(new Rect(60,588,700,30),Ui("房间码：","Room ID: ")+(lobby.HasValue?lobby.Value.Id.ToString():"—"),small);
  GUI.enabled=lobby.HasValue&&!loading;
  if(GameText.Button(new Rect(60,632,210,36),"邀请 Steam 好友",FrontSmallButton))InviteFriends();
  if(GameText.Button(new Rect(285,632,210,36),"复制房间号",FrontSmallButton)){GUIUtility.systemCopyBuffer=lobby.Value.Id.ToString();FrontNotice(Ui("房间码已复制","Room ID copied"));}GUI.enabled=true;
  if(GameText.Button(new Rect(510,632,250,36),"个人设置",FrontSmallButton))OpenSettings();
  string block=LobbyStartBlock;
  GameText.RawLabel(new Rect(817,478,403,68),Host?(block??Ui("队友已准备，可以出发","Everyone is ready. Let's go!")):Ui("准备后等待房主开始；修改装扮会取消准备。","Ready up and wait for the host. Changing appearance cancels ready."),small);
  GUI.enabled=Host?block==null||!saveReady:ReadyMutable&&target!=null&&configRevision==target.configRevision;
  bool ready=localSlot>=0&&(DisplayReadyMask&(1<<localSlot))!=0;
  if(GameText.RawButton(new Rect(817,553,403,48),Host?(!saveReady?Ui("重试保存当前进度","Retry saving current progress"):loading?Ui("加载中…","Loading…"):Ui("开始 / 继续冒险","Start / continue adventure")):(ready?Ui("取消准备","Cancel ready"):Ui("准备","Ready")),button)){if(Host){if(!saveReady)RetryCurrentSave();else StartOrResume();}else SetLocalPlayerReady(!ready);}GUI.enabled=true;
  if(GameText.RawButton(new Rect(817,616,403,38),Host?Ui("解散房间…","Disband room…"):Ui("离开房间…","Leave room…"),FrontSmallButton))exitConfirmation=true;
  GameText.RawLabel(new Rect(65,691,1150,25),Time.unscaledTime<frontNoticeUntil?frontNotice:GameText.Translate(status.Contains("失败")||status.Contains("超时")||status.Contains("断开")?status:"共同进度由房主保存。"),small);
 }
 void ClearLobbyPortraits(){foreach(var p in lobbyPortraits)p?.Dispose();Array.Clear(lobbyPortraits,0,RequiredPlayers);kickCandidate=-1;}
}
}
