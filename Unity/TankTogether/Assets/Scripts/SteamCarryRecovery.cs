using UnityEngine;
namespace TankTogether.Networking {
    public sealed partial class SteamCarrySession {
        ulong reconnectRoom;
        bool exitSaveFailed,discardExitConfirmed;
        // A retry is only an attempt. Steam transport identity and the host roster decide admission.
        public bool RetryLastRoom(){
            if(reconnectRoom==0||!CanChooseMode)return false;
            if(!OpenSteamMenu())return false;
            roomInput=reconnectRoom.ToString();JoinRoom();return true;
        }
        void ConnectionLost(string reason){
            bool departing=exitPending;
            if(departing)reconnectRoom=0;
            Mode=EntryMode.Home;
            Leave(departing?"已退出房间。":"与房主的连接已断开。可重试原房间；若房主已退出，需由房主重建。仅保留最近成功保存的进度。"+reason);
        }
        void CheckConnectionHealth(){
            if(leaving)return;
            if(busy&&Time.unscaledTime-connectAt>25){Mode=EntryMode.Home;Leave("Steam 房间请求超时，请检查网络后重试。");return;}
            if(net==null||!net.IsClient||Host)return;
            if(localSlot<0&&Time.unscaledTime-connectAt>25){ConnectionLost("连接超时。");return;}
            if(localSlot>=0&&Time.unscaledTime-lastSnapshotAt>25)ConnectionLost("同步超时，原会话状态无法确认。");
        }
        bool RetryCurrentSave(){
            if(!Host)return false;
            if(AtFinish){finishVote.Close();PersistPendingVote();bool reportOk=SaveFinalReport();if(!reportOk)Log("save_retry_failed "+saveStatus);return reportOk;}
            bool ok=PersistRun();if(ok){status="进度已保存，请重新准备后继续。";nextAdventureInfo=0;}else Log("save_retry_failed "+saveStatus);return ok;
        }
    }
}
