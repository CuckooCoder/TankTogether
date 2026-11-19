using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Steamworks;
namespace TankTogether.Networking
{
    public enum PlayerAction {Forward,Backward,Left,Right,Run,Jump,Crouch,Grab,Grow,Shrink,AgreeReset,Interact}
    [Serializable] public sealed class PlayerPreferenceData
    {
        public int version=2,language,quality=1,width=1280,height=720,windowMode=2,fps=60;
        public bool vsync=true,invertY;
        public float volume=1,sensitivity=1;
        public int cosmetic;
        public int[] keys=DefaultKeys();
        public static int[] DefaultKeys()=>new[]{(int)Key.W,(int)Key.S,(int)Key.A,(int)Key.D,(int)Key.LeftShift,(int)Key.Space,(int)Key.LeftCtrl,1000,(int)Key.E,(int)Key.Q,(int)Key.P,(int)Key.F};
    }
    public static class PlayerPreferences
    {
        public static PlayerPreferenceData Data {get;private set;}=new PlayerPreferenceData();
        static string storageKey;
        static UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset qualityAsset;
        static UnityEngine.Rendering.RenderPipelineAsset originalQualityAsset;
        static bool qualityOverride;
        public static void RestoreQuality(){
            if(!qualityOverride)return;
            QualitySettings.renderPipeline=originalQualityAsset;qualityOverride=false;originalQualityAsset=null;
            if(qualityAsset)UnityEngine.Object.Destroy(qualityAsset);qualityAsset=null;
        }
        public static void Load()
        {
            string key="TankTogether.Personal.v1."+(SteamClient.IsValid?SteamClient.SteamId.ToString():"local");
            if(key==storageKey)return;storageKey=key;
            try{Data=JsonUtility.FromJson<PlayerPreferenceData>(PlayerPrefs.GetString(key,""))??new PlayerPreferenceData();}catch{Data=new PlayerPreferenceData();}
            Migrate(Data);
            Data.sensitivity=Mathf.Clamp(Data.sensitivity,.2f,3);Data.volume=Mathf.Clamp01(Data.volume);Data.language=Mathf.Clamp(Data.language,0,1);Data.quality=Mathf.Clamp(Data.quality,0,2);Data.cosmetic=Mathf.Clamp(Data.cosmetic,0,3);
            ApplyNonDisplay();
        }
        public static void Save(){if(storageKey==null)Load();PlayerPrefs.SetString(storageKey,JsonUtility.ToJson(Data));PlayerPrefs.Save();}
        public static void Migrate(PlayerPreferenceData data)
        {
            if(data.version<2 && data.keys!=null && data.keys.Length==11){
                var keys=new int[12];Array.Copy(data.keys,keys,11);
                if(keys[8]==(int)Key.Q&&keys[9]==(int)Key.E){keys[8]=(int)Key.E;keys[9]=(int)Key.Q;}
                // A custom old F binding is retained. Pick an unused key for the new action.
                foreach(Key key in new[]{Key.F,Key.G,Key.H,Key.J,Key.K,Key.L,Key.U,Key.I,Key.O,Key.N,Key.M,Key.B})
                    if(Array.IndexOf(data.keys,(int)key)<0){keys[11]=(int)key;break;}
                data.keys=keys;
            }
            if(!ValidKeys(data.keys))data.keys=PlayerPreferenceData.DefaultKeys();
            data.version=2;
        }
        public static string Binding(PlayerAction action)=>KeyName(Data.keys[(int)action]);
        public static bool ValidKeys(int[] keys)
        {
            if(keys==null||keys.Length!=12)return false;var used=new System.Collections.Generic.HashSet<int>();
            foreach(int key in keys)if(!IsBindable(key)||!used.Add(key))return false;return true;
        }
        public static bool IsBindable(int key)=>key>=1000&&key<=1002||key>0&&Enum.IsDefined(typeof(Key),key)&&key!=(int)Key.Escape&&key!=(int)Key.Enter&&key!=(int)Key.F1&&key!=(int)Key.F2&&key!=(int)Key.F3&&key!=(int)Key.F4&&key!=(int)Key.R&&key!=(int)Key.Tab&&key!=(int)Key.Digit1&&key!=(int)Key.Digit2&&key!=(int)Key.Digit3&&key!=(int)Key.Digit4;
        public static bool Held(PlayerAction action)=>Read(Data.keys[(int)action],false);
        public static bool Pressed(PlayerAction action)=>Read(Data.keys[(int)action],true);
        static bool Read(int code,bool edge)
        {
            UnityEngine.InputSystem.Controls.ButtonControl control=null;
            if(code>=1000){var m=Mouse.current;if(m!=null)control=code==1000?m.leftButton:code==1001?m.rightButton:m.middleButton;}
            else if(Keyboard.current!=null)control=Keyboard.current[(Key)code];
            return control!=null&&(edge?control.wasPressedThisFrame:control.isPressed);
        }
        public static string KeyName(int code)=>code>=1000?new[]{"Mouse 1","Mouse 2","Mouse 3"}[Mathf.Clamp(code-1000,0,2)]:((Key)code).ToString();
        public static Vector3 Move=>new Vector3((Held(PlayerAction.Right)?1:0)-(Held(PlayerAction.Left)?1:0),0,(Held(PlayerAction.Forward)?1:0)-(Held(PlayerAction.Backward)?1:0));
        public static void ApplyNonDisplay()
        {
            AudioListener.volume=Data.volume;Application.targetFrameRate=Data.fps;QualitySettings.vSyncCount=Data.vsync?1:0;
            var active=UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if(active && !qualityAsset){originalQualityAsset=QualitySettings.renderPipeline;qualityOverride=true;qualityAsset=UnityEngine.Object.Instantiate(active);qualityAsset.name="Personal quality (runtime)";QualitySettings.renderPipeline=qualityAsset;}
            if(qualityAsset){qualityAsset.renderScale=new[]{.75f,1f,1f}[Data.quality];qualityAsset.shadowDistance=new[]{20f,45f,80f}[Data.quality];qualityAsset.msaaSampleCount=new[]{1,2,4}[Data.quality];}
        }
    }
}
