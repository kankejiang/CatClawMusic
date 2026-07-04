using System.Diagnostics;

namespace CatClawMusic.Maui.Services;

#if ANDROID
/// <summary>Android FFmpeg 音频转码服务 — 使用 APK 内置 libffmpeg.so</summary>
public class FFmpegService
{
    private string? _ffmpegPath;
    private bool _initAttempted;
    private readonly SemaphoreSlim _transcodeLock = new(2, 2);

    /// <summary>需要 FFmpeg 软解的扩展名集合</summary>
    private static readonly string[] TranscodeExtensions =
        { ".m4a", ".m4b", ".mp4", ".mov", ".wma", ".ogg", ".opus", ".ape", ".wv", ".aiff", ".aif", ".alac" };

    /// <summary>获取 FFmpeg 是否可用（已成功定位 libffmpeg.so）</summary>
    public bool IsAvailable => _ffmpegPath != null;

    /// <summary>异步初始化 FFmpeg，定位并校验 libffmpeg.so 的可用性</summary>
    /// <returns>初始化成功返回 true，否则返回 false</returns>
    public async Task<bool> InitializeAsync()
    {
        if (_initAttempted) return _ffmpegPath != null;
        _initAttempted = true;

        var destPath = GetNativeLibPath();
        if (!string.IsNullOrEmpty(destPath) && File.Exists(destPath))
        {
            _ffmpegPath = destPath;
            Debug.WriteLine($"[FFmpeg] 就绪: {destPath}");
            return true;
        }

        Debug.WriteLine("[FFmpeg] libffmpeg.so 未找到");
        return false;
    }

    /// <summary>判断指定文件是否需要通过 FFmpeg 转码（按扩展名匹配）</summary>
    /// <param name="filePath">待检测的文件路径</param>
    /// <returns>需要转码返回 true，否则返回 false</returns>
    public bool NeedsTranscoding(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return !string.IsNullOrEmpty(ext) && TranscodeExtensions.Contains(ext);
    }

    /// <summary>用 FFmpeg 软解为 WAV（PCM 16bit / 44.1kHz / 双声道）</summary>
    /// <param name="inputPath">输入文件路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>生成的 WAV 临时文件路径；失败返回 null</returns>
    public async Task<string?> TranscodeToWavAsync(string inputPath, CancellationToken ct = default)
    {
        if (_ffmpegPath == null && !await InitializeAsync())
            return null;
        if (_ffmpegPath == null || !File.Exists(inputPath)) return null;

        var outputPath = Path.Combine(Path.GetTempPath(),
            $"cc_ff_{Path.GetFileNameWithoutExtension(inputPath)}_{Guid.NewGuid():N}.wav");

        var args = $"-y -i \"{inputPath}\" -acodec pcm_s16le -ar 44100 -ac 2 \"{outputPath}\"";
        var result = await RunFFmpegAsync(args, ct);

        if (!result || !File.Exists(outputPath) || new FileInfo(outputPath).Length < 1024)
        {
            SafeDelete(outputPath);
            return null;
        }
        return outputPath;
    }

    // ═══════════════════════════════════════
    // FFmpeg 执行 — 三级回退
    // ═══════════════════════════════════════

    private async Task<bool> RunFFmpegAsync(string args, CancellationToken ct)
    {
        if (_ffmpegPath == null) return false;
        await _transcodeLock.WaitAsync(ct);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(2));

            // 方法 0: /system/bin/linker64 直接加载
            if (await TryLinker64Async(args, cts.Token))
            {
                Debug.WriteLine("[FFmpeg] linker64 成功");
                return true;
            }

            // 方法 1: Java.Lang.Runtime.Exec()
            if (await TryJavaExecAsync(args, cts.Token))
            {
                Debug.WriteLine("[FFmpeg] Java exec 成功");
                return true;
            }

            // 方法 2: sh -c
            if (await TryShExecAsync(args, cts.Token))
            {
                Debug.WriteLine("[FFmpeg] sh exec 成功");
                return true;
            }

            // 方法 3: .NET Process
            if (await TryDotNetProcessAsync(args, cts.Token))
            {
                Debug.WriteLine("[FFmpeg] .NET Process 成功");
                return true;
            }

