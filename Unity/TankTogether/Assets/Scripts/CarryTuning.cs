using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace TankTogether.M0
{
    [Serializable] public sealed class CarryTuningExtras
    {
        [HideInInspector] public float twoPlayerGripSpring=160, twoPlayerGripDamping=18, twoPlayerGripMaxTorque=40;
        public float cameraDistance=7.5f, cameraFollow=25, cameraReturn=10, mouseYaw=.12f, mousePitch=.1f;
        public float snapshotRate=30, snapshotSmoothing=25, inputHeartbeat=10;
        public float liftSpeed=2.5f, liftDelay=2;
    }
    [Serializable] public sealed class CarryTuningValue { public string key, label; public float value; }
    [Serializable] public sealed class CarryTuningProfile
    {
        public int schema=1;
        public string levelId, baselineVersion, context;
        public CarryTuningValue[] values;
    }
    // One catalog drives editing, validation, clipboard, disk presets and network configuration.
    public static class CarryTuning
    {
        public const int MaxBytes=32768;
        public static readonly string[] Groups={"人物走跳","尺寸站位","鱼缸搬运","抓取支撑","历史兼容","相机","机关联机","人物伸缩"};
        public sealed class Parameter
        {
            public string key,label,help; public int group; public float min,max;
            public Parameter(int g,string k,string l,float lo,float hi,string h){group=g;key=k;label=l;min=lo;max=hi;help=h;}
            public float Read(M0CarryBaseline b,CarryTuningExtras e){var a=key.Split('.');object o=a[0]=="b"?(object)b:e;var f=o.GetType().GetField(a[1]);object v=f.GetValue(o);return a.Length==2?(float)v:(float)v.GetType().GetField(a[2]).GetValue(v);}
            public void Write(M0CarryBaseline b,CarryTuningExtras e,float value){var a=key.Split('.');object o=a[0]=="b"?(object)b:e;var f=o.GetType().GetField(a[1]);if(a.Length==2)f.SetValue(o,value);else{object v=f.GetValue(o);v.GetType().GetField(a[2]).SetValue(v,value);f.SetValue(o,v);}}
        }
        public const int StretchGroup = 7;
        public static readonly Parameter[] All={
            new Parameter(7,"b.stretchBalanceMode","身高搬运代价：0关/1开",0,1,"B关卡中生效。开启后两名及以上接地承托/抓握者按平均身高影响稳定性；空手、无接触、空中不主动摇缸。0恢复原物理对照。"),
            new Parameter(7,"b.stretchBalanceStart","高身形代价起点 m",1.7f,4.9f,"从此平均身高起逐渐增加转向/启停带来的倾斜。默认站高不额外施加摇摆力矩；最高身高达到完整代价。若起点高于身高上限，则不产生高身形代价。"),
            new Parameter(7,"b.stretchBalanceLever","最高身形惯性力臂 m",0,2,"把实际水平加速度转换为鱼缸俯仰/侧倾的虚拟力臂。越大急停转向越晃，0关闭额外惯性力矩；不按时间随机抖动、不改变移动速度。"),
            new Parameter(7,"b.stretchBalanceTorque","四人额外力矩上限 N·m",0,200,"限制额外晃缸力矩，两/三人按鱼缸质量同比缩小。只在至少两人接地支撑、缸未严重翻倒时生效；不得靠此救回翻倒。"),
            new Parameter(7,"b.stretchBalanceLowDamping","最矮旋转阻尼倍率",1,4,"最低身高、至少两人接地承托时，对鱼缸原旋转阻尼的倍率。越大越抗晃，但不会自动扶正或锁住鱼缸。正常站高为1倍。"),
            new Parameter(7,"b.stretchBalanceHighDamping","最高旋转阻尼倍率",.3f,1,"高于代价起点渐变，最高身高达到此倍率。越小越难收稳。失去足够的接地承托后恢复原阻尼，松手但仍顶缸不算失去承托。"),
            new Parameter(7,"b.stretchMinHeight","伸缩最低身高 m",.6f,2.5f,"B/B0按缩矮键（默认Q）能缩到的最低高度。调小可过更低通道，也更容易失去头顶承托；不能低于身体直径，且须不高于出生站高。A模式蹲高独立。"),
            new Parameter(7,"b.stretchMaxHeight","伸缩最高身高 m",1,5,"B/B0按长高键（默认E）可达到的身高上限。调大增加上顶范围，也更容易碰低顶；必须高于最低身高且不低于出生站高。遇阻仍会提前停下。"),
            new Parameter(7,"b.stretchGrowSpeed","长高速率 m/s",.05f,1.5f,"按长高键（默认E）每秒增加的身高。默认0.3；调大响应更快，也更容易突然顶缸。受天花板、推拉力和受力偏差限制，不保证顶着缸也达到此速度。"),
            new Parameter(7,"b.stretchShrinkSpeed","缩矮速率 m/s",.05f,1.5f,"按缩矮键（默认Q）每秒降低的身高。默认0.3；调大放低更快。未抓握只减少支撑，已抓握才通过固定连接下拉；松键保留身高。"),
            new Parameter(7,"b.stretchGripMaxForce","伸缩抓握推拉上限 N",10,800,"每个已抓握角色的主动竖直推拉力上限，默认240。调大更容易抬/拉缸，也更易抖动或翻缸；总系统仍受反作用力。头顶未抓握承托上限在抓取支撑页。"),
            new Parameter(7,"b.stretchGripStiffness","伸缩抓握刚度 N/m",100,8000,"头顶与固定抓握点的高度差乘此值产生推拉力，默认3000。调大更硬、更紧，过大可能振荡；实际力受推拉上限限制，不改变被动关节限距。"),
            new Parameter(7,"b.stretchGripDamping","伸缩抓握阻尼 N·s/m",0,400,"按人物和固定抓握点的竖直相对速度修正推拉力，默认100。调大抑制晃动但可能拖慢伸缩，过大也可能冲击；与刚度一起调。"),
            new Parameter(7,"b.stretchCompliance","伸缩受力容许偏差 m",.01f,.2f,"主动伸缩时，允许头顶越过接触/固定抓握高度的距离，默认0.08。调小更早受阻；调大更容易施力但压入/拉伸更明显。不是天花板穿透距离，建议不大于抓取关节限距。"),
            new Parameter(0,"b.controlFeelMode","3C 对照版：0旧 / 1新",0,1,"0保留旧控制；1启用移动平台相对速度、独立空中松键减速与镜头纵向跟随。应用后重开本段。只接受0或1。"),
            new Parameter(0,"b.airReleaseDeceleration","空中松键减速 m/s²",0,80,"仅3C新版、空手且没有方向输入时生效。越小越保留跳跃水平惯性；0完全保留，8为候选值。按方向时仍用空中修正参数；地面刹停不变。"),
            new Parameter(5,"b.cameraVerticalFollow","镜头纵向跟随响应",5,80,"仅3C新版生效；越小越柔和但上下跟随滞后越大。水平跟随、鼠标旋转和墙体避障独立；18为候选值。"),
            new Parameter(0,"b.walkSpeed","行走速度 m/s",1,12,"变大走得更快，也增加同一跳跃的水平距离。"),
            new Parameter(0,"b.sprintSpeed","奔跑速度 m/s",1,16,"按 Shift 使用；变大跑得更快。"),
            new Parameter(0,"b.freeAcceleration","起步 / 转向 m/s²",1,120,"越大越跟手；只影响空手移动。"),
            new Parameter(0,"b.freeDeceleration","松键刹停 m/s²",1,160,"越大松开方向后滑得越短。"),
            new Parameter(0,"b.airAcceleration","空中修正 m/s²",0,80,"越大越容易调整落点；不增加跳高。"),
            new Parameter(0,"b.freeJumpHeight","跳跃高度 m",.1f,3,"空手和抓缸共用的起跳目标高度。障碍和抓缸站位会影响实际升幅。"),
            new Parameter(0,"b.freeFallGravityMultiplier","空手下落重力倍率",1,4,"只加快空手下降；不改上升/跳高，不用于抓缸或头顶支撑。1为旧节奏。"),
            new Parameter(0,"b.coyoteTime","离地补跳窗口 s",0,.25f,"离开边缘后短暂允许补按跳跃；每次落地仅一次。"),
            new Parameter(0,"b.jumpBufferTime","提前按跳缓存 s",0,.25f,"落地前略早按跳也能触发，降低卡帧要求。"),
            new Parameter(1,"b.tankSize.x","鱼缸长度 X m",2,6,"同时影响玻璃、碰撞、抓取边界与支架；需复查窄道。"),
            new Parameter(1,"b.tankSize.z","鱼缸宽度 Z m",1.5f,4,"越宽越占转弯空间，不会自动增加质量。"),
            new Parameter(1,"b.tankSize.y","鱼缸高度 Y m",.5f,2.5f,"影响重心和头顶净空；不是仅改外观。"),
            new Parameter(1,"b.standingHeight","角色站高 m",1,2.5f,"改变胶囊与支撑高度；需同步复查初始缸底高度。"),
            new Parameter(1,"b.crouchingHeight","角色蹲高 m",.6f,2.5f,"决定低矮空间通行和放低鱼缸幅度，不能超过站高。"),
            new Parameter(1,"b.playerDiameter","角色碰撞直径 m",.3f,1.2f,"越大越难挤过窄缝；不能超过蹲高。"),
            new Parameter(1,"b.spawnHalfSpacing.x","旧配置横向半间距 m",.1f,2.5f,"保留旧预设中的 X 偏移。三人集合时根据缸的左、右、后三边计算站位。"),
            new Parameter(1,"b.spawnHalfSpacing.y","双人集合纵向半间距 m",.1f,1.5f,"双人集合时，每人沿缸的前后方向距中心的距离。三人集合时根据缸边计算站位。"),
            new Parameter(1,"b.carryBottomHeight","初始缸底高度 m",.6f,3,"实验场初始缸底高度。正式白盒单柱高度固定为模型高度，检查点重生由队员头顶承托。"),
            new Parameter(1,"b.standInset.x","支架横向内缩 m",0,.8f,"旧四柱实验场的支架内缩；正式新白盒使用模型单柱，本项不改变它。"),
            new Parameter(1,"b.standInset.y","支架纵向内缩 m",0,.6f,"相对缸边内缩；影响支架与角色的空间。"),
            new Parameter(1,"b.standWidth","支架宽度 m",.1f,.6f,"越宽越容易承托，也更容易挡住角色。"),
            new Parameter(2,"b.tankMass","四人基准鱼缸质量 kg",1,80,"两/三人局按既有团队人数规则缩放；越重越难搬。"),
            new Parameter(2,"b.playerMass","角色质量 kg",1,30,"影响与缸的质量比、推挤和起跳传力。"),
            new Parameter(2,"b.tankDamping","鱼缸线性阻尼",0,5,"越大移动衰减越快，也缩短搬缸滞空与惯性滑行。"),
            new Parameter(2,"b.angularDamping","鱼缸旋转阻尼",0,15,"越大旋转停得越快，不会直接把倾斜的缸扶正。"),
            new Parameter(2,"b.carrySpeed","满员抓缸行走 m/s",.5f,8,"实际速度还受抓取人数、重量、拉扯和推力限制。"),
            new Parameter(2,"b.carrySprintSpeed","满员抓缸奔跑 m/s",.5f,10,"变大跳得更远，也更难停稳。"),
            new Parameter(2,"b.carryMotorGain","搬运移动响应",1,180,"把速度误差转成推力；越大响应越快，受最大推力限制。"),
            new Parameter(2,"b.carryMotorForce","搬运最大推力 N",1,300,"限制每个角色的移动力；越大越容易推得动。"),
            new Parameter(2,"b.carryBrakeAcceleration","抓住时刹停 m/s²",.1f,20,"仅仍抓住且松开方向时起作用，不是松手后的鱼缸刹车。"),
            new Parameter(4,"b.carryJumpVelocity","旧搬缸起跳增速（已停用）",1,20,"保留旧版配置值。跳跃高度统一在人物走跳页设置。"),
            new Parameter(3,"b.gripInset","抓取边缘内缩 m",0,.4f,"越大有效抓取区域越小，避免站在缸外也能抓到。"),
            new Parameter(3,"b.gripVerticalReach","抓取垂直范围 m",.05f,.6f,"越大越容易抓到头顶附近的缸；过大可能隔空抓取。"),
            new Parameter(3,"b.gripLimit","抓取关节限距 m",.02f,.4f,"越大连接允许的位移越多、越松；小值可能增加抖动。"),
            new Parameter(3,"b.headSupportMode","头顶承托：0单点 / 1平头",0,1,"0为旧版对照；1为有限矩形四点承托。皮肤不变，身体仍为胶囊；不叠加伸缩抓握推拉。应用后重开本段。"),
            new Parameter(3,"b.headSupportWidthRatio","平头边长 / 身体直径",.3f,.7f,"仅平头生效。默认0.7，即直径0.6米时边长0.42米，四角在身体投影内；增大拓宽接触面，但不增加总支撑力。"),
            new Parameter(3,"b.headSupportFriction","平头被动摩擦系数",0,.4f,"仅平头且未抓握生效。默认0.15沿用伸缩摩擦上限；越大越抗滑，0无切向摩擦。依赖实际承托力，不吸附、不主动拉缸。"),
            new Parameter(3,"b.supportStiffness","头顶支撑刚度",100,8000,"越大支撑压缩越少，太大可能弹跳。"),
            new Parameter(3,"b.supportDamping","头顶支撑阻尼",0,400,"抵消支撑相对速度，越大越能压住上下振荡。"),
            new Parameter(3,"b.supportMaxForce","头顶最大支撑力 N",10,800,"限制单个角色支撑力量；不足会被缸压下。"),
            new Parameter(4,"e.twoPlayerGripSpring","两人扶正强度（已停用）",0,400,"保留旧版配置值。"),
            new Parameter(4,"e.twoPlayerGripDamping","两人抗旋转阻尼（已停用）",0,80,"保留旧版配置值。"),
            new Parameter(4,"e.twoPlayerGripMaxTorque","两人辅助力矩上限（已停用）",0,120,"保留旧版配置值，单位 N·m。"),
            new Parameter(5,"e.cameraDistance","相机目标距离 m",2,12,"墙体会继续把相机顶到前方，不能越墙观看。"),
            new Parameter(5,"e.cameraFollow","相机跟随响应",5,80,"越大跟随越紧，越小越柔但增加视觉拖尾。"),
            new Parameter(5,"e.cameraReturn","离墙后拉远速度 m/s",1,30,"遇墙立即前移，离墙后按此速度回到目标距离。"),
            new Parameter(5,"e.mouseYaw","水平鼠标灵敏度",.02f,.4f,"鼠标单位位移对应水平旋转角度；越大转得越快。"),
            new Parameter(5,"e.mousePitch","垂直鼠标灵敏度",.02f,.4f,"越大上下看越快；上下角度限制保持 -70～80°。"),
            new Parameter(6,"e.liftSpeed","升降台速度 m/s",.2f,6,"仅改变速度，101m路线/终点高度不变。"),
            new Parameter(6,"e.liftDelay","旧自动升降延迟（当前不用）",.2f,10,"仅为旧配置兼容保留。当前靠近白色机关按交互键立即切换升降，不读取这个延迟。"),
            new Parameter(6,"e.snapshotRate","状态发送频率 Hz",10,50,"越高更新越密也增加带宽；物理仍按固定步长运行，不能消除网络延迟。"),
            new Parameter(6,"e.snapshotSmoothing","加入方画面平滑响应",5,80,"越大更跟手但网络抖动更明显，越小更平滑但拖尾增加。"),
            new Parameter(6,"e.inputHeartbeat","输入保活频率 Hz",5,30,"无变化时发送频率；按键变化仍立即发送。不会加快物理模拟。")
        };
        public static CarryTuningExtras Clone(CarryTuningExtras e)=>JsonUtility.FromJson<CarryTuningExtras>(JsonUtility.ToJson(e));
        public static CarryTuningProfile Capture(M0CarryLab lab)
        {
            lab.ReadTuningComponents();return Capture(lab.baseline,lab.Tuning,lab.GetComponent<WhiteboxCarryLevel>()?lab.GetComponent<WhiteboxCarryLevel>().levelId:"flat");
        }
        public static CarryTuningProfile Capture(M0CarryBaseline b,CarryTuningExtras e,string level)
        {
            var p=new CarryTuningProfile{levelId=level,baselineVersion=b.version,values=new CarryTuningValue[All.Length],context="世界重力="+Physics.gravity+"；物理步长="+Time.fixedDeltaTime+"；俯仰=-70..80；升降行程由地图定义；空手和抓缸的跳跃目标="+b.freeJumpHeight+"米；motorGain/motorForce为已停用旧字段。"};
            for(int i=0;i<All.Length;i++)p.values[i]=new CarryTuningValue{key=All[i].key,label=All[i].label,value=All[i].Read(b,e)};return p;
        }
        public static string Validate(M0CarryBaseline b,CarryTuningExtras e)
        {
            if(b==null||e==null)return "缺少配置。";
            foreach(var f in All){float v=f.Read(b,e);if(float.IsNaN(v)||float.IsInfinity(v)||v<f.min||v>f.max)return f.label+"：范围 "+f.min+"～"+f.max;}

            if(b.stretchMinHeight>=b.stretchMaxHeight||b.standingHeight<b.stretchMinHeight||b.standingHeight>b.stretchMaxHeight)return "Stretch height range must include the standing height.";
            if(b.stretchMinHeight<b.playerDiameter)return "伸缩最低身高不能小于人物碰撞直径。";
            if(b.stretchBalanceMode!=0&&b.stretchBalanceMode!=1)return "身高搬运代价只能为0或1。";
            if(b.headSupportMode!=0&&b.headSupportMode!=1)return "头顶承托对照只能为0或1。";
            if(b.controlFeelMode!=0&&b.controlFeelMode!=1)return "3C 对照版只能为0或1。";
            if(b.crouchingHeight>b.standingHeight||b.playerDiameter>b.crouchingHeight)return "蹲高不得超过站高，碰撞直径不得超过蹲高。";
            if(b.spawnHalfSpacing.x>=b.tankSize.x/2-b.gripInset||b.spawnHalfSpacing.y>=b.tankSize.z/2-b.gripInset)return "旧配置横向半间距和双人纵向半间距都要在缸底抓取范围内。";
            if(b.standInset.x+b.standWidth/2>=b.tankSize.x/2||b.standInset.y+b.standWidth/2>=b.tankSize.z/2)return "支架内缩和宽度超过鱼缸范围。";
            return null;
        }
        public static void Read(CarryTuningProfile p,M0CarryBaseline b,CarryTuningExtras e,string level)
        {
            if(p==null||p.schema!=1||p.levelId!=level||p.values==null||(p.values.Length!=All.Length && p.values.Length!=64 && p.values.Length!=56 && p.values.Length!=53))throw new InvalidDataException("配置格式、关卡或参数数量不匹配。");
            var values=new Dictionary<string,float>();foreach(var v in p.values){if(v==null||v.key==null||values.ContainsKey(v.key))throw new InvalidDataException("配置包含重复/空参数。");values.Add(v.key,v.value);}
            if(p.values.Length==53){
                if(values.ContainsKey("b.headSupportMode")||values.ContainsKey("b.headSupportWidthRatio")||values.ContainsKey("b.headSupportFriction"))throw new InvalidDataException("旧版参数不完整。");
                values.Add("b.headSupportMode",0);values.Add("b.headSupportWidthRatio",.7f);values.Add("b.headSupportFriction",.15f);
            }
            if(p.values.Length==53||p.values.Length==56){
                var defaults=UnityEngine.ScriptableObject.CreateInstance<M0CarryBaseline>();
                try{foreach(var f in All)if(f.group==StretchGroup){if(values.ContainsKey(f.key))throw new InvalidDataException("旧版伸缩参数不完整。");values.Add(f.key,f.Read(defaults,new CarryTuningExtras()));}}
                finally{if(Application.isPlaying)UnityEngine.Object.Destroy(defaults);else UnityEngine.Object.DestroyImmediate(defaults);}
            }
            if(p.values.Length<All.Length){
                var defaults=UnityEngine.ScriptableObject.CreateInstance<M0CarryBaseline>();
                try{foreach(var f in All)if(f.key.StartsWith("b.stretchBalance")&&!values.ContainsKey(f.key))values.Add(f.key,f.Read(defaults,new CarryTuningExtras()));values["b.stretchBalanceMode"]=0;}
                finally{if(Application.isPlaying)UnityEngine.Object.Destroy(defaults);else UnityEngine.Object.DestroyImmediate(defaults);}
            }
            // Validate on temporary objects so a failed import cannot partly overwrite the live settings.
            var copy=UnityEngine.Object.Instantiate(b);var extra=Clone(e);
            try{foreach(var f in All){if(!values.TryGetValue(f.key,out float value))throw new InvalidDataException("缺少参数："+f.key);f.Write(copy,extra,value);}string error=Validate(copy,extra);if(error!=null)throw new InvalidDataException(error);
                copy.defaults=Clone(extra);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(copy),b);JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(extra),e);
            }finally{if(Application.isPlaying)UnityEngine.Object.Destroy(copy);else UnityEngine.Object.DestroyImmediate(copy);}
        }
        public static string Hash(CarryTuningProfile p)
        {
            var s=new StringBuilder(p.schema+"|"+p.levelId);var values=new Dictionary<string,float>();foreach(var v in p.values)values.Add(v.key,v.value);
            foreach(var f in All)s.Append('|').Append(f.key).Append('=').Append(values[f.key].ToString("R",CultureInfo.InvariantCulture));return Hash128.Compute(s.ToString()).ToString();
        }
        public static CarryTuningProfile Parse(string text){if(string.IsNullOrEmpty(text)||Encoding.UTF8.GetByteCount(text)>MaxBytes)throw new InvalidDataException("配置为空或超过32KB。");return JsonUtility.FromJson<CarryTuningProfile>(text);}
        public static void Apply(M0CarryLab lab,CarryTuningProfile p){Read(p,lab.baseline,lab.Tuning,lab.GetComponent<WhiteboxCarryLevel>()?lab.GetComponent<WhiteboxCarryLevel>().levelId:"flat");lab.ApplyTuningComponents();}
    }
}
