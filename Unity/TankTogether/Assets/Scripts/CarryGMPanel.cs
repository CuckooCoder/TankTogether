using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TankTogether.Networking;

namespace TankTogether.M0
{
    [DefaultExecutionOrder(-200)]
    public sealed class CarryGMPanel : MonoBehaviour
    {
        M0CarryLab lab;
        SteamCarrySession session;
        M0CarryBaseline draft;
        CarryTuningExtras extra;
        Font font;
        GUIStyle label,button,field,hint;
        Vector2 scroll;
        readonly Dictionary<string,string> numberText=new Dictionary<string,string>();
        readonly Dictionary<string,string> errors=new Dictionary<string,string>();
        float previousTimeScale;
        bool previousCapture,diagnostics,onlineOpen;
        int tab;
        string search="",notice="修改草稿后应用；从当前检查点重开。";
        public bool IsOpen {get;private set;}
        bool Online=>session&&session.InRoom;
        bool CanOpen=>CanEdit||Online;
        public bool CanEdit=>lab&&(!session||(session.CanChooseMode&&session.Mode==SteamCarrySession.EntryMode.LocalTest)||(session.Host&&session.InRoom));
        string Level=>lab.GetComponent<WhiteboxCarryLevel>()?lab.GetComponent<WhiteboxCarryLevel>().levelId:"flat";
        public string PresetPath=>Path.Combine(Application.persistentDataPath,"gm-local-"+Level+".json");
        public void Initialize(M0CarryLab owner){lab=owner;session=GetComponent<SteamCarrySession>();font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Arial"},18);}
        void Update()
        {
            if(!lab||Keyboard.current==null)return;
            if(session&&session.PersonalDialogVisible&&!IsOpen)return;
            if(Keyboard.current.f3Key.wasPressedThisFrame)diagnostics=!diagnostics;
            if(Keyboard.current.f1Key.wasPressedThisFrame){if(IsOpen)Close();else Open();}
            else if(Keyboard.current.f2Key.wasPressedThisFrame&&lab.Stretch&&lab.Stretch.Active){if(IsOpen&&tab==CarryTuning.StretchGroup)Close();else OpenStretch();}
            else if(IsOpen&&Keyboard.current.escapeKey.wasPressedThisFrame)Close();
        }
        public void RefreshFromCurrent()
        {
            if(!lab)return;numberText.Clear();errors.Clear();lab.ReadTuningComponents();
            if(draft)Destroy(draft);draft=Instantiate(lab.baseline);draft.hideFlags=HideFlags.DontSave;extra=CarryTuning.Clone(lab.Tuning);
        }
        public bool Open()
        {
            if(IsOpen||!CanOpen)return false;
            RefreshFromCurrent();onlineOpen=Online;
            previousCapture=lab.LocalInputCaptured;previousTimeScale=Time.timeScale;
            if(onlineOpen){if(!session.OpenGM())return false;}
            else{Time.timeScale=0;lab.SetDebugCapture(false);}
            IsOpen=true;notice=onlineOpen?(CanEdit?"房主应用后全员同步；关闭面板后从检查点开始。":"队友只能查看/复制。关闭面板后等待房主继续。"):"应用仅影响本次模拟；返回大厅恢复随包配置。";
            return true;
        }
        public bool OpenStretch()
        {
            if(!IsOpen&&!Open())return false;
            tab=CarryTuning.StretchGroup;search="";scroll=Vector2.zero;return true;
        }
        public void Close()
        {
            if(!IsOpen)return;IsOpen=false;
            if(!onlineOpen){Time.timeScale=previousTimeScale;lab.SetDebugCapture(previousCapture);}
            else{Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        }
        public bool ApplyDraft()
        {
            if(!CanEdit||!draft)return false;
            string error=DraftError();if(error!=null){notice=error;return false;}
            var profile=CarryTuning.Capture(draft,extra,Level);
            if(Online){if(!session.ApplyRoomConfiguration(profile,out error)){notice=error;return false;}}
            else{CarryTuning.Apply(lab,profile);lab.ResetLab();previousCapture=false;}
            notice=Online?"已应用全员参数。关闭 GM，等队友确认后由房主开始本段。":"已应用并重开本段。关闭后按 Enter 操作。";return true;
        }
        public static string Validate(M0CarryBaseline b)=>CarryTuning.Validate(b,new CarryTuningExtras());
        string DraftError(){foreach(var e in errors)return e.Value;return draft?CarryTuning.Validate(draft,extra):"没有草稿。";}
        public string CopyCurrent()
        {
            if(Online&&session.ConfigRevision<1){notice="正在等待房主参数，稍后再复制。";return null;}
            string json=JsonUtility.ToJson(CarryTuning.Capture(lab),true);GUIUtility.systemCopyBuffer=json;
            notice="已复制当前生效的全部 "+CarryTuning.All.Length+" 项参数；未应用的草稿不包含在内。";return json;
        }
        public bool ImportDraft(string json)
        {
            if(!CanEdit||!draft)return false;
            try{CarryTuning.Read(CarryTuning.Parse(json),draft,extra,Level);numberText.Clear();errors.Clear();notice="已导入草稿，点击应用后才生效。旧预设缺少的伸缩项使用旧版默认值。";return true;}
            catch(Exception e){notice="导入失败："+e.Message;return false;}
        }
        void Save()
        {
            if(!CanEdit)return;var error=DraftError();if(error!=null){notice=error;return;}
            try{File.WriteAllText(PresetPath,JsonUtility.ToJson(CarryTuning.Capture(draft,extra,Level),true));notice="草稿已保存本机；不覆盖项目参数资产或房主进度。";}
            catch(Exception e){notice="保存失败："+e.Message;}
        }
        void Load()
        {
            if(!CanEdit)return;
            try{if(!File.Exists(PresetPath)){notice="尚无本机草稿文件。";return;}
                string text=File.ReadAllText(PresetPath);
                if(text.Contains("\"schema\"")){ImportDraft(text);return;}
                // Legacy local presets contain baseline fields only; preserve current extra settings.
                var legacy=Instantiate(draft);try{JsonUtility.FromJsonOverwrite(text,legacy);var error=CarryTuning.Validate(legacy,extra);if(error!=null)throw new InvalidDataException(error);JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(legacy),draft);numberText.Clear();errors.Clear();notice="已读取旧版人物/鱼缸草稿；相机与联机值保留当前值，应用后生效。";}finally{Destroy(legacy);}
            }catch(Exception e){notice="读取失败："+e.Message;}
        }
        void RestoreDraft(){if(!CanEdit)return;try{CarryTuning.Read(lab.ShippedTuning,draft,extra,Level);numberText.Clear();errors.Clear();notice="随包参数已读入草稿；点击应用后才生效。";}catch(Exception e){notice=e.Message;}}
        void Styles()
        {
            if(label!=null)return;
            label=new GUIStyle(GUI.skin.label){font=font,fontSize=18,wordWrap=true,normal={textColor=Color.white}};
            hint=new GUIStyle(label){fontSize=15,normal={textColor=new Color(.76f,.85f,.90f)}};
            button=new GUIStyle(GUI.skin.button){font=font,fontSize=16};field=new GUIStyle(GUI.skin.textField){font=font,fontSize=17};
        }
        void Number(CarryTuning.Parameter p)
        {
            if(p.min==p.max)
            {
                GUILayout.Label(new GUIContent(p.label+"    "+p.max.ToString("F2")+"（固定）",p.help),label);
                GUILayout.Label(p.help,hint);return;
            }
            if(p.key=="b.controlFeelMode")
            {
                GUILayout.BeginHorizontal();GUILayout.Label(new GUIContent(p.label,p.help),label,GUILayout.Width(290));GUI.enabled=CanEdit;
                int selected=GUILayout.Toolbar(draft.controlFeelMode>=.5f?1:0,new[]{"A 旧版对照","B 新版候选"},button,GUILayout.Width(420));
                draft.controlFeelMode=selected;GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.Label(p.help,hint);return;
            }
            if(p.key=="b.headSupportMode")
            {
                GUILayout.BeginHorizontal();GUILayout.Label(new GUIContent(p.label,p.help),label,GUILayout.Width(290));GUI.enabled=CanEdit;
                draft.headSupportMode=GUILayout.Toolbar(draft.headSupportMode>=.5f?1:0,new[]{"旧单点承托","平头承托（试验）"},button,GUILayout.Width(420));
                GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.Label(p.help,hint);return;
            }
            float value=p.Read(draft,extra);
            if(p.group==4){GUILayout.Label(new GUIContent(p.label+"  "+value.ToString("G",CultureInfo.InvariantCulture),p.help),hint);return;}
            GUILayout.BeginHorizontal();GUILayout.Label(new GUIContent(p.label,p.help),label,GUILayout.Width(290));
            GUI.enabled=CanEdit;
            float slide=GUILayout.HorizontalSlider(value,p.min,p.max,GUILayout.Width(290));
            // Do not clamp a value merely by drawing the slider.
            if(!numberText.ContainsKey(p.key))numberText[p.key]=value.ToString("R",CultureInfo.InvariantCulture);
            if(slide!=Mathf.Clamp(value,p.min,p.max)){numberText[p.key]=slide.ToString("0.###",CultureInfo.InvariantCulture);value=slide;}
            string text=GUILayout.TextField(numberText[p.key],field,GUILayout.Width(120));numberText[p.key]=text;
            if(float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float typed)&&!float.IsNaN(typed)&&!float.IsInfinity(typed)&&typed>=p.min&&typed<=p.max){p.Write(draft,extra,typed);errors.Remove(p.key);}
            else errors[p.key]=p.label+"：请输入 "+p.min+"～"+p.max+"。";
            GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.Label(p.help+"  ["+p.min+"～"+p.max+"]",hint);GUILayout.Space(7);
        }
        void OnGUI()
        {
            var sessionView=GetComponent<SteamCarrySession>();if(sessionView&&sessionView.MenuOverlayVisible&&!IsOpen)return;
            if(!lab)return;Styles();var matrix=GUI.matrix;int depth=GUI.depth;
            float scale=Mathf.Min(Screen.width/1280f,Screen.height/720f);GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);float width=Screen.width/scale;
            if(!IsOpen){
                GUI.Label(new Rect(width-470,260,450,48),CanOpen?"F1  GM 参数 / 一键复制 · F3 网络状态":"进入本地模拟或 Steam 房间后按 F1 调参",hint);
                if(diagnostics&&session)GUI.Label(new Rect(24,310,width-48,90),session.NetworkDiagnostics+"\n"+(Online?session.ConfigurationStatus:""),hint);
                GUI.matrix=matrix;return;
            }
            GUI.depth=-100;GUI.color=new Color(.025f,.04f,.055f,.99f);GUI.DrawTexture(new Rect(width/2-520,10,1040,700),Texture2D.whiteTexture);GUI.color=Color.white;
            GUILayout.BeginArea(new Rect(width/2-500,22,1000,678));
            GUILayout.BeginHorizontal();GUILayout.Label(Online?(CanEdit?"GM · 房主统一调参":"GM · 队友只读"):"GM · 本地试调（已暂停）",label);if(GUILayout.Button("关闭 / F1",button,GUILayout.Width(130)))Close();GUILayout.EndHorizontal();
            GUILayout.Label(session?session.ConfigurationStatus:"本地运行时参数",hint);
            GUILayout.BeginHorizontal();GUI.enabled=!Online||session.ConfigRevision>0;
            if(GUILayout.Button("一键复制当前生效参数",button,GUILayout.Height(35)))CopyCurrent();GUI.enabled=CanEdit;
            if(GUILayout.Button("从剪贴板导入草稿",button))ImportDraft(GUIUtility.systemCopyBuffer);
            GUI.enabled=true;GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();GUILayout.Label("筛选",hint,GUILayout.Width(40));string q=GUILayout.TextField(search,field);if(q!=search){search=q;scroll=Vector2.zero;}lab.ShowHeadSupport=GUILayout.Toggle(lab.ShowHeadSupport,new GUIContent("显示头顶支撑面","仅本机调试显示；青色空闲、黄色承托、绿色抓握。线框按实际平头尺寸绘制，穿透显示不代表隔墙接触。旧单点模式不显示矩形。"),button,GUILayout.Width(170));GUILayout.EndHorizontal();
            int selected=GUILayout.Toolbar(tab,CarryTuning.Groups,button,GUILayout.Height(32));if(selected!=tab){tab=selected;scroll=Vector2.zero;}
            scroll=GUILayout.BeginScrollView(scroll,GUILayout.Height(355));int found=0;
            foreach(var p in CarryTuning.All){if(string.IsNullOrWhiteSpace(search)?p.group!=tab:!(p.label+" "+p.key+" "+p.help).ToLowerInvariant().Contains(search.ToLowerInvariant()))continue;Number(p);found++;}
            if(found==0)GUILayout.Label("没有匹配项。支持中文名或代码字段名。",hint);
            GUILayout.EndScrollView();
            GUILayout.Label("这里只改当前测试配置。地图位置 / 升降总行程 / 存档规则不变；复制内容含环境说明。",hint);
            GUILayout.Label(DraftError()??notice,hint,GUILayout.Height(42));
            GUILayout.BeginHorizontal();GUI.enabled=CanEdit&&DraftError()==null;
            if(GUILayout.Button(Online?"应用全员并重开本段":"应用并重开本段",button,GUILayout.Height(34)))ApplyDraft();
            if(GUILayout.Button("保存本机草稿",button))Save();GUI.enabled=CanEdit;
            if(GUILayout.Button("读取本机草稿",button))Load();if(GUILayout.Button("随包参数→草稿",button))RestoreDraft();
            GUI.enabled=true;GUILayout.EndHorizontal();GUILayout.EndArea();GUI.matrix=matrix;GUI.depth=depth;
        }
        void OnDisable(){Close();}
        void OnDestroy(){Close();if(draft)Destroy(draft);if(font)Destroy(font);}
    }
}
