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
 public static class WhiteboxLiftChecks {
  [Serializable] public class Evidence {public bool complete,passed;public string scope="isolated-editor-physics-not-full-route-or-remote";public List<M0CarryChecks.Check> checks=new List<M0CarryChecks.Check>();}
  public static Evidence Last;
  static void C(bool ok,string name,object detail){Last.checks.Add(new M0CarryChecks.Check{passed=ok,name=name,evidence=detail.ToString()});}
  static IEnumerator Steps(int n){for(int i=0;i<n;i++)yield return new WaitForFixedUpdate();}
  [MenuItem("Tank Together/Whitebox/Check Revision Lift")]
  public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();if(!EditorApplication.isPlaying||!s||s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Play idle formal lobby first");s.StartCoroutine(Run(s));}
  public static Vector3 OperatorPoint(WhiteboxLift lift){var b=lift.SwitchCollider.bounds;return new Vector3(b.center.x,lift.platformCollider.bounds.max.y+.02f,b.min.z-.7f);}
  static IEnumerator Run(SteamCarrySession s){
   Last=new Evidence();s.StartLocalTest();var lab=s.lab;lab.Testing=true;var lift=lab.GetComponent<WhiteboxLift>();
   lab.SetInput(0,Vector3.zero,false);lab.PlacePlayer(0,OperatorPoint(lift));lab.Tank.isKinematic=true;
   C(lift.SwitchCollider&&lift.SwitchCollider.transform.IsChildOf(lift.platform.transform),"authored_switch_moves_with_platform",lift.SwitchCollider);
   yield return Steps(120);C(lift.Distance==0&&!lift.Moving,"never_auto_starts",lift.Distance);
   C(lift.NearSwitch(0),"near_authored_switch",lab.Players[0].body.position);
   lab.SetInput(0,Vector3.zero,true);C(!lift.TryInteract(0,out _),"held_grab_rejected","");lab.SetInput(0,Vector3.zero,false);
   var near=lab.Players[0].body.position;lab.PlacePlayer(0,near+Vector3.right*10);C(!lift.TryInteract(0,out _),"far_rejected","");lab.PlacePlayer(0,near);
   C(lift.TryInteract(0,out _)&&lift.GoingUp&&lift.Moving,"one_operator_no_tank_required","");
   C(!lift.TryInteract(0,out _)&&lift.GoingUp,"same_step_double_toggle_rejected","");
   yield return Steps(20);C(lift.Distance>.9f&&lift.Distance<1.1f,"ascent_speed",lift.Distance);
   lab.PlacePlayer(0,OperatorPoint(lift));C(lift.TryInteract(0,out _)&&!lift.GoingUp,"press_again_reverses","");
   yield return Steps(22);C(lift.Distance==0&&!lift.Moving,"stops_at_bottom",lift.Distance);
   lab.PlacePlayer(0,OperatorPoint(lift));lab.Simulate=false;C(!lift.TryInteract(0,out _),"proxy_cannot_authorize","");
   lift.ApplyProxy(50,true,false);yield return Steps(3);C(lift.Distance==50&&!lift.GoingUp,"proxy_descending_snapshot",lift.Distance);lab.Simulate=true;lift.ResetLift(false);
   lab.PlacePlayer(0,OperatorPoint(lift));lab.SetSessionPaused(true);C(!lift.TryInteract(0,out _),"pause_rejects","");lab.SetSessionPaused(false);
   lift.ResetLift(true);lab.PlacePlayer(0,OperatorPoint(lift));lab.SetInput(0,Vector3.zero,false);C(lift.TryInteract(0,out _)&&!lift.GoingUp,"top_checkpoint_can_descend","");
   float d=lift.Distance;yield return Steps(10);C(lift.Distance<d,"actually_descends",lift.Distance);
   lab.SetSessionPaused(true);d=lift.Distance;yield return Steps(5);C(lift.Distance==d,"pause_freezes_motion",d);lab.SetSessionPaused(false);
   lab.ResetLab();C(lift.Distance==0&&!lift.Moving&&!lift.GoingUp,"reset_clears_direction","");
   lab.PlacePlayer(0,OperatorPoint(lift));lab.SetProxyState(true,"test");C(!lift.TryInteract(0,out _),"failure_rejects","");lab.SetProxyState(false,"");
   var input=new SteamCarryProtocol.Input{round=1,sequence=1,interact=true};var decoded=SteamCarryProtocol.DecodeInput(SteamCarryProtocol.Encode(input));C(decoded.interact,"interaction_input_roundtrip","");
   var snap=new SteamCarryProtocol.Snapshot{round=1,rotation=Quaternion.identity,liftDistance=14,liftMoving=true,liftGoingUp=false};for(int i=0;i<4;i++)snap.actors[i].height=1.7f;
   var decodedState=SteamCarryProtocol.DecodeSnapshot(SteamCarryProtocol.Encode(snap));C(decodedState.liftMoving&&!decodedState.liftGoingUp&&decodedState.liftDistance==14,"direction_snapshot_roundtrip","");
   var old=new PlayerPreferenceData{version=1,keys=PlayerPreferenceData.DefaultKeys().Take(11).ToArray()};old.keys[8]=(int)UnityEngine.InputSystem.Key.Q;old.keys[9]=(int)UnityEngine.InputSystem.Key.E;PlayerPreferences.Migrate(old);
   C(old.keys[8]==(int)UnityEngine.InputSystem.Key.E&&old.keys[9]==(int)UnityEngine.InputSystem.Key.Q&&PlayerPreferences.ValidKeys(old.keys),"old_default_keys_migrate","");
   var custom=new PlayerPreferenceData{version=1,keys=PlayerPreferenceData.DefaultKeys().Take(11).ToArray()};custom.keys[8]=(int)UnityEngine.InputSystem.Key.F;custom.keys[9]=(int)UnityEngine.InputSystem.Key.Z;PlayerPreferences.Migrate(custom);
   C(custom.keys[8]==(int)UnityEngine.InputSystem.Key.F&&custom.keys[9]==(int)UnityEngine.InputSystem.Key.Z&&PlayerPreferences.ValidKeys(custom.keys),"custom_keys_preserved","");
   Last.complete=true;Last.passed=Last.checks.All(c=>c.passed);Directory.CreateDirectory("Logs/feedback-20261017");File.WriteAllText("Logs/feedback-20261017/lift-switch.json",JsonUtility.ToJson(Last,true));s.ReturnToLobby();Debug.Log("Lift switch checks "+Last.passed+" count="+Last.checks.Count);
  }
 }
}