            Debug.WriteLine("[FFmpeg] 所有执行方式均失败");
            return false;
        }
        finally { _transcodeLock.Release(); }
    }

    private async Task<bool> TryLinker64Async(string args, CancellationToken ct)
    {
        try
        {
            var ffArgs = ParseArguments(args);
            var cmdArray = new string[2 + ffArgs.Length];
            cmdArray[0] = "/system/bin/linker64";
            cmdArray[1] = _ffmpegPath!;
            for (int i = 0; i < ffArgs.Length; i++)
                cmdArray[i + 2] = ffArgs[i];

            var process = Java.Lang.Runtime.GetRuntime()!.Exec(
                cmdArray, Array.Empty<string>(),
                new Java.IO.File(GetSafeWorkDir()));
            return await WaitForProcessAsync(process, ct);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { Debug.WriteLine($"[FFmpeg] linker64 异常: {ex.Message}"); return false; }
    }

    private async Task<bool> TryJavaExecAsync(string args, CancellationToken ct)
    {
        try
        {
            var ffArgs = ParseArguments(args);
            var cmdArray = new string[ffArgs.Length + 1];
            cmdArray[0] = _ffmpegPath!;
            for (int i = 0; i < ffArgs.Length; i++)
                cmdArray[i + 1] = ffArgs[i];

            var process = Java.Lang.Runtime.GetRuntime()!.Exec(
                cmdArray, Array.Empty<string>(),
                new Java.IO.File(GetSafeWorkDir()));
            return await WaitForProcessAsync(process, ct);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { Debug.WriteLine($"[FFmpeg] Java exec 异常: {ex.Message}"); return false; }
    }

    private async Task<bool> TryShExecAsync(string args, CancellationToken ct)
    {
        try
        {
            var shellCmd = $"\"{_ffmpegPath}\" {args}";
            var cmdArray = new[] { "/system/bin/sh", "-c", shellCmd };

            var process = Java.Lang.Runtime.GetRuntime()!.Exec(
                cmdArray, Array.Empty<string>(),
                new Java.IO.File(GetSafeWorkDir()));
            return await WaitForProcessAsync(process, ct);
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) { Debug.WriteLine($"[FFmpeg] sh exec 异常: {ex.Message}"); return false; }
    }

    private async Task<bool> TryDotNetProcessAsync(string args, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                WorkingDirectory = GetSafeWorkDir(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            try { await p.WaitForExitAsync(ct); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return false; }
            return p.ExitCode == 0;
        }
        catch (Exception ex) { Debug.WriteLine($"[FFmpeg] .NET Process 异常: {ex.Message}"); return false; }
    }

    private static async Task<bool> WaitForProcessAsync(Java.Lang.Process process, CancellationToken ct, int timeoutMs = 120_000)
    {
        try
        {
            var stdoutMs = new MemoryStream();
            var stderrMs = new MemoryStream();
            using (var stdStream = process.InputStream!)
            using (var errStream = process.ErrorStream!)
            {
                var stdoutTask = stdStream.CopyToAsync(stdoutMs);
                var stderrTask = errStream.CopyToAsync(stderrMs);
                var timeoutTask = Task.Delay(timeoutMs, ct);
                var done = await Task.WhenAny(
                    Task.WhenAll(stdoutTask, stderrTask), timeoutTask);
                if (done == timeoutTask)
                {
                    try { process.Destroy(); } catch { }
                    return false;
                }
            }
            var exitCode = process.WaitFor();
            try { process.Destroy(); } catch { }
            return exitCode == 0;
        }
        catch (OperationCanceledException)
        {
            try { process.Destroy(); } catch { }
            return false;
        }
        catch { return false; }
    }

    // ═══════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════

    private static string GetNativeLibPath()
    {
        var nativeLibDir = global::Android.App.Application.Context.ApplicationInfo?.NativeLibraryDir;
        if (!string.IsNullOrEmpty(nativeLibDir))
            return Path.Combine(nativeLibDir, "libffmpeg.so");
        return "";
    }

    private static string GetSafeWorkDir()
    {
        var ctx = global::Android.App.Application.Context;
        var dir = ctx.FilesDir?.AbsolutePath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string[] ParseArguments(string argsString)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < argsString.Length; i++)
        {
            char c = argsString[i];
            if (c == '"') { inQuote = !inQuote; }
            else if (c == ' ' && !inQuote)
            {
                if (sb.Length > 0) { result.Add(sb.ToString()); sb.Clear(); }
            }
            else { sb.Append(c); }
        }
        if (sb.Length > 0) result.Add(sb.ToString());
        return result.ToArray();
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
#endif
