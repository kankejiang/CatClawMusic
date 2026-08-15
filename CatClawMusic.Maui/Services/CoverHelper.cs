using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 封面缓存工具：提取嵌入封面并缓存到本地文件。
/// 后续加载直接命中磁盘缓存，无需重新解析音频文件。
/// </summary>
public static class CoverHelper
{
    private static readonly string _coverCacheDir;
    private static readonly ConcurrentDictionary<int, byte> _resolvedSongIds = new();

    // 网络封面下载并发控制：信号量限制同时进行的远程请求数，去重字典避免同一首歌重复下载
    private static readonly SemaphoreSlim _networkCoverSemaphore = new(4);
    private static readonly ConcurrentDictionary<int, byte> _networkCoverInflight = new();

    /// <summary>
    /// 封面下采样/编码全局并发信号量：Skia 解码+缩放+JPEG 编码很重（CPU+内存），
    /// 1000 首歌同时 Downsample 会打爆线程池与 GC（表现为扫描后列表页卡死数分钟、
    /// 日志大量 .NET TP Worker + GC + Skipped frames、Glide 加载失败刷屏）。
    /// 全局限制并发 2——重启后缓存命中不触发，首次全量解析时自动排队节流。
    /// </summary>
    private static readonly SemaphoreSlim _downsampleSemaphore = new(2);

    /// <summary>
    /// 轻量级封面文件完整性校验：检查文件存在、大小 > 0、文件头为已知图片格式魔术字节，
    /// 并检查文件尾标记（JPEG EOI / PNG IEND），避免写入中途崩溃/断电留下的半截损坏缓存
    /// 被当成有效图片使用，导致封面只显示上半部分。
    /// 不完整性解码图片，避免 GC 压力。
    /// </summary>
    public static bool IsValidImageFilePublic(string? path) => IsValidImageFile(path);

    /// <summary>读取图片最长边像素（解码失败返回 0——多为解码器不认识的合法图，交由 UI 解码器自行裁决）。</summary>
    public static int MaxDimensionPublic(string path) => MaxDimension(path);

    /// <summary>
    /// 校验路径是否是一个"可识别的图片文件"。
    /// 只判"是不是图"（文件头 magic + 非空尺寸），不再苛求文件尾严格收尾：
    /// 很多真实封面（尤其内嵌封面提取/刻录工具产出）会在 EOI/IEND 后带填充字节、
    /// 或省略 EOI 标记，严格尾校验会把它们误判为非法 → 播放页回退默认封面，
    /// 而发现页（直接解码、不过此校验）却正常显示。
    /// 解码正确性交给各平台图片解码器自行裁决（解码失败只显示空白/占位，不会崩溃）。
    /// </summary>
    private static bool IsValidImageFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        try
        {
            if (!File.Exists(path)) return false;
            var fi = new FileInfo(path);
            if (fi.Length < 16) return false; // 太小，肯定不完整

            using var fs = File.OpenRead(path);
            Span<byte> header = stackalloc byte[12];
            int read = fs.Read(header);
            if (read < 4) return false;

            // JPEG: FF D8 FF（EOI 标记可能缺失或被尾随字节覆盖，不苛求）
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return true;

            // PNG: 89 50 4E 47（IEND 后可能有尾随 chunk，不苛求）
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                return true;

            // BMP: 42 4D（无简单尾标记，至少检查头部大小字段合理）
            if (header[0] == 0x42 && header[1] == 0x4D)
            {
                if (fi.Length < 14) return false;
                // BMP 文件头 2 字节标志 + 4 字节文件大小
                fs.Seek(2, SeekOrigin.Begin);
                Span<byte> sizeBytes = stackalloc byte[4];
                if (fs.Read(sizeBytes) != 4) return false;
                var declaredSize = BitConverter.ToUInt32(sizeBytes);
                // 声明大小与实际大小允许一定容差（部分 BMP 写入时不精确）
                return declaredSize == 0 || Math.Abs((long)declaredSize - fi.Length) <= 4096;
            }

            // GIF: 47 49 46 38（尾部 3B 标记可被填充字节覆盖，不苛求）
            if (header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38)
                return true;

            // WebP: 52 49 46 46 ?? ?? ?? ?? 57 45 42 50，文件大小在 header[4..7]
            if (header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
                && read >= 12 && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            {
                var chunkSize = BitConverter.ToUInt32(header.Slice(4, 4));
                // RIFF chunk 大小 = 文件总大小 - 8
                return Math.Abs((long)chunkSize - (fi.Length - 8)) <= 4096;
            }

            return false;
        }
        catch { return false; }
    }

