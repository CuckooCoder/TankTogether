from pathlib import Path
from html import escape
import json,re,hashlib
from reportlab.pdfgen import canvas
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle
from reportlab.platypus import Paragraph,Table,TableStyle
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.utils import ImageReader
from pypdf import PdfReader

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'output/pdf';OUT.mkdir(parents=True,exist_ok=True)
PDF=OUT/'搬鱼缸_策划编辑器调参手册_20261013.pdf'
SHOT=ROOT/'tools/docs/assets/editor-parameters-20261013.png'
pdfmetrics.registerFont(TTFont('CN',r'C:\Windows\Fonts\simhei.ttf'))
W,H=595.276,841.89
INK=colors.HexColor('#18363C');TEAL=colors.HexColor('#087F8C');MUTED=colors.HexColor('#527077');PALE=colors.HexColor('#EAF4F3');GOLD=colors.HexColor('#B26B22')
styles={
 'body':ParagraphStyle('body',fontName='CN',fontSize=10.5,leading=15.2,textColor=INK,wordWrap='CJK',spaceAfter=0),
 'small':ParagraphStyle('small',fontName='CN',fontSize=9,leading=12.6,textColor=MUTED,wordWrap='CJK'),
 'tiny':ParagraphStyle('tiny',fontName='CN',fontSize=8,leading=11,textColor=MUTED,wordWrap='CJK'),
 'h':ParagraphStyle('h',fontName='CN',fontSize=14,leading=19,textColor=TEAL,wordWrap='CJK'),
 'card':ParagraphStyle('card',fontName='CN',fontSize=11.5,leading=16,textColor=INK,wordWrap='CJK'),
 'table':ParagraphStyle('table',fontName='CN',fontSize=9.5,leading=13.4,textColor=INK,wordWrap='CJK'),
}
c=canvas.Canvas(str(PDF),pagesize=(W,H));c.setTitle('搬鱼缸 | 策划编辑器调参手册');c.setAuthor('Tank Together 项目组')
page=0;y=0;checks=[];book=[]
def para(text,x=44,width=W-88,style='body',gap=7):
 global y
 p=Paragraph(text,styles[style]);_,h=p.wrap(width,1000);p.drawOn(c,x,y-h);y-=h+gap
 if y<48:raise ValueError(f'Page {page} overflow: {y} / {text[:30]}')
 return h

def start(title,kicker='编辑器玩法参数 / 策划工作手册'):
 global page,y
 if page:c.showPage()
 page+=1;book.append((page,title));c.bookmarkPage('p'+str(page));c.addOutlineEntry(title,'p'+str(page),0)
 c.setFillColor(colors.HexColor('#FAFCFC'));c.rect(0,0,W,H,fill=1,stroke=0)
 c.setFillColor(TEAL);c.rect(0,H-9,W,9,fill=1,stroke=0)
 c.setFont('CN',9);c.setFillColor(MUTED);c.drawString(44,H-37,'TANK TOGETHER  /  '+kicker)
 c.setFont('CN',23);c.setFillColor(INK);c.drawString(44,H-76,title)
 c.setStrokeColor(colors.HexColor('#CCDDDF'));c.line(44,43,W-44,43)
 c.setFont('CN',8);c.setFillColor(MUTED);c.drawString(44,29,'搬鱼缸 · 内部策划调参手册 · 2026-10-13 · v1.0');c.drawRightString(W-44,29,f'{page:02d} / 15')
 y=H-100

def sub(title):
 global y
 y-=4;para(title,style='h',gap=7)
def box(title,text):
 global y
 titlep=Paragraph(title,styles['card']);bodyp=Paragraph(text,styles['body']);width=W-112
 _,a=titlep.wrap(width,900);_,b=bodyp.wrap(width,900);height=a+b+27
 c.setFillColor(PALE);c.roundRect(44,y-height,W-88,height,6,fill=1,stroke=0)
 titlep.drawOn(c,56,y-11-a);bodyp.drawOn(c,56,y-17-a-b);y-=height+12
 if y<48:raise ValueError(f'Box overflow on {page}')
