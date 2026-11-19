using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;

namespace TankTogether.Editor
{
    public static class CarryJumpChecks
    {
        [Serializable] public class Report
        {
            public bool complete, passed;
            public List<M0CarryChecks.Check> checks = new List<M0CarryChecks.Check>();
        }

        public static Report Last { get; private set; }
        static readonly WaitForFixedUpdate Tick = new WaitForFixedUpdate();
        static readonly Vector3 Origin = new Vector3(1000, 0, 0);
        static readonly Vector3[] Triangle = { new Vector3(0, 0, 1.12f), new Vector3(.97f, 0, -.56f), new Vector3(-.97f, 0, -.56f) };
        static readonly MethodInfo FixedStep = typeof(M0CarryLab).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        [MenuItem("Tank Together/Tests/Check Three Player Carry Jump")]
        public static void Begin()
        {
            var session = UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
            if (!Application.isPlaying || !session || session.Mode != SteamCarrySession.EntryMode.Home || !session.StartLocalStretchLevel())
                throw new InvalidOperationException("Enter Play in the formal whitebox main menu first.");
            Last = new Report();
            session.StartCoroutine(Run(session.lab, session));
        }

        static void Check(string name, bool pass, string evidence)
        {
            Last.checks.Add(new M0CarryChecks.Check { name = name, passed = pass, evidence = evidence });
        }

        static Vector3 Grip(M0CarryLab lab, int i) => lab.Tank.position + lab.Tank.rotation * lab.Players[i].anchor;

        static void PlaceAndGrip(M0CarryLab lab, Vector3[] points, float slope, float yaw, float offset)
        {
            lab.ResetLab();
            var rotation = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(slope, Vector3.forward);
            var position = Origin + Vector3.up * (lab.standingHeight + lab.tankSize.y / 2);
            lab.Tank.position = position;
            lab.Tank.rotation = rotation;
            lab.Tank.transform.SetPositionAndRotation(position, rotation);
            for (int i = 0; i < 3; i++)
            {
                var anchor = position + rotation * (points[i] + Vector3.forward * offset + Vector3.down * (lab.tankSize.y / 2));
                var feet = new Vector3(anchor.x, Origin.y + .005f, anchor.z);
                lab.PlacePlayer(i, feet);
                var a = lab.Players[i];
                a.body.transform.position = feet;
                a.height = a.capsule.height = anchor.y - feet.y;
                a.capsule.center = Vector3.up * (a.height / 2);
            }
            Physics.SyncTransforms();
            for (int i = 0; i < 3; i++)
            {
                lab.SetInput(i, Vector3.zero, false);
                lab.SetInput(i, Vector3.zero, true);
            }
        }

        static IEnumerator Arrange(M0CarryLab lab, float slope, float yaw, float offset = 0, Vector3[] points = null)
        {
            PlaceAndGrip(lab, points ?? Triangle, slope, yaw, offset);
            for (int step = 0; step < 45; step++) yield return Tick;
        }

