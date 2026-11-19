using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;

namespace TankTogether.Editor
{
    public static class FailureChecks
    {
        [Serializable] public class Report {
            public string utc, scope="local-physics-and-injected-network-states-not-remote-clients";
            public bool complete, passed;
            public List<M0CarryChecks.Check> checks=new List<M0CarryChecks.Check>();
        }
        public static Report Last;
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
        static object Get(object s,string n)=>s.GetType().GetField(n,Flags).GetValue(s);
        static void Set(object s,string n,object v)=>s.GetType().GetField(n,Flags).SetValue(s,v);
        static void Call(object s,string n,params object[] args)=>s.GetType().GetMethod(n,Flags).Invoke(s,args);
        static void Check(bool ok,string n,string detail)=>Last.checks.Add(new M0CarryChecks.Check{name=n,passed=ok,evidence=detail});
        static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
        static string Saves(){
            string p=Path.Combine(Application.persistentDataPath,"CoopCampaigns");
            if(!Directory.Exists(p))return "";
            using var hash=System.Security.Cryptography.SHA256.Create();
            return string.Join("|",Directory.GetFiles(p,"*",SearchOption.AllDirectories).OrderBy(x=>x)
                .Select(x=>x+Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(x)))));
        }
        [MenuItem("Tank Together/Steam/Check Failure Flow")]
        public static void Begin(){
            var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
            if(!EditorApplication.isPlaying||!s||s.Mode!=SteamCarrySession.EntryMode.Home||!s.CanChooseMode)
                throw new InvalidOperationException("Play idle lobby");
            s.StartCoroutine(Run(s));
        }
        static IEnumerator Run(SteamCarrySession s){
            Last=new Report{utc=DateTime.UtcNow.ToString("O")};
            var lab=s.lab;
            var feedback=lab.GetComponent<TankFailureFeedback>();
            string localSaves=Saves();
            s.StartLocalTest();lab.Testing=true;
            yield return Steps(30);
            Check(!lab.Broken,"safe_stands_do_not_fail","initial safe support");
            var route=lab.GetComponent<WhiteboxCarryLevel>();
            lab.PlacePlayer(0,new Vector3(0,route.killHeight-1,0));
            yield return Steps(3);
            Check(!lab.Broken&&lab.Players[0].body.position.y>route.killHeight,"single_fall_not_team_failure","player respawned");
            lab.ResetLab();yield return Steps(30);
            int count=lab.FailureCount,bursts=feedback.BurstCount;
            lab.Tank.position+=Vector3.up*4;
            lab.Tank.linearVelocity=Vector3.down*4;
            var danger=GameObject.CreatePrimitive(PrimitiveType.Cube);
            danger.name="Failure test danger";
            danger.AddComponent<M0Danger>();
            danger.transform.position=lab.Tank.position-Vector3.up*(lab.tankSize.y/2+.20f);
            danger.transform.localScale=Vector3.one*.3f;
            Physics.SyncTransforms();
            yield return Steps(3);
            UnityEngine.Object.Destroy(danger);
            Check(lab.Broken&&lab.FailureCount==count+1,"actual_danger_collision_fails_once","count="+lab.FailureCount);
            Check(lab.Tank.isKinematic&&HeightExperimentChecks.HasLocalActors(lab,3)&&lab.Players.Take(3).All(a=>a.body.isKinematic)&&Enumerable.Range(0,3).All(i=>!lab.IsGripping(i)),
                "failure_freezes_and_clears_grips","all bodies frozen");
            Check(!lab.RetryFailure(),"short_feedback_window_prevents_accidental_retry","first 0.65 s");
            lab.Fail("duplicate collision");lab.SetInput(0,Vector3.forward,true,true);
            Check(lab.FailureCount==count+1&&feedback.BurstCount==bursts+1&&!lab.Players[0].held&&!lab.Players[0].jump,
                "duplicate_failure_and_input_ignored","one burst");
            var root=GameObject.Find("Cosmetic glass burst");
            Check(root&&root.GetComponentsInChildren<Collider>().Length==0&&root.GetComponentsInChildren<Rigidbody>().Length==0,
                "shards_are_cosmetic","no collider or rigidbody");
            lab.Testing=false;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot("Logs/failure-local-burst-20261013.png");
            yield return new WaitForSecondsRealtime(.8f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot("Logs/failure-local-menu-20261013.png");
            Check(lab.RetryFailure(),"local_retry_available","checkpoint start");
            Check(!lab.Broken&&!lab.Tank.isKinematic&&HeightExperimentChecks.HasLocalActors(lab,3)&&lab.Players.Take(3).All(a=>!a.held&&!a.jump)&&
                lab.Tank.GetComponentsInChildren<Renderer>().All(x=>x.enabled),"local_retry_restores_intact_tank_and_inputs","safe rebuilt actors");
            lab.Testing=true;yield return Steps(30);
            for(int i=0;i<lab.LocalPlayerCount;i++){
                var head=lab.Tank.transform.TransformPoint(new Vector3(i==2?0:i==0?-.9f:.9f,-lab.tankSize.y/2,i<2?-.55f:.55f));
                lab.PlacePlayer(i,head-Vector3.up*lab.Players[i].height);
                lab.SetInput(i,Vector3.zero,true);
            }
            Check(Enumerable.Range(0,3).All(lab.IsGripping),"can_grab_again_after_retry","all three");
            s.ReturnToLobby();float deadline=Time.unscaledTime+8;
            while(!s.CanChooseMode&&Time.unscaledTime<deadline)yield return null;
            Check(Saves()==localSaves,"local_failure_and_retry_do_not_write_save","directory hashes");
            string previousRoot=s.EditorSaveRoot;s.EditorSaveRoot=Path.GetFullPath("Logs/failure-save-"+Guid.NewGuid().ToString("N"));
            s.OpenSteamMenu();s.CreateRoom();
            deadline=Time.unscaledTime+30;
            while((bool)Get(s,"busy")&&Time.unscaledTime<deadline)yield return null;
            Check(s.Host,"steam_host_available","single real account; peer below is injected");
            if(s.Host){
                string saves=Saves();
                var connected=(bool[])Get(s,"connected");
                var ready=(bool[])Get(s,"peerReady");
                var time=(float[])Get(s,"lastInputTime");
                var roster=(SessionRoster)Get(s,"roster");var consent=(RoomReadyState)Get(s,"playerReady");
                void LoadGuests(){for(int i=1;i<=2;i++){ready[i]=true;time[i]=Time.unscaledTime;}}
                for(int i=1;i<=2;i++){
                    ulong peer=122UL+(ulong)i;roster.Admit(123456UL+(ulong)i,peer,out _,out _);
                    ((Dictionary<ulong,int>)Get(s,"slots"))[peer]=i;
                    connected[i]=((bool[])Get(s,"configReady"))[i]=true;
                    consent.SetConnected(i,true);consent.SetReady(i,true,consent.Revision);
                }
                LoadGuests();Check(s.StartOrResume(),"three_member_start_request","two injected peers");
                LoadGuests();
                yield return Steps(2);
                Check(s.IsPlaying,"checkpoint_handshake_enters_play","injected ready");
                lab.Fail("host failure test");
                Check((SteamCarryProtocol.Phase)Get(s,"phase")==SteamCarryProtocol.Phase.Failed&&!s.IsPlaying,
                    "host_failure_changes_session_phase","Failed");
                Check(!s.SaveCheckpoint("start"),"failed_round_cannot_save","existing checkpoint only");
                int epoch=(int)Get(s,"round");
                lab.Fail("again");
                Check((int)Get(s,"round")==epoch,"failure_does_not_repeat_epoch","once");
                yield return new WaitForSecondsRealtime(.8f);
                Check(s.StartOrResume(),"host_retry_keeps_room","same network host");
                LoadGuests();
                yield return Steps(2);
                Check(s.IsPlaying&&!lab.Broken&&s.PlayerCount==3&&s.Host,"retry_rebuilds_same_room","three actors");
                Check(Saves()==saves,"host_failure_retry_does_not_touch_personal_saves","directory hashes unchanged");
                lab.Fail("disconnect during failure");
                Call(s,"OnDisconnected",123UL);
                Check(lab.Broken&&s.PlayerCount==2&&!s.StartOrResume(),"restart_waits_for_failure_animation_after_dropout","failure preserved");
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot("Logs/failure-host-menu-20261013.png");
                s.ReturnToLobby();
                deadline=Time.unscaledTime+10;while(!s.CanChooseMode&&Time.unscaledTime<deadline)yield return null;
            }
            s.EditorSaveRoot=previousRoot;
            // Apply the same proxy snapshot twice to check presentation idempotence and restore.
            lab.DynamicRoster=true;lab.ConfigureSessionRoster(new[]{true,true,false,false},false);
            int startBursts=feedback.BurstCount;
            var state=new SteamCarryProtocol.Snapshot{round=10,tick=1,phase=SteamCarryProtocol.Phase.Failed,
                broken=true,position=lab.Tank.position,rotation=lab.Tank.rotation,teamSize=2};
            for(int i=0;i<4;i++)state.actors[i]=new SteamCarryProtocol.Actor{active=i<2,height=1.7f,position=i<2?lab.Players[i].body.position:Vector3.zero};
            Set(s,"target",state);
            Call(s,"ApplySnapshot",1f);Call(s,"ApplySnapshot",1f);
            Check(lab.Broken&&feedback.BurstCount==startBursts+1,"client_repeated_failed_snapshot_one_burst","proxy apply");
            int failures=lab.FailureCount;lab.Fail("client-only contact");
            Check(lab.FailureCount==failures,"client_cannot_author_failure","Simulate false");
            state.broken=false;state.phase=SteamCarryProtocol.Phase.Preparing;
            Call(s,"ApplySnapshot",1f);
            Check(!lab.Broken&&lab.Tank.GetComponentsInChildren<Renderer>().All(x=>x.enabled),
                "client_retry_restores_visuals","same tank object");
            Check(!SteamCarryProtocol.IsNewer(9,999,10,1),"old_failure_epoch_rejected","stale snapshot");
            s.ReturnToLobby();
            Last.complete=true;Last.passed=Last.checks.All(x=>x.passed);
            File.WriteAllText("Logs/failure-checks-20261013.json",JsonUtility.ToJson(Last,true));
            Debug.Log("[FailureChecks] passed="+Last.passed+" checks="+Last.checks.Count);
        }
    }
}
