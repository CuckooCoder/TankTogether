using UnityEditor;
using UnityEngine;
using TankTogether.M0;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TankTogether.Editor
{
    public static class M0BaselineSetup
    {
        [MenuItem("Tank Together/M0/Apply Document Baseline")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying || SceneManager.GetActiveScene().isDirty)
                throw new System.InvalidOperationException("Stop Play and save the current scene first");
            const string path = "Assets/Resources/CarryBaseline.asset";
            var baseline = AssetDatabase.LoadAssetAtPath<M0CarryBaseline>(path);
            if (!baseline)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                baseline = ScriptableObject.CreateInstance<M0CarryBaseline>();
                AssetDatabase.CreateAsset(baseline, path);
            }
            string previous = SceneManager.GetActiveScene().path;
            foreach (string scenePath in new[] { M0CarryLabSetup.ScenePath, SteamCarrySetup.ScenePath })
            {
                var scene = EditorSceneManager.OpenScene(scenePath);
                var lab = Object.FindFirstObjectByType<M0CarryLab>();
                lab.baseline = baseline;
                EditorUtility.SetDirty(lab);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
            Debug.Log("[Baseline] Both lab scenes use " + baseline.Signature);
        }
        [MenuItem("Tank Together/M0/Measure Motion Baseline")]
        public static void Measure()
        {
            var lab = Object.FindFirstObjectByType<M0CarryLab>();
            if (!EditorApplication.isPlaying || !lab || lab.ExternalControl || lab.Testing)
                throw new System.InvalidOperationException("Play the local idle M0 lab first");
            lab.StartCoroutine(M0BaselineMeasurements.Run(lab));
        }
    }
}