        sealed class Flight
        {
            public bool ready = true, held = true, plannedSupports = true, tuningPreserved = true, landed, retried;
            public int airTicks, airborneStages, jumpsAfterLanding, driftStep, driftSlot, driftAirMask, driftSettlementTicks;
            public float driftTime;
            public float firstAxisError, firstSupportsDrift, lastSupportDrift, repeatRise;
            public readonly Vector3[] launchPoses = new Vector3[3]; // Tilt before/after, angular step (degrees).
            public readonly int[] jumpEvents = new int[3];
            public readonly float[] gripPeakRise = new float[3], headPeakRise = new float[3];
            public float target;
            public bool ReachedHeight(int jumpers)
            {
                for (int i = 0; i < jumpers; i++)
                    if (jumpEvents[i] != 1 || Mathf.Abs(gripPeakRise[i] - target) > .08f ||
                        Mathf.Abs(headPeakRise[i] - target) > .08f) return false;
                return tuningPreserved;
            }
            public float rise, tilt, pivotDrift, axisError, supportDirectionChange, initialSlope, maxGripError;
            public readonly float[] gripRise = new float[3];
            public readonly List<Vector4> airMotion = new List<Vector4>(); // Z displacement of tank, P1, P2, P3.
            public readonly Vector4[] firstStep = new Vector4[3], atPeak = new Vector4[3];
            public string Evidence => $"ready={ready} held={held} landed={landed} jumps={string.Join(",", jumpEvents)} " +
                $"targetRise={target:F3} massAndJumpTuningPreserved={tuningPreserved} " +
                $"individualGripApex={string.Join(",", Array.ConvertAll(gripPeakRise, h => h.ToString("F3")))} " +
                $"individualHeadApex={string.Join(",", Array.ConvertAll(headPeakRise, h => h.ToString("F3")))} " +
                $"rise={rise:F3} tilt={tilt:F2} initialSlope={initialSlope:F2} axisError={axisError:F2} supportDrift={pivotDrift:F3} axisDirectionChange={supportDirectionChange:F2} " +
                $"maxDriftAt=P{driftSlot+1}/step{driftStep}/t{driftTime:F3}s plantedMask={7 & ~driftAirMask} precedingPlantedTicks={driftSettlementTicks} " +
                $"gripRise={gripRise[0]:F3},{gripRise[1]:F3},{gripRise[2]:F3} maxGripError={maxGripError:F3} airInputTicks={airTicks} stages={airborneStages} scheduledSupports={plannedSupports} " +
                $"step1[gripDX,DZ,footDX,DZ] P1/P2/P3={string.Join("/", Array.ConvertAll(firstStep, v => v.ToString("F4")))} " +
                $"atPeak P1/P2/P3={string.Join("/", Array.ConvertAll(atPeak, v => v.ToString("F4")))}";
        }

