using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using RewardAutoMate.Configuration;
using RewardAutoMate.Logging;
using RewardAutoMate.Models;

namespace RewardAutoMate.Services;

/// <summary>
/// 邮件通知服务，用于异常报警和每日执行汇总
/// </summary>
public static class EmailNotificationService
{
    private static readonly Dictionary<string, DateTime> LastNotifyTimes = [];
    private static readonly object Lock = new object();

    static EmailNotificationService()
    {
        LoadConfigFromFile();
    }

    /// <summary>
    /// 从 email_config.json 加载 SMTP 配置（与邮件测试项目格式一致），存在则覆盖 AppConfig
    /// </summary>
    private static void LoadConfigFromFile()
    {
        try
        {
            string _path = Path.Combine(AppContext.BaseDirectory, "External", "email_config.json");
            if (!File.Exists(_path))
            {
                return;
            }

            string _json = File.ReadAllText(_path);
            using JsonDocument _doc = JsonDocument.Parse(_json);
            JsonElement _root = _doc.RootElement;

            if (_root.TryGetProperty("smtp_server", out JsonElement _srv) && _srv.GetString() is { Length: > 0 } _server)
            {
                AppConfig.SmtpServer = _server;
            }
            if (_root.TryGetProperty("smtp_port", out JsonElement _port) && _port.TryGetInt32(out int _port_value))
            {
                AppConfig.SmtpPort = _port_value;
            }
            if (_root.TryGetProperty("sender", out JsonElement _from) && _from.GetString() is { Length: > 0 } _sender)
            {
                AppConfig.SmtpFrom = _sender;
            }
            if (_root.TryGetProperty("auth_code", out JsonElement _pwd) && _pwd.GetString() is { Length: > 0 } _auth)
            {
                AppConfig.SmtpPassword = _auth;
            }
            if (_root.TryGetProperty("receiver", out JsonElement _to) && _to.GetString() is { Length: > 0 } _receiver)
            {
                AppConfig.SmtpTo = _receiver;
            }

            AppLogger.Info($"已从 email_config.json 加载邮件配置: {AppConfig.SmtpServer}:{AppConfig.SmtpPort} -> {AppConfig.SmtpTo}");
        }
        catch (Exception ex)
        {
            AppLogger.Warning($"读取 email_config.json 失败，使用默认配置: {ex.Message}");
        }
    }

    /// <summary>
    /// 发送异常通知邮件（附最近执行历史上下文）
    /// </summary>
    public static async Task SendExceptionNotificationAsync(
        string _exception_type,
        string _message,
        string _stack_trace,
        string _account_status,
        List<string> _executed_tasks)
    {
        AppLogger.Info($"异常通知触发: {_exception_type}");

        if (!AppConfig.NotifyOnFailure)
        {
            AppLogger.Debug("邮件通知已禁用（NotifyOnFailure=false），跳过");
            return;
        }

        if (!IsSmtpConfigured())
        {
            AppLogger.Warning("邮件通知已启用但 SMTP 未配置，跳过发送。请配置 SmtpServer/SmtpFrom/SmtpTo/SmtpPassword");
            return;
        }

        // ! 防轰炸检查
        if (IsInCooldown(_exception_type))
        {
            AppLogger.Info($"异常 [{_exception_type}] 处于冷却期（{AppConfig.NotifyCooldownMinutes}分钟），跳过邮件通知");
            return;
        }

        List<ExecutionRecord> _recent_history = [];
        try { _recent_history = ExecutionHistoryService.GetRecent(7); } catch { /* 忽略 */ }

        string _subject = $"[RewardAutoMate] 异常通知 - {_exception_type}";
        string _body = BuildExceptionEmailBody(_exception_type, _message, _stack_trace, _account_status, _executed_tasks, _recent_history);

        await SendEmailAsync(_subject, _body);
        RecordNotifyTime(_exception_type);
    }

