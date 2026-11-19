using System.Collections;
using TankTogether.M0;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
namespace TankTogether.Networking
{
    public sealed partial class SteamCarrySession
    {
        const string RoomActionMessage = "tt.room-action.1";
        readonly CheckpointVote resetVote = new CheckpointVote();
        bool exitPending, exitAcknowledged;
        void RegisterRoomActions() => net.CustomMessagingManager.RegisterNamedMessageHandler(RoomActionMessage, ReceiveRoomAction);
        void ReceiveRoomAction(ulong sender, FastBufferReader reader)
        {
            if (reader.Length-reader.Position != 5) return;
            reader.ReadValueSafe(out byte action); reader.ReadValueSafe(out int revision);
            if (!Host) {
                if (sender == NetworkManager.ServerClientId && action == 2 && exitPending) exitAcknowledged = true;
                return;
            }
            if (sender == NetworkManager.ServerClientId || !slots.TryGetValue(sender,out int slot)) return;
            if (action == 1) {
                roster.Disconnect(sender,true);
                SendRoomAction(sender,2,0);
                // Detach now; reliable acknowledgement precedes client-side shutdown.
                OnDisconnected(sender);StartCoroutine(CloseDepartedConnection(sender));
            } else if (action == 3 && IsPlaying) resetVote.Agree(slot,revision,Time.unscaledTime);
        }
        IEnumerator CloseDepartedConnection(ulong id)
        {
            yield return new WaitForSecondsRealtime(.5f);
            if(Host&&net.ConnectedClients.ContainsKey(id))net.DisconnectClient(id,"已主动离队。");
        }
        void SendRoomAction(ulong recipient, byte action, int revision)
        {
            using var w = new FastBufferWriter(5,Allocator.Temp);w.WriteValueSafe(action);w.WriteValueSafe(revision);
            net.CustomMessagingManager.SendNamedMessage(RoomActionMessage,recipient,w,NetworkDelivery.ReliableSequenced);
        }
        IEnumerator DepartRoom()
        {
            exitPending=true;exitAcknowledged=false;Capture(false);
            SendRoomAction(NetworkManager.ServerClientId,1,0);
            float until=Time.unscaledTime+3;
            while(!exitAcknowledged && net!=null && Time.unscaledTime<until) yield return null;
            exitPending=false; Mode=EntryMode.Home; Leave(exitAcknowledged?"已主动离队；本局不能重连。":"退出确认未返回，连接已关闭。");
        }
        public bool KickLobbyMember(int slot)
        {
            if (!Host || roster.Locked || slot<1 || slot>3) return false;
            ulong id=0;foreach(var pair in slots)if(pair.Value==slot)id=pair.Key;
            if(id==0 || !roster.Kick(id))return false;
            net.DisconnectClient(id,"你已被房主移出本房间");OnDisconnected(id);return true;
        }
        void UpdateRoomActions()
        {
            if(!Running)return;
            if(Host && resetVote.Active) {
                if(!IsPlaying)resetVote.Cancel();
                else if(resetVote.Passed) {
                    resetVote.Cancel();if(progress?.run!=null){progress.run.resets++;PersistRun();}round++;PrepareRound();
                    // This is a consented reset of the current run, not a fresh two-player start.
                    phase=SteamCarryProtocol.Phase.Playing;lab.SetSessionPaused(false);status="已按全员同意返回最近检查点。";
                } else if(Time.unscaledTime>=resetVote.Deadline) {resetVote.Cancel();status="复位投票未全员通过，继续当前搬运。";}
            }
            if(!Host && target!=null && target.voteMask!=0 && IsPlaying && !lab.GMOpen && Keyboard.current!=null && Application.isFocused && !steamOverlayActive && bindingIndex<0 && !textInputFocused && PlayerPreferences.Pressed(PlayerAction.AgreeReset))
                SendRoomAction(NetworkManager.ServerClientId,3,target.voteRevision);
        }
        void DrawResetVote()
        {
            byte mask=Host?resetVote.Voters:target?.voteMask??0;
            if(mask==0)return;
            byte yes=Host?resetVote.Yes:target.voteYes;
            float seconds=Host?resetVote.Deadline-Time.unscaledTime:target.voteSeconds;
            Panel(new Rect(370,150,540,92));
            GameText.Label(new Rect(388,160,504,72),"返回检查点投票 · "+Mathf.CeilToInt(Mathf.Max(0,seconds))+" 秒\n"+"同意 "+CountBits(yes)+" / "+CountBits(mask)+" · "+GameText.Translate("同意复位投票")+": "+PlayerPreferences.KeyName(PlayerPreferences.Data.keys[(int)PlayerAction.AgreeReset]),label);
        }
        static int CountBits(byte b) {int n=0;for(int i=0;i<4;i++)if((b&(1<<i))!=0)n++;return n;}
    }
}
