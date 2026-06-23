using System.Diagnostics;

namespace CatClawMusic.UI.Services;

/// <summary>
/// Android 音频转码服务 — 优先 MediaCodec，回退 APK 内置 FFmpeg
/// </summary>
public class FFmpegService
{
    private string? _ffmpegPath;
    private bool _initAttempted;
    private readonly SemaphoreSlim _transcodeLock = new(2, 2);
    private static readonly string[] TranscodeExtensions =
        { ".m4a", ".m4b", ".mp4", ".mov", ".wma", ".ogg", ".opus", ".ape", ".wv", ".aiff", ".aif", ".alac" };

    public bool IsAvailable => _ffmpegPath != null;

    private static string GetFFmpegDestPath()
    {
        // 仅使用 APK native lib 目录（Android 解压 .so 到此，必定可执行）
        var nativeLibDir = global::Android.App.Application.Context.ApplicationInfo?.NativeLibraryDir;
        if (!string.IsNullOrEmpty(nativeLibDir))
            return System.IO.Path.Combine(nativeLibDir, "libffmpeg.so");
        return "";
    }


    public async Task<bool> InitializeAsync()
    {
        if (_initAttempted) return _ffmpegPath != null;
        _initAttempted = true;

        var destPath = GetFFmpegDestPath();
        if (!string.IsNullOrEmpty(destPath) && System.IO.File.Exists(destPath))
        {
            _ffmpegPath = destPath;
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] 就绪: {destPath}");
            return true;
        }

