using System;
using static TankTogether.Networking.SessionRoster;
namespace TankTogether.Networking
{
    // Player consent is separate from transport/configuration readiness.
    public sealed class RoomReadyState
    {
        readonly bool[] members = new bool[RequiredPlayers];
        readonly bool[] ready = new bool[RequiredPlayers];
        public int Revision { get; private set; } = 1;
        public byte ReadyMask { get { byte mask=0; for(int i=0;i<RequiredPlayers;i++) if(members[i]&&(i==0||ready[i])) mask|=(byte)(1<<i); return mask; } }
        public bool CanStart => ReadyMask == (1 << RequiredPlayers) - 1;
        public void SetConnected(int slot, bool connected)
        { if(slot<0||slot>=RequiredPlayers)throw new ArgumentOutOfRangeException(nameof(slot));if(members[slot]==connected)return;members[slot]=connected;ready[slot]=false; }
        public bool SetReady(int slot, bool value, int expectedRevision)
        { if(slot<1||slot>=RequiredPlayers||!members[slot]||expectedRevision!=Revision)return false;ready[slot]=value;return true; }
        public void ResetForConfiguration() { Revision++;Array.Clear(ready,0,RequiredPlayers); }
        public void Clear() { Revision++;Array.Clear(members,0,RequiredPlayers);Array.Clear(ready,0,RequiredPlayers); }
    }
}
