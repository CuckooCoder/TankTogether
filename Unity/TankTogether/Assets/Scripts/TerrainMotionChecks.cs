#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;

namespace TankTogether.Editor
{
    public sealed class TerrainMotionChecks : MonoBehaviour
    {
        const string MenuPath = "Tank Together/Tests/Live Terrain Movement";
        const string ReportPath = "Logs/terrain-movement-live.json";
        const float DriveSeconds = 1.6f, BrakeSeconds = 1.5f;
        static readonly WaitForFixedUpdate Tick = new WaitForFixedUpdate();
        static readonly Vector3 Origin = new Vector3(1200, 0, 0);

        readonly struct Trial
        {
            public readonly int grade, drivers;
            public readonly bool sprint, transition;
            public Trial(int grade, int drivers, bool sprint = false, bool transition = false)
            {
                this.grade = grade; this.drivers = drivers; this.sprint = sprint; this.transition = transition;
            }
            public string Label => (grade == 0 ? "平地" : (grade > 0 ? "上坡 " : "下坡 ") + Mathf.Abs(grade) + "°") +
                (transition ? " · 平地接坡再回平地" : "") + " · " +
                (drivers == 0 ? "空手" : drivers == 1 ? "三人抓缸 / 一人出力" : "三人抓缸 / 同向") + (sprint ? "跑" : "走");
        }

        [Serializable] sealed class Sample
        {
            public string label, message;
            public int grade, drivers, initialGrips, minGrips, groundSamples, samples;
            public bool sprint, transition, valid, clearedRamp, broken;
            public float driveSeconds, distanceX, heightChange, firstSpeedX, peakSpeedX, endSpeedX,
                brakeDistanceX, finalSpeedX, settledSeconds = -1, tankTiltChange;
        }

        [Serializable] sealed class Report
        {
            public string baseline, scene;
            public float fixedDeltaTime, brakeSeconds;
            public bool complete;
            public List<Sample> cases = new List<Sample>();
        }

        readonly List<Trial> trials = new List<Trial>();
        M0CarryLab lab;
        Report report;
        Coroutine run;
        GameObject floor;
        Material surface, markings;
        Mesh mesh;
        GUIStyle display, title;
        Font font;
        int current;
        string phase = "准备";
        float speed, distance;
        bool live;
        static TerrainMotionChecks active;

        [MenuItem(MenuPath)]
        public static void Begin()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("请在正式白盒场景进入 Play Mode 后运行。");
            if (active) { active.StartRun(0, true); return; }
            var session = FindAnyObjectByType<SteamCarrySession>();
            if (!session || session.LevelId != "formal-whitebox-v1" || !session.CanChooseMode ||
                (session.Mode != SteamCarrySession.EntryMode.LocalTest && !session.StartLocalStretchLevel()))
                throw new InvalidOperationException("请从正式白盒主菜单运行地形测试。");
            var driver = new GameObject("Live Terrain Movement").AddComponent<TerrainMotionChecks>();
            active = driver;
            driver.Setup(session.lab);
            EditorWindow.GetWindow(typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView")).Focus();
        }

        void Setup(M0CarryLab owner)
        {
            lab = owner;
            lab.Testing = true;
            foreach (int grade in new[] { 0, 15, -15, 28, -28, 32, -32 })
            {
                trials.Add(new Trial(grade, 0));
                trials.Add(new Trial(grade, 3));
            }
            foreach (int grade in new[] { 32, -32 })
            {
                trials.Add(new Trial(grade, 0, sprint: true));
                trials.Add(new Trial(grade, 3, sprint: true));
                trials.Add(new Trial(grade, 1));
            }
            foreach (int grade in new[] { 28, -28 })
            {
                trials.Add(new Trial(grade, 0, transition: true));
                trials.Add(new Trial(grade, 3, transition: true));
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            surface = new Material(shader) { color = new Color(.16f, .26f, .24f) };
            markings = new Material(shader) { color = new Color(.38f, .53f, .47f) };
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 18);
            StartRun(0, true);
        }

        void StartRun(int index, bool all = false)
        {
            if (run != null) StopCoroutine(run);
            if (all) report = new Report
            {
                baseline = lab.baseline.version, scene = gameObject.scene.name,
                fixedDeltaTime = Time.fixedDeltaTime, brakeSeconds = BrakeSeconds
            };
            run = StartCoroutine(Run(index, all));
        }