    /// <summary>
    /// 发送每周执行汇总邮件（周一触发，汇总上一个完整自然周：按天概况 + 明细 + 异常）
    /// </summary>
    public static async Task SendWeeklySummaryAsync()
    {
        if (!AppConfig.SendWeeklyReport)
        {
            return;
        }

        if (!IsSmtpConfigured())
        {
            AppLogger.Warning("周报已启用但 SMTP 未配置，跳过发送");
            return;
        }

        // ! 统计区间 = 上一个完整自然周（上周一 00:00 ~ 上周日 23:59:59）
        DateTime _week_start = ExecutionHistoryService.GetWeekStart(DateTime.Today).AddDays(-7);
        DateTime _week_end = ExecutionHistoryService.GetWeekStart(DateTime.Today).AddSeconds(-1);
        string _subject = $"[RewardAutoMate] 每周执行汇总  {_week_start:yyyy-MM-dd} ~ {_week_end:MM-dd}";
        string _body = BuildWeeklySummaryEmailBody(_week_start, _week_end);

        await SendEmailAsync(_subject, _body);
    }

    /// <summary>
    /// 发送自定义邮件
    /// </summary>
    public static async Task SendEmailAsync(string _subject, string _body)
    {
        if (!IsSmtpConfigured())
        {
            return;
        }

        string[] _recipients = AppConfig.SmtpTo.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (_recipients.Length == 0)
        {
            AppLogger.Warning("收件人列表为空，无法发送邮件");
            return;
        }

        AppLogger.Info($"正在发送邮件通知... 收件人: {AppConfig.SmtpTo}, SMTP: {AppConfig.SmtpServer}:{AppConfig.SmtpPort}");

        // ! 尝试发送，失败重试 3 次
        for (int _retry = 0; _retry < 3; _retry++)
        {
            try
            {
                AppLogger.Debug($"  -> 邮件发送尝试 {_retry + 1}/3...");
                using SmtpClient _client = new SmtpClient(AppConfig.SmtpServer, AppConfig.SmtpPort);
                _client.EnableSsl = AppConfig.SmtpEnableSsl;
                _client.Credentials = new NetworkCredential(AppConfig.SmtpFrom, AppConfig.SmtpPassword);
                _client.Timeout = 30000;

                MailMessage _mail = new MailMessage();
                _mail.From = new MailAddress(AppConfig.SmtpFrom, "RewardAutoMate");
                foreach (string _recipient in _recipients)
                {
                    string _trimmed = _recipient.Trim();
                    if (!string.IsNullOrEmpty(_trimmed))
                    {
                        _mail.To.Add(_trimmed);
                    }
                }
                _mail.Subject = _subject;
                _mail.Body = _body;
                _mail.IsBodyHtml = false;
                _mail.BodyEncoding = Encoding.UTF8;
                _mail.SubjectEncoding = Encoding.UTF8;

                await _client.SendMailAsync(_mail);
                AppLogger.Info($"邮件发送成功: {_subject} -> {AppConfig.SmtpTo}");
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"邮件发送失败（第 {_retry + 1}/3 次尝试）", ex);
                if (_retry < 2)
                {
                    AppLogger.Info("  -> 5秒后重试...");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
                else
                {
                    AppLogger.Error("邮件发送最终失败，所有重试次数已用完");
                }
            }
        }
    }

    private static bool IsSmtpConfigured()
    {
        return !string.IsNullOrEmpty(AppConfig.SmtpServer)
            && !string.IsNullOrEmpty(AppConfig.SmtpFrom)
            && !string.IsNullOrEmpty(AppConfig.SmtpPassword)
            && !string.IsNullOrEmpty(AppConfig.SmtpTo);
    }

    private static bool IsInCooldown(string _exception_type)
    {
        lock (Lock)
        {
            if (LastNotifyTimes.TryGetValue(_exception_type, out DateTime _last_time))
            {
                return DateTime.Now - _last_time < TimeSpan.FromMinutes(AppConfig.NotifyCooldownMinutes);
            }
            return false;
        }
    }

    private static void RecordNotifyTime(string _exception_type)
    {
        lock (Lock)
        {
            LastNotifyTimes[_exception_type] = DateTime.Now;
        }
    }