def card(title,key,value,impact,trial,related):
 global y
 para(title+'  <font color="#087F8C">当前 '+escape(value)+'</font>',style='card',gap=2)
 para(escape(key),style='tiny',gap=4)
 para('<b>作用与方向：</b>'+impact,gap=3)
 para('<b>首轮试调：</b>'+trial,gap=3)
 para('<b>联动检查：</b>'+related,style='small',gap=8)
 c.setStrokeColor(colors.HexColor('#DDE8E8'));c.line(44,y+3,W-44,y+3)
 checks.append(key)
def table(headers,rows,widths,fs='table'):
 global y
 data=[[Paragraph(escape(str(v)),styles[fs]) for v in row] for row in [headers]+rows]
 t=Table(data,colWidths=widths,hAlign='LEFT');t.setStyle(TableStyle([
 ('BACKGROUND',(0,0),(-1,0),PALE),('VALIGN',(0,0),(-1,-1),'TOP'),('LEFTPADDING',(0,0),(-1,-1),7),('RIGHTPADDING',(0,0),(-1,-1),7),('TOPPADDING',(0,0),(-1,-1),7),('BOTTOMPADDING',(0,0),(-1,-1),7),('LINEBELOW',(0,0),(-1,0),.7,TEAL),('LINEBELOW',(0,1),(-1,-1),.35,colors.HexColor('#D6E3E3'))]))
 _,height=t.wrap(W-88,900);t.drawOn(c,44,y-height);y-=height+11
 if y<48:raise ValueError(f'Table overflow {page} {y}')

start('策划编辑器调参手册','UNITY / WHITEBOX 3 / INTERNAL')
y-=10
para('让人物容易控制，把协作难度留给搬鱼缸。',style='h',gap=16)
para('针对你截图中的「搬鱼缸 · 玩法参数」窗口。按照面板顺序解释每个控件，说明保存方式、调大与调小的后果、参数之间的关系，以及怎样把试调结果交给程序和队友。',gap=16)
box('先记住这四件事','① 面板里改的是草稿，必须点“保存参数”，下一次 Play 才生效。<br/>② F1 GM 是运行时试调，和这个编辑器窗口不是同一份草稿。<br/>③ 先改一个问题、一次一个变量，再走同一段路线比较。<br/>④ 改了工程不会更新旧 EXE；同组联机必须使用一致的新包。')
table(['查什么','页码'],[
 ('打开面板、保存按钮、截图定位','02-03'),('行走/奔跑，起步/刹停/空中控制，跳跃','04-06'),('鱼缸和角色尺寸、出生与支架','07-08'),('质量/阻尼、驱动力/抓取、头顶支撑','09-11'),('常见手感问题怎么排查','12'),('F1新增项，联机试调与固化流程','13-14'),('验收清单、记录模板与事实来源','15')],[394,113])
para('<b>适用快照：</b>国际版 Unity 6000.6.5f1；SteamWhiteboxRevision3 场景；WhiteboxJumpTrial 配置；参数版本 v0.2.3-whitebox-feel2；新联机协议06。数值核对日期：2026-10-13。',gap=10)
para('所有“首轮试调”均是本手册建议的对比步幅，不是策划冻结值，也不是自动生效的程序限制。测试结果需要团队实际确认。',style='small')

start('01 / 找到面板并保存')
para('菜单：Tank Together → 玩法参数。先停止 Play，再点“读取当前场景配置”。',gap=13)
base=y
im=ImageReader(str(SHOT));iw,ih=im.getSize();dw=247;dh=dw*ih/iw;c.drawImage(im,44,base-dh,width=dw,height=dh,mask='auto')
y=base
for title,txt in [
 ('A · 确认配置','截图应显示 WhiteboxJumpTrial，路径为 Assets/Resources/WhiteboxJumpTrial.asset。'),
 ('B · 状态与保存','“当前参数已保存”表示没有新草稿变化，保存按钮灰色是正常状态。改一个数值后会变成“有未保存的修改”。'),
 ('C · 修改数值','点数字输入。鼠标停在参数名称上可看说明。尺寸、物理项需要展开折叠栏。'),
 ('D · 保存后再运行','点“保存参数（下次运行生效）”，再进入Play。不需要另存场景来替代这个按钮。'),
 ('E · 运行时只读','这个窗口在Play中不允许保存。临时试验使用游戏画面的F1；退出Play后再固化。')]:
 para(title,x=309,width=242,style='card',gap=3);para(txt,x=309,width=242,style='small',gap=11)
