# RewardAutoMate —— AGENTS.md

自动完成 Microsoft Rewards（Bing 搜索奖励）日常任务的 Windows 桌面工具。仅个人学习使用。

## 技术栈

- **.NET 8 + WPF**（解决方案 `src/RewardAutoMate.sln`）
- **Selenium WebDriver** + Edge（`WebDriverUpdater` 自动匹配 msedgedriver 版本）
- **SqlSugar + SQLite**（`RewardsHistory.db` 执行历史、`HeatMapData.db` 热力图数据）
- 自研库 **BaseTool / BaseWPFControl**（日志、通用函数、UI 控件）

## 部署与测试环境：Windows Server

本项目**在远程 Windows Server 上部署和测试**，不在本机运行。服务器地址、账号、SSH 别名、系统版本等全部以这份文档为准，不要凭记忆填写：

```
D:\AgentCode\Project\「2026年10月28日」SSH 操作\WindowsServer连接与配置.md
```

- **连接方式**：`ssh srv`（已配公钥免密）；传文件用 `scp 文件 srv:C:/路径/`。远程桌面用 `mstsc /v:<服务器IP>`
- **服务器部署路径**：`D:\Rewards`，主程序 `D:\Rewards\RewardAutoMate.exe`
- **启动方式**：桌面快捷方式 `Rewards.lnk`；启动文件夹里放了一份副本，登录后自动运行
- **GUI 启动限制**：SSH 里的进程落在 session 0，窗口不可见。要在交互桌面启动需用 `schtasks /create ... /it` 建一次性任务再 `/run`
- **代理**：服务器系统代理为 `127.0.0.1:7890`（mihomo）；程序自身走 WinINET 代理，命令行下载工具需另设 `HTTP_PROXY`

## 注意事项

1. 自动化行为可能违反 Microsoft Rewards 服务条款，测试时不要用主账号
2. 依赖 Edge 浏览器与 msedgedriver，服务器上 Edge 版本变化后首次运行会重新下载驱动，需保证代理可用
3. 数据库文件随部署目录走，测试数据不要提交到仓库
