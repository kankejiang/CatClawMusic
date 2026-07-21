using System.Diagnostics;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

#if ANDROID
/// <summary>Android FFmpeg 音频转码服务 — 使用 APK 内置 libffmpeg.so</summary>
public class FFmpegService : IDisposable
{
    private string? _ffmpegPath;
    private bool _initAttempted;
    private bool _disposed;
    private readonly SemaphoreSlim _transcodeLock = new(4, 4);
    /// <summary>转码 WAV 缓存目录（按源文件指纹命名，避免重复转码）</summary>
    private static readonly string WavCacheDir = Path.Combine(Path.GetTempPath(), "cc_ff_cache");
    /// <summary>缓存上限（200MB），超出时按最后访问时间清理最旧的文件</summary>
    private const long MaxCacheBytes = 200L * 1024 * 1024;
    /// <summary>缓存键与文件路径的内存映射，避免每次都遍历目录</summary>
    private static readonly Dictionary<string, string> _cacheMap = new();
    private static readonly object _cacheMapLock = new();

    static FFmpegService()
    {
        try { Directory.CreateDirectory(WavCacheDir); } catch { }
    }

    /// <summary>需要 FFmpeg 软解的扩展名集合</summary>
    private static readonly string[] TranscodeExtensions =
        { ".m4a", ".m4b", ".mp4", ".mov", ".wma", ".ogg", ".opus", ".ape", ".wv", ".aiff", ".aif", ".alac", ".flac" };

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
            Log.Debug("FFmpegService", $"[FFmpeg] 就绪: {destPath}");
            return true;
        }

        Log.Debug("FFmpegService", "[FFmpeg] libffmpeg.so 未找到");
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

    /// <summary>用 FFmpeg 软解为 WAV（PCM 16bit / 44.1kHz / 双声道），可选烘焙均衡器滤镜</summary>
    /// <param name="inputPath">输入文件路径</param>
    /// <param name="audioFilter">FFmpeg -af 滤镜链（均衡器等），空串表示不应用</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>生成的 WAV 临时文件路径；失败返回 null</returns>
    public async Task<string?> TranscodeToWavAsync(string inputPath, string? audioFilter = null, CancellationToken ct = default)
    {
        if (_ffmpegPath == null && !await InitializeAsync())
            return null;
        if (_ffmpegPath == null || !File.Exists(inputPath)) return null;

        // 缓存命中：基于源文件指纹（路径+长度+修改时间+滤镜）查找已转码的 WAV
        var cacheKey = BuildCacheKey(inputPath, audioFilter);
        if (TryGetCachedWav(cacheKey, out var cachedPath))
        {
            Log.Debug("FFmpegService", $"[FFmpeg] 缓存命中: {Path.GetFileName(inputPath)}");
            return cachedPath;
        }

        var outputPath = Path.Combine(WavCacheDir, $"cc_ff_{cacheKey}.wav");

        var afPart = string.IsNullOrEmpty(audioFilter) ? "" : $" -af \"{audioFilter}\"";
        var args = $"-y -i \"{inputPath}\"{afPart} -acodec pcm_s16le -ar 44100 -ac 2 \"{outputPath}\"";
        var result = await RunFFmpegAsync(args, ct);

        if (!result || !File.Exists(outputPath) || new FileInfo(outputPath).Length < 1024)
        {
            SafeDelete(outputPath);
            return null;
        }

        // 注册到缓存映射并尝试清理超限缓存
        RegisterCachedWav(cacheKey, outputPath);
        _ = Task.Run(TryTrimCacheAsync);

        return outputPath;
    }

    /// <summary>基于源文件路径+长度+修改时间+滤镜参数生成稳定哈希，作为缓存键</summary>
    private static string BuildCacheKey(string inputPath, string? audioFilter = null)
    {
        long size = 0, mtime = 0;
        try
        {
            var fi = new FileInfo(inputPath);
            if (fi.Exists)
            {
                size = fi.Length;
                mtime = fi.LastWriteTimeUtc.Ticks;
            }
        }
        catch { }
        var raw = $"{inputPath}|{size}|{mtime}|{audioFilter ?? ""}";
        // 简单稳定的字符串哈希（避免 SHA256 在 Android 上额外开销）
        ulong hash = 14695981039346656037UL;
        unchecked
        {
            foreach (var c in raw)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
        }
        return hash.ToString("x16");
    }

    /// <summary>尝试从缓存中获取 WAV 文件路径，校验文件存在性并更新访问时间</summary>
    private static bool TryGetCachedWav(string cacheKey, out string? path)
    {
        path = null;
        lock (_cacheMapLock)
        {
            if (!_cacheMap.TryGetValue(cacheKey, out var p) || p == null) return false;
            if (!File.Exists(p))
            {
                _cacheMap.Remove(cacheKey);
                return false;
            }
            path = p;
        }
        // 更新访问时间，LRU 清理依据
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); } catch { }
        return true;
    }

    /// <summary>注册已生成的 WAV 缓存</summary>
    private static void RegisterCachedWav(string cacheKey, string path)
    {
        lock (_cacheMapLock) { _cacheMap[cacheKey] = path; }
    }

    /// <summary>缓存超限时按最后访问时间清理最旧的文件（后台执行）</summary>
    private static void TryTrimCacheAsync()
    {
        try
        {
            var dir = new DirectoryInfo(WavCacheDir);
            if (!dir.Exists) return;
            var files = dir.GetFiles("*.wav");
            var total = 0L;
            foreach (var f in files) total += f.Length;
            if (total <= MaxCacheBytes) return;

            // 按访问时间升序，删除最旧的直至总量降到 80% 阈值
            var target = (long)(MaxCacheBytes * 0.8);
            foreach (var f in files.OrderBy(f => f.LastAccessTimeUtc))
            {
                if (total <= target) break;
                try { total -= f.Length; f.Delete(); } catch { }
            }

            // 同步内存映射
            lock (_cacheMapLock)
            {
                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in dir.GetFiles("*.wav")) existing.Add(f.FullName);
                var staleKeys = _cacheMap.Where(kv => !existing.Contains(kv.Value)).Select(kv => kv.Key).ToList();
                foreach (var k in staleKeys) _cacheMap.Remove(k);
            }
        }
        catch { }
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
                Log.Debug("FFmpegService", "[FFmpeg] linker64 成功");
                return true;
            }

            // 方法 1: Java.Lang.Runtime.Exec()
            if (await TryJavaExecAsync(args, cts.Token))
            {
                Log.Debug("FFmpegService", "[FFmpeg] Java exec 成功");
                return true;
            }

            // 方法 2: sh -c
            if (await TryShExecAsync(args, cts.Token))
            {
                Log.Debug("FFmpegService", "[FFmpeg] sh exec 成功");
                return true;
            }

            // 方法 3: .NET Process
            if (await TryDotNetProcessAsync(args, cts.Token))
            {
                Log.Debug("FFmpegService", "[FFmpeg] .NET Process 成功");
                return true;
            }

            Log.Debug("FFmpegService", "[FFmpeg] 所有执行方式均失败");
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
        catch (Exception ex) { Log.Debug("FFmpegService", $"[FFmpeg] linker64 异常: {ex.Message}"); return false; }
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
        catch (Exception ex) { Log.Debug("FFmpegService", $"[FFmpeg] Java exec 异常: {ex.Message}"); return false; }
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
        catch (Exception ex) { Log.Debug("FFmpegService", $"[FFmpeg] sh exec 异常: {ex.Message}"); return false; }
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
        catch (Exception ex) { Log.Debug("FFmpegService", $"[FFmpeg] .NET Process 异常: {ex.Message}"); return false; }
    }

    private static async Task<bool> WaitForProcessAsync(Java.Lang.Process process, CancellationToken ct, int timeoutMs = 120_000)
    {
        try
        {
            using var stdoutMs = new MemoryStream();
            using var stderrMs = new MemoryStream();
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _transcodeLock.Dispose();
    }
}
#endif
