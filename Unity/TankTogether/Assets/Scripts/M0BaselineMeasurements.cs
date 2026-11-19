using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TankTogether.M0
{
    // Measurements use the real controller/joints under gravity, not a parallel model.
    public static class M0BaselineMeasurements
    {
        [Serializable] public class Sample
        {
            public string name;
            public float steadySpeed, startTo90Seconds = -1, stopDistance, stopSeconds;
            public float playerRise, tankBottomRise, maxGripError;
            public bool broken, settled;
        }
        [Serializable] public class Report
        {
            public string utc, platform, parameters;
            public Vector3 tankSize;
            public float standingHeight, fixedDeltaTime;
            public List<Sample> samples = new List<Sample>();
        }
        static IEnumerator Steps(int count) { for (int i = 0; i < count; i++) yield return new WaitForFixedUpdate(); }
        static float Bottom(M0CarryLab lab) => lab.Tank.transform.TransformPoint(Vector3.down * lab.tankSize.y / 2).y;
        static IEnumerator Prepare(M0CarryLab lab, bool carry)
        {
            lab.ResetLab();
            // A long, level instrumented lane: only test geometry changes, never the motor.
            GameObject.Find("Danger floor").transform.localScale = new Vector3(160, 0.3f, 160);
            lab.Tank.position += Vector3.left * 20;
            for (int i = 0; i < lab.LocalPlayerCount; i++) lab.PlacePlayer(i, lab.Players[i].body.position + Vector3.left * 20);
            if (!carry) lab.PlacePlayer(0, new Vector3(-30, 0.02f, 0));
            yield return Steps(60);
            if (carry) for (int i = 0; i < lab.LocalPlayerCount; i++) lab.SetInput(i, Vector3.zero, true);
            yield return Steps(20);
        }
        public static IEnumerator Run(M0CarryLab lab, bool quit = false)
        {
            lab.Testing = true;
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), platform = Application.platform.ToString(),
                tankSize = lab.tankSize, standingHeight = lab.standingHeight, fixedDeltaTime = Time.fixedDeltaTime,
                parameters = lab.baseline.Signature + " " + JsonUtility.ToJson(lab.baseline) };
            for (int mode = 0; mode < 4; mode++)
            {
                bool carry = mode >= 2, sprint = mode % 2 == 1;
                yield return Prepare(lab, carry);
                var s = new Sample { name = (carry ? "three_carry" : "free") + (sprint ? "_sprint" : "_walk") };
                Vector3 Position() => carry ? lab.Tank.position : lab.Players[0].body.position;
                float Speed() => Vector3.ProjectOnPlane(carry ? lab.Tank.linearVelocity : lab.Players[0].body.linearVelocity, Vector3.up).magnitude;
                int n = Mathf.CeilToInt(3 / Time.fixedDeltaTime);
                var speeds = new List<float>();
                for (int i = 0; i < (carry ? lab.LocalPlayerCount : 1); i++) lab.SetInput(i, Vector3.forward, carry, sprint: sprint);
                for (int step = 0; step < n; step++) { yield return Steps(1); speeds.Add(Speed()); }
                for (int step = n - 25; step < n; step++) s.steadySpeed += speeds[step] / 25;
                for (int step = 0; step < n; step++) if (speeds[step] >= s.steadySpeed * 0.9f) { s.startTo90Seconds = (step + 1) * Time.fixedDeltaTime; break; }
                Vector3 stopStart = Position();
                for (int i = 0; i < (carry ? lab.LocalPlayerCount : 1); i++) lab.SetInput(i, Vector3.zero, carry, sprint: sprint);
                int quiet = 0;
                for (int step = 0; step < Mathf.CeilToInt(4 / Time.fixedDeltaTime); step++)
                {
                    yield return Steps(1);
                    s.stopSeconds = (step + 1) * Time.fixedDeltaTime;
                    quiet = Speed() < 0.05f ? quiet + 1 : 0;
                    if (quiet >= 10) { s.settled = true; break; }
                }
                s.stopDistance = Vector3.ProjectOnPlane(Position() - stopStart, Vector3.up).magnitude;
                s.broken = lab.Broken; s.maxGripError = lab.MaxGripError; report.samples.Add(s);
            }
            for (int mode = 0; mode < 4; mode++)
            {
                bool carry = mode != 0;
                yield return Prepare(lab, carry);
                var s = new Sample { name = new[] { "free_jump", "three_synchronous_jump", "one_of_three_jump", "front_back_stagger_jump" }[mode] };
                float feet = lab.Players[0].body.position.y, bottom = Bottom(lab);
                for (int i = 0; i < lab.LocalPlayerCount; i++) lab.SetInput(i, Vector3.zero, carry, mode == 1 || (mode == 3 ? i >= 2 : i == 0));
                for (int step = 0; step < Mathf.CeilToInt(2 / Time.fixedDeltaTime); step++)
                {
                    if (mode == 3 && step == 10) for (int i = 0; i < 2; i++) lab.SetInput(i, Vector3.zero, true, true);
                    yield return Steps(1);
                    s.playerRise = Mathf.Max(s.playerRise, lab.Players[0].body.position.y - feet);
                    s.tankBottomRise = Mathf.Max(s.tankBottomRise, Bottom(lab) - bottom);
                }
                s.broken = lab.Broken; s.maxGripError = lab.MaxGripError;
                s.settled = lab.Players[0].body.position.y < 0.08f && Mathf.Abs(lab.Players[0].body.linearVelocity.y) < 0.1f;
                report.samples.Add(s);
            }
            string output = Path.GetFullPath("Logs/baseline-measurements.json");
            var args = Environment.GetCommandLineArgs(); int arg = Array.IndexOf(args, "-baseline-report");
            if (arg >= 0 && arg + 1 < args.Length) output = Path.GetFullPath(args[arg + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("[Baseline] Measurements saved: " + output);
            lab.ResetLab(); lab.Testing = false;
            if (quit) Application.Quit(0);
        }
    }
}
