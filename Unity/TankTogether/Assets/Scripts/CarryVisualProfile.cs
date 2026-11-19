using UnityEngine;
namespace TankTogether.M0 {
 // Art-specific data on the visual prefab; gameplay and network identities stay unchanged.
 public sealed class CarryVisualProfile : MonoBehaviour {
  public RuntimeAnimatorController controller;
  public Material[] colors;
  public void ApplyColor(int index) {
   if(colors==null||colors.Length==0)return;
   var material=colors[Mathf.Clamp(index,0,colors.Length-1)];if(!material)return;
   foreach(var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))r.sharedMaterial=material;
  }
 }
}