y=min(y,base-dh)-13
para('图：用户提供的编辑器截图；用于定位控件，未改动截图内数值。',style='tiny',gap=10)
box('策划每次操作顺序','停止 Play → 读取配置 → 记录原值 → 修改一项 → 保存参数 → 重新 Play → 重复同一测试 → 记录结论。')

start('02 / 保存、恢复与生效范围')
table(['你做的动作','实际结果'],[
 ('编辑器修改数字','只改面板草稿；尚未保存时不会写到配置资产。'),
 ('点击保存参数','写入上方显示的配置资产；下次Play使用。引用同一资产的其他场景也受影响。'),
 ('放弃草稿，恢复已保存值','丢弃本次未保存修改。恢复的是磁盘中已保存值，不是出厂默认。'),
 ('F1应用参数','只改当前运行/房间配置；重开当前检查点。退房/返回大厅恢复随包值。'),
 ('推送工程 / 发新测试包','工程同步与包体更新是两件事；旧EXE不会自动取得新参数。')],[158,349])
sub('保存按钮灰色：按顺序检查')
para('① 是否还在Play；② 是否确实改过数值；③ 是否出现红色校验提示；④ 切换场景后是否重新读取了当前配置。截图中已显示“已保存”，所以当时无需再次保存。')
sub('参数版本：只是标记，不是强度')
para('当前：<b>v0.2.3-whitebox-feel2</b>。对应字段 version。建议经程序确认后用清晰试验编号命名，例如 feel-A01；不要把已有版本名误解成实际包日期。名字和数值都会影响随包联机兼容检查；F1临时配置另有房间revision/哈希。')
checks.append('version')
box('两处校验不完全相同','编辑器主要检查非负数、尺寸关系与有效值；F1另设更窄的试调范围。编辑器“能保存”不等于适合玩法或可被联机GM接受。超出常用区间请先与程序确认，再生成新包。')
para('确认读的是白模配置，不要误改共用的 CarryBaseline.asset。当前正式场景为 SteamWhiteboxRevision3；老提示里写的 WhiteboxCarryLab 是早期场景名。',style='small')

start('03 / 移动：先决定目标速度')
card('自由行走','walkSpeed','4 m/s','空手、不按Shift时的目标速度。调大移动更快，同样滞空时间下通常跳得更远；调小相反。','4 → 4.5；保持起步加速度不变，在平地走同样长度。','只改最高目标，不解决起步慢；起步/松键手感看第05页。')
card('自由奔跑','sprintSpeed','6 m/s','空手按Shift时的目标速度。调大跑得更快，空手跑跳更远；不直接抬高跳跃。','6 → 6.5；分别测直线跑、转弯、落点。','空中能否及时改方向由 airAcceleration 决定；不要同时改跳高。')
card('四人搬运行走','carrySpeed','2.5 m/s','当前实现按“已抓人数 ÷ 本局人数”的平方缩放。联机两人都抓时也是满员目标，不要求凑四人。','2.5 → 2.75；保持人数、抓取人数和方向输入一致。','目标速度不等于实际速度。推力、缸质量、坡度、碰撞和队友反向都会影响。')
card('四人搬运奔跑','carrySprintSpeed','3.5 m/s','抓缸并按Shift时的满员目标速度。调大通常跑跳更远，也更难控制边缘刹停。','3.5 → 3.75；先测平台起步/停车，再测缺口。','不要先用更大起跳力度补水平距离；抓取不齐会严重降速。')
box('抓取人数不足为什么很慢','四人局只两人抓，倍率=(2/4)^2=0.25；2.5m/s目标缩为0.625m/s。两人局两人都抓，倍率=(2/2)^2=1。以上是目标值计算，不是实际测得速度。')

