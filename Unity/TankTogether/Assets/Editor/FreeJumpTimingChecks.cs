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
    public static class FreeJumpTimingChecks
    {
        [Serializable] public class Sample { public float multiplier, speed, peak, apexSeconds, landedSeconds, distance; public bool landed; }
        [Serializable] public class Report { public bool complete,passed; public string scope="local-fixed-step-flat-ground-comparison-not-full-route-or-remote-multiplayer";public List<Sample> samples=new List<Sample>();public List<M0CarryChecks.Check> checks=new List<M0CarryChecks.Check>(); }
        public static Report Last;
        static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
        static void Check(bool ok,string name,string evidence){Last.checks.Add(new M0CarryChecks.Check{passed=ok,name=name,evidence=evidence});}
        public static void Begin()
        {
            var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
            if(!Application.isPlaying||s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Play idle lobby");
            s.StartCoroutine(Run(s));
        }
        static IEnumerator Run(SteamCarrySession s)
        {
            Last=new Report();s.StartLocalTest();var lab=s.lab;lab.Testing=true;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Free jump timing test floor";floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(100,1,100);
            foreach(float mult in new[]{1f,2f})foreach(float speed in new[]{0f,4f,6f})
            {
                lab.baseline.freeFallGravityMultiplier=mult;lab.PlacePlayer(0,new Vector3(1000,.02f,0));lab.SetInput(0,Vector3.zero,false);yield return Steps(20);
                var move=speed==0?Vector3.zero:Vector3.right;bool sprint=speed>4;
                lab.SetInput(0,move,false,false,false,sprint);yield return Steps(40);
                var actor=lab.Players[0];var start=actor.body.position;
                var result=new Sample{multiplier=mult,speed=speed};Last.samples.Add(result);
                lab.SetInput(0,move,false,true,false,sprint);
                bool tookOff=false;
                for(int i=1;i<=100;i++){
                    yield return Steps(1);float y=actor.body.position.y-start.y;
                    if(y>result.peak){result.peak=y;result.apexSeconds=i*Time.fixedDeltaTime;}
                    if(y>.1f)tookOff=true;
                    // Contact height/velocity, not the permissive pre-contact ground probe.
                    if(tookOff&&y<=.012f&&Mathf.Abs(actor.body.linearVelocity.y)<.15f){result.landed=true;result.landedSeconds=i*Time.fixedDeltaTime;result.distance=actor.body.position.x-start.x;break;}
                }
                lab.SetInput(0,Vector3.zero,false);yield return Steps(20);
            }
            for(int i=0;i<3;i++){
                var before=Last.samples[i];var after=Last.samples[i+3];
                Check(before.landed&&after.landed&&after.landedSeconds<before.landedSeconds-.06f,"shorter_airtime_speed_"+before.speed,$"{before.landedSeconds:F3}s -> {after.landedSeconds:F3}s");
                Check(Mathf.Abs(after.peak-before.peak)<.015f&&after.peak>.60f,"same_peak_speed_"+before.speed,$"{before.peak:F3}m -> {after.peak:F3}m");
                Check(Mathf.Abs(after.apexSeconds-before.apexSeconds)<.021f,"same_ascent_speed_"+before.speed,$"{before.apexSeconds:F3}s -> {after.apexSeconds:F3}s");
            }
            var peaks=new float[2];int sampleIndex=0;
            foreach(float mult in new[]{1f,2f}){
                lab.ResetLab();lab.baseline.freeFallGravityMultiplier=mult;
                for(int i=0;i<4;i++)lab.PlacePlayer(i,lab.Tank.position+new Vector3(i%2==0?-.9f:.9f,-lab.tankSize.y/2-lab.standingHeight,i<2?-.55f:.55f));
                yield return Steps(20);for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);yield return Steps(5);
                Check(Enumerable.Range(0,4).All(lab.IsGripping),"carry_grips_multiplier_"+mult,"all four actual joints");
                float y=lab.Tank.position.y;for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true,true);
                for(int i=0;i<50;i++){yield return Steps(1);peaks[sampleIndex]=Mathf.Max(peaks[sampleIndex],lab.Tank.position.y-y);}
                Check(!lab.Broken,"carry_not_broken_multiplier_"+mult,peaks[sampleIndex].ToString("F3"));sampleIndex++;
            }
            Check(Mathf.Abs(peaks[0]-peaks[1])<.03f,"held_tank_jump_unchanged",$"tank rise: {peaks[0]:F3}m -> {peaks[1]:F3}m");
            s.ReturnToLobby();UnityEngine.Object.Destroy(floor);Last.complete=true;Last.passed=Last.checks.All(c=>c.passed);
            File.WriteAllText("Logs/free-jump-timing.json",JsonUtility.ToJson(Last,true));Debug.Log("Free jump timing passed="+Last.passed);
        }
    }
}
