using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using TankTogether.Networking;
using TankTogether.M0;
namespace TankTogether.Editor {
public static class FeedbackInteractionChecks {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static T Get<T>(SteamCarrySession s,string n)=>(T)typeof(SteamCarrySession).GetField(n,F).GetValue(s);
 static void Set(SteamCarrySession s,string n,object v)=>typeof(SteamCarrySession).GetField(n,F).SetValue(s,v);
 static void Call(SteamCarrySession s,string n,params object[] args)=>typeof(SteamCarrySession).GetMethod(n,F).Invoke(s,args);
 public static void Begin(){var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();s.StartCoroutine(Run(s));}
 static IEnumerator Run(SteamCarrySession s){
 var rows=new List<string>();bool pass=true;void C(string name,bool ok){pass&=ok;rows.Add(name+"="+ok);}
 var oldKeys=Keyboard.current;Keyboard keys=null;var preferences=PlayerPreferences.Data;string oldPrefs=JsonUtility.ToJson(preferences);string oldSave=s.EditorSaveRoot;
 s.StartLocalTest();s.lab.Testing=true;var l=s.lab;var lift=l.GetComponent<WhiteboxLift>();
 try {
  preferences.keys=PlayerPreferenceData.DefaultKeys();keys=InputSystem.AddDevice<Keyboard>();keys.MakeCurrent();
  l.Tank.isKinematic=true;l.PlacePlayer(0,WhiteboxLiftChecks.OperatorPoint(lift));l.SetInput(0,Vector3.zero,false);
  InputSystem.QueueStateEvent(keys,new KeyboardState());InputSystem.Update();
  typeof(M0CarryLab).GetMethod("SetCaptured",F).Invoke(l,new object[]{true});
  InputSystem.QueueStateEvent(keys,new KeyboardState(Key.F));InputSystem.Update();
  l.Testing=false;typeof(M0CarryLab).GetMethod("Update",F).Invoke(l,null);l.Testing=true;
  C("actual_local_F_press_starts",lift.Moving&&lift.GoingUp);
  InputSystem.QueueStateEvent(keys,new KeyboardState(Key.F));InputSystem.Update();
  C("held_F_not_repeated",!PlayerPreferences.Pressed(PlayerAction.Interact));
  InputSystem.QueueStateEvent(keys,new KeyboardState(Key.Q));InputSystem.Update();C("Q_shrink",PlayerPreferences.Held(PlayerAction.Shrink)&&!PlayerPreferences.Held(PlayerAction.Grow));
  InputSystem.QueueStateEvent(keys,new KeyboardState(Key.E));InputSystem.Update();C("E_grow",PlayerPreferences.Held(PlayerAction.Grow)&&!PlayerPreferences.Held(PlayerAction.Shrink));
 } finally {if(keys!=null)InputSystem.RemoveDevice(keys);if(oldKeys!=null)oldKeys.MakeCurrent();JsonUtility.FromJsonOverwrite(oldPrefs,preferences);s.ReturnToLobby();}
 float end=Time.unscaledTime+8;while(!s.CanChooseMode&&Time.unscaledTime<end)yield return null;
 s.EditorSaveRoot=Path.GetFullPath("Logs/feedback-20261017/network-save-"+Guid.NewGuid().ToString("N"));s.OpenSteamMenu();s.CreateRoom();end=Time.unscaledTime+25;while(!s.Host&&Time.unscaledTime<end)yield return null;
 C("actual_Steam_host",s.Host);
 if(s.Host)try {
  const ulong peer=999999;
  Get<SessionRoster>(s,"roster").Admit(123456789,peer,out _,out _);
  Get<Dictionary<ulong,int>>(s,"slots")[peer]=1;Get<bool[]>(s,"connected")[1]=true;Get<bool[]>(s,"peerReady")[1]=true;Get<bool[]>(s,"configReady")[1]=true;
  var ready=Get<RoomReadyState>(s,"playerReady");ready.SetConnected(1,true);
  // Direct host phase setup isolates transport handling; it is not a second remote account.
  Set(s,"phase",SteamCarryProtocol.Phase.Playing);l.ConfigureSessionRoster(new[]{true,true,false,false},true);l.SetSessionPaused(false);l.Testing=true;l.Tank.isKinematic=true;
  lift.ResetLift(false);l.PlacePlayer(1,WhiteboxLiftChecks.OperatorPoint(lift));l.SetInput(1,Vector3.zero,false);
  int seq=1;int round=Get<int>(s,"round");
  void Deliver(ulong id,bool interact,int cfg,int sequence){var cmd=new SteamCarryProtocol.Input{configRevision=cfg,round=round,sequence=sequence,interact=interact};var bytes=SteamCarryProtocol.Encode(cmd);using var writer=new Unity.Netcode.FastBufferWriter(bytes.Length,Allocator.Temp);writer.WriteBytesSafe(bytes);using var reader=new Unity.Netcode.FastBufferReader(writer,Allocator.Temp);Call(s,"ReceiveInput",id,reader);Call(s,"FixedUpdate");}
  Deliver(peer+1,true,s.ConfigRevision,seq);C("unknown_sender_rejected",!lift.Moving);
  Deliver(peer,true,s.ConfigRevision-1,seq);C("stale_configuration_rejected",!lift.Moving);
  Deliver(peer,true,s.ConfigRevision,seq++);C("remote_handler_starts_lift",lift.GoingUp&&lift.Moving);
  lift.ResetLift(false);Deliver(peer,true,s.ConfigRevision,seq-1);C("replayed_press_rejected",!lift.Moving);
  l.PlacePlayer(1,WhiteboxLiftChecks.OperatorPoint(lift)+Vector3.right*10);Deliver(peer,true,s.ConfigRevision,seq++);C("remote_distance_checked_by_host",!lift.Moving);
  l.PlacePlayer(1,WhiteboxLiftChecks.OperatorPoint(lift));Set(s,"phase",SteamCarryProtocol.Phase.Waiting);Deliver(peer,true,s.ConfigRevision,seq++);C("lobby_cannot_operate",!lift.Moving);
 } catch(Exception e){pass=false;rows.Add(e.ToString());}
 s.ReturnToLobby();s.EditorSaveRoot=oldSave;
 File.WriteAllText("Logs/feedback-20261017/input-network.txt",string.Join("\n",rows)+"\npassed="+pass+"\nscope=actual keyboard events and one Steam host with injected peer messages; no remote multiplayer");
 }
}
}