start('04 / 自由操作容错：让人物跟手')
card('地面起步加速度','freeAcceleration','40 m/s^2','只影响空手地面起步、反向和追赶目标速度。调大更利落；调小更缓、更容易感觉拖。','40 → 45；同一站位按方向起步，再做180°反向。','不要与walkSpeed一起改，否则无法判断是更快还是起步更快。')
card('松键刹停加速度','freeDeceleration','60 m/s^2','空手地面松开方向后的减速能力。调大滑得更短；调小滑得更远。','60 → 70；以相同速度经过标记后松键，比较停车距离。','不作用于空中或抓缸刹车；抓缸看carryBrakeAcceleration。')
card('空中调整加速度','airAcceleration','25 m/s^2','空手离地后的水平修正。调大可更快改变落点、也更快趋向空中目标速度；调小惯性保留更强。','25 → 30；测试持续前进、空中松方向、空中反向三种输入。','它不加跳高。空中松键也会受这套水平控制影响，不是完全自由滑翔。')
card('离地补跳窗口（秒）','coyoteTime','0.10 s','空手走出边缘后仍可补按跳跃。调大宽容度更高；调小更严格。不是二段跳，也不用于抓缸跳。','0.10 → 0.12；从同一边缘走下后略晚按跳比较。','编辑器允许0-0.25秒；不要用它弥补过远缺口。')
card('提前按跳缓存（秒）','jumpBufferTime','0.12 s','空手落地前稍早按跳时保存这次输入，窗口内满足起跳条件后触发一次。调大更容易接跳。','0.12 → 0.15；从小台落下，提前按一次跳。','编辑器允许0-0.25秒；太大会产生“我早就松了还跳”的感觉。')

start('05 / 跳跃与刹停：不要混调')
card('搬缸起跳力度（米/秒）','carryJumpVelocity','10.2 m/s','接地且抓缸时，只给按跳的该角色增加向上速度。调大通常跳高、滞空和翻缸风险增加；不是直接抬起鱼缸。','10.2 → 10.5，最多先比较这一档；多人错时发力也要测。','结果受质量比、抓取、支撑、阻尼影响，不按数值同比增高；不会自动全队齐跳。')
card('自由跳高（米）','freeJumpHeight','0.65 m','空手自由跳的目标高度。调大更高、上升更久；同速下通常也更远。碰到顶会达不到目标。','0.65 → 0.70；在平地测，不先拿关卡失败作为唯一判断。','高度已经够而滞空偏长，优先比较下落倍率，不继续加高度。')
card('空手下落重力倍率','freeFallGravityMultiplier','2','仅空手下降、未头顶支撑、未接地时增加下落重力。调大落得更快、同速跳远通常更短；不改上升或目标高度。','2 → 2.2；或与1比较旧节奏。允许范围1-3。','不用于抓缸或头顶托缸；不能靠此改变搬缸滞空。质量变大也不代表自由落体更快。')
card('搬运刹停力度（米/秒^2）','carryBrakeAcceleration','2.5 m/s^2','保持抓缸但松方向时的限力尺度。调大通常停得更快；调小更缓。不是松手后的鱼缸刹车。','2.5 → 3.0；让全员仍按住抓取，统一松开方向。','还受搬运响应、队友输入和碰撞影响；松手后主要看缸的阻尼与接触。')
card('缸底抬升参考（仅记录）','carryJumpHeightTarget','1.33 m / 只读','历史试调参考记录，不参与起跳计算，改它不会改变跳跃。','不要编辑；真实起跳参数变化后请程序重新测量并更新记录。','这不是当前所有人数、所有场景保证达到的抬升高度。')

start('06 / 尺寸：占地、净空与头顶')
card('缸尺寸：X长 / Y高 / Z宽（米）','tankSize','X3.2 / Y0.95 / Z2.2 m','同时改玻璃、物理碰撞和相关支架位置。调大更占通道与转弯空间，也改变旋转惯性；质量不会自动增加。','只选一个轴先改约5%，例如X3.2 → 3.36；先静置、抓取，再走窄道。','Y是竖直高度。长度不是高度；检查角色能否站到缸下、抬升后会不会撞顶。')
card('角色站高（米）','standingHeight','1.7 m','角色胶囊碰撞与头顶锚点高度。调大更高、易碰顶；调小可能够不到缸。','1.7 → 1.75 或1.65，每次只选一侧比较。','应配合缸底高度与抓取垂直范围；站高必须不小于蹲高。')
card('角色蹲高（米）','crouchingHeight','1.1 m','下蹲时目标胶囊高度，也改变抓取锚点。调小蹲得更低、能过更矮空间；调大下蹲幅度更小。','1.1 → 1.05；检查空手与抓缸蹲起，并在低顶下测试站起。','必须满足站高 ≥ 蹲高 ≥ 碰撞直径。下蹲到目标需要过程，不是瞬间变高。')
card('角色碰撞直径（米）','playerDiameter','0.6 m','决定胶囊横向占地。调大更容易挤到队友和窄道；调小更容易穿窄缝。','0.6 → 0.55 或0.65；先看四人并排和出生是否重叠。','不是抓取半径，也不改质量。变细后需要复查角色模型与碰撞是否合理。')
box('尺寸改动的最小验收','出生不互挤 → 角色能走到缸下 → 每个人能抓到 → 站立/蹲下无明显穿插 → 缸在支架上能稳定停放 → 最窄通道与最低顶能通过。')

