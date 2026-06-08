### 主要更新内容

- 同步 Shasnow main 分支后续更新到 Justice dev。
- 保留 Justice 独有的脚本仓库、自定义任务列表、自定义任务配置等功能。
- 前端程序启动时请求管理员权限，避免启动需要提权的 `SRA-cli.exe` 时报错。
- 记忆窗口大小和位置：在设置中启用后，SRA 会记住上次关闭窗口时的大小和位置。
- SRA-server：新增服务端应用，可通过 HTTP 协议与 SRA 通信。
- 远程后端支持：桌面端可以通过 HTTP 连接远程 SRA 服务端，实现跨设备任务管理。

### 功能调整

- 更新任务结束后通知的截图页面，增加截图信息量。 #198
- 更新问候语。

#### 远程后端

- 桌面端新增远程后端模式，可通过 HTTP 连接远程 SRA 服务端执行任务。
- 在高级设置中可启用远程后端并配置服务器地址（默认 `http://localhost:5000`）。
- 连接后自动订阅服务端 SSE 日志流，实时显示远程任务输出。
- 支持通过 `Task/run` 和 `Task/stop` API 启动和停止远程任务。
- SSE 断线后自动重连（3 秒间隔）。

#### SRA-server

- 新增服务端应用，可通过 HTTP 协议与 SRA 通信。
- 服务端应用端口号为 5000，可通过 `--port` 选项自定义。
- 访问 `http://localhost:5000/swagger` 可查看 API 文档。

### 问题修复

- 修复 SRA 无法将游戏窗口移动到前台的问题。
- 修复云游戏模式启动游戏任务中途失败的问题。
- 修复启用任务通知后，内置任务启动时报 `_task_notify_key` 缺失并导致任务线程崩溃的问题。
- 修复 Python 后端未读取 `taskOrder`、`customTasks`、`enabledTasks`，导致自定义任务和任务排序配置无法正确参与执行的问题。
- 修复脚本仓库、自定义任务、脚本配置、任务通知等界面中的英文残留。
- 修复部分窗口和错误提示中的中文乱码。
- 修复自定义任务相关动态文案、Toast 提示和脚本文件选择器标题。
- 修复自定义脚本任务加载时未导入 `AppDataDir`、未注册动态模块导致脚本无法实例化的问题。
- 修复自定义脚本任务未读取脚本目录 `config.json`，导致脚本配置未传入后端执行的问题。
- 修复脚本配置窗口底部按钮栏遮挡最后一项配置的问题。

### 下载说明

- StarRailAssistant_Core*.zip - 标准版（需要手动配置）。
- StarRailAssistant_Lite*.zip - 试玩版（需要手动安装和配置 Python 环境）。
- StarRailAssistant_vX.X.X.zip - 基础便携版。
- StarRailAssistant_Full*.zip - 完整便携版（包含桌面端和服务端）。
- StarRailAssistant_ServerDLC*.zip - 服务端 DLC。
- StarRailAssistant_DesktopDLC*.zip - 桌面端 DLC。
- StarRailAssistant_vX.X.X_Setup.exe - 安装版。

需要安装 [.NET 桌面运行时 10.0](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0) 才能运行。
首次使用建议下载完整便携版或安装版。

### 测试说明

- 已本地构建并发布前端。
- 已在 `E:\SRA-ReleaseTest` 覆盖测试包前端和后端文件。
