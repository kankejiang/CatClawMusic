using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Collections.Concurrent;

namespace CatClawMusic.Data;

/// <summary>网络音乐服务 —— partial 分域文件。</summary>
public partial class NetworkMusicService
{
    public async Task<List<Song>> ScanAsync(ConnectionProfile profile,
        IProgress<(int done, int total, string status)>? progress = null,
        Action<List<Song>>? songBatchCallback = null,
        bool quickScan = false)
    {
        try { await _db.EnsureInitializedAsync(); } catch { }

        var scannedRemoteIds = new HashSet<string>();
        var allSongs = new List<Song>();

        if (profile.Protocol == ProtocolType.Navidrome)
        {
            var scanner = new MusicScanner(_db, songBatchCallback);
            allSongs = await _subsonic.GetSongsAsync(profile, progress, async (batch) =>
            {
                try
                {
                    foreach (var s in batch)
                    {
                        if (!string.IsNullOrEmpty(s.RemoteId)) scannedRemoteIds.Add(s.RemoteId);
                    }
                    // 批量入队，避免逐首 await 锁竞争
                    await scanner.AddSongsBatchAsync(batch);
                }
                catch (Exception ex)
                {
                    Log.Debug("NetworkMusicService", $"[CatClaw] 增量入库失败: {ex.Message}");
                }
                songBatchCallback?.Invoke(batch);
            });
            await scanner.FlushAsync();
        }
        else if (profile.Protocol == ProtocolType.WebDAV)
        {
            var (newSongs, allFoundIds) = await ScanWebDavAsync(profile, songBatchCallback, progress);
            allSongs = newSongs;
            foreach (var id in allFoundIds)
            {
                if (!string.IsNullOrEmpty(id)) scannedRemoteIds.Add(id);
            }
        }
        else if (profile.Protocol == ProtocolType.SMB)
        {
            var (newSongs, allFoundIds) = await ScanSmbAsync(profile, songBatchCallback, progress, quickScan);
            allSongs = newSongs;
            foreach (var id in allFoundIds)
            {
                if (!string.IsNullOrEmpty(id)) scannedRemoteIds.Add(id);
            }
        }

        try
        {
            var source = profile.Protocol == ProtocolType.SMB ? SongSource.SMB : SongSource.WebDAV;
            var removed = await _db.RemoveStaleSongsAsync(source, new HashSet<string>(), scannedRemoteIds);
            if (removed > 0)
                Log.Debug("NetworkMusicService", $"[CatClaw] 清理 {removed} 首已移除的网络歌曲 ({source})");
        }
        catch (Exception ex) { Log.Debug("NetworkMusicService", $"[CatClaw] 清理旧网络歌曲失败: {ex.Message}"); }

        return allSongs;
    }

    /// <summary>
    /// 按协议类型搜索网络音乐（当前仅支持 Navidrome）
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="profile">连接配置</param>
    /// <returns>匹配的歌曲列表</returns>
    public async Task<List<Song>> SearchAsync(string keyword, ConnectionProfile profile)
    {
        return profile.Protocol switch
        {
            ProtocolType.Navidrome => await _subsonic.SearchAsync(keyword, profile),
            _ => new List<Song>()
        };
    }

    /// <summary>
    /// 下载文件头部数据用于读取标签信息的大小（256KB，平衡扫描速度和封面质量）
    /// </summary>
    private const int TagHeadSize = 256 * 1024;

    /// <summary>
    /// 自适应重试时使用的较大标签头大小（2MB）：当小头解析不出有效元数据
    /// （如 FLAC 内嵌大封面 / 长 vorbis comment 使标签头超过 256KB）时，用此大小重读一次，
    /// 避免直接回退到整文件下载。
    /// </summary>
    private const int TagHeadSizeLarge = 2 * 1024 * 1024;

    /// <summary>
    /// 文件尾部下载大小（8MB）：非 faststart 的 M4A/MP4 的 moov box（含标签/封面/时长）位于文件末尾，
    /// 头部截断解析失败时 Range 请求最后一段，从 moov 手动解析元数据（无需整首下载）。
    /// </summary>
    private const int TagTailSize = 8 * 1024 * 1024;

