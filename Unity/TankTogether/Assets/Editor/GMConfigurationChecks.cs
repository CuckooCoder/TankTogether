using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TankTogether.M0;
using TankTogether.Networking;
using Unity.Collections;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace TankTogether.Editor
{
    public static class GMConfigurationChecks
    {
        [Serializable] public sealed class Report {public string scope="local-GM-and-single-account-Steam-host-with-injected-peer-messages-NOT-two-account-remote";public bool complete,passed;public List<M0CarryChecks.Check> checks=new List<M0CarryChecks.Check>();}
        public static Report Last;
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool pass,string name,string evidence){Last.checks.Add(new M0CarryChecks.Check{passed=pass,name=name,evidence=evidence});}
        static object Get(object o,string name)=>o.GetType().GetField(name,Private).GetValue(o);
        static void Set(object o,string name,object value)=>o.GetType().GetField(name,Private).SetValue(o,value);
        static void Message(SteamCarrySession s,string method,ulong sender,byte[] bytes){using var w=new FastBufferWriter(bytes.Length,Allocator.Temp);w.WriteBytesSafe(bytes);using var r=new FastBufferReader(w,Allocator.Temp);typeof(SteamCarrySession).GetMethod(method,Private).Invoke(s,new object[]{sender,r});}
        static bool Reject(Action a){try{a();return false;}catch{return true;}}
        [MenuItem("Tank Together/Tests/Check Complete GM Configuration")]
        public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();if(!Application.isPlaying||s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Play idle Home first");s.StartCoroutine(Run(s));}
        static IEnumerator Run(SteamCarrySession s)
        {
            Last=new Report();var lab=s.lab;var gm=lab.GM;string clipboard=GUIUtility.systemCopyBuffer;
            var asset=AssetDatabase.LoadAssetAtPath<M0CarryBaseline>("Assets/Resources/WhiteboxJumpTrial.asset");string assetBefore=JsonUtility.ToJson(asset);
            s.StartLocalTest();yield return null;gm.Open();var current=CarryTuning.Capture(lab);string beforeHash=CarryTuning.Hash(current);
            var draft=(M0CarryBaseline)Get(gm,"draft");var extra=(CarryTuningExtras)Get(gm,"extra");draft.walkSpeed=5;
            string copied=gm.CopyCurrent();var copy=CarryTuning.Parse(copied);
            Check(GUIUtility.systemCopyBuffer==copied&&CarryTuning.Hash(copy)==beforeHash&&copy.values.Single(v=>v.key=="b.walkSpeed").value==asset.walkSpeed,"clipboard_uses_applied_not_draft",copy.values.Length+" parameters");
            string[] omitted={"version","motorGain","motorForce","carryJumpHeightTarget","defaults"};
            bool coverage=typeof(M0CarryBaseline).GetFields(BindingFlags.Public|BindingFlags.Instance).Where(f=>!omitted.Contains(f.Name)).All(f=>CarryTuning.All.Any(c=>c.key.StartsWith("b."+f.Name)))&&typeof(CarryTuningExtras).GetFields().All(f=>CarryTuning.All.Any(c=>c.key=="e."+f.Name));
            Check(coverage&&CarryTuning.All.Select(c=>c.key).Distinct().Count()==CarryTuning.All.Length,"catalog_covers_all_active_fields",CarryTuning.All.Length.ToString());
            Check(gm.ImportDraft(copied)&&CarryTuning.Hash(CarryTuning.Capture(draft,extra,s.LevelId))==beforeHash,"clipboard_import_roundtrip","all values equal");
            var bad=CarryTuning.Parse(copied);bad.values[0].value=float.NaN;
            Check(Reject(()=>CarryTuning.Read(bad,draft,extra,s.LevelId))&&CarryTuning.Hash(CarryTuning.Capture(draft,extra,s.LevelId))==beforeHash,"invalid_import_atomic","NaN rejected without partial mutation");
            bad=CarryTuning.Parse(copied);bad.values[1].key=bad.values[0].key;Check(Reject(()=>CarryTuning.Read(bad,draft,extra,s.LevelId)),"duplicate_rejected","");
            bad=CarryTuning.Parse(copied);bad.values=bad.values.Skip(1).ToArray();Check(Reject(()=>CarryTuning.Read(bad,draft,extra,s.LevelId)),"missing_rejected","");
            bad=CarryTuning.Parse(copied);bad.levelId="wrong";Check(Reject(()=>CarryTuning.Read(bad,draft,extra,s.LevelId)),"wrong_level_rejected","");
            var packet=new SteamCarrySession.ConfigEnvelope{revision=2,round=3,hash=beforeHash,profile=current};
            SteamCarrySession.ValidateConfiguration(packet,draft,extra,s.LevelId);Check(true,"network_profile_roundtrip",Encoding.UTF8.GetByteCount(JsonUtility.ToJson(packet))+" bytes; reliable fragmentation required");
            packet.hash=new string('0',32);Check(Reject(()=>SteamCarrySession.ValidateConfiguration(packet,draft,extra,s.LevelId)),"hash_mismatch_rejected","");
            draft.walkSpeed=5;draft.tankSize*=1.1f;extra.liftSpeed=3;extra.twoPlayerGripMaxTorque=55;extra.cameraDistance=9;extra.snapshotRate=40;
            Check(gm.ApplyDraft()&&lab.baseline.walkSpeed==5&&lab.GetComponent<WhiteboxLift>().speed==3&&lab.twoPlayerGripMaxTorque==55&&lab.Tuning.cameraDistance==9&&lab.Tuning.snapshotRate==40,"all_component_domains_apply","movement/geometry/lift/camera/network and preserved legacy assist value");
            Check(Vector3.Distance(lab.Tank.GetComponent<BoxCollider>().size,draft.tankSize)<.001f,"tank_collision_matches_configuration",lab.tankSize.ToString());
            gm.Close();s.ReturnToLobby();yield return null;
            Check(Time.timeScale==1&&CarryTuning.Hash(CarryTuning.Capture(lab))==beforeHash,"leave_local_restores_all_defaults",lab.baseline.airAcceleration.ToString());
            Check(JsonUtility.ToJson(asset)==assetBefore,"asset_and_user_air25_unchanged",asset.airAcceleration.ToString());
            // Exercise the real client state transition in an isolated, disconnected fixture.
            // This validates application logic, not Steam transport between two accounts.
            var accept=typeof(SteamCarrySession).GetMethod("AcceptHostConfiguration",Private);
            var clientProfile=CarryTuning.Parse(copied);clientProfile.values.Single(v=>v.key=="b.walkSpeed").value=5.5f;clientProfile.values.Single(v=>v.key=="b.stretchBalanceLever").value=1.1f;
            var received=new SteamCarrySession.ConfigEnvelope{revision=4,round=5,profile=clientProfile,hash=CarryTuning.Hash(clientProfile)};
            var tankBefore=lab.Tank;bool accepted=(bool)accept.Invoke(s,new object[]{received});
            Check(accepted&&s.ConfigRevision==4&&lab.baseline.walkSpeed==5.5f&&lab.baseline.stretchBalanceLever==1.1f&&lab.SessionPaused&&!ReferenceEquals(lab.Tank,tankBefore),"client_applies_rebuilds_and_pauses","isolated receive transition; no live remote connection");
            var newTank=lab.Tank;accept.Invoke(s,new object[]{received});Check(ReferenceEquals(lab.Tank,newTank),"duplicate_config_does_not_rebuild","");
            received.revision=3;Check(!(bool)accept.Invoke(s,new object[]{received})&&s.ConfigRevision==4,"late_old_config_does_not_rollback","");
            received.revision=4;received.hash=new string('0',32);Check(Reject(()=>accept.Invoke(s,new object[]{received}))&&lab.baseline.walkSpeed==5.5f,"bad_client_hash_cannot_mutate","");
            Set(s,"localSlot",1);Set(s,"snapshotTick",-1);
            var snapshot=new SteamCarryProtocol.Snapshot{configRevision=3,round=5,tick=2,teamSize=2,phase=SteamCarryProtocol.Phase.Paused,rotation=Quaternion.identity,position=lab.Tank.position};
            for(int i=0;i<4;i++)snapshot.actors[i]=new SteamCarryProtocol.Actor{active=i<2,height=lab.baseline.standingHeight,position=lab.Players[i].body?lab.Players[i].body.position:Vector3.zero};
            Message(s,"ReceiveState",NetworkManager.ServerClientId,SteamCarryProtocol.Encode(snapshot));Check((int)Get(s,"snapshotTick")==-1,"old_config_snapshot_ignored","");
            snapshot.configRevision=4;Message(s,"ReceiveState",999,SteamCarryProtocol.Encode(snapshot));Check((int)Get(s,"snapshotTick")==-1,"nonhost_snapshot_ignored","");
            Message(s,"ReceiveState",NetworkManager.ServerClientId,SteamCarryProtocol.Encode(snapshot));Check((int)Get(s,"snapshotTick")==2,"matching_config_snapshot_accepted","");
            typeof(SteamCarrySession).GetMethod("ApplySnapshot",Private).Invoke(s,new object[]{1f});
            Check(!gm.CanEdit&&!gm.ApplyDraft(),"nonhost_cannot_apply_configuration","");
            Set(s,"localSlot",-1);Set(s,"configRevision",0);Set(s,"configHash","");Set(s,"target",null);Set(s,"snapshotTick",-1);Set(s,"round",1);s.ReturnToLobby();yield return null;
            string stable=(string)typeof(SteamCarrySession).GetProperty("Compatibility",Private).GetValue(s);
            s.OpenSteamMenu();stable=(string)typeof(SteamCarrySession).GetProperty("Compatibility",Private).GetValue(s);s.CreateRoom();float deadline=Time.realtimeSinceStartup+25;
            while(!s.Host&&Time.realtimeSinceStartup<deadline)yield return null;
            Check(s.Host,"actual_Steam_host_created","single Steam account");
            if(s.Host)
            {
                Check(s.ConfigRevision==1&&s.ConfigHash==beforeHash,"host_initial_config",s.ConfigurationStatus);
                gm.Open();Check(Time.timeScale==1&&lab.SessionPaused,"online_GM_does_not_stop_network_clock","paused physics, normal game clock");
                draft=(M0CarryBaseline)Get(gm,"draft");extra=(CarryTuningExtras)Get(gm,"extra");draft.walkSpeed=5.25f;draft.stretchGrowSpeed=.55f;draft.stretchGripMaxForce=210;draft.stretchBalanceTorque=80;extra.liftSpeed=3.2f;extra.snapshotRate=35;
                Check(gm.ApplyDraft()&&s.ConfigRevision==2&&lab.baseline.walkSpeed==5.25f&&lab.GetComponent<WhiteboxLift>().speed==3.2f&&lab.Stretch.growRate==.55f&&lab.Stretch.gripMaxForce==210&&lab.baseline.stretchBalanceTorque==80,"host_applies_and_increments_revision",s.ConfigurationStatus);
                Check((string)typeof(SteamCarrySession).GetProperty("Compatibility",Private).GetValue(s)==stable,"late_join_package_identity_stable","runtime changes do not change lobby approval identity");
                Check(!s.StartOrResume(),"cannot_start_with_GM_open","");
                var slots=(Dictionary<ulong,int>)Get(s,"slots");var connected=(bool[])Get(s,"connected");var ready=(bool[])Get(s,"peerReady");var configs=(bool[])Get(s,"configReady");var times=(float[])Get(s,"lastInputTime");
                slots[999]=1;connected[1]=true;ready[1]=true;times[1]=Time.unscaledTime;
                slots[998]=2;connected[2]=true;ready[2]=true;times[2]=Time.unscaledTime;gm.Close();
                Message(s,"ReceiveConfigurationAck",998,Encoding.UTF8.GetBytes("{\"revision\":2,\"hash\":\""+s.ConfigHash+"\"}"));
                bool Everyone()=> (bool)typeof(SteamCarrySession).GetProperty("EveryoneReady",Private).GetValue(s);
                Check(configs[2]&&!Everyone(),"unconfirmed_peer_blocks_resume","");
                Message(s,"ReceiveConfigurationAck",999,Encoding.UTF8.GetBytes("{\"revision\":1,\"hash\":\""+s.ConfigHash+"\"}"));Check(!configs[1],"stale_ack_ignored","");
                Message(s,"ReceiveConfigurationAck",888,Encoding.UTF8.GetBytes("{\"revision\":2,\"hash\":\""+s.ConfigHash+"\"}"));Check(!configs[1],"unregistered_sender_ignored","");
                Message(s,"ReceiveConfigurationAck",999,Encoding.UTF8.GetBytes("{\"revision\":2,\"hash\":\""+s.ConfigHash+"\"}"));Check(configs[1]&&Everyone(),"matching_ack_unlocks_readiness","");
                int round=(int)Get(s,"round");var input=new SteamCarryProtocol.Input{configRevision=1,round=round,sequence=900,pauseRequested=true};
                Message(s,"ReceiveInput",999,SteamCarryProtocol.Encode(input));Check(((int[])Get(s,"lastSequence"))[1]!=900,"old_config_input_ignored","");
                input.configRevision=2;Message(s,"ReceiveInput",999,SteamCarryProtocol.Encode(input));Check(((bool[])Get(s,"rearm"))[1],"inactive_input_rearms_grip","");
                Set(s,"phase",SteamCarryProtocol.Phase.Playing);Message(s,"ReceiveGMPause",999,new byte[]{1});Check(!ready[1]&&!Everyone()&&lab.SessionPaused,"remote_GM_message_pauses_round","");
                var authoritativeHash=s.ConfigHash;Message(s,"ReceiveConfiguration",999,Encoding.UTF8.GetBytes("{}"));Check(s.ConfigHash==authoritativeHash,"peer_cannot_overwrite_host_config","");
                slots.Remove(999);connected[1]=false;configs[1]=false;ready[1]=false;
                slots.Remove(998);connected[2]=false;configs[2]=false;ready[2]=false;
                gm.Open();Check(CarryTuning.Hash(CarryTuning.Parse(gm.CopyCurrent()))==s.ConfigHash,"host_clipboard_matches_live_network_hash",s.ConfigHash);gm.Close();
                s.ReturnToLobby();yield return null;yield return null;yield return null;
                Check(s.ConfigRevision==0&&CarryTuning.Hash(CarryTuning.Capture(lab))==beforeHash,"leave_room_clears_revision_restores_defaults","");
            }
            GUIUtility.systemCopyBuffer=clipboard;
            Last.complete=true;Last.passed=Last.checks.All(c=>c.passed);Directory.CreateDirectory("Logs");File.WriteAllText("Logs/gm-configuration-checks.json",JsonUtility.ToJson(Last,true));Debug.Log("GM configuration checks complete passed="+Last.passed);
        }
    }
}
