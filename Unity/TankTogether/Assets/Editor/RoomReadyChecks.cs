using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TankTogether.Networking;
namespace TankTogether.Editor
{
    public static class RoomReadyChecks
    {
        public static string Result="Not run";
        [MenuItem("Tank Together/Formal/Run Ready Checks")]
        public static void Run()
        {
            var rows=new List<string>();bool pass=true;
            void Check(string name,bool ok){rows.Add(name+"="+ok);pass&=ok;}
            var r=new RoomReadyState();
            Check("empty_blocked",!r.CanStart);
            r.SetConnected(0,true);Check("host_alone_blocked",!r.CanStart&&r.ReadyMask==1);
            Check("host_has_no_ready_button",!r.SetReady(0,true,r.Revision));
            Check("absent_member_rejected",!r.SetReady(1,true,r.Revision));
            r.SetConnected(1,true);Check("two_unready_blocked",!r.CanStart);
            r.SetReady(1,true,r.Revision);Check("two_ready_cannot_start",!r.CanStart&&r.ReadyMask==3);
            r.SetReady(1,false,r.Revision);Check("cancel_blocks",!r.CanStart);
            r.SetReady(1,true,r.Revision);r.SetConnected(2,true);Check("new_member_blocks_without_clearing_others",!r.CanStart&&r.ReadyMask==3);
            r.SetReady(2,true,r.Revision);Check("three_ready_can_start",r.CanStart);
            Check("fourth_slot_cannot_consent",!r.SetReady(3,true,r.Revision)&&r.ReadyMask==7);
            r.SetConnected(2,false);Check("leave_blocks_start_and_preserves_other_readiness",!r.CanStart&&r.ReadyMask==3);
            r.SetConnected(2,true);Check("reused_slot_not_ready",!r.CanStart&&r.ReadyMask==3);
            int old=r.Revision;r.ResetForConfiguration();Check("configuration_clears_nonhost",r.ReadyMask==1&&!r.CanStart);
            Check("stale_configuration_readiness_rejected",!r.SetReady(1,true,old));
            r.SetReady(1,true,r.Revision);Check("duplicate_consent_idempotent",r.SetReady(1,true,r.Revision)&&r.ReadyMask==3);
            r.Clear();Check("new_room_has_no_old_members",r.ReadyMask==0&&!r.CanStart);
            Check("old_room_revision_rejected",!r.SetReady(1,true,old));
            var snap=new SteamCarryProtocol.Snapshot{round=1,tick=1,rotation=Quaternion.identity,readyMask=3,readyRevision=5};
            for(int i=0;i<4;i++)snap.actors[i].height=1.7f;
            var decoded=SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snap));
            Check("snapshot_ready_roundtrip",decoded.readyMask==3&&decoded.readyRevision==5);
            bool rejected=false;snap.readyMask=8;try{SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snap));}catch(InvalidDataException){rejected=true;}
            Check("invalid_mask_rejected",rejected);
            rejected=false;snap.readyMask=3;snap.readyRevision=0;try{SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snap));}catch(InvalidDataException){rejected=true;}
            Check("invalid_revision_rejected",rejected);
            Result=string.Join("\n",rows)+"\npassed="+pass;Directory.CreateDirectory("Logs");File.WriteAllText("Logs/formal-readiness.txt",Result);Debug.Log(Result);
        }
    }
}
