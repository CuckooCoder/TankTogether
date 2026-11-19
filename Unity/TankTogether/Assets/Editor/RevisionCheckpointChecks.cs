using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;
namespace TankTogether.Editor
{
    public static class RevisionCheckpointChecks
    {
        [Serializable] public class Evidence { public bool complete,passed; public string scope="isolated-authoritative-physics-and-save-store-not-remote-or-route"; public List<M0CarryChecks.Check> checks=new List<M0CarryChecks.Check>(); }
        public static Evidence Last;
        static void Check(bool value,string name,string evidence) => Last.checks.Add(new M0CarryChecks.Check{passed=value,name=name,evidence=evidence});
        static IEnumerator Steps(int count){for(int i=0;i<count;i++)yield return new WaitForFixedUpdate();}
        [MenuItem("Tank Together/Whitebox/Check Revision Checkpoints")]
        public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();if(!EditorApplication.isPlaying||s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Play idle revision lobby");s.StartCoroutine(Run(s));}
        static IEnumerator Run(SteamCarrySession s)
        {
            Last=new Evidence();s.StartLocalTest();var lab=s.lab;lab.Testing=true;var route=lab.GetComponent<WhiteboxCarryLevel>();var lift=lab.GetComponent<WhiteboxLift>();
            yield return Steps(30);
            Check(!lab.Broken&&route.CheckpointId=="start","initial_tank_safe","tank="+lab.Tank.position);
            Check(lab.Players.All(a=>Vector3.Distance(a.body.position,route.checkpoints[0].playerFeet)<1.1f),"players_spawn_at_first_yellow","players="+string.Join(";",lab.Players.Select(a=>a.body.position.ToString("F2"))));
            Check(Mathf.Abs(lab.Tank.position.x+126.063324f)<.02f&&Mathf.Abs(lab.Tank.position.y+27.325f)<.03f,"tank_at_authored_first_marker_height","tank="+lab.Tank.position);
            for(int i=0;i<4;i++) {lab.PlacePlayer(i,route.checkpoints[0].tankFeet+new Vector3(i%2==0?-.9f:.9f,.02f,i<2?-.55f:.55f));}
            yield return Steps(10);for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
            Check(Enumerable.Range(0,4).All(i=>lab.IsGripping(i)),"first_spawn_tank_grabbable","four grips");
            for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,false);
            lab.ResetLab();yield return Steps(10);
            for(int i=0;i<4;i++)lab.PlacePlayer(i,route.checkpoints[1].playerFeet+new Vector3(i%2==0?-.7f:.7f,.03f,i<2?-.7f:.7f));
            yield return Steps(25);
            Check(route.CheckpointId=="start","players_alone_do_not_save","all players at upper yellow");
            for(int i=0;i<4;i++)lab.PlacePlayer(i,route.checkpoints[0].playerFeet+Vector3.right*i);
            var park=route.checkpoints[1].parkingFeet;
            lab.Tank.isKinematic=true;lab.Tank.position=park+Vector3.up*(lab.baseline.carryBottomHeight+lab.tankSize.y/2+.5f);lab.Tank.rotation=Quaternion.identity;
            yield return Steps(20);
            Check(route.CheckpointId=="start","hovering_tank_does_not_save","no top contacts");
            lab.Tank.isKinematic=false;lab.Tank.position=park+Vector3.up*(lab.baseline.carryBottomHeight+lab.tankSize.y/2+.02f);lab.Tank.linearVelocity=Vector3.zero;
            yield return Steps(60);
            Check(!lab.Broken&&route.CheckpointId=="upper","four_actual_support_contacts_activate","checkpoint="+route.CheckpointId+" broken="+lab.Broken);
            int cp=route.CurrentCheckpoint;lab.Fail("checkpoint retry test");yield return new WaitForSecondsRealtime(.7f);
            Check(lab.RetryFailure(),"retry_from_upper_allowed","local retry");yield return Steps(40);
            Check(!lab.Broken&&route.CurrentCheckpoint==cp&&cp==1,"retry_keeps_upper_checkpoint","checkpoint="+route.CheckpointId);
            Check(lab.Players.All(a=>Vector3.Distance(a.body.position,route.checkpoints[1].playerFeet)<1.1f),"retry_players_at_second_yellow","positions="+string.Join(";",lab.Players.Select(a=>a.body.position.ToString("F2"))));
            Check(Vector3.Distance(lab.Tank.position,route.checkpoints[1].tankFeet+Vector3.up*(lab.baseline.carryBottomHeight+lab.tankSize.y/2))<.03f,"retry_tank_at_second_white_xz_on_safe_supports","tank="+lab.Tank.position);
            Check(lift.AtTop,"upper_checkpoint_restores_lift_at_top","distance="+lift.Distance);
            lab.PlacePlayer(0,new Vector3(0,route.killHeight-1,0));yield return Steps(5);
            Check(!lab.Broken&&Vector3.Distance(lab.Players[0].body.position,route.checkpoints[1].playerFeet)<1.1f,"single_fall_respawns_at_current_checkpoint","P1="+lab.Players[0].body.position);
            string root=Path.GetFullPath("Logs/revision-save-tests/"+Guid.NewGuid().ToString("N"));
            bool Valid(CoopCampaignSave.Progress p)=>p.levelId==route.levelId&&route.HasCheckpoint(p.checkpointId);
            var create=CoopCampaignSave.Create(123,"test-revision3",route.levelId,"start","test-v3",Valid,root);
            var next=CoopCampaignSave.Copy(create.progress);next.checkpointId="upper";
            var saved=CoopCampaignSave.Save(next,Valid,root);var loaded=CoopCampaignSave.Load(123,"test-revision3","test-v3",Valid,root);
            Check(saved.success&&loaded.success&&loaded.progress.checkpointId=="upper","upper_checkpoint_persists_and_reloads","isolated save root, no personal save overwritten");
            next=CoopCampaignSave.Copy(loaded.progress);next.checkpointId="invalid";
            Check(!CoopCampaignSave.Save(next,Valid,root).success,"unknown_checkpoint_rejected","catalog validation");
            var ss=new SteamCarryProtocol.Snapshot{round=1,tick=1,rotation=Quaternion.identity,checkpoint=1,liftDistance=101};for(int i=0;i<4;i++)ss.actors[i]=new SteamCarryProtocol.Actor{height=1.7f};
            Check(SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(ss)).checkpoint==1,"checkpoint_index_snapshot_roundtrip","protocol05");
            lab.Testing=false;UnityEditor.EditorApplication.ExecuteMenuItem("Window/General/Game");yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot("Logs/revision3-upper.png");yield return null;
            Last.complete=true;Last.passed=Last.checks.All(c=>c.passed);File.WriteAllText("Logs/revision3-checkpoint-checks.json",JsonUtility.ToJson(Last,true));
            s.ReturnToLobby();Debug.Log("[RevisionCheckpointChecks] "+Last.passed+" count="+Last.checks.Count);
        }
    }
}
