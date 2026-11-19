# 搬鱼缸 · Tank Together

多人合作 Steam 游戏的准备工程。英文名为工作名，尚未进行商标或商店重名核查。

## 目录

- `Unity/TankTogether/`：国际版 Unity URP 工程及独立物理、Steam 联网测试场，Hub 打开此目录。
- `docs/`：项目状态、开发环境和决策记录。
- `tools/`：本地启动、编译验收、Windows 构建工具。
- `包体/`：按版本整理的测试包说明与校验清单；ZIP 从私有 GitHub Releases 下载后放入对应目录。
- `.codex/config.toml`：本机 MCP 连接配置。

总目录：`D:\工作区\Project\Game\搬鱼缸项目`。

## 开始

在总目录执行 `./tools/unity.ps1 -Action Open`。如使用 Unity Hub，选择 `Unity/TankTogether`，不要选择总目录。

Main 保持基础场景；`M0CarryLab` 为本地搬运实验，`SteamCarryLab` 为 Steam 异地联网验证入口。当前已验证本机 Steam 建房与主机重建，双端异地手感、主机迁移尚未验收。代码托管于 [GitHub 私有仓库](https://github.com/1205971577/tank-together)；成员由项目负责人添加。

最新测试包见 [包体目录](包体/README.md)，开发记录见 [2026-10-10 开发日志](docs/开发日志/2026-10-10-M0与Steam联网验证.md)。

2026-10-12：按策划 V0.2.2 对齐尺寸与运动基线，详见 [实施与测量记录](docs/尺寸与运动基线实施记录.md)。新版测试包已上传私有 Releases，下载入口见包体目录；所有后续工作先依据策划里程碑及前置验证推进。

同日继续了 [M1悬挂与停车实验](docs/M1悬挂与停车实验.md)：独立本地实验场已提供短救援与停车/驶离的受控证据，仍不代表长期悬挂、正式检查点或M2联机验收完成。

## 协作与资源

主仓库为 GitHub（origin）；Gitee（gitee）保留迁移前历史备份，不自动双向同步。首次克隆前安装 Git LFS 并执行 `git lfs install`，再克隆主仓库。已有 Gitee 协作者需在 GitHub 单独邀请，权限不会自动迁移。

贴图、模型、音频等按 .gitattributes 进入 LFS；场景、Prefab、.meta 保持普通 Git 文本。白模SKP和截图等二进制资源按上述规则使用LFS，拉取后需要下载完整资源；未启用付费或自动扣费。

## 2026-10-15 当前试玩入口

- Unity 6000.6.5f1 打开 Unity/TankTogether，加载 Assets/Scenes/SteamFormalWhiteboxV1.unity 并 Play。菜单 Tank Together → Steam → Open Whitebox Co-op 也打开此场景。旧白盒及其专用构建入口已退役。
- Steam 合作固定为三人。房主和两位队友到齐，队友准备后由房主开局。对局中普通掉线可重连，其他队友可以继续。编辑器的“进入关卡·伸缩 B”和旧版对照 A 都模拟 P1～P3，Tab 在三人间切换。B0 沙盒只显示 P1。Q 变矮、E 长高，F2 打开伸缩参数，F4 本地传送到鱼缸旁。
- 参数面板：Tank Together → 玩法参数 → 读取当前场景配置。当前配置 Assets/Resources/FormalWhiteboxV1.asset，缸长4.2米、宽3.2米，最高身高5米。保存后下次 Play 生效；旧窗口草稿不会自动覆盖或切换。
- 第二版白盒已接入；高处黄球、绿色区域黄球和台座旁黄球均为中途存档点。用户已确认末端白色平台暂作终点：完整鱼缸进入平台中央范围后通关，无须放下；人物单独到达或鱼缸破碎不算。Steam 先保存完成状态，再进入结算/投票；本地模拟仅提示送达，不写联机存档。
- 当前工程 Steam 协议20，App ID 480内部验证；第二版白盒新增5个上下往复方块（0.6米/秒、单程2米）及存档点，保留白色机关 F 升降切换和10月17日修复。旧 LevelLift-20261016 测试包使用协议17。联机需要所有玩家使用相同版本。构建入口为 Tank Together → Steam → Build Formal Whitebox Windows。
- 验证与未完成项以 docs/MAIN_THREAD_HANDOFF.md 的最新记录为准；全路线、真人异地和主观手感需单独验收。

2026-10-16：新模型中央单柱已恢复，黄点不生成支柱；完整鱼缸到达即存档，重生由实际队员承托。此前四柱存档要求已按用户最新确认覆盖，见[修正日志](docs/开发日志/2026-10-16-中央单柱与无柱存档修正.md)。
