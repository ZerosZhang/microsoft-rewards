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
8. [常见问题排查](#八常见问题排查)

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

## 八、常见问题排查

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
