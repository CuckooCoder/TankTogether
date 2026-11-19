using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using TankTogether.Networking;
namespace TankTogether.Editor
{
 public static class FormalSettingsChecks
 {
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static T Get<T>(SteamCarrySession s,string n)=>(T)typeof(SteamCarrySession).GetField(n,F).GetValue(s);
    static void Set(SteamCarrySession s,string n,object v)=>typeof(SteamCarrySession).GetField(n,F).SetValue(s,v);
    static object Call(SteamCarrySession s,string n,params object[] a)=>typeof(SteamCarrySession).GetMethod(n,F).Invoke(s,a);
    public static string Result="Not run";
    public static void Run()
    {
        var s=UnityEngine.Object.FindFirstObjectByType<SteamCarrySession>();
        if(!Application.isPlaying||s.Mode!=SteamCarrySession.EntryMode.Home)throw new InvalidOperationException("Run in idle home menu");
        var rows=new List<string>();bool pass=true;void C(string n,bool ok){rows.Add(n+"="+ok);pass&=ok;}
        var data=PlayerPreferences.Data;string before=JsonUtility.ToJson(data);var keyboard=Keyboard.current;Keyboard virtualKeys=null;
        var keyField=typeof(PlayerPreferences).GetField("storageKey",BindingFlags.Static|BindingFlags.NonPublic);string oldKey=(string)keyField.GetValue(null),testKey="TankTogether.SettingsCheck."+Guid.NewGuid().ToString("N");
        try{
            keyField.SetValue(null,testKey);data.keys=PlayerPreferenceData.DefaultKeys();C("default_keys_valid",PlayerPreferences.ValidKeys(data.keys));
            var conflict=(int[])data.keys.Clone();conflict[0]=conflict[1];C("duplicates_rejected",!PlayerPreferences.ValidKeys(conflict));
            C("escape_reserved",!PlayerPreferences.IsBindable((int)Key.Escape));C("mouse_bindable",PlayerPreferences.IsBindable(1001));
            virtualKeys=InputSystem.AddDevice<Keyboard>();virtualKeys.MakeCurrent();
            void Press(params Key[] keys){InputSystem.QueueStateEvent(virtualKeys,new KeyboardState(keys));InputSystem.Update();}
            data.keys[(int)PlayerAction.AgreeReset]=(int)Key.O;
            Press(Key.P);C("old_vote_key_inactive",!PlayerPreferences.Pressed(PlayerAction.AgreeReset));
            Press();Press(Key.O);C("new_vote_key_edge",PlayerPreferences.Pressed(PlayerAction.AgreeReset));
            Press(Key.O);C("held_key_has_no_new_edge",!PlayerPreferences.Pressed(PlayerAction.AgreeReset));Press();
            data.keys[(int)PlayerAction.Forward]=(int)Key.I;Press(Key.W);C("old_move_key_inactive",PlayerPreferences.Move==Vector3.zero);Press(Key.I);C("new_move_key_drives",PlayerPreferences.Move==Vector3.forward);Press();
            PlayerPreferences.Save();C("personal_mapping_persists",JsonUtility.FromJson<PlayerPreferenceData>(PlayerPrefs.GetString(testKey)).keys[(int)PlayerAction.Forward]==(int)Key.I);
            Call(s,"OpenSettings");Set(s,"bindingIndex",0);Set(s,"bindingCandidate",-1);Press(Key.Escape);Call(s,"UpdatePersonalSettings");Call(s,"HandleMenuEscape");
            C("escape_capture_returns_to_settings",Get<bool>(s,"showSettings")&&Get<int>(s,"bindingIndex")==-1);
            Set(s,"oldWidth",Screen.width);Set(s,"oldHeight",Screen.height);Set(s,"oldMode",Screen.fullScreenMode);Set(s,"displayPending",true);Set(s,"displayDeadline",Time.realtimeSinceStartup-1);
            string persisted=PlayerPrefs.GetString(testKey);Call(s,"UpdatePersonalSettings");C("display_timeout_restores",!Get<bool>(s,"displayPending"));C("unconfirmed_display_not_persisted",PlayerPrefs.GetString(testKey)==persisted);
            Set(s,"bindingCancelFrame",-1);Call(s,"HandleMenuEscape");C("escape_settings_returns_to_home",!Get<bool>(s,"showSettings")&&s.Mode==SteamCarrySession.EntryMode.Home);
            Set(s,"exitConfirmation",true);Set(s,"exitSaveFailed",true);Set(s,"discardExitConfirmed",true);Press();Press(Key.Escape);Call(s,"HandleMenuEscape");C("escape_cancels_discard_confirmation",!Get<bool>(s,"exitConfirmation")&&!Get<bool>(s,"exitSaveFailed")&&!Get<bool>(s,"discardExitConfirmed"));
            var duration=typeof(SteamCarrySession).GetMethod("Duration",BindingFlags.Static|BindingFlags.NonPublic);C("duration_over_24h_does_not_wrap",(string)duration.Invoke(null,new object[]{90000d})=="25:00:00");
            data.language=1;C("english_menu_text",GameText.Translate("个人设置")=="Personal settings");
        }catch(Exception e){pass=false;rows.Add(e.ToString());}
        finally{if(virtualKeys!=null)InputSystem.RemoveDevice(virtualKeys);if(keyboard!=null)keyboard.MakeCurrent();JsonUtility.FromJsonOverwrite(before,data);keyField.SetValue(null,oldKey);PlayerPrefs.DeleteKey(testKey);Call(s,"ClosePersonalMenus");PlayerPreferences.ApplyNonDisplay();}
        Result=string.Join("\n",rows)+"\npassed="+pass;File.WriteAllText("Logs/formal-settings-checks.txt",Result);Debug.Log(Result);
    }
 }
}
