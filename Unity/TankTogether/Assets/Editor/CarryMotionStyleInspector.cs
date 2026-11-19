using UnityEditor;
using UnityEngine;
using TankTogether.M0;
namespace TankTogether.Editor {
 [CustomEditor(typeof(CarryMotionStyle))]
 public sealed class CarryMotionStyleInspector:UnityEditor.Editor {
  public override void OnInspectorGUI(){serializedObject.Update();EditorGUILayout.HelpBox("仅骨骼表现，物理稳定性不变。修改后立即影响当前角色；这是项目资源，保存工程后随版本共享。联机双方需使用同一资源版本，不走玩法GM同步。",MessageType.Info);
   string[] keys={"armSoftness","tallStartHeight","tallFullHeight","tallSwayDegrees","tallSwayHz","settleSeconds","carryHandHalfWidth","carryShoulderLift"};
   string[] labels={"摆臂柔软度","摇摆起始身高（米）","摇摆完整身高（米）","最大侧摆幅度（度）","摇摆频率（次/秒）","停步收稳时间（秒）","托举双手半间距（米）","托举抬肩高度（米）"};
   for(int i=0;i<keys.Length;i++){var p=serializedObject.FindProperty(keys[i]);EditorGUILayout.PropertyField(p,new GUIContent(labels[i],p.tooltip));}
   serializedObject.ApplyModifiedProperties();
  }
 }
}
