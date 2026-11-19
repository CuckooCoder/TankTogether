using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TankTogether.M0
{
    // Exercises the same input and physics path used by the interactive lab.
    public static class M0CarryChecks
    {
        [Serializable] public class Check { public string name, evidence; public bool passed; }
        [Serializable] public class Report
        {
            public string utc, unityVersion, platform, baseline;
            public bool passed;
            public List<Check> checks = new List<Check>();
        }
        static IEnumerator Steps(int n) { for (int i = 0; i < n; i++) yield return new WaitForFixedUpdate(); }

        public static IEnumerator Run(M0CarryLab lab, bool quit)
        {
            lab.Testing = true;
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion, platform = Application.platform.ToString(), baseline = lab.baseline.Signature };
            void Record(string name, bool passed, string evidence) => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
            lab.ResetLab();
            yield return Steps(100);
            Record("safe_start", !lab.Broken && Mathf.Abs(lab.Tank.position.y - (lab.baseline.carryBottomHeight + lab.tankSize.y / 2)) < 0.15f, "Tank center y=" + lab.Tank.position.y);

            lab.PlacePlayer(0, new Vector3(0, 0.02f, -4));
            lab.SetInput(0, Vector3.zero, true);
            Record("outside_press_rejected", !lab.IsGripping(0), lab.Players[0].feedback);
            // Move the proxy without resetting held state: specifically tests no auto-grab on entry.
            lab.Players[0].body.position = new Vector3(-0.6f, 0.02f, -0.4f);
            lab.Players[0].body.linearVelocity = Vector3.zero;
            lab.SetInput(0, Vector3.zero, true);
            yield return Steps(4);
            Record("held_entry_no_auto_grab", !lab.IsGripping(0), "Held outside then moved under tank");
            lab.SetInput(0, Vector3.zero, false);
            lab.SetInput(0, Vector3.zero, true);
            yield return Steps(4);
            Record("release_repress_grabs", lab.IsGripping(0), lab.Players[0].feedback);
            Vector3 anchor = lab.Players[0].anchor;
            lab.SetInput(0, Vector3.right, true);
            yield return Steps(15);
            Record("local_anchor_fixed", lab.IsGripping(0) && lab.Players[0].anchor == anchor, "Anchor=" + anchor);
            lab.SetInput(0, Vector3.zero, false);
            Record("release_disconnects", !lab.IsGripping(0), "Release removes active relation synchronously");

            lab.ResetLab();
            yield return Steps(60);
            for (int i = 0; i < lab.LocalPlayerCount; i++) lab.SetInput(i, Vector3.forward, true);
            yield return Steps(100);
            float distance = lab.Tank.position.z;
            float speed = lab.Tank.linearVelocity.magnitude;
            Record("gravity_three_player_carry", !lab.Broken && distance > 0.8f && lab.Tank.position.y > 1.8f, "z=" + distance + "; y=" + lab.Tank.position.y);
            Record("bounded_grip", lab.MaxGripError < 0.4f && float.IsFinite(lab.MaxGripError), "Maximum head-anchor distance=" + lab.MaxGripError);
            for (int i = 0; i < lab.LocalPlayerCount; i++) lab.SetInput(i, Vector3.zero, false);
            yield return Steps(1);
            Record("release_keeps_motion", lab.Tank.linearVelocity.magnitude > speed * 0.7f, "Before=" + speed + "; after=" + lab.Tank.linearVelocity.magnitude);

            lab.ResetLab();
            yield return Steps(40);
            // Remove the stand as a support by translating both tank and players into open floor.
            lab.Tank.position += Vector3.forward * 3;
            for (int i = 0; i < lab.LocalPlayerCount; i++) lab.PlacePlayer(i, lab.Players[i].body.position + Vector3.forward * 3);
            yield return Steps(60);
            int supportCount = 0;
            for (int i = 0; i < lab.LocalPlayerCount; i++) if (lab.Players[i].support) supportCount++;
            Record("passive_head_support", !lab.Broken && supportCount > 0 && lab.Tank.position.y > 1.7f, "supportCount=" + supportCount + "; y=" + lab.Tank.position.y);
            Vector3 before = lab.Tank.position;
            for (int i = 0; i < lab.LocalPlayerCount; i++) lab.SetInput(i, Vector3.forward, false);
            yield return Steps(12);
            Record("ungripped_no_horizontal_drive", Mathf.Abs(lab.Tank.position.z - before.z) < 0.08f, "dz=" + (lab.Tank.position.z - before.z));

            lab.ResetLab();
            yield return Steps(40);
            for (int i = 0; i < lab.LocalPlayerCount; i++) lab.SetInput(i, Vector3.forward, true);
            yield return Steps(75);
            float restingY = lab.Tank.position.y;
            for (int i = 0; i < lab.LocalPlayerCount; i++) lab.SetInput(i, Vector3.zero, true, true);
            float peakY = restingY;
            for (int frame = 0; frame < 40; frame++)
            {
                yield return Steps(1);
                peakY = Mathf.Max(peakY, lab.Tank.position.y);
            }
            Record("synchronous_jump_transfers_lift", peakY > restingY + 0.1f && !lab.Broken, "rest=" + restingY + "; peak=" + peakY);
            yield return Steps(40);
            restingY = lab.Tank.position.y;
            for (int i = 0; i < lab.LocalPlayerCount; i++) lab.SetInput(i, Vector3.zero, true, false, true);
            yield return Steps(40);
            Record("crouch_changes_carry_height", lab.Tank.position.y < restingY - 0.15f && !lab.Broken, "before=" + restingY + "; crouched=" + lab.Tank.position.y);
            Record("vertical_constraints_bounded", lab.MaxGripError < 0.4f, "maximum=" + lab.MaxGripError);

            lab.ResetLab();
            yield return Steps(40);
            for (int i = 1; i < lab.LocalPlayerCount; i++) lab.PlacePlayer(i, new Vector3(-5, 0.02f, i));
            lab.PlacePlayer(0, new Vector3(0, 0.02f, 0));
            lab.SetInput(0, Vector3.forward, true);
            yield return Steps(100);
            Record("single_grip_slow_movement", !lab.Broken && lab.Tank.position.z > 0.05f && lab.Tank.position.z < distance * 0.4f,
                "single z=" + lab.Tank.position.z + "; three-player z=" + distance);

            lab.ResetLab();
            yield return Steps(30);
            for (int i = 0; i < lab.LocalPlayerCount; i++) lab.PlacePlayer(i, new Vector3(-5, 0.05f, i));
            lab.Tank.position = new Vector3(0, 4, 3);
            lab.Tank.rotation = Quaternion.Euler(0, 0, 25);
            lab.Tank.linearVelocity = Vector3.zero;
            lab.Tank.angularVelocity = Vector3.zero;
            yield return Steps(15);
            Record("unsupported_gravity", lab.Tank.position.y < 3.8f, "Tank center y=" + lab.Tank.position.y);
            Record("no_airborne_auto_upright", Mathf.Abs(Mathf.DeltaAngle(0, lab.Tank.rotation.eulerAngles.z)) > 20, "roll=" + lab.Tank.rotation.eulerAngles.z);
            yield return Steps(75);
            Record("danger_floor_breaks", lab.Broken, "Tank-floor contact produces failure");
            int failures = lab.FailureCount;
            lab.Fail("duplicate test event");
            Record("failure_idempotent", lab.FailureCount == failures, "Duplicate failure did not increment counter");
            for (int i = 0; i < lab.LocalPlayerCount; i++) Record("failure_clears_grip_" + i, !lab.IsGripping(i), "No active relation after failure");
            lab.ResetLab();
            yield return Steps(40);
            Record("reset_safe", !lab.Broken && lab.Tank.linearVelocity.magnitude < 0.1f, "Safe stand restored");

            report.passed = report.checks.TrueForAll(c => c.passed);
            string output = Application.isEditor ? Path.GetFullPath("Logs/m0-carry-checks.json") : Path.Combine(Application.persistentDataPath, "m0-carry-checks.json");
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-m0-report");
            if (index >= 0 && index + 1 < args.Length) output = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("[M0 Carry] " + report.checks.FindAll(c => c.passed).Count + "/" + report.checks.Count + "; " + output);
            lab.Testing = false;
            if (quit) Application.Quit(report.passed ? 0 : 1);
        }
    }
}
