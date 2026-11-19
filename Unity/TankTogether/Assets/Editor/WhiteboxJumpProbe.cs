using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TankTogether.M0;

namespace TankTogether.Editor
{
    public static class WhiteboxJumpProbe
    {
        [Serializable] public class Trial
        {
            public bool sprint, success, broken;
            public string variant;
            public float jumpVelocity, effectiveGap;
            public float lead, takeoffX, speed, tankRise, lowestFeet, lastX;
            public int groundedAtPress;
            public string reason;
        }
        [Serializable] public class Report
        {
            public string utc, baseline;
            public float nearEdge, farEdge, gapWidth;
            public List<Trial> trials=new List<Trial>();
        }
        static bool comparison, documentCandidate;
        [MenuItem("Tank Together/Whitebox/Probe Document Gap")]
        public static void DocumentGap() { documentCandidate=true; comparison=true; Start(); }
        [MenuItem("Tank Together/Whitebox/Compare First Jump")]
        public static void Compare() { comparison=true; Start(); }
        static bool Ground(Transform root,float x,float z,out RaycastHit hit)
        {
            foreach(var h in Physics.RaycastAll(new Vector3(x,2,z),Vector3.down,8))
                if(h.collider.transform.IsChildOf(root)) {hit=h;return true;}
            hit=default;return false;
        }
        static bool Grounded(M0CarryLab.Actor a) => Physics.Raycast(a.body.position+Vector3.up*.08f,Vector3.down,.16f);
        static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
        [MenuItem("Tank Together/Whitebox/Probe First Jump")]
        public static void Start()
        {
            var lab=UnityEngine.Object.FindFirstObjectByType<M0CarryLab>();
            if(!EditorApplication.isPlaying||!lab||!lab.GetComponent<WhiteboxCarryLevel>()||lab.Testing)throw new InvalidOperationException("Play idle whitebox first");
            lab.StartCoroutine(Run(lab));
        }
        static IEnumerator Run(M0CarryLab lab)
        {
            var route=lab.GetComponent<WhiteboxCarryLevel>();
            var report=new Report {utc=DateTime.UtcNow.ToString("O"),baseline=lab.baseline.Signature};
            bool gap=false;
            for(int i=0;i<800;i++)
            {
                float x=-23-i*.01f;
                bool solid=Ground(route.levelRoot,x,-4,out _);
                if(!solid&&!gap){report.nearEdge=x;gap=true;}
                if(solid&&gap){report.farEdge=x;break;}
            }
            if(report.farEdge==0)throw new InvalidOperationException("Gap not found");
            report.gapWidth=report.nearEdge-report.farEdge;
            bool compare=comparison, candidate=documentCandidate; comparison=false;documentCandidate=false;
            var original=lab.baseline;
            var temporary=UnityEngine.Object.Instantiate(original);
            lab.baseline=temporary;
            GameObject extension=null;
            lab.Testing=true;
            try
            {
                foreach(string variant in candidate ? new[]{"gap_0.8m"} : compare ? new[]{"gap_1.5m","jump_velocity_8"} : new[]{"original"})
                foreach(bool sprint in new[]{false,true}) foreach(float lead in compare ? new[]{.65f,.35f,.1f} : new[]{1f,.65f,.35f,.1f})
                {
                    if(extension) { extension.SetActive(false); UnityEngine.Object.Destroy(extension); }
                    float takeoffEdge=report.nearEdge;
                    temporary.carryJumpVelocity=variant=="jump_velocity_8" ? 8 : original.carryJumpVelocity;
                    if(variant.StartsWith("gap_"))
                    {
                        float length=report.gapWidth-(variant=="gap_0.8m"?.8f:1.5f);
                        extension=GameObject.CreatePrimitive(PrimitiveType.Cube);
                        extension.name="Temporary probe gap extension";
                        extension.transform.position=new Vector3(report.nearEdge-length/2,-1,-4);
                        extension.transform.localScale=new Vector3(length,1,6);
                        extension.AddComponent<M0Danger>();
                        takeoffEdge-=length;
                    }
                    lab.ResetLab();
                    // Isolated setup at the real bridge before takeoff. No repositioning during the attempt.
                    lab.Tank.transform.parent.position+=new Vector3(-20,-.5f,-4);
                    Physics.SyncTransforms();yield return Steps(60);
                    for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,true);
                    yield return Steps(20);
                    var t=new Trial {sprint=sprint,lead=lead,variant=variant,jumpVelocity=temporary.carryJumpVelocity,effectiveGap=takeoffEdge-report.farEdge};
                    float startBottom=lab.Tank.position.y-lab.tankSize.y/2;
                    bool pressed=false;int landed=0;
                    for(int step=0;step<300;step++)
                    {
                        float front=float.MaxValue;
                        for(int i=0;i<4;i++)front=Mathf.Min(front,lab.Players[i].body.position.x);
                        bool jump=!pressed&&front<=takeoffEdge+lead;
                        if(jump)
                        {
                            pressed=true;t.takeoffX=front;t.speed=-lab.Tank.linearVelocity.x;
                            for(int i=0;i<4;i++)if(Grounded(lab.Players[i]))t.groundedAtPress++;
                        }
                        for(int i=0;i<4;i++)lab.SetInput(i,Vector3.left,true,jump,false,sprint);
                        yield return new WaitForFixedUpdate();
                        t.tankRise=Mathf.Max(t.tankRise,lab.Tank.position.y-lab.tankSize.y/2-startBottom);
                        bool all=true;
                        for(int i=0;i<4;i++)
                        {
                            t.lowestFeet=Mathf.Min(t.lowestFeet,lab.Players[i].body.position.y);
                            all &= lab.Players[i].body.position.x<report.farEdge-.35f&&Grounded(lab.Players[i]);
                        }
                        landed=all?landed+1:0;
                        if(landed>=15){t.success=true;break;}
                        if(lab.Broken||t.lowestFeet < -6)break;
                    }
                    t.lastX=lab.Tank.position.x;t.broken=lab.Broken;t.reason=lab.Broken?lab.Message:t.success?"All four landed beyond gap":"Fell or failed to land all four";
                    for(int i=0;i<4;i++)lab.SetInput(i,Vector3.zero,false);
                    report.trials.Add(t);
                    File.WriteAllText(candidate?"Logs/whitebox-jump-document.json":compare?"Logs/whitebox-jump-comparison.json":"Logs/whitebox-jump-probe.json",JsonUtility.ToJson(report,true));
                    Debug.Log("[JumpProbe] sprint="+sprint+" lead="+lead+" success="+t.success+" rise="+t.tankRise+" reason="+t.reason);
                }
            }
            finally {if(extension) UnityEngine.Object.Destroy(extension); lab.baseline=original; UnityEngine.Object.Destroy(temporary); lab.ResetLab();lab.Testing=false;}
        }
    }
}