    /// <summary>封面尺寸分级（最大边长，像素）——按使用场景限制，减少内存与解码开销。</summary>
    public const int NowPlayingSize = 1000;   // 播放页大图
    public const int DiscoverSize = 800;      // 发现页卡片 / 精选大图
    public const int ThumbnailSize = 300;     // 歌单 / 列表 / 缩略图
    private const int DefaultMaxSize = 1000;

    static CoverHelper()
    {
        _coverCacheDir = System.IO.Path.Combine(FileSystem.CacheDirectory, "covers");
        Directory.CreateDirectory(_coverCacheDir);
    }

    /// <summary>获取封面缓存目录路径</summary>
    public static string CacheDirectory => _coverCacheDir;

    /// <summary>
    /// 批量解析歌曲封面：先查磁盘缓存，未命中则提取嵌入封面。
    /// 直接修改 song.CoverArtPath 为缓存文件路径。
    /// 使用并行处理（最多 8 线程）以充分利用八核 CPU。
    /// </summary>
    /// <param name="songs">待解析封面的歌曲集合</param>
    public static void BatchResolveCovers(IEnumerable<Song> songs, int maxSize = ThumbnailSize)
    {
        var songList = songs as IList<Song> ?? songs.ToList();
        if (songList.Count == 0) return;

        // 单首或少量歌曲直接串行，避免线程调度开销
        if (songList.Count <= 2)
        {
            foreach (var song in songList)
                ResolveOneInline(song, maxSize);
            return;
        }

        // 并行度：上限 8（八核设备满负载解码），封顶避免过多线程争抢；下限 2 避免双核设备过慢。
        // 该解析运行在后台线程（BatchResolveCoversAsync 内 Task.Run），不阻塞 UI 渲染与输入。
        // 并行度 3：TagLib 提取内嵌封面 = 全文件读取（大 flac/mp3），并行太高 IO 饱和
        // （1000 首时曾拖垮整个设备）；下采样另有全局信号量(2)二次限流。
        var options = new ParallelOptions { MaxDegreeOfParallelism = 3 };
        Parallel.ForEach(songList, options, s => ResolveOneInline(s, maxSize));
    }

