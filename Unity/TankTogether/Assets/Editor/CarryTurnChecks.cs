using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;

namespace TankTogether.Editor
{
    public static class CarryTurnChecks
    {
        [Serializable] public class Report
        {
            public bool complete, passed;
            public List<M0CarryChecks.Check> checks = new List<M0CarryChecks.Check>();
        }

        public static Report Last { get; private set; }
        static readonly Vector3 Origin = new Vector3(1000, 0, 0);
        static readonly WaitForFixedUpdate Tick = new WaitForFixedUpdate();

        [MenuItem("Tank Together/Tests/Check Three Player Carry Turn")]
        public static void Begin()
        {
            var session = UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
            if (!Application.isPlaying || !session || session.Mode != SteamCarrySession.EntryMode.Home ||
                !session.StartLocalStretchLevel())
                throw new InvalidOperationException("Enter Play in the formal whitebox main menu first.");
            Last = new Report();
            session.StartCoroutine(Run(session));
        }

        static void Check(string name, bool pass, string evidence)
        {
            Last.checks.Add(new M0CarryChecks.Check { name = name, passed = pass, evidence = evidence });
        }

        static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);
        static Vector3 Anchor(M0CarryLab lab, int i) => Flat(lab.Tank.position + lab.Tank.rotation * lab.Players[i].anchor);
        static Vector3 Center(M0CarryLab lab) => (Anchor(lab, 0) + Anchor(lab, 1) + Anchor(lab, 2)) / 3;
        static Vector3 Tangent(M0CarryLab lab, int i) => Vector3.Cross(Vector3.up, Anchor(lab, i) - Center(lab)).normalized;
        static Vector3 Foot(M0CarryLab lab, int i) => Flat(lab.Players[i].body.position);
        static Vector3 Heading(M0CarryLab lab) => Flat(lab.Tank.rotation * Vector3.forward).normalized;
        static bool ThreeGrips(M0CarryLab lab) => lab.IsGripping(0) && lab.IsGripping(1) && lab.IsGripping(2);

        static IEnumerator Arrange(M0CarryLab lab, Vector3[] offsets = null, float yaw = 0,
            Vector3? feet = null, int settleTicks = 35)
        {
            if (lab.Broken) lab.ResetLab();
            for (int i = 0; i < 3; i++) lab.SetInput(i, Vector3.zero, false);
            yield return Tick;
            lab.Tank.isKinematic = false;
            Vector3 floorPoint = feet ?? Origin;
            Vector3 tankPosition = floorPoint + Vector3.up * (lab.standingHeight + lab.tankSize.y / 2);
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            lab.Tank.position = tankPosition;
            lab.Tank.rotation = rotation;
            lab.Tank.transform.SetPositionAndRotation(tankPosition, rotation);
            lab.Tank.linearVelocity = lab.Tank.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            // The default triangle is offset from the tank center. P1 has the longest radius.
            for (int i = 0; i < 3; i++)
            {
                if (!lab.Players[i].body) continue;
                float x = (i == 0 ? .42f : -.12f) * lab.tankSize.x;
                float z = (i == 0 ? .075f : i == 1 ? .26f : -.11f) * lab.tankSize.z;
                var offset = offsets == null ? new Vector3(x, 0, z) : offsets[i];
                lab.PlacePlayer(i, floorPoint + rotation * offset + Vector3.up * .02f);
                lab.Players[i].body.rotation = rotation;
                lab.SetInput(i, Vector3.zero, false);
                lab.SetInput(i, Vector3.zero, true);
            }
            Physics.SyncTransforms();
            for (int i = 0; i < settleTicks; i++) yield return Tick;
        }

