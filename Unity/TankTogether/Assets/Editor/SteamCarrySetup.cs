using System;
using System.Collections.Generic;
using System.IO;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Collections;
using Unity.Netcode;

namespace TankTogether.Editor
{
    public static class SteamCarrySetup
    {
        public const string ScenePath = "Assets/Scenes/SteamCarryLab.unity";
        [MenuItem("Tank Together/Steam/Open Network Lab")]
        public static void Open()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Stop Play and save current scene first");
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main; camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.09f, 0.12f); camera.fieldOfView = 55; camera.nearClipPlane = 0.05f;
            var light = UnityEngine.Object.FindAnyObjectByType<Light>(); light.transform.rotation = Quaternion.Euler(48, -35, 0); light.intensity = 1.5f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.5f, 0.57f, 0.62f);
            var go = new GameObject("Steam Carry Lab");
            var lab = go.AddComponent<M0CarryLab>(); lab.surfaceShader = Shader.Find("Universal Render Pipeline/Lit");
            go.AddComponent<SteamCarrySession>().lab = lab;
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        [MenuItem("Tank Together/Steam/Open Whitebox Co-op")]
        public static void OpenWhitebox() => FormalWhiteboxSetup.Open();
        [MenuItem("Tank Together/Steam/Check Protocol")]
        public static void Checks()
        {
            var checks = new List<string>();
            void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
            var input = new SteamCarryProtocol.Input { round = 2, sequence = 7, move = Vector3.forward, held = true, jump = true, pauseRequested = true, sentAt = 10 };
            var bytes = SteamCarryProtocol.Encode(input); var decoded = SteamCarryProtocol.DecodeInput(bytes);
            Check(decoded.round == 2 && decoded.sequence == 7 && decoded.held && decoded.jump && decoded.pauseRequested && decoded.move == Vector3.forward, "input_round_trip_edges_and_identity");
            using (var writer = new FastBufferWriter(256, Allocator.Temp))
            {
                writer.WriteValueSafe((ulong)123); writer.WriteBytesSafe(bytes);
                using var reader = new FastBufferReader(writer, Allocator.Temp);
                reader.ReadValueSafe(out ulong hash);
                var payload = SteamCarrySession.ReadPayload(reader);
                Check(hash == 123 && payload.Length == bytes.Length && SteamCarryProtocol.DecodeInput(payload).sequence == 7, "ngo_named_message_payload_offset");
            }
            Check(!SteamCarryProtocol.AcceptInput(decoded, 3, 0), "old_round_input_rejected");
            Check(!SteamCarryProtocol.AcceptInput(decoded, 2, 7), "duplicate_input_rejected");
            Check(SteamCarryProtocol.AcceptInput(decoded, 2, 6), "next_input_accepted");
            Check(!SteamCarryProtocol.IsNewer(1, 999, 2, 1), "old_round_snapshot_rejected");
            Check(!SteamCarryProtocol.IsNewer(2, 2, 2, 3), "out_of_order_snapshot_rejected");
            Check(SteamCarryProtocol.IsNewer(3, 0, 2, 999), "new_round_snapshot_accepted");
            var snapshot = new SteamCarryProtocol.Snapshot { round = 2, tick = 15, broken = true, position = Vector3.one, rotation = Quaternion.Euler(10, 30, 5), velocity = Vector3.forward };
            for (int i = 0; i < 4; i++) snapshot.actors[i] = new SteamCarryProtocol.Actor { active = i < 2, height = 1.8f, grip = i == 1, support = i == 0, rejected = i == 3, position = Vector3.right * i };
            bytes = SteamCarryProtocol.Encode(snapshot); var state = SteamCarryProtocol.DecodeSnapshot(bytes);
            Check(bytes.Length < 1000, "snapshot_fits_unreliable_message");
            Check(state.phase == SteamCarryProtocol.Phase.Waiting && state.teamSize == 3, "session_phase_and_team_round_trip");
            Check(state.broken && state.actors[1].grip && state.actors[0].support && !state.actors[2].active && state.actors[3].rejected, "snapshot_state_round_trip");
            bool Reject(Action action) { try { action(); return false; } catch (Exception e) when (e is IOException || e is InvalidDataException) { return true; } }
            Check(Reject(() => SteamCarryProtocol.DecodeInput(new byte[2])), "truncated_input_rejected");
            input.move = Vector3.right * 5;
            Check(Reject(() => SteamCarryProtocol.DecodeInput(SteamCarryProtocol.Encode(input))), "overspeed_input_rejected");
            input.move = new Vector3(float.NaN, 0, 0);
            Check(Reject(() => SteamCarryProtocol.DecodeInput(SteamCarryProtocol.Encode(input))), "nan_input_rejected");
            Check(Reject(() => SteamCarryProtocol.DecodeSnapshot(new byte[2000])), "oversized_snapshot_rejected");
            snapshot.phase = SteamCarryProtocol.Phase.Failed;
            Check(SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snapshot)).phase == SteamCarryProtocol.Phase.Failed, "failure_phase_round_trip");
            snapshot.phase = (SteamCarryProtocol.Phase)255;
            Check(Reject(() => SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snapshot))), "unknown_phase_rejected");
            snapshot.phase = SteamCarryProtocol.Phase.Waiting;
            snapshot.teamSize = 1;
            Check(Reject(() => SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snapshot))), "invalid_team_size_rejected");
            snapshot.teamSize = 4;
            Check(Reject(() => SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snapshot))), "four_player_snapshot_rejected");
            snapshot.teamSize = 3;
            snapshot.actors[3].active = true;
            Check(Reject(() => SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snapshot))), "reserved_fourth_actor_rejected");
            snapshot.actors[3].active = false;
            snapshot.rotation = new Quaternion(0, 0, 0, 0);
            Check(Reject(() => SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snapshot))), "invalid_rotation_rejected");
            var baseline = Resources.Load<M0CarryBaseline>(M0CarryBaseline.ResourceName);
            Check(baseline != null, "baseline_in_build_resources");
            var altered = UnityEngine.Object.Instantiate(baseline);
            try
            {
                Check(altered.Signature == baseline.Signature, "same_configuration_same_fingerprint");
                altered.playerDiameter += 0.01f;
                Check(altered.Signature != baseline.Signature, "geometry_change_changes_room_compatibility");
                altered.playerDiameter = baseline.playerDiameter;
                altered.carrySpeed += 0.1f;
                Check(altered.Signature != baseline.Signature, "motion_change_changes_room_compatibility");
                Check(System.Text.Encoding.UTF8.GetByteCount(SteamCarryProtocol.Version + ":" + baseline.Signature + ":whitebox-route:coop-prototype-20261013-1:160,18,40:" + Guid.NewGuid().ToString("N")) <= 256, "versioned_approval_payload_fits_limit");
            }
            finally { UnityEngine.Object.DestroyImmediate(altered); }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/steam-protocol-checks.json", JsonUtility.ToJson(new CheckEvidence { utc = DateTime.UtcNow.ToString("O"), passed = true, checks = checks.ToArray() }, true));
            Debug.Log("[SteamCarry] Protocol checks passed: " + checks.Count);
        }
        [MenuItem("Tank Together/Steam/Build Windows")]
        public static void Build() => BuildScene(ScenePath, "Builds/Coop-20261013/SteamCarryLab");
        [MenuItem("Tank Together/Steam/Build Formal Whitebox Windows")]
        public static void BuildFormalWhitebox()=>BuildScene(FormalWhiteboxSetup.ScenePath,"Builds/FormalWhitebox-20261015/Windows");
        static void BuildScene(string scene, string output)
        {
            if (EditorApplication.isPlaying || !File.Exists(scene)) throw new InvalidOperationException("Stop Play and create Steam scene first");
            Directory.CreateDirectory(output);
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { scene }, locationPathName = output + "/TankTogether-Steam.exe", target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            File.WriteAllText("Logs/steam-build.json", JsonUtility.ToJson(new BuildEvidence { utc = DateTime.UtcNow.ToString("O"), result = result.summary.result.ToString(), errors = result.summary.totalErrors, bytes = result.summary.totalSize }, true));
            if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Steam build failed");
            File.WriteAllText(output + "/steam_appid.txt", "480\n");
        }
        [Serializable] class CheckEvidence { public string utc; public bool passed; public string[] checks; }
        [Serializable] class BuildEvidence { public string utc, result; public int errors; public ulong bytes; }
    }
}
