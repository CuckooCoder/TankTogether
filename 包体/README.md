# 测试包体

## 当前版本：新关卡与全员带缸升降 · 2026-10-16

**本地包已生成，未上传Release。** [测试说明](LevelLift-20261016/测试说明.md) · [校验清单](LevelLift-20261016/manifest.json)。

使用 `LevelLift-20261016/TankTogether-LevelLift-20261016.zip`，完整解压后运行 `Windows-Test/TankTogether-Steam.exe`。协议17，2～4人，全员必须使用本包。

包含最新白盒、起点/鱼缸台座/高处三处检查点，以及全员与完整鱼缸进入平台后等待2秒、跳跃不打断的升降规则。本地和Steam正式联机共用。

构建与单账号Steam建房/重建/GM冒烟通过，ZIP校验通过；异地与完整路线待试玩。末端终点仍等策划，F开关/下降未实现。

## 已被新版替代：中央单柱与无柱存档 · 2026-10-16

**本地包已生成，尚未上传Release。** [测试说明](SinglePost-20261016/测试说明.md) · [校验清单](SinglePost-20261016/manifest.json)。

使用SinglePost-20261016/TankTogether-SinglePost-20261016.zip，完整解压运行Windows-Test/TankTogether-Steam.exe。协议15，全员同包。单机与联机均保留原模型中央单柱；黄点无柱，鱼缸到达激活存档，重生由队员承托。替代下方仍有旧四柱的协议14包。

35项局部存档/物理专项通过，真实异地和全路线待试玩。末端终点仍等策划模型。


## 已被修正版替代：正式第一版白盒 · 2026-10-16

**本地包已生成，Release 上传待确认，暂无在线下载。** · [测试说明](FormalWhitebox-20261016/测试说明.md) · [校验清单](FormalWhitebox-20261016/manifest.json)。

下载 TankTogether-FormalWhitebox-20261016.zip，完整解压运行 Windows-Test/TankTogether-Steam.exe。协议14，2～4人，所有人换同一新包。包含新地图、5米伸缩、身高稳定性、最新角色动作、大厅与房主存档/重连、70项GM。

成品建房/重建、GM往返和主菜单/本地关卡画面已检查。**路线末端终点仍等策划模型，目前不触发通关**；异地多人及全路线需本轮试玩验收。旧包仅保留追溯，不作为本轮联机版本。


## 历史本地测试包：续玩与重连（尚未上传）

[Recovery-20261015 测试说明](Recovery-20261015/测试说明.md) / [校验清单](Recovery-20261015/manifest.json)。

ZIP：Recovery-20261015/TankTogether-Recovery-20261015.zip。完整解压运行 Windows-Test/TankTogether-Steam.exe。

包含新版主菜单/选档大厅、平头支撑与64项调参，以及跨次续玩、坏档保护、保存失败处理、断线重试和重连防误抓。协议12，全员使用同包。恢复36项和最终EXE两次独立启动通过；真实异地、全路线及本轮成品视觉巡检仍待验收。没有提交、推送或上传。

## 之前的本地测试包：正式流程整合（尚未上传）

[Formal-20261015 测试说明](Formal-20261015/测试说明.md) / [校验清单](Formal-20261015/manifest.json)。
ZIP：Formal-20261015/TankTogether-Formal-20261015.zip，完整解压后运行 Windows-Test/TankTogether-Steam.exe。
包含 2～4 人、默认伸缩 B、五槽存档、准备/身份重连/复位投票、设置改键与装扮、末组四柱结算/报告/重玩。协议10，所有队友必须换同一包。编辑器专项和单账号独立 EXE 冒烟通过，真实异地及全路线仍待验收。源码与 ZIP 本轮均未提交/上传。

## 之前的本地测试包：伸缩实验（尚未上传）

