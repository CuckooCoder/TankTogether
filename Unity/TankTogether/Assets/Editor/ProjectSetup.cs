using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
namespace TankTogether.Editor
{
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        [MenuItem("Tank Together/Environment/Connect MCP")]
        public static async void ConnectMcp()
        {
            try
            {
                ConfigureMcp();
                bool connected = await MCPServiceLocator.TransportManager.StartAsync(TransportMode.Http);
                Debug.Log($"[Tank Together] MCP connected: {connected}");
            }
            catch (Exception ex) { Debug.LogException(ex); }
        }

        public static void ConfigureMcp()
        {
            EditorPrefs.SetBool("MCPForUnity.UseHttpTransport", true);
            EditorPrefs.SetString("MCPForUnity.HttpTransportScope", "local");
            EditorPrefs.SetString("MCPForUnity.HttpUrl", "http://127.0.0.1:8088");
            EditorPrefs.SetBool("MCPForUnity.TelemetryDisabled", true);
            EditorPrefs.SetBool("MCPForUnity.AutoStartOnLoad", false);
            EditorPrefs.SetBool("MCPForUnity.SetupDismissed", true);
            EditorConfigurationCache.Instance.Refresh();
        }

        [MenuItem("Tank Together/Environment/Verify")]
        public static void Verify()
        {
            ConfigureMcp();
            EditorSettings.serializationMode = SerializationMode.ForceText;
            UnityEditor.VersionControlSettings.mode = "Visible Meta Files";
            EditorSettings.projectGenerationRootNamespace = "TankTogether";
            PlayerSettings.companyName = "DefaultCompany";
            PlayerSettings.productName = "Tank Together";
            PlayerSettings.bundleVersion = "0.0.1";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.tanktogether.game");
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.runInBackground = true;
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (!pipeline) throw new InvalidOperationException("Missing URP pipeline");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            else EditorSceneManager.OpenScene(ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/environment-verify.json", "{\"passed\":true,\"unityVersion\":\"" + Application.unityVersion + "\",\"scene\":\"" + ScenePath + "\"}");
            Debug.Log("[Tank Together] VERIFY PASS");
        }
        public static void BuildWindows()
        {
            Verify();
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = "Builds/Windows/TankTogether.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Build failed");
            Debug.Log("[Tank Together] BUILD PASS");
        }
    }
    [InitializeOnLoad]
    internal static class ProjectMcpConnection
    {
        static ProjectMcpConnection()
        {
            if (!Application.isBatchMode) EditorApplication.delayCall += ConnectWhenReady;
        }

        private static void ConnectWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ConnectWhenReady;
                return;
            }
            ProjectSetup.ConnectMcp();
        }
    }
}