        static IEnumerator Fly(M0CarryLab lab, int jumpers, Flight f, float slope = 0, float yaw = 0,
            int steer = 0, bool retry = false, bool stagger = false, bool reverse = false, float offset = 0, bool lateRear = false)
        {
            var wide = lateRear ? new[] { lab.CarryFormationOffset(0), lab.CarryFormationOffset(1), lab.CarryFormationOffset(2) } : null;
            yield return Arrange(lab, slope, yaw, offset, wide);
            float mass = lab.Tank.mass, jumpTuning = lab.baseline.carryJumpVelocity;
            f.target = lab.baseline.carryJumpHeightTarget;
            var initial = new Vector3[3];
            var joints = new ConfigurableJoint[3];
            var localAnchors = new Vector3[3];
            var startFeet = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                var a = lab.Players[i];
                initial[i] = Grip(lab, i);
                localAnchors[i] = a.anchor;
                joints[i] = a.joint;
                startFeet[i] = a.body.position;
                f.ready &= lab.IsGripping(i) && a.grounded;
            }
            var startTank = lab.Tank.position;
            var startRotation = lab.Tank.rotation;
            Vector3 axis = (initial[2] - initial[1]).normalized;
            f.initialSlope = Mathf.Asin(Mathf.Abs(axis.y)) * Mathf.Rad2Deg;
            bool leftFloor = false, firstAirEnded = false, repeating = false;
            int plantedTicks = 0, expectedAir = 0;
            int spacing = reverse ? 6 : 3;
            int Slot(int order) => reverse ? (order + 2) % 3 : order;
            var jumpAt = new[] { -1, -1, -1 };
            for (int order = 0; order < jumpers; order++)
                jumpAt[stagger ? Slot(order) : order] = stagger ? order * spacing : 0;
            if (lateRear) jumpAt[2] = 27; // 0.54 s front-to-rear interval observed on the 2 m cliff.
            var measuring = new bool[3];
            var leftGround = new bool[3];
            var takeoffGripY = new float[3];
            var takeoffHeadY = new float[3];
            Vector3 lastSupport = Vector3.zero;
            void OnAction(int slot, string action)
            {
                if (action != "jump" || slot < 0 || slot >= 3) return;
                if (repeating) f.jumpsAfterLanding++;
                else if (f.jumpEvents[slot]++ == 0)
                {
                    measuring[slot] = true;
                    takeoffGripY[slot] = Grip(lab, slot).y;
                    takeoffHeadY[slot] = lab.Head(slot).y;
                }
            }
            lab.PlayerAction += OnAction;
            try
            {
                for (int step = 0; step < 160 && !lab.Broken; step++)
                {
                    int stage = stagger && step % spacing == 0 && step / spacing < 3 ? step / spacing : -1;
                    Quaternion previousRotation = lab.Tank.rotation;
                    if (stage == 1) lastSupport = Grip(lab, Slot(2));
                    // Air steering starts only after positive measured foot clearance; no ground input precedes it.
                    bool allRequiredAir = true;
                    for (int i = 0; i < jumpers; i++)
                        allRequiredAir &= !lab.Players[i].grounded && lab.Players[i].body.position.y > startFeet[i].y + .04f;
                    bool airControl = allRequiredAir && step > 0;
                    if (airControl) f.airTicks++;
                    bool secondPress = retry && !f.retried && airControl && Time.time >= lab.Players[0].jumpLockUntil;
                    if (secondPress) f.retried = true;
                    for (int i = 0; i < 3; i++)
                    {
                        bool jump = jumpAt[i] == step || i == 0 && secondPress;
                        lab.SetInput(i, i == 0 && airControl ? Vector3.forward * steer : Vector3.zero, true, jump);
                    }
                    yield return Tick;
                    f.tuningPreserved &= Mathf.Approximately(mass, lab.Tank.mass) && Mathf.Approximately(jumpTuning, lab.baseline.carryJumpVelocity);
                    int airborne = 0, airMask = 0;
                    float activeRise = 0;
                    for (int i = 0; i < 3; i++)
                    {
                        var a = lab.Players[i];
                        if (!a.grounded) { airborne++; airMask |= 1 << i; }
                        if (measuring[i])
                        {
                            f.gripPeakRise[i] = Mathf.Max(f.gripPeakRise[i], Grip(lab, i).y - takeoffGripY[i]);
                            f.headPeakRise[i] = Mathf.Max(f.headPeakRise[i], lab.Head(i).y - takeoffHeadY[i]);
                            leftGround[i] |= !a.grounded && a.body.position.y > startFeet[i].y + .04f;
                            if (leftGround[i] && a.grounded) measuring[i] = false;
                        }
                        f.held &= lab.IsGripping(i) && a.joint == joints[i] && a.joint.connectedBody == lab.Tank &&
                            (a.joint.connectedAnchor - localAnchors[i]).sqrMagnitude < .000001f;
                        f.maxGripError = Mathf.Max(f.maxGripError, Vector3.Distance(lab.Head(i), Grip(lab, i)));
                        if (i < jumpers) activeRise += Grip(lab, i).y - initial[i].y;
                    }
                    if (stage >= 0)
                    {
                        expectedAir |= 1 << Slot(stage);
                        f.plannedSupports &= airMask == expectedAir;
                        Quaternion turn = lab.Tank.rotation * Quaternion.Inverse(previousRotation);
                        turn.ToAngleAxis(out float angle, out Vector3 turnAxis);
                        f.launchPoses[stage] = new Vector3(Quaternion.Angle(startRotation, previousRotation),
                            Quaternion.Angle(startRotation, lab.Tank.rotation), Mathf.Min(angle, 360 - angle));
                        if (stage == 0)
                        {
                            f.firstAxisError = Vector3.Angle(turnAxis, initial[Slot(2)] - initial[Slot(1)]);
                            f.firstAxisError = Mathf.Min(f.firstAxisError, 180 - f.firstAxisError);
                        }
                    }
                    if (stagger && step < spacing)
                        for (int support = 1; support <= 2; support++)
                            f.firstSupportsDrift = Mathf.Max(f.firstSupportsDrift, Vector3.Distance(Grip(lab, Slot(support)), initial[Slot(support)]));
                    if (stagger && step >= spacing && step < spacing*2)
                        f.lastSupportDrift = Mathf.Max(f.lastSupportDrift, Vector3.Distance(lastSupport, Grip(lab, Slot(2))));
                    f.airborneStages |= 1 << airborne;
                    leftFloor |= airborne > 0;
                    if (!airControl && f.airMotion.Count > 0) firstAirEnded = true;
                    if (airControl && !firstAirEnded)
                    {
                        var movement = new Vector4 { x = lab.Tank.position.z - startTank.z };
                        for (int i = 0; i < 3; i++) movement[i+1] = lab.Players[i].body.position.z - startFeet[i].z;
                        f.airMotion.Add(movement);
                    }
                    float rise = jumpers == 3 ? lab.Tank.position.y - startTank.y : activeRise / jumpers;
                    if (step == 0 || rise > f.rise)
                        for (int i = 0; i < 3; i++)
                        {
                            Vector3 gripDelta = Grip(lab, i) - initial[i];
                            Vector3 footDelta = lab.Players[i].body.position - startFeet[i];
                            var sample = new Vector4(gripDelta.x, gripDelta.z, footDelta.x, footDelta.z);
                            if (step == 0) f.firstStep[i] = sample;
                            if (rise > f.rise) f.atPeak[i] = sample;
                        }
                    if (rise > f.rise)
                    {
                        f.rise = rise;
                        Quaternion turn = lab.Tank.rotation * Quaternion.Inverse(startRotation);
                        turn.ToAngleAxis(out f.tilt, out Vector3 turnAxis);
                        f.tilt = Mathf.Min(f.tilt, 360 - f.tilt);
                        f.axisError = Vector3.Angle(axis, turnAxis);
                        f.axisError = Mathf.Min(f.axisError, 180 - f.axisError);
                        f.supportDirectionChange = Vector3.Angle(axis, Grip(lab, 2) - Grip(lab, 1));
                        for (int i = 0; i < 3; i++) f.gripRise[i] = Grip(lab, i).y - initial[i].y;
                    }
                    if (!f.landed && leftFloor)
                        for (int i = jumpers; i < 3; i++)
                        {
                            float drift = Vector3.Distance(Grip(lab, i), initial[i]);
                            if (drift <= f.pivotDrift) continue;
                            f.pivotDrift = drift;
                            f.driftStep = step + 1;
                            f.driftTime = (step + 1) * Time.fixedDeltaTime;
                            f.driftSlot = i;
                            f.driftAirMask = airMask;
                            f.driftSettlementTicks = plantedTicks;
                        }
                    plantedTicks = leftFloor && airborne == 0 ? plantedTicks + 1 : 0;
                    if (plantedTicks >= 6) { f.landed = true; break; }
                }
                f.held &= !lab.Broken;
                if (stagger && f.landed)
                {
                    int leader = Slot(0);
                    repeating = true;
                    float feet = lab.Players[leader].body.position.y;
                    lab.SetInput(leader, Vector3.zero, true, jump: true);
                    for (int step = 0; step < 6; step++)
                    {
                        yield return Tick;
                        f.repeatRise = Mathf.Max(f.repeatRise, lab.Players[leader].body.position.y - feet);
                        for (int i = 0; i < 3; i++) f.held &= lab.IsGripping(i) && lab.Players[i].joint == joints[i];
                    }
                }
            }
            finally { lab.PlayerAction -= OnAction; }
        }

