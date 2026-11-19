using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;

namespace TankTogether.Editor
{
    public static class LobbyChecks
    {
        [Serializable] public class Report {
            public string utc, scope = "editor-local-lobby-transitions-and-single-steam-host";
            public bool complete, passed;
            public List<M0CarryChecks.Check> checks = new List<M0CarryChecks.Check>();
        }
        public static Report Last;
        static object Field(SteamCarrySession s, string name) =>
            typeof(SteamCarrySession).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s);
        static void Check(bool ok, string name, string detail) =>
            Last.checks.Add(new M0CarryChecks.Check { name = name, passed = ok, evidence = detail });
        static int Bodies(M0CarryLab lab) => lab.Players.Count(a => a != null && a.body && a.active);
        static string SaveFingerprint() {
            var dir = Path.Combine(Application.persistentDataPath, "CoopCampaigns");
            if (!Directory.Exists(dir)) return "";
            using var hash = System.Security.Cryptography.SHA256.Create();
            return string.Join("|", Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(p => p)
                .Select(p => p + ":" + Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(p)))));
        }
        [MenuItem("Tank Together/Steam/Check Lobby Transitions")]
        public static void Begin() {
            var s = UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
            if (!EditorApplication.isPlaying || !s || s.Mode != SteamCarrySession.EntryMode.Home || !s.CanChooseMode)
                throw new InvalidOperationException("Start Play at the idle entry lobby");
            s.StartCoroutine(Run(s));
        }
        static IEnumerator Run(SteamCarrySession s) {
            Last = new Report { utc = DateTime.UtcNow.ToString("O") };
            var lab = s.lab;
            var before = SaveFingerprint();
            Check(!(bool)Field(s,"initialized") && Bodies(lab)==0 && lab.Tank.isKinematic,
                "startup_home_no_steam_no_players_frozen", "initialized="+Field(s,"initialized")+" bodies="+Bodies(lab));
            s.CreateRoom();
            Check(!s.Host && Field(s,"net")==null, "home_cannot_accidentally_host", "network absent");
            Check(s.StartLocalTest(), "local_entry_available_without_steam_init", "started");
            yield return null;
            Check(Bodies(lab)==3 && !lab.ExternalControl && !lab.Testing && !lab.DynamicRoster && lab.Simulate
                && lab.Mode==1 && lab.Selected==0 && !lab.Tank.isKinematic,
                "three_actor_simulator_restored", "bodies="+Bodies(lab)+" mode="+lab.Mode);
            Check(!s.StartLocalTest() && !s.OpenSteamMenu(), "duplicate_start_and_steam_during_local_rejected", "no reset or network takeover");
            s.CreateRoom(); s.JoinRoom();
            Check(Field(s,"net")==null && !(bool)Field(s,"busy"), "local_cannot_host_or_join", "network absent");
            lab.Testing=true; // Own only input during deterministic physical movement.
            for(int t=0;t<35;t++) yield return new WaitForFixedUpdate();
            Vector3 start=lab.Tank.position;
            var direction=Quaternion.Euler(0,lab.GetComponent<WhiteboxCarryLevel>().startYaw,0)*Vector3.forward;
            for(int i=0;i<lab.LocalPlayerCount;i++) lab.SetInput(i,direction,true);
            for(int t=0;t<60;t++) yield return new WaitForFixedUpdate();
            Check(!lab.Broken && Vector3.Dot(lab.Tank.position-start,direction)>1 &&
                Enumerable.Range(0,3).All(lab.IsGripping), "local_carry_physics_moves", "distance="+Vector3.Distance(start,lab.Tank.position));
            lab.Testing=false;
            s.ReturnToLobby();
            yield return null;
            Check(s.Mode==SteamCarrySession.EntryMode.Home && Bodies(lab)==0 && lab.Tank.isKinematic &&
                Cursor.lockState==CursorLockMode.None, "return_clears_bodies_grips_and_cursor", "bodies="+Bodies(lab));
            for(int cycle=0;cycle<3;cycle++) {
                Check(s.StartLocalTest(), "repeat_local_"+cycle, "entry");
                lab.ChangeMode(3);
                s.ReturnToLobby();
                yield return null;
            }
            s.StartLocalTest();
            Check(lab.Mode==1 && lab.Selected==0, "reentry_resets_simulation_mode", "mode="+lab.Mode);
            s.ReturnToLobby();
            yield return null;
            Check(SaveFingerprint()==before, "local_testing_never_changes_campaign_files", "full directory content fingerprint");
            Check(s.OpenSteamMenu(), "steam_menu_after_local", "opened");
            Check((bool)Field(s,"initialized"), "steam_initializes_on_demand", "single desktop account");
            s.CreateRoom();
            float deadline=Time.unscaledTime+30;
            while((bool)Field(s,"busy") && Time.unscaledTime<deadline) yield return null;
            yield return new WaitForSecondsRealtime(1);
            Check(s.Host && s.PlayerCount==1 && Bodies(lab)==1 && lab.DynamicRoster, "host_only_one_real_actor", "count="+s.PlayerCount+" bodies="+Bodies(lab));
            Check(!s.StartOrResume() && !s.StartLocalTest(), "single_host_cannot_play_or_switch_without_leaving", "room requires three");
            s.ReturnToLobby();
            Check(!s.StartLocalTest(), "teardown_blocks_new_local_start", "wait for NGO disposal");
            deadline=Time.unscaledTime+10;
            while(!s.CanChooseMode && Time.unscaledTime<deadline) yield return null;
            Check(s.CanChooseMode && !s.Host && Bodies(lab)==0 && Field(s,"net")==null,
                "host_exit_returns_clean_home", "network removed");
            Check(s.StartLocalTest(), "local_after_host_shutdown", "started");
            yield return null;
            Check(Bodies(lab)==3 && !lab.Tank.isKinematic && !lab.ExternalControl, "post_network_local_physics_restored", "bodies="+Bodies(lab));
            s.ReturnToLobby();
            yield return null;
            Last.complete=true; Last.passed=Last.checks.All(c=>c.passed);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/lobby-checks-20261013.json",JsonUtility.ToJson(Last,true));
            Debug.Log("[LobbyChecks] Passed="+Last.passed+" checks="+Last.checks.Count);
        }
    }
}
