using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEditor;
using UnityEngine;
namespace TankTogether.Editor {
public static class StretchTuningChecks {
 static List<string> rows; static bool passed;
 static void Check(string n,bool ok,object v){rows.Add(n+"="+ok+" "+v);passed&=ok;}
 static void Write(string file){File.WriteAllText("Logs/"+file,string.Join("\n",rows)+"\npassed="+passed);}
 public static string EditChecks(){
 rows=new List<string>();passed=true;
 var source=AssetDatabase.LoadAssetAtPath<M0CarryBaseline>("Assets/Resources/WhiteboxJumpTrial.asset");
 string original=JsonUtility.ToJson(source),path="Assets/Resources/StretchTuningIsolatedCheck.asset";
 if(AssetDatabase.LoadAssetAtPath<M0CarryBaseline>(path))throw new Exception("test path occupied");
 var asset=UnityEngine.Object.Instantiate(source);var draft=UnityEngine.Object.Instantiate(source);
 try {
 AssetDatabase.CreateAsset(asset,path);draft.stretchGrowSpeed=.6f;draft.stretchShrinkSpeed=.45f;draft.defaults.cameraDistance=9;
 CarryParametersWindow.SaveDraft(asset,draft);
 Check("saved_fields",asset.stretchGrowSpeed==.6f&&asset.defaults.cameraDistance==9,asset.stretchGrowSpeed);
 draft.defaults.cameraDistance=11;
 Check("save_deep_copy",asset.defaults.cameraDistance==9,asset.defaults.cameraDistance);
 string text=File.ReadAllText(path);Check("disk_serialization",text.Contains("stretchGrowSpeed: 0.6")&&text.Contains("cameraDistance: 9"),text.Length);
 var profile=CarryTuning.Capture(asset,asset.defaults,"test");var imported=UnityEngine.Object.Instantiate(source);
 try {
 CarryTuning.Read(CarryTuning.Parse(JsonUtility.ToJson(profile)),imported,imported.defaults,"test");
 Check("GM_editor_roundtrip",imported.stretchGrowSpeed==.6f&&imported.defaults.cameraDistance==9&&CarryTuning.Hash(CarryTuning.Capture(imported,imported.defaults,"test"))==CarryTuning.Hash(profile),profile.values.Length);
 foreach(int count in new[]{56,53}){
 var old=CarryTuning.Parse(JsonUtility.ToJson(profile));old.values=old.values.Where(x=>!x.key.StartsWith("b.stretch")&&(count==56||!x.key.StartsWith("b.headSupport"))).ToArray();
 CarryTuning.Read(old,imported,imported.defaults,"test");Check("legacy_"+count,imported.stretchGrowSpeed==.3f&&imported.headSupportMode==(count==53?0:asset.headSupportMode),old.values.Length);
 }
 imported.stretchMinHeight=2;Check("invalid_spawn_height",CarryTuning.Validate(imported,imported.defaults)!=null,"");
 imported.stretchMinHeight=.6f;imported.playerDiameter=.8f;Check("invalid_capsule_height",CarryTuning.Validate(imported,imported.defaults)!=null,"");
 }finally{UnityEngine.Object.DestroyImmediate(imported);}
 Check("source_unchanged",JsonUtility.ToJson(source)==original,"");
 }finally{AssetDatabase.DeleteAsset(path);UnityEngine.Object.DestroyImmediate(draft);}
 Write("stretch-tuning-edit.txt");return string.Join("\n",rows);
 }
 public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Run(s));}
 static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
 static IEnumerator Run(SteamCarrySession s){
 rows=new List<string>();passed=true;s.StartLocalTest();var l=s.lab;l.Testing=true;
 var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();
 var profile=CarryTuning.Capture(l);foreach(var v in profile.values){if(v.key=="b.stretchGrowSpeed")v.value=.6f;if(v.key=="b.stretchShrinkSpeed")v.value=.45f;if(v.key=="b.stretchMaxHeight")v.value=2.8f;if(v.key=="b.stretchGripMaxForce")v.value=180;}
 e.Begin(l);e.ManualInput=true;var gm=l.GM;gm.OpenStretch();Check("F2_unified_page",gm.IsOpen,"");
 Check("GM_apply",gm.ImportDraft(JsonUtility.ToJson(profile))&&gm.ApplyDraft()&&e.growRate==.6f&&e.gripMaxForce==180,e.growRate);gm.Close();
 yield return Wait(20);var a=l.Players[0];float start=a.height;e.SetDirection(1);yield return Wait(50);e.SetDirection(0);
 Check("real_grow_speed",Mathf.Abs(a.height-start-.6f*50*Time.fixedDeltaTime)<.025f,a.height-start);
 start=a.height;e.SetDirection(-1);yield return Wait(50);e.SetDirection(0);Check("real_shrink_speed",Mathf.Abs(start-a.height-.45f*50*Time.fixedDeltaTime)<.025f,start-a.height);
 e.BeginNetwork(l);Check("network_keeps_config_and_caps_legacy_height",e.growRate==.6f&&e.shrinkRate==.45f&&e.maxHeight==2.8f&&e.gripMaxForce==180,e.growRate);
 e.NetworkMode=false;var restored=CarryTuning.Capture(l);Check("copy_contains_applied_stretch",restored.values.Length==CarryTuning.All.Length&&restored.values.Single(x=>x.key=="b.stretchGrowSpeed").value==.6f,restored.values.Length);
 s.ReturnToLobby();yield return null;Check("return_restores_defaults",l.baseline.stretchGrowSpeed==.3f&&l.baseline.stretchGripMaxForce==240,l.baseline.stretchGrowSpeed);
 Write("stretch-tuning-runtime.txt");Debug.Log("Stretch tuning checks passed="+passed);
 }
}
}
