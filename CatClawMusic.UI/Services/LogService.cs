using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 日志服务实现，同时输出到 Android logcat 和本地 debug.log 文件。
/// 插件通过静态属性 <see cref="Instance"/> 获取实例进行日志写入。
/// </summary>
public class LogService : ILogService
{
    private readonly string _logFilePath;
    private readonly object _lock = new();

    /// <summary>静态单例，供插件通过 Core 接口访问</summary>
    public static LogService? Instance { get; private set; }

    public LogService()
    {
        var externalDir = global::Android.App.Application.Context?.GetExternalFilesDir(null)?.AbsolutePath
            ?? "/data/local/tmp";
        _logFilePath = System.IO.Path.Combine(externalDir, "debug.log");
        Instance = this;
    }

    public void Info(string tag, string message) => Write("I", tag, message);
    public void Warn(string tag, string message) => Write("W", tag, message);
    public void Error(string tag, string message) => Write("E", tag, message);

    private void Write(string level, string tag, string message)
    {
        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff}\t[{level}][{tag}] {message}";
            global::Android.Util.Log.WriteLine(level switch { "I" => global::Android.Util.LogPriority.Info, "W" => global::Android.Util.LogPriority.Warn, _ => global::Android.Util.LogPriority.Error }, tag, message);

            lock (_lock)
            {
                var dir = System.IO.Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
        catch { }
    }
}
