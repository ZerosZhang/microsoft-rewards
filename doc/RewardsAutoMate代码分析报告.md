# RewardAutoMate 代码分析报告

> 版本：v1.0  
> 日期：2026-06-08  
> 分析范围：`RewardsAutoMate` 项目全部源代码
>
> ⚠️ **历史快照**：本文记录的是 2026-06-08 当时的代码状态，其中「未实现」「0%」等结论已被后续开发推翻。
> 当前实现情况以 `doc/RewardsAutoMate开发实现文档.md` 为准，选择器与统计口径见该文档「选择器策略」一节。

---

## 一、项目概览

| 项目 | 值 |
|------|-----|
| **项目名称** | RewardAutoMate |
| **项目类型** | WPF 桌面应用程序 |
| **目标框架** | .NET 8.0-windows |
| **UI 框架** | WPF + Windows Forms (NotifyIcon) |
| **浏览器自动化** | Selenium WebDriver + EdgeDriver |
| **数据持久化** | SQLite (via SqlSugar ORM) |
| **外部依赖** | BaseTool.dll、BaseWPFControl.dll（自定义黑盒 DLL） |
| **代码总行数** | ~457 行（有效业务代码） |
| **文件数量** | 6 个核心源文件 |

### 1.1 NuGet 包依赖

| 包名 | 版本 | 用途 | 问题 |
|------|------|------|------|
| `CommunityToolkit.Mvvm` | 8.4.2 | MVVM 工具集 | 未实际使用 |
| `Selenium.WebDriver` | 4.44.0 | 浏览器自动化 | ✅ 正常使用 |
| `Selenium.WebDriver.ChromeDriver` | 150.0.7871.400-beta | Chrome 驱动 | ⚠️ 代码实际用 EdgeDriver，此包冗余 |
| `SqlSugarCore` | 5.1.4.215-preview13 | ORM + SQLite | ✅ 正常使用 |

### 1.2 项目文件清单

```
RewardsAutoMate/
├── RewardAutoMate.sln
├── README.md                              # 项目说明（含热力图预览）
├── RewardAutoMate/
│   ├── RewardAutoMate.csproj              # 项目文件
│   ├── App.xaml.cs                        # 应用程序入口（空壳）
│   ├── MainWindow.xaml.cs                 # 主窗口 + 热力图 + 定时调度
│   ├── QueueService.cs                    # SQLite 数据访问层
│   ├── WindowsPCServer.cs                 # 核心搜索业务逻辑
│   ├── AssemblyInfo.cs                    # 程序集信息
│   └── External/                          # 外部依赖目录
│       ├── BaseTool.dll                   # 自定义工具库（日志、取消令牌）
│       ├── BaseWPFControl.dll             # 自定义 WPF 控件库
│       └── msedgedriver.exe               # Edge 浏览器驱动
```

---

## 二、各模块详细分析

### 2.1 App.xaml.cs — 应用程序入口

**文件路径**：`RewardAutoMate/App.xaml.cs`  
**代码行数**：15 行  
**复杂度**：⭐

```csharp
public partial class App : Application
{
}
```

**分析**：
- 完全的空壳类，未重写 `OnStartup`、`OnExit` 等生命周期方法
- 未进行任何全局异常处理、依赖注入初始化或服务注册
- 建议：后续重构时应在此注入配置系统、日志系统和全局异常处理

---

### 2.2 MainWindow.xaml.cs — 主窗口与调度中心

**文件路径**：`RewardAutoMate/MainWindow.xaml.cs`  
**代码行数**：154 行  
**复杂度**：⭐⭐

#### 2.2.1 核心职责

| 职责 | 说明 |
|------|------|
| **热力图渲染** | GitHub 风格 7 行 × 52 列贡献热力图 |
| **系统托盘** | NotifyIcon 最小化到托盘，支持双击恢复 |
| **定时调度** | 每 12 小时触发一次 `AutoSearchFunction` |
| **数据绑定** | 从 SQLite 读取历史搜索次数并渲染颜色 |

#### 2.2.2 定时调度逻辑

```csharp
MainTimer = new Timer(async (_) => await AutoSearchFunction(), 
    null, TimeSpan.Zero, TimeSpan.FromHours(12));
```

