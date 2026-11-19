using System;
using System.IO;
using TankTogether.M0;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TankTogether.Editor
{
    public static class M1InteractionSetup
    {
        public const string ScenePath="Assets/Scenes/M1InteractionLab.unity";
        [MenuItem("Tank Together/M1/Open Interaction Lab")]
        public static void Open()
        {
            if(EditorApplication.isPlaying||SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Stop Play and save first");
            if(File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            var scene=EditorSceneManager.OpenScene(M0CarryLabSetup.ScenePath);
            UnityEngine.Object.FindFirstObjectByType<M0CarryLab>().gameObject.AddComponent<M1InteractionLab>();
            EditorSceneManager.SaveScene(scene,ScenePath);
        }
        [MenuItem("Tank Together/M1/Run Interaction Checks")]
        public static void Check()
        {
            var lab=UnityEngine.Object.FindFirstObjectByType<M1InteractionLab>();
            if(!EditorApplication.isPlaying||!lab||lab.Lab.Testing)throw new InvalidOperationException("Play idle M1 lab first");
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            lab.StartCoroutine(M1InteractionChecks.Run(lab));
        }
        [MenuItem("Tank Together/M1/Build Windows")]
        public static void Build()
        {
            if(EditorApplication.isPlaying||!File.Exists(ScenePath))throw new InvalidOperationException("Stop Play and create M1 scene first");
            const string output="Builds/M1-20261012";
            Directory.CreateDirectory(output);
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath},locationPathName=output+"/TankTogether-M1.exe",target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development });
            File.WriteAllText("Logs/m1-build.json",JsonUtility.ToJson(new Evidence { utc=DateTime.UtcNow.ToString("O"),result=report.summary.result.ToString(),errors=report.summary.totalErrors },true));
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("M1 build failed");
        }
        [Serializable]class Evidence { public string utc,result; public int errors; }
    }
}