start('07 / 站位与支架：先看场景类型')
card('出生半间距：X / Z（米）','spawnHalfSpacing','X0.9 / Z0.55 m','基础实验站位到中心的半间距，两侧距离为其两倍。调大更分散、调小更集中；第二格子标签虽写Y，实际用于地面Z。','基础平地实验可每次±0.05m；当前白模黄球出生位置不靠这里移动。','白模3由WhiteboxCarryLevel.RespawnPoint覆盖：在已编排检查点playerFeet周围固定偏移±0.7m。调整出生位置需程序改关卡数据。')
card('初始缸底高度（米）','carryBottomHeight','1.7 m','决定本段鱼缸起始底面与对应支架高度。调大更高但可能够不到；调小可能压入角色。','1.7 → 1.75，每次约0.05m；先看静止抓取。','匹配站高和抓取垂直范围；当前白模还会据此重建停车检查点支架。不是跳高开关。')
card('支架内缩：X / Z（米）','standInset','X0.2 / Z0.15 m','支架中心从缸边往内的距离。调大支架靠中间；调小更靠边，改变承托与角色避让。','只改一个方向，每次约0.02m。第二格虽标Y，实际是Z。','不改变抓取范围。当前关卡的出生和停车支架均受影响，需要验证四脚接触和存档触发。')
card('支架宽度（米）','standWidth','0.25 m','支架横截面宽度。调大更易接触承托，也更容易挡路；调小支撑区更窄。','0.25 → 0.28 或0.22；看停车是否稳定、角色能否离开。','不是鱼缸宽度，也不是头顶支撑力。增宽不应掩盖停车点本身的布置问题。')
box('保存时会检查的几何关系','基础站位不得超出可抓范围；两倍半间距不得小于角色直径。站高 ≥ 蹲高 ≥ 碰撞直径 > 0。支架内缩不得超过相应半尺寸。保存通过后仍需要实际出生/停车验收。')

start('08 / 质量与阻尼：重量不等于重力')
card('角色质量','playerMass','7 kg','影响人与缸的质量比、碰撞推挤和搬缸传力。当前空手移动使用“质量×目标加速度”计算力，因此不能简单推断加重就起步慢。','7 → 7.5；其他驱动力不变，分别测空手和抓缸。','同样固定推力下较重物体更难加速，但空手控制器会补偿质量；自由落体不会因质量更大而更快。')
card('鱼缸质量','tankMass','24 kg / 四人基准','实际联机缸质量按本局人数/4缩放：2人12kg、3人18kg、4人24kg。调大通常更难搬与抬升；调小更易受扰动。','基准24 → 26；至少用2人和4人各测一次。','中途松手不会改变本局质量。不要把两人模式12kg误认为配置丢失。需联动搬运最大力、支撑上限。')
card('鱼缸移动阻尼','tankDamping','0.8','衰减鱼缸所有方向速度，包含水平和竖直。调大更耗散，调小更保留惯性；跳跃轨迹也会变化。','0.8 → 0.9；分别测抓住走、松方向停、完全松手滑行、跳跃。','搬运输入会补偿部分水平阻尼，但受最大力限制。它不是纯粹的刹车或滞空旋钮。')
card('鱼缸旋转阻尼','angularDamping','5','衰减旋转速度。调大转动更慢、更不易继续甩动；调小更灵活也更晃。不会自动扶正已歪的缸。','5 → 5.5；同一次转弯/错时起跳比较。','如果增加后转角变钝，不要继续无限加大；先检查发力不同步和质量比。两人扶正辅助另在F1。')
box('观察对象要一致','“人物惯性”优先看自由起步/刹停/空中控制；“缸滑得远”看抓住还是已经松手；“缸翻了”看旋转、发力时差和抓取。先分清对象，再动参数。')

