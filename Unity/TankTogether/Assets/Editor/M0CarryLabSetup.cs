using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TankTogether.M0;

namespace TankTogether.Editor
{
    public static class M0CarryLabSetup
    {
        public const string ScenePath = "Assets/Scenes/M0CarryLab.unity";

        [MenuItem("Tank Together/M0/Open Carry Lab")]
        public static void Open()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before opening lab");
            if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save your current scene before opening lab");
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.09f, 0.12f);
            camera.fieldOfView = 55;
            camera.nearClipPlane = 0.05f;
            var light = UnityEngine.Object.FindFirstObjectByType<Light>();
            light.transform.rotation = Quaternion.Euler(48, -35, 0);
            light.intensity = 1.5f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.57f, 0.62f);
            var lab = new GameObject("M0 Carry Lab").AddComponent<M0CarryLab>();
            lab.surfaceShader = Shader.Find("Universal Render Pipeline/Lit");
            if (!lab.surfaceShader) throw new InvalidOperationException("URP Lit shader unavailable");
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        [MenuItem("Tank Together/M0/Run Carry Checks")]
        public static void Checks()
        {
            var lab = UnityEngine.Object.FindFirstObjectByType<M0CarryLab>();
            if (!EditorApplication.isPlaying || !lab) throw new InvalidOperationException("Play M0CarryLab first");
            lab.StartChecks();
        }

        [MenuItem("Tank Together/M0/Build Carry Lab Windows")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before build");
            if (!File.Exists(ScenePath)) throw new InvalidOperationException("Create lab scene first");
            const string output = "Builds/Baseline-v022-r1/M0CarryLab";
            Directory.CreateDirectory(output);
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = output + "/TankTogether-M0.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/m0-carry-build.json", JsonUtility.ToJson(new BuildEvidence {
                result = result.summary.result.ToString(), errors = result.summary.totalErrors,
                bytes = result.summary.totalSize, utc = DateTime.UtcNow.ToString("O")
            }, true));
            if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("M0 build failed");
        }
        [Serializable] class BuildEvidence { public string result, utc; public int errors; public ulong bytes; }
    }
}
