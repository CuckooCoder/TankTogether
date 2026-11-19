using UnityEditor;
using UnityEngine;
using TankTogether.M0;
namespace TankTogether.Editor {
 public static class MotionStyleEditor {
  public const string Path="Assets/Resources/CharacterAnimation/MotionStyle.asset";
  [MenuItem("Tank Together/角色表现参数")]
  public static void Open(){var asset=AssetDatabase.LoadAssetAtPath<CarryMotionStyle>(Path);if(!asset){asset=ScriptableObject.CreateInstance<CarryMotionStyle>();AssetDatabase.CreateAsset(asset,Path);AssetDatabase.SaveAssets();}Selection.activeObject=asset;EditorGUIUtility.PingObject(asset);}
 }
}