start('09 / 驱动力与抓取：能动、能抓、抓稳')
card('搬运移动响应','carryMotorGain','65','把水平速度误差转成驱动力，松方向时也参与刹停。调大反应更快，太大可能拉扯抖动；调小更柔但拖。','65 → 70；同样抓取人数测起步和反向。','有方向时受搬运最大力限制；松方向时受搬运刹停限力限制。')
card('搬运最大力','carryMotorForce','70 N / 每个角色','抓缸且有方向输入时的水平驱动力上限。调大更能推得动、抵抗坡道；调小可能追不上目标速度。','70 → 75；看缸确实受阻还是被墙卡住。','不是最高速度；不限制起跳增速。松方向刹停使用另一套限力，不能只改此项。')
card('抓取边缘内缩（米）','gripInset','0.12 m','从缸底边缘排除一圈区域。调大可抓区域更小；调小更容易在边缘抓到。','0.12 → 0.10；只比较边缘位置的发起抓取。','只影响能否开始抓，不增强抓力，也不移动已经抓住的锚点。')
card('抓取垂直范围（米）','gripVerticalReach','0.23 m','发起抓取时允许头顶与缸底的高度误差（缸局部方向）。调大更容易够到；调小要求贴得更准。','0.23 → 0.25；在固定水平位置试不同头顶高度。','太大可能看起来隔空抓缸。先核对角色站高与初始缸底高度；不是抓住后的伸缩限距。')
card('抓取关节限距（米）','gripLimit','0.12 m','抓住后两端锚点允许的平移活动范围。调大连接更松、更晃；调小更紧，也可能加重强约束抖动。','0.12 → 0.10 或0.14，每次约0.02m。','不是初次抓取距离，不锁旋转，也不是超过后自动松手的阈值。')

start('10 / 头顶支撑：和抓取是两套作用')
para('角色站在缸底下面，即使没有抓取，也可能由头顶产生向上支撑。支撑只推不拉，对角色施加反向力；三项共同决定“顶得多硬、有没有振荡、最多能顶多大”。',gap=15)
card('头顶支撑刚度','supportStiffness','3500','按头顶挤入支撑区的程度计算向上力。调大更硬、弹开更快，过大可能顶跳；调小更软。','3500 → 3750；在静止缸下缓慢进入，再观察是否弹跳。','最终受最大支撑力截断。已经到上限时，仅增刚度不会继续增加峰值。')
card('头顶支撑阻尼','supportDamping','100','根据头顶与缸底的竖直相对速度修正支撑力。调大通常更抑制振荡，但太大也会产生冲击；调小速度修正更弱。','100 → 110；在同一刚度下比较轻微顶起后的上下摆动。','它不是鱼缸整体阻尼；分离时支撑会减弱，不会把鱼缸吸在头上。')
card('头顶最大支撑力','supportMaxForce','240 N / 每个角色','单人头顶向上支撑力上限。调大允许更强承托，也可能顶飞；调小可能托不住。','240 → 260；先轻触和静置，再测试多人同时支撑。','需与质量、刚度、阻尼一起看，但一次先只改一个。不限制抓取关节传力。')
box('推荐调节顺序','先确认头顶和缸底几何位置正确 → 看是否撞上支撑力上限 → 在质量不变时微调刚度 → 用阻尼减少上下摆动 → 最后检查多人同时接触。不要一开始同时调三项。')
para('已停用字段：motorGain / motorForce。它们仍保存在旧资产里用于兼容历史记录，但当前空手控制使用第05页的加速度，不应再把这两个旧值当调参入口。',style='small')

