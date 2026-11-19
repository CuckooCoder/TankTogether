using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor
{
    [DefaultExecutionOrder(-40)]
    public sealed class ControlFeelMovingFloor : MonoBehaviour
    {
        public Rigidbody body; public Vector3 velocity;
        void FixedUpdate(){body.MovePosition(body.position+velocity*Time.fixedDeltaTime);}
    }
    public static class ControlFeelChecks
    {
        [Serializable] public class Sample
        {
            public string mode;
            public float startTime, reverseTime, stopDistance, releasedJumpDistance, releasedJumpPeak, releasedJumpTime;
            public float liftGroundedFraction, liftJumpRelativePeak, movingFloorDrift, wallSlideDistance;
        }
        [Serializable] public class Report
        {
            public string scope="isolated-real-Unity-physics-input-replay; NOT complete-route or real remote play";
            public bool complete,passed; public List<Sample> samples=new List<Sample>();
            public List<M0CarryChecks.Check> checks=new List<M0CarryChecks.Check>();
        }
        public static Report Last;
        static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
        static void Check(bool pass,string name,string evidence){Last.checks.Add(new M0CarryChecks.Check{passed=pass,name=name,evidence=evidence});}
        static GameObject Box(string name,Vector3 position,Vector3 size){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.position=position;go.transform.localScale=size;return go;}
        [MenuItem("Tank Together/Tests/Compare Control Feel")]
        public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();if(!Application.isPlaying||!s||s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Play idle Home first");s.StartCoroutine(Run(s));}
        static IEnumerator Run(SteamCarrySession s)
        {
            Last=new Report(); var lab=s.lab; s.StartLocalTest();lab.Testing=true;
            var floor=Box("3C floor",new Vector3(1500,-.5f,0),new Vector3(100,1,100));
            var platform=Box("3C moving platform",new Vector3(1700,-.5f,0),new Vector3(20,1,20));
            var rb=platform.AddComponent<Rigidbody>();rb.isKinematic=true;rb.interpolation=RigidbodyInterpolation.Interpolate;
            var driver=platform.AddComponent<ControlFeelMovingFloor>();driver.body=rb;
            var wall=Box("3C sliding wall",new Vector3(1502,2,0),new Vector3(1,6,20));wall.SetActive(false);
            var modeField=typeof(M0CarryBaseline).GetField("controlFeelMode");
            int modes=modeField==null?1:2;
            for(int mode=0;mode<modes;mode++)
            {
                if(modeField!=null)modeField.SetValue(lab.baseline,(float)mode);
                var sample=new Sample{mode=mode==0?"A previous":"B candidate"};Last.samples.Add(sample);
                lab.PlacePlayer(0,new Vector3(1500,.02f,0));lab.SetInput(0,Vector3.zero,false);yield return Steps(20);
                lab.SetInput(0,Vector3.right,false);for(int i=0;i<50;i++){yield return Steps(1);if(lab.Players[0].body.linearVelocity.x>=3.95f){sample.startTime=(i+1)*Time.fixedDeltaTime;break;}}
                lab.SetInput(0,Vector3.left,false);for(int i=0;i<50;i++){yield return Steps(1);if(lab.Players[0].body.linearVelocity.x<=-3.95f){sample.reverseTime=(i+1)*Time.fixedDeltaTime;break;}}
                float x=lab.Players[0].body.position.x;lab.SetInput(0,Vector3.zero,false);yield return Steps(30);sample.stopDistance=Mathf.Abs(lab.Players[0].body.position.x-x);
                lab.PlacePlayer(0,new Vector3(1500,.02f,0));yield return Steps(20);lab.SetInput(0,Vector3.right,false);yield return Steps(30);
                x=lab.Players[0].body.position.x;lab.SetInput(0,Vector3.right,false,true);yield return Steps(1);lab.SetInput(0,Vector3.zero,false);
                for(int i=0;i<90;i++){yield return Steps(1);sample.releasedJumpPeak=Mathf.Max(sample.releasedJumpPeak,lab.Players[0].body.position.y);if(i>10&&lab.Players[0].grounded){sample.releasedJumpTime=(i+2)*Time.fixedDeltaTime;break;}}
                sample.releasedJumpDistance=lab.Players[0].body.position.x-x;
                rb.position=new Vector3(1700,-.5f,0);driver.velocity=Vector3.zero;lab.PlacePlayer(0,new Vector3(1700,.02f,0));yield return Steps(20);
                driver.velocity=Vector3.up*2.5f;yield return Steps(20);int grounded=0;
                for(int i=0;i<50;i++){yield return Steps(1);if(lab.Players[0].grounded)grounded++;}sample.liftGroundedFraction=grounded/50f;
                lab.SetInput(0,Vector3.zero,false,true);for(int i=0;i<45;i++){yield return Steps(1);sample.liftJumpRelativePeak=Mathf.Max(sample.liftJumpRelativePeak,lab.Players[0].body.position.y-(rb.position.y+.5f));}
                driver.velocity=Vector3.zero;yield return Steps(1);rb.position=new Vector3(1700,-.5f,0);lab.PlacePlayer(0,new Vector3(1700,.02f,0));yield return Steps(20);
                driver.velocity=Vector3.right*2;yield return Steps(20);float relativeStart=lab.Players[0].body.position.x-rb.position.x;yield return Steps(100);sample.movingFloorDrift=Mathf.Abs(lab.Players[0].body.position.x-rb.position.x-relativeStart);driver.velocity=Vector3.zero;
                wall.SetActive(true);lab.PlacePlayer(0,new Vector3(1501.17f,.02f,-4));yield return Steps(20);float z=lab.Players[0].body.position.z;lab.SetInput(0,new Vector3(1,0,1).normalized,false);yield return Steps(60);sample.wallSlideDistance=lab.Players[0].body.position.z-z;wall.SetActive(false);lab.SetInput(0,Vector3.zero,false);
                Check(sample.startTime>0&&sample.startTime<=.14f&&sample.reverseTime>0&&sample.reverseTime<=.24f&&sample.stopDistance<.15f,sample.mode+" ground_response",JsonUtility.ToJson(sample));
                Check(sample.releasedJumpPeak>.60f&&sample.releasedJumpPeak<.72f&&sample.releasedJumpTime<.8f,sample.mode+" jump_height_time",sample.releasedJumpPeak+" / "+sample.releasedJumpTime);
                Check(sample.wallSlideDistance>2.5f,sample.mode+" wall_slide",sample.wallSlideDistance.ToString());
                if(mode==1){Check(sample.liftGroundedFraction>.95f&&sample.liftJumpRelativePeak>.55f,"B moving_platform_jump",sample.liftGroundedFraction+" / "+sample.liftJumpRelativePeak);Check(sample.movingFloorDrift<.15f,"B moving_floor_no_slip",sample.movingFloorDrift.ToString());}
            }
            if(modes==2)Check(Last.samples[1].releasedJumpDistance>Last.samples[0].releasedJumpDistance*1.5f&&Last.samples[1].releasedJumpDistance<1.5f,"B modest_air_release_carry",Last.samples[0].releasedJumpDistance+" -> "+Last.samples[1].releasedJumpDistance);
            s.ReturnToLobby();foreach(var go in new[]{floor,platform,wall})UnityEngine.Object.Destroy(go);
            Last.complete=true;Last.passed=Last.checks.All(c=>c.passed);Directory.CreateDirectory("Logs");File.WriteAllText("Logs/control-feel-comparison.json",JsonUtility.ToJson(Last,true));Debug.Log("3C checks complete passed="+Last.passed);
        }
    }
}
