using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor
{
    public static class MovingBlockChecks
    {
        static readonly List<string> rows=new List<string>();static bool passed;
        public static string Last="not run";
        static void C(string n,bool ok,object detail){passed &=ok;rows.Add(n+"="+ok+" "+detail);}
        static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
        public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
        static IEnumerator Photo(M0CarryLab lab,Vector3 target,Vector3 offset,string name)
        {
            var go=new GameObject("V2 evidence camera");var c=go.AddComponent<Camera>();c.CopyFrom(Camera.main);c.enabled=true;c.transform.position=target+offset;c.transform.LookAt(target);
            var rt=RenderTexture.GetTemporary(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);var old=RenderTexture.active;
            try{c.targetTexture=rt;yield return new WaitForEndOfFrame();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();File.WriteAllBytes("Logs/v2-"+name+".png",tex.EncodeToPNG());}
            finally{c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(tex);UnityEngine.Object.Destroy(go);}
        }
        static IEnumerator Run(SteamCarrySession s)
        {
            Last="running";rows.Clear();passed=true;s.StartLocalTest();s.enabled=false;var l=s.lab;l.Testing=true;
            var route=l.GetComponent<WhiteboxCarryLevel>();var m=l.GetComponent<WhiteboxMovingBlocks>();l.Tank.isKinematic=true;
            C("five_blocks_three_up_two_down",m.blocks.Length==5&&m.blocks.Count(b=>b.initiallyUp)==3,m.blocks.Length);
            C("authored_speed_and_stroke",m.speed==.6f&&m.travel==2,m.speed+"/"+m.travel);
            C("kinematic_mesh_colliders",m.blocks.All(b=>b.body&&b.body.isKinematic&&b.body.GetComponent<MeshCollider>()),"");
            var blue=m.blocks[0];var red=m.blocks[3];m.ResetBlocks();
            var b=blue.body.GetComponent<Renderer>().bounds;l.PlacePlayer(0,new Vector3(b.center.x+1,b.max.y+.04f,b.center.z+1));l.SetInput(0,Vector3.zero,false);
            float start=m.Phase;yield return Steps(50);
            C("one_second_speed",Mathf.Abs(m.Phase-start-.6f)<.025f,m.Phase);
            C("blue_up_red_down",Mathf.Abs(blue.body.position.y-blue.origin.y-m.Phase)<.03f&&Mathf.Abs(red.body.position.y-red.origin.y+m.Phase)<.03f,blue.body.position.y-blue.origin.y);
            C("rider_carried_up",Mathf.Abs(l.Players[0].body.position.y-blue.body.GetComponent<Renderer>().bounds.max.y)<.12f,l.Players[0].body.position.y-blue.body.GetComponent<Renderer>().bounds.max.y);
            float min=9,max=-9;
            for(int i=0;i<300;i++){yield return new WaitForFixedUpdate();float y=blue.body.position.y-blue.origin.y;min=Mathf.Min(min,y);max=Mathf.Max(max,y);}
            C("full_cycle_bounded_and_reversed",min>=-.02f&&min<.03f&&max>1.97f&&max<=2.02f,min+".."+max);
            C("rider_still_on_block_after_cycle",Mathf.Abs(l.Players[0].body.position.y-blue.body.GetComponent<Renderer>().bounds.max.y)<.12f,l.Players[0].body.position.y-blue.body.GetComponent<Renderer>().bounds.max.y);
            l.SetSessionPaused(true);start=m.Phase;yield return Steps(5);C("pause_freezes_phase",m.Phase==start,m.Phase);l.SetSessionPaused(false);
            l.Simulate=false;start=m.Phase;yield return Steps(5);C("proxy_never_simulates",m.Phase==start,m.Phase);
            m.ApplyProxy(3.9f,1);m.ApplyProxy(.1f,.5f);C("proxy_wrap_uses_short_path",m.Phase<.02f||m.Phase>3.98f,m.Phase);
            m.ApplyProxy(1.25f,1);C("proxy_pose_matches_host",Mathf.Abs(blue.body.position.y-blue.origin.y-1.25f)<.001f&&Mathf.Abs(red.body.position.y-red.origin.y+1.25f)<.001f,m.Phase);
            var snap=new SteamCarryProtocol.Snapshot{round=1,tick=1,rotation=Quaternion.identity,movingBlockPhase=.3125f,checkpoint=4};for(int i=0;i<4;i++)snap.actors[i].height=1.7f;
            var decoded=SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snap));C("network_phase_and_new_checkpoint",decoded.movingBlockPhase==.3125f&&decoded.checkpoint==4,SteamCarryProtocol.Version);
            foreach(float bad in new[]{-1f,1f,1.1f,float.NaN,float.PositiveInfinity}){snap.movingBlockPhase=bad;bool rejected=false;try{SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snap));}catch(InvalidDataException){rejected=true;}C("reject_phase_"+bad,rejected,"");}
            // Exercise the actual client snapshot application, not only binary serialization.
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            decoded.checkpoint=route.CurrentCheckpoint;decoded.position=l.Tank.position;decoded.rotation=l.Tank.rotation;
            typeof(SteamCarrySession).GetField("target",flags).SetValue(s,decoded);
            m.ResetBlocks();typeof(SteamCarrySession).GetMethod("ApplySnapshot",flags).Invoke(s,new object[]{1f});
            C("actual_client_snapshot_moves_blocks",Mathf.Abs(m.Phase-1.25f)<.001f,m.Phase);
            typeof(SteamCarrySession).GetField("target",flags).SetValue(s,null);
            l.Simulate=true;l.SetProxyState(true,"fixture");start=m.Phase;yield return Steps(5);C("broken_tank_freezes_phase",m.Phase==start,m.Phase);l.SetProxyState(false,"");
            l.ResetLab();C("checkpoint_reset_restores_phase",m.Phase==0&&m.blocks.All(x=>Vector3.Distance(x.body.position,x.origin)<.001f),m.Phase);
            l.SetSessionPaused(true);
            var maze=route.checkpoints.Single(x=>x.id=="maze");
            yield return Photo(l,new Vector3(-251,47,131),new Vector3(30,26,17),"moving-blocks");
            yield return Photo(l,maze.parkingFeet,new Vector3(18,22,-15),"maze-checkpoint");
            yield return Photo(l,route.checkpoints.Single(x=>x.id=="pickup").parkingFeet,new Vector3(6,5,6),"pickup");
            var lift=l.GetComponent<WhiteboxLift>();yield return Photo(l,lift.platformCollider.bounds.center,new Vector3(1,12,9),"lift");
            l.SetSessionPaused(false);s.enabled=true;s.ReturnToLobby();Last="passed="+passed;rows.Add(Last);rows.Add("Local physics and injected client snapshot; not remote multiplayer or continuous full route.");File.WriteAllLines("Logs/v2-moving-checks.txt",rows);Debug.Log("V2 moving blocks "+Last);
        }
    }
}
