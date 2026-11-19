using UnityEngine;
namespace TankTogether.M0 {
 [CreateAssetMenu(menuName="Tank Together/角色表现参数")]
 public sealed class CarryMotionStyle:ScriptableObject {
  [Header("只影响骨骼表现，不改变速度、碰撞、抓握或网络物理")]
  [Tooltip("四肢跟随动画的柔软程度。0为原始动画，1为轻微延迟和回摆；过大可能显得拖沓。")][Range(0,1.5f)] public float armSoftness=1;
  [Tooltip("高个子摇摆开始的身高（米）；以下没有额外摇摆。")][Range(1.7f,5)] public float tallStartHeight=2.3f;
  [Tooltip("达到最大摇摆的身高（米），必须高于开始身高。")][Range(1.8f,5)] public float tallFullHeight=4.5f;
  [Tooltip("走动时躯干额外侧摆幅度（度）。头部反向平衡；不旋转物理刚体。")][Range(0,5)] public float tallSwayDegrees=3;
  [Tooltip("摇摆频率（次/秒），奔跑略快。")][Range(.4f,2)] public float tallSwayHz=1.1f;
  [Tooltip("启停平滑时间（秒）。越大越柔和，但收稳更慢。")][Range(.08f,.5f)] public float settleSeconds=.22f;
  [Tooltip("托举时每只手离中心的水平距离（米）。加大留出头帽空间；只改姿态，不改物理抓点。")][Range(.22f,.42f)] public float carryHandHalfWidth=.32f;
  [Tooltip("托举时肩骨向上抬起的距离（米），给肘部留弯曲余量。随抓取平滑过渡，不拉长手臂。")][Range(.1f,.3f)] public float carryShoulderLift=.22f;
  void OnValidate(){tallFullHeight=Mathf.Max(tallStartHeight+.1f,tallFullHeight);}
 }
}