    /// <summary>
    /// 下载远程文件的头部数据用于标签解析，失败时回退到完整下载
    /// </summary>
    /// <summary>
    /// 下载远程文件头部用于封面提取。默认用较大的 2MB 头（内嵌大封面常见于 FLAC，常超过 256KB），
    /// 返回是否被截断（文件比头部更长），供调用方在抽不到封面时决定是否整首下载兜底。
    /// Range 不被服务器支持时直接整首下载（truncated=false，已拿到完整文件）。
    /// </summary>

    private async Task<(List<Song> NewSongs, HashSet<string> AllFoundIds)> ScanWebDavAsync(
        ConnectionProfile profile, Action<List<Song>>? songBatchCallback,
        IProgress<(int done, int total, string status)>? progress = null)
    {
        var songs = new List<Song>();
        var basePath = profile.BasePath?.TrimEnd('/') ?? "/";
        if (string.IsNullOrEmpty(basePath)) basePath = "/";

        progress?.Report((0, 0, "正在连接服务器..."));
        var connResult = await _webDav.TestConnectionAsync(profile);
        if (!connResult.Success)
        {
            return (songs, new HashSet<string>());
        }

        _webDav.Configure(profile);
        if (_webDav is WebDavService wdsScan) await wdsScan.EnsureDetectedAsync();

        var foundIds = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        var existingIds = new HashSet<string>();

        var scanner = new MusicScanner(_db, songBatchCallback);
        var serverType = (WebDavServerType)profile.ServerType;

        // 如果自动检测发现是 OpenList，同步到 serverType
        if (serverType != WebDavServerType.OpenList && _webDav is WebDavService wdsScanType
            && wdsScanType.CurrentServerType == WebDavServerType.OpenList)
        {
            serverType = WebDavServerType.OpenList;
            profile.ServerType = (int)WebDavServerType.OpenList;
            try { await _db.SaveConnectionProfileAsync(profile); } catch { }
        }

        // OpenList/Alist：先快速扫描入库（不下载元数据），再后台补齐元数据
        var quickScan = serverType == WebDavServerType.OpenList;

        // 标准 WebDAV 默认用 depth=1 递归扫描：避免大库 depth=infinity 一次 PROPFIND 返回巨量 XML 导致超时/OOM。
        // 仅 OpenList/Alist（走 REST，无此问题）或递归结果为空时才回退到 depth=infinity。
        List<RemoteFile> allFiles = new();
        bool scannedRecursively = false;
        if (serverType != WebDavServerType.OpenList)
        {
            Log.Debug("NetworkMusicService", "[WebDAV Scan] 标准服务器：优先 depth=1 递归扫描");
            var visitedDirs = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            await ScanWebDavDirectoryAsync(basePath, profile, songs, foundIds, existingIds, scanner, visitedDirs, quickScan, 0, progress);
            scannedRecursively = true;
            if (songs.Count == 0)
                allFiles = await _webDav.ListAllFilesAsync(basePath, serverType); // 递归无果 → 兜底 depth=infinity
        }
        else
        {
            allFiles = await _webDav.ListAllFilesAsync(basePath, serverType);
        }

        // 自动检测：如果 ListAllFiles 内部切换了 ServerType，同步到 profile 并保存
        if (_webDav is WebDavService wds && wds.CurrentServerType == WebDavServerType.OpenList
            && (WebDavServerType)profile.ServerType != WebDavServerType.OpenList)
        {
            Log.Debug("NetworkMusicService", "[WebDAV Scan] 自动检测到 OpenList，更新 profile.ServerType");
            profile.ServerType = (int)WebDavServerType.OpenList;
            try { await _db.SaveConnectionProfileAsync(profile); } catch { }
            serverType = WebDavServerType.OpenList;
            quickScan = true;
        }

        try
        {
            var existingSongs = await _db.GetCachedNetworkSongsAsync();
            foreach (var s in existingSongs.Where(s => s.Source == SongSource.WebDAV && !string.IsNullOrEmpty(s.RemoteId)))
            {
                existingIds.Add(s.RemoteId!);
            }
        }
        catch { }

        if (scannedRecursively)
        {
            // 已通过 depth=1 递归扫描入库，无需再次处理
            Log.Debug("NetworkMusicService", $"[WebDAV Scan] 递归扫描完成，已入库 {songs.Count} 首歌曲");
        }
        else if (allFiles.Count > 0)
        {
            Log.Debug("NetworkMusicService", $"[WebDAV Scan] 深度 PROPFIND 成功，找到 {allFiles.Count} 个文件，并发处理中...");
            progress?.Report((0, allFiles.Count, $"发现 {allFiles.Count} 个文件，正在扫描..."));
            await ProcessFileListAsync(allFiles, profile, songs, foundIds, existingIds, scanner, quickScan, progress);
        }
        else
        {
            Log.Debug("NetworkMusicService", $"[WebDAV Scan] 深度 PROPFIND 不支持，回退到递归扫描 (quickScan={quickScan})");
            progress?.Report((0, 0, "正在递归扫描目录..."));
            var visitedDirs = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            await ScanWebDavDirectoryAsync(basePath, profile, songs, foundIds, existingIds, scanner, visitedDirs, quickScan, 0, progress);
        }

        await scanner.FlushAsync();

        progress?.Report((songs.Count, songs.Count, $"扫描完成，发现 {songs.Count} 首歌曲"));

        return (songs, new HashSet<string>(foundIds.Keys, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// WebDAV 目录扫描最大递归深度
    /// </summary>
    private const int MaxScanDepth = 20;

    /// <summary>
    /// 支持的音频文件扩展名集合
    /// </summary>
    private static readonly HashSet<string> AudioExtSet = new(
        new[] { ".MP3", ".WAV", ".FLAC", ".AAC", ".OGG", ".M4A", ".WMA", ".APE", ".AIFF", ".DSF" },
        StringComparer.Ordinal);

    /// <summary>
    /// 判断文件扩展名是否为支持的音频格式
    /// </summary>
    private static bool IsAudioExtension(string ext)
        => AudioExtSet.Contains(ext);

    /// <summary>
    /// 侧车封面图扩展名集合（同目录独立图片文件，如 folder.jpg / cover.jpg / front.png 等）
    /// </summary>
    private static readonly HashSet<string> CoverImageExtSet = new(
        new[] { ".JPG", ".JPEG", ".PNG", ".WEBP", ".BMP", ".GIF" },
        StringComparer.Ordinal);

    /// <summary>
    /// 判断路径是否为图片扩展名（用于侧车封面探测）
    /// </summary>
    private static bool IsCoverImageExtension(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var ext = System.IO.Path.GetExtension(path);
        return CoverImageExtSet.Contains(ext.ToUpperInvariant());
    }

    /// <summary>
    /// 侧车封面文件名优先级：folder &gt; cover &gt; front &gt; album/albumart/artwork &gt; 其他。数字越小优先级越高。
    /// </summary>
    private static int RankCoverName(string name)
    {
        var n = System.IO.Path.GetFileNameWithoutExtension(name)?.ToLowerInvariant() ?? "";
        if (n == "folder") return 1;
        if (n == "cover") return 2;
        if (n is "front" or "frontcover") return 3;
        if (n is "album" or "albumart" or "artwork" or "albumcover") return 4;
        return 10;
    }

    /// <summary>
    /// 从文件列表中构建侧车封面映射：
    /// - exactCoverMap：键 "父目录/无扩展名文件名" → 与该音频同名的图片（精确匹配，最高优先级）
    /// - dirCoverMap：键 "父目录" → 该目录下优先级最高的通用封面图（folder/cover/...）
    /// </summary>
    private static void BuildCoverMaps(IEnumerable<RemoteFile> files,
        out Dictionary<string, RemoteFile> exactCoverMap,
        out Dictionary<string, RemoteFile> dirCoverMap)
    {
        exactCoverMap = new Dictionary<string, RemoteFile>(StringComparer.OrdinalIgnoreCase);
        dirCoverMap = new Dictionary<string, RemoteFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (f.IsDirectory) continue;
            if (!IsCoverImageExtension(f.Name)) continue;

            var parentDir = System.IO.Path.GetDirectoryName(f.Path)?.Replace('\\', '/').TrimEnd('/') ?? "";
            var nameNoExt = System.IO.Path.GetFileNameWithoutExtension(f.Name) ?? "";
            if (!string.IsNullOrEmpty(nameNoExt))
                exactCoverMap.TryAdd($"{parentDir}/{nameNoExt}", f);

            var rank = RankCoverName(f.Name);
            if (!dirCoverMap.TryGetValue(parentDir, out var cur) || rank < RankCoverName(cur.Name))
                dirCoverMap[parentDir] = f;
        }
    }

    /// <summary>
    /// 并发处理深度 PROPFIND 返回的扁平文件列表
    /// </summary>
    private async Task ProcessFileListAsync(List<RemoteFile> allFiles, ConnectionProfile profile,
        List<Song> songs, System.Collections.Concurrent.ConcurrentDictionary<string, byte> foundIds, HashSet<string> existingIds, MusicScanner scanner, bool quickScan = false,
        IProgress<(int done, int total, string status)>? progress = null)
    {
        var audioFiles = allFiles
            .Where(f =>
            {
                var ext = System.IO.Path.GetExtension(f.Name)?.ToUpperInvariant() ?? "";
                if (string.IsNullOrEmpty(ext))
                    ext = System.IO.Path.GetExtension(f.Path)?.ToUpperInvariant() ?? "";
                return IsAudioExtension(ext);
            })
            .ToList();

        // 收集歌词文件（.lrc/.ttml），按父目录+文件名索引（避免跨目录误匹配）
        var lyricsMap = new Dictionary<string, RemoteFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in allFiles)
        {
            if (f.IsDirectory) continue;
            var ext = System.IO.Path.GetExtension(f.Name)?.ToUpperInvariant() ?? "";
            if (ext is ".LRC" or ".TTML")
            {
                var nameNoExt = System.IO.Path.GetFileNameWithoutExtension(f.Name) ?? "";
                if (!string.IsNullOrEmpty(nameNoExt))
                {
                    var parentDir = System.IO.Path.GetDirectoryName(f.Path)?.Replace('\\', '/').TrimEnd('/') ?? "";
                    lyricsMap.TryAdd($"{parentDir}/{nameNoExt}", f);
                }
            }
        }

        Log.Debug("NetworkMusicService", $"[WebDAV Scan] 过滤后音频文件: {audioFiles.Count}");
        progress?.Report((0, audioFiles.Count, $"发现 {audioFiles.Count} 个音频文件，正在提取元数据..."));

        var processedCount = 0;
        var progressLock = new object();
        var totalAudio = audioFiles.Count;

        var metadataTasks = audioFiles.Select(file => Task.Run(async () =>
        {
            await ScanSemaphore.WaitAsync();
            try
            {
                foundIds.TryAdd(file.Path, 0);

                if (existingIds.Contains(file.Path))
                    return; // 已存在则跳过，元数据在播放时懒加载

                // 使用 WebDavService.BuildStreamUrl 构建正确的 URL（自动包含 /dav 前缀和 Basic Auth）
                string streamUrl;
                if (_webDav is WebDavService wds)
                    streamUrl = wds.BuildStreamUrl(file.Path);
                else
                    streamUrl = BuildWebDavStreamUrl(file.Path, profile);

                var title = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? file.Name;
                if (string.IsNullOrEmpty(title))
                    title = System.IO.Path.GetFileNameWithoutExtension(file.Path) ?? file.Path;
                var song = new Song
                {
                    Title = title,
                    Artist = "",
                    Album = "",
                    FilePath = streamUrl,
                    Duration = 0,
                    FileSize = file.Size,
                    DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Source = SongSource.WebDAV,
                    Protocol = ProtocolType.WebDAV,
                    RemoteId = file.Path,
                    CoverArtPath = file.Path
                };

                // 匹配同目录下的外挂歌词文件
                var audioNameNoExt = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? "";
                var audioParentDir = System.IO.Path.GetDirectoryName(file.Path)?.Replace('\\', '/').TrimEnd('/') ?? "";
                if (!string.IsNullOrEmpty(audioNameNoExt) && lyricsMap.TryGetValue($"{audioParentDir}/{audioNameNoExt}", out var lrcFile))
                {
                    if (_webDav is WebDavService wdsLrc)
                        song.LyricsPath = wdsLrc.BuildStreamUrl(lrcFile.Path);
                    else
                        song.LyricsPath = BuildWebDavStreamUrl(lrcFile.Path, profile);
                }

                // 扫描阶段不下载标签头取元数据（避免拖慢扫描），交由播放/取图时懒加载
                song.Artist = "未知艺术家";
                song.Album = "未知专辑";

                lock (songs)
                    songs.Add(song);
                await scanner.AddSongAsync(song);
            }
            finally
            {
                ScanSemaphore.Release();
                int done;
                lock (progressLock)
                {
                    processedCount++;
                    done = processedCount;
                }
                if (progress != null && (done % 5 == 0 || done == totalAudio))
                {
                    progress.Report((done, totalAudio, $"正在扫描 {done}/{totalAudio}"));
                }
            }
        }));

        await Task.WhenAll(metadataTasks);
    }

    /// <summary>
    /// 递归扫描 WebDAV 目录（回退方案，当深度 PROPFIND 不支持时使用）
    /// </summary>
    private async Task ScanWebDavDirectoryAsync(string path, ConnectionProfile profile, List<Song> songs,
        System.Collections.Concurrent.ConcurrentDictionary<string, byte> foundIds, HashSet<string> existingIds, MusicScanner scanner, System.Collections.Concurrent.ConcurrentDictionary<string, byte> visitedDirs, bool quickScan = false,
        int depth = 0,
        IProgress<(int done, int total, string status)>? progress = null)
    {
        if (depth > MaxScanDepth)
            return;

        var normalizedDir = path.TrimEnd('/').TrimEnd('\\');
        if (string.IsNullOrEmpty(normalizedDir)) normalizedDir = "/";
        if (!visitedDirs.TryAdd(normalizedDir, 0))
        {
            Log.Debug("NetworkMusicService", $"[WebDAV Scan] 跳过已访问目录: {path}");
            return;
        }

        List<RemoteFile> files;
        try
        {
            files = await _webDav.ListFilesAsync(path);
        }
        catch (Exception ex)
        {
            Log.Debug("NetworkMusicService", $"[WebDAV Scan] 列出 {path} 失败: {ex.Message}");
            return;
        }

        Log.Debug("NetworkMusicService", $"[WebDAV Scan] 目录 {path} 有 {files.Count} 个条目 (depth={depth})");

        var audioFiles = new List<RemoteFile>();
        var subDirs = new List<RemoteFile>();
        // 收集同目录下的歌词文件（.lrc/.ttml），按父目录+文件名索引
        var lyricsMap = new Dictionary<string, RemoteFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (file.IsDirectory)
                subDirs.Add(file);
            else
            {
                var ext = System.IO.Path.GetExtension(file.Name)?.ToUpperInvariant() ?? "";
                if (string.IsNullOrEmpty(ext))
                    ext = System.IO.Path.GetExtension(file.Path)?.ToUpperInvariant() ?? "";
                if (IsAudioExtension(ext))
                    audioFiles.Add(file);
                else if (ext is ".LRC" or ".TTML")
                {
                    var nameNoExt = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? "";
                    if (!string.IsNullOrEmpty(nameNoExt))
                    {
                        var parentDir = System.IO.Path.GetDirectoryName(file.Path)?.Replace('\\', '/').TrimEnd('/') ?? "";
                        lyricsMap.TryAdd($"{parentDir}/{nameNoExt}", file);
                    }
                }
            }
        }

        // 并行扫描子目录（限制并发数避免服务器过载）
        var subDirTasks = subDirs.Select(subDir => Task.Run(async () =>
        {
            await DirScanSemaphore.WaitAsync();
            try
            {
                await ScanWebDavDirectoryAsync(subDir.Path, profile, songs, foundIds, existingIds, scanner, visitedDirs, quickScan, depth + 1, progress);
            }
            finally
            {
                DirScanSemaphore.Release();
            }
        }));
        await Task.WhenAll(subDirTasks);

        if (audioFiles.Count == 0) return;

        progress?.Report((songs.Count, 0, $"正在扫描 {Path.GetFileName(path)} ({audioFiles.Count} 个音频文件)"));

        var dirProcessedCount = 0;
        var dirProgressLock = new object();
        var dirTotalAudio = audioFiles.Count;

        var metadataTasks = audioFiles.Select(file => Task.Run(async () =>
        {
            await ScanSemaphore.WaitAsync();
            try
            {
                foundIds.TryAdd(file.Path, 0);

                if (existingIds.Contains(file.Path))
                    return; // 已存在则跳过，元数据在播放时懒加载

                // 使用 WebDavService.BuildStreamUrl 构建正确的 URL（自动包含 /dav 前缀和 Basic Auth）
                string streamUrl;
                if (_webDav is WebDavService wds)
                    streamUrl = wds.BuildStreamUrl(file.Path);
                else
                    streamUrl = BuildWebDavStreamUrl(file.Path, profile);

                var title = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? file.Name;
                if (string.IsNullOrEmpty(title))
                    title = System.IO.Path.GetFileNameWithoutExtension(file.Path) ?? file.Path;
                var song = new Song
                {
                    Title = title,
                    Artist = "",
                    Album = "",
                    FilePath = streamUrl,
                    Duration = 0,
                    FileSize = file.Size,
                    DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Source = SongSource.WebDAV,
                    Protocol = ProtocolType.WebDAV,
                    RemoteId = file.Path,
                    CoverArtPath = file.Path
                };

                // 匹配同目录下的外挂歌词文件
                var audioNameNoExt = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? "";
                var audioParentDir = System.IO.Path.GetDirectoryName(file.Path)?.Replace('\\', '/').TrimEnd('/') ?? "";
                if (!string.IsNullOrEmpty(audioNameNoExt) && lyricsMap.TryGetValue($"{audioParentDir}/{audioNameNoExt}", out var lrcFile))
                {
                    if (_webDav is WebDavService wdsLrc)
                        song.LyricsPath = wdsLrc.BuildStreamUrl(lrcFile.Path);
                    else
                        song.LyricsPath = BuildWebDavStreamUrl(lrcFile.Path, profile);
                }

                // 扫描阶段不下载标签头取元数据（避免拖慢扫描），交由播放/取图时懒加载
                song.Artist = "未知艺术家";
                song.Album = "未知专辑";

                lock (songs)
                    songs.Add(song);
                await scanner.AddSongAsync(song);
            }
            finally
            {
                ScanSemaphore.Release();
                int done;
                lock (dirProgressLock)
                {
                    dirProcessedCount++;
                    done = dirProcessedCount;
                }
                if (progress != null && (done % 5 == 0 || done == dirTotalAudio))
                {
                    progress.Report((done, dirTotalAudio, $"正在扫描 {Path.GetFileName(path)} {done}/{dirTotalAudio}"));
                }
            }
        }));

        await Task.WhenAll(metadataTasks);
    }