    private static string BuildExceptionEmailBody(
        string _exception_type,
        string _message,
        string _stack_trace,
        string _account_status,
        List<string> _executed_tasks,
        List<ExecutionRecord> _recent_history)
    {
        StringBuilder _sb = new StringBuilder();
        _sb.AppendLine("========================================");
        _sb.AppendLine("RewardAutoMate 异常通知");
        _sb.AppendLine("========================================");
        _sb.AppendLine();
        _sb.AppendLine($"异常类型: {_exception_type}");
        _sb.AppendLine($"发生时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        _sb.AppendLine($"异常消息: {_message}");
        _sb.AppendLine();
        _sb.AppendLine("----------------------------------------");
        _sb.AppendLine("账户状态:");
        _sb.AppendLine("----------------------------------------");
        _sb.AppendLine(_account_status);
        _sb.AppendLine();

        _sb.AppendLine("----------------------------------------");
        _sb.AppendLine("最近 7 天执行概况:");
        _sb.AppendLine("----------------------------------------");
        _sb.AppendLine(BuildRecentHistoryText(_recent_history));
        _sb.AppendLine();

        _sb.AppendLine("----------------------------------------");
        _sb.AppendLine("已执行任务:");
        _sb.AppendLine("----------------------------------------");
        if (_executed_tasks.Count == 0)
        {
            _sb.AppendLine("无");
        }
        else
        {
            foreach (string _task in _executed_tasks)
            {
                _sb.AppendLine($"- {_task}");
            }
        }
        _sb.AppendLine();
        _sb.AppendLine("----------------------------------------");
        _sb.AppendLine("堆栈跟踪:");
        _sb.AppendLine("----------------------------------------");
        _sb.AppendLine(_stack_trace);
        _sb.AppendLine();
        _sb.AppendLine("========================================");
        _sb.AppendLine("本邮件由 RewardAutoMate 自动发送");
        _sb.AppendLine("========================================");

        return _sb.ToString();
    }

    private static string BuildWeeklySummaryEmailBody(DateTime _week_start, DateTime _week_end)
    {
        // 取上周区间内的执行记录（供周报汇总），EndTime 加 1 秒转为闭区间查询
        List<ExecutionRecord> _records = ExecutionHistoryService.GetByRange(_week_start, _week_end.AddSeconds(1))
            .OrderBy(it => it.StartTime)
            .ToList();

        int _total = _records.Count;
        int _success = _records.Count(it => it.Success);
        int _fail = _total - _success;

        // ! 真实积分对比：上周最后一次读取的积分 - 上上周最后一次读取的积分
        int _last_week_points = ExecutionHistoryService.GetLastPointsBefore(_week_start);
        int _this_week_points = _records
            .Where(it => it.CurrentPoints > 0)
            .OrderByDescending(it => it.StartTime)
            .Select(it => it.CurrentPoints)
            .FirstOrDefault();
        bool _has_real_points = _last_week_points > 0 && _this_week_points > 0;
        int _week_gain = _has_real_points ? _this_week_points - _last_week_points : 0;
        int _estimated_points = _records.Sum(it => it.EstimatedPoints);

        StringBuilder _sb = new StringBuilder();
        _sb.AppendLine("========================================");
        _sb.AppendLine($"RewardAutoMate 每周执行汇总  {_week_start:yyyy-MM-dd} ~ {_week_end:MM-dd}");
        _sb.AppendLine("========================================");
        _sb.AppendLine();
        _sb.AppendLine($"上周执行: {_total} 次（成功 {_success}，失败 {_fail}）");
        if (_has_real_points)
        {
            _sb.AppendLine($"上周积分增长: +{_week_gain:N0}（当前总积分: {_this_week_points:N0}）");
            _sb.AppendLine($"（依据: 上上周结束 {_last_week_points:N0} -> 上周结束 {_this_week_points:N0}）");
        }
        else
        {
            _sb.AppendLine($"上周估计积分: +{_estimated_points:N0}（账户积分数据不足，为程序估算）");
        }
        _sb.AppendLine();

        // ! 按天概况（积分列为当日账户积分变化，数据不足时显示 -）
        _sb.AppendLine("----------------------------------------");
        _sb.AppendLine("上周概况（按天）:");
        _sb.AppendLine("----------------------------------------");
        var _grouped = _records
            .GroupBy(it => it.StartTime.Date)
            .OrderBy(it => it.Key);
        if (!_grouped.Any())
        {
            _sb.AppendLine("（上周尚无执行记录）");
        }
        else
        {
            string[] _week_names = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
            int _prev_day_last_points = _last_week_points;
            foreach (var _group in _grouped)
            {
                List<ExecutionRecord> _day_records = [.. _group];
                int _day_total = _day_records.Count;
                int _day_success = _day_records.Count(it => it.Success);
                int _day_fail = _day_total - _day_success;

                int _day_last_points = _day_records
                    .Where(it => it.CurrentPoints > 0)
                    .OrderByDescending(it => it.StartTime)
                    .Select(it => it.CurrentPoints)
                    .FirstOrDefault();
                bool _has_day_gain = _prev_day_last_points > 0 && _day_last_points > 0;
                string _day_gain_text = _has_day_gain ? $"积分 +{_day_last_points - _prev_day_last_points:N0}" : "积分 -";
                if (_day_last_points > 0) { _prev_day_last_points = _day_last_points; }

                int _week_index = (int)_group.Key.DayOfWeek == 0 ? 6 : (int)_group.Key.DayOfWeek - 1;
                _sb.AppendLine($"  {_group.Key:MM-dd} {_week_names[_week_index]}  {_day_total} 次  成功 {_day_success}  失败 {_day_fail}  {_day_gain_text}");
            }
        }
        _sb.AppendLine();

        // ! 上周明细
        _sb.AppendLine("----------------------------------------");
        _sb.AppendLine("上周执行明细:");
        _sb.AppendLine("----------------------------------------");
        if (_records.Count == 0)
        {
            _sb.AppendLine("（无）");
        }
        else
        {
            foreach (ExecutionRecord _record in _records)
            {
                string _status = _record.Success ? "成功" : "失败";
                string _points_text = _record.CurrentPoints > 0
                    ? $"积分 {_record.CurrentPoints:N0}"
                    : $"估 +{_record.EstimatedPoints}";
                _sb.AppendLine($"  [{_record.StartTime:MM-dd HH:mm}] {_status}  " +
                               $"搜索 {_record.PcSearches}+{_record.MobileSearches}={_record.TotalSearches}  " +
                               $"任务 {_record.TotalTasks}  耗时 {_record.DurationMinutes:F0}分  {_points_text}");
                if (!_record.Success && !string.IsNullOrEmpty(_record.ErrorMessage))
                {
                    _sb.AppendLine($"       错误: {Truncate(_record.ErrorMessage, 200)}");
                }
            }
        }
        _sb.AppendLine();

        // ! 异常提醒
        _sb.AppendLine("----------------------------------------");
        _sb.AppendLine("上周异常:");
        _sb.AppendLine("----------------------------------------");
        List<ExecutionRecord> _failed = _records.Where(it => !it.Success).ToList();
        if (_failed.Count == 0)
        {
            _sb.AppendLine("无");
        }
        else
        {
            foreach (ExecutionRecord _record in _failed)
            {
                _sb.AppendLine($"- [{_record.StartTime:MM-dd HH:mm}] {Truncate(_record.ErrorMessage, 150)}");
            }
        }
        _sb.AppendLine();
        _sb.AppendLine("========================================");
        _sb.AppendLine("本邮件由 RewardAutoMate 自动发送");
        _sb.AppendLine("========================================");

        return _sb.ToString();
    }

    /// <summary>
    /// 按天生成最近执行概况文本
    /// </summary>
    private static string BuildRecentHistoryText(List<ExecutionRecord> _records)
    {
        StringBuilder _sb = new StringBuilder();
        var _grouped = _records
            .GroupBy(it => it.StartTime.Date)
            .OrderByDescending(it => it.Key);

        if (!_grouped.Any())
        {
            return "（暂无记录）";
        }

        foreach (var _group in _grouped)
        {
            List<ExecutionRecord> _day_records = [.. _group];
            int _total = _day_records.Count;
            int _success = _day_records.Count(it => it.Success);
            int _fail = _total - _success;
            int _points = _day_records.Sum(it => it.EstimatedPoints);
            _sb.AppendLine($"  {_group.Key:MM-dd}  {_total} 次  成功 {_success}  失败 {_fail}  积分 +{_points}");
        }
        return _sb.ToString().TrimEnd();
    }

    private static (int Total, int Success, int Fail, int Points) Summarize(List<ExecutionRecord> _records)
    {
        int _total = _records.Count;
        int _success = _records.Count(it => it.Success);
        int _fail = _total - _success;
        int _points = _records.Sum(it => it.EstimatedPoints);
        return (_total, _success, _fail, _points);
    }

    private static string Truncate(string _text, int _max_length)
    {
        if (string.IsNullOrEmpty(_text)) { return string.Empty; }
        return _text.Length <= _max_length ? _text : _text[.._max_length] + "...";
    }
}
