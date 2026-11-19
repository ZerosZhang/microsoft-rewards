# RewardAutoMate 开发实现文档

> 版本：v2.0（对应当前 `src/` 代码）
> 说明：本文档阐明**当前代码**的实现细节，覆盖架构、核心流程、关键机制与注意事项。

---

## 目录

1. [项目概览](#一项目概览)
2. [架构与模块划分](#二架构与模块划分)
3. [启动流程](#三启动流程)
4. [核心执行流程](#四核心执行流程)
5. [搜索实现细节](#五搜索实现细节)
6. [WebDriver 管理与版本同步](#六webdriver-管理与版本同步)
7. [反检测机制](#七反检测机制)
8. [人类行为模拟](#八人类行为模拟)
9. [Dashboard 交互](#九dashboard-交互)
10. [日志系统](#十日志系统)
11. [邮件通知](#十一邮件通知)
12. [执行历史与数据存储](#十二执行历史与数据存储)
13. [配置详解](#十三配置详解)
14. [界面实现](#十四界面实现)
15. [周报定时调度](#十五周报定时调度)
16. [已知问题与注意事项](#十六已知问题与注意事项)

---

## 一、项目概览

| 项 | 值 |
|----|----|
| 项目类型 | WPF 桌面应用（.NET 8.0-windows） |
| 自动化引擎 | Selenium WebDriver + Edge |
| 数据持久化 | SQLite（SqlSugar ORM） |
| 日志 | BaseTool `BaseLogManager` |
| UI 控件库 | BaseWPFControl |
| 运行形态 | 无人值守 + 热力图 + 邮件通知 |

**运行目录文件**（程序运行期生成）：
- `Logs/yyyy-MM-dd.log`：按天日志文件（保留 30 天）
- `Logs/LogOnSaveSQLite.db`：日志 SQLite 库（按天分表）
- `HeatMapData.db`：每日搜索次数（热力图数据）
- `RewardsHistory.db`：每次执行记录
- `EdgeUserData/`：Edge 用户数据（登录状态）

---

## 二、架构与模块划分

```
src/RewardAutoMate/
├── App.xaml / App.xaml.cs            # 程序入口 + 全局异常处理
├── MainWindow.xaml / .cs             # 主界面 + 定时调度 + 周报调度
├── QueueService.cs                   # 热力图数据访问（SQLite）
├── WindowsPCServer.cs                # 核心流程编排 + 搜索实现
├── Configuration/AppConfig.cs        # 全局配置（静态类）
├── Logging/AppLogger.cs              # 日志封装（BaseLogManager 包装）
├── Models/
│   ├── QueueItem.cs                  # 每日搜索次数（热力图）
│   ├── TodayStats.cs                 # 界面统计面板数据
│   └── ExecutionRecord.cs            # 单次执行记录
└── Services/
    ├── EdgeDriverFactory.cs          # Edge 驱动统一创建/销毁
    ├── WebDriverUpdater.cs           # 驱动版本自动同步
    ├── AntiDetectionService.cs       # 反检测（指纹/UA/窗口）
    ├── HumanBehaviorSimulator.cs     # 人类行为模拟
    ├── RewardsDashboardService.cs    # Dashboard 页面交互
    ├── EmailNotificationService.cs   # 邮件（异常/周报）
    └── ExecutionHistoryService.cs    # 执行历史存储/统计
```

### 依赖关系

```
MainWindow ──┬─► WindowsPCServer ──► EdgeDriverFactory ──► WebDriverUpdater
             │         │                   │
             │         ├─► RewardsDashboardService ──► EdgeDriverFactory
             │         └─► ExecutionHistoryService / EmailNotificationService
             ├─► QueueService
             ├─► AppLogger ──► BaseLogManager（BaseTool）
             └─► ExecutionHistoryService / EmailNotificationService
```

---

## 三、启动流程

```
App.OnStartup
  ├─► AppLogger.Initialize()        // 设置 Logs 目录、注册文件输出、DebugEnable=false
  ├─► 注册 3 类全局异常处理
  └─► StartupUri 创建 MainWindow
        ├─► LogViewHost.Content = LogBlock.LogView   // 挂载日志视图
        ├─► BaseLogManager.Register_SendLogFunction(OnStatusLog)  // 状态栏订阅
        ├─► 热力图加载/渲染（QueueService）
        ├─► 统计面板初始化
        ├─► 后台 RefreshDashboardStatsAsync()        // 读当前积分/等级
        ├─► MainTimer：首触发 30s，周期 ExecuteIntervalHours
        └─► WeeklyReportTimer：调度周一 09:00
```

### 全局异常处理（App.xaml.cs）

| 事件 | 处理 |
|------|------|
| `AppDomain.UnhandledException` | 记 Fatal + 发异常邮件 + 关闭日志 |
| `DispatcherUnhandledException` | 记 Fatal + 发异常邮件 + 标记已处理 |
| `TaskScheduler.UnobservedTaskException` | 记 Error + SetObserved |

---

## 四、核心执行流程

`WindowsPCServer.ExecuteFullRewardsFlow()` 为一次完整执行，共 4 阶段：

```
┌───────────────────────────────────────────────┐
│ Phase 1/4  PC 端搜索（AutoSearchWithRetry）    │
│      └─ 0 次成功 → 中止并返回（Success=false）  │
├───────────────────────────────────────────────┤
│ 等待 30~120 秒（随机，切换端前缓冲）            │
├───────────────────────────────────────────────┤
│ Phase 2/4  移动端搜索（UA 模拟）                │
├───────────────────────────────────────────────┤
│ Phase 3/4  Dashboard 任务（新 driver）          │
│   读状态 → 领福利 → Daily Set → Punch Cards     │
│   → 日常任务（每项受 AppConfig 开关控制）        │
├───────────────────────────────────────────────┤
│ Phase 4/4  报告                                 │
│   保存执行历史（ExecutionHistoryService.Add）    │
│   输出统计日志                                   │
│   （周报由独立定时器负责，此处不发邮件）          │
└───────────────────────────────────────────────┘
```

- 返回 `ExecuteResult`（搜索数、任务数、估计积分、**当前积分**、错误信息）
- 任一阶段异常 → catch 记录 + 发异常邮件 + 返回部分结果
- 手动执行按钮 / 定时器都调用此方法

---

## 五、搜索实现细节

### 5.1 单端搜索 `AutoSearch(bool _user_agent)`

对每个搜索词执行：

```
选择随机搜索词（内置词库）
构造 URL：
  前半程 www.bing.com/search?q=...&form=随机4位&cvid=随机32位
  后半程 cn.bing.com/search?...
导航（超时保护：WebDriverException 含 timed out → Refresh + 跳过）
随机导航延迟 SearchDelayMin~Max
自然滚动（NaturalScrollPage）
模拟阅读 3~9 秒
约 5% 概率随机访问非搜索页面（bing news/images/videos/msn）
随机页面停留 PageStayMin~Max
记录到 ExecutedTasks（供异常邮件上下文）
```

**批次暂停**：每 `_batch_size` 次（随机化 ±2）后暂停随机化间隔（`PauseTimeMinute` ±20%）。

### 5.2 重试 `AutoSearchWithRetry`

- 最多 `MaxRetryCount`（默认 3）次尝试
- 累计达到目标 → 返回
- 单次 ≥3 次成功视为部分成功，继续尝试完成剩余
- 单次 <3 且非最后一次 → 等待随机 10~30 秒后重试

### 5.3 资源释放

`finally` 中：`CancellationTokenSource.Cancel()/Dispose()` + `EdgeDriverFactory.QuitActiveDriver()`，保证浏览器必关。

---

## 六、WebDriver 管理与版本同步

### 6.1 EdgeDriverFactory

- 全局单例 `ActiveDriver`（`lock` 保护），创建新实例前先关旧实例
- 每次创建前调用 `WebDriverUpdater.EnsureDriverUpToDate()` 保证驱动版本匹配
- 反检测启动参数（见第七节）
- 超时设置：`PageLoad=15s`、`ImplicitWait=ElementTimeoutSeconds`、`AsyncJS=10s`
- 创建后注入反检测脚本

### 6.2 WebDriverUpdater（驱动版本自动同步）

```
EnsureDriverUpToDate()
  ├─ 进程内缓存（CachedDriverPath）
  ├─ 获取本机 Edge 版本：
  │    注册表 HKLM\SOFTWARE\...\Edge\BLBeacon → 回退 msedge.exe 文件版本
  ├─ 读取本地驱动版本（运行 msedgedriver.exe --version 解析）
  ├─ 不一致 → 下载：
  │    https://msedgedriver.microsoft.com/{版本}/edgedriver_win64.zip
  │    解压到临时目录 → 替换目标（输出目录 msedgedriver.exe）
  │    顺带同步回源码 External/（防重编译回退）
  └─ 下载失败 → 记录警告，继续用现有驱动（不阻塞）
```

> 触发时机：每次创建驱动前；同进程仅检测一次（缓存）。

---

## 七、反检测机制

### 7.1 启动参数（EdgeDriverFactory）

- `--disable-blink-features=AutomationControlled`（关闭自动化标识）
- `--disable-infobars`、`--disable-extensions`、`--disable-popup-blocking`
- `--disable-web-security`、`--disable-features=IsolateOrigins,site-per-process`
- `--no-sandbox`、`--disable-dev-shm-usage`
- 随机窗口尺寸 / 随机 User-Agent（桌面/移动独立列表）
- `--lang=zh-CN,zh,en-US,en`；`Headless` 时加 `--headless=new`

### 7.2 CDP 脚本（AntiDetectionService.ApplyStealthScripts）

通过 `Page.addScriptToEvaluateOnNewDocument` 在页面加载前注入：

- `navigator.webdriver = undefined`
- 覆盖 `plugins`（伪造 3 个真实插件）、`languages`、`platform`、`hardwareConcurrency`、`deviceMemory`、`maxTouchPoints`
- 覆盖 `permissions.query`（notifications 等）
- 删除 `window.chrome.runtime.onInstalled/onConnect`
- Canvas 像素噪声 + WebGL 指纹固定为 Intel 渲染器

### 7.3 行为层（HumanBehaviorSimulator，见下节）

---

## 八、人类行为模拟

| 方法 | 说明 |
|------|------|
| `NaturalScrollPage` | 变速滚动、随机步长、偶尔回滚、阅读停顿 |
| `SimulateReading` | 定时内随机停顿 + 鼠标微动 |
| `VisitRandomPage` | 随机访问 bing news/images/videos/msn/shop |
| `GetRandomizedInterval` | 基础间隔 ±20% 随机 |
| `GetRandomizedBatchSize` | 批次大小 ±2 随机 |
| `MoveToElementNaturally` | 三次贝塞尔曲线模拟鼠标轨迹（含抖动） |
| `RandomizedClick` | 元素内随机偏移点击，小概率双击/右键 |

---

## 九、Dashboard 交互

`RewardsDashboardService` 封装所有页面操作，方法：

| 方法 | 功能 | 关键实现 |
|------|------|---------|
| `NavigateToDashboard` | 导航并等待加载 | GoToUrl + WaitForPageLoad |
| `GetDashboardStatus` | 读积分/等级 | `header p` 解析大数字；`//*[contains(text(),'会员')]` |
| `GetTodayProgress` | 读今日进度 | 遍历 `button` 解析 `x/x`；可领取；DailySet 完成统计 |
| `ClaimBenefits` | 领福利 | 扫描 `button,a` 中"领取/claim/获取"，逐个点击 |
| `ExecuteDailySet` | Daily Set | 卡片 Ctrl+点击开新标签页 → 停留 25-40s → 关标签页 → 回 dashboard 复查「已完成」，未完成补做第 2 轮；积分按复查结果结算 |
| `ExecutePunchCards` | Punch Cards | Earn 页扫描 + 子任务完成 |
| `ExecuteDailyTasks` | 日常任务 | `offerId` 链接 + 页面随机选选项 |

**选择器策略**：优先 `#dailyset a[href]`（2026-11-17 实测首页结构：`<section id="dailyset">` 内 3 个 `target="_blank"` 链接，页面已无 `data-bi-name` 属性），失败回退 `[data-bi-name*='DailySet']`、通用卡片类名。

**Daily Set 完成数**：直接读「每日活动」进度按钮的 `活动: x/3`；页面徽章文本是 `10` 而非 `+10`，依赖 `+10` 的旧判断一律失效。

> ⚠️ 微软随时可能改版，选择器集中在 `RewardsDashboardService` 便于统一更新。

---

## 十、日志系统

### 10.1 接入方式（AppLogger → BaseLogManager）

`AppLogger` 是 `BaseLogManager` 的包装，保留 `Debug/Info/Warning/Error/Fatal/CloseAndFlush` 同名方法，调用点无需感知底层。

初始化（`AppLogger.Initialize`，App 启动时最先调用）：
```csharp
BaseDefine.DirectoryPathOnSaveLog = Logs目录
BaseDefine.SQLiteFilePathOnSaveLog = Logs/LogOnSaveSQLite.db
BaseLogManager.Register_SaveLogToFile()   // 按天文件，保留30天
BaseLogManager.DebugEnable = false        // Debug 不显示界面
```

### 10.2 级别过滤机制（复用 BaseTool）

`BaseLogManager` 中：
- `DebugEnable/InfoEnable/...` 开关**只影响界面显示**（`SendLogToForm` 回调）
- `WriteLogToFile` / `WriteLogToDatabase` **无条件执行**

因此 `DebugEnable=false` 实现"Debug 只写文件、不进界面"，符合服务器场景。

### 10.3 界面展示

- 隐藏的 `LogBlock` 自动订阅 `BaseLogManager`，其 `LogView`（列表+详情+按级别着色）挂载到主界面日志卡片
- 运行状态 `TextBlock_LogStatus` 由 `OnStatusLog(Log)` 回调驱动（Dispatcher 安全）

---

## 十一、邮件通知

### 11.1 配置加载

`EmailNotificationService` 静态构造从 `External/email_config.json` 读取 SMTP 配置（存在则覆盖 `AppConfig`）：

```json
{ "sender", "auth_code", "smtp_server", "smtp_port", "receiver" }
```

### 11.2 异常邮件

- 触发：搜索/流程/浏览器异常、全局未处理异常
- 防轰炸：`NotifyCooldownMinutes`（默认 30 分钟）内同类型不重复发送
- 内容：异常类型、时间、堆栈、账户状态、**近 7 天执行概况**、已执行任务清单

### 11.3 周报邮件

由独立定时器周一 09:00 触发（详见第十五节），内容：
- 本周执行次数/成功失败
- **本周积分增长 = 本周最后执行时账户积分 − 上周最后执行时账户积分**（真实对比，非估计）
- 按天概况（每日积分变化基于相邻日账户积分差）
- 本周异常列表

### 11.4 发送实现

`SmtpClient`（EnableSsl）+ 授权码认证，失败重试 3 次（间隔 5 秒）。

---

## 十二、执行历史与数据存储

### 12.1 数据库总览

| 库文件 | 用途 | 访问类 |
|--------|------|--------|
| `HeatMapData.db` | 每日搜索次数（热力图） | `QueueService` |
| `RewardsHistory.db` | 每次执行记录 + 周报标记 | `ExecutionHistoryService` |
| `Logs/LogOnSaveSQLite.db` | 日志（按天分表，保留 30 天） | `BaseLogManager` |

### 12.2 ExecutionRecord 字段

| 字段 | 说明 |
|------|------|
| `StartTime / EndTime` | 起止时间 |
| `Success` | 整体是否成功 |
| `PcSearches / MobileSearches / TotalSearches` | 搜索数 |
| `BenefitsClaimed / DailySetCompleted / PunchCardsCompleted / DailyTasksCompleted / TotalTasks` | 任务数 |
| `EstimatedPoints` | 估计积分（任务加分粗估） |
| `CurrentPoints` | **执行时读取的真实账户积分**（周报对比用） |
| `ErrorMessage` | 失败原因 |

### 12.3 周报去重

`DailyReportMark` 单行表记录最近发送日期，`IsWeeklyReportSent()` 判断是否本周（周一 00:00 起）已发。

---

## 十三、配置详解

### 13.1 AppConfig.cs（静态类）

| 分类 | 项 | 默认 | 说明 |
|------|-----|------|------|
| 搜索 | `MaxPcSearches` / `MaxMobileSearches` | 50/50 | 每轮搜索次数 |
| 搜索 | `PauseTimeMinute` | 15 | 批次暂停（分钟） |
| 搜索 | `BatchSize` | 5 | 每批次搜索数 |
| 搜索 | `SearchDelayMin/MaxMs` | 1000/30000 | 导航后延迟 |
| 搜索 | `PageStayMin/MaxMs` | 10000/30000 | 页面停留 |
| 定时 | `ExecuteIntervalHours` | 12 | 执行周期 |
| Dashboard | `EnableDashboardTasks` | true | 任务开关 |
| Dashboard | `AutoClaimBenefits/AutoDailySet/AutoPunchCards/AutoDailyTasks` | true | 各类任务开关 |
| 浏览器 | `Headless` | true | 无头模式 |
| 浏览器 | `MobileUserAgent` | iPhone UA | 移动 UA |
| 反检测 | `ExtraRandomDelayMaxMs` / `SimulateMouseMovement` | 2000/true | 行为随机 |
| 反检测 | `MaxRetryCount` | 3 | 搜索重试次数 |
| 邮件 | `NotifyOnFailure` / `SendWeeklyReport` | true/true | 邮件开关 |
| 邮件 | `SmtpServer/Port/From/To/Password/EnableSsl` | QQ 465 | SMTP（可由 json 覆盖） |
| 邮件 | `NotifyCooldownMinutes` | 30 | 异常冷却 |
| 其他 | `AutoUpdateWebDriver` | true | 驱动自动同步 |

### 13.2 email_config.json

运行时读取，优先于 `AppConfig` 中的 SMTP 默认值；**含敏感授权码，已被 `.gitignore` 忽略**，仓库只保留 `email_config.json.example` 模板。

---

## 十四、界面实现

### 14.1 布局（单页仪表盘，6 区域）

```
标题栏（应用名 + 下次执行徽章 + 内存占用）
统计卡片 ×4（当前积分/可领取/今日已获得/会员等级）
搜索进度 ×2（PC / Mobile：标签+数值同行 + 进度条）
执行热力图（7×52，365 天）
实时日志（LogView：列表 + 详情）
底部操作（发送周报 | 手动执行 + 提示）
```

### 14.2 样式统一

- `CardStyle`：白底、浅灰边框、圆角 8、Padding 12、Margin 2（相邻净距 4）
- `StatLabelStyle`（11px 灰）：所有卡片标签
- `StatValueStyle`（20px Bold 蓝 #2196F3）：所有卡片数值
- 字体：Consolas, 微软雅黑

### 14.3 热力图

- 7 行（周一~周日）× 52 列，5 级颜色映射（0/40/80/120/120+）
- 数据源：`QueueService.DequeueTop365()` → `HeatMapData.db`

---

## 十五、周报定时调度

`MainWindow.ScheduleWeeklyReportTimer()`：

```
计算目标 = 下一个周一 09:00
特殊情况：当前是周一且已过 09:00 且本周未发 → 10 秒后补发本周
到点 → SendWeeklyReportAsync（未发才发）→ 发完重新调度下周一
```

- 定时器为一次性触发（`Timeout.InfiniteTimeSpan` 周期），发完由 `finally` 重排
- 周报**不依赖**是否有执行发生（与主流程解耦）

---

## 十六、已知问题与注意事项

| 问题 | 影响 | 说明 |
|------|------|------|
| 搜索耗时较长 | 一轮可能 1~3 小时 | 每次搜索含导航等待+滚动+阅读+停留 |
| 显示的下次执行时间偏晚 | 提示不精确 | `NextExecuteTime`=执行结束+间隔，而 Timer 按"上次开始+间隔"触发 |
| DailySet 统计依赖页面文本 | 可能误统计 | 页面结构变动会导致统计偏差 |
| 反检测参数激进 | 风控/兼容风险 | `--disable-web-security` 等为绕过检测而设 |
| 邮件授权码 | 安全风险 | 已在 `.gitignore` 忽略，勿提交；建议定期重置 |
| 无头登录 | 首次需人工 | 建议先 `Headless=false` 完成一次 Edge 登录再开启无头 |

---

*本文档基于 `src/` 当前代码整理，与历史 `RewardsAutoMate代码分析报告.md`（旧版 15% 完成度分析）对照可见完整演进过程。*
