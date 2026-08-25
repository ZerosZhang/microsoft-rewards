# BaseTool 与 BaseWPFControl 使用文档

> 版本：v1.0
> 日期：2026-06-08
> 源码位置：`D:\VisualStudioCode\ToolsRepositoryV4`
> 目标：指导 `RewardAutoMate` 项目复用这两个库 DLL 中已实现的功能

---

## 目录

1. [库概述](#一库概述)
2. [引用与集成方式](#二引用与集成方式)
3. [BaseTool 使用指南](#三basetool-使用指南)
4. [BaseWPFControl 使用指南](#四basewpfcontrol-使用指南)
5. [在 RewardAutoMate 中的复用示例](#五在rewardautomate中的复用示例)
6. [常见问题与注意事项](#六常见问题与注意事项)

---

## 一、库概述

| 项目 | 说明 |
|------|------|
| **BaseTool.dll** | 纯逻辑/工具类库。日志、配置读写、系统函数、任务/取消令牌、Socket/串口/Modbus/数据库通信、数据转换、数学计算、基元抽象接口等 |
| **BaseWPFControl.dll** | WPF 控件库。**依赖 BaseTool.dll**。提供对话框体系、状态栏控件（LogBlock/MemoryBlock）、表单控件（ConfigControl/NumericBox/SelectFileBox 等）、30+ 套控件样式、附加属性、转换器 |

**技术栈**：
- 目标框架：`net8.0-windows`
- 语言版本：`preview`（使用了集合表达式 `[.. x]`、主构造函数等 C# 12+ 语法）
- BaseTool 依赖 NuGet：`SqlSugarCore 5.1.4.188`、`System.IO.Ports 9.0.4`、`System.Management 9.0.6`
- BaseWPFControl 通过 `ProjectReference` 引用 BaseTool，并全局 `using BaseTool;`

**命名空间**：
- BaseTool 主命名空间：`BaseTool`
- BaseTool 抽象层：`BaseTool.AbstractTool`
- BaseWPFControl 控件：`BaseWPFControl.UIWidget`
- BaseWPFControl 对话框：`BaseWPFControl.ToolDialog`
- BaseWPFControl 扩展：`BaseWPFControl.Extension`
- BaseWPFControl 转换器：`BaseWPFControl.Converters`

> ⚠️ **BaseWPFControl 依赖 BaseTool**，两者必须同时引用（dll 方式引用时两个 dll 都要放进输出目录，且 BaseTool.dll 不能被裁剪）。

---

## 二、引用与集成方式

### 2.1 方式一：DLL 引用（RewardAutoMate 当前方式）

`RewardAutoMate.csproj` 中已有：

```xml
<ItemGroup>
  <Reference Include="BaseTool">
    <HintPath>External\BaseTool.dll</HintPath>
  </Reference>
  <Reference Include="BaseWPFControl">
    <HintPath>External\BaseWPFControl.dll</HintPath>
  </Reference>
</ItemGroup>
```

注意：两个 dll 默认会拷贝到输出目录（`bin\Debug\net8.0-windows\`），BaseWPFControl.dll 运行时需要找到 BaseTool.dll。

### 2.2 方式二：源码项目引用（推荐，可直接修改/调试）

把 ToolsRepositoryV4 的源码项目直接引用进来：

```xml
<ItemGroup>
  <ProjectReference Include="D:\VisualStudioCode\ToolsRepositoryV4\BaseTool\BaseTool.csproj" />
  <ProjectReference Include="D:\VisualStudioCode\ToolsRepositoryV4\BaseWPFControl\BaseWPFControl.csproj" />
</ItemGroup>
```

好处：可以断点进源码、按需裁剪、保持版本同步。MainStation 主程序就是这么用的。

### 2.3 XAML 中的三个关键配置

**① 命名空间声明**（`MainWindow.xaml` 根元素）：

```xml
xmlns:BaseWPFControl="https://BaseWPFControl"
xmlns:BaseWPFControlDesign="https://BaseWPFControl/design"
xmlns:BaseTool="clr-namespace:BaseTool;assembly=BaseTool"
```

> `https://BaseWPFControl` 是一个 **URI 形式的命名空间**，不是真正的网址。由 BaseWPFControl 的 `AssemblyInfo.cs` 中的 `XmlnsDefinition` 映射到多个实际命名空间（UIWidget、ToolDialog、Extension、Converters），所以一个前缀就能用全部控件。

**② 合并样式资源**（App.xaml 或 Window.Resources）：

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceDictionary Source="pack://application:,,,/BaseWPFControl;component/Themes/Generic.xaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

`Generic.xaml` 会串联合并 **40+ 个样式字典**（ButtonStyle/TextBoxStyle/DialogStyle/...）并注册所有控件的**隐式默认样式**。因此合并后：普通 `Button`、`TextBox`、`CheckBox` 等会自动套用 BaseWPFControl 的样式。

**③ 主窗口样式**（可选，启用对话框体系所需）：

```xml
<Window Style="{StaticResource DefaultWindowStyle}" ...>
```

`DefaultWindowStyle` 会把窗口模板替换为 `DialogContainer`（对话框容器），**这是 `Dialog.ShowDialog()` 能正常工作的前提**。RewardAutoMate 的 MainWindow 当前未使用该样式，若要使用 `LoadingDialog`/`MessageDialog` 等系统对话框，需要套用它，或用 `ShowAsWindow()` 以独立窗口形式弹出（不依赖容器）。

---

## 三、BaseTool 使用指南

所有类都在 `using BaseTool;` 命名空间下，绝大多数是静态类。

### 3.1 日志：BaseLogManager（最常用）

```csharp
// 输出日志，4 个重载
BaseLogManager.SendLog(LogLevel.Debug, "调试日志");
BaseLogManager.SendLog(LogLevel.Info, "信息日志", "描述/详情");
BaseLogManager.SendLog(LogLevel.Warning, "警告日志", true);           // 控制台显示
BaseLogManager.SendLog(LogLevel.Error, "错误日志", ex.ToString(), true);
```

**级别控制**（作用于"重定向函数"场景，Error 始终显示）：

```csharp
BaseLogManager.DebugEnable = true;
BaseLogManager.InfoEnable = true;
BaseLogManager.WarningEnable = true;
BaseLogManager.ErrorEnable = true;
```

**保存到本地文件**（按天分文件，自动清理 30 天前日志）：

```csharp
BaseDefine.DirectoryPathOnSaveLog = "./SaveLog";        // 或直接设置路径
BaseLogManager.Register_SaveLogToFile();
// 重载：BaseLogManager.Register_SaveLogToFile("./SaveLog");
```

**保存到 SQLite 数据库**：

```csharp
BaseLogManager.Register_SaveLogToSQLite();
```

> ⚠️ **重要**：`BaseLogManager` 的**静态构造函数默认就会执行 `Register_SaveLogToSQLite()`**！只要代码里用到（哪怕一次）这个类，就会自动在 `BaseDefine.SQLiteFilePathOnSaveLog`（默认 `SaveLog\LogOnSaveSQLite.db`）创建日志数据库并按天分表。RewardAutoMate 已有自己的 Serilog 日志，接入时要留意这个默认行为。

**重定向日志输出**（把日志接到自己的 UI / 回调）：

```csharp
BaseLogManager.Clear_SendLogFunction();                          // 先清掉旧的
BaseLogManager.Register_SendLogFunction((Log _log) =>
{
    // _log.LogTime / _log.LogLevel / _log.LogMessage / _log.LogDescription
    Console.WriteLine($"{_log.LogTime:HH:mm:ss} {_log.LogLevel} {_log.LogMessage}");
});
```

**关闭**：`BaseLogManager.Close();`

**日志结构体 `Log`**（重定向回调参数）：
| 属性 | 类型 | 说明 |
|------|------|------|
| `LogTime` | DateTime | 时间 |
| `LogLevel` | LogLevel | Debug/Info/Warning/Error |
| `LogMessage` | string | 标题 |
| `LogDescription` | string | 详情（通常放异常堆栈） |

### 3.2 全局定义：BaseDefine（路径常量）

```csharp
BaseDefine.CurrentDirectory;                        // 程序启动目录（只读）
BaseDefine.CurrentProcess;                          // 当前进程 Process 对象
BaseDefine.DirectoryPathOnSaveConfig;               // 默认 {启动目录}/Config，可写
BaseDefine.DirectoryPathOnSaveLog;                  // 默认 {启动目录}/SaveLog，可写
BaseDefine.DirectoryPathOnSaveData;                 // 默认 {启动目录}/SaveData，可写
BaseDefine.DirectoryPathOnSaveFile;                 // 默认 {启动目录}/SaveFile，可写
BaseDefine.FilePathOnSaveSoftwareSetting;           // 默认 {启动目录}/SoftwareSetting.json
BaseDefine.SQLiteFilePathOnSaveLog;                 // 默认 {启动目录}/SaveLog/LogOnSaveSQLite.db
```

### 3.3 通用函数：BaseFunction（配置 + 系统功能）

**JSON 配置读写**（最常用）：

```csharp
// 读取，文件不存在返回 new T()（不抛异常）
AppConfig? _config = BaseFunction.LoadConfig_Json<AppConfig>("config.json");
// 保存
BaseFunction.SaveConfig_Json(_config, "config.json");

// 对象 <-> JSON 字符串
string _json = BaseFunction.TransToJsonString(_config, indented: true);
AppConfig? _back = BaseFunction.TransFromJsonString<AppConfig>(_json);

// 深拷贝（基于 JSON 序列化）
AppConfig _copy = BaseFunction.DeepClone(_config);
```

**XML 配置读写**：

```csharp
ConfigType? _cfg = BaseFunction.LoadConfig_Xml<ConfigType>("config.xml");
BaseFunction.SaveConfig_Xml(_cfg, "config.xml");
```

**系统/文件功能**：

```csharp
float _free = BaseFunction.GetDiskFreeSpace("C");      // C 盘剩余空间 GB
long _mem   = BaseFunction.GetSoftwareUsedMemory();    // 本进程内存 MB
bool _dup   = BaseFunction.ApplicationRepeatRun();     // 是否重复启动（进程互斥锁）
bool _focus = BaseFunction.FocusExistingWindow();      // 置前已打开的窗口，返回是否找到
bool _focus2= BaseFunction.FocusExistingWindow("窗口标题");
BaseFunction.DeleteFileByTime("./SaveLog", 30);        // 删除目录下 N 天前的文件/文件夹
BaseFunction.SetLocalDateTime(DateTime.Now);           // 设置系统本地时间
BaseFunction.CompressFileZip("D:/1.txt", "D:/1.zip");  // 压缩文件（重名自动加(1)(2)...）
BaseFunction.CompressDirectoryZip("D:/dir", "D:/dir.zip");
string _safe = BaseFunction.RemoveInvalidCharInFileName("a/b:c*?.txt");  // 去掉文件名非法字符
BaseResult _r   = BaseFunction.TryCreateFileOrDirectory("D:/path/file.txt"); // 创建文件或目录
string _hwid   = BaseFunction.Get_HardwareID();        // CPU序列号+UUID
string[] _descs = BaseFunction.GetDescriptionsArray<MyEnum>();  // 枚举所有 Description
string _desc   = MyEnum.Value.GetDescription();        // 单个枚举的 Description
```

**条件等待**（Rewards 页面等待很有用）：

```csharp
// 超时版本：条件不满足则抛 TimeoutException
await BaseFunction.Condition(() => driver.FindElements(By.XPath("//*[contains(text(),'已完成')]")).Count > 0,
                             10000, cancellationToken);

// 无限等待版本（支持取消）
await BaseFunction.Condition(() => _status.IsReady, cancellationToken);
```

**枚举扩展**：

```csharp
// 依赖 System.ComponentModel.DescriptionAttribute
[Description("银牌会员")]
Level silver = Level.Silver;
string desc = silver.GetDescription();        // => "银牌会员"
string[] all = BaseFunction.GetDescriptionsArray<Level>();
```

**反射工具**：

```csharp
// 比较对象差异（带 [ParameterName] 特性的属性）
List<ParameterChangeInfo> diffs = BaseFunction.CompareDifferences(oldCfg, newCfg);
List<ParameterInfo> infos = BaseFunction.GetParameterInfos(config);

// 移除事件订阅
BaseFunction.ClearEventHandler(obj, "EventName", "HandlerMethodName");
BaseFunction.ClearAllEventHandler(obj, "EventName");
```

**foreach 简化**（Kotlin 风格）：

```csharp
foreach (int i in 0..5) { }       // 0,1,2,3,4（Range 循环）
foreach (int i in 3..8) { }       // 3,4,5,6,7
```

**保存 JPEG**：`BaseFunction.SaveBitmapAsJpeg(bitmap, "a.jpg", 85);`

### 3.4 动作与结果：BaseAction / BaseResult

**BaseAction**：一个"可执行/可取消/可复位"的动作单元，常用于流程步骤管理。

```csharp
// 定义动作
BaseAction _step = new BaseAction("PC 搜索", async (_cts) =>
{
    for (int i = 0; i < 50; i++)
    {
        _cts.Token.ThrowIfCancellationRequested();   // 支持取消
        await DoSearchAsync();
    }
    return BaseAction.ResultOK;
});

// 执行
uint _code = await _step.Run();

// 取消（静态，取消所有 BaseAction）
BaseAction.Cancel();
// 取消令牌
var _token = BaseAction.Token;          // 只读 CancellationToken
var _cts   = BaseAction.TokenSource;    // 可拿到的 CancellationTokenSource
BaseAction.ResetToken();                // 重置令牌（取消后要重置才能再 Run）
```

**结果码常量**（BaseAction 与 BaseResult 共用语义）：

| 常量 | 值 | 含义 |
|------|------|------|
| `ResultOK` | `0` | 成功 |
| `Running` | `0x0000_FFFF` | 正在执行 |
| `Default` | `0xFFFF_FFFF` | 未执行 |
| `ResultError` | `0xFFFF_0000` | 错误 |
| `ResultCancel` | `0xFFFF_0001` | 取消 |
| `ResultTimeout` | `0xFFFF_0002` | 超时 |
| `ResultSkip` | `0xFFFF_0003` | 跳过 |

**BaseResult**：统一返回结果对象，携带结果码 + 消息 + 可选异常。

```csharp
BaseResult _ok = BaseResult.OK();                     // 成功
BaseResult _ok2 = BaseResult.OK("执行成功");
BaseResult _err = BaseResult.Error("失败原因");
BaseResult _err2 = BaseResult.Error(exception);       // 携带异常
BaseResult _cancel = BaseResult.Cancel();
BaseResult _timeout = BaseResult.Timeout();

// 判断
if (_result.IsOK) { }
if (_result.IsError) { }
if (_result.IsNotOK) { }
string msg = _result.Message;

// 失败则抛异常
BaseResult.ThrowErrorIfNotOk(_result);
```

> RewardAutoMate 适用场景：把"PC 搜索 / 移动搜索 / Dashboard 任务 / 发邮件"封装成 BaseAction 列表，用 `BaseAction.Token` 统一支持取消，用 `BaseResult` 统一返回。

### 3.5 任务队列：BaseTaskQueue

```csharp
BaseTaskQueue _queue = new();
_queue.Enqueue(() => Task1());          // Func<BaseResult> 或 Func<Task<BaseResult>>
_queue.Enqueue(async () => await Task2());
_queue.Enqueue(() => BaseResult.OK());

await _queue.ExecuteQueueAsync();       // 串行执行全部
BaseResult _last = _queue.LastTaskResult;   // 上一个任务的结果
_queue.ClearQueue();
```

> 适合 RewardAutoMate：串行执行"福利领取→DailySet→PunchCards→日常任务"，任一失败不影响后面的。

### 3.6 单例：BaseSingleton

```csharp
BaseSingleton _s = BaseSingleton.GetInstance();   // 线程安全双重锁单例
```

### 3.7 参数验证：BaseValidateArgument

```csharp
BaseValidateArgument.ThrowIfNull(obj);                       // null 抛 ArgumentNullException
BaseValidateArgument.ThrowIfNullOrEmpty(str);                // null/空串抛异常
BaseValidateArgument.ThrowIfNegative(value);                 // 负数抛异常
BaseValidateArgument.ThrowIfNotInRange(value, 0, 100);
T? t = BaseValidateArgument.TryCast<T>(obj);                 // 可空转换
T t = BaseValidateArgument.NotNullAndCast<T>(obj);           // 非空强转，失败抛异常
```

### 3.8 数据转换：BaseTransform

```csharp
string hex = BaseTransform.ByteToHexStr([0x01, 0x23]);       // => "01 23"
byte[] bytes = BaseTransform.HexStrToByte("01 23");          // 严格 16 进制
string fmt = BaseTransform.HexStringFormatting("01234567");  // => "01 23 45 67"
string bin = BaseTransform.BinStringFormatting("11001010");  // => "1100-1010"

// 数组格式化表（DEC/HEX/Bin 三列），返回字符串
string table = BaseTransform.FormatShowArray([0x01, 0x23, ...]);  // 支持 byte/ushort/uint/ulong/sbyte/short/int/long
```

### 3.9 数组扩展：BaseArrayExtension

```csharp
bool equal = a.SequenceEqual(b, (x, y) => x.Id == y.Id);     // 自定义比较

// 对比集合增删改（返回元组 _adds/_remove/_updates）
var (adds, removes, updates) = newList.CompareChanges(oldList);
var (adds, removes, updates) = newList.CompareChanges(oldList, x => x.Id);   // 按 Id 分组

// Plus 版返回带新旧值配对
var (adds, removes, updates) = newList.CompareChangesPlus(oldList, x => x.Id);
```

### 3.10 数学计算：BaseCalculate

```csharp
double rad = BaseCalculate.Rad(90);            // 角度转弧度
double deg = BaseCalculate.Deg(Math.PI / 2);   // 弧度转角度
BaseCalculate.RotationCalculation(before, center, angle, out PointF after);    // 绕中心旋转
BaseCalculate.RotationCenterCalculation(before, after, angle, out PointF center);
BaseCalculate.RotationAngleCalculation(before, after, center, out double angle);
```

### 3.11 通信类（RewardAutoMate 暂不需要，概述）

**BaseSocketClient**（TCP 客户端）：
```csharp
var client = new BaseSocketClient("127.0.0.1", 5001, "客户端");
client.Connect();
client.SendMessage("Hello");                                 // 不等待回复
client.SendMessage([0x00, 0x01]);                            // 发字节
byte[] reply = client.SendMessage_WaitReply([0x00], 4);      // 等固定长度回复（默认5s超时）
string reply = client.SendMessage_WaitReply("Hello", "A");   // 等到结尾是"A"的回复
client.CancelWait();                                          // 取消等待
client.DisConnect();
```
`Connect`/`DisConnect` 返回 `BaseResult`，实现 `IToolFunction`。

**BaseSocketServer**：
```csharp
var server = new BaseSocketServer(5001, "服务器");
server.Listen();     // 阻塞监听，返回 BaseResult
server.Close();
```

**BaseSerialPort**（串口，继承 System.IO.Ports.SerialPort，实现 IToolFunction）：
```csharp
var sp = new BaseSerialPort("COM1", 115200);   // 波特率，默认 None/8/1
sp.Connect();
sp.SendMessage("cmd");
sp.SendMessage([0x01, 0x02]);
byte[] reply = sp.SendMessage_WaitReply([0x01], 4);          // 等 4 字节回复
byte[] reply = sp.SendMessage_WaitReply([0x01], b => Check(b));  // 自定义回复条件
string reply = sp.SendMessage_WaitReply("cmd", "OK");
var task = sp.SendMessage_WaitReply_Async([0x01], 4, token); // 异步版，支持取消
sp.DisConnect();
```

**BaseModbusRTU**（静态）：
```csharp
byte[] crc = BaseModbusRTU.CalculateCRC16(data);             // CRC16 校验
byte[] cmd = BaseModbusRTU.GetCommandBytes(0x01, 0x03, regAddr, regCount);  // 生成带CRC的命令
```

**BaseSQLConnection**（MySQL，实现 IToolFunction）：
```csharp
var db = new BaseSQLConnection("192.168.2.120", "Log", "root", "123456");
db.Connect();
db.Insert(entity);                  // 插入实体
db.Insert("TableName", entity);     // 指定表名
db.Insert_WithSplitTable(entity);   // 分表插入（实体需定义 SplitTable 特性）
db.DisConnect();
```

### 3.12 文件句柄检测：BaseHandle

```csharp
// 用 handle.exe 检测哪个进程占用了文件
string[]? procs = await BaseHandle.GetLockedProcessName("handle.exe", "file.txt", token);
```

### 3.13 树转换：BaseTree

```csharp
List<TreeNode> tree = list.ToTree(x => x.Id, x => x.ParentId);   // 平铺转树
List<TreeNode> tree = list.ToTree();                              // 约定式（Id/ParentId）
```

### 3.14 基元抽象：AbstractTool（扩展体系）

用于封装可复用的"硬件/软件基元"（相机、控制器、串口等），核心接口：

```csharp
// 实现 IToolFunction：具备连接状态和 Connect/DisConnect 的"工具"
public interface IToolFunction
{
    string ToolName { get; }
    bool StateConnect { get; }
    BaseResult Connect();
    BaseResult DisConnect();
}

// 完整基元：泛型指定调试界面 TForm 和配置 TConfig
public interface IAbstractTool<out TForm, out TConfig> : IToolFunction, INotifyPropertyChanged
    where TForm : IToolForm where TConfig : IToolConfig
{
    bool ShowInStation { get; init; }
    bool AutoConnect { get; set; }
    bool StateShowLog { get; set; }
    ConnectStrategy ConnectStrategy { get; set; }
    TForm RuntimeView { get; }
    TConfig RuntimeParameter { get; }
    TConfig DefaultParameter { get; }
    BaseResult GetRuntimeParameter();        // 硬件参数 -> RuntimeParameter
    BaseResult OverwriteRuntimeParameter();  // Default -> Runtime -> 硬件
    BaseResult OverwriteDefaultParameter();  // 硬件 -> Runtime -> Default
}

public interface IToolForm { }      // 调试界面标记接口
public interface IToolConfig { }    // 配置标记接口

public enum ConnectStrategy { UseRuntimeParameters, UseDefaultParameters, OverrideDefaultParameters }
```

RewardAutoMate 不涉及硬件，此部分一般用不到，了解即可。

### 3.15 枚举：ConstMember.cs

```csharp
public enum LogLevel { Debug = 0, Info = 10, Warning = 20, Error = 30 }
public enum AuthorityLevel { Operator = 0, Engineer = 10, Administrator = 20, Developer = 30 }
```

---

## 四、BaseWPFControl 使用指南

### 4.1 命名空间映射（AssemblyInfo.cs）

```csharp
[assembly: XmlnsDefinition("https://BaseWPFControl", "BaseWPFControl.ToolDialog")]
[assembly: XmlnsDefinition("https://BaseWPFControl", "BaseWPFControl.Extension")]
[assembly: XmlnsDefinition("https://BaseWPFControl", "BaseWPFControl.Converters")]
[assembly: XmlnsDefinition("https://BaseWPFControl", "BaseWPFControl.UIWidget")]
[assembly: XmlnsDefinition("https://BaseWPFControl", "BaseWPFControl")]
[assembly: XmlnsPrefix("https://BaseWPFControl", "BaseWPFControl")]
```

所以 XAML 里 `xmlns:BaseWPFControl="https://BaseWPFControl"` 一个前缀即可用所有控件/转换器/扩展。

### 4.2 控件总览

| 控件 | 命名空间 | 用途 |
|------|---------|------|
| `MemoryBlock` | UIWidget | 内存占用显示块（每秒刷新，属性：`Header`、`Interval`） |
| `LogBlock` | UIWidget | 日志显示块，**实例化即自动订阅 BaseLogManager 日志**；`LogBlock.LogView` 可展示完整日志列表 |
| `LogView` | UIWidget | 日志列表视图（时间/级别/消息/详情），双击可看详情、可清空 |
| `ConfigControl` | UIWidget | 配置行容器（`Header` 左标题 + 右侧内容），属性 `LeftWidth`/`RightWidth` |
| `NumericBox` | UIWidget | 数值输入框（`Value`/`MinValue`/`MaxValue`/`Interval`/`ShowButton`/`TextFormat`） |
| `SelectFileBox` | UIWidget | 文件选择框（`ExtensionFilter` 过滤、`MultiselectLine` 多选） |
| `SelectFolderBox` | UIWidget | 文件夹选择框 |
| `DateTimePickerBox`/`Clock` | UIWidget | 日期时间选择器（`SelectedDateTime`、`DateTimeFormat`、`IsShowHour/Minute/Second`） |
| `PositionBox` | UIWidget | 轴位置输入框（`AxisIndex`） |
| `BaseClipBorder` | UIWidget | 可裁剪圆角容器（`CornerRadius`），可代替 Border |
| `Loading` | UIWidget | 环形加载动画（`Progress`/`Diameter`/`StrokeThickness`/`FocusedBrush`/`StrokeDashArray`） |
| `CurveChart` | UIWidget | 曲线图（`DataCollection`/`LineColor`/`PointSize`/`MinValue`/`MaxValue`/`TimeFormat`/`LineSmooth`） |
| `FlipBox`/`FlipBoxItem` | UIWidget | 轮播容器（`VerticalOffset`/`AnimateDuration`/`IsLoop`/`IsContinuousSwitching`） |
| `StepBar`/`StepItem` | UIWidget | 步骤条（`ItemsSource` 绑定 `BaseAction` 列表） |
| `SmoothProgressBar` | UIWidget | 平滑进度条（`Value` 动画过渡，`Duration` 毫秒） |
| `TextEditor` | UIWidget | 代码编辑器（基于 BaseAvalonEdit/TextEditorUtil） |
| `SideTabControl`/`SideTabItem` | UIWidget | 侧边标签页（`Header`/`Icon`/`IsSelected`） |
| `ToolWindow` | UIWidget | 工具窗口（静态 `ShowContent(control)` 弹出内容窗口） |
| `Dialog` | UIWidget | 对话框基类（`ShowDialog()`/`ShowDialogAsWindow()`/`CloseDialog()`） |
| `DialogContainer` | UIWidget | 对话框容器（由 `DefaultWindowStyle` 注入窗口模板） |
| `DialogWindow` | UIWidget | 对话框独立窗口（`ShowDialogAsWindow` 用） |
| `LoadingDialog` | ToolDialog | 加载提示框（带动画） |
| `LoadingCancelDialog` | ToolDialog | 带取消按钮的加载框（传 `CancellationTokenSource`） |
| `MessageDialog` | ToolDialog | 确认框（确定/取消，返回 bool） |
| `NotificationDialog` | ToolDialog | 通知框（仅确定，异步） |
| `EnumBindingSource` | Extension | 标记扩展，枚举转列表绑定 ComboBox |

### 4.3 对话框体系（重点）

BaseWPFControl 的对话框分两种形式：

**① 容器内对话框**：`Dialog.ShowDialog()` 把对话框覆盖到主窗口上（需 `DefaultWindowStyle` 提供 `DialogContainer`）。

**② 独立窗口对话框**：`Dialog.ShowDialogAsWindow()` 用无边框置顶小窗口弹出（不依赖容器，RewardAutoMate 当前主窗口未用 DefaultWindowStyle，用这个最省事）。

**现成系统对话框**（全部静态方法）：

```csharp
// 加载提示（异步显示，任务完成后关闭）
_ = LoadingDialog.Show("正在执行搜索...");      // 显示（不阻塞）
LoadingDialog.Close();                          // 关闭
await LoadingDialog.DoAction(() => DoWork(), "执行中...");   // 封装执行+显示+关闭

// 带取消的加载框（点取消会 Cancel 传入的 CancellationTokenSource）
var _cts = new CancellationTokenSource();
_ = LoadingCancelDialog.Show("正在搜索，可取消...", _cts);
// ... 在任务里用 _cts.Token 判断取消 ...
LoadingCancelDialog.Close();

// 确认框（返回 bool）
bool confirmed = await MessageDialog.Show("确定要退出吗？");

// 通知框（异步提示）
await NotificationDialog.Show("执行完成");

// 独立窗口形式
LoadingDialog.ShowAsWindow("...");
bool ok = MessageDialog.ShowAsWindow("...");
NotificationDialog.ShowAsWindow("...");
```

**自定义对话框**（继承 Dialog）：

```xml
<!-- MyDialog.xaml 根元素 -->
<BaseWPFControl:Dialog x:Class="RewardAutoMate.MyDialog"
        xmlns="..." xmlns:BaseWPFControl="https://BaseWPFControl">
    <StackPanel>
        <TextBlock Text="自定义内容" />
        <Button Content="确定" Click="Button_Ok_Click" />
        <Button Content="取消" Click="Button_Cancel_Click" />
    </StackPanel>
</BaseWPFControl:Dialog>
```

```csharp
public partial class MyDialog : BaseWPFControl.UIWidget.Dialog
{
    private MyDialog(string _msg) { InitializeComponent(); TextBlock_Info.Text = _msg; }

    private void Button_Ok_Click(object sender, RoutedEventArgs e) => CloseDialog(true);
    private void Button_Cancel_Click(object sender, RoutedEventArgs e) => CloseDialog(false);

    public static async Task<bool> Show(string _msg)
        => await new MyDialog(_msg).ShowDialog() is true;
}
```

> 对话框返回结果通过 `CloseDialog(object? _result)` 传入，调用方 `await dialog.ShowDialog()` 拿到；`_result is true` 判断确定。

### 4.4 附加属性（Attached Properties）

**ControlProperty**（通用，作用于多种控件）：
```xml
<!-- 圆角 -->
<Button BaseWPFControl:ControlProperty.CornerRadius="10" />
<!-- 前缀/后缀内容（如单位） -->
<TextBox BaseWPFControl:ControlProperty.PrefixContent="X" />
<NumericBox BaseWPFControl:ControlProperty.PostfixContent="deg/s" />
<!-- 水印 -->
<TextBox BaseWPFControl:ControlProperty.WaterMask="请输入搜索词" />
<!-- 强调色 / 鼠标悬停色 / 按下色 -->
<Button BaseWPFControl:ControlProperty.HighLight="Orange" />
<Button BaseWPFControl:ControlProperty.MouseOverColor="LightGray" />
<Button BaseWPFControl:ControlProperty.PressedColor="Gray" />
```

**ButtonProperty.Icon / MenuItemProperty.Icon**（按钮/菜单图标）：
```xml
<Button BaseWPFControl:ButtonProperty.Icon="{StaticResource MyIcon}" />
<MenuItem BaseWPFControl:MenuItemProperty.Icon="{StaticResource MyIcon}" />
```

**ProgressBarProperty.ShowValue**（进度条显示数值）：
```xml
<ProgressBar BaseWPFControl:ProgressBarProperty.ShowValue="True" />
```

### 4.5 扩展：EnumBindingSource（枚举绑定）

```xml
<ComboBox ItemsSource="{BaseWPFControl:EnumBindingSource EnumType=BaseTool:LogLevel}" />
```

### 4.6 转换器（BaseWPFControl.Converters）

| 转换器 | 功能 |
|--------|------|
| `BooleanConverter` | bool 相关转换 |
| `BorderThicknessConverter` | 边框厚度 |
| `CompareConverter` | 比较值 |
| `CornerRadiusConverter` | 圆角 |
| `DateTimeConverter` | 日期时间格式化 |
| `EnumConverter` | 枚举<->描述 |
| `ListConverter` | 集合转换 |
| `MathConverter` | 数学运算 |
| `NullConverter` | null 处理 |
| `PercentConverter` | 百分比 |
| `StringConverter` | 字符串处理 |
| `TagConverter` | Tag 相关 |

使用示例（XAML 中作为资源）：
```xml
<Window.Resources>
    <BaseWPFControl:BooleanConverter x:Key="BoolConv" />
</Window.Resources>
<CheckBox IsChecked="{Binding Flag, Converter={StaticResource BoolConv}}" />
```

### 4.7 工具：ControlFunction（图像/元素操作）

```csharp
using BaseWPFControl;

BitmapSource bmp = ControlFunction.CaptureUIElement(element);   // 截取 UI 元素
Bitmap bmp2 = ControlFunction.CaptureScreen(region);            // 截取屏幕区域
byte[] bytes = ControlFunction.BitmapSourceToByteArray(source);
ControlFunction.SaveImageToPNG(source, "a.png");                // PNG/JPEG/Tiff/BMP
ControlFunction.ClearEvents(element, routedEvent);              // 清空事件
var child = element.TryGetChild<TargetControl>();               // 递归找子元素
```

---

## 五、在 RewardAutoMate 中的复用示例

RewardAutoMate 是 WPF + Selenium 的 Rewards 自动化程序。以下是"不用重复造轮子"的落地建议：

### 5.1 日志：并行使用，避免自动建库的坑

RewardAutoMate 已用 Serilog（`AppLogger`）。若想复用 `BaseLogManager` 并接到界面：

```csharp
// 启动时（App.xaml.cs OnStartup）
BaseDefine.DirectoryPathOnSaveLog = Path.Combine(AppContext.BaseDirectory, "Logs");
BaseLogManager.Register_SaveLogToFile();          // 写本地文件（按天，保留30天）

// 把日志接到 WPF 实时日志框（替代自己写 WpfLogSink 逻辑）
BaseLogManager.Register_SendLogFunction(_log =>
    Dispatcher.Invoke(() =>
    {
        TextBox_Log.AppendText($"[{_log.LogTime:HH:mm:ss} {_log.LogLevel}] {_log.LogMessage}{Environment.NewLine}");
    }));
```

> 注意：`BaseLogManager` 静态构造会自动注册 SQLite。若不想生成数据库文件，可用下面方式避免首次触发：不要直接调用它，或者在引用前先 `BaseDefine.SQLiteFilePathOnSaveLog` 设置到统一目录。实际上只要用到类就必然建库，Rewards 场景建议**继续用 Serilog，BaseLogManager 仅用于需要对接 LogBlock 的场景**。

### 5.2 配置：用 BaseFunction 替代手动 Json

```csharp
public sealed class RewardsSettings
{
    public int MaxPcSearches { get; set; } = 50;
    public int MaxMobileSearches { get; set; } = 50;
    public double ExecuteIntervalHours { get; set; } = 12;
    // ...
}

// 读取（不存在则返回默认值）
var _settings = BaseFunction.LoadConfig_Json<RewardsSettings>(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json")) ?? new RewardsSettings();

// 保存
BaseFunction.SaveConfig_Json(_settings, "appsettings.json");

// 深拷贝备份
var _backup = BaseFunction.DeepClone(_settings);
```

### 5.3 任务编排：BaseTaskQueue + BaseAction 管理四阶段流程

```csharp
BaseTaskQueue _queue = new();
_queue.Enqueue(() => { uint n = await WindowsPCServer.AutoSearchWithRetry(false); 
                      return n > 0 ? BaseResult.OK() : BaseResult.Error("PC搜索失败"); });
_queue.Enqueue(() => { uint n = await WindowsPCServer.AutoSearchWithRetry(true);
                      return BaseResult.OK(); });
_queue.Enqueue(() => new RewardsDashboardService(_driver).ClaimBenefits() > 0 
                      ? BaseResult.OK() : BaseResult.OK("无福利"));
await _queue.ExecuteQueueAsync();
```

### 5.4 取消令牌：用 BaseAction.Token 统一支持"手动执行中退出"

```csharp
// 在搜索循环中检查
public static async Task<uint> AutoSearch(...)
{
    for (int i = 0; i < max; i++)
    {
        BaseAction.Token.ThrowIfCancellationRequested();   // 用户点退出时取消
        ...
    }
}
// 主窗口退出时
BaseAction.Cancel();
```

### 5.5 界面：对话框与状态控件

```csharp
// 手动执行前的确认
bool _go = await MessageDialog.ShowAsWindow("确定开始自动执行 Rewards 任务吗？");
if (!_go) return;

// 执行中显示加载框（独立窗口形式，无需 DefaultWindowStyle）
_ = LoadingCancelDialog.ShowAsWindow("正在执行，可点取消");   // 无取消令牌版本用 ShowAsWindow
```

XAML 底部状态栏（RewardAutoMate 已用 MemoryBlock）：
```xml
<DockPanel>
    <BaseWPFControl:MemoryBlock DockPanel.Dock="Right" Header="内存占用" />
    <BaseWPFControl:LogBlock DockPanel.Dock="Right" x:Name="LogBlock" />
</DockPanel>
```

配置行（替代手写 Grid+TextBlock+控件）：
```xml
<BaseWPFControl:ConfigControl Header="最大 PC 搜索次数">
    <BaseWPFControl:NumericBox Width="120" Value="50" MinValue="1" MaxValue="100" />
</BaseWPFControl:ConfigControl>
```

### 5.6 条件等待：替代自写的轮询

Rewards 页面元素加载等待（RewardsDashboardService 中 `WaitForPageLoad` 目前是固定 Delay）：

```csharp
try
{
    await BaseFunction.Condition(() =>
        Driver.FindElements(By.CssSelector("header p")).Any(p => int.TryParse(p.Text, out var v) && v > 1000),
        10000, BaseAction.Token);
}
catch (TimeoutException) { AppLogger.Warning("页面元素等待超时"); }
```

---

## 六、常见问题与注意事项

1. **BaseWPFControl 依赖 BaseTool.dll**：dll 引用时必须同时部署两者；源码引用时 BaseWPFControl.csproj 已自动带 ProjectReference。
2. **`https://BaseWPFControl` 是映射命名空间**：不要当成 URL 访问；前缀由 `XmlnsDefinition` 统一映射。
3. **默认样式自动生效**：合并 `Themes/Generic.xaml` 后，普通 `Button/TextBox/ComboBox/CheckBox` 等自动换成 BaseWPFControl 样式。若某处想用原生样式，用 `Style="{x:Null}"` 或显式指定。
4. **`DefaultWindowStyle` 与对话框**：`Dialog.ShowDialog()`（容器内）需要窗口模板里有 `DialogContainer`（由 `DefaultWindowStyle` 提供）。RewardAutoMate 主窗口若不用该样式，请一律使用 `ShowAsWindow()` 系列。
5. **BaseLogManager 静态构造默认建 SQLite 库**：在 `SaveLog\LogOnSaveSQLite.db` 按天分表、保留 30 天。不想要就在引用前重定向路径，或干脆只用 Serilog。
6. **LogBlock 自动订阅日志**：XAML 里实例化 `LogBlock` 后，它会自动 `Register_SendLogFunction`，所有 `BaseLogManager.SendLog` 都会显示到界面，无需额外接线。RewardAutoMate 若用 Serilog，需要手动把 Serilog 事件转发给 `BaseLogManager`（通过 `Register_SendLogFunction`）才能让 LogBlock 显示。
7. **LanguageVersion 需 preview**：库源码使用 C# 12+ 特性（集合表达式 `[.. x]`、主构造函数）。如果你把源码复制进项目而不是引用 DLL，需在 csproj 加 `<LangVersion>preview</LangVersion>`。
8. **net8.0-windows 匹配**：RewardAutoMate 是 `net8.0-windows`，可直接兼容（都带 `-windows` 才能引用 WPF 程序集）。
9. **`BaseSingleton`、`BaseHandle`、`BaseSQLConnection`、Socket/串口/Modbus** 等与 Rewards 场景无关，按需使用，不需要全部引入。
10. **编码**：源码使用 UTF-8 与中文注释，若在旧编辑器（如老版 VS/GBK）打开可能出现中文乱码，建议 VS2022+ / VSCode 打开。

---

## 附：RewardAutoMate 当前对接状态速查

| 功能 | RewardAutoMate 现状 | 可复用的库能力 | 建议 |
|------|--------------------|---------------|------|
| 日志 | Serilog（AppLogger） | BaseLogManager（文件/SQLite/重定向/LogBlock） | 保留 Serilog；仅 LogBlock 展示时做转发 |
| 配置 | 硬编码 AppConfig 静态类 | BaseFunction.LoadConfig_Json / SaveConfig_Json | 改为 Json 配置，成熟可靠 |
| 任务取消 | 自建 CancellationTokenSource | BaseAction.Token / Cancel() | 可统一收编 |
| 任务串行 | 自写 async/await | BaseTaskQueue | 可选 |
| 等待页面 | 固定 Task.Delay | BaseFunction.Condition（超时+取消） | 强烈建议替换 |
| 界面提示 | 无/自写 | LoadingDialog / LoadingCancelDialog / MessageDialog / NotificationDialog | 直接使用 ShowAsWindow 系列 |
| 状态栏 | MemoryBlock 已用 | MemoryBlock / LogBlock / SmoothProgressBar | 已接入，可加 LogBlock |
| 表单 | 手写 XAML | ConfigControl / NumericBox / SelectFileBox | 可选美化 |
| 曲线/热力图 | 自绘 | CurveChart | 可选 |

---

*本文档基于 `D:\VisualStudioCode\ToolsRepositoryV4` 源码整理，与 RewardAutoMate 引用的 `External\BaseTool.dll` / `BaseWPFControl.dll` 为同一代码库产物。*
