using System;
using System.Linq;
using System.Collections.Generic;
using TankTogether.M0;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace TankTogether.Editor {
 public static class FormalLevelRevisionSetup {
  public const string ModelPath="Assets/Scenes/FishTank关卡白盒第一版_20261016.skp";
  // Historical October 16 importer; hidden to prevent replacing the active V2 map.
  public static void Apply(){
   if(UnityEngine.Object.FindFirstObjectByType<WhiteboxMovingBlocks>())throw new InvalidOperationException("V2 is active; use WhiteboxV2Setup instead");
   if(EditorApplication.isPlaying||SceneManager.GetActiveScene().path!=FormalWhiteboxSetup.ScenePath)throw new InvalidOperationException("Stopped formal scene required");
   var route=UnityEngine.Object.FindFirstObjectByType<WhiteboxCarryLevel>();
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
   if(!prefab)throw new InvalidOperationException("Updated model is not imported");
   // Detach the previous imported-model source; all geometry is replaced below while scene references remain valid.
   if(PrefabUtility.IsPartOfPrefabInstance(route.levelRoot.gameObject))
    PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(route.levelRoot.gameObject),PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
   var temp=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
   try {
    temp.transform.SetPositionAndRotation(route.levelRoot.position,route.levelRoot.rotation);temp.transform.localScale=route.levelRoot.localScale;
    var originals=route.levelRoot.GetComponentsInChildren<MeshFilter>(true).ToDictionary(x=>x.name);
    foreach(var source in temp.GetComponentsInChildren<MeshFilter>(true)){
     if(!originals.TryGetValue(source.name,out var target)){
      var go=new GameObject(source.name);go.transform.SetParent(route.levelRoot,false);target=go.AddComponent<MeshFilter>();go.AddComponent<MeshRenderer>();go.AddComponent<MeshCollider>();go.AddComponent<M0Danger>();
     }
     target.transform.SetPositionAndRotation(source.transform.position,source.transform.rotation);target.transform.localScale=source.transform.localScale;
     target.sharedMesh=source.sharedMesh;
     target.GetComponent<MeshRenderer>().sharedMaterials=source.GetComponent<MeshRenderer>().sharedMaterials;
     var collider=target.GetComponent<MeshCollider>();if(collider)collider.sharedMesh=source.sharedMesh;
    }
    route.levelRoot.name="Formal whitebox revision 20261016";
    var marker=route.levelRoot.GetComponentsInChildren<MeshRenderer>(true).Single(x=>x.name=="Group 29");
    var feet=marker.bounds.center;feet.y=marker.bounds.min.y-1.7f;
    route.checkpoints[0].tankFeet=feet;
    route.checkpoints[0].tankRestHeight=1.7f;
    var upper=route.checkpoints.Single(x=>x.id=="upper");
    route.checkpoints=new[]{route.checkpoints[0],new WhiteboxCarryLevel.Checkpoint{id="pickup",label="鱼缸初始台座",playerFeet=feet,tankFeet=feet,parkingFeet=feet,tankYaw=route.checkpoints[0].tankYaw,tankRestHeight=1.7f,activateOnGrip=true},upper};
    route.geometryRevision="formal-v1-20261016-three-checkpoints-r1";
    route.FinalStationIsTestFinish=false;
    var lift=route.GetComponent<WhiteboxLift>();
    var platform=route.levelRoot.GetComponentsInChildren<Transform>(true).Single(x=>x.name=="Group 23");
    lift.platform=platform.GetComponent<Rigidbody>();lift.platformCollider=platform.GetComponent<Collider>();lift.bottomPosition=platform.position;
    // The switch travels with the deck and is the authoritative interaction target.
    var control=route.levelRoot.GetComponentsInChildren<Transform>(true).Single(x=>x.name=="Group 59");control.SetParent(platform,true);lift.switchCollider=control.GetComponent<Collider>();
    SplitPedestal(route,feet);
    foreach(string name in new[]{"Group 33","Group 35"}){var t=route.levelRoot.GetComponentsInChildren<Transform>(true).Single(x=>x.name==name);t.gameObject.SetActive(true);foreach(var c in t.GetComponentsInChildren<Collider>())c.enabled=false;}
    marker.gameObject.SetActive(false);
    EditorUtility.SetDirty(route);EditorUtility.SetDirty(lift);
    UnityEngine.Object.DestroyImmediate(temp);temp=null;Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    Debug.Log("October16 level imported: 3 checkpoints, initial fish marker "+marker.bounds.center+", F switch interaction configured");
   } finally {if(temp)UnityEngine.Object.DestroyImmediate(temp);}
  }
  public static void SplitPedestal(WhiteboxCarryLevel route,Vector3 feet){
   var node=route.levelRoot.GetComponentsInChildren<Transform>(true).Single(x=>x.name=="Group 31");
   var original=node.GetComponent<MeshFilter>().sharedMesh;
   var safe=UnityEngine.Object.Instantiate(original);safe.name="Authored central pedestal safe collider";
   var rest=UnityEngine.Object.Instantiate(original);rest.name="Current route excluding safe pedestal";
   var verts=original.vertices;int kept=0;
   bool Inside(int i){var v=node.TransformPoint(verts[i])-feet;return Mathf.Abs(v.x)<.70f&&Mathf.Abs(v.z)<.70f&&v.y>=-.002f&&v.y<=1.702f;}
   for(int sub=0;sub<original.subMeshCount;sub++){
    var a=new List<int>();var b=new List<int>();var triangles=original.GetTriangles(sub);
    for(int i=0;i<triangles.Length;i+=3){bool inside=Inside(triangles[i])&&Inside(triangles[i+1])&&Inside(triangles[i+2]);var dst=inside?a:b;if(inside)kept++;dst.Add(triangles[i]);dst.Add(triangles[i+1]);dst.Add(triangles[i+2]);}
    safe.SetTriangles(a,sub);rest.SetTriangles(b,sub);
   }
   if(kept==0)throw new InvalidOperationException("No pedestal triangles found");
   safe=Save(safe,"Assets/Scenes/FormalV1CentralPedestalCollider.asset");rest=Save(rest,"Assets/Scenes/FormalV1RouteWithoutReferencePedestal.asset");
   node.GetComponent<MeshCollider>().sharedMesh=rest;
   var support=node.Find("Authored central pedestal safe contact");if(!support){var go=new GameObject("Authored central pedestal safe contact");support=go.transform;support.SetParent(node,false);go.AddComponent<MeshCollider>();}
   support.GetComponent<MeshCollider>().sharedMesh=safe;
   Debug.Log("Rebuilt collision from current mesh; safe pedestal triangles="+kept+", all remaining triangles preserved");
  }
  static Mesh Save(Mesh mesh,string path){var saved=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(saved){EditorUtility.CopySerialized(mesh,saved);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(saved);return saved;}AssetDatabase.CreateAsset(mesh,path);return mesh;}
 }
}