        IEnumerator Run(int index, bool all)
        {
            for (current = index; current < (all ? trials.Count : index + 1); current++)
            {
                Trial trial = trials[current];
                yield return Measure(trial, result => {
                    if (!all) return;
                    report.cases.Add(result);
                    WriteReport();
                });
            }
            current--;
            if (all) { report.complete = true; WriteReport(); }
            phase = all ? "整组完成 · 可重放任一项" : "本项完成";
            live = false;
            run = null;
        }

        void WriteReport()
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        }

        float TerrainY(Trial trial, float x) =>
            Mathf.Tan(trial.grade * Mathf.Deg2Rad) * (trial.transition ? Mathf.Clamp(x, 0, 7) : x);

        void BuildFloor(Trial trial)
        {
            if (floor) { floor.SetActive(false); Destroy(floor); }
            if (mesh) Destroy(mesh);
            floor = new GameObject("Terrain test lane");
            floor.transform.SetParent(transform);
            floor.transform.position = Origin;
            mesh = new Mesh { name = "Terrain test surface" };
            float[] sections = trial.transition ? new[] { -23f, 0, 7, 34 } : new[] { -23f, 34 };
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < sections.Length; i++)
            {
                float x = sections[i], y = TerrainY(trial, x);
                vertices.Add(new Vector3(x, y, -7));
                vertices.Add(new Vector3(x, y, 7));
                if (i == 0) continue;
                int j = i * 2;
                triangles.AddRange(new[] { j - 2, j - 1, j, j, j - 1, j + 1 });
            }
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals();
            floor.AddComponent<MeshFilter>().sharedMesh = mesh;
            floor.AddComponent<MeshRenderer>().sharedMaterial = surface;
            floor.AddComponent<MeshCollider>().sharedMesh = mesh;
            for (int x = -6; x <= 26; x += 2)
            {
                var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.transform.SetParent(floor.transform, false);
                stripe.transform.localPosition = new Vector3(x, TerrainY(trial, x) + .018f, 0);
                stripe.transform.localRotation = Quaternion.AngleAxis(trial.transition && (x <= 0 || x >= 7) ? 0 : trial.grade, Vector3.forward);
                stripe.transform.localScale = new Vector3(.045f, .02f, 13f);
                stripe.GetComponent<MeshRenderer>().sharedMaterial = markings;
                stripe.GetComponent<Collider>().enabled = false;
            }
        }

        void Place(Trial trial)
        {
            float start = trial.transition ? -4 : 0;
            bool carry = trial.drivers > 0;
            var rotation = Quaternion.AngleAxis(trial.transition ? 0 : trial.grade, Vector3.forward);
            var up = rotation * Vector3.up;
            Vector3 tankPosition = Origin + new Vector3(start, lab.standingHeight + lab.tankSize.y / (2 * up.y), 0);
            if (carry)
            {
                lab.Tank.position = tankPosition;
                lab.Tank.rotation = rotation;
                lab.Tank.transform.SetPositionAndRotation(tankPosition, rotation);
            }
            for (int i = 0; i < (carry ? 3 : 1); i++)
            {
                var actor = lab.Players[i];
                Vector3 head = carry ? tankPosition + rotation * (lab.CarryFormationOffset(i) + Vector3.down * (lab.tankSize.y / 2)) : tankPosition;
                float x = head.x - Origin.x;
                var feet = new Vector3(head.x, Origin.y + TerrainY(trial, x) + actor.capsule.radius * (1 / up.y - 1) + .004f, head.z);
                lab.PlacePlayer(i, feet);
                actor.body.transform.position = feet;
                if (carry)
                {
                    actor.height = actor.capsule.height = head.y - feet.y;
                    actor.capsule.center = Vector3.up * (actor.height / 2);
                }
            }
            Physics.SyncTransforms();
            if (carry) for (int i = 0; i < 3; i++) { lab.SetInput(i, Vector3.zero, false); lab.SetInput(i, Vector3.zero, true); }
            lab.SetExternalView(carry ? 2 : 0, 33, 22);
        }

        IEnumerator Measure(Trial trial, Action<Sample> record)
        {
            phase = "准备"; live = true; speed = distance = 0;
            BuildFloor(trial);
            lab.ResetLab();
            yield return null;
            Place(trial);
            bool carry = trial.drivers > 0;
            int actorCount = carry ? 3 : 1;
            for (int i = 0; i < 30; i++)
            {
                yield return Tick;
                if (i >= 11 && lab.Players.Take(actorCount).All(a => a.grounded)) break;
            }
            Rigidbody body = carry ? lab.Tank : lab.Players[0].body;
            Vector3 start = body.position, up = body.rotation * Vector3.up;
            int grips = Enumerable.Range(0, 3).Count(lab.IsGripping);
            var s = new Sample
            {
                label = trial.Label, grade = trial.grade, drivers = trial.drivers, sprint = trial.sprint,
                transition = trial.transition, initialGrips = grips, minGrips = grips,
                valid = !lab.Broken && (!carry || grips == 3) && lab.Players.Take(actorCount).All(a => a.grounded)
            };
            phase = s.valid ? "按住前进" : "起始站位失效";
            float dt = Time.fixedDeltaTime;
            if (s.valid)
            {
                for (int step = 0; step < Mathf.RoundToInt((trial.transition ? 8 : DriveSeconds) / dt) && !lab.Broken; step++)
                {
                    for (int i = 0; i < actorCount; i++)
                        lab.SetInput(i, !carry || i < trial.drivers ? Vector3.right : Vector3.zero, carry, sprint: trial.sprint);
                    yield return Tick;
                    s.driveSeconds += dt;
                    speed = body.linearVelocity.x;
                    distance = body.position.x - start.x;
                    if (step == 0) s.firstSpeedX = speed;
                    s.peakSpeedX = Mathf.Max(speed, s.peakSpeedX);
                    s.groundSamples += lab.Players.Take(actorCount).Count(a => a.grounded);
                    s.samples += actorCount;
                    s.minGrips = Mathf.Min(s.minGrips, Enumerable.Range(0, 3).Count(lab.IsGripping));
                    s.tankTiltChange = Mathf.Max(s.tankTiltChange, Vector3.Angle(up, body.rotation * Vector3.up));
                    if (trial.transition && body.position.x > Origin.x + 10)
                    { s.clearedRamp = true; break; }
                }
                Vector3 release = body.position;
                s.distanceX = release.x - start.x; s.heightChange = release.y - start.y; s.endSpeedX = speed;
                phase = "松开方向键";
                for (int i = 0; i < actorCount; i++) lab.SetInput(i, Vector3.zero, carry, sprint: trial.sprint);
                for (int step = 0; step < Mathf.RoundToInt(BrakeSeconds / dt) && !lab.Broken; step++)
                {
                    yield return Tick;
                    speed = body.linearVelocity.x; distance = body.position.x - start.x;
                    s.settledSeconds = Mathf.Abs(speed) <= .05f ? (s.settledSeconds < 0 ? (step + 1) * dt : s.settledSeconds) : -1;
                    s.minGrips = Mathf.Min(s.minGrips, Enumerable.Range(0, 3).Count(lab.IsGripping));
                }
                s.brakeDistanceX = body.position.x - release.x; s.finalSpeedX = speed;
            }
            s.broken = lab.Broken; s.message = lab.Message;
            record(s);
            phase = s.broken ? "鱼缸碰撞破碎" : s.valid ? "记录完成" : "起始站位失效";
            yield return new WaitForSecondsRealtime(.35f);
        }

        void OnGUI()
        {
            if (!lab) return;
            title ??= new GUIStyle(GUI.skin.label) { font = font, fontSize = 20, fontStyle = FontStyle.Bold };
            display ??= new GUIStyle(GUI.skin.label) { font = font, fontSize = 16 };
            var previousDepth = GUI.depth;
            GUI.depth = -1000;
            float width = Mathf.Min(Screen.width - 32, 495);
            GUILayout.BeginArea(new Rect((Screen.width - width) / 2, 60, width, 125), GUI.skin.box);
            GUILayout.Label(trials[current].Label, title);
            GUILayout.Label($"{current + 1}/{trials.Count}   {phase}", display);
            GUILayout.Label($"水平速度 {speed:F2} m/s   水平位移 {distance:F2} m", display);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上一项")) StartRun(Mathf.Max(0, current - 1));
            if (GUILayout.Button("重放本项")) StartRun(current);
            if (GUILayout.Button("下一项")) StartRun(Mathf.Min(trials.Count - 1, current + 1));
            if (GUILayout.Button(live ? "停止本轮" : "整组重跑"))
            {
                if (live) { if (run != null) StopCoroutine(run); run = null; for (int i = 0; i < 3; i++) lab.SetInput(i, Vector3.zero, trials[current].drivers > 0); phase = "已停止 · 可重放本项"; live = false; }
                else StartRun(0, true);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            GUI.depth = previousDepth;
        }

        void OnDestroy()
        {
            if (lab) lab.Testing = false;
            if (mesh) Destroy(mesh);
            if (surface) Destroy(surface);
            if (markings) Destroy(markings);
            if (font) Destroy(font);
        }
    }
}
#endif
