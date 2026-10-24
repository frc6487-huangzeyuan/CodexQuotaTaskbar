# Codex 额度托盘

只在 Windows 右下角托盘显示一个同心双圆环图标，没有主界面。

- 外圈绿色圆环：5 小时窗口的剩余额度。
- 内圈紫色圆环：每周窗口的剩余额度。
- 圆环从顶部顺时针填充，完整一圈表示剩余 100%。
- 鼠标悬停：按两段完整中文分别显示 5 小时与每周的剩余百分比、重置时间（本机时区）。
- 左键单击：立即刷新。
- 右键：刷新、选择 1 / 2 / 5 分钟刷新间隔、开机启动、退出。
- 开机启动默认关闭，可以在右键菜单中自行开启。

开机启动使用“任务计划程序”中的当前用户登录任务 `CodexQuotaTaskbar`，登录后延迟 15 秒运行，无需管理员权限或保存密码。使用电池也会启动，程序持续运行不受任务默认时长限制。右键菜单会检查任务的实际启用状态、目标路径和登录触发器。

`data/launch.json` 记录最近一次启动时间、来源、阶段与启动异常，便于区分 Windows 未发起启动和程序启动后出错。它不含登录凭据。

圆环使用透明背景、平滑边缘和圆角端点，适配本机 150% 缩放的 24×24 托盘图标，并根据 Windows 深色或浅色任务栏调整配色。小于等于 20% 时变黄，小于等于 10% 时变红；精确百分比在悬停提示及右键菜单中查看。

## 运行

双击 `CodexQuotaTaskbar.exe`。若图标位于 `^` 收纳菜单中，把它拖到任务栏托盘即可。程序只允许运行一份；关闭请使用右键菜单的“退出”。

程序使用 Windows 自带的 .NET Framework，不需要安装 Python、Node.js 或额外的 .NET SDK。需要同一 Windows 用户下已安装并登录 ChatGPT 账户的 Codex。

## 额度来源

程序启动本机 `codex.exe app-server --stdio`，完成 `initialize` / `initialized` 握手后，只调用 `account/rateLimits/read`，取 `codex` 桶中 300 分钟及 10080 分钟窗口的 `100 - usedPercent`。窗口缺失时显示虚线圆环，悬停提示中显示 `--`。查询结束后关闭子进程。

不读取 `auth.json`，不保存密码、Token、账户 ID 或完整 RPC 响应；不发起模型任务。认证与续期由本机 Codex CLI 处理。`data/status.json` 只保存显示数值、重置时间、更新时间、错误状态；`data/tray-icon.png` 是当前图标预览。

每分钟自动刷新。失败后保留上次读到的余量，圆环变淡并在右上角显示黄色提示点；悬停提示“旧值”，右键菜单可查看错误。首次查询失败显示虚线圆环。

默认依次查找 Codex 桌面版的本机 CLI、PATH 中的 `codex.exe`、npm 安装的本机 CLI。需要指定其他版本时，在程序旁新建 `codex-path.txt`，填入 `codex.exe` 的完整路径即可。程序继承 `CODEX_HOME` 和网络环境设置。

## 从源码编译

在项目目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

使用系统自带的 .NET Framework C# 编译器，不需要 NuGet 包。修改后先退出正在运行的程序，再编译并启动新的 EXE。

源文件：

- `src/Program.cs`：纯托盘生命周期、右键菜单、刷新与配置。
- `src/QuotaReader.cs`：Codex 定位、官方 JSONL RPC、额度转换。
- `src/TrayRenderer.cs`：同心双圆环图标。
- `src/Native.cs`：图标尺寸与原生图标资源接口。
- `src/StartupRegistration.cs`：当前用户登录任务的登记、状态读取与关闭，以及旧 Run 项迁移。
- `src/LaunchLog.cs`：启动记录。

## 已知限制

- 额度接口曾间歇返回 `-32603`，程序会保留上次成功读到的余量并继续按设置的间隔刷新；该错误码本身不足以确定登录或网络出了什么问题，具体原因尚未确认。
- 登录启动任务已通过手动触发任务验证；尚未完成真实注销登录或重启后的自动触发验证。

## 参考项目与文档

- [OpenAI 官方 App Server 文档](https://learn.chatgpt.com/docs/app-server#6-rate-limits-chatgpt)：额度 RPC 与字段含义。
- [Tooblippe/codex-usage](https://github.com/Tooblippe/codex-usage)：动态数字托盘与本机 app-server 的项目参考。
- [lch5518/CodexPeek](https://github.com/lch5518/CodexPeek)：本机 app-server 生命周期参考。任务栏嵌入方案经需求调整后已从本项目移除。

## 本机验证

2026-10-21：编译通过；已使用真实账户成功读取两种额度；已观察到自动刷新；已检查实际生成的 24×24 双行图标；用户已将图标拖到任务栏托盘。本机之外的 Windows 版本及缩放组合未验证。

2026-10-22：针对开机未启动的问题，改用当前用户登录后延迟 15 秒的交互式计划任务，并移除旧 Run 项。已通过任务计划程序实际启动新进程，确认 `--startup` 参数、`launch.json` 的 Running 状态以及启动后的真实额度更新。未执行重启或注销；下一次实际登录触发仍需后续确认。旧版缺少启动记录，本次未启动的原始原因尚未确定。

2026-10-22：外观更新为同心双圆环：外圈为 5 小时，内圈为每周；悬停提示改用完整中文分段展示额度及重置时间。已检查本机实际生成的 24×24 图标，确认启动后读取额度成功。