        static IEnumerator CheckSupportLine(M0CarryLab lab, Vector3[] wideTriangle)
        {
            var route = lab.GetComponent<WhiteboxCarryLevel>();
            if (!Physics.Raycast(route.startFeet + Vector3.up * .6f, Vector3.down, out var ground, 1.2f,
                    ~0, QueryTriggerInteraction.Ignore) || !ground.collider.transform.IsChildOf(route.levelRoot) || ground.normal.y < .99f)
                throw new InvalidOperationException("Expected the authored flat ground at the start platform.");

            string[] names = { "centered_line_touches_floor", "oblique_line_touches_floor", "line_falls_toward_right_load", "line_falls_toward_left_load", "one_carrier_touches_floor", "two_host_carriers_touch_floor", "wide_triangle_stays_level" };
            float[] onset = { -1, -1 };
            for (int trial = 0; trial < names.Length; trial++)
            {
                bool wide = trial == 6;
                int carriers = trial == 4 ? 1 : trial == 5 ? 2 : 3;
                float yaw = trial == 1 || trial == 3 ? 37f : 0f;
                float offset = trial == 2 ? -.40f : trial == 3 ? .40f : 0f;
                Vector3 side = Quaternion.Euler(0, yaw, 0) * Vector3.right;
                Vector3 middle = Vector3.right * offset;
                var points = wide ? wideTriangle : new[] { middle, middle + Vector3.forward * .9f, middle - Vector3.forward * .9f };
                if (carriers == 2) points = new[] { Vector3.forward * .9f, Vector3.back * .9f, Vector3.zero };
                if (lab.Broken) lab.ResetLab();
                if (carriers == 2)
                {
                    lab.DynamicRoster = true;
                    lab.SessionTeamSize = 2;
                    lab.ConfigureSessionRoster(new[] { true, true, false, false }, true);
                    lab.SetSessionPaused(false);
                }
                yield return Arrange(lab, points, yaw, ground.point, 0);
                for (int i = carriers; i < 3; i++)
                    if (lab.Players[i].body) lab.PlacePlayer(i, ground.point + new Vector3(4f, .02f, i - 1));
                float maxTilt = 0, across = 0, tiltTime = -1, elapsed = 0;
                bool held = true, grounded = false, touched = false;
                for (int tick = 0; tick < (wide ? 160 : 500) && !lab.Broken; tick++)
                {
                    yield return Tick;
                    elapsed += Time.fixedDeltaTime;
                    touched |= lab.TankContacts.Touches(ground.collider);
                    Vector3 normal = lab.Tank.rotation * Vector3.up;
                    float tilt = Vector3.Angle(Vector3.up, normal);
                    maxTilt = Mathf.Max(maxTilt, tilt);
                    if (!lab.Broken)
                    {
                        bool allGrounded = true;
                        for (int i = 0; i < carriers; i++)
                        {
                            held &= lab.IsGripping(i);
                            allGrounded &= lab.Players[i].grounded;
                        }
                        grounded |= allGrounded;
                    }
                    if (tiltTime < 0 && tilt >= 18f)
                    {
                        across = Vector3.Dot(Flat(normal).normalized, side);
                        tiltTime = elapsed;
                    }
                }
                lab.DynamicRoster = false;
                lab.SessionTeamSize = lab.LocalPlayerCount;
                float bottomGap = lab.Tank.GetComponent<BoxCollider>().bounds.min.y - ground.point.y;
                bool floorHit = lab.Broken && lab.LastFailureWasDanger && touched;
                bool fallsAcrossLine = tiltTime >= 0 && (carriers == 1 || Mathf.Abs(across) > .82f);
                bool heavySide = offset == 0 || across * offset < 0;
                Check(names[trial], held && grounded && (wide ? !lab.Broken && maxTilt < 7f : floorHit && fallsAcrossLine && heavySide),
                    $"mapFloor={ground.collider.name} carriers={carriers} yaw={yaw:F0} maxTiltDeg={maxTilt:F2} timeTo18Deg={tiltTime:F2} sideProjection={across:F2} elapsed={elapsed:F2} bottomGap={bottomGap:F3} floorHit={floorHit} contactRecorded={touched} heldBeforeImpact={held} grounded={grounded}");
                if (trial < 2) onset[trial] = tiltTime;
                if (carriers == 2) lab.ResetLab();
            }
            Check("line_tip_is_heading_independent", onset[0] >= 0 && onset[1] >= 0 && Mathf.Abs(onset[0] - onset[1]) < .5f,
                $"timeTo18Deg yaw0={onset[0]:F2} yaw37={onset[1]:F2}");
            route.SelectCheckpoint("start");
            lab.ResetLab();
            for (int tick = 0; tick < 100; tick++) yield return Tick;
            float pedestalTilt = Vector3.Angle(lab.Tank.rotation * Vector3.up, Vector3.up);
            float restError = Mathf.Abs(lab.Tank.position.y - lab.tankSize.y / 2 - route.checkpoints[0].tankFeet.y - route.checkpoints[0].tankRestHeight);
            Check("authored_pedestal_stays_level", !lab.Broken && pedestalTilt < 3f && restError < .05f,
                $"tiltDeg={pedestalTilt:F2} restHeightError={restError:F3}");
        }

