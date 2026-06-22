using System.Diagnostics;

namespace CatClawMusic.UI.Services;

/// <summary>
/// Android 音频转码服务
/// 优先使用 MediaCodec，失败时回退到 FFmpeg；
/// FFmpeg 二进制优先从 APK Assets 内置提取，未找到时允许从网络下载。
/// </summary>
public class FFmpegService
{
    private string? _ffmpegPath;
    private bool _initAttempted;
    private readonly SemaphoreSlim _transcodeLock = new(2, 2);
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private static readonly string[] TranscodeExtensions =
        { ".m4a", ".m4b", ".mp4", ".mov", ".wma", ".ogg", ".opus", ".ape", ".wv", ".aiff", ".aif", ".alac" };

    // 国内可访问的 FFmpeg 二进制镜像
    private static readonly string[] DownloadUrls = new[]
    {
        "https://ghproxy.net/https://github.com/arthenica/ffmpeg-kit/releases/download/v6.0/ffmpeg-kit-min-6.0-arm64-v8a-android-lts.zip",
        "https://gh.con.sh/https://github.com/arthenica/ffmpeg-kit/releases/download/v6.0/ffmpeg-kit-min-6.0-arm64-v8a-android-lts.zip",
        "https://gh-proxy.com/https://github.com/arthenica/ffmpeg-kit/releases/download/v6.0/ffmpeg-kit-min-6.0-arm64-v8a-android-lts.zip",
        "https://gh.llkk.cc/https://github.com/arthenica/ffmpeg-kit/releases/download/v6.0/ffmpeg-kit-min-6.0-arm64-v8a-android-lts.zip",
    };

    public bool IsAvailable => _ffmpegPath != null;
    public bool IsDownloading { get; private set; }
    public int DownloadProgress { get; private set; }

    public event Action<int>? DownloadProgressChanged;
    public event Action<bool>? DownloadCompleted;

    private static string GetFFmpegDestPath()
    {
        // 使用 Android 私有文件目录，避免 SpecialFolder.Personal 在某些 ROM 上映射到不存在路径
        var filesDir = global::Android.App.Application.Context.FilesDir?.AbsolutePath
            ?? System.Environment.GetFolderPath(System.Environment.SpecialFolder.Personal);
        var destDir = System.IO.Path.Combine(filesDir, "ffmpeg_bin");
        System.IO.Directory.CreateDirectory(destDir);
        return System.IO.Path.Combine(destDir, "ffmpeg");
    }

    public async Task<bool> InitializeAsync()
    {
        if (_initAttempted) return _ffmpegPath != null;
        _initAttempted = true;

        var destPath = GetFFmpegDestPath();

        // 优先使用已解压的内置二进制
        if (System.IO.File.Exists(destPath))
        {
            await EnsureExecutableAsync(destPath);
            _ffmpegPath = destPath;
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] 使用已存在的内置二进制: {destPath}");
            return true;
        }

        // 首次启动：从 APK Assets 提取对应 ABI 的 FFmpeg 二进制
        System.Diagnostics.Debug.WriteLine("[FFmpeg] 开始从 APK Assets 提取内置二进制");
        var bundled = await ExtractBundledBinaryAsync(destPath);
        if (bundled)
        {
            _ffmpegPath = destPath;
            return true;
        }

        // Try system ffmpeg
        if (System.IO.File.Exists("/data/local/tmp/ffmpeg"))
        {
            _ffmpegPath = "/data/local/tmp/ffmpeg";
            return true;
        }

        return false;
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

