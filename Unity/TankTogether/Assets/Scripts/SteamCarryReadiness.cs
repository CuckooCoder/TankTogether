using Unity.Collections;
using Unity.Netcode;
namespace TankTogether.Networking
{
    public sealed partial class SteamCarrySession
    {
        const string PlayerReadyMessage = "tt.player-ready.1";
        readonly RoomReadyState playerReady = new RoomReadyState();
        byte DisplayReadyMask => Host ? playerReady.ReadyMask : target != null ? target.readyMask : (byte)0;
        bool PlayersConsented => playerReady.CanStart;
        bool ReadyMutable => phase == SteamCarryProtocol.Phase.Waiting || phase == SteamCarryProtocol.Phase.Paused || phase == SteamCarryProtocol.Phase.Failed;
        void ReceivePlayerReady(ulong sender, FastBufferReader reader)
        {
            if(!Host||!ReadyMutable||sender==NetworkManager.ServerClientId||!slots.TryGetValue(sender,out int slot)||reader.Length-reader.Position!=9)return;
            reader.ReadValueSafe(out int revision);reader.ReadValueSafe(out int configuration);reader.ReadValueSafe(out byte value);
            if(value>1||configuration!=configRevision||!configReady[slot])return;
            if(playerReady.SetReady(slot,value==1,revision))nextSnapshot=0;
        }
        public bool SetLocalPlayerReady(bool value)
        {
            if(Host||!Running||!ReadyMutable||target==null||localSlot<1||configRevision!=target.configRevision)return false;
            using var w=new FastBufferWriter(9,Allocator.Temp);
            w.WriteValueSafe(target.readyRevision);w.WriteValueSafe(configRevision);w.WriteValueSafe((byte)(value?1:0));
            net.CustomMessagingManager.SendNamedMessage(PlayerReadyMessage,NetworkManager.ServerClientId,w,NetworkDelivery.ReliableSequenced);
            return true; // UI changes only after the host snapshot confirms this request.
        }
    }
}