    /// <summary>
    /// <summary>
    /// 递归扫描 SMB 共享目录，批量入库发现的音频文件。
    /// </summary>
    /// <param name="profile">SMB 连接配置。</param>
    /// <param name="songBatchCallback">每批次歌曲扫描完成后的回调。</param>
    /// <returns>(新扫描到的歌曲列表, 所有发现的文件路径 ID 集合)。</returns>
    private async Task<(List<Song> NewSongs, HashSet<string> AllFoundIds)> ScanSmbAsync(
        ConnectionProfile profile, Action<List<Song>>? songBatchCallback,
        IProgress<(int done, int total, string status)>? progress = null,
        bool quickScan = false)
    {
        var songs = new List<Song>();
        var basePath = profile.BasePath?.TrimEnd('/', '\\') ?? "\\";
        if (string.IsNullOrEmpty(basePath) || basePath == "/") basePath = "\\";

        progress?.Report((0, 0, "正在连接 SMB 服务器..."));
        var connResult = await _smb.TestConnectionAsync(profile);
        if (!connResult.Success) return (songs, new HashSet<string>());

        _smb.Configure(profile);

        var foundIds = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        var existingIds = new HashSet<string>();
        try
        {
            var existingSongs = await _db.GetCachedNetworkSongsAsync();
            existingIds = existingSongs
                .Where(s => s.Source == SongSource.SMB && !string.IsNullOrEmpty(s.RemoteId))
                .Select(s => s.RemoteId!.TrimStart('\\'))
                .ToHashSet();
        }
        catch { }

        var scanner = new MusicScanner(_db, songBatchCallback);
        var visitedDirs = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        progress?.Report((0, 0, "正在递归扫描 SMB 目录..."));
        await ScanSmbDirectoryAsync(basePath, profile, songs, foundIds, existingIds, scanner, visitedDirs, quickScan, 0, progress);
        await scanner.FlushAsync();
        progress?.Report((songs.Count, songs.Count, $"扫描完成，发现 {songs.Count} 首歌曲"));

        return (songs, new HashSet<string>(foundIds.Keys, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 递归扫描 SMB 单个目录，将其中的音频文件入库并继续扫描子目录。
    /// 使用 visitedDirs 集合防止循环引用，depth 限制最大递归深度。
    /// </summary>
    /// <param name="path">当前扫描的 SMB 目录路径。</param>
    /// <param name="profile">SMB 连接配置。</param>
    /// <param name="songs">累计扫描到的歌曲列表。</param>
    /// <param name="foundIds">本次扫描发现的所有文件路径 ID 集合。</param>
    /// <param name="existingIds">数据库中已存在的文件路径 ID 集合（用于增量扫描跳过）。</param>
    /// <param name="scanner">音乐扫描器实例。</param>
    /// <param name="visitedDirs">已访问目录集合（防止循环）。</param>
    /// <param name="depth">当前递归深度。</param>
    private async Task ScanSmbDirectoryAsync(string path, ConnectionProfile profile, List<Song> songs,
        System.Collections.Concurrent.ConcurrentDictionary<string, byte> foundIds, HashSet<string> existingIds, MusicScanner scanner, System.Collections.Concurrent.ConcurrentDictionary<string, byte> visitedDirs, bool quickScan = false, int depth = 0,
        IProgress<(int done, int total, string status)>? progress = null)
    {
        if (depth > MaxScanDepth) return;

        var normalizedDir = path.TrimEnd('/').TrimEnd('\\');
        if (string.IsNullOrEmpty(normalizedDir)) normalizedDir = "\\";
        if (!visitedDirs.TryAdd(normalizedDir, 0))
        {
            Log.Debug("NetworkMusicService", $"[SMB Scan] 跳过已访问目录: {path}");
            return;
        }

        List<RemoteFile> files;
        try
        {
            files = await _smb.ListFilesAsync(path);
        }
        catch (Exception ex)
        {
            Log.Debug("NetworkMusicService", $"[SMB Scan] 列出 {path} 失败: {ex.Message}");
            return;
        }

        Log.Debug("NetworkMusicService", $"[SMB Scan] 目录 {path} 有 {files.Count} 个条目 (depth={depth})");

        var audioFiles = new List<RemoteFile>();
        var subDirs = new List<RemoteFile>();
        // 收集同目录下的歌词文件（.lrc/.ttml），按父目录+文件名索引
        var lyricsMap = new Dictionary<string, RemoteFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (file.IsDirectory)
                subDirs.Add(file);
            else
            {
                var ext = System.IO.Path.GetExtension(file.Name)?.ToUpperInvariant() ?? "";
                if (string.IsNullOrEmpty(ext))
                    ext = System.IO.Path.GetExtension(file.Path)?.ToUpperInvariant() ?? "";
                if (IsAudioExtension(ext))
                    audioFiles.Add(file);
                else if (ext is ".LRC" or ".TTML")
                {
                    var nameNoExt = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? "";
                    if (!string.IsNullOrEmpty(nameNoExt))
                    {
                        var parentDir = System.IO.Path.GetDirectoryName(file.Path)?.Replace('\\', '/').TrimEnd('/') ?? "";
                        lyricsMap.TryAdd($"{parentDir}/{nameNoExt}", file);
                    }
                }
            }
        }

        // 并行扫描子目录（与 WebDAV 版本一致），上限 4 并发避免 SMB 协议压力
        if (subDirs.Count > 0)
        {
            var degree = Math.Min(4, subDirs.Count);
            using var sem = new SemaphoreSlim(degree, degree);
            var subTasks = subDirs.Select(async subDir =>
            {
                await sem.WaitAsync();
                try
                {
                    await ScanSmbDirectoryAsync(subDir.Path, profile, songs, foundIds, existingIds, scanner, visitedDirs, quickScan, depth + 1, progress);
                }
                catch (Exception ex)
                {
                    Log.Debug("NetworkMusicService", $"[SMB] 子目录扫描失败: {subDir.Path}, {ex.Message}");
                }
                finally { sem.Release(); }
            });
            await Task.WhenAll(subTasks);
        }

        if (audioFiles.Count == 0) return;

        progress?.Report((songs.Count, 0, $"正在扫描 {Path.GetFileName(path)} ({audioFiles.Count} 个音频文件)"));

        var smbProcessedCount = 0;
        var smbProgressLock = new object();
        var smbTotalAudio = audioFiles.Count;

        var metadataTasks = audioFiles.Select(file => Task.Run(async () =>
        {
            await ScanSemaphore.WaitAsync();
            try
            {
                foundIds.TryAdd(file.Path, 0);

                if (existingIds.Contains(file.Path))
                    return;

                var streamUrl = BuildSmbStreamUrl(file.Path, profile);
                var title = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? file.Name;
                if (string.IsNullOrEmpty(title))
                    title = System.IO.Path.GetFileNameWithoutExtension(file.Path) ?? file.Path;
                var song = new Song
                {
                    Title = title,
                    Artist = "",
                    Album = "",
                    FilePath = streamUrl,
                    Duration = 0,
                    FileSize = file.Size,
                    DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Source = SongSource.SMB,
                    Protocol = ProtocolType.SMB,
                    RemoteId = file.Path,
                    CoverArtPath = file.Path
                };

                // 匹配同目录下的外挂歌词文件
                var audioNameNoExt = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? "";
                var audioParentDir = System.IO.Path.GetDirectoryName(file.Path)?.Replace('\\', '/').TrimEnd('/') ?? "";
                if (!string.IsNullOrEmpty(audioNameNoExt) && lyricsMap.TryGetValue($"{audioParentDir}/{audioNameNoExt}", out var lrcFile))
                    song.LyricsPath = BuildSmbStreamUrl(lrcFile.Path, profile);

                // 扫描阶段不下载标签头取元数据（避免拖慢扫描），交由播放/取图时懒加载
                song.Artist = "未知艺术家";
                song.Album = "未知专辑";

                lock (songs)
                    songs.Add(song);
                await scanner.AddSongAsync(song);
            }
            finally
            {
                ScanSemaphore.Release();
                int done;
                lock (smbProgressLock)
                {
                    smbProcessedCount++;
                    done = smbProcessedCount;
                }
                if (progress != null && (done % 5 == 0 || done == smbTotalAudio))
                {
                    progress.Report((done, smbTotalAudio, $"正在扫描 {Path.GetFileName(path)} {done}/{smbTotalAudio}"));
                }
            }
        }));

        await Task.WhenAll(metadataTasks);
    }
}