        static IEnumerator CheckOrbit(M0CarryLab lab, string shape, Vector3[] offsets)
        {
            yield return Arrange(lab, offsets);
            var center = Center(lab);
            var radii = new float[3];
            var errors = new float[3];
            var speeds = new float[3];
            float yaw = 0;
            for (int i = 0; i < 3; i++) radii[i] = (Anchor(lab, i) - center).magnitude;
            for (int step = 0; step < 120; step++)
            {
                var heading = Heading(lab);
                lab.SetInput(0, Tangent(lab, 0), true);
                yield return Tick;
                yaw += Vector3.SignedAngle(heading, Heading(lab), Vector3.up);
                for (int i = 0; i < 3; i++)
                {
                    // Use the fixed, original centroid. Measuring from the current center would hide drift.
                    float radius = (Anchor(lab, i) - center).magnitude;
                    errors[i] = Mathf.Max(errors[i], Mathf.Abs(radius - radii[i]));
                    if (step >= 70) speeds[i] += Flat(lab.Players[i].body.linearVelocity).magnitude / 50;
                }
            }
            float grade = lab.baseline.carrySpeed; // Full walking input with all three gripping.
            bool orbit = ThreeGrips(lab) && !lab.Broken && yaw > 40 &&
                errors[0] < .16f && errors[1] < .16f && errors[2] < .16f;
            Check(shape + "_grip_orbits", orbit,
                $"yawDeg={yaw:F1} radii={radii[0]:F3},{radii[1]:F3},{radii[2]:F3} maxRadiusError={errors[0]:F3},{errors[1]:F3},{errors[2]:F3}");
            Check(shape + "_passive_speeds", speeds[1] > .05f && speeds[2] > .05f && speeds[1] < grade && speeds[2] < grade,
                $"meanMetersPerSecond P1={speeds[0]:F3} P2={speeds[1]:F3} P3={speeds[2]:F3} walkingGrade={grade:F3}");
        }

        static IEnumerator CheckPassiveBlocked(M0CarryLab lab)
        {
            // P2 is the front grip. A clockwise turn initially carries P2 along +X.
            var offsets = new[] { new Vector3(.78f, 0, -.6f), new Vector3(0, 0, 1.2f), new Vector3(-.78f, 0, -.6f) };
            const float gap = .16f, thickness = .2f;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Carry turn test wall at passive P2";
            wall.SetActive(false);
            var box = wall.GetComponent<BoxCollider>();
            var yaw = new float[2];
            var travel = new float[2];
            bool held = true;
            float penetration = 0;
            try
            {
                for (int trial = 0; trial < 2; trial++)
                {
                    yield return Arrange(lab, offsets);
                    var passive = lab.Players[1];
                    var start = Foot(lab, 1);
                    if (trial == 1)
                    {
                        wall.transform.position = start + new Vector3(passive.capsule.radius + gap + thickness / 2, .6f, 0);
                        wall.transform.localScale = new Vector3(thickness, 1.2f, 1.4f);
                        wall.SetActive(true);
                        Physics.SyncTransforms();
                    }
                    for (int step = 0; step < 100; step++)
                    {
                        var before = Heading(lab);
                        lab.SetInput(0, Tangent(lab, 0), true);
                        yield return Tick;
                        held &= ThreeGrips(lab) && !lab.Broken;
                        yaw[trial] += Vector3.SignedAngle(before, Heading(lab), Vector3.up);
                        travel[trial] = Mathf.Max(travel[trial], Foot(lab, 1).x - start.x);
                        if (trial == 1 && Physics.ComputePenetration(passive.capsule, passive.body.position, passive.body.rotation,
                            box, wall.transform.position, wall.transform.rotation, out _, out float depth))
                            penetration = Mathf.Max(penetration, depth);
                    }
                }
                Check("wall_blocks_passive_grip", held && travel[0] > gap + .2f && travel[1] >= gap - .05f &&
                    travel[1] < gap + .04f && penetration < .04f && yaw[0] > yaw[1] + 10f,
                    $"freeYawDeg={yaw[0]:F1} blockedYawDeg={yaw[1]:F1} freeTravelX={travel[0]:F3} blockedTravelX={travel[1]:F3} contactTravelX={gap:F3} maxPenetration={penetration:F4} held={held}");
            }
            finally
            {
                wall.SetActive(false);
                UnityEngine.Object.Destroy(wall);
            }
        }