start('11 / 常见问题：优先改哪一项')
table(['症状','先检查 / 再试调'],[
 ('空手松键后还滑很远','确认已落地且未抓缸，再增加freeDeceleration；空中滑行看airAcceleration。'),
 ('空手高度够，但落地太慢','保持freeJumpHeight，逐档比较freeFallGravityMultiplier；注意同速跳远会缩短。'),
 ('空手跳不远，高度已够','先确认起跳前水平速度与是否一直按方向，再微调walk/sprint或airAcceleration。'),
 ('四人齐跳把缸掀翻','先测发力时差与站位；小降carryJumpVelocity，再分别比较旋转阻尼。不能只提高支撑力。'),
 ('抓着缸走很慢','确认当前人数、实际抓取数、是否碰墙/坡道；再看carryMotorForce与carryMotorGain。'),
 ('能走到缸下却抓不到','看站高、缸底高度、水平边缘位置与缸倾角；分别核对gripVerticalReach和gripInset。'),
 ('抓住后晃动或拉扯明显','先恢复一次只改一项；比较gripLimit、搬运响应和旋转阻尼，检查多人反向输入。'),
 ('改出生半间距没效果','当前白模检查点使用独立编排出生点，需要改关卡数据；不是保存按钮失效。'),
 ('只有加入方感觉拖','F3记录往返时间/快照间隔；看相机和画面平滑，再查网络。不直接把全局走速调高补延迟。')],[149,358])
sub('两组可直接照做的 A/B 比较')
para('<b>A 空手下落：</b>保持跳高0.65、空中修正25和走速4，比较下落倍率2与2.2。在同一平地持续按前进起跳，各做5次，记录滞空感觉、落点和是否更难过缺口。')
para('<b>B 松键拖沓：</b>保持走速4、起步40，比较刹停60与70。相同位置进入匀速后松键，各做5次，标注停车距离。若只在空中拖，不用这组测试下结论。')
para('这些是对比实验，不是“推荐最终数值”。涉及鱼缸时还要重复两人/四人和同向/错时输入。',style='small')

start('12 / F1还有哪些编辑器没有的项')
para('下面13项目前在运行时F1面板。它们没有加入你截图里的“玩法参数”窗口。试调确认后请程序写入对应脚本/场景默认值，不能只保存WhiteboxJumpTrial就以为全部固化。',gap=10)
table(['F1分类 / 参数','当前值','调大后主要影响'],[
 ('两人辅助 / 扶正强度','160','两人都抓、倾角小于45°时更强地扶正；松手撤销。'),
 ('两人辅助 / 抗旋转阻尼','18','更抑制横向晃动；不用于三/四人。'),
 ('两人辅助 / 力矩上限','40 N·m','允许更强辅助；0关闭，不能救回严重翻倒。'),
 ('相机 / 目标距离','7.5 m','镜头更远；遇墙仍前移，不允许隔墙观看。'),
 ('相机 / 跟随响应','25','镜头追得更紧；小值更柔但拖尾更多。'),
 ('相机 / 离墙拉远速度','10 m/s','离开遮挡后更快恢复目标距离。'),
 ('相机 / 水平灵敏度','0.12','鼠标同样移动，左右转得更多。'),
 ('相机 / 垂直灵敏度','0.10','上下看转得更多；俯仰边界仍-70°至80°。'),
 ('机关 / 升降台速度','2.5 m/s','上升更快；行程仍由地图定义，不改变终点。'),
 ('机关 / 等齐延迟','2 s','全员到齐后等更久，离开重新计时。'),
 ('联机 / 状态发送频率','30 Hz','状态更密、带宽更高；物理步长不变。'),
 ('联机 / 画面平滑响应','25','加入方更跟手，但网络抖动更明显。'),
 ('联机 / 输入保活频率','10 Hz','无变化时更频繁报告；按键变化原本就立即发送。')],[209,76,222])
para('以上是当前随包默认值，不是已冻结的正式平衡。提高频率不等于消除网络延迟；当前没有加入方本地预测。',style='small')

start('13 / 联机试调，再固化到工程')
sub('在同一房间里让策划快速比较')
for t in [
 '1. 所有人使用同一份 Whitebox3-GM-20261013 测试包。至少两人进入；编辑器面板保持关闭或只读。',
 '2. 房主按F1，改一项草稿，点击“应用全员并重开本段”。全员暂停，按当前检查点重建，保护鱼缸。',
 '3. 查看配置revision/短哈希与确认人数。全员关闭GM，房主开始本段；队友不能单独应用另一套数值。',
 '4. 试完打开F1，点顶部“一键复制当前生效参数”，粘贴到文本文件或发给程序。复制不包含未应用草稿。',
 '5. 保留对比记录与JSON。房主退房再建房会恢复随包值；可把JSON“导入草稿”，再应用。晚加入自动取得房主当前配置。']:
 para(t,gap=10)