[Stretch-20261015 测试说明](Stretch-20261015/测试说明.md) / [校验清单](Stretch-20261015/manifest.json)。
ZIP：Stretch-20261015/TankTogether-Stretch-20261015.zip，解压后运行 Windows-Test/TankTogether-Steam.exe。
含角色动画、原 A、本地 B0、关卡伸缩 B；Steam B 需所有队友勾选一致。协议 08，异地多人尚未验收。本轮源码推送不包含 ZIP，尚无此包的 Releases 下载。

## 之前的本地 3C 测试包（尚未上传）

[Whitebox3-3C-20261014 测试说明](Whitebox3-3C-20261014/测试说明.md) / [校验清单](Whitebox3-3C-20261014/manifest.json)。

ZIP：Whitebox3-3C-20261014/TankTogether-Whitebox3-3C-20261014.zip，完整解压后运行SteamWhitebox/TankTogether-Steam.exe。F1人物走跳可切换A旧版/B新版，应用后重开本段；默认B。修正移动平台接地/起跳，空手空中松键减速度8，相机纵向跟随18，其余原走跳/搬运目标保留。

协议07、53项GM参数，所有队友使用同一包。旧50项配置不能直接导入。人物仍为胶囊，昨天美术已同步到仓库但未绑定动画。专项物理/相机/GM/协议回归、开场P1连续两缺口及成品单账号Steam烟测通过；整关、真实异地和主观手感仍待验收。未上传。

## 上一版本（本地保留）

[Whitebox3-GM-20261013 测试说明](Whitebox3-GM-20261013/测试说明.md)：协议06，原50项GM，作为3C调整前完整包保留。

每个版本独立一个文件夹，保留测试说明和 manifest.json（文件大小、SHA256、协议版本）。ZIP 上传到本仓库私有 GitHub Releases，避免二进制历史使源码仓库持续膨胀。仓库成员登录 GitHub 后可下载；不公开发布。

## 之前已上传：大厅与白模联机测试包

- [lobby-20261013 下载](https://github.com/1205971577/tank-together/releases/tag/lobby-20261013)：启动先选本地四人模拟或 Steam 2～4 人好友联机。
- [测试说明](Lobby-20261013/测试说明.md) / [校验清单](Lobby-20261013/manifest.json)。
- ZIP：TankTogether-Lobby-20261013.zip，完整解压后运行 TankTogether-Steam.exe。
- 参数面板只在Unity编辑器使用；包体使用构建时保存的参数。新版协议03，联机双方使用同一包。
- 本机大厅切换和Steam建房/重建已验证；异地双真人、白模全程、正式检查点仍待验证。仅内部私有预发布。

## 之前的实验版本（已上传私有预发布）

- [m1-20261012 下载](https://github.com/1205971577/tank-together/releases/tag/m1-20261012)：独立本地悬挂/救援与停车候选实验，目录 `包体/M1-20261012/`。[试玩说明](M1-20261012/测试说明.md)。不是联网更新，仍保留下方Steam基线包。

- [baseline-20261012 下载](https://github.com/1205971577/tank-together/releases/tag/baseline-20261012)：V0.2.2 尺寸与运动基线，协议 `tank-together-steam-02`。
- 本地目录：`包体/Baseline-20261012/`，分别包含 M0 本地实验 ZIP 与 Steam 联网实验 ZIP。
- [测试说明](Baseline-20261012/测试说明.md) / [校验清单](Baseline-20261012/manifest.json)。此版与下方旧包不兼容，联机双方必须使用同版。

## 历史版本

- 版本：`steam-test-20261010`，Windows x64 内部预发布。
- [下载页面](https://github.com/1205971577/tank-together/releases/tag/steam-test-20261010)
- 本地目录：`包体/Steam-20261010/`。
- ZIP：`TankTogether-Steam-20261010.zip`，完整解压后启动 `TankTogether-Steam.exe`。
- [测试说明](Steam-20261010/测试说明.md) / [校验清单](Steam-20261010/manifest.json)。

Steam App ID 480。两台电脑使用不同 Steam 账号、互加好友、运行同一份包。已验证单机 Steam 建房、离开后重建；双账号异地连接与手感待人工验证，主机迁移尚未实现。
