using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TankTogether.Formal;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;

namespace TankTogether.Editor
{
    public static class CarryGapDistanceChecks
    {
        [Serializable] public class Foot
        {
            public int firstLipStep = -1, requestStep = -1, jumpEventStep = -1, firstAirStep = -1;
            public bool groundedAtLip, nearTopAtLip, groundedAtRequest, nearTopAtRequest, bearingAtRequest;
            public float leadAtLip, requestLead, requestY, footVyAtRequest, gripVyAtRequest, gripSupportN;
            public float takeoffGripY, takeoffHeadY, peakGripRise, peakHeadRise;
            public int firstThreeSamples;
            public float maxFootVyFirstThree, peakRise;
            public int nearTopAfterAir = -1, farTopAfterAir = -1, leftTopAgain = -1, belowLipStep = -1;
            public int nearWallStep = -1, farWallStep = -1, nearWallTicks, farWallTicks;
            public string finalLocation;
            public float finalLead, finalHeight, finalForwardSpeed;
        }

        [Serializable] public class Trial
        {
            public float width, targetLead, peakTilt;
            public string jump, outcome;
            public int farFeet, footContactMask;
            public int[] takeoffStep = { -1, -1, -1 };
            public float[] takeoffLead = { -1, -1, -1 };
            public bool held = true;
            public Foot[] feet = { new Foot(), new Foot(), new Foot() };
        }

        [Serializable] public class Report
        {
            public bool complete, passed;
            public Vector3[] localGrips;
            public float[] gripSeparations;
            public List<Trial> trials = new List<Trial>();
        }

        static readonly float[] Widths = { .8f, 1.4f, 2f, 2.5f, 3f, 3.5f };
        enum Jump { Together, PairFirst, SingleFirst, LeftFirst, RightFirst }
        const string FileName = "Logs/carry-gap-distances.json";
        static readonly WaitForFixedUpdate Tick = new WaitForFixedUpdate();
        public static Report Last { get; private set; }

        [MenuItem("Tank Together/Tests/Check Carry Jump Distances")]
        public static void Begin()
        {
            var session = UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
            if (!Application.isPlaying || !session || session.Mode != SteamCarrySession.EntryMode.Home || !session.StartLocalStretchLevel())
                throw new InvalidOperationException("Enter Play at the formal whitebox main menu.");
            session.StartCoroutine(RunStandalone(session.lab, session));
        }

        static BoxCollider MakeBank(Transform root, Vector3 center, string label)
        {
            var bank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bank.name = label;
            bank.transform.SetParent(root, false);
            bank.transform.position = center;
            bank.transform.localScale = new Vector3(14, 8, 14);
            return bank.GetComponent<BoxCollider>();
        }

        public static IEnumerator RunStandalone(M0CarryLab lab, SteamCarrySession session = null)
        {
            Last = new Report();
            if (session) session.enabled = false;
            lab.Testing = true;
            lab.Stretch.ManualInput = true;
            var course = new GameObject("三人边缘抓取 · 等高断崖对照");
            try
            {
                for (int lane = 0; lane < Widths.Length; lane++)
                {
                    float x = 700 + 18 * lane, width = Widths[lane];
                    var near = MakeBank(course.transform, new Vector3(x, -4, 693), $"起跳岸 {width:F1} m");
                    var far = MakeBank(course.transform, new Vector3(x, -4, 707 + width), $"落脚岸 {width:F1} m");
                    Physics.SyncTransforms();
                    foreach (Jump jump in Enum.GetValues(typeof(Jump)))
                    foreach (float lead in new[] { .12f, .20f })
                    {
                        var trial = new Trial { width = far.bounds.min.z - near.bounds.max.z, jump = jump.ToString(), targetLead = lead };
                        Last.trials.Add(trial);
                        yield return Cross(lab, near, far, jump, trial);
                        File.WriteAllText(session ? FileName : "Logs/carry-standalone-gap-distances.json", JsonUtility.ToJson(Last, true));
                    }
                }
                Last.complete = true;
                // A failed crossing is a measured outcome. Invalid setups or missing results invalidate the experiment.
                Last.passed = Last.trials.TrueForAll(t => t.outcome != null && t.outcome != "invalid setup") &&
                    Last.trials.Exists(t => t.jump == Jump.LeftFirst.ToString() && t.outcome == "landed");
            }
            finally
            {
                UnityEngine.Object.Destroy(course);
                lab.Stretch.ManualInput = false;
                if (session) { session.enabled = true; session.ReturnToLobby(); }
                File.WriteAllText(session ? FileName : "Logs/carry-standalone-gap-distances.json", JsonUtility.ToJson(Last, true));
            }
        }

