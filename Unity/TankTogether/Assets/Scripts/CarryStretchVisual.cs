using System;
using System.Collections.Generic;
using UnityEngine;
namespace TankTogether.M0 {
 // Visual-only additive torso extension. Head/accessories and limb bone lengths never scale.
 // Remove our last offsets before Animator evaluation; apply after its pose, before contact IK.
 public sealed class CarryStretchVisual {
  readonly Transform root;
  readonly List<Anchor> anchors=new List<Anchor>();
  readonly List<RenderBounds> bounds=new List<RenderBounds>();
  sealed class Anchor { public Transform bone;public float relativeWeight;public Vector3 applied; }
  sealed class RenderBounds { public SkinnedMeshRenderer renderer;public Bounds original; }
  public bool Ready {get;private set;}
  public float AppliedExtension {get;private set;}
  public CarryStretchVisual(Transform visual){
   root=visual;var bones=new Dictionary<string,Transform>();
   foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)){
    bounds.Add(new RenderBounds{renderer=renderer,original=renderer.localBounds});
    foreach(var bone in renderer.bones)if(bone&&!bones.ContainsKey(bone.name))bones.Add(bone.name,bone);
   }
   if(!bones.TryGetValue("Pelvis",out var pelvis)||!bones.TryGetValue("Head",out var head)||!bones.ContainsKey("Spine")||!bones.ContainsKey("Chest"))return;
   float bottom=root.InverseTransformPoint(pelvis.position).y,top=root.InverseTransformPoint(head.position).y;
   if(top-bottom<.1f)return;
   var weights=new Dictionary<Transform,float>();
   foreach(var name in new[]{"Spine","Chest","Head","Clavicle.L","Clavicle.R"}){
    if(!bones.TryGetValue(name,out var bone))return;
    float weight=name=="Head"||name.StartsWith("Clavicle")?1:Mathf.InverseLerp(bottom,top,root.InverseTransformPoint(bone.position).y);
    float inherited=weights.TryGetValue(bone.parent,out float parentWeight)?parentWeight:0;
    anchors.Add(new Anchor{bone=bone,relativeWeight=weight-inherited});weights[bone]=weight;
   }
   Ready=true;
  }
  public void Restore(){foreach(var a in anchors){if(a.bone)a.bone.localPosition-=a.applied;a.applied=Vector3.zero;}AppliedExtension=0;}
  public void Apply(float height){
   Restore();if(!Ready)return;
   // The imported rig and clips are authored at 1.7 m. Root Y scale stays exactly one.
   AppliedExtension=height-1.7f;
   foreach(var a in anchors){a.applied=a.bone.parent.InverseTransformVector(root.up*(AppliedExtension*a.relativeWeight));a.bone.localPosition+=a.applied;}
   foreach(var b in bounds){if(!b.renderer)continue;var box=b.original;var offset=b.renderer.transform.InverseTransformVector(root.up*AppliedExtension);box.Encapsulate(b.original.min+offset);box.Encapsulate(b.original.max+offset);b.renderer.localBounds=box;}
  }
 }
}