**执行流程**：
```
AutoSearchFunction()
  ├── PC 端搜索 → WindowsPCServer.AutoSearch(false)
  ├── 更新数据库 + 刷新热力图
  ├── 手机端搜索 → WindowsPCServer.AutoSearch(true)
  └── 更新数据库 + 刷新热力图
```

**问题**：
- 定时器是 `System.Threading.Timer`，非 UI 线程安全，虽使用了 `Dispatcher.Invoke` 但仍需注意异常处理
- 12 小时间隔固定，不可配置
- 无任务并发控制，若上一次任务未执行完，新任务会并行启动

#### 2.2.3 热力图实现

| 属性 | 值 |
|------|-----|
| 网格大小 | 7 行（周一~周日）× 52 列（一年） |
| 数据范围 | 最近 365 天 |
| 颜色分级 | 5 级（0 / ≤40 / ≤80 / ≤120 / ≥120 次） |
| 数据源 | SQLite `HeatMapData.db` → `QueueItem` 表 |

**颜色映射**：
```csharp
value switch {
    0      => #EBEDF0,    // 浅灰 — 无数据
    <= 40  => #C3E8C1,    // 浅绿
    <= 80  => #81C98F,    // 中绿
    <= 120 => #39A369,    // 深绿
    >= 120 => #166342,    // 墨绿
};
```

---

### 2.3 QueueService.cs — 数据访问层

**文件路径**：`RewardAutoMate/QueueService.cs`  
**代码行数**：69 行  
**复杂度**：⭐

#### 2.3.1 数据模型

```csharp
public class QueueItem
{
    [SugarColumn(IsPrimaryKey = true)]
    public DateTime Date { get; set; }   // 主键：日期
    public uint Count { get; set; }       // 当日搜索次数
}
```

#### 2.3.2 提供的方法

| 方法 | 功能 |
|------|------|
| `DequeueTop365()` | 读取最近 365 条记录，用于热力图渲染 |
| `GetTodayCount()` | 读取今日记录，不存在则返回 Count=0 的新对象 |
| `UpdateItem()` | 插入或更新单条记录（UPSERT） |

#### 2.3.3 数据库配置

```csharp
ConnectionString = "DataSource=HeatMapData.db"
DbType = DbType.Sqlite
```

**分析**：
- 设计简洁，仅用于记录"每天搜索了多少次"
- **严重局限**：未存储积分余额、任务完成状态、领取记录等关键业务数据
- 建议扩展字段：`PointsEarned`、`PointsClaimed`、`TasksCompletedJson` 等

---

### 2.4 WindowsPCServer.cs — 核心搜索逻辑

**文件路径**：`RewardAutoMate/WindowsPCServer.cs`  
**代码行数**：219 行  
**复杂度**：⭐⭐⭐  
**这是项目中唯一包含业务逻辑的模块**

#### 2.4.1 核心常量

| 常量 | 值 | 说明 |
|------|-----|------|
| `MaxRewards` | `50` | 每次执行固定搜索 50 次 |
| `PauseTimeMinute` | `15` | 每搜索 5 次后暂停 15 分钟 |

#### 2.4.2 搜索词库

- **数量**：~170 个中文搜索词
- **分类**：科技、娱乐、体育、文化、社会、健康、经济、国际、政治
- **选择方式**：`Random.Next()` 随机抽取

#### 2.4.3 EdgeDriver 配置

```csharp
EdgeOptions options = new EdgeOptions();
options.AddArgument($"--user-data-dir={AppContext.BaseDirectory}/EdgeUserData");
options.AddArgument("--no-sandbox");
options.AddArgument("--disable-dev-shm-usage");
```

**分析**：
- 使用独立的用户数据目录，避免与系统 Edge 冲突 ✅
- 未设置 `--headless`，会弹出浏览器窗口
- 未禁用自动化检测标志（如 `--disable-blink-features=AutomationControlled`）

#### 2.4.4 移动端模拟

```csharp
string userAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 13_2_3 like Mac OS X) ...";
options.AddArgument($"user-agent={userAgent}");
```

**分析**：
- 仅通过 `user-agent` 参数模拟，未同步修改 `navigator.platform`、`screen` 等指纹
- 现代风控系统可通过 JS 检测出这是伪造的移动端

#### 2.4.5 搜索 URL 构造