        static IEnumerator CheckStraightHaul(M0CarryLab lab, Vector3[] offsets)
        {
            float[] speeds = new float[4];
            string[] names = { "one_pulls_two", "two_pull_one", "all_three_pull", "front_player_resists" };
            for (int trial = 0; trial < speeds.Length; trial++)
            {
                yield return Arrange(lab, offsets);
                var initialCenter = Center(lab);
                var initialHeading = Heading(lab);
                // P1 is on the forward radius, P2 and P3 are the symmetric rear pair.
                lab.SetInput(0, trial == 1 ? Vector3.zero : trial == 3 ? Vector3.back : Vector3.forward, true);
                for (int i = 1; i < 3; i++) lab.SetInput(i, trial == 0 ? Vector3.zero : Vector3.forward, true);
                for (int step = 0; step < 100; step++) yield return Tick;
                var center = Center(lab);
                var feet = new[] { Foot(lab, 0), Foot(lab, 1), Foot(lab, 2) };
                for (int step = 0; step < 50; step++) yield return Tick;
                float elapsed = 50 * Time.fixedDeltaTime;
                Vector3 velocity = (Center(lab) - center) / elapsed;
                speeds[trial] = velocity.z;
                float maxFootError = 0;
                for (int i = 0; i < 3; i++)
                    maxFootError = Mathf.Max(maxFootError, Mathf.Abs((Foot(lab, i).z - feet[i].z) / elapsed - velocity.z));
                Check(names[trial], ThreeGrips(lab) && !lab.Broken && Mathf.Abs(velocity.x) < .22f && maxFootError < .22f,
                    $"teamForward={velocity.z:F3} lateral={velocity.x:F3} maxFootSpeedDelta={maxFootError:F3}");
                if (trial == 2)
                {
                    float yaw = Vector3.Angle(initialHeading, Heading(lab));
                    float side = Mathf.Abs(Center(lab).x - initialCenter.x);
                    Check("three_parallel_inputs_stay_straight", yaw < 6f && side < .18f,
                        $"yawDeg={yaw:F2} totalSideTravel={side:F3} forwardTravel={Center(lab).z - initialCenter.z:F3}");
                }
            }
            float grade = lab.baseline.carrySpeed;
            Check("three_haul_speed_grades",
                Mathf.Abs(speeds[0] - grade * .32f) < grade * .11f &&
                Mathf.Abs(speeds[1] - grade * .64f) < grade * .11f &&
                Mathf.Abs(speeds[2] - grade) < grade * .13f &&
                speeds[1] > speeds[0] + grade * .17f && speeds[2] > speeds[1] + grade * .17f,
                $"metersPerSecond single={speeds[0]:F3} pair={speeds[1]:F3} three={speeds[2]:F3} grade={grade:F3}");
            Check("reverse_input_resists_haul", speeds[3] < speeds[1] - grade * .10f,
                $"twoHelping={speeds[1]:F3} thirdOpposes={speeds[3]:F3}");
        }

