using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor
{
    public static class FeelChecks
    {
        [Serializable] public class Report
        {
            public string scope="isolated-local-physics-camera-GM-not-full-route-or-remote-multiplayer";
            public bool complete,passed;
            public float stopDistance,jumpPeak,slopeDrift;
            public List<M0CarryChecks.Check> checks=new List<M0CarryChecks.Check>();
        }
        public static Report Last;
        static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
        static void Check(bool ok,string name,string evidence){Last.checks.Add(new M0CarryChecks.Check{passed=ok,name=name,evidence=evidence});}
        [MenuItem("Tank Together/Tests/Check Feel Camera GM")]
        public static void Begin()
        {
            var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
            if(!Application.isPlaying||s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Play idle lobby first");
            s.StartCoroutine(Run(s));
        }
        static IEnumerator Run(SteamCarrySession session)
        {
            Last=new Report();var lab=session.lab;session.StartLocalTest();lab.Testing=true;
            var baselineAsset=AssetDatabase.LoadAssetAtPath<M0CarryBaseline>("Assets/Resources/WhiteboxJumpTrial.asset");string assetBefore=JsonUtility.ToJson(baselineAsset);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Feel test floor";floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(100,1,100);
            lab.PlacePlayer(0,new Vector3(1000,.02f,0));yield return Steps(20);
            lab.SetInput(0,Vector3.right,false);yield return Steps(75);float speed=lab.Players[0].body.linearVelocity.x;float x=lab.Players[0].body.position.x;
            lab.SetInput(0,Vector3.zero,false);yield return Steps(50);Last.stopDistance=lab.Players[0].body.position.x-x;
            Check(Mathf.Abs(speed-4)<.08f&&Last.stopDistance<.20f,"walk_and_release",$"speed={speed:F3}, stop={Last.stopDistance:F3}m; before stop=0.356m");
            lab.SetInput(0,Vector3.zero,false,true);
            for(int i=0;i<70;i++){yield return Steps(1);Last.jumpPeak=Mathf.Max(Last.jumpPeak,lab.Players[0].body.position.y);}
            Check(Last.jumpPeak>.60f&&Last.jumpPeak<.72f,"free_jump_height",Last.jumpPeak.ToString("F3"));
            lab.PlacePlayer(0,new Vector3(1000,.30f,0));lab.Players[0].body.linearVelocity=Vector3.down*2;yield return Steps(1);lab.SetInput(0,Vector3.zero,false,true);
            float bufferedPeak=0;for(int i=0;i<60;i++){yield return Steps(1);bufferedPeak=Mathf.Max(bufferedPeak,lab.Players[0].body.position.y);}
            Check(bufferedPeak>.50f,"early_jump_buffer",$"peak={bufferedPeak:F3}");
            // A small platform gives a real walk-off edge; do not teleport during this jump.
            var edge=GameObject.CreatePrimitive(PrimitiveType.Cube);edge.transform.position=new Vector3(1200,-.5f,0);edge.transform.localScale=new Vector3(4,1,4);
            lab.PlacePlayer(0,new Vector3(1201,.02f,0));yield return Steps(15);lab.SetInput(0,Vector3.right,false);
            for(int i=0;i<90&&lab.Players[0].grounded;i++)yield return Steps(1);
            yield return Steps(2);lab.SetInput(0,Vector3.right,false,true);float coyotePeak=-10;
            for(int i=0;i<25;i++){yield return Steps(1);coyotePeak=Mathf.Max(coyotePeak,lab.Players[0].body.position.y);}
            Check(coyotePeak>.35f,"walk_off_coyote_jump",$"peak={coyotePeak:F3}");
            var slope=GameObject.CreatePrimitive(PrimitiveType.Cube);slope.transform.position=new Vector3(1100,-.5f,0);slope.transform.rotation=Quaternion.Euler(0,0,15);slope.transform.localScale=new Vector3(50,1,30);
            lab.PlacePlayer(0,new Vector3(1100,.1f,0));lab.SetInput(0,Vector3.zero,false);yield return Steps(90);var start=lab.Players[0].body.position;yield return Steps(100);Last.slopeDrift=Vector3.Distance(start,lab.Players[0].body.position);
            Check(Last.slopeDrift<.08f,"idle_15deg_slope",$"drift over 2s={Last.slopeDrift:F4}m");
            lab.SetInput(0,Vector3.right,false);yield return Steps(70);var uphill=lab.Players[0].body.position;Check(uphill.x>start.x+3&&uphill.y>start.y+.6f,"walk_up_slope",(uphill-start).ToString("F3"));lab.SetInput(0,Vector3.zero,false);
            lab.PlacePlayer(0,new Vector3(1000,.02f,0));lab.SetExternalView(0,0,0);yield return Steps(50);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=new Vector3(1000,1.25f,-3);wall.transform.localScale=new Vector3(10,8,.2f);Physics.SyncTransforms();yield return Steps(5);
            Check(lab.CameraDistance<2.8f&&lab.CameraDistance>1,"camera_in_front_of_wall",$"distance={lab.CameraDistance:F3}, camera={Camera.main.transform.position:F3}");
            wall.SetActive(false);yield return Steps(50);Check(lab.CameraDistance>7.4f,"camera_returns_after_wall",lab.CameraDistance.ToString("F3"));
            lab.SetExternalView(0,0,-65);yield return null;yield return new WaitForEndOfFrame();Check(Mathf.Abs(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.x,-65))<.1f,"camera_can_look_up",Camera.main.transform.eulerAngles.ToString());
            lab.SetExternalView(0,0,80);yield return null;yield return new WaitForEndOfFrame();Check(Mathf.Abs(Mathf.DeltaAngle(Camera.main.transform.eulerAngles.x,80))<.1f,"camera_can_look_down",Camera.main.transform.eulerAngles.ToString());
            var gm=lab.GM;Check(gm.Open()&&Time.timeScale==0,"GM_pauses_local",$"open={gm.IsOpen}");
            var draft=(M0CarryBaseline)typeof(CarryGMPanel).GetField("draft",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(gm);draft.walkSpeed=5;draft.tankSize*=1.2f;
            Check(gm.ApplyDraft()&&lab.baseline.walkSpeed==5&&Vector3.Distance(lab.Tank.GetComponent<BoxCollider>().size,draft.tankSize)<.001f,"GM_updates_motor_and_tank_collider",lab.tankSize.ToString("F3"));
            Check(JsonUtility.ToJson(baselineAsset)==assetBefore,"GM_never_writes_source_asset",baselineAsset.name);
            gm.Close();Check(Time.timeScale==1,"GM_resumes_time",Time.timeScale.ToString());
            session.ReturnToLobby();yield return Steps(3);Check(lab.baseline.walkSpeed==baselineAsset.walkSpeed&&!gm.CanEdit&&!gm.Open(),"lobby_restores_shipping_config_and_locks_GM",lab.baseline.Signature);
            foreach(var obj in new[]{floor,edge,slope,wall})UnityEngine.Object.Destroy(obj);
            Last.complete=true;Last.passed=Last.checks.All(c=>c.passed);Directory.CreateDirectory("Logs");File.WriteAllText("Logs/feel-camera-gm.json",JsonUtility.ToJson(Last,true));Debug.Log("Feel checks completed passed="+Last.passed);
        }
    }
}