        System.Diagnostics.Debug.WriteLine("[FFmpeg] libffmpeg.so 未找到，安装可能不完整");
        return false;
    }

    /// <summary>获取一个应用可写的安全工作目录，避免默认 '/' 导致 Permission denied</summary>
    private static string GetSafeWorkingDirectory()
    {
        var ctx = global::Android.App.Application.Context;
        var dir = ctx.FilesDir?.AbsolutePath
            ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>从 APK Assets 提取与当前 CPU ABI 匹配的内置 FFmpeg 二进制</summary>
    private static async Task<bool> ExtractBundledBinaryAsync(string destPath)
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            var assets = ctx.Assets;
            if (assets == null) return false;

            var preferredAbi = GetPreferredAbi();
            if (string.IsNullOrEmpty(preferredAbi)) return false;

            var assetName = $"ffmpeg/{preferredAbi}/ffmpeg";
            try
            {
                using var stream = assets.Open(assetName);
                using var output = System.IO.File.Create(destPath);
                await stream.CopyToAsync(output);
                await output.FlushAsync();
            }
            catch (Java.IO.FileNotFoundException)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] 未找到内置二进制: {assetName}");
                return false;
            }

            await EnsureExecutableAsync(destPath);

            // 仅验证文件大小（Java exec 在部分 Android 14 设备上不稳定）
            var fileInfo = new System.IO.FileInfo(destPath);
            if (fileInfo.Exists && fileInfo.Length > 1_000_000)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] 内置二进制就绪: {preferredAbi} ({fileInfo.Length/1024/1024}MB)");
                return true;
            }

            System.Diagnostics.Debug.WriteLine($"[FFmpeg] 内置二进制大小异常: {fileInfo.Length} bytes");
            SafeDelete(destPath);
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] 提取内置二进制失败: {ex.Message}");
            SafeDelete(destPath);
            return false;
        }
    }

    /// <summary>验证 FFmpeg 二进制可执行（运行 -version）</summary>
    private static async Task<bool> ValidateBinaryAsync(string binaryPath)
    {
        try
        {
            // 优先用 Java.Lang.Runtime.Exec() —— Android 标准进程创建机制
            var result = await RunProcessViaJavaAsync(binaryPath, new[] { "-version" });
            if (result.exitCode == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] 二进制验证通过 (Java exec): {binaryPath}");
                return true;
            }
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] Java exec 验证失败 (exit={result.exitCode})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] Java exec 验证异常: {ex.Message}");
        }

        try
        {
            // 回退：通过 sh -c 间接执行
            var result = await RunProcessViaShAsync(binaryPath, "-version");
            if (result.exitCode == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] 二进制验证通过 (sh): {binaryPath}");
                return true;
            }
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] sh 验证失败 (exit={result.exitCode})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] sh 验证异常: {ex.Message}");
        }

        try
        {
            // 最后回退：.NET Process（在部分设备上可能因 posix_spawn 失败）
            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = "-version",
                WorkingDirectory = GetSafeWorkingDirectory(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                if (proc.ExitCode == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[FFmpeg] 二进制验证通过 (.NET Process): {binaryPath}");
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] .NET Process 验证异常: {ex.Message}");
        }

        return false;
    }

    /// <summary>获取当前设备首选 ABI，映射到 Assets 目录名</summary>
    private static string? GetPreferredAbi()
    {
        try
        {
            var abis = global::Android.OS.Build.SupportedAbis;
            if (abis == null || abis.Count == 0)
            {
                var fallback = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(global::Android.OS.Build.CpuAbi))
                    fallback.Add(global::Android.OS.Build.CpuAbi);
                if (!string.IsNullOrEmpty(global::Android.OS.Build.CpuAbi2))
                    fallback.Add(global::Android.OS.Build.CpuAbi2);
                abis = fallback;
            }

            // 按优先级匹配我们内置的 ABI 目录
            var supported = new[] { "arm64-v8a", "armeabi-v7a", "x86_64", "x86" };
            foreach (var candidate in supported)
            {
                if (abis.Any(a =>
                    !string.IsNullOrEmpty(a) &&
                    a.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                    return candidate;
            }
        }
        catch { }
        return null;
    }

    public bool NeedsTranscoding(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath)?.ToLowerInvariant();
        return !string.IsNullOrEmpty(ext) && TranscodeExtensions.Contains(ext);
    }

    /// <summary>直接用 FFmpeg 软解为 WAV</summary>
    public async Task<string?> TranscodeToWavAsync(string inputPath, CancellationToken ct = default)
    {
        if (_ffmpegPath == null && !await InitializeAsync())
        {
            System.Diagnostics.Debug.WriteLine("[FFmpeg] 内置 FFmpeg 不可用");
            return null;
        }
        if (_ffmpegPath == null || !System.IO.File.Exists(inputPath)) return null;

        var outputPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"cc_ff_{System.IO.Path.GetFileNameWithoutExtension(inputPath)}_{Guid.NewGuid():N}.wav");

        var args = $"-y -i \"{inputPath}\" -acodec pcm_s16le -ar 44100 -ac 2 \"{outputPath}\"";
        var result = await RunFFmpegAsync(args, ct);

        if (!result || !System.IO.File.Exists(outputPath) || new System.IO.FileInfo(outputPath).Length < 1024)
        {
            SafeDelete(outputPath);
            return null;
        }

        return outputPath;
    }

    // ── FFmpeg 进程执行 ──

    private static async Task EnsureExecutableAsync(string path)
    {
        try
        {
            // .NET 7+ 在 Unix 平台可直接设置可执行权限
            System.IO.File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        catch (Exception ex1)
        {
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] SetUnixFileMode 失败: {ex1.Message}，回退 chmod");
            try
            {
                using var chmod = Process.Start(new ProcessStartInfo
                {
                    FileName = "chmod", Arguments = $"755 \"{path}\"",
                    WorkingDirectory = GetSafeWorkingDirectory(),
                    CreateNoWindow = true, UseShellExecute = false
                });
                if (chmod != null) await chmod.WaitForExitAsync();
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] chmod 失败: {ex2.Message}");
            }
        }
    }

    // ── 进程执行：三级回退策略 ──

    /// <summary>
    /// 方法 1：通过 Java.Lang.Runtime.Exec() 创建进程。
    /// 这是 Android 标准的进程创建机制（fork+exec），在 .NET Android 的
    /// System.Diagnostics.Process 因 posix_spawn 报 "Permission denied" 时，
    /// 此方法通常能正常工作。
    /// </summary>
    private static async Task<(int exitCode, string stdout, string stderr)> RunProcessViaJavaAsync(
        string binaryPath, string[] args)
    {
        var cmdArray = new string[args.Length + 1];
        cmdArray[0] = binaryPath;
        for (int i = 0; i < args.Length; i++)
            cmdArray[i + 1] = args[i];

        var workDir = GetSafeWorkingDirectory();
        var env = Array.Empty<string>();
        var process = Java.Lang.Runtime.GetRuntime()!.Exec(cmdArray, env, new Java.IO.File(workDir));

        // 并行读取 stdout 和 stderr，防止管道满导致死锁
        var stdoutMs = new MemoryStream();
        var stderrMs = new MemoryStream();
        using (var stdStream = process!.InputStream!)
        using (var errStream = process.ErrorStream!)
        {
            var stdoutTask = stdStream.CopyToAsync(stdoutMs);
            var stderrTask = errStream.CopyToAsync(stderrMs);
            await Task.WhenAll(stdoutTask, stderrTask);
        }

        var exitCode = process.WaitFor();
        var stdout = System.Text.Encoding.UTF8.GetString(stdoutMs.ToArray());
        var stderr = System.Text.Encoding.UTF8.GetString(stderrMs.ToArray());

        // 释放 Java 对象
        try { process.Destroy(); } catch { }

        return (exitCode, stdout, stderr);
    }

    /// <summary>
    /// 方法 2：通过 /system/bin/sh -c 间接执行。
    /// sh 本身是可执行的，通过它调用我们的二进制可以绕过部分设备的直接执行限制。
    /// </summary>
    private static async Task<(int exitCode, string stdout, string stderr)> RunProcessViaShAsync(
        string binaryPath, string args)
    {
        var shellCmd = $"\"{binaryPath}\" {args}";
        var cmdArray = new[] { "/system/bin/sh", "-c", shellCmd };

        var workDir = GetSafeWorkingDirectory();
        var env = Array.Empty<string>();
        var process = Java.Lang.Runtime.GetRuntime()!.Exec(cmdArray, env, new Java.IO.File(workDir));

        var stdoutMs = new MemoryStream();
        var stderrMs = new MemoryStream();
        using (var stdStream = process!.InputStream!)
        using (var errStream = process.ErrorStream!)
        {
            var stdoutTask = stdStream.CopyToAsync(stdoutMs);
            var stderrTask = errStream.CopyToAsync(stderrMs);
            await Task.WhenAll(stdoutTask, stderrTask);
        }

        var exitCode = process.WaitFor();
        var stdout = System.Text.Encoding.UTF8.GetString(stdoutMs.ToArray());
        var stderr = System.Text.Encoding.UTF8.GetString(stderrMs.ToArray());

        try { process.Destroy(); } catch { }

        return (exitCode, stdout, stderr);
    }

    /// <summary>
    /// 运行 FFmpeg 进行音频转码。三级回退：
    /// 1. Java.Lang.Runtime.Exec() (直接执行)
    /// 2. /system/bin/sh -c (间接执行)
    /// 3. System.Diagnostics.Process (.NET 标准，最后回退)
    /// </summary>
    private async Task<bool> RunFFmpegAsync(string args, CancellationToken ct)
    {
        if (_ffmpegPath == null) return false;
        await _transcodeLock.WaitAsync(ct);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(2));

            // ── 方法 0: /system/bin/linker64 直接加载（绕过 noexec 挂载）──
            try
            {
                System.Diagnostics.Debug.WriteLine("[FFmpeg] 执行方式: linker64");
                var ffArgs = ParseArguments(args);
                var cmdArray = new string[2 + ffArgs.Length];
                cmdArray[0] = "/system/bin/linker64";
                cmdArray[1] = _ffmpegPath;
                for (int i = 0; i < ffArgs.Length; i++)
                    cmdArray[i + 2] = ffArgs[i];

                var process = Java.Lang.Runtime.GetRuntime()!.Exec(cmdArray, Array.Empty<string>(),
                    new Java.IO.File(GetSafeWorkingDirectory()));
                var stdoutMs = new MemoryStream();
                var stderrMs = new MemoryStream();
                using (var stdStream = process!.InputStream!)
                using (var errStream = process.ErrorStream!)
                {
                    var stdoutTask = stdStream.CopyToAsync(stdoutMs);
                    var stderrTask = errStream.CopyToAsync(stderrMs);
                    var timeoutTask = Task.Delay(TimeSpan.FromMinutes(2), cts.Token);
                    var done = await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), timeoutTask);
                    if (done == timeoutTask) { try { process.Destroy(); } catch { } return false; }
                }
                var exitCode = process.WaitFor();
                try { process.Destroy(); } catch { }
                if (exitCode == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[FFmpeg] linker64 成功");
                    return true;
                }
                var stderr = System.Text.Encoding.UTF8.GetString(stderrMs.ToArray());
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] linker64 退出码 {exitCode}: {stderr.Trim()}");
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] linker64 异常: {ex.Message}");
            }

            // ── 方法 1: Java.Lang.Runtime.Exec() ──
            try
            {
                System.Diagnostics.Debug.WriteLine("[FFmpeg] 执行方式: Java.Lang.Runtime.Exec()");
                // 解析参数（简单按空格分割，引号内的内容视为一个参数）
                var parsedArgs = ParseArguments(args);
                var resultTask = RunProcessViaJavaAsync(_ffmpegPath, parsedArgs);
                var completedTask = await Task.WhenAny(resultTask, Task.Delay(Timeout.Infinite, cts.Token));
                if (completedTask != resultTask)
                {
                    System.Diagnostics.Debug.WriteLine("[FFmpeg] Java exec 超时");
                    return false;
                }
                var result = await resultTask;
                if (result.exitCode == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[FFmpeg] Java exec 成功");
                    return true;
                }
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] Java exec 退出码 {result.exitCode}: {result.stderr}");
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] Java exec 异常: {ex.Message}");
            }

            // ── 方法 2: sh -c ──
            try
            {
                System.Diagnostics.Debug.WriteLine("[FFmpeg] 执行方式: sh -c");
                var resultTask = RunProcessViaShAsync(_ffmpegPath, args);
                var completedTask = await Task.WhenAny(resultTask, Task.Delay(Timeout.Infinite, cts.Token));
                if (completedTask != resultTask)
                {
                    System.Diagnostics.Debug.WriteLine("[FFmpeg] sh exec 超时");
                    return false;
                }
                var result = await resultTask;
                if (result.exitCode == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[FFmpeg] sh exec 成功");
                    return true;
                }
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] sh exec 退出码 {result.exitCode}: {result.stderr}");
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] sh exec 异常: {ex.Message}");
            }

            // ── 方法 3: .NET Process (最后回退) ──
            try
            {
                System.Diagnostics.Debug.WriteLine("[FFmpeg] 执行方式: System.Diagnostics.Process");
                var psi = new ProcessStartInfo
                {
                    FileName = _ffmpegPath, Arguments = args,
                    WorkingDirectory = GetSafeWorkingDirectory(),
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using var p = new Process { StartInfo = psi };
                p.Start();
                try { await p.WaitForExitAsync(cts.Token); }
                catch (OperationCanceledException) { try { p.Kill(true); } catch { } return false; }

                var stderr = await p.StandardError.ReadToEndAsync();
                var stdout = await p.StandardOutput.ReadToEndAsync();
                if (p.ExitCode != 0)
                {
                    var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    System.Diagnostics.Debug.WriteLine($"[FFmpeg] .NET Process 退出码 {p.ExitCode}: {combined}");
                }
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] .NET Process 异常: {ex.Message}");
            }

            System.Diagnostics.Debug.WriteLine("[FFmpeg] 所有执行方式均失败");
            return false;
        }
        finally { _transcodeLock.Release(); }
    }

    /// <summary>
    /// 简易参数解析：按空格分割，支持双引号包裹含空格的参数。
    /// 例: -y -i "path with spaces/file.m4a" -acodec pcm_s16le
    /// </summary>
    private static string[] ParseArguments(string argsString)
    {
        var result = new System.Collections.Generic.List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;

        for (int i = 0; i < argsString.Length; i++)
        {
            char c = argsString[i];
            if (c == '"')
            {
                inQuote = !inQuote;
            }
            else if (c == ' ' && !inQuote)
            {
                if (sb.Length > 0)
                {
                    result.Add(sb.ToString());
                    sb.Clear();
                }
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0)
            result.Add(sb.ToString());

        return result.ToArray();
    }

    /// <summary>清理旧版安装在 noexec FilesDir 中的二进制</summary>
    private static void CleanOldNoexecBinary()
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            var oldPath = System.IO.Path.Combine(ctx.FilesDir?.AbsolutePath ?? "", "ffmpeg_bin", "ffmpeg");
            if (System.IO.File.Exists(oldPath))
            {
                System.IO.File.Delete(oldPath);
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] 已清理旧版二进制: {oldPath}");
            }
        }
        catch { }
    }

    private static void SafeDelete(string path)
    { try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { } }
}
