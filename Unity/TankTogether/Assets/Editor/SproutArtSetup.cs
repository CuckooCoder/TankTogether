using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using TankTogether.M0;
namespace TankTogether.Editor {
 public static class SproutArtSetup {
  public const string Folder="Assets/Art/Characters/PartySprouts";
  const string Source="Assets/ToonBridgeModels/Party_Sprouts_Blender52_cc594b65/Characters/";
  public const string Prefab=Folder+"/PartySprout.prefab";
  static string BoneName(string n) {
   var map=new Dictionary<string,string>{{"pelvis","Pelvis"},{"spine","Spine"},{"chest","Chest"},{"head","Head"},{"shoulder","Clavicle"},{"upper_arm","UpperArm"},{"forearm","Forearm"},{"hand","Hand"},{"thigh","Thigh"},{"shin","Shin"},{"foot","Foot"}};
   var a=n.Split('.');return map.TryGetValue(a[0],out var name)?name+(a.Length>1?"."+a[1]:""):n;
  }
  struct Rest {public Transform t;public Vector3 p,s;public Quaternion q;public void Reset(){t.localPosition=p;t.localRotation=q;t.localScale=s;}}
  [MenuItem("Tank Together/Art/Import Party Sprouts")]
  public static void Build() {
   if(Application.isPlaying)throw new InvalidOperationException("Stop Play first");
   Directory.CreateDirectory(Folder);AssetDatabase.Refresh();
   var src=AssetDatabase.LoadAssetAtPath<GameObject>(Source+"RIG_Sky/Model.fbx");
   var sample=UnityEngine.Object.Instantiate(src);var root=new GameObject("PartySprout");var model=UnityEngine.Object.Instantiate(src,root.transform);model.name="Model";
   try {
    foreach(var a in model.GetComponentsInChildren<Animator>())UnityEngine.Object.DestroyImmediate(a);
    var transforms=model.GetComponentsInChildren<Transform>(true);
    var paths=transforms.ToDictionary(t=>AnimationUtility.CalculateTransformPath(t,model.transform),t=>t);
    // Normalize once in the art wrapper, never scale the physics root or animated bones.
    var mesh=new Mesh();var renderer=model.GetComponentInChildren<SkinnedMeshRenderer>();renderer.BakeMesh(mesh);
    var points=mesh.vertices.Select(v=>renderer.transform.TransformPoint(v)).ToArray();float min=points.Min(v=>v.y),max=points.Max(v=>v.y);float scale=1.7f/(max-min);
    model.transform.localScale=Vector3.one*scale;model.transform.localPosition=Vector3.up*(-min*scale);
    UnityEngine.Object.DestroyImmediate(mesh);
    foreach(var t in transforms)t.name=BoneName(t.name);
    AdaptTorso(renderer,root.transform);
    var rest=transforms.Select(t=>new Rest{t=t,p=t.localPosition,q=t.localRotation,s=t.localScale}).ToArray();
    var clips=new Dictionary<string,AnimationClip>();
    foreach(var pair in new[]{("Idle","Sprout_Idle"),("Walk","Sprout_Run"),("Run","Sprout_Run"),("Celebrate","Sprout_Celebrate")}) {
     var clip=AssetDatabase.LoadAllAssetsAtPath(Source+"RIG_Sky/Model.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__")&&c.name.EndsWith(pair.Item2));
     float duration=pair.Item1=="Walk"?1.05f:pair.Item1=="Run"?.70f:clip.length;
     clips[pair.Item1]=Bake(root,paths.Values.Where(t=>t!=model.transform).ToArray(),pair.Item1,duration,true,t=>{
      clip.SampleAnimation(sample,t*clip.length);
      foreach(var p in paths)if(p.Key.Length>0){var from=sample.transform.Find(p.Key);p.Value.localPosition=from.localPosition;p.Value.localRotation=from.localRotation;p.Value.localScale=from.localScale;}
     });
    }
    var bones=model.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Distinct().ToDictionary(b=>b.name);
    var pose=typeof(CharacterAnimationAuthoring).GetMethod("Pose",BindingFlags.Static|BindingFlags.NonPublic);
    foreach(var name in new[]{"Takeoff","Rise","Apex","Fall","LandSoft","LandHard"}) {
     float duration=name=="Takeoff"?.1f:name.StartsWith("Land")?.26f:.4f;
     clips[name]=Bake(root,transforms.Where(t=>t!=model.transform).ToArray(),name,duration,name=="Rise"||name=="Apex"||name=="Fall",t=>{foreach(var r in rest)r.Reset();pose.Invoke(null,new object[]{root.transform,bones,name,t});});
    }
    foreach(var r in rest)r.Reset();
    string controllerPath=Folder+"/SproutCharacter.controller";
    if(!AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath)&&!AssetDatabase.CopyAsset("Assets/Resources/CharacterAnimation/CarryCharacter.controller",controllerPath))throw new Exception("Controller copy failed");
    var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
    foreach(var state in controller.layers[0].stateMachine.states) Replace(state.state.motion,clips,state.state);
    EditorUtility.SetDirty(controller);
    var profile=root.AddComponent<CarryVisualProfile>();profile.controller=controller;
    profile.colors=new[]{"Sky","Lilac","Lemon","Coral"}.Select(color=>AssetDatabase.LoadAssetAtPath<GameObject>(Source+"RIG_"+color+"/Model.fbx").GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial).ToArray();
    foreach(var material in profile.colors)if(!material||!material.shader||!material.shader.isSupported)throw new Exception("Missing/unsupported art material");
    profile.ApplyColor(0);foreach(var r in root.GetComponentsInChildren<SkinnedMeshRenderer>())r.updateWhenOffscreen=true;
    PrefabUtility.SaveAsPrefabAsset(root,Prefab);AssetDatabase.SaveAssets();
    UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(sample);
    var lab=UnityEngine.Object.FindFirstObjectByType<M0CarryLab>();if(!lab)throw new Exception("No current lab");Undo.RecordObject(lab,"Replace character art");lab.playerVisualPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);EditorUtility.SetDirty(lab);EditorSceneManager.MarkSceneDirty(lab.gameObject.scene);EditorSceneManager.SaveScene(lab.gameObject.scene);
    Debug.Log("[Sprouts] Prefab, four colors, authored idle/movement and adapted jump clips ready. scale="+scale+" sourceHeight="+(max-min));
   } finally {if(root)UnityEngine.Object.DestroyImmediate(root);if(sample)UnityEngine.Object.DestroyImmediate(sample);}
  }
  // Source weights are segmented at different heights around the neck. Translate-only
  // shortening exposes inverted rings. Reweight only pure torso vertices along the
  // torso bone chain; preserve all limb/leaf weights and the original FBX mesh.
  static void AdaptTorso(SkinnedMeshRenderer renderer,Transform root) {
   var source=renderer.sharedMesh;var mesh=UnityEngine.Object.Instantiate(source);mesh.name="Sprout torso stretch adapter";
   string[] names={"Pelvis","Spine","Chest","Head"};int[] chain=names.Select(n=>Array.FindIndex(renderer.bones,b=>b.name==n)).ToArray();
   if(chain.Any(i=>i<0))throw new Exception("Incomplete torso rig");
   float[] heights=chain.Select(i=>root.InverseTransformPoint(renderer.bones[i].position).y).ToArray();
   var weights=mesh.boneWeights;var vertices=mesh.vertices;int changed=0;
   for(int i=0;i<weights.Length;i++){
    var b=weights[i];int[] ids={b.boneIndex0,b.boneIndex1,b.boneIndex2,b.boneIndex3};float[] ws={b.weight0,b.weight1,b.weight2,b.weight3};
    if(Enumerable.Range(0,4).Any(j=>ws[j]>.00001f&&!chain.Contains(ids[j])))continue;
    float y=root.InverseTransformPoint(renderer.transform.TransformPoint(vertices[i])).y;
    int upper=Array.FindIndex(heights,h=>h>=y);if(upper<0)upper=3;int lower=Mathf.Max(0,upper-1);
    float blend=upper==lower?0:Mathf.InverseLerp(heights[lower],heights[upper],y);
    weights[i]=new BoneWeight{boneIndex0=chain[lower],weight0=1-blend,boneIndex1=chain[upper],weight1=blend};changed++;
   }
   mesh.boneWeights=weights;string path=Folder+"/SproutStretchMesh.asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
   if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;}else AssetDatabase.CreateAsset(mesh,path);
   EditorUtility.SetDirty(mesh);renderer.sharedMesh=mesh;Debug.Log("[Sprouts] Torso adapter vertices="+changed);
  }
  static void Replace(Motion motion,Dictionary<string,AnimationClip> clips,AnimatorState state=null) {
   if(motion is BlendTree tree){var children=tree.children;for(int i=0;i<children.Length;i++){if(children[i].motion is AnimationClip c&&clips.TryGetValue(c.name,out var next))children[i].motion=next;else Replace(children[i].motion,clips);}tree.children=children;EditorUtility.SetDirty(tree);}
   else if(state&&motion&&clips.TryGetValue(motion.name,out var next)){state.motion=next;EditorUtility.SetDirty(state);}
  }
  static AnimationClip Bake(GameObject root,Transform[] transforms,string name,float duration,bool loop,Action<float> sample) {
   string path=Folder+"/"+name+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(!clip){clip=new AnimationClip();AssetDatabase.CreateAsset(clip,path);}clip.ClearCurves();clip.name=name;clip.frameRate=30;
   var curves=transforms.ToDictionary(t=>t,t=>Enumerable.Range(0,10).Select(i=>new AnimationCurve()).ToArray());int count=Mathf.CeilToInt(duration*30);
   for(int i=0;i<=count;i++){float t=(float)i/count;sample(t);foreach(var b in transforms){var c=curves[b];var q=b.localRotation;if(i>0&&Quaternion.Dot(q,new Quaternion(c[3].keys[i-1].value,c[4].keys[i-1].value,c[5].keys[i-1].value,c[6].keys[i-1].value))<0)q=new Quaternion(-q.x,-q.y,-q.z,-q.w);var p=b.localPosition;var s=b.localScale;float[] values={p.x,p.y,p.z,q.x,q.y,q.z,q.w,s.x,s.y,s.z};for(int j=0;j<10;j++)c[j].AddKey(t*duration,values[j]);}}
   foreach(var curvesForBone in curves.Values)for(int j=0;j<curvesForBone.Length;j++){
    var curve=curvesForBone[j];float first=curve.keys[0].value;
    if(curve.keys.All(k=>Mathf.Abs(k.value-first)<.000001f))curvesForBone[j]=AnimationCurve.Constant(0,duration,first);
    else for(int k=0;k<curve.length;k++)curve.SmoothTangents(k,0);
   }
   string[] props={"localPosition.x","localPosition.y","localPosition.z","localRotation.x","localRotation.y","localRotation.z","localRotation.w","localScale.x","localScale.y","localScale.z"};
   foreach(var b in transforms)for(int j=0;j<10;j++)AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(b,root.transform),typeof(Transform),props[j]),curves[b][j]);
   var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=loop;settings.loopBlend=loop;AnimationUtility.SetAnimationClipSettings(clip,settings);clip.EnsureQuaternionContinuity();EditorUtility.SetDirty(clip);return clip;
  }
 }
}
