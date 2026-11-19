# Steam 最小异地联网验证

日期：2026-10-10。用户决定直接验证异地 Steam 路线，不做局域网产品入口；授权先用 Spacewar App ID 480 内部测试。当前是生产路线的最小实验，不是正式联机验收。

## 架构与边界

Steam 好友房间负责进入会话，Steam Networking Sockets 的 Relay 连接负责传输，NGO 负责连接与消息。玩家房主统一计算四个角色和鱼缸物理；其他端发送操作意图、平滑显示权威状态。不能把 Steam 大厅自动换房主当作游戏模拟迁移。

固定 NGO 2.13.3、社区 Facepunch Transport 2.0.0（提交 `0fab638470379ace12b0149dfb41c043d23dbce5`）、传输包自带的 Facepunch.Steamworks 2.3.2（本轮构建 Windows x64，不重复安装另一份 DLL）。Unity 6000.6.5f1 和 MCP 固定版本不变。NGO 2.7.0 在此编辑器上产生 EntityId 废弃接口编译错误，因此没有继续采用。

本实验未采用 NetworkTransform 分别同步约束两侧。应用层使用有版本的小消息：客户端输入按可靠顺序送达，房主固定步应用；快照 20 Hz、不可靠有序传输，含回合、Tick、缸位置旋转速度、四人的位置高度、在线/抓取/支撑/失败状态。约束只在房主创建。客户端没有运动预测或重模拟；平滑显示会增加延迟，需真人验证后决定预测范围。

客户端连接 ID 决定可操作角色，客户端不能指定他人槽位。房间版本及随机会话标记在连接审批阶段检查；480 是共享示例应用，好友房间避免混入公开测试列表。房间号与会话标记不提交仓库。当前不是完整反作弊或商业认证方案。

## 两台电脑操作

1. 两台 Windows x64 电脑使用不同 Steam 账号，登录 Steam 桌面客户端并互加好友。
2. 两端解压同一份完整构建，不能只传 exe。保持 exe、Data、UnityPlayer、原生插件和 `steam_appid.txt` 的目录关系。
3. 房主启动 `TankTogether-Steam.exe`，点击“创建好友房间”，复制房间号，手动发给队友。
4. 队友启动同一程序，粘贴数字房间号，点击“加入房间”。看到自己的 P 编号才表示游戏连接成功；仅进入 Steam 房间不算。
5. 按 Enter 操作：WASD、鼠标看向、左键按住抓取、Space 跳跃、Ctrl 蹲下、Shift 奔跑。Esc 松手并释放鼠标。仅房主 R 重置。
6. 未加入玩家没有碰撞/承托/模拟输入。两人搬运速度沿用原临时承重曲线，明显慢于四人；不要据此改变最终人数平衡。

若 Windows 弹出网络权限提示，由电脑所有者允许该测试程序所需权限；不要关闭防火墙。Steam 显示 Spacewar 是 480 测试身份，不代表上架。

## 第一轮人工记录

| 测试 | 应观察的结果 |
|---|---|
| 建房、加入 | 两端明确角色编号，房间号一致，最多四人 |
| 各自移动与转镜头 | 只控制本人，镜头转动本身不拖缸 |
| 一起抓取/同向/反向 | 两人的输入均有作用，两端看到同一个缸 |
| 松手、重新抓取 | 两端状态一致，预按进入不自动抓 |
| 碰墙碎裂、房主 R | 碎裂由房主判定，重置后旧输入不能抓取新缸 |
| 客户端离开 | 房主侧该人的角色/承托/抓取立即移除，其余继续 |
| 主机退出 | 本版明确报告连接断开；这是未解决项，不算 D-08 通过 |
| 网络卡顿 | 记录往返延迟、快照间隔峰值、明显回拉/拖手/视觉碰撞差异 |

分别记录城市、网络运营商、测试时间、版本、两端画面和日志。首轮两人通过后仍须四真人不同电脑/网络验证；本机启动、协议检查或截图均不能替代。

## 暂定处理与未完成项

- 1 秒未收到有效输入时先停止该玩家动作并解除抓取，恢复后须松开再按；超时到真实断线之前仍保留人体碰撞。此为实验失联策略，F-09 未冻结。
- 房主重置递增回合，丢弃旧回合输入与快照；暂用手动重置，不冒充完整检查点/死亡复位。
- 主机迁移、客户端预测、完整认证/滥用防护、Steam 邀请覆盖所有启动路径、检查点和重连未完成。
- 碰撞严格按房主权威判定；客户端平滑画面与真实接触的公平性 F-05 待异地测试。
- 正式上线须替换游戏自己的 App ID，并重新验证 Steamworks 配置、拥有权和发布包；480 仅用于当前内部接入。

## 开发者复现

Unity 菜单 `Tank Together > Steam > Open Network Lab` 创建/打开独立场景；`Check Protocol` 检查序列化、异常输入及回合/顺序过滤；`Build Windows` 仅构建该场景到 `Unity/TankTogether/Builds/SteamCarryLab`，不改变 Main 或编辑器 Build Settings 场景列表。

独立程序 `-steam-smoke -steam-report <绝对路径>` 会在当前账号上短暂建立好友房间，验证 Steam 初始化/创建房间/主机监听后退出；报告中没有远程玩家，因此不能称为双端联机成功。

会话日志位于 `%USERPROFILE%/AppData/LocalLow/DefaultCompany/Tank Together/steam-session-*.log`，Unity `Player.log` 位于同目录。开发测试可用 `-logFile` 指定输出。

## 来源

- [Steam 网络](https://partner.steamgames.com/doc/features/multiplayer/networking)
- [Steam Spacewar 示例](https://partner.steamgames.com/doc/sdk/api/example)
- [Unity 社区传输适配](https://github.com/Unity-Technologies/multiplayer-community-contributions)
- Facepunch 使用上述固定传输包中自带的版本及许可。曾下载 2.5.2 评估，但发现包已自带 SDK，因此没有重复导入。

验证结果以 `docs/m0-evidence/steam-*.json` 的最新记录为准；没有相应报告的项目保持待验证。
