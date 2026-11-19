using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TankTogether.M0;

namespace TankTogether.Editor
{
    public static class WhiteboxJumpFeelProbe
    {
        [Serializable] public class Trial
        {
            public float impulse, damping, rise, airSeconds, distance, maxGripError;
            public bool sprint, broken, settled;
            public int mode, groundedAtPress, gripsAtPress, groundedAtEnd;
            public float finalSpeed; public string finalFeet;
        }
        [Serializable] public class Report
        {
            public string utc, parameters;
            public bool complete;
            public List<Trial> trials = new List<Trial>();
        }
        static bool Grounded(M0CarryLab.Actor a) => Physics.Raycast(a.body.position + Vector3.up * .08f, Vector3.down, .16f, ~0, QueryTriggerInteraction.Ignore);
        static IEnumerator Steps(int n) { for (int i=0;i<n;i++) yield return new WaitForFixedUpdate(); }
        [MenuItem("Tank Together/Whitebox/Compare Jump Feel")]
        public static void Start()
        {
            var lab=UnityEngine.Object.FindFirstObjectByType<M0CarryLab>();
            if (!EditorApplication.isPlaying || !lab || !lab.GetComponent<WhiteboxCarryLevel>() || lab.Testing) throw new InvalidOperationException("Play idle whitebox first");
            lab.StartCoroutine(Run(lab));
        }
        static IEnumerator Run(M0CarryLab lab)
        {
            var original=lab.baseline;
            var temporary=UnityEngine.Object.Instantiate(original);
            var report=new Report { utc=DateTime.UtcNow.ToString("O"), parameters=original.Signature };
            lab.baseline=temporary; lab.Testing=true;
            try
            {
                for (int test=0;test<6;test++)
                {
                    var t=new Trial { impulse=test<2?12f:original.carryJumpVelocity, damping=test<2?2:original.angularDamping, sprint=test%2==1, mode=test<4?0:test-3 };
                    temporary.carryJumpVelocity=t.impulse; temporary.angularDamping=t.damping;
                    lab.ResetLab(); lab.Tank.transform.parent.position += Vector3.left * 5; Physics.SyncTransforms(); yield return Steps(60);
                    for(int i=0;i<4;i++) lab.SetInput(i,Vector3.zero,true);
                    yield return Steps(20);
                    for(int i=0;i<4;i++) lab.SetInput(i,Vector3.right,true,false,false,t.sprint);
                    yield return Steps(50);
                    float startY=lab.Tank.position.y, startX=lab.Tank.position.x;
                    for(int i=0;i<4;i++) { if(Grounded(lab.Players[i]))t.groundedAtPress++; if(lab.Players[i].joint)t.gripsAtPress++; }
                    bool airborne=false, landed=false; int quiet=0;
                    for(int step=0;step<125;step++)
                    {
                        for(int i=0;i<4;i++)
                        {
                            bool jump=t.mode==0 ? step==0 : t.mode==1 ? step==0&&i==0 : (step==0&&i>=2)||(step==10&&i<2);
                            lab.SetInput(i,Vector3.right,true,jump,false,t.sprint);
                        }
                        yield return Steps(1);
                        t.rise=Mathf.Max(t.rise,lab.Tank.position.y-startY);
                        bool all=true; for(int i=0;i<4;i++) all &= Grounded(lab.Players[i]);
                        if(!all) airborne=true;
                        if(airborne&&!landed) { t.airSeconds=(step+1)*Time.fixedDeltaTime; t.distance=lab.Tank.position.x-startX; }
                        quiet=airborne&&all?quiet+1:0;
                        if(quiet>=3) {landed=true;break;}
                        if(lab.Broken)break;
                    }
                    for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
                    yield return Steps(200);
                    t.settled=landed;
                    for(int i=0;i<4;i++)t.settled &= Grounded(lab.Players[i]);
                    for(int i=0;i<4;i++) { if(Grounded(lab.Players[i]))t.groundedAtEnd++; t.finalFeet+=lab.Players[i].body.position.ToString("F3")+" "; } t.finalSpeed=lab.Tank.linearVelocity.magnitude;
                    t.broken=lab.Broken;t.maxGripError=lab.MaxGripError;report.trials.Add(t);
                    File.WriteAllText("Logs/whitebox-jump-feel.json",JsonUtility.ToJson(report,true));
                }
                report.complete=true;
                File.WriteAllText("Logs/whitebox-jump-feel.json",JsonUtility.ToJson(report,true));
            }
            finally { lab.baseline=original; UnityEngine.Object.Destroy(temporary);lab.ResetLab();lab.Testing=false; }
        }
    }
}
