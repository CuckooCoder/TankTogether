using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TankTogether.M0;
namespace TankTogether.Editor {
 public static class StretchVisualChecks {
  static List<string> rows;static bool passed;
  static void C(string n,bool ok,object v){rows.Add(n+"="+ok+" "+v);passed&=ok;}
  public static string Inspect(){
   if(Application.isPlaying)throw new Exception("Stopped editor required");rows=new List<string>();passed=true;
   var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Characters/Juese001/Juese001_URPPreview.prefab");var objects=new List<GameObject>();
   var refMeshes=new Dictionary<string,Vector3[]>();var refLengths=new Dictionary<string,float>();var refScales=new Dictionary<string,Vector3>();
   var sourceJson=EditorJsonUtility.ToJson(prefab);
   try {
    int index=0;foreach(float height in new[]{1.7f,1.1f,3f,5f}){
     var go=UnityEngine.Object.Instantiate(prefab);objects.Add(go);go.name="Height "+height;go.transform.position=new Vector3(1000+(index-1.5f)*2,0,0);index++;
     foreach(var camera in go.GetComponentsInChildren<Camera>(true))camera.enabled=false;foreach(var light in go.GetComponentsInChildren<Light>(true))light.enabled=false;
     var bones=go.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(x=>x.bones).Where(x=>x).Distinct().ToDictionary(x=>x.name);
     var shape=new CarryStretchVisual(go.transform);C("rig_ready_"+height,shape.Ready,"");shape.Apply(height);
     foreach(string side in new[]{"L","R"})foreach(var pair in new[]{new[]{"UpperArm.","Forearm."},new[]{"Forearm.","Hand."},new[]{"Thigh.","Shin."},new[]{"Shin.","Foot."}}){string key=pair[0]+side;float length=Vector3.Distance(bones[pair[0]+side].position,bones[pair[1]+side].position);if(height==1.7f)refLengths[key]=length;C("limb_length_"+height+"_"+key,Mathf.Abs(length-refLengths[key])<.0001f,length);}
     foreach(var pair in bones){if(height==1.7f)refScales[pair.Key]=pair.Value.localScale;C("bone_scale_"+height+"_"+pair.Key,pair.Value.localScale==refScales[pair.Key],pair.Value.localScale);}
     foreach(var renderer in go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x=>x.name.Contains("Cap")||x.name.Contains("Eye")||x.name.Contains("Pupil"))){var mesh=new Mesh();renderer.BakeMesh(mesh);var points=mesh.vertices;var center=points.Aggregate(Vector3.zero,(a,b)=>a+b)/points.Length;points=points.Select(x=>x-center).ToArray();if(height==1.7f)refMeshes[renderer.name]=points;float error=points.Select((x,i)=>(x-refMeshes[renderer.name][i]).magnitude).Max();C("rigid_feature_"+height+"_"+renderer.name,error<.0003f,error);UnityEngine.Object.DestroyImmediate(mesh);}
     var before=bones["Head"].position;for(int n=0;n<120;n++){shape.Restore();shape.Apply(height);}C("no_accumulation_"+height,Vector3.Distance(before,bones["Head"].position)<.0005f,Vector3.Distance(before,bones["Head"].position));
    }
    var cg=new GameObject("Stretch comparison camera");objects.Add(cg);var cam=cg.AddComponent<Camera>();cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(.075f,.105f,.13f);cam.nearClipPlane=.05f;cam.farClipPlane=50;cam.orthographic=true;cam.orthographicSize=2.8f;cg.transform.position=new Vector3(1000,2.45f,12);cg.transform.rotation=Quaternion.Euler(0,180,0);
    var lg=new GameObject("Comparison light");objects.Add(lg);var sun=lg.AddComponent<Light>();sun.type=LightType.Directional;sun.intensity=1.4f;lg.transform.rotation=Quaternion.Euler(40,-35,0);
    var rt=new RenderTexture(1800,1050,24);cam.targetTexture=rt;cam.Render();var old=RenderTexture.active;RenderTexture.active=rt;var tex=new Texture2D(1800,1050,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,1800,1050),0,0);tex.Apply();File.WriteAllBytes("Logs/stretch-visual-comparison.png",tex.EncodeToPNG());RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(tex);cam.targetTexture=null;rt.Release();UnityEngine.Object.DestroyImmediate(rt);
    C("source_prefab_unchanged",sourceJson==EditorJsonUtility.ToJson(prefab),"");
   }finally{foreach(var go in objects)if(go)UnityEngine.Object.DestroyImmediate(go);}
   File.WriteAllText("Logs/stretch-visual-checks.txt",string.Join("\n",rows)+"\npassed="+passed);return "checks="+rows.Count+" passed="+passed;
  }
 }
}
