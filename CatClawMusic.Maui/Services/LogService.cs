using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 日志服务实现，同时输出到调试控制台和本地 debug.log 文件。
/// 跨平台实现，不依赖 Android logcat。
/// </summary>
/// <remarks>
/// 性能与隐私设计：
/// - 通过 <see cref="IsEnabled"/> 开关控制是否写文件，默认关闭以提升性能；
///   关闭时仅保留调试控制台输出（Release 下几乎零开销）。
/// - 使用 <see cref="ConcurrentQueue{T}"/> 缓冲 + 后台 Timer 批量刷写，
///   避免每次日志调用都执行同步文件 I/O，并将写入移出主线程。
/// - 写入前对消息进行 <see cref="Sanitize"/> 脱敏，掩码 URL 凭证、API Key、Bearer Token 等。
/// </remarks>
public partial class LogService : ILogService
{
    /// <summary>持久化开关的 Preferences 键名</summary>
    private const string EnabledKey = "diagnostic_log_enabled";

    private readonly string _logFilePath;
    private readonly string _logDir;

    /// <summary>日志行缓冲队列，由各写入方法入队、后台 Timer 出队刷盘</summary>
    private readonly ConcurrentQueue<string> _buffer = new();
    /// <summary>后台刷盘定时器，批量将缓冲区落盘</summary>
    private readonly Timer _flushTimer;
    /// <summary>刷盘锁，防止 Timer 重入</summary>
    private readonly object _flushLock = new();

    /// <summary>缓冲区触发立即刷盘的条数阈值</summary>
    private const int FlushThreshold = 50;
    /// <summary>后台刷盘间隔（毫秒）</summary>
    private const int FlushIntervalMs = 1000;
    /// <summary>单条日志最大长度（超出截断，防止异常大对象撑爆文件）</summary>
    private const int MaxLineLength = 4000;

    /// <summary>静态单例，供插件通过 Core 接口访问</summary>
    public static LogService? Instance { get; private set; }

    /// <summary>诊断日志文件完整路径（外部 CatClawMusic/debug.log，文件管理器可访问）。供 LogPage/导出复用。</summary>
    public static string LogFilePath { get; private set; } = "";

    /// <summary>解析诊断日志目录：优先外部存储 CatClawMusic 目录（文件管理器可访问），失败回退应用私有目录。</summary>
    private static string ResolveLogDirectory()
    {
        try
        {
#if ANDROID
            var externalRoot = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath ?? "/sdcard";
            var dir = Path.Combine(externalRoot, "CatClawMusic");
            Directory.CreateDirectory(dir);
            return dir;
#endif
        }
        catch { }
        return FileSystem.AppDataDirectory;
    }

