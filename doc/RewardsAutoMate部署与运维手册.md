# RewardAutoMate 部署与运维手册

> 配套《RewardsAutoMate开发实现文档.md》，面向"放服务器长期运行"场景。

---

## 目录

1. [构建与发布](#一构建与发布)
2. [服务器部署](#二服务器部署)
3. [运行环境与目录](#三运行环境与目录)
4. [邮件配置](#四邮件配置)
5. [数据库说明](#五数据库说明)
6. [日志说明](#六日志说明)
7. [定时与调度](#七定时与调度)
8. [进程清理](#八进程清理)
9. [边缘场景排查](#九边缘场景排查)
10. [常见问题排查](#十常见问题排查)

---

## 一、构建与发布

### 1.1 本地构建

```bash
dotnet build src/RewardAutoMate.sln
```

### 1.2 发布（框架依赖版）

```bash
dotnet publish -c Release -o src/RewardAutoMate/bin/Publish
```

发布产物包含：`RewardAutoMate.exe`、运行依赖 dll、`External/msedgedriver.exe`、`External/email_config.json`。

> 驱动在 `External\` 下，程序会自动回退到这里（`WebDriverUpdater.ResolveDriverPath`），首次运行不需要联网下载；
> 只有当驱动版本与本机 Edge 不一致时才联网更新。

> 若目标机无 .NET 运行时，可改用自包含：`dotnet publish -c Release -r win-x64 --self-contained true -o ...`

### 1.3 首次登录（重要）

程序使用独立的 `EdgeUserData` 目录保存登录态。**首次必须人工登录一次**：

1. `Configuration/AppConfig.cs` 设 `Headless = false`
2. 启动程序，浏览器弹出后登录 Microsoft 账户
3. 确认登录成功后关闭程序，改回 `Headless = true` 重新发布/运行

> 登录态保存在运行目录 `EdgeUserData/`，随程序目录一起备份/迁移即可保留登录。

---

## 二、服务器部署

### 2.1 环境要求

| 依赖 | 说明 |
|------|------|
| Windows Server / Win10+ | 需有交互桌面会话（RDP） |
| .NET 8 Desktop Runtime | 框架依赖版需要；自包含版免装 |
| Microsoft Edge | 随系统更新，驱动由程序自动同步 |

### 2.2 启动方式

程序关闭窗口仅最小化到托盘，**不会退出**。保持常驻的推荐方式：

- **任务计划程序**：登录时启动 → `RewardAutoMate.exe`
- 或**开机自启**（Startup 文件夹/服务）

> 不推荐作为 Windows 服务运行——浏览器自动化需要交互桌面会话。

### 2.3 停机维护

托盘右键 → 退出；或直接结束进程（会丢失未发送日志，属正常）。

---

## 三、运行环境与目录

程序运行目录（`AppContext.BaseDirectory`）下的文件：

| 路径 | 类型 | 说明 |
|------|------|------|
| `RewardAutoMate.exe` | 程序 | — |
| `External/` | 依赖 | dll、`msedgedriver.exe`、`email_config.json` |
| `Logs/yyyy-MM-dd.log` | 日志 | 按天，保留 30 天 |
| `Logs/LogOnSaveSQLite.db` | 日志库 | 按天分表 |
| `HeatMapData.db` | 数据 | 每日搜索次数 |
| `RewardsHistory.db` | 数据 | 执行记录 + 周报标记 |
| `EdgeUserData/` | 数据 | Edge 登录态 |

> 备份与迁移：整目录拷贝即可；`EdgeUserData` 保证无需重新登录。

---

## 四、邮件配置

配置文件：`External/email_config.json`（**已被 .gitignore 忽略，勿提交**）。

```json
{
  "sender": "your_email@qq.com",
  "auth_code": "你的QQ邮箱授权码",
  "smtp_server": "smtp.qq.com",
  "smtp_port": 465,
  "receiver": "your_receiver@163.com"
}
```

- 程序启动时读取，覆盖 `AppConfig` 中的 SMTP 默认值
- 163 邮箱作收件方时无需额外设置；发件方（QQ）需开启 SMTP 服务并生成**授权码**（非登录密码）
- 修改配置后**重启程序生效**

**验证**：主界面底部「发送周报」按钮可立即发一封测试周报，确认通道可用。

---

## 五、数据库说明

### 5.1 HeatMapData.db（热力图）

表 `QueueItem`：`Date`(主键) / `Count`。每次执行后累加当日搜索次数。

### 5.2 RewardsHistory.db（执行历史）

表 `ExecutionRecord`：单次执行完整结果（时间/成败/搜索数/任务数/积分/错误）。

表 `DailyReportMark`：单行，记录最近周报发送日期，用于每周去重。

常用查询示例：

```sql
-- 最近 7 天执行概况
SELECT date(StartTime) AS d, COUNT(*) AS times,
       SUM(CASE WHEN Success=1 THEN 1 ELSE 0 END) AS ok,
       SUM(EstimatedPoints) AS points
FROM ExecutionRecord
WHERE StartTime >= datetime('now','-7 days')
GROUP BY d ORDER BY d;
```

### 5.3 日志库（Logs/LogOnSaveSQLite.db）

表 `Log_年月日`：按天分表，`BaseLogManager` 自动清理 30 天前表。

---

## 六、日志说明

- **文件**：`Logs/yyyy-MM-dd.log`，含全部级别（含 Debug），排障主依据
- **界面 LogView**：只显示 Info+（`DebugEnable=false`）
- **级别**：Debug / Info / Warning / Error

排查顺序建议：
1. 看 `Logs/` 当天文件 Error/Warning 行
2. 看 `RewardsHistory.db` 中 `Success=0` 的 `ErrorMessage`
3. 邮件收件箱对照异常通知

---

## 七、定时与调度

| 调度 | 触发 | 说明 |
|------|------|------|
| 主流程 | 启动 30 秒后 + 每 `ExecuteIntervalHours`（默认 12h） | `AutoSearchFunction` 一轮完整执行 |
| 周报邮件 | 每周一 09:00（周一 09:00 后启动会补发本周） | 独立定时器，与主流程解耦 |
| 驱动同步 | 每次创建驱动前（同进程一次） | `WebDriverUpdater` 自动匹配 Edge 版本 |

---

## 八、进程清理

浏览器由 `msedgedriver` 拉起，**子进程不会随父进程一起退出**。程序被强杀、崩溃、或驱动 `Quit()` 失败时，`msedge.exe` 会变成孤儿进程继续占用 `EdgeUserData`，下一次启动就会得到 `session not created: ... crashed` 或 `DevToolsActivePort file doesn't exist`。清理机制就是为这条链路兜底。

### 8.1 清理时机

| 时机 | 触发点 | 行为 |
|------|--------|------|
| 程序启动（首次创建驱动前） | `EdgeDriverFactory.EnsureStartupCleanup` | 结束上次运行残留的浏览器/驱动进程，删除锁文件 |
| 托盘右键「退出」 | `MainWindow` 退出项 | 停定时器 → `ShutdownAll` → `Quit()` 后兜底清理 |
| 正常退出 | `App.OnExit` | 同上（与托盘退出重复调用是幂等的） |
| 系统注销/关机 | `App.OnSessionEnding` | 同上 |
| 进程退出兜底 | `AppDomain.ProcessExit` | 同上（走不到 OnExit 的场景） |
| 驱动升级前 | `WebDriverUpdater.DownloadDriver` | 结束全部 `msedgedriver` 再覆盖驱动文件 |
| 浏览器启动崩溃 | `EdgeDriverFactory.CreateDriver` | 清进程 + 删锁文件后重试（见第九章） |

`ShutdownAll` 的 `Quit()` 只等 10 秒，超时直接转进程清理，避免关程序时卡死。

### 8.2 清理范围（不会误杀用户浏览器）

| 对象 | 匹配方式 | 说明 |
|------|---------|------|
| `msedge.exe` | 命令行含本项目 `--user-data-dir`（WMI `Win32_Process.CommandLine`） | 只杀本程序的自动化实例；用户自己开的 Edge 命令行里没有这个路径，不受影响 |
| `msedgedriver.exe` | 按进程名全杀 | 除 Selenium 自动化外基本没有程序常驻它 |
| 锁文件 | `SingletonLock` / `SingletonSocket` / `SingletonCookie` / `DevToolsActivePort` / `lockfile` | 仅在 `EdgeUserData` 目录内删除 |

WMI 读不到命令行时**放弃浏览器清理**（只清驱动），宁可不杀也不误杀。切到 `EdgeUserData.fallback` 之后的重试，清理目标同步改为 fallback 目录。

### 8.3 单实例保护

程序持有互斥量 `Local\RewardAutoMate_SingleInstance`。重复启动只弹提示后退出，不会出现两个进程抢同一个 `EdgeUserData`、互相清理对方浏览器的局面。

> 程序崩溃后互斥量会被系统释放，不会影响下次启动；但**托盘里还在运行的实例**会阻止新实例启动，需先右键「退出」。

### 8.4 手动确认与手动清理

确认是否还有残留（PowerShell）：

```powershell
# 本项目 profile 下的浏览器进程
Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
  Where-Object { $_.CommandLine -match 'EdgeUserData' } |
  Select-Object ProcessId, @{n='headless';e={$_.CommandLine -match '--headless'}}

# 驱动进程
Get-Process msedgedriver -ErrorAction SilentlyContinue
```

手动清理（程序已退出时执行）：

```powershell
Get-CimInstance Win32_Process -Filter "Name='msedge.exe'" |
  Where-Object { $_.CommandLine -match 'EdgeUserData' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Get-Process msedgedriver -ErrorAction SilentlyContinue | Stop-Process -Force
```

清理日志关键字：`[进程清理]`、`[进程清理/启动检查]`、`已结束残留浏览器进程`、`已删除残留锁文件`。

---

## 九、边缘场景排查

### 9.1 浏览器启动即退

日志表现：

```
EdgeDriver 创建失败（第 1/3 次） ... session not created: Microsoft Edge failed to start: crashed.
(session not created: DevToolsActivePort file doesn't exist)
```

说明 `msedgedriver` 起来了，但 `msedge` 进程启动后立刻退出。代码会自动走三级降级：

| 次数 | 变化 | 目的 |
|------|------|------|
| 1 | 原参数 | 正常路径 |
| 2 | 清理进程/锁文件 + `--disable-gpu` `--disable-software-rasterizer`，去掉 `--start-maximized` | 排查 GPU/远程桌面环境导致的崩溃 |
| 3 | 改用 `EdgeUserData.fallback` | 跳过原 profile 的损坏状态 |

日志出现 `改用备用用户目录` 且随后成功，说明主 profile 已不可用：此时**登录态在那个新目录里是空的**，需要按 1.3 重新登录一次；确认无误后可把 `EdgeUserData.fallback` 改名为 `EdgeUserData` 复用。

按可能性排查：

| 现象 | 常见原因 | 处理 |
|------|---------|------|
| 三级都失败 | `EdgeUserData` 目录不可写（部署在 `Program Files`、只读盘、网络盘） | 程序会打印"用户数据目录不可写"，把程序挪到可写目录 |
| 三级都失败 | 杀软/EDR 拦截 `msedge.exe` 子进程 | 加白名单 |
| 偶发失败后自愈 | 上次运行残留进程占着 profile | 正常，启动清理已覆盖；频繁出现说明退出方式不规范（强杀/关机） |
| 只在计划任务里失败 | 任务计划勾了"不管用户是否登录"→ Session 0 无桌面 | 改为"只在用户登录时运行"，或用 RDP 会话保持 |
| 启动即退且磁盘告警 | `TEMP` 或系统盘满 | 清理磁盘；Edge 启动要写 `Crashpad` 与临时文件 |

### 9.2 驱动服务未初始化就退出

日志表现：

```
Driver service process exited unexpectedly before initialization completed. Service URL: http://localhost:xxxxx/
EdgeDriver 创建失败
```

这是 **驱动进程本身**退出（还没到浏览器），与 9.1 不同。已确认的坑：

- **不要设置 `EdgeDriverService.LogPath`**。实测 Selenium 4.44 + msedgedriver 155 下，设置任意 `LogPath`（含纯 ASCII 路径）都会让驱动立即退出。代码里只开 `EnableVerboseLogging`（即 `--verbose`），`AppConfig.EnableDriverVerboseLog` 控制。
- 升级 Selenium 后可复测：临时加回 `_service.LogPath = Path.Combine(AppContext.BaseDirectory, "Logs");`，若驱动又能正常起来，说明上游已修，可以把日志目录正式启用。
- 其余原因：驱动 exe 被杀软隔离、驱动被替换后仍在移动中（程序已对覆盖加重试）、端口被占（驱动自选端口，概率低）。

### 9.3 驱动覆盖失败 / 下载失败

| 日志关键字 | 含义 | 处理 |
|-----------|------|------|
| `覆盖驱动失败（第 n/3 次，可能被进程占用）` | 有 `msedgedriver` 正在运行 | 程序会先清进程再重试；仍失败就手动按 8.4 清理 |
| `下载驱动失败（已重试 3 次）` | 网络/代理/防火墙到 `msedgedriver.microsoft.com` 不通 | 手动下载对应版本放到 `msedgedriver.exe`（根目录或 `External\`） |
| `本机缺少 msedgedriver，且自动下载失败` | 无驱动且下载失败，程序拒绝继续 | 按上一条手动放驱动；或用发布产物自带的 `External\msedgedriver.exe` |
| `下载的驱动包无效` | 下载到的是 HTML 错误页/半截文件 | 检查代理；程序已自动清理临时文件，重跑即可 |

发布产物里驱动在 `External\`，程序会自动回退使用，首次运行不需要联网。

### 9.4 版本不一致警告

日志 `继续使用本地驱动 xxx（与本机 Edge yyy 不一致，浏览器可能无法启动）`：驱动升级失败但本地有驱动，程序选择继续跑。Edge 主版本不一致时 Selenium 一定失败，看到这条请按 9.3 手动换驱动，别指望它能自动恢复。

### 9.5 排查入口速查

| 想确认的事 | 看哪里 |
|-----------|--------|
| 进程是否残留 | 8.4 的命令；日志 `[进程清理]` |
| 浏览器为什么崩 | `Logs/yyyy-MM-dd.log` 里 `EdgeDriver 创建失败` 后的堆栈；`EdgeUserData\Crashpad\reports\*.dmp`；事件查看器 → 应用程序日志中的 `msedge.exe` 错误 |
| 驱动为什么起不来 | `Driver service process exited unexpectedly` 前后的日志行 |
| 驱动是否升级成功 | `检测到 Edge 版本: ... 本地驱动版本: ...`、`WebDriver 已同步到版本 ...` |
| 调度是否被另一个实例占用 | 启动日志有无单实例提示；托盘是否有旧实例 |

---

## 十、常见问题排查

### Q1 报 `SessionNotCreated` / 驱动版本不匹配

`WebDriverUpdater` 会自动同步，若仍失败：
- 检查服务器是否可访问 `msedgedriver.microsoft.com`
- 查看日志"WebDriver 自动同步失败"行，确认下载异常原因

### Q2 搜索一直失败（导航超时）

- 多为网络到 bing.com 不稳定；日志会有 `timed out` 行，程序自动跳过该次
- 检查服务器代理/防火墙是否拦截

### Q3 无头模式不执行 Dashboard 任务

- 确认 `EnableDashboardTasks=true`
- 无头下登录态可能失效 → 按"首次登录"重新 `Headless=false` 登录一次

### Q4 邮件收不到

- 确认 `External/email_config.json` 存在且授权码正确
- 点「发送周报」按钮看是否弹成功/失败
- 查看日志"邮件发送"相关行

### Q5 显示的下次执行时间不准

`NextExecuteTime` 按"执行结束+间隔"估算，实际 Timer 按"上次开始+间隔"触发；因单轮耗时较长会有偏差，属已知现象。

### Q6 程序崩溃/异常

3 类全局异常处理器会记录日志并尝试发异常邮件；重启进程即可恢复（登录态在 `EdgeUserData`）。

---

*本手册配套《RewardsAutoMate开发实现文档.md》与《RewardsAutoMate代码分析报告.md》使用。*
