using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TankTogether.M0;
using TankTogether.Networking;
using UnityEngine;
namespace TankTogether.Editor {
 public static class SinglePedestalChecks {
  static readonly List<string> rows=new List<string>(); static bool passed;
  static void C(string name,bool ok,object value){rows.Add(name+"="+ok+" "+value);passed &= ok;}
  static void Picture(M0CarryLab l,string file) {
   var go=new GameObject("Single pedestal evidence camera");var camera=go.AddComponent<Camera>();camera.CopyFrom(Camera.main);
   var target=l.Tank.position-Vector3.up*.6f;go.transform.position=target+new Vector3(6,3,6);go.transform.LookAt(target);
   var rt=new RenderTexture(1280,720,24);camera.targetTexture=rt;camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;
   var image=new Texture2D(1280,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(file,image.EncodeToPNG());
   RenderTexture.active=old;camera.targetTexture=null;UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(rt);UnityEngine.Object.Destroy(go);
  }
  static IEnumerator Wait(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
  public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.ReturnToLobby();s.StartCoroutine(Run(s));}
  static IEnumerator Run(SteamCarrySession s){
   rows.Clear();passed=true;s.StartLocalTest();s.enabled=false;var l=s.lab;l.Testing=true;
   var route=l.GetComponent<WhiteboxCarryLevel>();var persist=route.PersistCheckpoint;
   var e=l.GetComponent<HeightExperiment>();if(!e)e=l.gameObject.AddComponent<HeightExperiment>();e.Begin(l,true);e.ManualInput=true;
   route.SelectCheckpoint("start");l.ResetLab();yield return Wait(100);
   C("initial_tank_safe_single_post",!l.Broken,l.Message);
   C("initial_bottom_aligns_model",Mathf.Abs(l.Tank.position.y-l.tankSize.y/2+27.8f)<.04f,l.Tank.position);
   C("no_generated_four_posts",UnityEngine.Object.FindObjectsByType<M1SupportPost>(FindObjectsSortMode.None).Length==0,0);
   C("single_authored_safe_collider",GameObject.Find("Authored central pedestal safe contact")!=null,route.useCheckpointZones);
   C("no_floor_repair",GameObject.Find("Formal V1 reference pedestal floor repair")==null,"");
   foreach(string name in new[]{"Group 33","Group 35"}){var marker=route.levelRoot.GetComponentsInChildren<Transform>(true).Single(x=>x.name==name);C("yellow_visible_nonblocking_"+name,marker.gameObject.activeInHierarchy&&marker.GetComponentsInChildren<Collider>().All(c=>!c.enabled),marker.position);}
   var spawn=route.checkpoints[0].tankFeet;
   for(int i=0;i<4;i++){l.PlacePlayer(i,spawn+new Vector3(i%2==0?-.9f:.9f,.02f,i<2?-.55f:.55f));l.SetInput(i,Vector3.zero,false);}
   yield return Wait(15);for(int i=0;i<4;i++)l.SetInput(i,Vector3.zero,true);yield return Wait(10);
   C("can_grip_around_central_post",Enumerable.Range(0,4).All(l.IsGripping)&&!l.Broken,Enumerable.Range(0,4).Count(l.IsGripping));
   Picture(l,"Logs/single-post-grip.png");
   for(int i=0;i<4;i++){l.SetInput(i,Vector3.zero,false);l.PlacePlayer(i,route.checkpoints.Single(c=>c.id=="upper").playerFeet+Vector3.right*(i+3));}
   yield return Wait(10);C("players_only_do_not_save",route.CheckpointId=="pickup",route.CheckpointId);
   l.Tank.isKinematic=true;
   var upper=route.checkpoints.Single(c=>c.id=="upper").parkingFeet;
   l.Tank.position=upper+new Vector3(0,10,0);Physics.SyncTransforms();yield return Wait(3);C("too_high_does_not_save",route.CheckpointId=="pickup",route.CheckpointId);
   l.Tank.position=upper+new Vector3(3,2.2f,0);Physics.SyncTransforms();yield return Wait(3);C("outside_radius_does_not_save",route.CheckpointId=="pickup",route.CheckpointId);
   l.Simulate=false;l.Tank.position=upper+Vector3.up*2.2f;yield return Wait(3);C("client_proxy_cannot_save",route.CheckpointId=="pickup",route.CheckpointId);l.Simulate=true;
   int writes=0;route.PersistCheckpoint=id=>{writes++;return false;};l.DynamicRoster=true;
   yield return Wait(3);C("save_failure_keeps_checkpoint",writes>0&&route.CheckpointId=="pickup",writes);
   string root=Path.GetFullPath("Logs/single-post-save-"+Guid.NewGuid().ToString("N"));
   Func<CoopCampaignSave.Progress,bool> valid=p=>route.HasCheckpoint(p.checkpointId);
   var created=CoopCampaignSave.Create(123,"slot1",route.levelId,"start","single-post-test",valid,root);
   C("isolated_save_created",created.success,created.status);
   writes=0;route.PersistCheckpoint=id=>{writes++;var next=CoopCampaignSave.Copy(created.progress);next.checkpointId=id;return CoopCampaignSave.Save(next,valid,root).success;};yield return Wait(3);
   var loaded=CoopCampaignSave.Load(123,"slot1","single-post-test",valid,root);
   C("disk_saved_upper",loaded.success&&loaded.progress.checkpointId=="upper",loaded.status);
   C("intact_tank_arrival_saves",route.CheckpointId=="upper"&&writes==1&&!l.Broken,route.CheckpointId+" writes="+writes);
   yield return Wait(5);C("arrival_only_once",writes==1,writes);
   C("not_final",!route.FinalStationIsTestFinish,"");
   for(int count=2;count<=4;count++){
    l.ConfigureSessionRoster(new[]{true,true,count>=3,count>=4},true);l.SessionTeamSize=count;l.SetSessionPaused(false);l.ResetLab();yield return Wait(150);
    C("checkpoint_respawn_safe_"+count,!l.Broken&&route.CheckpointId=="upper",l.Message+" tilt="+Vector3.Angle(l.Tank.transform.up,Vector3.up));
    C("respawn_above_heads_"+count,Mathf.Abs(l.Tank.position.y-upper.y-l.standingHeight-l.tankSize.y/2)<.25f,l.Tank.position);
    C("respawn_no_reactivation_"+count,writes==1,writes);
    if(count==2)Picture(l,"Logs/single-post-upper-no-columns.png");
    C("respawn_no_hidden_posts_"+count,UnityEngine.Object.FindObjectsByType<M1SupportPost>(FindObjectsSortMode.None).Length==0,0);
    for(int i=0;i<count;i++)l.SetInput(i,Vector3.zero,false);yield return Wait(2);for(int i=0;i<count;i++)l.SetInput(i,Vector3.zero,true);yield return Wait(8);
    C("respawn_grab_"+count,Enumerable.Range(0,count).All(l.IsGripping)&&!l.Broken,Enumerable.Range(0,count).Count(l.IsGripping));
   }
   l.DynamicRoster=false;route.SelectCheckpoint("start");l.ResetLab();yield return Wait(5);
   l.Tank.isKinematic=false;l.Tank.position=upper+Vector3.up*2.2f;
   var hazard=GameObject.CreatePrimitive(PrimitiveType.Cube);hazard.name="Isolated same-step dangerous collision";hazard.transform.position=l.Tank.position;hazard.transform.localScale=Vector3.one;hazard.AddComponent<M0Danger>();Physics.SyncTransforms();yield return Wait(3);UnityEngine.Object.Destroy(hazard);
   C("danger_priority",route.CheckpointId=="start"&&l.Broken,route.CheckpointId);
   route.PersistCheckpoint=persist;route.SelectCheckpoint("start");s.enabled=true;s.ReturnToLobby();
   rows.Add("passed="+passed);rows.Add("Scope: local fixture and callback persistence; not remote multiplayer or continuous route.");
   File.WriteAllText("Logs/single-pedestal-checks.txt",string.Join("\n",rows));Debug.Log("Single pedestal checks passed="+passed);
  }
 }
}