            // 验证二进制可执行：执行 ffmpeg -version
            var versionPsi = new ProcessStartInfo
            {
                FileName = destPath,
                Arguments = "-version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var versionProc = Process.Start(versionPsi);
            if (versionProc != null)
            {
                await versionProc.WaitForExitAsync();
                if (versionProc.ExitCode == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[FFmpeg] 内置二进制提取成功: {preferredAbi}");
                    return true;
                }
            }

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

    /// <summary>从网络下载 FFmpeg 二进制</summary>
    public async Task<bool> DownloadAsync()
    {
        if (IsDownloading) return false;

        var destPath = GetFFmpegDestPath();

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadProgressChanged?.Invoke(0);

        try
        {
            foreach (var url in DownloadUrls)
            {
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[FFmpeg] Trying: {url}");
                    using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    if (!resp.IsSuccessStatusCode) continue;

                    var totalLen = resp.Content.Headers.ContentLength ?? -1;
                    using var stream = await resp.Content.ReadAsStreamAsync();
                    using var output = System.IO.File.Create(destPath);

                    var buffer = new byte[8192];
                    long downloaded = 0;
                    int read;
                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read));
                        downloaded += read;
                        if (totalLen > 0)
                        {
                            int pct = (int)(downloaded * 100 / totalLen);
                            if (pct != DownloadProgress)
                            {
                                DownloadProgress = pct;
                                DownloadProgressChanged?.Invoke(pct);
                            }
                        }
                    }
                    await output.FlushAsync();

                    if (downloaded > 100000) // 至少 100KB
                    {
                        await EnsureExecutableAsync(destPath);
                        _ffmpegPath = destPath;
                        IsDownloading = false;
                        DownloadProgress = 100;
                        DownloadProgressChanged?.Invoke(100);
                        DownloadCompleted?.Invoke(true);
                        System.Diagnostics.Debug.WriteLine($"[FFmpeg] Downloaded: {downloaded} bytes");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FFmpeg] Download error: {ex.Message}");
                }
            }

            // All mirrors failed
            SafeDelete(destPath);
            IsDownloading = false;
            DownloadCompleted?.Invoke(false);
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] Download failed: {ex.Message}");
            SafeDelete(destPath);
            IsDownloading = false;
            DownloadCompleted?.Invoke(false);
            return false;
        }
    }

    public bool NeedsTranscoding(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath)?.ToLowerInvariant();
        return !string.IsNullOrEmpty(ext) && TranscodeExtensions.Contains(ext);
    }

    /// <summary>转码为 WAV（ALAC 等格式软解为 ExoPlayer 可播放的 PCM/WAV）</summary>
    public async Task<string?> TranscodeToWavAsync(string inputPath, CancellationToken ct = default)
    {
        // Try MediaCodec first
        System.Diagnostics.Debug.WriteLine($"[FFmpeg] 开始转码: {inputPath}");
        var wavPath = await MediaCodecToWavAsync(inputPath, ct);
        if (wavPath != null)
        {
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] MediaCodec 转码成功: {wavPath}");
            return wavPath;
        }

        // Try FFmpeg
        if (_ffmpegPath == null && !await InitializeAsync())
        {
            System.Diagnostics.Debug.WriteLine("[FFmpeg] 内置二进制不可用，尝试网络下载");
            // Auto-download FFmpeg on first failure
            if (await DownloadAsync())
                await InitializeAsync();
        }

        if (_ffmpegPath == null)
        {
            System.Diagnostics.Debug.WriteLine("[FFmpeg] 没有可用的 FFmpeg 二进制");
            return null;
        }
        if (!System.IO.File.Exists(inputPath)) return null;

        System.Diagnostics.Debug.WriteLine($"[FFmpeg] 使用 FFmpeg 软解: {_ffmpegPath}");
        var outputPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"cc_ff_{System.IO.Path.GetFileNameWithoutExtension(inputPath)}_{Guid.NewGuid():N}.wav");

        var args = $"-y -i \"{inputPath}\" -acodec pcm_s16le -ar 44100 -ac 2 \"{outputPath}\"";
        var result = await RunFFmpegAsync(args, ct);

        if (!result || !System.IO.File.Exists(outputPath) || new System.IO.FileInfo(outputPath).Length < 1024)
        {
            System.Diagnostics.Debug.WriteLine("[FFmpeg] FFmpeg 软解失败或输出文件为空");
            SafeDelete(outputPath);
            return null;
        }

        System.Diagnostics.Debug.WriteLine($"[FFmpeg] FFmpeg 软解成功: {outputPath}");
        return outputPath;
    }

    // ── MediaCodec 转码 ──

    private static async Task<string?> MediaCodecToWavAsync(string inputPath, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            Android.Media.MediaExtractor? extractor = null;
            Android.Media.MediaCodec? codec = null;

            try
            {
                extractor = new Android.Media.MediaExtractor();
                extractor.SetDataSource(inputPath);

                int audioIdx = -1;
                string? mime = null;
                Android.Media.MediaFormat? format = null;

                for (int i = 0; i < extractor.TrackCount; i++)
                {
                    var f = extractor.GetTrackFormat(i);
                    var m = f.GetString(Android.Media.MediaFormat.KeyMime);
                    if (m != null && m.StartsWith("audio/"))
                    {
                        audioIdx = i;
                        mime = m;
                        format = f;
                        break;
                    }
                }

                if (audioIdx < 0 || format == null || mime == null) return null;

                System.Diagnostics.Debug.WriteLine($"[FFmpeg] MediaCodec trying: {mime}");

                try { codec = Android.Media.MediaCodec.CreateDecoderByType(mime); }
                catch { return null; }

                extractor.SelectTrack(audioIdx);
                codec.Configure(format, null, null, Android.Media.MediaCodecConfigFlags.None);
                codec.Start();

                var outputPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"cc_mc_{System.IO.Path.GetFileNameWithoutExtension(inputPath)}_{Guid.NewGuid():N}.wav");
                using var output = System.IO.File.Create(outputPath);

                // Placeholder header
                output.Write(new byte[44], 0, 44);

                var bufferInfo = new Android.Media.MediaCodec.BufferInfo();
                var inputBuffers = codec.GetInputBuffers();
                bool inputDone = false;
                bool outputDone = false;
                long totalPcm = 0;
                int sampleRate = 44100;
                int channelCount = 2;

                while (!outputDone && !ct.IsCancellationRequested)
                {
                    if (!inputDone)
                    {
                        int idx = codec.DequeueInputBuffer(10000);
                        if (idx >= 0)
                        {
                            var buf = inputBuffers[idx];
                            int n = extractor.ReadSampleData(buf, 0);
                            if (n < 0)
                            {
                                codec.QueueInputBuffer(idx, 0, 0, 0, Android.Media.MediaCodecBufferFlags.EndOfStream);
                                inputDone = true;
                            }
                            else
                            {
                                codec.QueueInputBuffer(idx, 0, n, extractor.SampleTime, Android.Media.MediaCodecBufferFlags.None);
                                extractor.Advance();
                            }
                        }
                    }

                    int outIdx = codec.DequeueOutputBuffer(bufferInfo, 10000);
                    if (outIdx >= 0)
                    {
                        var outBuf = codec.GetOutputBuffer(outIdx);
                        byte[] pcm = new byte[bufferInfo.Size];
                        outBuf?.Get(pcm, 0, bufferInfo.Size);
                        outBuf?.Position(0);
                        output.Write(pcm, 0, bufferInfo.Size);
                        totalPcm += bufferInfo.Size;

                        if ((bufferInfo.Flags & Android.Media.MediaCodecBufferFlags.EndOfStream) != 0)
                            outputDone = true;

                        codec.ReleaseOutputBuffer(outIdx, false);
                    }
                    else if (outIdx == (int)Android.Media.MediaCodecInfoState.OutputFormatChanged)
                    {
                        try
                        {
                            var outf = codec.OutputFormat;
                            sampleRate = outf.GetInteger(Android.Media.MediaFormat.KeySampleRate);
                            channelCount = outf.GetInteger(Android.Media.MediaFormat.KeyChannelCount);
                        }
                        catch { }
                    }
                    else if (outIdx == (int)Android.Media.MediaCodecInfoState.TryAgainLater)
                    {
                        if (inputDone) System.Threading.Thread.Sleep(10);
                    }
                }

                output.Flush();

                if (totalPcm < 1024) { SafeDelete(outputPath); return null; }

                // Fix WAV header
                try
                {
                    using var fs = new System.IO.FileStream(outputPath, System.IO.FileMode.Open, System.IO.FileAccess.ReadWrite);
                    using var bw = new System.IO.BinaryWriter(fs);
                    var byteRate = sampleRate * channelCount * 2;
                    bw.Seek(0, System.IO.SeekOrigin.Begin);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                    bw.Write((uint)(36 + totalPcm));
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
                    bw.Write((uint)16);
                    bw.Write((ushort)1);
                    bw.Write((ushort)channelCount);
                    bw.Write((uint)sampleRate);
                    bw.Write((uint)byteRate);
                    bw.Write((ushort)(channelCount * 2));
                    bw.Write((ushort)16);
                    bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                    bw.Write((uint)totalPcm);
                }
                catch { }

                System.Diagnostics.Debug.WriteLine($"[FFmpeg] MediaCodec OK: {totalPcm} bytes PCM");
                return outputPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] MediaCodec fail: {ex.Message}");
                return null;
            }
            finally
            {
                try { codec?.Stop(); codec?.Release(); } catch { }
                try { extractor?.Release(); } catch { }
            }
        }, ct);
    }

    // ── FFmpeg 二进制 ──

    private static async Task EnsureExecutableAsync(string path)
    {
        try
        {
            using var chmod = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod", Arguments = $"755 \"{path}\"",
                CreateNoWindow = true, UseShellExecute = false
            });
            if (chmod != null) await chmod.WaitForExitAsync();
        }
        catch { }
    }

    private async Task<bool> RunFFmpegAsync(string args, CancellationToken ct)
    {
        if (_ffmpegPath == null) return false;
        await _transcodeLock.WaitAsync(ct);
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath, Arguments = args,
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            using var p = new Process { StartInfo = psi };
            p.Start();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(2));
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { try { p.Kill(true); } catch { } return false; }

            var stderrTask = p.StandardError.ReadToEndAsync();
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderr = await stderrTask;
            var stdout = await stdoutTask;
            if (p.ExitCode != 0)
            {
                var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                System.Diagnostics.Debug.WriteLine($"[FFmpeg] 退出码 {p.ExitCode}, 输出: {combined}");
            }
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FFmpeg] 运行异常: {ex.Message}");
            return false;
        }
        finally { _transcodeLock.Release(); }
    }

    private static void SafeDelete(string path)
    { try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { } }
}