```csharp
string searchUrl = _count <= MaxRewards / 2
    ? $"https://www.bing.com/search?q={Uri.EscapeDataString(_search_word)}&form={_random_string}&cvid={_rand_cvid}"
    : $"https://cn.bing.com/search?q={Uri.EscapeDataString(_search_word)}&form={_random_string}&cvid={_rand_cvid}";
```

**分析**：
- 前 25 次使用 `www.bing.com`，后 25 次使用 `cn.bing.com`
- `form` 和 `cvid` 参数随机生成（4位和32位随机字符串）
- 有一定反检测意识，但 `cvid` 应是一个 GUID 格式，纯随机字符串可能反而可疑

#### 2.4.6 执行时序

```
For i = 0 to 49:
    If i > 0 && i % 5 == 0:
        暂停 15 分钟                    ← 总计 150 分钟
    
    随机选择搜索词
    构造 URL
    Navigate().GoToUrl()               ← ~2-5 秒
    随机延迟 1-30 秒                   ← 平均 15 秒 × 50 = 750 秒
    执行平滑滚动 JS                    ← 瞬间
    随机延迟 10-30 秒                  ← 平均 20 秒 × 50 = 1000 秒
    
总耗时 ≈ 25 分钟（纯搜索） + 150 分钟（暂停） = 175 分钟 ≈ 3 小时
```

**问题**：
- 每 5 次搜索暂停 15 分钟的设计过于保守，实际 Rewards 搜索奖励通常允许更密集的搜索
- 总耗时约 3 小时，如果每 12 小时执行一轮，一天执行 2 轮，占用了大量时间窗口

#### 2.4.7 异常处理

```csharp
catch (OperationCanceledException) { return _count; }
catch (Exception ex)
{
    BaseLogManager.SendLog(LogLevel.Error, $"{ex.Message}", $"{ex}", true);
    return _count;
}
finally { driver.Quit(); }
```

**分析**：
- 支持取消令牌（`BaseAction.Token`）✅
- 异常时仅记录日志并返回已执行次数，无重试机制
- `driver.Quit()` 在 `finally` 中保证浏览器关闭 ✅

---

## 三、已实现功能清单

| 功能 | 实现状态 | 代码位置 | 质量评估 |
|------|---------|---------|---------|
| Bing PC 搜索自动化 | ✅ | `WindowsPCServer.AutoSearch(false)` | ⭐⭐⭐ |
| Bing Mobile 搜索自动化 | ✅ | `WindowsPCServer.AutoSearch(true)` | ⭐⭐ |
| 搜索词随机化 | ✅ | `GetRandomSearchWord()` | ⭐⭐⭐ |
| 搜索间隔随机化 | ✅ | `Random.Next(1000, 30000)` | ⭐⭐⭐ |
| 页面滚动模拟 | ✅ | `smoothScrollToBottom` JS 注入 | ⭐⭐ |
| 热力图可视化 | ✅ | `MainWindow.CreateHeatmap()` | ⭐⭐⭐ |
| 历史数据持久化 | ✅ | `QueueService` + SQLite | ⭐⭐⭐ |
| 系统托盘最小化 | ✅ | `NotifyIcon` | ⭐⭐⭐ |
| 定时自动执行 | ⚠️ | `Timer` 每 12 小时 | ⭐⭐ |
| 取消令牌支持 | ✅ | `BaseAction.Token` | ⭐⭐⭐ |

---

## 四、严重缺失与问题

### 4.1 业务功能缺失（与需求文档对比）

| 需求文档功能 | 实现状态 | 影响等级 |
|-------------|---------|---------|
| **访问 Rewards Dashboard** | ❌ 完全未实现 | 🔴 致命 |
| **Daily Set 自动完成** | ❌ 完全未实现 | 🔴 致命 |
| **Punch Cards（任务区域）** | ❌ 完全未实现 | 🔴 致命 |
| **日常任务自动完成** | ❌ 完全未实现 | 🔴 致命 |
| **积分手动领取** | ❌ 完全未实现 | 🔴 致命 |
| **福利奖励领取** | ❌ 完全未实现 | 🔴 致命 |
| **会员等级追踪** | ❌ 完全未实现 | 🟡 高 |
| **连签任务追踪** | ❌ 完全未实现 | 🟡 高 |
| **积分统计报告** | ❌ 仅记录搜索次数 | 🟡 高 |
| **视觉搜索任务** | ❌ 完全未实现 | 🟢 低 |
| **移动应用签到** | ❌ 完全未实现 | 🟢 低 |

