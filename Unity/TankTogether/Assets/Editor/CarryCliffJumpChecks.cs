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
    public static class CarryCliffJumpChecks
    {
        [Serializable] public class Crossing
        {
            public int gap, jumpRequestMask, jumpEventMask, airborneMask, farContactMask;
            public bool success, broken, held = true;
            public string result;
            public string[] finalGround = new string[3];
            public Vector3[] finalFeet = new Vector3[3];
            public int groundedOnRequest, sequenceCount;
            public int[] requests = { -1, -1, -1 }, accepted = { -1, -1, -1 }, sequence = new int[3];
            public float[] requestEdge = new float[3], acceptedEdge = new float[3];
            public float[] takeoffGripY = new float[3], takeoffHeadY = new float[3], peakGripRise = new float[3], peakHeadRise = new float[3];
            public int[] firstLanding = { -1, -1, -1 };
            public float firstPressLead, firstPressSpeed, maxTilt, maxGripError, tankRearX, rearBeyondBank, launchPhase, launchFeetY;
        }
        [Serializable] public class Attempt
        {
            public string strategy;
            public int firstGap, delayTicks;
            public float lead, supportSpacing, waitPhase, formationYaw, sideGripStagger;
            public bool prepared, frontSingle, brokenAtStart;
            public int readyGroundedMask, readyJointMask, readyContactMask;
            public float setupPhase, readyPhase, readyTankTop;
            public string preparationReason;
            public string[] supportingColliders = new string[3];
            public Vector3[] startingFeet = new Vector3[3], startingGrips = new Vector3[3], localGrips = new Vector3[3];
            public float[] sideLengths = new float[3];
            public float[] edgeDistances = new float[3];
            public List<Crossing> crossings = new List<Crossing>();
        }
        [Serializable] public class Report
        {
            public bool complete, passed, wideStance;
            public string scene, baseline, diagnostic, screenshotStrategy, edgePhoto;
            public Vector3 movingNear, movingFar;
            public float laneZ, fixedDeltaTime;
            public float[] near = new float[2], far = new float[2];
            public string[] banks = new string[2];
            public List<Attempt> attempts = new List<Attempt>();
        }
        public static Report Last { get; private set; }
        const float LaneZ = -5.45f;
        static string observedImpact;
        static readonly WaitForFixedUpdate Tick = new WaitForFixedUpdate();
        static readonly string[] Strategies = { "together", "front_pair_then_rear", "front_to_rear", "rear_to_front", "rear_then_front_pair" };
        static readonly Vector3 Diagonal = new Vector3(-1, 0, 1).normalized;
        static readonly float[] Side = { -.53f, .53f, 0 };

        [MenuItem("Tank Together/Tests/Check Formal Cliff Jumps")]
        public static void Begin() => Start(false);
        [MenuItem("Tank Together/Tests/Check Formal Cliff Jumps With Wide Grips")]
        public static void BeginWide() => Start(true);
        static void Start(bool wide)
        {
            var session = UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
            if (!Application.isPlaying || !session || session.Mode != SteamCarrySession.EntryMode.Home || !session.StartLocalStretchLevel())
                throw new InvalidOperationException("Start Play in the formal whitebox main menu.");
            Last = new Report { scene = session.gameObject.scene.path, baseline = session.lab.baseline.Signature, laneZ = LaneZ,
                fixedDeltaTime = Time.fixedDeltaTime, wideStance = wide };
            Save();
            session.StartCoroutine(Run(session));
        }
        static void Save() => File.WriteAllText(Last.wideStance ? "Logs/carry-cliff-wide-checks.json" : "Logs/carry-cliff-jump-checks.json", JsonUtility.ToJson(Last, true));

        static bool Surface(Transform terrain, Vector3 top, float length, out RaycastHit surface)
        {
            surface = default;
            bool found = false;
            foreach (var hit in Physics.RaycastAll(top, Vector3.down, length, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(terrain) || hit.normal.y < .75f || found && hit.distance >= surface.distance) continue;
                surface = hit;
                found = true;
            }
            return found;
        }

        static bool FindBanks(Transform terrain, Collider[] banks)
        {
            float[] starts = { -22f, -35f };
            for (int g = 0; g < 2; g++)
            {
                bool gap = false;
                for (int i = 0; i < 330; i++)
                {
                    float x = starts[g] - i * .025f;
                    bool solid = Surface(terrain, new Vector3(x, .5f, LaneZ), 1.45f, out var hit);
                    if (!gap && !solid) { Last.near[g] = x + .0125f; gap = true; }
                    if (gap && solid)
                    {
                        Last.far[g] = x + .0125f;
                        banks[g] = hit.collider;
                        Last.banks[g] = hit.collider.name;
                        break;
                    }
                }
                if (!banks[g] || Last.near[g] - Last.far[g] < 1.5f || Last.near[g] - Last.far[g] > 3f) return false;
            }
            var red = terrain.Find("Group 73");
            var blue = terrain.Find("Group 69");
            if (!red || !blue) return false;
            var fromDeck = red.GetComponent<Collider>();
            banks[2] = blue.GetComponent<Collider>();
            Vector3 center = new Vector3(-245.354f, 49f, 126.56f);
            bool leftDeck = false;
            for (int i = 0; i < 180; i++)
            {
                Vector3 p = center + Diagonal * (-1f + i * .025f);
                var ray = new Ray(p + Vector3.up * 5, Vector3.down);
                if (!leftDeck && !fromDeck.Raycast(ray, out _, 9f)) { Last.movingNear = p - Diagonal * .0125f; leftDeck = true; }
                if (!leftDeck || !banks[2].Raycast(ray, out var landing, 9f)) continue;
                Last.movingFar = landing.point - Diagonal * .0125f;
                return Vector3.Dot(Last.movingFar - Last.movingNear, Diagonal) > 1.5f;
            }
            return false;
        }

        internal static byte[] CameraFrame(M0CarryLab lab, Vector3 direction)
        {
            var cam = Camera.main;
            if (!cam) return null;
            var old = cam.targetTexture;
            var oldPose = cam.transform.position;
            var oldRotation = cam.transform.rotation;
            var active = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                Vector3 focus = lab.Tank.position + Vector3.down * .45f;
                Vector3 chosen = focus;
                float best = float.NegativeInfinity;
                for (int view = 0; view < 24; view++)
                {
                    Vector3 radial = Quaternion.AngleAxis((view%12)*30f + 15f, Vector3.up) * direction;
                    Vector3 candidate = focus + radial*7.2f + Vector3.up*(view<12 ? 3.8f : 6f);
                    if (Array.Exists(Physics.OverlapSphere(candidate, .18f), lab.IsEnvironment)) continue;
                    float score = 0, separation = float.PositiveInfinity;
                    for (int i = 0; i < 3; i++)
                    {
                        var actor = lab.Players[i];
                        foreach (float fraction in new[] { .1f, .5f, .92f })
                        {
                            Vector3 target = actor.body.position + Vector3.up * (actor.height*fraction);
                            Vector3 line = target - candidate;
                            bool hidden = Array.Exists(Physics.RaycastAll(candidate, line.normalized, line.magnitude-.04f,
                                ~0, QueryTriggerInteraction.Ignore), hit => lab.IsEnvironment(hit.collider));
                            if (!hidden) score += 10f;
                        }
                        for (int j = 0; j < i; j++)
                            separation = Mathf.Min(separation, Vector3.ProjectOnPlane(actor.body.position-lab.Players[j].body.position,
                                (candidate-focus).normalized).magnitude);
                    }
                    score += separation;
                    if (score > best) { best = score; chosen = candidate; }
                }
                cam.transform.position = chosen;
                cam.transform.LookAt(focus);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                image.Apply();
                return image.EncodeToPNG();
            }
            finally
            {
                cam.transform.SetPositionAndRotation(oldPose, oldRotation);
                cam.targetTexture = old; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.Destroy(image);
            }
        }

        static float FootEdge(Collider deck, Vector3 foot, Vector3 near, Vector3 direction)
        {
            float distance = Vector3.Dot(near-foot, direction);
            if (Mathf.Abs(distance) > 1.5f) return distance;
            Vector3 line = foot + direction * distance + Vector3.up;
            bool HasDeck(float along) => deck.Raycast(new Ray(line + direction * along, Vector3.down), out var hit, 1.40f) && hit.normal.y > .75f;
            float inside = -.7f, outside = .7f;
            if (!HasDeck(inside) || HasDeck(outside)) return 99f;
            for (int i = 0; i < 7; i++)
            {
                float mid = (inside + outside) * .5f;
                if (HasDeck(mid)) inside = mid; else outside = mid;
            }
            return distance + (inside+outside)*.5f;
        }

        static IEnumerator TryCross(M0CarryLab lab, Transform terrain, Collider[] banks, Attempt attempt, int strategy)
        {
            lab.ResetLab();
            observedImpact = null;
            var probes = new CheckpointContactProbe[3];
            var joints = new ConfigurableJoint[3];
            var anchors = new Vector3[3];
            bool moving = attempt.firstGap == 2;
            var motion = lab.GetComponent<WhiteboxMovingBlocks>();
            if (moving)
            {
                // Leave the team at its authored checkpoint until the red deck descends clear of the overhead beams.
                while (motion.NormalizedPhase < .42f && !lab.Broken) yield return Tick;
                attempt.setupPhase = motion.NormalizedPhase;
            }
            Vector3 direction = moving ? Diagonal : Vector3.left;
            Vector3 side = Vector3.Cross(Vector3.up, direction);
            Vector3 start = moving ? Last.movingNear - direction * 4.0f + side * 2.25f :
                new Vector3(Last.near[attempt.firstGap] + (attempt.firstGap == 0 ? 5.5f : 4.3f), 0, Last.wideStance ? -3.88f : LaneZ);
            RaycastHit ground;
            if (moving)
            {
                if (!terrain.Find("Group 73").GetComponent<Collider>().Raycast(new Ray(start + Vector3.up * 5, Vector3.down), out ground, 9f))
                { attempt.preparationReason = "No original Group 73 floor under setup."; yield break; }
            }
            else if (!Surface(terrain, start + Vector3.up, 2f, out ground)) { attempt.preparationReason = "No original bridge below setup."; yield break; }
            start.y = ground.point.y;
            var position = start + Vector3.up * (lab.standingHeight + lab.tankSize.y / 2 + .005f);
            var rotation = Quaternion.AngleAxis(attempt.formationYaw + (attempt.frontSingle ? 180 : 0), Vector3.up) * Quaternion.LookRotation(direction);
            lab.Tank.position = position;
            lab.Tank.rotation = rotation;
            lab.Tank.transform.SetPositionAndRotation(position, rotation);
            for (int i = 0; i < 3; i++)
            {
                float front = i == 2 ? -attempt.supportSpacing : attempt.supportSpacing * .5f;
                Vector3 offset = Last.wideStance ? lab.CarryFormationOffset(i) : new Vector3(Side[i], 0, front);
                if (i < 2) offset.z += (i == 0 ? 1f : -1f) * attempt.sideGripStagger;
                Vector3 feet = start + rotation * offset + Vector3.up * .005f;
                lab.PlacePlayer(i, feet);
                lab.Players[i].body.transform.position = feet;
                probes[i] = lab.Players[i].body.gameObject.AddComponent<CheckpointContactProbe>();
                foreach (var bank in banks) probes[i].RegisterDanger(bank);
                probes[i].RegisterDanger(ground.collider);
            }
            Physics.SyncTransforms();
            for (int i = 0; i < 3; i++)
            {
                lab.SetInput(i, Vector3.zero, false);
                lab.SetInput(i, Vector3.zero, true);
                joints[i] = lab.Players[i].joint;
                anchors[i] = lab.Players[i].anchor;
            }
            lab.SetExternalView(0, moving ? -34f : -69f, 25f);
            for (int i = 0; i < 30; i++) yield return Tick;
            if (moving) while (motion.NormalizedPhase < attempt.waitPhase && !lab.Broken) yield return Tick;
            attempt.prepared = !lab.Broken;
            attempt.brokenAtStart = lab.Broken;
            attempt.readyPhase = moving ? motion.NormalizedPhase : 0;
            attempt.readyTankTop = lab.Tank.GetComponent<BoxCollider>().bounds.max.y;
            var rank = new int[] { 0, 1, 2 };
            for (int i = 0; i < 3; i++)
            {
                attempt.prepared &= lab.IsGripping(i) && lab.Players[i].grounded;
                attempt.startingFeet[i] = lab.Players[i].body.position;
                attempt.localGrips[i] = lab.Players[i].anchor;
                attempt.startingGrips[i] = lab.Tank.position + lab.Tank.rotation * lab.Players[i].anchor;
                if (lab.IsGripping(i)) attempt.readyJointMask |= 1 << i;
                if (lab.Players[i].grounded) attempt.readyGroundedMask |= 1 << i;
                probes[i].ReadContacts();
                if (Surface(terrain, attempt.startingFeet[i] + Vector3.up * .07f, .16f, out var supporting))
                {
                    attempt.supportingColliders[i] = supporting.collider.name;
                    if (probes[i].Danger && supporting.collider == ground.collider) attempt.readyContactMask |= 1 << i;
                }
                var local = attempt.localGrips[i];
                attempt.edgeDistances[i] = Mathf.Min(lab.tankSize.x / 2 - Mathf.Abs(local.x), lab.tankSize.z / 2 - Mathf.Abs(local.z));
                if (Last.wideStance) attempt.prepared &= attempt.edgeDistances[i] > .15f && attempt.edgeDistances[i] <= .35f;
            }
            Array.Sort(rank, (i,j) => Vector3.Dot(attempt.startingFeet[j] - attempt.startingFeet[i], direction).CompareTo(0f));
            for (int i = 0; i < 3; i++)
            {
                attempt.sideLengths[i] = Vector3.Distance(attempt.startingGrips[i], attempt.startingGrips[(i+1)%3]);
                if (Last.wideStance) attempt.prepared &= attempt.sideLengths[i] > 2.2f;
            }
            attempt.prepared &= attempt.readyContactMask == 7;
            attempt.preparationReason = attempt.prepared ? "Ready on authored ground" : (observedImpact ?? lab.Message);
            if (!attempt.prepared) yield break;
            if (Last.wideStance && Last.edgePhoto == null)
            {
                var edgePhoto = CameraFrame(lab, direction);
                if (edgePhoto != null)
                {
                    Last.edgePhoto = "Logs/carry-cliff-edge-grips.png";
                    File.WriteAllBytes(Last.edgePhoto, edgePhoto);
                }
            }
            var box = lab.Tank.GetComponent<BoxCollider>();
            for (int g = attempt.firstGap; g < (moving ? 3 : 2); g++)
            {
                Vector3 near = moving ? Last.movingNear : new Vector3(Last.near[g], start.y, LaneZ);
                Vector3 far = moving ? Last.movingFar : new Vector3(Last.far[g], start.y, LaneZ);
                var c = new Crossing { gap = g + 1 };
                attempt.crossings.Add(c);
                int pressed = -1, bankTicks = 0;
                int currentStep = 0, lastPulse = -1;
                var jumpY = new float[3];
                var edge = new float[3];
                var eligible = new bool[3];
                byte[] airFrame = null;
                void Action(int slot, string action)
                {
                    if (action != "jump" || slot < 0 || slot >= 3) return;
                    c.jumpEventMask |= 1 << slot;
                    c.accepted[slot] = currentStep;
                    c.acceptedEdge[slot] = edge[slot];
                    c.takeoffGripY[slot] = (lab.Tank.position + lab.Tank.rotation * anchors[slot]).y;
                    c.takeoffHeadY[slot] = lab.Head(slot).y;
                    if (c.sequenceCount < 3) c.sequence[c.sequenceCount++] = slot+1;
                }
                lab.PlayerAction += Action;
                try
                {
                    for (int step = 0; step < 300; step++)
                    {
                        currentStep = step;
                        float front = float.NegativeInfinity;
                        for (int i = 0; i < 3; i++)
                        {
                            var a = lab.Players[i];
                            edge[i] = FootEdge(ground.collider, a.body.position, near, direction);
                            front = Mathf.Max(front, Vector3.Dot(a.body.position - near, direction));
                            probes[i].ReadContacts();
                            eligible[i] = a.grounded && probes[i].Danger && edge[i] <= Mathf.Clamp(attempt.lead, .03f, .18f) && edge[i] >= -.15f;
                        }
                        if (pressed < 0 && front >= -attempt.lead)
                        {
                            pressed = step;
                            c.firstPressLead = -front;
                            c.firstPressSpeed = Vector3.Dot(lab.Tank.linearVelocity, direction);
                            c.launchPhase = moving ? motion.NormalizedPhase : 0;
                            c.launchFeetY = lab.Players[0].body.position.y;
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            int place = Array.IndexOf(rank, i);
                            int order = strategy == 0 ? 0 : strategy == 1 ? (place == 2 ? 1 : 0) : strategy == 2 ? place :
                                strategy == 3 ? 2-place : place == (attempt.frontSingle ? 0 : 2) ? 0 : 1;
                            bool jump = pressed >= 0 && step - pressed == order * attempt.delayTicks;
                            if (Last.wideStance && strategy != 0)
                            {
                                jump = c.requests[i] < 0 && eligible[i];
                                if (strategy == 2 || strategy == 3)
                                    jump &= step > lastPulse && (place == 0 || c.accepted[rank[place-1]] >= 0);
                                else if (strategy == 1 && place < 2) jump &= eligible[rank[0]] && eligible[rank[1]];
                                else if (strategy == 1) jump &= c.accepted[rank[0]] >= 0 && c.accepted[rank[1]] >= 0;
                                else if (strategy == 4 && place > 0) jump &= eligible[rank[1]] && eligible[rank[2]] && c.accepted[rank[0]] >= 0;
                            }
                            Vector3 run = direction;
                            if (Last.wideStance && g == 1)
                            {
                                // Overlapping portions of the original banks are too narrow for the wide triangle.
                                // Start on the middle island and steer diagonally out to the left-hand landing strip.
                                float targetZ = Mathf.Lerp(-3.88f, -6.25f, Mathf.InverseLerp(Last.near[g]+2.2f, Last.far[g]-.5f, lab.Tank.position.x));
                                run = (direction + Vector3.forward * Mathf.Clamp((targetZ - lab.Tank.position.z) * 1.1f, -.60f, .60f)).normalized;
                            }
                            lab.SetInput(i, run, true, jump: jump, sprint: true);
                            if (jump)
                            {
                                c.jumpRequestMask |= 1 << i; jumpY[i] = lab.Players[i].body.position.y;
                                c.requests[i] = step; c.requestEdge[i] = edge[i]; lastPulse = step;
                                if (lab.Players[i].grounded) c.groundedOnRequest |= 1 << i;
                            }
                        }
                        yield return Tick;
                        c.maxTilt = Mathf.Max(c.maxTilt, Vector3.Angle(lab.Tank.rotation * Vector3.up, Vector3.up));
                        int onBank = 0;
                        bool fell = false;
                        for (int i = 0; i < 3; i++)
                        {
                            var a = lab.Players[i];
                            Vector3 foot = a.body.position;
                            c.finalFeet[i] = foot;
                            c.held &= lab.IsGripping(i) && a.joint == joints[i] && a.joint.connectedBody == lab.Tank &&
                                (a.joint.connectedAnchor - anchors[i]).sqrMagnitude < .000001f;
                            var grip = lab.Tank.position + lab.Tank.rotation * anchors[i];
                            var head = lab.Head(i);
                            c.maxGripError = Mathf.Max(c.maxGripError, Vector3.Distance(head, grip));
                            if ((c.jumpEventMask & (1 << i)) != 0 && !a.grounded && foot.y > jumpY[i] + .05f) c.airborneMask |= 1 << i;
                            if (c.accepted[i] >= 0 && c.firstLanding[i] < 0)
                            {
                                c.peakGripRise[i] = Mathf.Max(c.peakGripRise[i], grip.y - c.takeoffGripY[i]);
                                c.peakHeadRise[i] = Mathf.Max(c.peakHeadRise[i], head.y - c.takeoffHeadY[i]);
                                if ((c.airborneMask & (1 << i)) != 0 && a.grounded) c.firstLanding[i] = step;
                            }
                            fell |= foot.y < ground.point.y - (moving ? 4f : 2f);
                            probes[i].ReadContacts();
                            if (Vector3.Dot(foot - far, direction) > .08f && a.grounded && probes[i].Danger &&
                                Surface(terrain, foot + Vector3.up * .06f, .14f, out var hit) && hit.collider == banks[g])
                            {
                                onBank |= 1 << i;
                                c.finalGround[i] = hit.collider.name;
                            }
                        }
                        c.farContactMask |= onBank;
                        c.tankRearX = box.bounds.max.x;
                        var local = Quaternion.Inverse(lab.Tank.rotation) * direction;
                        c.rearBeyondBank = Vector3.Dot(lab.Tank.position - far, direction) -
                            Vector3.Dot(new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z)), box.size) / 2;
                        bankTicks = onBank == 7 && c.rearBeyondBank > .04f ? bankTicks + 1 : 0;
                        bool photograph = moving && (Last.screenshotStrategy == null || strategy == 2 && Last.screenshotStrategy != Strategies[2]);
                        if (photograph && airFrame == null && pressed >= 0 && step - pressed >= 15 &&
                            c.airborneMask == 7 && onBank == 0) airFrame = CameraFrame(lab, direction);
                        if (bankTicks >= 5 && c.held && c.jumpEventMask == 7 && c.airborneMask == 7)
                        {
                            c.success = true; c.result = "landed";
                            if (airFrame != null)
                            {
                                var shoreFrame = CameraFrame(lab, direction);
                                if (shoreFrame != null)
                                {
                                    string prefix = Last.wideStance ? "Logs/carry-cliff-wide-" : "Logs/carry-cliff-";
                                    File.WriteAllBytes(prefix + "air.png", airFrame);
                                    File.WriteAllBytes(prefix + "land.png", shoreFrame);
                                    Last.screenshotStrategy = Strategies[strategy];
                                }
                            }
                            break;
                        }
                        if (lab.Broken || !c.held || fell) { c.result = lab.Broken ? observedImpact ?? lab.Message : fell ? "feet fell below bank" : "lost original joint"; break; }
                        if (Vector3.Dot(lab.Tank.position - far, direction) > 5f) { c.result = "passed landing zone without verified landing"; break; }
                    }
                    c.broken = lab.Broken;
                    if (c.result == null) c.result = "timed out";
                }
                finally { lab.PlayerAction -= Action; }
                Save();
                if (!c.success) yield break;
            }
        }

        static IEnumerator Run(SteamCarrySession session)
        {
            var lab = session.lab;
            session.enabled = false;
            lab.Testing = true;
            lab.Stretch.ManualInput = true;
            void TrackImpact(string message, string stack, LogType type)
            {
                const string prefix = "[M0] Tank failure: ";
                if (message.StartsWith(prefix, StringComparison.Ordinal)) observedImpact = message.Substring(prefix.Length);
            }
            Application.logMessageReceived += TrackImpact;
            try
            {
                var terrain = lab.GetComponent<WhiteboxCarryLevel>().levelRoot;
                var banks = new Collider[3];
                if (!FindBanks(terrain, banks)) { Last.diagnostic = "Could not resolve the authored banks on the bridge and moving decks."; yield break; }
                int strategiesCrossed = 0, movingCrossed = 0;
                for (int strategy = 0; strategy < Strategies.Length; strategy++)
                {
                    for (int variant = 0; variant < 8; variant++)
                    {
                        var trial = new Attempt {
                            strategy = Strategies[strategy], firstGap = 2, supportSpacing = Last.wideStance ? lab.tankSize.z / 2 - .25f : variant % 3 == 2 ? .9f : .36f,
                            sideGripStagger = Last.wideStance && (strategy == 2 || strategy == 3) ? (variant%2 == 0 ? .45f : -.45f) : 0,
                            frontSingle = Last.wideStance && (strategy == 3 || strategy == 4),
                            lead = new[] { .10f, .20f, .24f, -.01f, .33f, .10f, .20f, .04f }[variant],
                            delayTicks = strategy == 0 ? 0 : new[] { 3, 2, 2, 1, 4, 5, 6, 1 }[variant],
                            waitPhase = variant < 4 ? .64f : .70f
                        };
                        Last.attempts.Add(trial);
                        yield return TryCross(lab, terrain, banks, trial, strategy);
                        Save();
                        if (trial.crossings.Exists(c => c.success)) { movingCrossed++; break; }
                    }
                    bool first = false, second = false;
                    // Try both gaps in one uninterrupted run first. Later trials may start on the original middle island.
                    for (int variant = 0; variant < 8 && !(first && second); variant++)
                    {
                        int firstGap = variant >= 4 && !second ? 1 : 0;
                        float[] leads = strategy == 0 ? new[] { .08f, .18f, -.03f, .29f } : new[] { .14f, .28f, .04f, .38f };
                        int delay = strategy == 0 ? 0 : variant % 4 == 1 || variant % 4 == 3 ? 3 : 2;
                        var attempt = new Attempt { strategy = Strategies[strategy], firstGap = firstGap, lead = leads[variant % 4],
                            delayTicks = delay, supportSpacing = Last.wideStance ? lab.tankSize.z / 2 - .25f : variant % 4 >= 2 ? .9f : .36f,
                            formationYaw = Last.wideStance && (strategy == 2 || strategy == 3) ? (variant%2 == 0 ? 12f : -16f) : 0,
                            frontSingle = Last.wideStance && (strategy == 3 || strategy == 4) };
                        Last.attempts.Add(attempt);
                        yield return TryCross(lab, terrain, banks, attempt, strategy);
                        foreach (var c in attempt.crossings)
                        {
                            if (c.gap == 1) first |= c.success;
                            if (c.gap == 2) second |= c.success;
                        }
                        Save();
                    }
                    if (first && second) strategiesCrossed++;
                }
                Last.passed = movingCrossed >= 3;
                Last.diagnostic = $"strategies crossing post-pickup moving cliff={movingCrossed}/{Strategies.Length}; both opening bridge gaps={strategiesCrossed}/{Strategies.Length}";
            }
            finally
            {
                Last.complete = true;
                Save();
                Application.logMessageReceived -= TrackImpact;
                session.enabled = true;
                session.ReturnToLobby();
            }
        }
    }
}