        static IEnumerator CheckThreeDirections(M0CarryLab lab, Vector3[] offsets)
        {
            for (int trial = 0; trial < 3; trial++)
            {
                yield return Arrange(lab, offsets);
                Vector3 start = Center(lab);
                float yaw = 0;
                for (int step = 0; step < 100; step++)
                {
                    Vector3 heading = Heading(lab);
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 arm = Anchor(lab, i) - Center(lab);
                        Vector3 tangent = Vector3.Cross(Vector3.up, arm) / .9f;
                        Vector3 input = tangent;
                        if (trial == 1) input = Vector3.forward * .5f + tangent * .45f;
                        if (trial == 2) input = arm.normalized;
                        lab.SetInput(i, input, true);
                    }
                    yield return Tick;
                    yaw += Vector3.SignedAngle(heading, Heading(lab), Vector3.up);
                }
                Vector3 travel = Center(lab) - start;
                bool held = ThreeGrips(lab) && !lab.Broken;
                string evidence = $"yawDeg={yaw:F2} forwardTravel={travel.z:F3} sideTravel={travel.x:F3}";
                if (trial == 0) Check("three_tangent_inputs_spin", held && yaw > 40f && travel.magnitude < .20f, evidence);
                if (trial == 1) Check("three_inputs_translate_and_spin", held && yaw > 15f && travel.z > .6f && Mathf.Abs(travel.x) < .30f, evidence);
                if (trial == 2) Check("three_radial_inputs_balance", held && Mathf.Abs(yaw) < 8f && travel.magnitude < .20f, evidence);
            }
        }

        static IEnumerator CheckFootResistance(M0CarryLab lab, Collider floor, Vector3[] offsets)
        {
            const float initialSpeed = 1.5f;
            var footSpeeds = new float[2];
            var tankSpeeds = new float[2];
            var grounded = new int[2];
            bool held = true;
            try
            {
                for (int trial = 0; trial < 2; trial++)
                {
                    yield return Arrange(lab, offsets);
                    floor.enabled = trial == 0;
                    Physics.SyncTransforms();
                    lab.Tank.linearVelocity = Vector3.forward * initialSpeed;
                    for (int i = 0; i < 3; i++) lab.Players[i].body.linearVelocity = Vector3.forward * initialSpeed;
                    // P1 pulls along its radius; both rear grips stay passive throughout.
                    lab.SetInput(0, Vector3.forward, true);
                    for (int step = 0; step < 18; step++)
                    {
                        yield return Tick;
                        if (step < 13) continue;
                        float meanFeet = (lab.Players[1].body.linearVelocity.z + lab.Players[2].body.linearVelocity.z) * .5f;
                        footSpeeds[trial] += meanFeet / 5;
                        tankSpeeds[trial] += lab.Tank.linearVelocity.z / 5;
                    }
                    for (int i = 0; i < 3; i++) if (lab.Players[i].grounded) grounded[trial]++;
                    held &= ThreeGrips(lab) && !lab.Broken;
                }
                Check("passive_foot_resistance_requires_ground", held && grounded[0] == 3 && grounded[1] == 0 &&
                    footSpeeds[1] > footSpeeds[0] + .15f,
                    $"initial={initialSpeed:F2} passiveFeetGround={footSpeeds[0]:F3} passiveFeetAir={footSpeeds[1]:F3} tankGround={tankSpeeds[0]:F3} tankAir={tankSpeeds[1]:F3} groundedCounts={grounded[0]},{grounded[1]}");
            }
            finally { floor.enabled = true; }
        }

        static IEnumerator CheckObliqueInputs(M0CarryLab lab)
        {
            var offsets = new[] { new Vector3(.60f, 0, .94f), new Vector3(-.83f, 0, -.34f), new Vector3(.90f, 0, -.21f) };
            float[] inputAngles = { 62, -18, -124 };
            float[] strengths = { .88f, .70f, .47f };
            Vector3[] travel = new Vector3[2];
            float[] yaw = new float[2];
            bool held = true;
            for (int trial = 0; trial < 2; trial++)
            {
                float startYaw = trial * 37;
                yield return Arrange(lab, offsets, startYaw);
                Vector3 start = Center(lab);
                Vector3 heading = Heading(lab);
                for (int i = 0; i < 3; i++)
                {
                    Vector3 input = Quaternion.Euler(0, startYaw + inputAngles[i], 0) * Vector3.forward * strengths[i];
                    lab.SetInput(i, input, true);
                }
                for (int step = 0; step < 90; step++) yield return Tick;
                travel[trial] = Center(lab) - start;
                yaw[trial] = Vector3.SignedAngle(heading, Heading(lab), Vector3.up);
                held &= ThreeGrips(lab) && !lab.Broken;
            }
            float positionError = (Quaternion.Euler(0, 37, 0) * travel[0] - travel[1]).magnitude;
            float yawError = Mathf.Abs(Mathf.DeltaAngle(yaw[0], yaw[1]));
            Check("oblique_inputs_rotate_with_formation", held && travel[0].magnitude > .4f && yaw[0] > 12f &&
                positionError < .16f && yawError < 7f,
                $"travel0={travel[0]:F3} travel37={travel[1]:F3} yaw0={yaw[0]:F2} yaw37={yaw[1]:F2} positionError={positionError:F3} yawError={yawError:F2}");
        }

        static IEnumerator Run(SteamCarrySession session)
        {
            var lab = session.lab;
            session.enabled = false;
            lab.Testing = true;
            lab.Stretch.ManualInput = true;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Carry turn test floor";
            floor.transform.position = Origin + Vector3.down * .5f;
            floor.transform.localScale = new Vector3(32, 1, 32);
            try
            {
                yield return Arrange(lab);
                var center = Center(lab);
                float offset = (center - Flat(lab.Tank.position)).magnitude;
                Check("three_off_center_grips", ThreeGrips(lab) && offset > .2f, $"centerOffset={offset:F3}");
                Vector3 direction = Tangent(lab, 0);
                lab.SetInput(0, direction, true);
                float lateYaw = 0;
                Vector3 lateStart = center;
                for (int step = 0; step < 300; step++)
                {
                    if (step == 250) lateStart = Center(lab);
                    yield return Tick;
                    if (step >= 275) lateYaw += Mathf.Abs(lab.Tank.angularVelocity.y) * Mathf.Rad2Deg / 25;
                }
                float aligned = Vector3.Angle(Anchor(lab, 0) - Center(lab), direction);
                Check("fixed_world_input_settles", aligned < 15 && lateYaw < 8 && ThreeGrips(lab),
                    $"alignmentErrorDeg={aligned:F2} finalMeanDegPerSecond={lateYaw:F2}");
                Vector3 lateVelocity = (Center(lab) - lateStart) / (50 * Time.fixedDeltaTime);
                float forward = Vector3.Dot(lateVelocity, direction);
                float lateral = (lateVelocity - forward * direction).magnitude;
                Check("fixed_world_input_turns_into_haul", !lab.Broken && ThreeGrips(lab) && forward > .45f && lateral < .22f,
                    $"forwardMeters={Vector3.Dot(Center(lab)-center, direction):F3} finalForwardSpeed={forward:F3} finalSideSpeed={lateral:F3}");

                yield return Arrange(lab);
                center = Center(lab);
                var initialFeet = new[] { Foot(lab, 0), Foot(lab, 1), Foot(lab, 2) };
                var previousFeet = (Vector3[])initialFeet.Clone();
                var distances = new float[3];
                float angle = 0, midpointError = 0;
                float drift = 0;
                for (int step = 0; step < 110; step++)
                {
                    var previousHeading = Heading(lab);
                    lab.SetInput(0, Tangent(lab, 0), true);
                    yield return Tick;
                    angle += Vector3.SignedAngle(previousHeading, Heading(lab), Vector3.up);
                    drift = Mathf.Max(drift, (Center(lab) - center).magnitude);
                    for (int i = 0; i < 3; i++)
                    {
                        var now = Foot(lab, i);
                        distances[i] += (now - previousFeet[i]).magnitude;
                        previousFeet[i] = now;
                    }
                    var passiveShift = (previousFeet[1] - initialFeet[1] + previousFeet[2] - initialFeet[2]) * .5f;
                    midpointError = Mathf.Max(midpointError, (passiveShift + (previousFeet[0] - initialFeet[0]) * .5f).magnitude);
                }
                Check("sustained_tangent_turn", angle > 45 && drift < .16f && !lab.Broken && ThreeGrips(lab),
                    $"yawDeg={angle:F1} maxXZDrift={drift:F3}");
                Check("passive_feet_follow_slower", distances[1] > .15f && distances[2] > .15f &&
                    Mathf.Max(distances[1], distances[2]) < distances[0] * .95f && midpointError < .25f,
                    $"pathP1={distances[0]:F2} P2={distances[1]:F2} P3={distances[2]:F2} midpointError={midpointError:F3}");

                lab.SetInput(0, Vector3.zero, true);
                for (int step = 0; step < 80; step++) yield return Tick;
                Check("idle_grips_stop_turn", Mathf.Abs(lab.Tank.angularVelocity.y) < .12f && ThreeGrips(lab),
                    $"yawRadiansPerSecond={lab.Tank.angularVelocity.y:F3}");

                var response = new float[2];
                for (int trial = 0; trial < 2; trial++)
                {
                    yield return Arrange(lab);
                    for (int step = 0; step < 65; step++)
                    {
                        var previousHeading = Heading(lab);
                        lab.SetInput(0, Tangent(lab, 0), true);
                        if (step >= 20) lab.SetInput(1, Tangent(lab, 1) * (trial == 0 ? 1 : -1), true);
                        yield return Tick;
                        if (step >= 20) response[trial] += Vector3.SignedAngle(previousHeading, Heading(lab), Vector3.up);
                    }
                }
                Check("passive_player_can_counter", response[0] > response[1] + 8 && ThreeGrips(lab),
                    $"aidingYawDeg={response[0]:F1} opposingYawDeg={response[1]:F1}");

                yield return Arrange(lab);
                lab.SetInput(0, Tangent(lab, 0), true);
                for (int step = 0; step < 20; step++) yield return Tick;
                lab.SetInput(2, Vector3.zero, false);
                lab.SetInput(0, Vector3.forward, true);
                lab.SetInput(1, Vector3.forward, true);
                Vector3 start = Flat(lab.Tank.position);
                for (int step = 0; step < 65; step++) yield return Tick;
                float travel = lab.Tank.position.z - start.z;
                Check("two_remaining_can_translate", !lab.Broken && lab.IsGripping(0) && lab.IsGripping(1) &&
                    !lab.IsGripping(2) && travel > .2f, $"forwardTravel={travel:F2}");

                float halfWidth = .9f * Mathf.Sqrt(3) / 2;
                Vector3[] equalRadii = { new Vector3(.2f, 0, .9f), new Vector3(.2f - halfWidth, 0, -.45f), new Vector3(.2f + halfWidth, 0, -.45f) };
                yield return CheckOrbit(lab, "equal_radii", equalRadii);
                Vector3[] shortDriverRadius = { new Vector3(.2f, 0, .42f), new Vector3(-.95f, 0, -.21f), new Vector3(1.35f, 0, -.21f) };
                yield return CheckOrbit(lab, "short_driver_radius", shortDriverRadius);
                // Check both directions early, before the same narrow support has time to topple.
                var middleGrips = new[] { Vector3.forward * .0015f, Vector3.forward * .9f, Vector3.back * .9f };
                float[] middleTravel = new float[2];
                bool centerDrive = true;
                for (int side = 0; side < 2; side++)
                {
                    yield return Arrange(lab, middleGrips, settleTicks: 2);
                    var middleStart = Center(lab);
                    centerDrive &= (Anchor(lab, 0) - middleStart).magnitude < .02f;
                    float sign = side == 0 ? 1 : -1;
                    lab.SetInput(0, Vector3.right * sign, true);
                    for (int tick = 0; tick < 25; tick++) yield return Tick;
                    middleTravel[side] = (Center(lab).x - middleStart.x) * sign;
                    centerDrive &= ThreeGrips(lab) && !lab.Broken && middleTravel[side] > .08f;
                }
                Check("near_center_grip_can_haul_sideways", centerDrive, $"firstHalfSecond right={middleTravel[0]:F3} left={middleTravel[1]:F3}");

                Vector3[] centered = { Vector3.forward * .9f, new Vector3(-halfWidth, 0, -.45f), new Vector3(halfWidth, 0, -.45f) };
                yield return CheckStraightHaul(lab, centered);
                yield return CheckThreeDirections(lab, centered);
                yield return CheckObliqueInputs(lab);
                yield return CheckFootResistance(lab, floor.GetComponent<Collider>(), centered);

                yield return CheckPassiveBlocked(lab);
                yield return CheckSupportLine(lab, centered);


                floor.SetActive(false);
                session.ReturnToLobby();
                if (!session.StartLocalStretchLevel(false)) throw new InvalidOperationException("Could not enter the B0 height sandbox.");
                lab.Testing = true;
                lab.Stretch.ManualInput = true;
                lab.SetInput(0, Vector3.zero, false);
                lab.SetInput(0, Vector3.zero, false, stretch: 1);
                int freeTicks = Mathf.CeilToInt(((lab.Stretch.maxHeight - lab.Stretch.minHeight) /
                    lab.Stretch.growRate + .5f) / Time.fixedDeltaTime);
                for (int tick = 0; tick < freeTicks; tick++) yield return Tick;
                Check("b0_free_stretch_reaches_configured_maximum", !lab.Stretch.InLevel &&
                    Mathf.Abs(lab.Players[0].height - lab.Stretch.maxHeight) < .015f,
                    $"freeHeight={lab.Players[0].height:F3} configuredMax={lab.Stretch.maxHeight:F3}");
                Last.complete = true;
                Last.passed = Last.checks.TrueForAll(c => c.passed);
            }
            finally
            {
                UnityEngine.Object.Destroy(floor);
                lab.Stretch.ManualInput = false;
                session.enabled = true;
                session.ReturnToLobby();
                File.WriteAllText("Logs/carry-turn-checks.json", JsonUtility.ToJson(Last, true));
            }
        }
    }
}