        static IEnumerator CheckAirInput(M0CarryLab lab, int jumpers, Flight neutral)
        {
            var forward = new Flight();
            var back = new Flight();
            yield return Fly(lab, jumpers, forward, steer: 1);
            yield return Fly(lab, jumpers, back, steer: -1);
            int commonTicks = Mathf.Min(neutral.airMotion.Count, Mathf.Min(forward.airMotion.Count, back.airMotion.Count));
            int halfSecond = Mathf.RoundToInt(.5f / Time.fixedDeltaTime);
            bool response = forward.ready && back.ready && forward.held && back.held && forward.landed && back.landed &&
                commonTicks >= halfSecond && commonTicks >= 10;
            var evidence = new string[4];
            for (int i = 0; i < 4; i++)
            {
                int streak = 0, firstResponse = -1;
                for (int tick = 0; tick < Mathf.Min(halfSecond, commonTicks); tick++)
                {
                    float plus = forward.airMotion[tick][i] - neutral.airMotion[tick][i];
                    float minus = back.airMotion[tick][i] - neutral.airMotion[tick][i];
                    // Joint travel can delay a teammate's response. Require opposite responses for two consecutive ticks.
                    streak = plus * minus < 0 && Mathf.Abs(plus - minus) > .002f ? streak + 1 : 0;
                    if (streak == 2 && firstResponse < 0) firstResponse = tick;
                    if (tick == 9 || tick == halfSecond-1) evidence[i] += $" t{tick+1}={plus:F4}/{minus:F4}";
                }
                response &= firstResponse >= 0;
                evidence[i] += $" firstResponseTick={firstResponse}";
            }
            Check("air_steer_reaches_tank_and_peers_" + jumpers, response,
                $"jumpers={jumpers} first0.50s deltaZ(plus/minus relative to neutral) tank,P1,P2,P3={string.Join(" | ", evidence)} " +
                $"commonAirTicks={commonTicks} airTicks={forward.airTicks}/{back.airTicks}/{neutral.airTicks} held={forward.held && back.held}");
        }

