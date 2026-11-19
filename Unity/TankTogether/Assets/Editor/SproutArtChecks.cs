using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
 public static class SproutArtChecks {
  public static string Last="not run";
  static readonly List<string> rows=new List<string>();static bool passed;
  static void Check(string name,bool ok,object detail){passed&=ok;rows.Add(name+"="+ok+" "+detail);}
  static IEnumerator Wait(float seconds){yield return new WaitForSeconds(seconds);}
  static IEnumerator Capture(Camera c,Vector3 center,float height,string name){c.orthographicSize=height*.65f;c.transform.position=center+new Vector3(2,.3f,5);c.transform.LookAt(center);var rt=RenderTexture.GetTemporary(800,800,24);var old=RenderTexture.active;var tex=new Texture2D(800,800,TextureFormat.RGB24,false);try{c.targetTexture=rt;c.enabled=true;yield return new WaitForEndOfFrame();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,800,800),0,0);tex.Apply();File.WriteAllBytes("Logs/sprouts/"+name+".png",tex.EncodeToPNG());}finally{c.enabled=false;c.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(tex);}}
  public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
  static IEnumerator Run(SteamCarrySession s){
   rows.Clear();passed=true;Last="running";Directory.CreateDirectory("Logs/sprouts");
   s.StartLocalTest();s.enabled=false;var l=s.lab;l.Testing=true;bool debug=l.ShowHeadSupport;l.ShowHeadSupport=false;
   var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Sprout test floor";floor.transform.position=new Vector3(1000,-.5f,0);floor.transform.localScale=new Vector3(200,1,200);
   var go=new GameObject("Sprout test camera");var c=go.AddComponent<Camera>();c.enabled=false;c.orthographic=true;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=new Color(.08f,.12f,.15f);
   var exp=l.GetComponent<HeightExperiment>();if(!exp)exp=l.gameObject.AddComponent<HeightExperiment>();exp.Begin(l,true);exp.ManualInput=true;
   for(int i=0;i<l.LocalPlayerCount;i++)l.PlacePlayer(i,new Vector3(997+i*2,.02f,0));
   var a=l.Players[0];var profile=a.visual.GetComponent<CarryVisualProfile>();var anim=a.presentation.Animation.Animator;
   try {
    Check("new_profile_and_controller",profile&&anim.runtimeAnimatorController==profile.controller,anim.runtimeAnimatorController.name);
    Check("four_unique_materials",profile.colors.Length==4&&profile.colors.Distinct().Count()==4,string.Join(",",profile.colors.Select(m=>m.name)));
    Check("textures_and_shaders",profile.colors.All(m=>m.mainTexture&&m.shader.isSupported),"four imported art materials");
    foreach(var name in new[]{"Idle","Walk","Run","Takeoff","Rise","Apex","Fall","LandSoft","LandHard"}){var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(SproutArtSetup.Folder+"/"+name+".anim");var bindings=AnimationUtility.GetCurveBindings(clip);Check(name+"_bindings",bindings.Length>100&&bindings.All(b=>a.visual.Find(b.path)),bindings.Length);}
    yield return Wait(.7f);yield return Capture(c,new Vector3(999,.85f,0),6.4f,"three-players");
    for(int i=0;i<l.LocalPlayerCount;i++)yield return Capture(c,l.Players[i].body.position+Vector3.up*.85f,1.7f,"color-"+i);
    foreach(bool run in new[]{false,true}){l.SetInput(0,Vector3.forward,false,false,false,run);yield return Wait(.7f);Check(run?"run_speed":"walk_speed",Mathf.Abs(a.body.linearVelocity.z-(run?6:4))<.1f,a.body.linearVelocity.z);for(int i=0;i<4;i++){yield return Wait(.1f);yield return Capture(c,a.body.position+Vector3.up*.85f,1.7f,(run?"run":"walk")+"-"+i);}}
    l.SetInput(0,Vector3.zero,false);yield return Wait(.8f);Check("returns_to_idle",anim.GetFloat("Speed")<.1f,anim.GetFloat("Speed"));
    l.SetInput(0,Vector3.forward,false,true);var states=new HashSet<CarryMotionState>();float end=Time.time+1.4f;int shot=0;float next=Time.time+.12f;
    while(Time.time<end){yield return new WaitForEndOfFrame();states.Add(a.presentation.Animation.State);if(Time.time>next&&shot<5){yield return Capture(c,a.body.position+Vector3.up*.85f,1.7f,"jump-"+shot++);next=Time.time+.15f;}}
    l.SetInput(0,Vector3.zero,false);Check("jump_air_and_land",states.Contains(CarryMotionState.Rise)&&states.Contains(CarryMotionState.Fall)&&states.Contains(CarryMotionState.Land),string.Join(",",states));
    var bones=a.visual.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Distinct().ToDictionary(t=>t.name);float arm=Vector3.Distance(bones["UpperArm.L"].position,bones["Forearm.L"].position);
    foreach(float h in new[]{1.1f,5f,1.7f}){a.height=h;a.capsule.height=h;a.capsule.center=Vector3.up*h/2;l.SetInput(0,Vector3.zero,false);yield return Wait(.8f);yield return new WaitForEndOfFrame();Check("stretch_"+h,a.presentation.StretchVisual.Ready&&Mathf.Abs(a.presentation.StretchVisual.AppliedExtension-(h-1.7f))<.01f&&Mathf.Abs(Vector3.Distance(bones["UpperArm.L"].position,bones["Forearm.L"].position)-arm)<.002f,a.presentation.StretchVisual.AppliedExtension);yield return Capture(c,a.body.position+Vector3.up*h*.5f,h,"height-"+h);}
    l.Tank.isKinematic=true;l.Tank.position=a.body.position+Vector3.up*(a.height+l.tankSize.y/2);l.Tank.rotation=Quaternion.identity;yield return Wait(.15f);l.SetInput(0,Vector3.zero,true);yield return Wait(.7f);yield return new WaitForEndOfFrame();Check("grip_ik",a.joint&&bones["Hand.L"].position.y>bones["UpperArm.L"].position.y&&bones["Hand.R"].position.y>bones["UpperArm.R"].position.y,"grip="+(bool)a.joint);yield return Capture(c,a.body.position+Vector3.up*1.3f,2.5f,"grip");
    // Same entry point that host/peer snapshots and preview use, without touching saved preferences.
    var apply=typeof(SteamCarrySession).GetMethod("ApplyCosmetic",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);apply.Invoke(s,new object[]{a.visual.gameObject,3});Check("session_cosmetic",a.visual.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial==profile.colors[3],"slot3 coral");profile.ApplyColor(0);
    Last="passed="+passed;File.WriteAllText("Logs/sprouts/checks.txt",string.Join("\n",rows)+"\n"+Last+"\nscope=local runtime fixture and shared cosmetic entry, no real remote/full-route acceptance");
   } finally {l.SetInput(0,Vector3.zero,false);l.Tank.isKinematic=false;l.ShowHeadSupport=debug;UnityEngine.Object.Destroy(floor);UnityEngine.Object.Destroy(go);s.enabled=true;s.ReturnToLobby();}
  }
 }
}
