using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TankTogether.M0;

namespace TankTogether.Editor
{
    public static class WhiteboxLongGapProbe
    {
        [Serializable] public class Trial
        {
            public string variant, reason;
            public float impulse, damping, lead, delay, speedAtPress, rise, maxTilt, tiltAtFirstLanding, firstLandingSeconds=-1, allLandingSeconds=-1, maxGrip, finalSpeed;
            public bool sprint=true, success, broken;
            public int initialGrounded, initialGrips, groundedAtFirstPress, groundedAtEnd;
            public Vector3 end;
        }
        [Serializable] public class Report
        {
            public string utc, baseline, scope;
            public bool complete;
            public float near=-38, far=-40, laneZ=-2f, simulationTimeScale=2;
            public List<Trial> trials=new List<Trial>();
        }
        static M0CarryLab lab;
        static Transform terrain;
        static bool Grounded(M0CarryLab.Actor a) => Physics.Raycast(a.body.position+Vector3.up*.08f,Vector3.down,.16f,~0,QueryTriggerInteraction.Ignore);
        static IEnumerator Steps(int n) { for(int i=0;i<n;i++)yield return new WaitForFixedUpdate(); }
        [MenuItem("Tank Together/Whitebox/Compare Long Gap")]
        public static void Menu()=>Start(false);
        public static void Start(bool detailed)
        {
            lab=UnityEngine.Object.FindFirstObjectByType<M0CarryLab>();
            if(!EditorApplication.isPlaying||!lab||!lab.GetComponent<WhiteboxCarryLevel>()||lab.Testing)throw new InvalidOperationException("Play idle whitebox first");
            terrain=lab.GetComponent<WhiteboxCarryLevel>().levelRoot;
            lab.StartCoroutine(Run(detailed));
        }
        static IEnumerator Run(bool detailed)
        {
            var original=lab.baseline;
            var temp=UnityEngine.Object.Instantiate(original);
            float oldScale=Time.timeScale;
            var report=new Report {utc=DateTime.UtcNow.ToString("O"),baseline=original.Signature,scope="Real second gap, independent input, fixed dt; setup only before trial; no geometry changes"};
            string path=detailed?"Logs/whitebox-longgap-detail.json":"Logs/whitebox-longgap-sweep.json";
            lab.Testing=true;lab.baseline=temp;Time.timeScale=2;
            try
            {
                var variants=detailed?new[]{"selected"}:new[]{"user12","jump9p3","jump9p3_damp5","jump10p2_damp5"};
                foreach(string variant in variants)
                foreach(float lead in detailed?new[]{1.3f,1f,.65f,.35f,.1f,-.15f}:new[]{1f,.65f,.35f,.1f})
                foreach(float delay in detailed?new[]{0f,.10f,.20f}:new[]{0f})
                {
                    temp.carryJumpVelocity=detailed?original.carryJumpVelocity:variant=="user12"?12:variant=="jump10p2_damp5"?10.2f:9.3f;
                    temp.angularDamping=detailed?original.angularDamping:variant.Contains("damp5")?5:2;
                    var t=new Trial {variant=variant,impulse=temp.carryJumpVelocity,damping=temp.angularDamping,lead=lead,delay=delay};
                    lab.ResetLab();lab.Tank.transform.parent.position+=new Vector3(-33,-.5f,-2f);
                    Physics.SyncTransforms();yield return Steps(60);
                    for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
                    yield return Steps(20);
                    for(int i=0;i<4;i++){if(Grounded(lab.Players[i]))t.initialGrounded++;if(lab.IsGripping(i))t.initialGrips++;}
                    if(lab.Broken||t.initialGrounded!=4||t.initialGrips!=4) {t.reason="Invalid fixture";report.trials.Add(t);continue;}
                    float initialY=lab.Tank.position.y;
                    bool[] frontRow=new bool[4], sent=new bool[4];
                    for(int i=0;i<4;i++)frontRow[i]=lab.Players[i].body.position.x<lab.Tank.position.x;
                    int pressed=-1, landed=0;
                    for(int step=0;step<450;step++)
                    {
                        float front=float.MaxValue;
                        for(int i=0;i<4;i++)front=Mathf.Min(front,lab.Players[i].body.position.x);
                        if(pressed<0&&front<=report.near+lead)
                        {
                            pressed=step;t.speedAtPress=-lab.Tank.linearVelocity.x;
                            for(int i=0;i<4;i++)if(Grounded(lab.Players[i]))t.groundedAtFirstPress++;
                        }
                        for(int i=0;i<4;i++)
                        {
                            int wait=frontRow[i]?0:Mathf.RoundToInt(delay/Time.fixedDeltaTime);
                            bool jump=pressed>=0&&!sent[i]&&step-pressed>=wait;
                            if(jump)sent[i]=true;
                            lab.SetInput(i,Vector3.left,true,jump,false,t.sprint);
                        }
                        yield return Steps(1);
                        if(pressed<0)continue;
                        float elapsed=(step-pressed+1)*Time.fixedDeltaTime;
                        float tilt=Vector3.Angle(lab.Tank.transform.up,Vector3.up);
                        t.rise=Mathf.Max(t.rise,lab.Tank.position.y-initialY);
                        t.maxTilt=Mathf.Max(t.maxTilt,tilt);t.maxGrip=Mathf.Max(t.maxGrip,lab.MaxGripError);
                        bool all=true, any=false, fell=false;
                        for(int i=0;i<4;i++)
                        {
                            bool bank=lab.Players[i].body.position.x<report.far-.35f&&Grounded(lab.Players[i]);
                            all &= bank;any |= bank;fell |= lab.Players[i].body.position.y< -7;
                        }
                        if(any&&t.firstLandingSeconds<0){t.firstLandingSeconds=elapsed;t.tiltAtFirstLanding=tilt;}
                        if(all&&t.allLandingSeconds<0)t.allLandingSeconds=elapsed;
                        landed=all?landed+1:0;
                        if(landed>=5)
                        {
                            for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
                            for(int brake=0;brake<100;brake++)
                            {
                                yield return Steps(1);
                                t.maxTilt=Mathf.Max(t.maxTilt,Vector3.Angle(lab.Tank.transform.up,Vector3.up));
                                if(lab.Broken)break;
                            }
                            t.success=!lab.Broken;
                            for(int i=0;i<4;i++)t.success &= Grounded(lab.Players[i])&&lab.Players[i].body.position.x<report.far-.35f;
                            t.success &= lab.Tank.linearVelocity.magnitude<.15f;
                            t.reason=t.success?"All four landed, tank intact, braked":"Unstable after landing/braking";
                            break;
                        }
                        if(lab.Broken||fell){t.reason=lab.Broken?lab.Message:"Fell below route";break;}
                    }
                    if(string.IsNullOrEmpty(t.reason))t.reason="Failed to get all four onto far bank";
                    for(int i=0;i<4;i++){lab.SetInput(i,Vector3.zero,true);if(Grounded(lab.Players[i]))t.groundedAtEnd++;}
                    t.broken=lab.Broken;t.end=lab.Tank.position;t.finalSpeed=lab.Tank.linearVelocity.magnitude;
                    report.trials.Add(t);File.WriteAllText(path,JsonUtility.ToJson(report,true));
                }
                report.complete=true;File.WriteAllText(path,JsonUtility.ToJson(report,true));
            }
            finally {Time.timeScale=oldScale;lab.baseline=original;UnityEngine.Object.Destroy(temp);lab.ResetLab();lab.Testing=false;}
        }
    }
}
