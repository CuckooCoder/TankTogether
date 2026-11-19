using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using TankTogether.Networking;
namespace TankTogether.Editor
{
    public static class RoomReadyIntegrationChecks
    {
        public static string Result="Not run";
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static T Field<T>(SteamCarrySession s,string name)=>(T)typeof(SteamCarrySession).GetField(name,Flags).GetValue(s);
        public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
        static IEnumerator Run(SteamCarrySession s)
        {
            s.ReturnToLobby();float timeout=Time.unscaledTime+8;
            while(!s.CanChooseMode&&Time.unscaledTime<timeout)yield return null;
            s.EditorSaveRoot=Path.GetFullPath("Logs/ready-save-"+Guid.NewGuid().ToString("N"));
            s.OpenSteamMenu();s.CreateRoom();timeout=Time.unscaledTime+25;
            while(!s.Host&&Time.unscaledTime<timeout)yield return null;
            var rows=new List<string>();bool pass=true;
            void Check(string name,bool ok){rows.Add(name+"="+ok);pass&=ok;}
            Check("actual_steam_host_created",s.Host);
            if(s.Host)
            {
                try
                {
                    Check("actual_host_alone_cannot_start",!s.StartOrResume());
                    var lobby=Field<Steamworks.Data.Lobby?>(s,"lobby");
                    Check("steam_lobby_has_three_seats",lobby.HasValue&&lobby.Value.MaxMembers==3);
                    const ulong peer=999999, secondPeer=999998;
                    var seats=Field<SessionRoster>(s,"roster");
                    var roster=Field<RoomReadyState>(s,"playerReady");
                    var connected=Field<bool[]>(s,"connected");var ready=Field<bool[]>(s,"peerReady");var config=Field<bool[]>(s,"configReady");
                    var inputTimes=Field<float[]>(s,"lastInputTime");
                    void AddGuest(ulong identity,ulong connection,int slot)
                    {
                        Check("guest_"+slot+"_admitted",seats.Admit(identity,connection,out int assigned,out _)&&assigned==slot);
                        Field<Dictionary<ulong,int>>(s,"slots").Add(connection,slot);
                        connected[slot]=ready[slot]=config[slot]=true;inputTimes[slot]=Time.unscaledTime;roster.SetConnected(slot,true);
                    }
                    AddGuest(123456789,peer,1);
                    Check("network_ready_not_player_consent",!s.StartOrResume());
                    void Deliver(ulong sender,int rev,int cfg,byte value)
                    {
                        using var writer=new Unity.Netcode.FastBufferWriter(9,Allocator.Temp);
                        writer.WriteValueSafe(rev);writer.WriteValueSafe(cfg);writer.WriteValueSafe(value);
                        using var reader=new Unity.Netcode.FastBufferReader(writer,Allocator.Temp);
                        typeof(SteamCarrySession).GetMethod("ReceivePlayerReady",Flags).Invoke(s,new object[]{sender,reader});
                    }
                    Deliver(peer+1,roster.Revision,s.ConfigRevision,1);Check("unknown_sender_rejected",roster.ReadyMask==1);
                    Deliver(peer,roster.Revision,s.ConfigRevision-1,1);Check("stale_config_rejected",roster.ReadyMask==1);
                    Deliver(peer,roster.Revision-1,s.ConfigRevision,1);Check("stale_revision_rejected",roster.ReadyMask==1);
                    Deliver(peer,roster.Revision,s.ConfigRevision,2);Check("invalid_value_rejected",roster.ReadyMask==1);
                    Deliver(peer,roster.Revision,s.ConfigRevision,1);Check("actual_handler_accepts_consent",roster.ReadyMask==3);
                    Check("two_ready_players_cannot_start",!s.StartOrResume());
                    AddGuest(123456788,secondPeer,2);
                    Check("fourth_seat_rejected",!seats.Admit(123456787,999997,out _,out _));
                    Check("one_guest_not_ready_blocks_start",!s.StartOrResume());
                    Deliver(secondPeer,roster.Revision,s.ConfigRevision,1);
                    Deliver(peer,roster.Revision,s.ConfigRevision,0);Check("actual_handler_cancels",!s.StartOrResume());
                    typeof(SteamCarrySession).GetField("phase",Flags).SetValue(s,SteamCarryProtocol.Phase.Failed);
                    Deliver(peer,roster.Revision,s.ConfigRevision,1);Check("failure_allows_new_consent",roster.ReadyMask==7);
                    typeof(SteamCarrySession).GetField("phase",Flags).SetValue(s,SteamCarryProtocol.Phase.Paused);
                    Deliver(peer,roster.Revision,s.ConfigRevision,1);Check("actual_start_enters_preparing",s.StartOrResume()&&Field<SteamCarryProtocol.Phase>(s,"phase")==SteamCarryProtocol.Phase.Preparing);
                    Deliver(peer,roster.Revision,s.ConfigRevision,0);Check("loading_rejects_ready_changes",roster.ReadyMask==7);
                    var step=typeof(SteamCarrySession).GetMethod("FixedUpdate",Flags);
                    ready[1]=true;inputTimes[1]=Time.unscaledTime;step.Invoke(s,null);
                    Check("two_loaded_players_cannot_start",Field<SteamCarryProtocol.Phase>(s,"phase")==SteamCarryProtocol.Phase.Preparing&&s.lab.SessionPaused);
                    ready[2]=true;inputTimes[2]=Time.unscaledTime;step.Invoke(s,null);
                    Check("three_loaded_players_start",s.IsPlaying&&s.lab.SessionTeamSize==3&&s.lab.Players[2].active&&!s.lab.Players[3].active);
                    var tank=s.lab.Tank; tank.linearVelocity=new Vector3(1,2,3);tank.angularVelocity=new Vector3(.1f,.2f,.3f);
                    var position=tank.position;var velocity=tank.linearVelocity;var spin=tank.angularVelocity;var rotation=tank.rotation;
                    typeof(SteamCarrySession).GetMethod("OnDisconnected",Flags).Invoke(s,new object[]{peer});
                    Check("disconnect_does_not_pause",s.IsPlaying&&!s.lab.SessionPaused&&s.PlayerCount==2);
                    Check("disconnect_removes_body",!s.lab.Players[1].active&&!s.lab.Players[1].body);
                    Check("disconnect_preserves_tank_state",tank.position==position&&tank.rotation==rotation&&tank.linearVelocity==velocity&&tank.angularVelocity==spin);
                    Check("disconnect_retains_identity_seat",Field<SessionRoster>(s,"roster").Admit(123456789,peer+10,out int same,out _)&&same==1);
                }
                catch(Exception e){pass=false;rows.Add("exception="+e);}
            }
            s.ReturnToLobby();
            Result=string.Join("\n",rows)+"\npassed="+pass+"\nremoteMultiplayer=not-tested; one real Steam host plus two injected peers";
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/formal-ready-integration.txt",Result);Debug.Log(Result);
        }
    }
}
