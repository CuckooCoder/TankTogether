using System;
using System.Collections.Generic;
namespace TankTogether.Networking
{
    // Steam identity owns a seat; NGO connection ids are disposable transport handles.
    public sealed class SessionRoster
    {
        public const int RequiredPlayers = 3;
        readonly ulong[] identities = new ulong[RequiredPlayers];
        readonly bool[] eligible = new bool[RequiredPlayers];
        readonly Dictionary<ulong, int> connections = new Dictionary<ulong, int>();
        readonly HashSet<ulong> banned = new HashSet<ulong>();
        public bool Locked { get; private set; }
        public ulong Identity(int slot) => slot >= 0 && slot < RequiredPlayers ? identities[slot] : 0;
        public bool CanReconnect(int slot) => slot >= 0 && slot < RequiredPlayers && eligible[slot];
        public bool TryGetSlot(ulong connection, out int slot) => connections.TryGetValue(connection, out slot);
        public void Begin(ulong host)
        {
            Clear(); identities[0] = host; eligible[0] = true; connections[0] = 0;
        }
        public bool Admit(ulong identity, ulong connection, out int slot, out string reason)
        {
            slot = -1; reason = "";
            if (identity == 0 || connection == 0) { reason = "无法确认 Steam 身份"; return false; }
            if (banned.Contains(identity)) { reason = "你已被移出此房间"; return false; }
            if (connections.ContainsKey(connection)) { reason = "连接已存在"; return false; }
            slot = Array.IndexOf(identities, identity);
            if (slot >= 0 && connections.ContainsValue(slot)) { reason = "此 Steam 身份已在房间内"; return false; }
            if (Locked && (slot < 0 || !eligible[slot])) { reason = "对局已开始，仅允许原成员重连"; return false; }
            if (slot < 0) slot = Array.FindIndex(identities, x => x == 0);
            if (slot < 1) { reason = "房间已满"; return false; }
            identities[slot] = identity; eligible[slot] = true; connections.Add(connection, slot); return true;
        }
        public bool Lock()
        {
            if (Locked) return true;
            if (!connections.ContainsKey(0) || connections.Count != RequiredPlayers) return false;
            Locked = true; return true;
        }
        public void Disconnect(ulong connection, bool voluntary = false)
        {
            if (!connections.TryGetValue(connection, out int slot) || slot == 0) return;
            connections.Remove(connection);
            if (!Locked) { identities[slot] = 0; eligible[slot] = false; }
            else if (voluntary) eligible[slot] = false;
        }
        public bool Kick(ulong connection)
        {
            if (Locked || !connections.TryGetValue(connection, out int slot) || slot == 0) return false;
            banned.Add(identities[slot]); Disconnect(connection); return true;
        }
        public void ReturnToRoom()
        {
            Locked=false;
            for(int i=1;i<RequiredPlayers;i++)if(!connections.ContainsValue(i)){identities[i]=0;eligible[i]=false;}
        }
        public void Clear()
        {
            Array.Clear(identities, 0, RequiredPlayers); Array.Clear(eligible, 0, RequiredPlayers);
            connections.Clear(); banned.Clear(); Locked = false;
        }
    }
}
