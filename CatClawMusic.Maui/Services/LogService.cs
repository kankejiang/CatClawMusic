using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 日志服务实现，同时输出到调试控制台和本地 debug.log 文件。
/// 跨平台实现，不依赖 Android logcat。
/// </summary>
public class LogService : ILogService
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    /// <summary>静态单例，供插件通过 Core 接口访问</summary>
    public static LogService? Instance { get; private set; }

    public LogService()
    {
        _logFilePath = Path.Combine(FileSystem.AppDataDirectory, "debug.log");
        Instance = this;
    }

    public void Info(string tag, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[I][{tag}] {message}");
        Write("I", tag, message, fileOnly: false);
    }

    public void Warn(string tag, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[W][{tag}] {message}");
        Write("W", tag, message, fileOnly: true);
    }

    public void Error(string tag, string message)
    {
        System.Diagnostics.Debug.WriteLine($"[E][{tag}] {message}");
        Write("E", tag, message, fileOnly: true);
    }

    private void Write(string level, string tag, string message, bool fileOnly)
    {
        try
        {
            if (!fileOnly) return;

            var line = $"{DateTime.Now:HH:mm:ss.fff}\t[{level}][{tag}] {message}";
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
        catch { }
    }
}
