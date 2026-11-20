using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using TankTogether.M0;

namespace TankTogether.Editor
{
    // Edit an isolated draft; only explicit Save touches the scene's configuration asset.
    public sealed class CarryParametersWindow : EditorWindow
    {
        [SerializeField] M0CarryBaseline source;
        M0CarryBaseline draft;
        [SerializeField] string draftJson;
        Vector2 scroll;
        [SerializeField] bool changed;
        bool dimensions, advanced, extras;
        string notice;
        [MenuItem("Tank Together/玩法参数")]
        public static void Open()
        {
            var window=GetWindow<CarryParametersWindow>("玩法参数");
            window.minSize=new Vector2(440,480);
            window.LoadCurrent();
            window.Show();
        }
        static M0CarryBaseline Current()
        {
            var scene=SceneManager.GetActiveScene();
            foreach(var root in scene.GetRootGameObjects())
            {
                var lab=root.GetComponentInChildren<M0CarryLab>(true);
                if(lab) return lab.baseline;
            }
            return null;
        }
        void OnEnable()
        {
            Undo.undoRedoPerformed+=OnUndo; EditorApplication.playModeStateChanged+=OnPlay;
            if(source)
            {
                draft=Instantiate(source);
                if(changed&&!string.IsNullOrEmpty(draftJson))JsonUtility.FromJsonOverwrite(draftJson,draft);

                draft.hideFlags=HideFlags.HideAndDontSave;
            }
        }
        void OnDisable()
        {
            Undo.undoRedoPerformed-=OnUndo;
            EditorApplication.playModeStateChanged-=OnPlay;
            if(draft) DestroyImmediate(draft);
        }
        void OnUndo() { if(!changed && source) Load(source); Repaint(); }
        void OnPlay(PlayModeStateChange state) { Repaint(); }
        void Load(M0CarryBaseline asset)
        {
            if(draft) DestroyImmediate(draft);
            source=asset; draft=source?Instantiate(source):null;
            if(draft) {  draft.hideFlags=HideFlags.HideAndDontSave; }
            changed=false;notice=null;draftJson=draft?JsonUtility.ToJson(draft):null;
        }
        void LoadCurrent()
        {
            if(changed && !EditorUtility.DisplayDialog("重新读取","放弃面板中尚未保存的参数？","重新读取","保留")) return;
            Load(Current());
        }
        // Keep field help here so every editor control describes the actual simulation parameter.
        static string TooltipFor(string field)
        {
            switch(field)
            {
                case nameof(M0CarryBaseline.controlFeelMode): return "0为旧控制对照，1为新版试调。新版修正移动平台接地/起跳，单独控制空中松键减速与镜头纵向跟随。不会改变抓缸推力与跳跃规则。";
                case nameof(M0CarryBaseline.airReleaseDeceleration): return "仅新版空手腾空且松方向时生效，米/秒²。越小越保留水平惯性；0不主动刹车。方向键仍用空中调整加速度，地面刹停不变。";
                case nameof(M0CarryBaseline.cameraVerticalFollow): return "仅新版的镜头上下跟随响应。越小越柔和、上下拖尾越大；越大越贴身。水平跟随、鼠标转向与避墙不变。";
                case nameof(M0CarryBaseline.freeAcceleration): return "未抓缸时的地面加速度（米/秒²），调大起步转向更快。独立于搬缸推力。";
                case nameof(M0CarryBaseline.freeDeceleration): return "未抓缸、松方向后的地面减速度（米/秒²），调大滑行更短。";
                case nameof(M0CarryBaseline.airAcceleration): return "未抓缸的空中水平控制加速度（米/秒²），调大更容易修正落点。";
                case nameof(M0CarryBaseline.coyoteTime): return "自由移动离开地面后允许补按跳跃的秒数；不提供二段跳，不作用于抓缸跳。";
                case nameof(M0CarryBaseline.jumpBufferTime): return "自由跳提前按键后等待落地的有效秒数；落地后只触发一次。";
                case nameof(M0CarryBaseline.version): return "这组参数的版本标记，便于团队区分试调方案。\n改名字不会增强或减弱任何动作；参数内容与版本都会参与联机兼容检查。\n调整后需要队友使用同一版配置或重新打包。";
                case nameof(M0CarryBaseline.walkSpeed): return "未抓鱼缸时的目标水平行走速度，单位米/秒。\n调大走得更快；调小走得更慢。\n达到目标的快慢还受自由移动响应、最大力和碰撞影响；不直接改变跳高。";
                case nameof(M0CarryBaseline.sprintSpeed): return "未抓鱼缸、按住奔跑时的目标水平速度，单位米/秒。\n调大跑得更快，相同腾空时间下通常跳得更远；调小相反。\n不直接改变起跳高度，实际速度仍受推力和碰撞影响。";
                case nameof(M0CarryBaseline.carrySpeed): return "四人抓住鱼缸时的目标水平行走速度，单位米/秒。\n调大通常搬得更快、同样滞空时跨得更远；调小相反。\n抓取人数不足四人会额外降速，质量、输入配合和推力也会影响实际速度。";
                case nameof(M0CarryBaseline.carrySprintSpeed): return "四人抓缸并奔跑时的目标水平速度，单位米/秒。\n调大通常增加跑跳距离，也更考验起跳和刹停时机；调小相反。\n不直接增加跳高；少人抓取、碰撞或推力不足时达不到目标速度。";
                case nameof(M0CarryBaseline.freeFallGravityMultiplier): return "空手下降时的重力倍率。1为原效果，2为当前试调；增大下降更快、滞空缩短，同速跳远也缩短。\n上升重力和跳高不变；抓缸及头顶支撑时不附加此重力。范围1～4。";
                case nameof(M0CarryBaseline.freeJumpHeight): return "空手和抓缸共用的起跳目标高度，单位米。\n实际升幅受碰撞、抓点和队友支撑影响。";
                case nameof(M0CarryBaseline.carryBrakeAcceleration): return "保持抓缸、松开方向输入时的刹停力度，单位米/秒²。\n调大通常停得更快、滑行更短；调小停得更缓、滑得更远。\n实际减速还受搬运移动响应、队友输入和接触影响；不是松手后的鱼缸刹车。";
                case nameof(M0CarryBaseline.tankSize): return "鱼缸本体的尺寸与碰撞范围：X长、Y高、Z宽，单位米。\n调大会增加通道占用、转弯空间，并改变支撑/抓取范围与旋转惯性；调小相反。\n不会自动增大质量；改后需复查站位、支架和关卡净空。";
                case nameof(M0CarryBaseline.standingHeight): return "角色站立时的胶囊碰撞高度及头顶位置，单位米。\n调大头顶更高，更容易顶到缸或低顶；调小可能够不到鱼缸。\n需联动初始缸底高度和抓取垂直范围；站高不能小于蹲高。";
                case nameof(M0CarryBaseline.crouchingHeight): return "角色蹲下时的胶囊碰撞高度及头顶位置，单位米。\n调小蹲得更低、可通过更矮空间，也会改变抓缸锚点和支撑；调大下蹲幅度更小。\n不能大于站高，也不能小于角色碰撞直径。";
                case nameof(M0CarryBaseline.playerDiameter): return "角色胶囊碰撞体的直径，单位米。\n调大更占空间、更容易碰到队友或窄道边缘；调小更容易通过窄处。\n出生间距需避免互相重叠；这不是质量或抓取距离。";
                case nameof(M0CarryBaseline.spawnHalfSpacing): return "第一格保留旧配置的横向半间距。第二格用于双人集合，表示每人沿缸的前后方向距中心的米数。\n三人集合时根据缸的左、右、后三边、角色碰撞半径和抓取范围计算站位。";
                case nameof(M0CarryBaseline.carryBottomHeight): return "开局时缸底相对出生地面的高度，也决定初始支架高度，单位米。\n调大鱼缸离地更高，但可能够不到；调小可能压到角色或产生重叠。\n需匹配角色站高与抓取范围，不会直接提高跳跃能力。";
                case nameof(M0CarryBaseline.standInset): return "开局支架中心从鱼缸边缘向内缩的距离，单位米。\n第一格为X方向，第二格为Z方向（Unity子标签显示Y）。\n调大支架更靠中间，调小更靠边；影响开局支撑与角色避让，不改变抓取范围。";
                case nameof(M0CarryBaseline.standWidth): return "开局支架的横截面宽度，单位米。\n调大支撑接触面积和占地更大，也更容易挡住角色；调小相反。\n只改初始支架尺寸，不是鱼缸宽度或支撑力。";
                case nameof(M0CarryBaseline.playerMass): return "每个角色刚体的质量。\n调大后同样推力更难加速，也会改变人与缸的质量比及多人传力；调小相反。\n不会让自由落体更快；起跳直接增加速度，搬缸跳实际效果需与缸质量一起实测。";
                case nameof(M0CarryBaseline.tankMass): return "鱼缸刚体的质量。\n调大通常更难推动、抬升和转动；调小更容易被角色带动，也更容易被扰动。\n改后需配合检查搬运推力与头顶支撑；质量不直接改变重力加速度。";
                case nameof(M0CarryBaseline.tankDamping): return "鱼缸全方向的移动阻尼，水平和竖直运动都会受影响。\n调大速度衰减更强，也会改变跳跃轨迹；调小惯性保留更多。\n搬运输入会补偿部分水平阻尼但受最大力限制，不能只把它当刹车或滞空时长开关。";
                case nameof(M0CarryBaseline.angularDamping): return "鱼缸转动速度的衰减强度。\n调大可减缓翻滚，但主动转弯、倾斜也更迟钝；调小转动更灵活、也更容易甩动。\n不会自动扶正已经倾斜的缸，也不能保证不会翻缸。";
                case nameof(M0CarryBaseline.motorGain): return "未抓缸时，追上目标水平速度的响应强度。\n调大起步、转向和松键减速通常更利落；调小更缓、更有滑行感。\n受自由移动最大力限制；不是最高速度，过大可能造成抖动。";
                case nameof(M0CarryBaseline.motorForce): return "未抓缸时，每个角色水平移动力的上限。\n调大通常更易加速、抗阻或推动接触物；调小可能起步慢、上坡乏力。\n达到目标速度由行走/奔跑值决定，不是把此值当速度。";
                case nameof(M0CarryBaseline.carryMotorGain): return "抓缸时，角色追上目标水平速度与松方向后刹停的响应强度。\n调大通常起步、转向和速度纠正更快；调小更柔和但更拖沓。\n移动受搬运最大力限制，松方向刹停另受刹停力度限制；过大可能加剧拉扯与抖动。";
                case nameof(M0CarryBaseline.carryMotorForce): return "抓缸且有方向输入时，每个角色水平驱动力的上限。\n调大通常更能推得动、抵抗坡道或碰撞；调小可能达不到目标速度。\n不是搬运速度，也不限制起跳冲量；松方向后的刹停使用另一套限力。";
                case nameof(M0CarryBaseline.gripInset): return "判定能否抓取时，从缸底边缘向内排除的距离，单位米。\n调大可抓区域变小，靠边更难抓到；调小可抓区域扩大。\n只影响发起抓取的范围，不是抓取强度，也不会移动已抓住的锚点。";
                case nameof(M0CarryBaseline.gripVerticalReach): return "发起抓取时，头顶与缸底沿鱼缸局部高度方向允许的距离误差，单位米。\n调大更容易在高度有偏差时抓到；调小需要头顶更贴近缸底。\n不控制抓住后的伸缩距离，也不会抬高手或角色。";
                case nameof(M0CarryBaseline.gripLimit): return "抓住后，角色头顶锚点与鱼缸锚点允许的平移活动限距，单位米。\n调大连接更松、可能更晃；调小更紧，但更容易产生强约束拉扯。\n适用于各个方向，不是初次抓取范围或自动松手距离，也不锁定旋转。";
                case nameof(M0CarryBaseline.supportStiffness): return "头顶挤入缸底支撑区时，按挤入程度产生向上支撑力的强度。\n调大支撑更硬、顶开更快，过大可能弹跳或抖动；调小更软，可能托不稳。\n受头顶最大支撑力限制；不是抓取关节强度。";
                case nameof(M0CarryBaseline.supportDamping): return "支撑区内，按头顶与缸底的竖直相对速度修正支撑力。\n调大时，头顶向缸底靠近会更强地顶开，分离时更快减弱支撑；调小此修正更弱。\n合适时可抑制振荡，过大也可能冲击或抖动；只推不拉，不会把缸吸在头上。";
                case nameof(M0CarryBaseline.headSupportMode): return "0旧单点，1有限矩形四点承托。身体胶囊与皮肤不变；只影响承托，不改变抓握连接。下一次Play生效。";
                case nameof(M0CarryBaseline.headSupportWidthRatio): return "平头边长除以角色碰撞直径；0.7×0.6米=0.42米。四角在身体圆形投影内；增大接触范围但不增加总支撑力。范围0.3～0.7。";
                case nameof(M0CarryBaseline.headSupportFriction): return "平头未抓握时，被动摩擦上限为实际支撑力乘此系数。默认0.15；增大抗滑，0无切向摩擦；没有接触就没有摩擦。范围0～0.4。";
                case nameof(M0CarryBaseline.supportMaxForce): return "每个角色头顶可施加的最大向上支撑力，并对角色施加反向力。\n调大允许更强支撑，但过大可能顶飞或抖动；调小更柔和，过低会托不住。\n需匹配鱼缸质量、支撑刚度和阻尼，不是多人合计上限，也不限制抓取关节传力。";
                default: return Array.Find(CarryTuning.All,p=>p.key=="b."+field)?.help ?? "";
            }
        }
        static GUIContent ParameterLabel(string field,string label)=>new GUIContent(label,TooltipFor(field));
        void F(string field,string label)
        {
            var info=typeof(M0CarryBaseline).GetField(field);
            float value=(float)info.GetValue(draft);
            float next=EditorGUILayout.FloatField(ParameterLabel(field,label),value);
            info.SetValue(draft,next);
        }
        void Header(string text) { EditorGUILayout.Space(8); EditorGUILayout.LabelField(text,EditorStyles.boldLabel); }
        public static string Validate(M0CarryBaseline b)
        {
            if(!b) return "没有配置。";
            string shared=CarryTuning.Validate(b,b.defaults);if(shared!=null)return shared;
            foreach(var field in typeof(M0CarryBaseline).GetFields())
                if(field.FieldType==typeof(float))
                {
                    float v=(float)field.GetValue(b);
                    if(float.IsNaN(v)||float.IsInfinity(v)||v<0) return "参数必须是有限的非负数："+field.Name;
                }
            if((b.controlFeelMode!=0&&b.controlFeelMode!=1)||b.airReleaseDeceleration>80||b.cameraVerticalFollow<5||b.cameraVerticalFollow>80)return "3C模式只能为0或1，空中松键减速0～80，纵向跟随5～80。";
            if((b.headSupportMode!=0&&b.headSupportMode!=1)||b.headSupportWidthRatio<.3f||b.headSupportWidthRatio>.7f||b.headSupportFriction<0||b.headSupportFriction>.4f)return "平头模式0或1，边长比例0.3～0.7，摩擦0～0.4。";
            if(b.freeAcceleration<=0||b.freeDeceleration<=0||b.coyoteTime>.25f||b.jumpBufferTime>.25f)return "自由起步和刹停必须大于0；补跳与缓存窗口不能超过0.25秒。";
            if(b.freeFallGravityMultiplier<1||b.freeFallGravityMultiplier>4)return "空手下落重力倍率需在1～4之间。";
            if(string.IsNullOrWhiteSpace(b.version))return "请填写参数版本。";
            if(b.playerMass<=0||b.tankMass<=0)return "玩家与鱼缸质量必须大于0。";
            if(!Finite(b.tankSize.x)||!Finite(b.tankSize.y)||!Finite(b.tankSize.z)||b.tankSize.x<=0||b.tankSize.y<=0||b.tankSize.z<=0)return "鱼缸三个尺寸必须是有限正数。";
            if(b.playerDiameter<=0||b.crouchingHeight<b.playerDiameter||b.standingHeight<b.crouchingHeight)return "身高需满足：站高 ≥ 蹲高 ≥ 碰撞直径 > 0。";
            if(b.gripInset>=Mathf.Min(b.tankSize.x,b.tankSize.z)/2)return "抓取边缘内缩不能超过鱼缸半宽。";
            if(!Finite(b.spawnHalfSpacing.x)||!Finite(b.spawnHalfSpacing.y)||b.spawnHalfSpacing.x<0||b.spawnHalfSpacing.y<0||
                b.spawnHalfSpacing.x>=b.tankSize.x/2-b.gripInset||b.spawnHalfSpacing.y>=b.tankSize.z/2-b.gripInset)return "旧配置横向半间距和双人纵向半间距都须为缸底抓取范围内的非负数。";
            if(b.spawnHalfSpacing.x*2<b.playerDiameter||b.spawnHalfSpacing.y*2<b.playerDiameter)return "旧配置横向半间距和双人纵向半间距都须至少为角色碰撞半径。";
            if(!Finite(b.standInset.x)||!Finite(b.standInset.y)||b.standInset.x<0||b.standInset.y<0||
                b.standInset.x>=b.tankSize.x/2||b.standInset.y>=b.tankSize.z/2||b.standWidth<=0)return "检查支架内缩与宽度。";
            if(b.carryBottomHeight<=0)return "初始托举高度必须大于0。";
            return null;
        }
        static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        public static void SaveDraft(M0CarryBaseline target,M0CarryBaseline values)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请停止运行后保存。");
            if(!target||!EditorUtility.IsPersistent(target))throw new InvalidOperationException("目标必须是工程配置资产。");
            var error=Validate(values);
            if(error!=null)throw new InvalidOperationException(error);
            Undo.RecordObject(target,"修改搬鱼缸玩法参数");
            // Copy only our declared data fields; never copy Unity's object name or HideFlags.
            foreach(var field in typeof(M0CarryBaseline).GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.DeclaredOnly))
                field.SetValue(target,field.Name==nameof(M0CarryBaseline.defaults)?CarryTuning.Clone(values.defaults):field.GetValue(values));

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }
        static string CurrentLevel()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {var level=root.GetComponentInChildren<WhiteboxCarryLevel>(true);if(level)return level.levelId;}
            return "flat";
        }
        void CatalogField(CarryTuning.Parameter p)
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(p.label,p.help),GUILayout.Width(Mathf.Min(300,position.width*.55f)));
            if(p.min==p.max)EditorGUILayout.LabelField(p.max.ToString("F2")+"（固定）");
            else
            {
                float next=EditorGUILayout.FloatField(p.Read(draft,draft.defaults));
                if(p.key=="b.stretchMinHeight"||p.key=="b.stretchBalanceStart")next=Mathf.Min(next,p.max);
                p.Write(draft,draft.defaults,next);
            }
            GUILayout.EndHorizontal();
        }
        void OnGUI()
        {
            bool playing=EditorApplication.isPlayingOrWillChangePlaymode;
            EditorGUILayout.LabelField("搬鱼缸 · 玩法参数",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(playing?"运行中只读。停止 Play 后修改、保存，再运行测试。":"修改的是面板草稿；点击“保存参数”才会写入配置。所有参数在下一次 Play 生效。悬停参数名可查看用途与调节影响。",MessageType.Info);
            using(new EditorGUI.DisabledScope(playing))
                if(GUILayout.Button(new GUIContent("读取当前场景配置","读取当前场景引用的配置资产到面板。\n有未保存草稿时会先询问是否放弃；读取本身不会修改资产。")))LoadCurrent();
            if(!source||!draft) {EditorGUILayout.HelpBox("请打开包含 M0 Carry Lab 的实验场景，然后读取配置。",MessageType.Info);return;}
            using(new EditorGUI.DisabledScope(true))EditorGUILayout.ObjectField(new GUIContent("当前配置","本面板读取和保存的配置资产。\n由当前场景的 M0 Carry Lab 引用决定；共用同一资产的场景都会受保存影响。"),source,typeof(M0CarryBaseline),false);
            EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(source),EditorStyles.wordWrappedMiniLabel);
            bool mismatch=Current()!=source;
            if(mismatch)EditorGUILayout.HelpBox("当前场景的配置已改变，请重新读取后再保存。",MessageType.Warning);
            if(AssetDatabase.GetAssetPath(source)=="Assets/Resources/CarryBaseline.asset")
                EditorGUILayout.HelpBox("这是 M0/M1/Steam 共用基线，修改会影响这些场景。白模试调请打开 WhiteboxCarryLab。",MessageType.Warning);
            string validation=Validate(draft);
            if(validation!=null)EditorGUILayout.HelpBox(validation,MessageType.Error);
            if(notice!=null)EditorGUILayout.HelpBox(notice,MessageType.Info);
            using(new EditorGUI.DisabledScope(playing||mismatch||validation!=null||!changed))
                if(GUILayout.Button(new GUIContent("保存参数（下次运行生效）","把面板草稿写入上方配置资产；下次 Play 生效。\n运行中、数值无效、场景配置已切换或没有新修改时不可保存。\n不会更新已打包的游戏；队友测试需同步配置并重新打包。"),GUILayout.Height(30)))
                {
                    SaveDraft(source,draft);changed=false;notice="已保存。重新 Play 可使用新参数；旧包不会自动更新。";
                }
            using(new EditorGUI.DisabledScope(playing||mismatch))
            {
                EditorGUILayout.BeginHorizontal();
                if(GUILayout.Button(new GUIContent("复制已保存全部参数","复制当前资产内的全部玩法参数，能粘贴到F1 GM；不会复制未保存草稿。")))
                {GUIUtility.systemCopyBuffer=JsonUtility.ToJson(CarryTuning.Capture(source,source.defaults,CurrentLevel()),true);notice="已复制已保存的全部参数。";}
                if(GUILayout.Button(new GUIContent("从GM剪贴板导入草稿","将F1一键复制的参数读入草稿，仍需点击保存参数。仅允许相同关卡的完整配置。")))
                {
                    try{CarryTuning.Read(CarryTuning.Parse(GUIUtility.systemCopyBuffer),draft,draft.defaults,CurrentLevel());changed=true;draftJson=JsonUtility.ToJson(draft);notice="已导入GM参数草稿；点击保存参数，下一次Play生效。";}
                    catch(Exception ex){notice="导入失败："+ex.Message;}
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.LabelField(changed ? "有未保存的修改" : "当前参数已保存",EditorStyles.miniLabel);
            scroll=EditorGUILayout.BeginScrollView(scroll);
            using(new EditorGUI.DisabledScope(playing))
            {
                EditorGUI.BeginChangeCheck();
                draft.version=EditorGUILayout.TextField(ParameterLabel(nameof(M0CarryBaseline.version),"参数版本"),draft.version);
                Header("人物伸缩 · B / B0（与 F2 / F1 GM 共用）");
                foreach(var p in CarryTuning.All)if(p.group==CarryTuning.StretchGroup)CatalogField(p);
                EditorGUILayout.HelpBox("最低 ≤ 出生站高 ≤ 最高；身高不能小于身体直径。数值为试调范围，不代表极端组合已通过玩法验收。悬停参数名查看影响。",MessageType.None);
                Header("移动 · 米/秒");
                F("walkSpeed","自由行走");F("sprintSpeed","自由奔跑");
                F("carrySpeed","四人搬运行走");F("carrySprintSpeed","四人搬运奔跑");
                Header("3C 对照试调");
                draft.controlFeelMode=EditorGUILayout.Popup(ParameterLabel("controlFeelMode","控制方案"),draft.controlFeelMode>=.5f?1:0,new[]{"A · 旧版对照","B · 新版候选"});
                F("airReleaseDeceleration","空中松键减速度");F("cameraVerticalFollow","镜头纵向跟随响应");
                Header("自由操作容错");
                F("freeAcceleration","地面起步加速度");F("freeDeceleration","松键刹停加速度");F("airAcceleration","空中调整加速度");
                F("coyoteTime","离地补跳窗口（秒）");F("jumpBufferTime","提前按跳缓存（秒）");
                Header("跳跃与刹停");
                F("freeJumpHeight","跳跃高度（米）");
                F("freeFallGravityMultiplier","空手下落重力倍率");
                F("carryBrakeAcceleration","搬运刹停力度（米/秒²）");
                dimensions=EditorGUILayout.Foldout(dimensions,new GUIContent("尺寸与初始站位","展开碰撞尺寸、开局站位与支架参数。\n这些参数相互关联，修改后需检查出生是否重叠、能否抓取与通道净空。"),true);
                if(dimensions)
                {
                    draft.tankSize=EditorGUILayout.Vector3Field(ParameterLabel(nameof(M0CarryBaseline.tankSize),"缸尺寸：X长 / Y高 / Z宽（米）"),draft.tankSize);
                    F("standingHeight","角色站高（米）");F("crouchingHeight","角色蹲高（米）");F("playerDiameter","角色碰撞直径（米）");
                    draft.spawnHalfSpacing=EditorGUILayout.Vector2Field(ParameterLabel(nameof(M0CarryBaseline.spawnHalfSpacing),"半间距（旧配置 X / 双人 Z，米）"),draft.spawnHalfSpacing);
                    F("carryBottomHeight","初始缸底高度（米）");
                    draft.standInset=EditorGUILayout.Vector2Field(ParameterLabel(nameof(M0CarryBaseline.standInset),"支架内缩：X / Z（米）"),draft.standInset);
                    F("standWidth","支架宽度（米）");
                    EditorGUILayout.HelpBox("尺寸联动碰撞、站位与抓取范围；改动后需要重新验收出生和抓取。",MessageType.None);
                }
                advanced=EditorGUILayout.Foldout(advanced,new GUIContent("高级物理与抓取","展开质量、驱动力、阻尼、抓取和头顶支撑参数。\n建议一次少改几项，分别检查起步、转弯、跳跃和落地，避免无法判断变化来源。"),true);
                if(advanced)
                {
                    F("playerMass","角色质量");F("tankMass","鱼缸质量");
                    F("tankDamping","鱼缸移动阻尼");F("angularDamping","鱼缸旋转阻尼");
                    // Legacy free-force fields remain serialized for old reports; the current controller uses acceleration above.
                    F("carryMotorGain","搬运移动响应");F("carryMotorForce","搬运最大力");
                    F("gripInset","抓取边缘内缩（米）");F("gripVerticalReach","抓取垂直范围（米）");F("gripLimit","抓取关节限距（米）");
                    F("headSupportMode","头顶承托：0单点 / 1平头");F("headSupportWidthRatio","平头边长 / 身体直径");F("headSupportFriction","平头被动摩擦系数");
                    F("supportStiffness","头顶支撑刚度");F("supportDamping","头顶支撑阻尼");F("supportMaxForce","头顶最大支撑力");
                }
                extras=EditorGUILayout.Foldout(extras,"两人辅助、相机、机关与联机（与 GM 共用）",true);
                if(extras)for(int group=4;group<=6;group++)
                {
                    Header(CarryTuning.Groups[group]);
                    foreach(var p in CarryTuning.All)if(p.group==group && p.key.StartsWith("e."))CatalogField(p);
                }
                if(EditorGUI.EndChangeCheck()) { changed=true;draftJson=JsonUtility.ToJson(draft); }
            }
            EditorGUILayout.EndScrollView();
            using(new EditorGUI.DisabledScope(playing))
                if(GUILayout.Button(new GUIContent("放弃草稿，恢复已保存值","放弃面板尚未保存的修改，重新读取当前场景已保存的配置。\n有修改时会先确认；不会把已保存的参数恢复为出厂默认值。")))LoadCurrent();
        }
    }
}