        static IEnumerator CheckDriveForces(M0CarryLab lab, Collider floor)
        {
            yield return Arrange(lab, 0, 0);
            Vector3[] inputs = { new Vector3(.31f, 0, .58f), new Vector3(-.47f, 0, .19f), new Vector3(.03f, 0, -.13f) };
            Vector3[] floorForces = new Vector3[3];
            lab.Tank.angularVelocity = lab.Tank.linearVelocity = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                lab.Players[i].body.linearVelocity = Vector3.zero;
                lab.SetInput(i, inputs[i], true);
            }
            bool comparable = true;
            float maxDifference = 0, minDrive = float.PositiveInfinity;
            try
            {
                for (int trial = 0; trial < 2; trial++)
                {
                    // Switch only floor support. No simulation step changes the pose or velocity between the two samples.
                    floor.enabled = trial == 0;
                    Physics.SyncTransforms();
                    var before = new Vector3[3];
                    for (int i = 0; i < 3; i++) before[i] = lab.Players[i].body.GetAccumulatedForce();
                    FixedStep.Invoke(lab, null);
                    for (int i = 0; i < 3; i++)
                    {
                        var a = lab.Players[i];
                        Vector3 force = Vector3.ProjectOnPlane(a.body.GetAccumulatedForce() - before[i], Vector3.up);
                        comparable &= lab.IsGripping(i) && a.grounded == floor.enabled;
                        minDrive = Mathf.Min(minDrive, Vector3.Dot(force, inputs[i].normalized));
                        if (trial == 0) floorForces[i] = force;
                        else maxDifference = Mathf.Max(maxDifference, Vector3.Distance(force, floorForces[i]));
                    }
                }
            }
            finally { floor.enabled = true; Physics.SyncTransforms(); }
            float cap = lab.baseline.carryMotorForce;
            bool fullAndPartial = Mathf.Abs(floorForces[0].magnitude - cap) < .02f && floorForces[2].magnitude < .80f * cap;
            Check("active_force_equal_on_ground_and_in_air", comparable && fullAndPartial && minDrive > 1f && maxDifference < .02f,
                $"groundForcesN={floorForces[0].magnitude:F2}/{floorForces[1].magnitude:F2}/{floorForces[2].magnitude:F2} limitN={cap:F2} " +
                $"matchedPoseAndVelocity={comparable} minActiveProjectionN={minDrive:F3} maxDifferenceN={maxDifference:F4}");
        }

        static void CheckCollinearTakeoff(M0CarryLab lab)
        {
            PlaceAndGrip(lab, new[] { Vector3.left, Vector3.zero, Vector3.right }, 0, 0, 0);
            // Both samples share the same level pose. Vertical leg forces on this centered X line have no X torque.
            float before = lab.Tank.GetAccumulatedTorque().x;
            FixedStep.Invoke(lab, null);
            float planted = lab.Tank.GetAccumulatedTorque().x - before;
            bool grounded = true, tookOff = true;
            for (int i = 0; i < 3; i++)
            {
                grounded &= lab.IsGripping(i) && lab.Players[i].grounded;
                lab.SetInput(i, Vector3.zero, true, jump: true);
            }
            before = lab.Tank.GetAccumulatedTorque().x;
            FixedStep.Invoke(lab, null);
            float launch = lab.Tank.GetAccumulatedTorque().x - before;
            for (int i = 0; i < 3; i++)
                tookOff &= lab.IsGripping(i) && !lab.Players[i].grounded && lab.Players[i].carryJumpAirborne;
            Check("collinear_tip_assist_stops_on_simultaneous_takeoff", grounded && tookOff && Mathf.Abs(planted) > 1f && Mathf.Abs(launch) < .05f,
                $"threePlanted={grounded} threeJumped={tookOff} torqueAboutGripLineNm planted={planted:F3} takeoff={launch:F3}");
        }

        public static void BeginStandalone(M0CarryLab lab)
        {
            Last = new Report();
            lab.StartCoroutine(Run(lab, null));
        }

        static IEnumerator Run(M0CarryLab lab, SteamCarrySession session)
        {
            lab.Testing = true;
            lab.Stretch.ManualInput = true;
            if (session) session.enabled = false;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Carry jump test floor";
            floor.transform.position = Origin + Vector3.down * .5f;
            floor.transform.localScale = new Vector3(32, 1, 32);
            try
            {
                Flight neutralOne = null;
                for (int trial = 0; trial < 3; trial++)
                {
                    var f = new Flight();
                    yield return Fly(lab, 1, f, trial == 0 ? 0 : trial == 1 ? 18 : -18, trial == 2 ? 67 : 0, retry: true);
                    bool axis = trial == 0 ? f.initialSlope < 2f : f.initialSlope > 12f;
                    Check("one_jumps_about_two_grips_" + trial, f.ready && f.held && f.landed && f.retried &&
                        f.ReachedHeight(1) && f.jumpEvents[1] == 0 && f.jumpEvents[2] == 0 && f.tilt > 5f &&
                        f.pivotDrift < .20f && f.axisError < 12f && f.supportDirectionChange < 8f && f.maxGripError < .25f && axis, f.Evidence);
                    if (trial == 0) neutralOne = f;
                }
                var two = new Flight();
                yield return Fly(lab, 2, two);
                Check("two_jump_about_one_grip", two.ready && two.held && two.landed && two.ReachedHeight(2) && two.jumpEvents[2] == 0 &&
                    two.tilt > 5f && two.pivotDrift < .20f && two.maxGripError < .25f, two.Evidence);
                var three = new Flight();
                yield return Fly(lab, 3, three);
                Check("three_jump_and_land_together", three.ready && three.held && three.landed && three.ReachedHeight(3) && three.airTicks >= 3 &&
                    three.tilt < 8f && three.maxGripError < .25f, three.Evidence);
                var offset = new Flight();
                yield return Fly(lab, 3, offset, offset: .32f);
                Check("off_center_team_reaches_three_grip_apices", offset.ready && offset.held && offset.landed && offset.ReachedHeight(3) &&
                    offset.airTicks >= 3 && offset.tilt < 8f, offset.Evidence);
                foreach (bool reverse in new[] { false, true })
                {
                    var stagger = new Flight();
                    yield return Fly(lab, 3, stagger, stagger: true, reverse: reverse);
                    Vector3 third = stagger.launchPoses[2];
                    // A handover may change angular velocity. Measure the actual pose step without assuming a dynamic body.
                    bool continuous = Array.TrueForAll(stagger.launchPoses, pose => pose.z < 9f);
                    Check("staggered_takeoff_" + (reverse ? "P3_P1_P2" : "P1_P2_P3"), stagger.ready && stagger.held && stagger.landed &&
                        stagger.plannedSupports && stagger.ReachedHeight(3) &&
                        (stagger.airborneStages & 14) == 14 && stagger.rise > .12f && stagger.firstAxisError < 12f &&
                        stagger.firstSupportsDrift < .20f && stagger.lastSupportDrift < .20f && continuous &&
                        third.x > 2f && third.y > third.x * .5f && stagger.jumpsAfterLanding == 1 && stagger.repeatRise > .04f,
                        stagger.Evidence + $" firstAxisError={stagger.firstAxisError:F2} lineDrift={stagger.firstSupportsDrift:F3} pointDrift={stagger.lastSupportDrift:F3} " +
                        $"launch[tiltBefore,tiltAfter,angularStep]={string.Join("/", Array.ConvertAll(stagger.launchPoses, v => v.ToString("F2")))} " +
                        $"jumpEventsAfterLanding={stagger.jumpsAfterLanding} repeatRise={stagger.repeatRise:F3}");
                }
                var rear = new Flight();
                yield return Fly(lab, 3, rear, lateRear: true);
                Check("wide_grips_front_pair_then_rear_reaches_own_apex", rear.ready && rear.held && rear.landed &&
                    rear.ReachedHeight(3) && rear.maxGripError < .25f, rear.Evidence);
                yield return CheckAirInput(lab, 1, neutralOne);
                yield return CheckAirInput(lab, 3, three);
                yield return CheckDriveForces(lab, floor.GetComponent<Collider>());
                CheckCollinearTakeoff(lab);
                Last.complete = true;
                Last.passed = Last.checks.TrueForAll(c => c.passed);
            }
            finally
            {
                UnityEngine.Object.Destroy(floor);
                if (session) { session.enabled = true; session.ReturnToLobby(); }
                File.WriteAllText(session ? "Logs/carry-jump-checks.json" : "Logs/carry-rollback-jumps.json", JsonUtility.ToJson(Last, true));
            }
        }
    }
}
