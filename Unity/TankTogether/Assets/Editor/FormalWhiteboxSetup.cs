using System;
using System.IO;
using System.Linq;
using TankTogether.M0;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace TankTogether.Editor {
 public static class FormalWhiteboxSetup {
  public const string ScenePath="Assets/Scenes/SteamFormalWhiteboxV1.unity";
  public const string ModelPath=WhiteboxV2Setup.ModelPath;
  public const string BaselinePath="Assets/Resources/FormalWhiteboxV1.asset";

  [MenuItem("Tank Together/Whitebox/Restore Authored Central Pedestal")]
  public static void RestoreAuthoredCentralPedestal(){
   if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=ScenePath)throw new InvalidOperationException("Stopped formal scene required");
   var route=UnityEngine.Object.FindFirstObjectByType<WhiteboxCarryLevel>();
   var pedestal=route.levelRoot.Find("Group 31/Authored central pedestal safe contact");
   var sourceFeet=route.checkpoints[0].tankFeet;
   if(pedestal)sourceFeet-=pedestal.parent.TransformVector(pedestal.localPosition);
   FormalLevelRevisionSetup.SplitPedestal(route,sourceFeet);
   EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
  }

  [MenuItem("Tank Together/Whitebox/Open Formal Whitebox V1")]
  public static void Open(){
   if(EditorApplication.isPlaying||SceneManager.GetActiveScene().isDirty)throw new InvalidOperationException("Stop Play and save first");
   if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))throw new InvalidOperationException("Restore the formal scene from version control; legacy maps are retired.");
   EditorSceneManager.OpenScene(ScenePath);
  }
 }
}
