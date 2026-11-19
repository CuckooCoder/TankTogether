using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TankTogether.Networking;
namespace TankTogether.Editor
{
    public static class SessionRosterChecks
    {
        public static string Result = "Not run";
        [MenuItem("Tank Together/Formal/Run Identity Checks")]
        public static void Run()
        {
            var lines = new List<string>(); bool passed = true;
            void Check(string name, bool ok) { lines.Add(name+"="+ok); passed &= ok; }
            var r = new SessionRoster(); r.Begin(100);
            Check("host_alone_not_lockable", !r.Lock());
            Check("zero_identity_rejected", !r.Admit(0,1,out _,out _));
            Check("host_impersonation_rejected", !r.Admit(100,1,out _,out _));
            Check("two_players_admitted", r.Admit(200,1,out int seat,out _) && seat==1);
            Check("duplicate_identity_rejected", !r.Admit(200,2,out _,out _));
            Check("duplicate_connection_rejected", !r.Admit(300,1,out _,out _));
            Check("two_players_cannot_lock", !r.Lock());
            Check("third_player_admitted", r.Admit(300,2,out seat,out _) && seat==2);
            Check("fourth_player_rejected", !r.Admit(400,3,out _,out string reason) && reason=="房间已满");
            Check("three_players_lock", r.Lock()); r.Disconnect(1);
            Check("newcomer_cannot_steal_reserved_seat", !r.Admit(400,3,out _,out _));
            Check("same_steam_new_connection_same_seat", r.Admit(200,99,out seat,out _) && seat==1);
            Check("old_disconnect_cannot_remove_reconnected_peer", !r.TryGetSlot(1,out _) && r.TryGetSlot(99,out seat)&&seat==1);
            r.Disconnect(99,true);Check("voluntary_departure_revokes_rejoin", !r.Admit(200,1000,out _,out _));
            r.Begin(100);r.Admit(200,1,out _,out _);Check("lobby_kick", r.Kick(1));
            Check("kicked_steam_cannot_rejoin", !r.Admit(200,2,out _,out _));
            Check("kicked_seat_can_be_reused",r.Admit(300,3,out seat,out _)&&seat==1);
            r.Disconnect(3);Check("normal_lobby_leave_frees_seat",r.Admit(400,4,out seat,out _)&&seat==1);
            r.Begin(100);Check("new_room_clears_ban",r.Admit(200,2,out _,out _));
            Result=string.Join("\n",lines)+"\npassed="+passed;File.WriteAllText("Logs/formal-identity.txt",Result);Debug.Log(Result);
        }
    }
}