    /// <summary>
    /// 分块异步解析封面，每处理一小批后让出 CPU/主线程，避免一次性并行解码成千上万个
    /// 音频文件内嵌封面导致设备整体卡顿、GC 压力剧增（表现为进入音乐库各页面时主线程被拖垮）。
    /// 用于进入"歌曲/艺术家/专辑"页面时的后台封面填充：列表先以占位图即时渲染，
    /// 封面在后台分批就绪后通过绑定（INotifyPropertyChanged）自动刷新。
    /// 块大小 32 / 让出 10ms：批量更大时仍会长时间占用 IO（实测 1000 首场景）。
    /// </summary>
    /// <param name="songs">待解析封面的歌曲集合</param>
    /// <param name="chunkSize">每批处理的歌曲数</param>
    /// <param name="yieldDelayMs">每批之间的让出间隔（毫秒），给渲染/输入让路</param>
    /// <param name="ct">取消令牌</param>
    public static async Task BatchResolveCoversAsync(IEnumerable<Song> songs, int chunkSize = 32, int yieldDelayMs = 10, CancellationToken ct = default)
    {
        var list = songs as List<Song> ?? songs.ToList();
        if (list.Count == 0) return;

        for (int i = 0; i < list.Count; i += chunkSize)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = i + chunkSize >= list.Count
                ? list.Skip(i).ToList()
                : list.GetRange(i, chunkSize);
            await Task.Run(() => BatchResolveCovers(chunk), ct);
            if (yieldDelayMs > 0)
                await Task.Delay(yieldDelayMs, ct);
        }
    }

    /// <summary>单首歌曲封面解析的内联方法（线程安全，无共享状态）</summary>
    private static void ResolveOneInline(Song song, int maxSize = ThumbnailSize)
    {
        if (song.Id <= 0) return;

        // 跳过已解析过的（同一会话内）
        if (_resolvedSongIds.ContainsKey(song.Id))
        {
            var cachedPath = GetCachedPath(song.Id, maxSize);
            if (File.Exists(cachedPath))
                song.CoverArtPath = cachedPath;
            return;
        }

        var path = ResolveSingleCover(song, maxSize);
        if (path != null)
            song.CoverArtPath = path;

        _resolvedSongIds.TryAdd(song.Id, 0);
    }

    /// <summary>
    /// 解析单首歌曲的封面路径。
    /// 优先检查磁盘缓存（已下采样），然后尝试从音频文件提取嵌入封面。
    /// 提取后会下采样到 MaxCoverSize 以减少 UI 解码开销。
    /// </summary>
    /// <param name="song">待解析封面的歌曲对象</param>
    /// <returns>封面文件路径；无可用封面时返回 null</returns>
    public static string? ResolveSingleCover(Song song, int maxSize = DefaultMaxSize)
    {
        if (song.Id <= 0) return null;

        // 1. 命中尺寸分桶缓存
        var cachedPath = GetCachedPath(song.Id, maxSize);
        if (File.Exists(cachedPath))
        {
            if (IsValidImageFile(cachedPath))
                return cachedPath;
            // 缓存损坏，删除后继续重新提取
            TryDeleteSource(cachedPath);
        }

        // 1.5 网络来源歌曲（WebDAV/SMB/Navidrome）：封面缓存在 covers/cover_{id}.jpg
        // （由播放页 LoadCoverArt 步骤6 下载并写入）。命中则返回；
        // 未命中【不再自动触发远程下载】——列表批量加载时若逐首发起远程 Range 请求，
        // 歌多时会形成请求风暴拖垮网络/线程池（表现为"网络音乐列表一进就卡死"）。
        // 封面改为播放时获取：播放页会下载封面并写缓存，之后列表自然命中缓存显示。
        if (song.Source != SongSource.Local && !string.IsNullOrEmpty(song.RemoteId))
        {
            var netCached = System.IO.Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
            if (File.Exists(netCached))
            {
                var bucket = GetCachedPath(song.Id, maxSize);
                return File.Exists(bucket) ? bucket : netCached;
            }
            // 无缓存：返回 null（列表显示占位图），不触发下载
        }

        // 2. 选择可用源：优先使用 >= maxSize 的已有文件，否则从音频文件重新提取全分辨率
        string? source = null;
        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath)
            && MaxDimension(song.CoverArtPath) >= maxSize)
        {
            source = song.CoverArtPath;
        }

        if (source == null
            && !string.IsNullOrEmpty(song.FilePath)
            && !song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase)
            && !song.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !song.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !song.FilePath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)
            && File.Exists(song.FilePath))
        {
            try
            {
                source = TagReader.ExtractCoverArtToFile(song.FilePath, _coverCacheDir);
            }
            catch (Exception ex)
            {
                Log.Debug("CoverHelper", $"[CoverHelper] Extract cover failed for {song.Title}: {ex.Message}");
            }
        }

        // 3. 兜底：退而求其次使用任意已有的封面文件（可能小于 maxSize，但不至于无图）。
        // 注意：小缩略图（< maxSize）【只直接返回、不写入 maxSize 尺寸桶】——
        // 否则会把缩略图内容"冒充"成 1000px 缓存，播放页盲目信任该桶后放大显示 = 低清。
        if (source == null && !string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
            return song.CoverArtPath;

        if (source != null)
        {
            if (DownsampleToCache(source, cachedPath, maxSize))
            {
                // 仅清理临时提取文件；song.CoverArtPath 是列表/歌单缩略图，不能删
                if (source != song.CoverArtPath && source != cachedPath)
                    TryDeleteSource(source);
                return cachedPath;
            }
            // 下采样失败则直接使用源文件
            if (source != cachedPath && File.Exists(source))
            {
                try { File.Copy(source, cachedPath, overwrite: true); } catch { }
                if (source != song.CoverArtPath) TryDeleteSource(source);
                return File.Exists(cachedPath) ? cachedPath : source;
            }
            return source;
        }

        return null;
    }

    /// <summary>
    /// 批量下载网络歌曲封面到本地缓存（前台流程使用，带进度与取消）。
    /// 仅处理有 RemoteId 的网络歌曲（已有缓存自动跳过）；每首下载完成后回填
    /// song.CoverArtPath 触发 INPC，列表可见 cell 自动刷新。
    /// 并发由 _networkCoverSemaphore(4) 控制，避免请求风暴。
    /// </summary>
    /// <param name="songs">待下载封面的歌曲集合</param>
    /// <param name="progress">进度回调 (done, total, status)</param>
    /// <param name="ct">取消令牌</param>
    public static async Task DownloadNetworkCoversAsync(
        IEnumerable<Song> songs,
        IProgress<(int done, int total, string status)>? progress = null,
        CancellationToken ct = default)
    {
        var list = songs
            .Where(s => s.Source != SongSource.Local && !string.IsNullOrEmpty(s.RemoteId))
            .ToList();
        if (list.Count == 0) return;

        var total = list.Count;
        var done = 0;
        progress?.Report((0, total, $"正在下载封面 0/{total}"));

        var tasks = list.Select(song => Task.Run(async () =>
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                await DownloadNetworkCoverAsync(song);
            }
            catch (Exception ex)
            {
                Log.Debug("CoverHelper", $"[CoverHelper] 网络封面批量下载失败 songId={song.Id}: {ex.Message}");
            }
            finally
            {
                var d = Interlocked.Increment(ref done);
                if (progress != null && (d % 10 == 0 || d == total))
                    progress.Report((d, total, $"正在下载封面 {d}/{total}"));
            }
        }, ct));

        await Task.WhenAll(tasks);
        progress?.Report((total, total, ct.IsCancellationRequested
            ? $"已跳过封面下载（{done}/{total}）"
            : $"封面下载完成，共 {total} 首"));
    }

    /// <summary>
    /// 触发网络歌曲封面的异步下载（fire-and-forget）。
    /// 不阻塞批量解析循环；对同一首歌去重，下载完成后写回 song.CoverArtPath 触发 INPC 刷新。
    /// 并发数由信号量限制，避免一次性为整个网络歌单发起海量 WebDAV/SMB 请求拖垮服务器与线程池。
    /// </summary>
    private static void TriggerNetworkCoverResolve(Song song)
    {
        if (song.Id <= 0 || string.IsNullOrEmpty(song.RemoteId)) return;
        // 去重：已在飞行中的下载不再重复触发
        if (!_networkCoverInflight.TryAdd(song.Id, 0)) return;
        _ = DownloadNetworkCoverAsync(song);
    }

    /// <summary>
    /// 按协议从远程服务下载封面并缓存到 covers/cover_{id}.jpg；完成后回填 song.CoverArtPath。
    /// WebDAV/SMB 走 INetworkMusicService.GetCoverAsync（下载文件头提取内嵌封面）；
    /// Navidrome 同样复用该方法（内部转 Subsonic GetCoverArtAsync）。
    /// </summary>
    private static async Task DownloadNetworkCoverAsync(Song song)
    {
        try
        {
            await _networkCoverSemaphore.WaitAsync();
            var cachedPath = System.IO.Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
            if (File.Exists(cachedPath)) return;

            var svc = CatClawMusic.Maui.MauiProgram.Services.GetService<INetworkMusicService>();
            if (svc == null) return;

            var profiles = await svc.GetProfilesAsync();
            var profile = song.Protocol switch
            {
                ProtocolType.WebDAV or ProtocolType.SMB => profiles.FirstOrDefault(p => p.Protocol == song.Protocol && p.IsEnabled),
                ProtocolType.Navidrome => profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome && p.IsEnabled),
                _ => null
            };
            if (profile == null) return;

            using var stream = await svc.GetCoverAsync(song.RemoteId!, profile, song.RemoteCoverPath);
            if (stream == null) return;

            // 临时文件 + 原子重命名 + 校验
            var tmpPath = cachedPath + ".tmp";
            await using (var fs = File.Create(tmpPath))
            {
                await stream.CopyToAsync(fs);
            }
            if (!IsValidImageFile(tmpPath))
            {
                TryDeleteSource(tmpPath);
                return;
            }
            File.Move(tmpPath, cachedPath, overwrite: true);
            // 回填封面路径：INPC 让列表可见 cell 自动刷新
            song.CoverArtPath = cachedPath;
        }
        catch (Exception ex)
        {
            Log.Debug("CoverHelper", $"[CoverHelper] 网络封面下载失败 songId={song.Id}: {ex.Message}");
        }
        finally
        {
            _networkCoverSemaphore.Release();
            _networkCoverInflight.TryRemove(song.Id, out _);
        }
    }

    /// <summary>
    /// 将源图片下采样到 MaxCoverSize 并保存到目标路径。
    /// 使用 Microsoft.Maui.Graphics 跨平台 API，避免主线程同步解码大图。
    /// 全局信号量限流：Skia 解码+JPEG 编码重负载，批量场景（1000 首封面首次解析）
    /// 同时进行会打爆线程池/GC，限制全局并发 2 自动排队。
    /// </summary>
    /// <param name="sourcePath">原始图片路径</param>
    /// <param name="destPath">目标缓存路径</param>
    /// <returns>下采样成功返回 true；失败返回 false</returns>
    public static bool DownsampleToCache(string sourcePath, string destPath, int maxSize = DefaultMaxSize)
    {
        _downsampleSemaphore.Wait();
        try
        {
            return DownsampleCore(sourcePath, destPath, maxSize);
        }
        finally
        {
            _downsampleSemaphore.Release();
        }
    }

    /// <summary>DownsampleToCache 的核心实现（调用方需持有 _downsampleSemaphore）</summary>
    private static bool DownsampleCore(string sourcePath, string destPath, int maxSize)
    {
        try
        {
            if (!File.Exists(sourcePath)) return false;

            using var srcStream = File.OpenRead(sourcePath);
            using var image = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(srcStream);
            if (image == null) return false;

            var width = (int)image.Width;
            var height = (int)image.Height;
            if (width <= 0 || height <= 0) return false;

            // 已足够小，无需下采样
            if (width <= maxSize && height <= maxSize)
            {
                if (sourcePath != destPath)
                {
                    File.Copy(sourcePath, destPath, overwrite: true);
                }
                return true;
            }

            // 等比缩放
            var ratio = Math.Min((double)maxSize / width, (double)maxSize / height);
            var newWidth = (int)(width * ratio);
            var newHeight = (int)(height * ratio);

            using var downsized = image.Downsize(newWidth, newHeight);
            // 临时文件 + 原子重命名：避免写入中途异常留下半截损坏缓存
            var tmpPath = destPath + ".tmp";
            using (var destStream = File.Create(tmpPath))
            {
                downsized.Save(destStream);
            }
            if (!IsValidImageFile(tmpPath))
            {
                // 下采样后校验失败，删除临时文件不写入目标
                TryDeleteSource(tmpPath);
                return false;
            }
            File.Move(tmpPath, destPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug("CoverHelper", $"[CoverHelper] Downsample failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>安全删除源文件（忽略失败）</summary>
    private static void TryDeleteSource(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                File.Delete(path);
        }
        catch { /* 忽略删除失败 */ }
    }

    /// <summary>快速读取图片最大边长（像素），用于判断是否需要重新提取更高分辨率源。</summary>
    private static int MaxDimension(string path)
    {
        try
        {
            using var s = File.OpenRead(path);
            using var img = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(s);
            if (img == null) return 0;
            return (int)Math.Max(img.Width, img.Height);
        }
        catch { return 0; }
    }

    /// <summary>将原始封面字节下采样并保存到 outputPath（用于 SAF 扫描期写入封面）。</summary>
    public static string? SaveCoverBytes(byte[] art, string outputPath, int maxSize = ThumbnailSize)
    {
        try
        {
            var tmp = outputPath + ".tmp";
            File.WriteAllBytes(tmp, art);
            if (DownsampleToCache(tmp, outputPath, maxSize))
            {
                TryDeleteSource(tmp);
                return outputPath;
            }
            if (File.Exists(tmp))
            {
                File.Move(tmp, outputPath, overwrite: true);
                return outputPath;
            }
        }
        catch (Exception ex)
        {
            Log.Debug("CoverHelper", $"[CoverHelper] SaveCoverBytes failed: {ex.Message}");
        }
        return null;
    }

    /// <summary>获取歌曲封面的标准缓存路径</summary>
    /// <param name="songId">歌曲唯一标识</param>
    /// <returns>封面缓存文件绝对路径</returns>
    /// <summary>获取歌曲封面的标准缓存路径（按尺寸分桶，避免不同尺寸互相覆盖）。</summary>
    public static string GetCachedPath(int songId, int maxSize = DefaultMaxSize)
    {
        return System.IO.Path.Combine(_coverCacheDir, $"cover_{songId}_{maxSize}.jpg");
    }

    /// <summary>
    /// 网络 http(s) 封面缓存路径（URL 指纹化）：cover_{songId}_{sha256(url)前8位}.jpg。
    /// URL 变化时缓存 key 自动变化 → 旧图不再命中（修复"同 Id 换封面 URL 后仍显示旧缓存图"的问题）。
    /// url 为 null/空 时退化为 cover_{songId}.jpg（兼容非 URL 封面源的旧行为）。
    /// </summary>
    public static string GetHttpCoverCachePath(int songId, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..8];
            return System.IO.Path.Combine(_coverCacheDir, $"cover_{songId}_{hash}.jpg");
        }
        return System.IO.Path.Combine(_coverCacheDir, $"cover_{songId}.jpg");
    }

    /// <summary>
    /// 清空已解析歌曲ID的内存缓存。
    /// 当音乐库刷新或需要释放内存时调用，防止 _resolvedSongIds 无限增长。
    /// 调用后下次访问歌曲时会重新检查磁盘缓存或重新提取封面。
    /// </summary>
    public static void ClearCache()
    {
        _resolvedSongIds.Clear();
    }

    /// <summary>
        /// 迁移旧版缓存封面：不做任何操作，旧缓存继续使用。
        /// 新提取的封面按尺寸分桶（播放页 1000 / 发现页 800 / 缩略图 300）。
    /// </summary>
    public static Task MigrateLegacyCoversAsync()
    {
        return Task.CompletedTask;
    }
}
