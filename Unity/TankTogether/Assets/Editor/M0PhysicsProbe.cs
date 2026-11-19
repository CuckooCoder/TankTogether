using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TankTogether.Editor
{
    // Disposable M0 experiment, not a production controller. No runtime/package dependency.
    // Planar fixture deliberately removes gravity and pitch/roll to isolate horizontal constraints.
    public static class M0PhysicsProbe
    {
        const float Dt = 0.02f;
        const int Steps = 200;
        const float Speed = 2f, MotorGain = 110f, MaxForce = 180f, GripLimit = 0.12f;
        const string Output = "Logs/m0-physics-probe.json";

        [Serializable] public class Sample
        {
            public string name;
            public float forwardDistance, sidewaysDistance, yawDegrees, maxGripError;
            public float peakTankSpeed, peakPlayerSpeed, immediateReleaseSpeed, finalSpeed;
            public float blockedPlayerMaxZ;
            public bool finite = true;
        }
        [Serializable] public class Check
        {
            public string name, criterion;
            public bool passed;
        }
        [Serializable] public class Report
        {
            public string utc, unityVersion;
            public string scope = "Planar dynamic-body/ConfigurableJoint feasibility only; no human or network validation.";
            public string limitations = "Gravity and pitch/roll disabled; fixed initial fixture anchors are NOT gameplay grab slots. No support, input validation, hanging, parking, fracture, camera, network or latency test.";
            public float fixedStep = Dt, duration = Steps * Dt, targetSpeed = Speed;
            public float motorGain = MotorGain, maximumMotorForce = MaxForce, gripLimit = GripLimit;
            public float tankMass = 20, playerMass = 5, tankLinearDamping = 0.8f;
            public List<Sample> samples = new List<Sample>();
            public List<Check> checks = new List<Check>();
            public bool passed;
        }

        sealed class Fixture : IDisposable
        {
            public Scene scene;
            public PhysicsScene physics;
            public Rigidbody tank;
            public readonly Rigidbody[] players = new Rigidbody[4];
            public readonly ConfigurableJoint[] grips = new ConfigurableJoint[4];
            public readonly Vector3[] anchors = {
                new Vector3(-1, 0, -0.65f), new Vector3(1, 0, -0.65f),
                new Vector3(-1, 0, 0.65f), new Vector3(1, 0, 0.65f)
            };

            public Fixture(bool wall, bool mirrored)
            {
                scene = SceneManager.CreateScene("M0 temporary physics " + Guid.NewGuid().ToString("N"), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                physics = scene.GetPhysicsScene();
                tank = Body("M0 tank", Vector3.zero, new Vector3(3.5f, 0.5f, 2.3f), 20);
                tank.linearDamping = 0.8f;
                tank.angularDamping = 2f;
                tank.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
                for (int i = 0; i < 4; i++)
                {
                    if (mirrored) anchors[i].x = -anchors[i].x;
                    players[i] = Body("M0 player " + i, anchors[i], Vector3.one * 0.4f, 5);
                    players[i].constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
                    Physics.IgnoreCollision(players[i].GetComponent<Collider>(), tank.GetComponent<Collider>());
                    var joint = players[i].gameObject.AddComponent<ConfigurableJoint>();
                    joint.connectedBody = tank;
                    joint.autoConfigureConnectedAnchor = false;
                    joint.anchor = Vector3.zero;
                    joint.connectedAnchor = anchors[i];
                    joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
                    joint.linearLimit = new SoftJointLimit { limit = GripLimit, bounciness = 0 };
                    joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
                    joint.enableCollision = false;
                    joint.projectionMode = JointProjectionMode.None;
                    joint.breakForce = float.PositiveInfinity;
                    joint.breakTorque = float.PositiveInfinity;
                    grips[i] = joint;
                }
                if (wall)
                {
                    var obstacle = new GameObject("M0 player-only blocking fixture");
                    SceneManager.MoveGameObjectToScene(obstacle, scene);
                    obstacle.transform.position = new Vector3(-1, 0, 1.2f);
                    var collider = obstacle.AddComponent<BoxCollider>();
                    collider.size = new Vector3(0.9f, 2, 0.25f);
                    // Isolate character obstruction. This is not a legal final level wall.
                    Physics.IgnoreCollision(collider, tank.GetComponent<Collider>());
                }
            }

            Rigidbody Body(string name, Vector3 position, Vector3 size, float mass)
            {
                var go = new GameObject(name);
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.position = position;
                go.AddComponent<BoxCollider>().size = size;
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = mass;
                rb.useGravity = false;
                rb.solverIterations = 12;
                rb.solverVelocityIterations = 4;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                return rb;
            }

            public void Release()
            {
                for (int i = 0; i < grips.Length; i++)
                    if (grips[i]) UnityEngine.Object.DestroyImmediate(grips[i]);
            }

            public void Dispose()
            {
                if (!scene.IsValid()) return;
                foreach (var root in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
                SceneManager.UnloadSceneAsync(scene);
            }
        }

        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        static Sample Run(string name, int mode, bool mirrored = false)
        {
            using (var fixture = new Fixture(mode == 4, mirrored))
            {
                var result = new Sample { name = name, blockedPlayerMaxZ = fixture.players[2].position.z };
                bool released = false;
                for (int step = 0; step < Steps; step++)
                {
                    if (mode == 3 && step == 100)
                    {
                        result.immediateReleaseSpeed = fixture.tank.linearVelocity.magnitude;
                        fixture.Release();
                        released = true;
                    }
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 input = Vector3.forward;
                        if (mode == 1 && i == 0) input = Vector3.back;
                        if (mode == 2) input = i % 2 == 0 ? Vector3.forward : Vector3.back;
                        if (mode == 5 || released) input = Vector3.zero;
                        var velocity = fixture.players[i].linearVelocity;
                        Vector3 motor = Vector3.ClampMagnitude((input * Speed - velocity) * MotorGain, MaxForce);
                        fixture.players[i].AddForce(motor, ForceMode.Force);
                    }
                    fixture.physics.Simulate(Dt);
                    result.finite &= Finite(fixture.tank.position) && Finite(fixture.tank.linearVelocity) && Finite(fixture.tank.angularVelocity);
                    result.peakTankSpeed = Mathf.Max(result.peakTankSpeed, fixture.tank.linearVelocity.magnitude);
                    for (int i = 0; i < 4; i++)
                    {
                        result.finite &= Finite(fixture.players[i].position) && Finite(fixture.players[i].linearVelocity);
                        result.peakPlayerSpeed = Mathf.Max(result.peakPlayerSpeed, fixture.players[i].linearVelocity.magnitude);
                        if (fixture.grips[i])
                            result.maxGripError = Mathf.Max(result.maxGripError,
                                Vector3.Distance(fixture.players[i].position, fixture.tank.position + fixture.tank.rotation * fixture.anchors[i]));
                    }
                    result.blockedPlayerMaxZ = Mathf.Max(result.blockedPlayerMaxZ, fixture.players[2].position.z);
                    if (mode == 3 && step == 100)
                        result.immediateReleaseSpeed = Mathf.Min(result.immediateReleaseSpeed, fixture.tank.linearVelocity.magnitude);
                }
                result.forwardDistance = fixture.tank.position.z;
                result.sidewaysDistance = fixture.tank.position.x;
                result.yawDegrees = Mathf.DeltaAngle(0, fixture.tank.rotation.eulerAngles.y);
                result.finalSpeed = fixture.tank.linearVelocity.magnitude;
                return result;
            }
        }

        [MenuItem("Tank Together/M0/Run Planar Physics Probe")]
        public static void RunAll()
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Enter Play mode before running the isolated local PhysicsScene experiment.");
            var active = SceneManager.GetActiveScene();
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
            try
            {
                var forward = Run("four_forward", 0);
                var opposed = Run("three_forward_one_back", 1);
                var rotate = Run("opposite_sides", 2);
                var mirror = Run("opposite_sides_mirrored_positions", 2, true);
                var release = Run("release_after_two_seconds", 3);
                var wall = Run("one_player_blocked", 4);
                var idle = Run("no_input", 5);
                var repeat = Run("four_forward_repeat", 0);
                report.samples.AddRange(new[] { forward, opposed, rotate, mirror, release, wall, idle, repeat });
                void Check(string name, bool passed, string criterion) => report.checks.Add(new Check { name = name, passed = passed, criterion = criterion });
                Check("forward_motion", forward.forwardDistance > 2, "4 s displacement > 2 m, diagnostic threshold only");
                Check("minority_has_effect", Vector3.Distance(new Vector3(forward.sidewaysDistance, 0, forward.forwardDistance), new Vector3(opposed.sidewaysDistance, 0, opposed.forwardDistance)) > 0.5f,
                    "Opposing input changes final trajectory by > 0.5 m");
                Check("position_changes_turn_direction", Mathf.Abs(rotate.yawDegrees) > 10 && rotate.yawDegrees * mirror.yawDegrees < 0,
                    "Same per-player inputs with mirrored anchors produce opposite yaw, > 10 degrees");
                Check("release_preserves_inertia", release.immediateReleaseSpeed > 0.5f && release.finalSpeed < release.immediateReleaseSpeed,
                    "First released step retains > 0.5 m/s, later damping reduces speed");
                Check("blocked_player_does_not_cross_wall", wall.blockedPlayerMaxZ < 1.05f,
                    "Tracked front-left player stays behind wall front surface (z=1.075), allowing contact tolerance");
                Check("obstruction_affects_tank", wall.forwardDistance < forward.forwardDistance * 0.8f,
                    "Obstruction reduces forward travel by at least 20 percent");
                Check("idle_does_not_drift", Mathf.Abs(idle.forwardDistance) < 0.01f && Mathf.Abs(idle.sidewaysDistance) < 0.01f, "Idle planar fixture drift < 1 cm");
                Check("repeatability", Mathf.Abs(forward.forwardDistance - repeat.forwardDistance) < 0.01f, "Same-process repeated forward run differs < 1 cm; not cross-machine determinism");
                foreach (var sample in report.samples)
                    Check(sample.name + "_bounded", sample.finite && sample.maxGripError < 0.3f && sample.peakTankSpeed < 6 && sample.peakPlayerSpeed < 8,
                        "Finite state; grip error < 0.3 m; tank speed < 6 m/s; proxy speed < 8 m/s. Experiment limits, not gameplay acceptance.");
                report.passed = report.checks.TrueForAll(check => check.passed);
                Directory.CreateDirectory("Logs");
                File.WriteAllText(Output, JsonUtility.ToJson(report, true));
                Debug.Log($"[M0] Planar probe: {report.checks.FindAll(c => c.passed).Count}/{report.checks.Count}; report {Output}. NOT M1/M2 acceptance.");
            }
            finally
            {
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
        }
    }
}
