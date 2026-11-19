using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using TankTogether.M0;
using TankTogether.Networking;
namespace TankTogether.Editor {
public static class FeedbackTuningChecks {
 public static void Begin(){var s=Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
 static IEnumerator Run(SteamCarrySession s){
 var rows=new List<string>();bool pass=true;void C(string n,bool ok,object detail){pass&=ok;rows.Add(n+"="+ok+" "+detail);}
 s.StartLocalTest();var l=s.lab;l.Testing=true;string before=JsonUtility.ToJson(l.baseline);var old=CarryTuning.Capture(l);
 var copy=Object.Instantiate(l.baseline);copy.freeJumpHeight=.8f;copy.freeFallGravityMultiplier=4;copy.stretchGrowSpeed=copy.stretchShrinkSpeed=.5f;
 C("editor_accepts_planner_values",CarryParametersWindow.Validate(copy)==null,CarryParametersWindow.Validate(copy)??"");
 C("GM_accepts_planner_values",CarryTuning.Validate(copy,l.Tuning)==null,CarryTuning.Validate(copy,l.Tuning)??"");
 var profile=CarryTuning.Capture(copy,l.Tuning,s.LevelId);CarryTuning.Apply(l,CarryTuning.Parse(JsonUtility.ToJson(profile)));
 C("copy_import_roundtrip",l.baseline.freeFallGravityMultiplier==4&&l.baseline.stretchGrowSpeed==.5f&&l.baseline.freeJumpHeight==.8f,"");
 l.PlacePlayer(0,new Vector3(1000,20,0));l.SetInput(0,Vector3.zero,false);var a=l.Players[0];a.body.linearVelocity=Vector3.down;
 yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();float v=a.body.linearVelocity.y;
 for(int i=0;i<5;i++)yield return new WaitForFixedUpdate();float acceleration=(a.body.linearVelocity.y-v)/(5*Time.fixedDeltaTime);
 C("runtime_four_times_gravity",Mathf.Abs(acceleration-Physics.gravity.y*4)<.5f,acceleration);
 copy.freeFallGravityMultiplier=4.1f;C("editor_rejects_above_four",CarryParametersWindow.Validate(copy)!=null,"");C("GM_rejects_above_four",CarryTuning.Validate(copy,l.Tuning)!=null,"");
 CarryTuning.Apply(l,old);C("test_restores_original_values",JsonUtility.ToJson(l.baseline)==before,"");Object.Destroy(copy);s.ReturnToLobby();
 File.WriteAllText("Logs/feedback-20261017/tuning.txt",string.Join("\n",rows)+"\npassed="+pass);
 }
}
}