    /// <summary>写入诊断开关标记文件（外部 CatClawMusic 目录）。</summary>
    private static void WriteFlagFile(bool on)
    {
        try
        {
#if ANDROID
            var externalRoot = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath ?? "/sdcard";
            var dir = Path.Combine(externalRoot, "CatClawMusic");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "diagnostic_enabled.txt"), on ? "1" : "0");
#endif
        }
        catch { }
    }

    /// <summary>诊断日志是否开启。变更后立即持久化到 Preferences。</summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            try { Preferences.Set(EnabledKey, value); } catch { }
            WriteFlagFile(value);
            if (!value) Flush();
        }
    }
    private bool _enabled;

    /// <summary>构造函数，初始化日志文件路径、开关状态并启动后台刷盘定时器。</summary>
    public LogService()
    {
        _logDir = ResolveLogDirectory();
        _logFilePath = Path.Combine(_logDir, "debug.log");
        LogFilePath = _logFilePath;
        try { Directory.CreateDirectory(_logDir); } catch { }

        // 诊断日志默认关闭：由设置页开关控制，默认不写文件以降低开销与隐私顾虑。
        // 若用户此前在设置页开启过（Preferences 持久化），则恢复为开启状态。
        _enabled = Preferences.Get(EnabledKey, false);
        WriteFlagFile(_enabled);

        Instance = this;
        Core.Interfaces.Log.SetProvider(this);

        _flushTimer = new Timer(_ => FlushInternal(), null, FlushIntervalMs, FlushIntervalMs);
    }

    /// <summary>记录 Debug 级别日志，同时输出到调试控制台和日志文件</summary>
    /// <param name="tag">日志标签，用于区分模块</param>
    /// <param name="message">日志内容</param>
    public void Debug(string tag, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[D][{tag}] {message}");
        Enqueue("D", tag, message);
    }

    /// <summary>记录 Info 级别日志，同时输出到调试控制台和日志文件</summary>
    /// <param name="tag">日志标签，用于区分模块</param>
    /// <param name="message">日志内容</param>
    public void Info(string tag, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[I][{tag}] {message}");
        Enqueue("I", tag, message);
    }

    /// <summary>记录 Warn 级别日志，同时输出到调试控制台和日志文件</summary>
    /// <param name="tag">日志标签，用于区分模块</param>
    /// <param name="message">日志内容</param>
    public void Warn(string tag, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[W][{tag}] {message}");
        Enqueue("W", tag, message);
    }

    /// <summary>记录 Error 级别日志，同时输出到调试控制台和日志文件</summary>
    /// <param name="tag">日志标签，用于区分模块</param>
    /// <param name="message">日志内容</param>
    public void Error(string tag, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[E][{tag}] {message}");
        Enqueue("E", tag, message);
    }

    /// <summary>立即将缓冲区中的日志刷写到磁盘</summary>
    public void Flush() => FlushInternal();

    /// <summary>入队一条日志（开关关闭时直接返回，不进行任何文件 I/O）</summary>
    private void Enqueue(string level, string tag, string message)
    {
        if (!_enabled) return;

        var sanitized = Sanitize(message);
        if (sanitized.Length > MaxLineLength)
            sanitized = sanitized[..MaxLineLength] + "..(截断)";

        var line = $"{DateTime.Now:HH:mm:ss.fff}\t[{level}][{tag}] {sanitized}";
        _buffer.Enqueue(line);

        if (_buffer.Count >= FlushThreshold)
            _ = Task.Run(FlushInternal);
    }

    /// <summary>将缓冲区所有日志一次性批量写入文件（后台线程执行）</summary>
    private void FlushInternal()
    {
        if (_buffer.IsEmpty) return;
        if (!Monitor.TryEnter(_flushLock)) return;
        try
        {
            var lines = new List<string>();
            while (_buffer.TryDequeue(out var line))
                lines.Add(line);
            if (lines.Count == 0) return;

            try
            {
                File.AppendAllLines(_logFilePath, lines);
            }
            catch { }
        }
        finally
        {
            Monitor.Exit(_flushLock);
        }
    }

    /// <summary>
    /// 对日志消息进行数据脱敏，掩码 URL 中的凭证、API Key、Bearer Token 等。
    /// </summary>
    /// <param name="message">原始日志消息</param>
    /// <returns>脱敏后的消息</returns>
    private static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message)) return message ?? "";

        message = CredentialRegex().Replace(message, "://***@");
        message = BearerRegex().Replace(message, "$1=***");
        message = ApiKeyRegex().Replace(message, "$1\"***\"");
        message = SkKeyRegex().Replace(message, "sk-***");

        return message;
    }

    [GeneratedRegex(@"://[^/@:\s]+:[^/@:\s]+@", RegexOptions.Compiled)]
    private static partial Regex CredentialRegex();

    [GeneratedRegex(@"(?i)(authorization|bearer)\s*[=:]\s*\S+", RegexOptions.Compiled)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(?i)(""?(?:api[_-]?key|apikey|key|token|secret|access[_-]?token)""?\s*[=:]\s*)""?[^""&\s,}]+""?", RegexOptions.Compiled)]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled)]
    private static partial Regex SkKeyRegex();
}
