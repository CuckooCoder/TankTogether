# 搬鱼缸项目约束

- 本目录为用户明确要求创建的搬鱼缸总目录；英文工作名 Tank Together。不要误用搬浴缸或今日守护的目录、玩法、工具链。
- 工程位于 `Unity/TankTogether`；事实源为 `docs/MAIN_THREAD_HANDOFF.md`、`docs/项目准备.md`、工程 Packages/manifest.json 与 ProjectSettings/ProjectVersion.txt。
- 国际版 Unity 6000.6.5f1；MCP for Unity v10.3.0，包提交固定。升级前重新验证。
- 普通脚本直接编辑；场景、Prefab、材质使用 Unity API/MCP，保留所有 .meta。
- MCP 仅访问 127.0.0.1:8088；操作前列出实例，选择 TankTogether 实例并核对完整工程路径。旧搬鱼缸测试工程可能同时在线，不能凭默认实例执行写入。
- 同一工程只允许一个编辑器；GUI 打开时使用 MCP，批处理前保存并关闭该工程。
- 用户已授权迁移至 GitHub 私有仓库，origin 为 GitHub，gitee 为历史备份；本总目录使用独立 Git 仓库。不得上传父仓库其他项目，不改成公开，不代为添加成员。
- 每轮更新 docs/MAIN_THREAD_HANDOFF.md，区分命令成功、实际验证通过、待人工试玩。空场景验收不是多人玩法验收。
- 文档优先中文。继续当前聊天，不自动新建聊天或 worktree。

- 美术二进制按 .gitattributes 使用 Git LFS，.meta、场景和 Prefab 保持文本。推送前检查待提交范围；不要自动混入未提交资料。

- 用户要求（2026-10-12）：所有推进先依据策划文档计划，明确当前里程碑、对应规则和前置验证；不自行扩展玩法或跳过阶段验收。规则以 V12 的最终确认清单为准，尺寸与运动试调以 docs/文档/初版尺寸与运动基线_V0.2.2.html 及 docs/尺寸与运动基线实施记录.md 为准。待实测数值和遗留问题不能写成已冻结规则。