        static IEnumerator Cross(M0CarryLab lab, BoxCollider near, BoxCollider far, Jump jump, Trial trial)
        {
            lab.ResetLab();
            float yaw = jump == Jump.SingleFirst ? 180 : jump == Jump.LeftFirst ? 17 : jump == Jump.RightFirst ? -17 : 0;
            var rotation = Quaternion.Euler(0, yaw, 0);
            var start = new Vector3(near.bounds.center.x, near.bounds.max.y + .005f, near.bounds.max.z - 6f);
            var position = start + Vector3.up * (lab.standingHeight + lab.tankSize.y / 2);
            lab.Tank.position = position;
            lab.Tank.rotation = rotation;
            lab.Tank.transform.SetPositionAndRotation(position, rotation);
            var joints = new ConfigurableJoint[3];
            var probes = new CheckpointContactProbe[3];
            for (int i = 0; i < 3; i++)
            {
                Vector3 feet = start + rotation * lab.CarryFormationOffset(i);
                lab.PlacePlayer(i, feet);
                lab.Players[i].body.transform.position = feet;
                probes[i] = lab.Players[i].body.gameObject.AddComponent<CheckpointContactProbe>();
                probes[i].RegisterDanger(far);
                probes[i].RegisterPost(near, 0, 0);
                probes[i].RegisterPost(far, 0, 1);
            }
            Physics.SyncTransforms();
            for (int i = 0; i < 3; i++)
            {
                lab.SetInput(i, Vector3.zero, false);
                lab.SetInput(i, Vector3.zero, true);
                joints[i] = lab.Players[i].joint;
            }
            for (int step = 0; step < 25; step++) yield return Tick;
            var grips = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                grips[i] = lab.Players[i].anchor;
                Vector3 offset = lab.CarryFormationOffset(i);
                if (!joints[i] || !lab.Players[i].grounded || Vector2.Distance(new Vector2(grips[i].x,grips[i].z),
                        new Vector2(offset.x,offset.z)) > .02f)
                { trial.outcome = "invalid setup"; yield break; }
            }
            if (Last.localGrips == null)
            {
                Last.localGrips = grips;
                Last.gripSeparations = new[] { Vector3.Distance(grips[0],grips[1]), Vector3.Distance(grips[1],grips[2]), Vector3.Distance(grips[2],grips[0]) };
            }
            int tick = 0, requests = 0, settled = 0;
            byte[] airFrame = null;
            var box = lab.Tank.GetComponent<BoxCollider>();
            void RecordJump(int index, string action)
            {
                if (action != "jump") return;
                trial.takeoffStep[index] = tick;
                trial.feet[index].jumpEventStep = tick;
                trial.feet[index].takeoffGripY = (lab.Tank.position + lab.Tank.rotation * grips[index]).y;
                trial.feet[index].takeoffHeadY = lab.Head(index).y;
                trial.takeoffLead[index] = near.bounds.max.z - lab.Players[index].body.position.z;
            }
            lab.PlayerAction += RecordJump;
            try
            {
                for (tick = 0; tick < 320; tick++)
                {
                    int atLip = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        var a = lab.Players[i];
                        float lead = near.bounds.max.z - a.body.position.z;
                        bool nearTop = near.Raycast(new Ray(a.body.position + Vector3.up * .08f, Vector3.down), out _, .14f);
                        var f = trial.feet[i];
                        if (lead <= trial.targetLead && f.firstLipStep < 0)
                        {
                            f.firstLipStep = tick; f.leadAtLip = lead; f.groundedAtLip = a.grounded; f.nearTopAtLip = nearTop;
                        }
                        if (lead >= 0 && lead <= trial.targetLead && a.grounded && nearTop)
                            atLip |= 1 << i;
                    }
                    int press = atLip;
                    if (jump == Jump.Together) press = atLip != 0 && requests == 0 ? 7 : 0;
                    else if (jump == Jump.PairFirst || jump == Jump.SingleFirst)
                        press = ((atLip & 3) == 3 ? 3 : 0) | (atLip & 4);
                    press &= ~requests;
                    requests |= press;
                    for (int i = 0; i < 3; i++)
                    {
                        var a = lab.Players[i];
                        bool requested = (press & (1 << i)) != 0;
                        if (requested)
                        {
                            var f = trial.feet[i]; var foot = a.body.position;
                            f.requestStep = tick; f.requestLead = near.bounds.max.z - foot.z; f.requestY = foot.y;
                            f.groundedAtRequest = a.grounded;
                            f.nearTopAtRequest = near.Raycast(new Ray(foot + Vector3.up * .08f, Vector3.down), out _, .14f);
                            f.gripSupportN = lab.Stretch.Forces[i]; f.bearingAtRequest = a.grounded && a.support;
                            // Both measured banks are static. World vertical speeds are relative to the bank.
                            f.footVyAtRequest = a.body.linearVelocity.y;
                            f.gripVyAtRequest = lab.Tank.GetPointVelocity(lab.Tank.position + lab.Tank.rotation * grips[i]).y;
                        }
                        lab.SetInput(i, settled == 0 ? Vector3.forward : Vector3.zero, true, jump: requested, sprint: true);
                    }
                    yield return Tick;
                    trial.peakTilt = Mathf.Max(trial.peakTilt, Vector3.Angle(lab.Tank.rotation * Vector3.up, Vector3.up));
                    int onBank = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        var a = lab.Players[i];
                        trial.held &= a.joint && a.joint == joints[i] && a.joint.connectedAnchor == grips[i];
                        var contacts = probes[i].ReadContacts();
                        contacts.TryGetValue(0, out int touching);
                        var f = trial.feet[i]; var foot = a.body.position;
                        float above = foot.y - near.bounds.max.y;
                        bool nearTop = (touching & 1) != 0 && near.Raycast(new Ray(foot + Vector3.up * .08f, Vector3.down), out _, .14f);
                        bool farTop = (touching & 2) != 0 && far.Raycast(new Ray(foot + Vector3.up * .08f, Vector3.down), out _, .14f);
                        // The two lane banks are vertical-sided boxes. Contact below the top, on the gap side, is a lip/wall contact.
                        bool nearWall = (touching & 1) != 0 && above < -.05f && foot.z > near.bounds.max.z;
                        bool farWall = (touching & 2) != 0 && above < -.05f && foot.z < far.bounds.min.z;
                        if (nearWall) { if (f.nearWallStep < 0) f.nearWallStep = tick; f.nearWallTicks++; }
                        if (farWall) { if (f.farWallStep < 0) f.farWallStep = tick; f.farWallTicks++; }
                        if (above < -.05f && f.belowLipStep < 0) f.belowLipStep = tick;
                        if (f.requestStep >= 0)
                        {
                            if (f.firstThreeSamples < 3)
                            {
                                float vy = a.body.linearVelocity.y;
                                f.maxFootVyFirstThree = f.firstThreeSamples == 0 ? vy : Mathf.Max(f.maxFootVyFirstThree, vy);
                                f.firstThreeSamples++;
                            }
                            f.peakRise = Mathf.Max(f.peakRise, foot.y - f.requestY);
                            if (f.jumpEventStep >= 0 && f.nearTopAfterAir < 0 && f.farTopAfterAir < 0)
                            {
                                float gripY = (lab.Tank.position + lab.Tank.rotation * grips[i]).y;
                                f.peakGripRise = Mathf.Max(f.peakGripRise, gripY - f.takeoffGripY);
                                f.peakHeadRise = Mathf.Max(f.peakHeadRise, lab.Head(i).y - f.takeoffHeadY);
                            }
                            if (f.firstAirStep < 0 && touching == 0 && !a.grounded && foot.y - f.requestY > .05f) f.firstAirStep = tick;
                            if (f.firstAirStep >= 0)
                            {
                                if (nearTop && f.nearTopAfterAir < 0) f.nearTopAfterAir = tick;
                                if (farTop && f.farTopAfterAir < 0) f.farTopAfterAir = tick;
                                if ((f.nearTopAfterAir >= 0 || f.farTopAfterAir >= 0) && touching == 0 && f.leftTopAgain < 0) f.leftTopAgain = tick;
                            }
                        }
                        f.finalLocation = nearTop ? "near top" : farTop ? "far top" : nearWall ? "near wall" : farWall ? "far wall" :
                            touching != 0 ? "bank edge contact" : foot.z <= near.bounds.max.z ? "over near bank" :
                            foot.z >= far.bounds.min.z ? "over far bank" : above < 0 ? "below gap" : "above gap";
                        f.finalLead = near.bounds.max.z - foot.z; f.finalHeight = above; f.finalForwardSpeed = a.body.linearVelocity.z;
                        if (a.grounded && probes[i].Danger && a.body.position.z > far.bounds.min.z) onBank |= 1 << i;
                    }
                    trial.footContactMask |= onBank;
                    trial.farFeet = (onBank & 1) + ((onBank >> 1) & 1) + ((onBank >> 2) & 1);
                    if (trial.width == 2f && jump == Jump.LeftFirst && trial.targetLead < .15f && requests == 7 && airFrame == null)
                        airFrame = CarryCliffJumpChecks.CameraFrame(lab, Vector3.forward);
                    if (lab.Broken || !trial.held) { trial.outcome = lab.Broken ? "glass hit terrain" : "lost grip"; yield break; }
                    if (Array.Exists(lab.Players, a => a != null && a.body && a.body.position.y < near.bounds.max.y - 2.5f))
                    { trial.outcome = "fell short"; yield break; }
                    settled = onBank == 7 && box.bounds.min.z > far.bounds.min.z + .05f ? settled + 1 : 0;
                    if (settled >= 35 && lab.Tank.linearVelocity.magnitude < .2f)
                    {
                        trial.outcome = Array.TrueForAll(trial.takeoffStep, t => t >= 0) ? "landed" : "crossed without three jumps";
                        if (airFrame != null && trial.outcome == "landed")
                        {
                            File.WriteAllBytes("Logs/carry-distance-2m-air.png", airFrame);
                            var frame = CarryCliffJumpChecks.CameraFrame(lab, Vector3.forward);
                            if (frame != null) File.WriteAllBytes("Logs/carry-distance-2m-land.png", frame);
                        }
                        yield break;
                    }
                }
                trial.outcome = "did not settle on far bank";
            }
            finally { lab.PlayerAction -= RecordJump; }
        }
    }
}
