using System.Text;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.Infrastructure;

namespace CatClawMusic.WinUI.Services;

/// <summary>
/// 文件日志（替代 MAUI 的 LogService）：<c>%LOCALAPPDATA%\CatClawMusic\logs\winui.log</c>。
/// 构造时把自己注册为 Core 的静态 <see cref="Log"/> 提供者，Core/Data 层无需 DI 即可写日志。
/// </summary>
public sealed class FileLogService : ILogService
{
    private const long MaxBytes = 8 * 1024 * 1024;

    private readonly object _gate = new();
    private readonly string _logPath;
    private StreamWriter? _writer;

    public FileLogService(JsonSettingsStore settings)
    {
        _logPath = Path.Combine(AppPaths.LogDirectory, "winui.log");
        IsEnabled = settings.Get("diagnostic_log_enabled", true);
        Log.SetProvider(this);
    }

    public bool IsEnabled { get; set; }

    public void Debug(string tag, string message) => Write("DBG", tag, message);
    public void Info(string tag, string message) => Write("INF", tag, message);
    public void Warn(string tag, string message) => Write("WRN", tag, message);
    public void Error(string tag, string message) => Write("ERR", tag, message);

    public void Flush()
    {
        lock (_gate)
        {
            _writer?.Flush();
        }
    }

    private void Write(string level, string tag, string message)
    {
        if (!IsEnabled)
            return;

        lock (_gate)
        {
            try
            {
                var writer = EnsureWriter();
                writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{tag}] {message}");
            }
            catch (IOException)
            {
                // 日志失败不能影响主流程：静默降级为无日志。
            }
            catch (UnauthorizedAccessException)
            {
                // 同上（目录被策略限制时）。
            }
        }
    }

    private StreamWriter EnsureWriter()
    {
        if (_writer is not null)
            return _writer;

        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
        RotateIfNeeded();

        _writer = new StreamWriter(
            new FileStream(_logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };

        return _writer;
    }

    private void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_logPath);
            if (!info.Exists || info.Length < MaxBytes)
                return;

            var backup = _logPath + ".1";
            File.Delete(backup);
            File.Move(_logPath, backup);
        }
        catch (IOException)
        {
            // 轮转失败不阻塞继续写当前文件。
        }
    }
}
