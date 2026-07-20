using System;
using System.IO;
using System.Text;
using Microsoft.Maui.Storage;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 本地崩溃记录器：把未处理异常（托管 + Android 原生）与崩溃前的运行日志轨迹
/// 落盘到两个位置：
///   1) 应用私有目录（FileSystem.AppDataDirectory/crash_log.txt）
///   2) 外部 files 目录（Android/data/com.catclaw.music/files/catclaw_crash.log，可用文件管理器直接访问）
/// 没有开发机/adb 时，用户只需复现一次崩溃，下次启动 App 即可看到日志并复制。
/// </summary>
public static class CrashReporter
{
    private const string InternalName = "crash_log.txt";
    private const string ExternalName = "catclaw_crash.log";

    /// <summary>记录一次托管层未处理异常。</summary>
    public static void RecordManaged(string source, Exception? ex, bool terminating)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"===== CRASH @ {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} (managed) =====");
        sb.AppendLine($"Source: {source}");
        sb.AppendLine($"Terminating: {terminating}");
        if (ex != null)
        {
            sb.AppendLine($"Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine("StackTrace:");
            sb.AppendLine(ex.StackTrace);
            if (ex.InnerException != null)
            {
                sb.AppendLine("InnerException:");
                sb.AppendLine(ex.InnerException.ToString());
            }
        }
        sb.AppendLine("============================================================");
        sb.AppendLine();
        Append(sb.ToString());
    }

#if ANDROID
    /// <summary>记录一次 Android 原生（Java/Kotlin）未处理异常。入参兼容 Java.Lang.Throwable 与 System.Exception。</summary>
    public static void RecordJava(string source, object? t)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"===== CRASH @ {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} (java/android) =====");
        sb.AppendLine($"Source: {source}");
        if (t is Java.Lang.Throwable jt)
        {
            sb.AppendLine($"Type: {jt.GetType().FullName}");
            sb.AppendLine($"Message: {jt.Message}");
            sb.AppendLine("StackTrace:");
            sb.AppendLine(jt.ToString());
        }
        else if (t is Exception ex)
        {
            sb.AppendLine($"Type: {ex.GetType().FullName}");
            sb.AppendLine($"Message: {ex.Message}");
            sb.AppendLine("StackTrace:");
            sb.AppendLine(ex.StackTrace);
            if (ex.InnerException != null)
            {
                sb.AppendLine("InnerException:");
                sb.AppendLine(ex.InnerException.ToString());
            }
        }
        else if (t != null)
        {
            sb.AppendLine($"Type: {t.GetType().FullName}");
            sb.AppendLine($"Detail: {t}");
        }
        sb.AppendLine("============================================================");
        sb.AppendLine();
        Append(sb.ToString());
    }
#endif

    /// <summary>把崩溃前的运行日志轨迹追加到崩溃日志中。</summary>
    public static void AppendTrace(string trace)
    {
        if (string.IsNullOrWhiteSpace(trace)) return;
        var content = "----- 崩溃前日志轨迹 (最近) -----\n"
                      + trace
                      + "----- 日志轨迹结束 -----\n\n";
        Append(content);
    }

    private static void Append(string content)
    {
        // 1) 应用私有目录
        try
        {
            var internalPath = Path.Combine(FileSystem.AppDataDirectory, InternalName);
            File.AppendAllText(internalPath, content);
        }
        catch { }

#if ANDROID
        // 2) 外部 files 目录（用户可直接用文件管理器访问）
        try
        {
            var ctx = Android.App.Application.Context;
            var externalDir = ctx?.GetExternalFilesDir(null);
            if (externalDir != null)
            {
                var extPath = Path.Combine(externalDir.AbsolutePath, ExternalName);
                File.AppendAllText(extPath, content);
            }
        }
        catch { }
#endif
    }

    /// <summary>读取私有目录中的崩溃日志（若有多条会包含全部历史）。</summary>
    public static string? LastCrash
    {
        get
        {
            try
            {
                var internalPath = Path.Combine(FileSystem.AppDataDirectory, InternalName);
                if (File.Exists(internalPath))
                    return File.ReadAllText(internalPath);
            }
            catch { }
            return null;
        }
    }

    /// <summary>清空崩溃日志（已读取/复制后调用）。</summary>
    public static void Clear()
    {
        try
        {
            var internalPath = Path.Combine(FileSystem.AppDataDirectory, InternalName);
            if (File.Exists(internalPath)) File.Delete(internalPath);
        }
        catch { }
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            var externalDir = ctx?.GetExternalFilesDir(null);
            if (externalDir != null)
            {
                var extPath = Path.Combine(externalDir.AbsolutePath, ExternalName);
                if (File.Exists(extPath)) File.Delete(extPath);
            }
        }
        catch { }
#endif
    }

    // === 阶段面包屑（用于定位 native 崩溃，托管异常钩子抓不到的情况） ===

    private const string StageName = "last_stage.txt";
    private const string StageExternalName = "catclaw_stage.log";

    /// <summary>标记当前执行阶段（同步写文件，覆盖式）。native 崩溃杀进程前不会触发托管异常钩子，
    /// 但本文件会保留最后一步，从而定位死在哪个阶段。</summary>
    public static void MarkStage(string stage)
    {
        var content = $"[{DateTime.Now:HH:mm:ss.fff}] {stage}\n";
        try { File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, StageName), content); } catch { }
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            var d = ctx?.GetExternalFilesDir(null);
            if (d != null) File.WriteAllText(Path.Combine(d.AbsolutePath, StageExternalName), content);
        }
        catch { }
#endif
    }

    /// <summary>页面/流程成功完成后清除阶段标记（表示未在此处崩溃）。</summary>
    public static void ClearStage()
    {
        try { var p = Path.Combine(FileSystem.AppDataDirectory, StageName); if (File.Exists(p)) File.Delete(p); } catch { }
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            var d = ctx?.GetExternalFilesDir(null);
            if (d != null) { var p = Path.Combine(d.AbsolutePath, StageExternalName); if (File.Exists(p)) File.Delete(p); }
        }
        catch { }
#endif
    }

    /// <summary>读取残留的阶段标记（进程若在某个阶段被 native 崩溃杀死，会残留）。</summary>
    public static string? LastStage
    {
        get
        {
            try
            {
                var p = Path.Combine(FileSystem.AppDataDirectory, StageName);
                if (File.Exists(p)) return File.ReadAllText(p);
            }
            catch { }
            return null;
        }
    }
}
