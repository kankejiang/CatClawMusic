using System.Diagnostics;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 启动耗时打点：从首次 Mark 起累计毫秒，写入应用数据目录 startup_trace.log（每次启动截断）。
/// Release 可用 —— 每日首启卡顿的现场诊断依赖它（真机 adb pull 或设备文件管理器取文件）。
/// 所有 IO 静默容错，不影响启动。
/// </summary>
public static class StartupTrace
{
    private static readonly object Gate = new();
    private static Stopwatch? _sw;
    private static string? _path;

    public static void Mark(string message)
    {
        try
        {
            lock (Gate)
            {
                if (_sw == null)
                {
                    _sw = Stopwatch.StartNew();
                    var dir = "";
#if ANDROID
                    // Android 写外部存储 CatClawMusic/(与 LogService debug.log 同目录),adb 可直接拉取
                    try
                    {
                        var externalRoot = Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath ?? "/sdcard";
                        dir = Path.Combine(externalRoot, "CatClawMusic");
                    }
                    catch { }
#endif
                    if (string.IsNullOrEmpty(dir))
                    {
                        try { dir = Microsoft.Maui.Storage.FileSystem.AppDataDirectory; }
                        catch { }
                    }
                    if (string.IsNullOrEmpty(dir)) dir = Path.GetTempPath();
                    _path = Path.Combine(dir, "startup_trace.log");
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.WriteAllText(_path, $"==== launch {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ====\n");
                }
                File.AppendAllText(_path, $"[{_sw.ElapsedMilliseconds,6} ms] {message}\n");
            }
        }
        catch { }
    }
}