> **核心结论**：现有代码本质上是一个"Bing 搜索次数机器人"，与 Rewards 平台几乎没有任何交互，无法感知任务状态、无法领取积分、无法完成任何 Dashboard 上的任务。

### 4.2 架构与设计问题

| 问题 | 说明 | 影响 |
|------|------|------|
| **单文件承载全部业务** | `WindowsPCServer` 一个 219 行的静态类包含了所有搜索逻辑 | 可维护性差 |
| **无任务抽象层** | 没有 `ITaskHandler` 接口或基类，所有逻辑硬编码 | 无法扩展新任务类型 |
| **无配置系统** | `MaxRewards`、`PauseTimeMinute`、定时间隔全部硬编码常量 | 无法动态调整 |
| **依赖黑盒 DLL** | `BaseTool.dll`、`BaseWPFControl.dll` 源码不可见 | 调试困难，存在隐性依赖风险 |
| **包引用冗余** | 引用了 `ChromeDriver` 但实际使用 `EdgeDriver` | 增加构建体积 |
| **无依赖注入** | 所有类型均为静态类或直接 new | 单元测试困难 |

### 4.3 风控与检测问题

| 问题 | 说明 | 风险 |
|------|------|------|
| 未禁用自动化检测标志 | 缺少 `--disable-blink-features=AutomationControlled` | 可能被检测为机器人 |
| 移动端指纹不完整 | 仅修改 UA，未同步 `navigator.platform`、`screen` 等 | 风控系统可识别伪造 |
| 搜索词库固定 | ~170 个词长期循环使用，模式可预测 | 容易被标记 |
| 无行为随机化 | 每次都是"搜索 → 等待 → 滚动 → 等待"固定模式 | 行为模式过于规律 |
| 固定 50 次搜索 | 不了解当日已搜索次数，可能超额或不足 | 效率低 |

### 4.4 异常处理不足

| 场景 | 现状 | 期望 |
|------|------|------|
| Edge 掉线/未登录 | 无法检测，会继续执行无效搜索 | 检测登录状态，必要时触发登录流程 |
| 页面加载超时 | 无显式超时控制 | 设置 `WebDriverWait` 超时 |
| 验证码挑战 | 无处理 | 检测并暂停等待人工介入 |
| 微软页面改版 | 无检测 | 关键元素找不到时报警 |
| 浏览器崩溃 | 无自动重启 | 捕获异常后尝试重启浏览器 |
| **异常时无通知** | **仅记录日志文件，用户必须手动查看才能发现异常** | **异常时立即发送邮件/通知，包含错误详情和上下文** |

---

## 五、与需求文档的差距矩阵

```
需求文档模块                    现有代码对应              完成度
─────────────────────────────────────────────────────────────────
FR-001 账户安全登录             无                       0%
FR-002 Bing PC 搜索             WindowsPCServer          60%（缺动态次数）
FR-003 Bing Mobile 搜索         WindowsPCServer          50%（指纹不完整）
FR-004 Daily Set 自动完成       无                       0%
FR-004a 福利奖励领取            无                       0%
FR-004b 积分领取弹窗            无                       0%
FR-005 Punch Cards              无                       0%
FR-005a 日常任务               无                       0%
FR-006 更多活动                无                       0%
FR-006a 视觉搜索               无                       0%
FR-006b 移动应用签到           无                       0%
FR-007 积分统计报告            QueueService（仅次数）    10%
FR-007a 会员等级追踪           无                       0%
FR-008 定时调度                MainTimer                30%（固定间隔）
FR-009 多账户支持              无                       0%
FR-010 反检测                  WindowsPCServer          40%
FR-011 日志监控                BaseLogManager           50%（黑盒）
FR-011a 异常邮件通知            无                       0%
FR-012 配置管理                无                       0%
```

**整体完成度估算**：约 **15%**

---

## 六、建议改造路线图

### 阶段一：接入 Rewards Dashboard（P0，1-2 天）