box('把有效方案变成下次默认','先复制已生效JSON并命名方案 → 停止Play → 编辑器读取正确配置 → 把本手册04-11页对应数值填入并保存 → 请程序固化第13页的额外项 → 重新打包 → 全员换包 → 复验。')
para('<b>当前限制：</b>你截图里的编辑器窗口没有“一键粘贴F1 JSON”或复制按钮。不要把整段JSON直接贴进某个数值格；F1里才能整套导入，也可以把JSON交给程序统一写回。',gap=12)
sub('策划和程序交接时一起给出')
para('方案名、配置JSON、改动理由、哪些数值变了、测试人数/抓取人数、测试包名、路线位置、成功率/异常、是否希望固化。质量/尺寸变动尤其要附出生、停车和窄路结果。')
para('保存配置资产会生成Git改动。由负责人员检查并提交对应资产与必要.meta；本手册不会替你自动提交、上传或重新打包。已发布旧ZIP的内容不会随着工程改变。',style='small')

start('14 / 验收与交接记录')
table(['最小验收','通过标准 / 要记录的现象'],[
 ('空手走跳','起步、180°反向、松键、空中松键/反向、提前按跳、边缘补跳；无难以控制的拖沓。'),
 ('搬缸','2人和4人分别测试；抓取/松手、同向走跑、错时跳、蹲起、落地、刹停。'),
 ('关卡与失败','出生不重叠、能抓到、窄道/低顶、停车触发、碎缸失败后恢复到正确检查点。'),
 ('联机一致性','全员同包；改参后版本/哈希一致；晚加入同步；队友只读；退房恢复随包值。')],[117,390])
sub('一条完整记录的模板')
box('方案：____________  日期 / 策划：____________','基于测试包：____________　人数 / 抓取人数：____________<br/>修改项：____________　原值 → 新值：____________<br/>位置与输入：____________　重复次数：____________<br/>观察结果 / 成功次数：____________<br/>副作用：____________　保留 / 回退 / 待确认：____________<br/>附件：当前生效JSON、必要截图或视频。')
sub('本手册依据与有效边界')
para('核对源均位于正式工程 Unity/TankTogether：<br/>- Assets/Editor/CarryParametersWindow.cs：菜单、保存和校验。<br/>- Assets/Resources/WhiteboxJumpTrial.asset：本手册当前数值。<br/>- Assets/Scripts/M0CarryLab.cs、WhiteboxCarryLevel.cs：实际运动、人数缩放、出生、支架与抓取。<br/>- Assets/Scripts/CarryTuning.cs、CarryGMPanel.cs、SteamCarryTuning.cs：F1参数与联机同步。',style='small',gap=10)
para('文档以当前原型实现为准，部分旧悬浮说明仍沿用早期四人/力驱动措辞；本手册已明确相应差异。它不替代策划最终规则，也不表示全关卡或真实异地手感已验收。本轮仅写文档，没有更改游戏数值。',style='small')
assert page==15
c.save()
reader=PdfReader(str(PDF));assert len(reader.pages)==15
for i,p in enumerate(reader.pages):
 txt=p.extract_text();assert len(txt)>100,(i,len(txt))
# Every visible field is described once; vector fields stay one editor item.
source=(ROOT/'Unity/TankTogether/Assets/Editor/CarryParametersWindow.cs').read_text(encoding='utf-8')
fields=set(re.findall(r'F\("([^"]+)"',source))|{'version','tankSize','spawnHalfSpacing','standInset'}
assert fields==set(checks),(fields-set(checks),set(checks)-fields)
report={'pages':15,'editorControlsCovered':len(fields),'controls':sorted(fields),'sha256':hashlib.sha256(PDF.read_bytes()).hexdigest(),'bytes':PDF.stat().st_size,'sourceAssetSHA256':hashlib.sha256((ROOT/'Unity/TankTogether/Assets/Resources/WhiteboxJumpTrial.asset').read_bytes()).hexdigest(),'scope':'documentation-only; source-checked; suggested increments are not tested/frozen balance','chapters':book}
(OUT/'策划调参手册_内容校验.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False))
