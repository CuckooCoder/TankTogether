using System;
using UnityEngine;
using UnityEngine.InputSystem;
namespace TankTogether.Networking
{
    public sealed partial class SteamCarrySession
    {
        bool showSettings,restoreKeysConfirm,displayPending,textInputFocused;
        int bindingIndex=-1,bindingCandidate=-1,displayChoice=0,windowChoice=2;
        int bindingCancelFrame=-1;
        int oldWidth,oldHeight;FullScreenMode oldMode;float displayDeadline;
        string settingsMessage="";
        static readonly string[] ActionLabels={"前进","后退","左移","右移","奔跑","跳跃","蹲下（A 模式）","长按抓取","长高","变矮","同意复位投票","机关交互"};
        static readonly int[] DisplayWidths={1280,1600,1920},DisplayHeights={720,900,1080};
        string ControlHint()
        {
            string K(PlayerAction a)=>PlayerPreferences.KeyName(PlayerPreferences.Data.keys[(int)a]);
            return K(PlayerAction.Forward)+"/"+K(PlayerAction.Backward)+"/"+K(PlayerAction.Left)+"/"+K(PlayerAction.Right)+" "+GameText.Translate("移动")+" · "+K(PlayerAction.Grab)+" "+GameText.Translate("长按抓取")+" · "+K(PlayerAction.Jump)+" "+GameText.Translate("跳跃")+" · "+K(PlayerAction.Run)+" "+GameText.Translate("奔跑")+(stretchNetwork?" · "+K(PlayerAction.Grow)+"/"+K(PlayerAction.Shrink)+" "+GameText.Translate("长高/变矮"):" · "+K(PlayerAction.Crouch)+" "+GameText.Translate("蹲下（A 模式）"));
        }
        void HandleMenuEscape()
        {
            if(Keyboard.current==null||!Keyboard.current.escapeKey.wasPressedThisFrame||bindingCancelFrame==Time.frameCount)return;
            if(showSettings){if(displayPending)RestoreDisplay();else if(bindingIndex<0)showSettings=false;}
            else if(exitConfirmation)exitConfirmation=exitSaveFailed=discardExitConfirmed=false;
            else if(showCosmetic){showCosmetic=false;DestroyPreview();}
            else if(showSaveMenu)CloseSaveMenu();
            else if(kickCandidate>=0)kickCandidate=-1;
            else if(confirmReplay)confirmReplay=false;
            else if(Mode==EntryMode.Steam){if(!Running)ReturnToLobby();else if(IsPlaying)Capture(!captured);else Capture(false);}
        }
        void OpenSettings(){Capture(false);lab.ReleaseLocalControl();showSettings=true;bindingIndex=bindingCandidate=-1;windowChoice=PlayerPreferences.Data.windowMode;displayChoice=Mathf.Max(0,Array.IndexOf(DisplayWidths,PlayerPreferences.Data.width));}
        void UpdatePersonalSettings()
        {
            if(displayPending&&Time.realtimeSinceStartup>=displayDeadline)RestoreDisplay();
            if(!showSettings||bindingIndex<0||bindingCandidate>=0)return;
            if(Keyboard.current!=null){foreach(var key in Keyboard.current.allKeys)if(key.wasPressedThisFrame){if(key.keyCode==Key.Escape){bindingIndex=-1;bindingCancelFrame=Time.frameCount;return;}bindingCandidate=(int)key.keyCode;break;}}
            var mouse=Mouse.current;if(mouse!=null){if(mouse.leftButton.wasPressedThisFrame)bindingCandidate=1000;else if(mouse.rightButton.wasPressedThisFrame)bindingCandidate=1001;else if(mouse.middleButton.wasPressedThisFrame)bindingCandidate=1002;}
        }
        void RestoreDisplay(){Screen.SetResolution(oldWidth,oldHeight,oldMode);displayPending=false;settingsMessage="显示设置已还原。";}
        void DrawSettings()
        {
            Panel(new Rect(100,40,1080,650));GameText.Label(new Rect(130,57,1020,40),"个人设置",heading);
            if(displayPending){
                GameText.Label(new Rect(250,220,780,60),"保留当前显示设置？ "+Mathf.CeilToInt(displayDeadline-Time.realtimeSinceStartup)+" 秒后自动还原",label);
                if(GameText.Button(new Rect(250,310,360,45),"保留",button)){var d=PlayerPreferences.Data;d.width=DisplayWidths[displayChoice];d.height=DisplayHeights[displayChoice];d.windowMode=windowChoice;PlayerPreferences.Save();displayPending=false;}
                if(GameText.Button(new Rect(650,310,360,45),"还原",button))RestoreDisplay();return;
            }
            var data=PlayerPreferences.Data;
            GameText.Label(new Rect(130,110,230,30),"语言 / Language",label);
            int language=GameText.Selection(new Rect(355,110,230,34),data.language,new[]{"简体中文","English"},2,button);if(language!=data.language){data.language=language;PlayerPreferences.Save();}
            GameText.Label(new Rect(130,165,220,30),"画质",label);int quality=GameText.Selection(new Rect(355,165,230,34),data.quality,new[]{"低","中","高"},3,button);if(quality!=data.quality){data.quality=quality;PlayerPreferences.ApplyNonDisplay();PlayerPreferences.Save();}
            GameText.Label(new Rect(130,215,220,30),"主音量 "+Mathf.RoundToInt(data.volume*100)+"%",label);float volume=GUI.HorizontalSlider(new Rect(355,226,230,20),data.volume,0,1);if(volume!=data.volume){data.volume=volume;AudioListener.volume=volume;PlayerPreferences.Save();}
            GameText.Label(new Rect(130,265,220,30),"鼠标灵敏度 "+data.sensitivity.ToString("F1"),label);float sensitivity=GUI.HorizontalSlider(new Rect(355,276,230,20),data.sensitivity,.2f,3);if(sensitivity!=data.sensitivity){data.sensitivity=sensitivity;PlayerPreferences.Save();}
            bool invert=GameText.Toggle(new Rect(130,310,230,30),data.invertY,"反转镜头上下");bool sync=GameText.Toggle(new Rect(355,310,230,30),data.vsync,"垂直同步");if(invert!=data.invertY||sync!=data.vsync){data.invertY=invert;data.vsync=sync;PlayerPreferences.ApplyNonDisplay();PlayerPreferences.Save();}
            GameText.Label(new Rect(130,355,450,28),"显示模式 / 分辨率（应用后需确认）",small);
            windowChoice=GameText.Selection(new Rect(130,393,455,34),windowChoice,new[]{"全屏","无边框","窗口"},3,button);
            displayChoice=GameText.Selection(new Rect(130,440,455,34),displayChoice,new[]{"1280×720","1600×900","1920×1080"},3,button);
            if(GameText.Button(new Rect(130,490,455,38),"应用显示设置",button)){
                oldWidth=Screen.width;oldHeight=Screen.height;oldMode=Screen.fullScreenMode;
                Screen.SetResolution(DisplayWidths[displayChoice],DisplayHeights[displayChoice],windowChoice==0?FullScreenMode.ExclusiveFullScreen:windowChoice==1?FullScreenMode.FullScreenWindow:FullScreenMode.Windowed);displayPending=true;displayDeadline=Time.realtimeSinceStartup+15;
            }
            for(int i=0;i<ActionLabels.Length;i++){
                float y=110+i*35;GameText.Label(new Rect(640,y,260,30),ActionLabels[i],label);
                if(GameText.Button(new Rect(910,y,230,32),bindingIndex==i?"按键…":PlayerPreferences.KeyName(data.keys[i]),button)){bindingIndex=i;bindingCandidate=-1;settingsMessage="按新键；Esc 取消。";}
            }
            if(bindingIndex>=0&&bindingCandidate>=0){
                var trial=(int[])data.keys.Clone();trial[bindingIndex]=bindingCandidate;bool valid=PlayerPreferences.ValidKeys(trial);
                settingsMessage=valid?"新绑定："+PlayerPreferences.KeyName(bindingCandidate):"按键冲突或保留键；请取消后重选。";
                GUI.enabled=valid;if(GameText.Button(new Rect(640,550,230,34),"确认绑定并保存",button)){data.keys=trial;PlayerPreferences.Save();bindingIndex=bindingCandidate=-1;}GUI.enabled=true;
                if(GameText.Button(new Rect(910,550,230,34),"取消改键",button))bindingIndex=bindingCandidate=-1;
            }else if(GameText.Button(new Rect(640,550,500,34),restoreKeysConfirm?"确认恢复全部默认按键":"恢复默认按键…",button)){
                if(restoreKeysConfirm){data.keys=PlayerPreferenceData.DefaultKeys();PlayerPreferences.Save();restoreKeysConfirm=false;}else restoreKeysConfirm=true;
            }
            GameText.Label(new Rect(130,590,1000,30),settingsMessage,small);
            if(GameText.Button(new Rect(130,635,1010,36),"返回",button)){showSettings=false;bindingIndex=bindingCandidate=-1;restoreKeysConfirm=false;}
        }
    }
}