1. **新增 `RewardsNavigator`**：封装 Selenium 导航到 `rewards.bing.com` 的逻辑
2. **新增 `DashboardReader`**：读取当前积分、会员等级、今日任务状态
3. **新增 `PointsClaimHandler`**：检测"可领取"角标并自动领取积分
4. **新增 `WelfareClaimHandler`**：检测福利区域并领取待领取奖励
5. **改造 `AutoSearchFunction`**：根据 Dashboard 显示的搜索进度动态调整搜索次数

### 阶段二：Daily Set 自动化（P0，2-3 天）

1. **新增 `DailySetScanner`**：扫描每日 3 项任务并识别类型
2. **新增 `SearchTaskHandler`**：处理搜索类 Daily Set（如"我附近即将举行的活动"）
3. **新增 `QuizTaskHandler`**：处理 Quiz 类 Daily Set
4. **新增 `PollTaskHandler`**：处理 Poll 类 Daily Set
5. **新增 `ClickTaskHandler`**：处理点击跳转类 Daily Set

### 阶段三：任务体系扩展（P1，3-5 天）

1. **新增 `PunchCardScanner`**：扫描"任务"区域的大任务卡片
2. **新增 `PunchCardHandler`**：按步骤完成 Punch Card 子任务
3. **新增 `DailyTaskScanner`**：扫描"日常任务"区域
4. **新增 `PuzzleTaskHandler`**：处理拼图任务（高难度，可延后）
5. **新增 `StreakTracker`**：追踪并报告连签天数

### 阶段四：工程化与重构（P2，2-3 天）

1. **引入配置系统**：`appsettings.json` 或 `Microsoft.Extensions.Configuration`
2. **引入依赖注入**：`Microsoft.Extensions.DependencyInjection`
3. **引入结构化日志**：替换 `BaseLogManager`，使用 `Serilog`
4. **任务处理器抽象**：定义 `ITaskHandler` 接口，所有任务统一入口
5. **移除黑盒 DLL 依赖**：将 `BaseTool` 和 `BaseWPFControl` 的功能内联或替换为标准库
6. **修复包引用**：移除 `ChromeDriver`，保留 `msedgedriver.exe` 或改用 `WebDriverManager`

---

## 七、关键代码片段存档

### 7.1 现有搜索执行循环

```csharp
for (_count = 0; _count < MaxRewards; _count++)
{
    if (_count != 0 && _count % 5 == 0)
    {
        await Task.Delay(TimeSpan.FromMinutes(PauseTimeMinute), BaseAction.Token);
    }

    string _search_word = GetRandomSearchWord();
    string searchUrl = $"https://www.bing.com/search?q={Uri.EscapeDataString(_search_word)}...";
    driver.Navigate().GoToUrl(searchUrl);
    await Task.Delay(Random.Next(1000, 30000), BaseAction.Token);
    driver.ExecuteScript(smoothScrollScript);
    await Task.Delay(Random.Next(10000, 30000), BaseAction.Token);
}
```

### 7.2 现有移动端 UA

```csharp
"Mozilla/5.0 (iPhone; CPU iPhone OS 13_2_3 like Mac OS X) 
 AppleWebKit/605.1.15 (KHTML, like Gecko) 
 Version/13.0.3 Mobile/15E148 Safari/604.1"
```

### 7.3 现有数据库模型

```csharp
public class QueueItem
{
    [SugarColumn(IsPrimaryKey = true)]
    public DateTime Date { get; set; }
    public uint Count { get; set; }
}
```

---

## 八、附录

### 8.1 技能文件参考

根据用户技能 `develop-code-style`，后续改造应遵循：
- 命名规范：PascalCase（类/方法/属性）、snake_case 前缀下划线（私有字段）
- 代码组织：MVVM 模式适用于 WPF 层，业务逻辑层应独立于 UI
- 注释风格：XML 文档注释 + 必要行内注释

### 8.2 后续行动建议

1. **立即行动**：接入 `rewards.bing.com` Dashboard，读取任务状态
2. **高优先级**：实现积分手动领取，避免积分过期
3. **中优先级**：将硬编码参数提取到配置文件
4. **低优先级**：重构为任务处理器抽象体系，便于扩展新任务类型

---

*报告基于对 RewardAutoMate 项目源代码的静态分析和实际浏览器页面观察生成。*
