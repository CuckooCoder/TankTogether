using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEngine;
using UnityEditor;

namespace TankTogether.Editor
{
    // Local physics/roster tests. These are deliberately not labelled remote multiplayer tests.
    public static class CoopSessionChecks
    {
        [Serializable] public class Report { public string utc, scope = "local-roster-and-physics"; public bool complete, passed; public List<M0CarryChecks.Check> checks = new List<M0CarryChecks.Check>(); }
        public static Report Last;
        public static void Begin()
        {
            var session = UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
            if (!EditorApplication.isPlaying || session == null || session.Host || session.PlayerCount != 0) throw new InvalidOperationException("Play idle Steam lab, outside any room");
            session.enabled = false;
            session.lab.StartCoroutine(Run(session));
        }
        public static void BeginTwoPlayerProbe() {
            var session = UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
            if (!EditorApplication.isPlaying || !session || session.Host) throw new InvalidOperationException("Idle Steam lab required");
            session.enabled=false; session.lab.StartCoroutine(TwoPlayerProbe(session));
        }
        static IEnumerator TwoPlayerProbe(SteamCarrySession session) {
            var lab=session.lab; var result=new Report { utc=DateTime.UtcNow.ToString("O"), scope="two-player-support-probe" }; Last=result;
            Vector3[] offsets={ new Vector3(0,0,.55f), new Vector3(.9f,0,.55f), new Vector3(0,0,.55f), new Vector3(.9f,0,.55f), new Vector3(.9f,0,0) };
            float[] damping={2,2,5,5,12};
            for(int variant=0;variant<offsets.Length;variant++) {
                lab.SessionTeamSize=2; lab.ConfigureSessionRoster(new[]{true,true,false,false},true);lab.SetSessionPaused(false);
                yield return Steps(40);
                var origin=MoveTankOffStands(lab);
                lab.PlacePlayer(0,origin+offsets[variant]+Vector3.up*.02f);lab.PlacePlayer(1,origin-offsets[variant]+Vector3.up*.02f);lab.Tank.angularDamping=damping[variant];
                Physics.SyncTransforms();
                for(int i=0;i<2;i++)lab.SetInput(i,Vector3.forward,true);
                bool heldUntilImpact=Grips(lab)==2;
                float maxTilt=0;
                for(int t=0;t<430&&!lab.Broken;t++){
                    if(t==90 || t==130) for(int i=0;i<2;i++)lab.SetInput(i,Vector3.zero,true,t==130);
                    yield return Steps(1);
                    maxTilt=Mathf.Max(maxTilt,Vector3.Angle(lab.Tank.rotation*Vector3.up,Vector3.up));
                    if(!lab.Broken)heldUntilImpact &= Grips(lab)==2;
                }
                float floorGap=lab.Tank.GetComponent<BoxCollider>().bounds.min.y;
                Check(heldUntilImpact && maxTilt>18 && lab.Broken && lab.LastFailureWasDanger && Grips(lab)==0 && Mathf.Abs(floorGap)<.12f,
                    "variant_"+variant,"offset="+offsets[variant]+"; damping="+damping[variant]+"; maxTilt="+maxTilt+"; heldUntilImpact="+heldUntilImpact+"; floorGap="+floorGap+"; broken="+lab.Broken);
            }
            result.complete=true;result.passed=result.checks.All(c=>c.passed);
            File.WriteAllText("Logs/coop-two-player-probe.json",JsonUtility.ToJson(result,true));lab.ConfigureSessionRoster(new bool[4],false);lab.SetSessionPaused(true);session.enabled=true;
        }
        static IEnumerator Steps(int count) { for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate(); }
        static void Check(bool ok, string name, string detail) { Last.checks.Add(new M0CarryChecks.Check { name = name, passed = ok, evidence = detail }); }
        static int Bodies(M0CarryLab lab) => lab.Players.Count(a => a != null && a.body && a.active);
        static int Grips(M0CarryLab lab) => Enumerable.Range(0,4).Count(lab.IsGripping);
        static Vector3 MoveTankOffStands(M0CarryLab lab)
        {
            var center = lab.Tank.position + Vector3.left * 3;
            lab.Tank.position = center;
            lab.Tank.transform.position = center;
            return Vector3.ProjectOnPlane(center, Vector3.up);
        }
        static IEnumerator Run(SteamCarrySession session)
        {
            var lab = session.lab;
            Last = new Report { utc = DateTime.UtcNow.ToString("O") };
            Check(Bodies(lab) == 0, "menu_has_no_spawned_players", "actual=" + Bodies(lab));
            lab.SessionTeamSize = 2;
            lab.ConfigureSessionRoster(new[] { true, false, false, false }, true); lab.SetSessionPaused(true);
            var tankAt = lab.Tank.position;
            yield return Steps(20);
            Check(Bodies(lab) == 1 && !lab.Players[1].body && !lab.Players[2].body && !lab.Players[3].body, "one_member_one_actual_body", "actual=" + Bodies(lab));
            Check(lab.Tank.isKinematic && lab.Tank.position == tankAt, "waiting_physics_is_frozen", "tank=" + lab.Tank.position);
            for (int n = 2; n <= lab.LocalPlayerCount; n++)
            {
                bool[] roster = Enumerable.Range(0, 4).Select(i => i < n).ToArray();
                lab.SessionTeamSize = n; lab.ConfigureSessionRoster(roster, true); lab.SetSessionPaused(true);
                yield return null;
                Check(Bodies(lab) == n, n + "_actual_bodies", "count=" + Bodies(lab));
                Vector3 centroid = Vector3.zero;
                foreach (var actor in lab.Players) if (actor.body) centroid += actor.body.position / n;
                Check(Mathf.Abs(centroid.x - lab.Tank.position.x) < .001f && Mathf.Abs(centroid.z - lab.Tank.position.z) < .001f, n + "_balanced_spawn", centroid.ToString("F3"));
                Check(Mathf.Abs(lab.Tank.mass - lab.baseline.tankMass*n/4f) < .001f, n + "_fixed_session_load", "kg=" + lab.Tank.mass);
                lab.SetSessionPaused(false);
                yield return Steps(40);
                if (n == 2)
                {
                    var origin = MoveTankOffStands(lab);
                    for (int i = 0; i < n; i++) lab.PlacePlayer(i, origin + new Vector3(0,.02f,i==0?-.55f:.55f));
                    Physics.SyncTransforms();
                    for (int i = 0; i < n; i++) lab.SetInput(i, Vector3.zero, true);
                    Check(Grips(lab)==n, "two_players_can_grab", "grips=" + Grips(lab));
                    float maxTilt = 0;
                    bool heldUntilImpact = true;
                    for (int t=0;t<500 && !lab.Broken;t++)
                    {
                        yield return Steps(1);
                        maxTilt=Mathf.Max(maxTilt,Vector3.Angle(lab.Tank.rotation*Vector3.up,Vector3.up));
                        if(!lab.Broken)heldUntilImpact &= Grips(lab)==n;
                    }
                    float floorGap = lab.Tank.GetComponent<BoxCollider>().bounds.min.y;
                    Check(maxTilt>18 && heldUntilImpact && lab.Broken && lab.LastFailureWasDanger && Grips(lab)==0 && Mathf.Abs(floorGap)<.12f,
                        "two_grips_tip_on_open_floor","maxTilt="+maxTilt+"; heldUntilImpact="+heldUntilImpact+"; floorGap="+floorGap+"; broken="+lab.Broken);
                }
                else
                {
                    for (int i = 0; i < n; i++) lab.SetInput(i, Vector3.forward, true);
                    yield return Steps(90);
                    float speed = new Vector2(lab.Tank.linearVelocity.x, lab.Tank.linearVelocity.z).magnitude;
                    Check(!lab.Broken && Grips(lab) == n && lab.Tank.position.z > 2 && speed > lab.baseline.carrySpeed*.8f,
                        n + "_carry_at_usable_speed", "z=" + lab.Tank.position.z + "; speed=" + speed + "; grips=" + Grips(lab) + "; broken=" + lab.Broken);
                    for (int i = 0; i < n; i++) lab.SetInput(i, Vector3.zero, true);
                    yield return Steps(40);
                    float baseY = lab.Tank.position.y, peak = baseY, tilt = 0;
                    for (int i = 0; i < n; i++) lab.SetInput(i, Vector3.zero, true, true);
                    for (int t = 0; t < 70; t++) { yield return Steps(1); peak = Mathf.Max(peak,lab.Tank.position.y); tilt = Mathf.Max(tilt,Vector3.Angle(lab.Tank.transform.up, Vector3.up)); }
                    Check(!lab.Broken && peak > baseY + .1f && tilt < 35, n + "_jump_and_land", "lift=" + (peak-baseY) + "; maxTilt=" + tilt + "; broken=" + lab.Broken);
                }
                lab.SetSessionPaused(true); var before = lab.Tank.position;
                lab.SetActorActive(n-1, false);
                yield return Steps(10);
                Check(Bodies(lab) == n-1 && Grips(lab)==0 && lab.Tank.position == before && lab.Tank.isKinematic,
                    n + "_departure_freezes_before_removal", "remaining=" + Bodies(lab));
            }
            Vector3[] directions = {Vector3.left,Vector3.back,new Vector3(-1,0,1).normalized};
            foreach (var direction in directions) {
                lab.SessionTeamSize=lab.LocalPlayerCount;lab.ConfigureSessionRoster(new[]{true,true,true,false},true);lab.SetSessionPaused(false);
                yield return Steps(40); float maxTilt=0;
                for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,direction,true);
                for(int t=0;t<80;t++){yield return Steps(1);maxTilt=Mathf.Max(maxTilt,Vector3.Angle(lab.Tank.transform.up,Vector3.up));}
                for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,Vector3.zero,true,true);
                for(int t=0;t<80;t++){yield return Steps(1);maxTilt=Mathf.Max(maxTilt,Vector3.Angle(lab.Tank.transform.up,Vector3.up));}
                Check(!lab.Broken && Grips(lab)==lab.LocalPlayerCount && maxTilt<35,"three_player_direction_"+direction,"maxTilt="+maxTilt+"; broken="+lab.Broken);
            }
            lab.SessionTeamSize=lab.LocalPlayerCount;lab.ConfigureSessionRoster(new[]{true,true,true,false},true);lab.SetSessionPaused(false);yield return Steps(40);
            for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,Vector3.forward,true);yield return Steps(60);
            for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,Vector3.left,true);yield return Steps(60);
            for(int i=0;i<lab.LocalPlayerCount;i++)lab.SetInput(i,Vector3.zero,true);yield return Steps(100);
            Check(!lab.Broken && Grips(lab)==lab.LocalPlayerCount && Vector3.Angle(lab.Tank.transform.up,Vector3.up)<35,"three_player_direction_change","tilt="+Vector3.Angle(lab.Tank.transform.up,Vector3.up));
            lab.SessionTeamSize=2; lab.ConfigureSessionRoster(new[] {true,false,true,false},true); lab.SetSessionPaused(true);
            Check(Bodies(lab)==2 && lab.Players[0].body && lab.Players[2].body && !lab.Players[1].body, "sparse_roster_keeps_slot_identity", "P1/P3 only");
            Check(Mathf.Abs(lab.Players[0].body.position.z+lab.Players[2].body.position.z)<.001f, "sparse_roster_balanced", "front/back");
            lab.ConfigureSessionRoster(new[] {true,false,true,false},false); lab.SetSessionPaused(false);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var targetField = typeof(SteamCarrySession).GetField("target", flags);
            var applySnapshot = typeof(SteamCarrySession).GetMethod("ApplySnapshot", flags);
            var previous = targetField.GetValue(session);
            var snapshot = new SteamCarryProtocol.Snapshot { position = lab.Tank.position, rotation = lab.Tank.rotation };
            for (int i=0; i<lab.LocalPlayerCount; i++)
            {
                var a = lab.Players[i];
                snapshot.actors[i] = new SteamCarryProtocol.Actor { active=a.active, height=a.height, position=a.body ? a.body.position : Vector3.zero };
            }
            bool followsHost = true;
            try
            {
                targetField.SetValue(session,snapshot);
                foreach (var velocity in new[] { new Vector3(.8f,2f,-.35f), new Vector3(-.2f,-1.5f,.4f) })
                {
                    snapshot.velocity = velocity;
                    applySnapshot.Invoke(session,new object[] { 1f });
                    followsHost &= lab.Tank.linearVelocity == velocity;
                }
            }
            finally { targetField.SetValue(session,previous); }
            Check(lab.Tank.isKinematic && lab.Players[2].body.isKinematic && !lab.Tank.GetComponent<Rigidbody>() && followsHost,
                "client_proxy_stays_kinematic", "consecutive host velocities applied="+followsHost);
            lab.ConfigureSessionRoster(new bool[4],false); lab.SetSessionPaused(true);
            Last.complete=true; Last.passed=Last.checks.All(c=>c.passed);
            File.WriteAllText("Logs/coop-session-physics-checks.json",JsonUtility.ToJson(Last,true));
            session.enabled=true;
            Debug.Log("[CoopChecks] local roster/physics passed="+Last.passed);
        }
    }
}
