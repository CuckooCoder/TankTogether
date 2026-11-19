using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TankTogether.M0
{
    public static class WhiteboxCarryChecks
    {
        [Serializable] public class Item { public string name, evidence; public bool passed; }
        [Serializable] public class Report { public string utc, baseline; public bool passed; public List<Item> checks = new List<Item>(); public List<string> routeSamples = new List<string>(); }
        static IEnumerator Steps(int count) { for (int i=0;i<count;i++) yield return new WaitForFixedUpdate(); }
        static void Add(Report r,string n,bool ok,string evidence) { r.checks.Add(new Item { name=n,passed=ok,evidence=evidence }); }
        static IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var image=ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes("Logs/"+name+".png",image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
        }
        public static IEnumerator Run(M0CarryLab lab)
        {
            var route=lab.GetComponent<WhiteboxCarryLevel>();
            var report=new Report { utc=DateTime.UtcNow.ToString("O"), baseline=lab.baseline.Signature };
            lab.Testing=true;
            try
            {
                lab.ResetLab(); yield return Steps(80);
                bool grounded=true;
                for(int i=0;i<lab.LocalPlayerCount;i++) grounded &= Physics.Raycast(lab.Players[i].body.position+Vector3.up*.08f,Vector3.down,.16f);
                Add(report,"spawn_three_grounded_tank_intact",grounded&&!lab.Broken,"tank="+lab.Tank.position);
                Add(report,"no_old_floor_wall_or_m1",!GameObject.Find("Danger floor")&&!GameObject.Find("Danger wall")&&!lab.GetComponent<M1InteractionLab>(),"Only imported route and initial safe stands");
                for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,Vector3.zero,true);
                yield return Steps(20);
                bool grips=true; for(int i=0;i<lab.LocalPlayerCount;i++)grips &= lab.IsGripping(i);
                Add(report,"three_grips_at_start",grips,"baseline="+lab.baseline.Signature);
                yield return Shot("whitebox-start");
                var start=lab.Tank.position;
                for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,route.Forward,true);
                yield return Steps(80);
                for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,Vector3.zero,true);
                yield return Steps(80);
                float advance=Vector3.Dot(lab.Tank.position-start,route.Forward);
                Add(report,"carry_forward_and_brake",!lab.Broken&&advance>3&&lab.Tank.linearVelocity.magnitude<.15f,"forward metres="+advance+" speed="+lab.Tank.linearVelocity.magnitude);
                yield return Shot("whitebox-forward");
                lab.ResetLab(); yield return Steps(60);
                var tankBefore=lab.Tank.position;
                lab.PlacePlayer(2,new Vector3(0,route.killHeight-1,0)); yield return Steps(15);
                Add(report,"fall_respawns_only_player_at_start",Vector3.Distance(lab.Players[2].body.position,route.RespawnPoint(2))<.2f&&Vector3.Distance(lab.Tank.position,tankBefore)<.15f&&!lab.Broken,"P3="+lab.Players[2].body.position);
                // Isolated collision fixture: intentionally touch the real imported floor.
                lab.Tank.position=new Vector3(4,.1f,0); lab.Tank.linearVelocity=Vector3.zero;
                yield return Steps(8);
                Add(report,"imported_floor_contact_breaks_tank",lab.Broken,lab.Message);
                lab.ResetLab();yield return Steps(60);
                Add(report,"reset_returns_to_marker",!lab.Broken&&Vector3.Distance(Vector3.ProjectOnPlane(lab.Tank.position,Vector3.up),route.startFeet)<.1f,"tank="+lab.Tank.position);
                // Record the original geometry; a sample is not a route-completion assertion.
                for(float x=-12;x>=-68;x-=1)
                {
                    if(Physics.Raycast(new Vector3(x,2,0),Vector3.down,out var hit,50)) report.routeSamples.Add(x+","+hit.point.y+","+hit.collider.name);
                    else report.routeSamples.Add(x+",gap");
                }
                report.passed=report.checks.TrueForAll(c=>c.passed);
                File.WriteAllText("Logs/whitebox-checks.json",JsonUtility.ToJson(report,true));
                Debug.Log("[Whitebox] Checks passed="+report.passed+" count="+report.checks.Count);
            }
            finally { lab.ResetLab(); lab.Testing=false; }
        }
    }
}
