using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using System.Collections.Concurrent;

namespace CatClawMusic.Data;

/// <summary>网络音乐服务 —— partial 分域文件。</summary>
public partial class NetworkMusicService
{
    private async Task<(MemoryStream? stream, bool truncated)> DownloadHeadAsync(string remotePath, int headSize = TagHeadSizeLarge)
    {
        var head = await _webDav.OpenReadRangeAsync(remotePath, 0, headSize);
        if (head.Length > 0)
            return (new MemoryStream(head), head.Length >= headSize);

        try
        {
            using var stream = await _webDav.OpenReadAsync(remotePath);
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            return (ms, false);
        }
        catch { return (null, false); }
    }

    /// <summary>
    /// 下载 SMB 远程文件的头部数据用于标签解析，失败时回退到完整下载。
    /// </summary>
    /// <param name="remotePath">SMB 远程文件路径。</param>
    /// <param name="profile">连接配置，用于初始化 SMB 客户端。</param>
    /// <returns>包含文件头数据的内存流，失败时返回 null。</returns>
    private async Task<(MemoryStream? stream, bool truncated)> DownloadSmbHeadAsync(string remotePath, ConnectionProfile profile, int headSize = TagHeadSizeLarge)
    {
        _smb.Configure(profile);
        var head = await _smb.OpenReadRangeAsync(remotePath, 0, headSize);
        if (head.Length > 0)
            return (new MemoryStream(head), head.Length >= headSize);

        try
        {
            using var stream = await _smb.OpenReadAsync(remotePath);
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            return (ms, false);
        }
        catch { return (null, false); }
    }

    /// <summary>下载 WebDAV 远程文件尾部数据（Range 请求最后一段），用于 moov 在文件末尾的 M4A/MP4 元数据解析。</summary>
    private async Task<byte[]?> DownloadTailAsync(string remotePath, long fileSize)
    {
        var tailSize = Math.Min(TagTailSize, fileSize);
        if (tailSize <= 0) return null;
        var offset = fileSize - tailSize;
        var tail = await _webDav.OpenReadRangeAsync(remotePath, offset, tailSize);
        return tail.Length > 0 ? tail : null;
    }

    /// <summary>下载 SMB 远程文件尾部数据（Range 请求最后一段），用于 moov 在文件末尾的 M4A/MP4 元数据解析。</summary>
    private async Task<byte[]?> DownloadSmbTailAsync(string remotePath, ConnectionProfile profile, long fileSize)
    {
        var tailSize = Math.Min(TagTailSize, fileSize);
        if (tailSize <= 0) return null;
        var offset = fileSize - tailSize;
        var tail = await _smb.OpenReadRangeAsync(remotePath, offset, tailSize);
        return tail.Length > 0 ? tail : null;
    }

    /// <summary>判断远程路径是否为 M4A/MP4 家族格式（moov 可能位于文件末尾，需尾部解析）。</summary>
    private static bool IsM4aPath(string path)
    {
        var ext = Path.GetExtension(path)?.ToLowerInvariant();
        return ext is ".m4a" or ".mp4" or ".m4b";
    }

    /// <summary>
    /// 获取歌曲封面图流，按协议类型分发
    /// </summary>
    /// <param name="songId">歌曲 ID 或文件路径</param>
    /// <param name="profile">连接配置</param>
    /// <param name="coverPath">同目录侧车封面图远程路径（folder.jpg / cover.jpg 等）；非空时优先下载该图片，避免为整首音频抽帧。</param>
    /// <returns>封面图流，失败时返回 null</returns>
    public async Task<Stream?> GetCoverAsync(string songId, ConnectionProfile profile, string? coverPath = null)
    {
        if (profile.Protocol == ProtocolType.Navidrome)
        {
            var bytes = await _subsonic.GetCoverArtAsync(songId, profile);
            return bytes != null ? new MemoryStream(bytes) : null;
        }
        if (profile.Protocol == ProtocolType.WebDAV)
        {
            _webDav.Configure(profile);
            if (_webDav is WebDavService wdsEnsure) await wdsEnsure.EnsureDetectedAsync();

            Log.Debug("NetworkMusicService", $"[CatClaw] GetCoverAsync(WebDAV) songId={songId} coverPath={(coverPath ?? "null")}");

            // 懒探测侧车封面：已扫描老歌 RemoteCoverPath 为空时，临时列举父目录寻找同目录图片，
            // 无需重新扫描即可拿到封面，避免回退下载整首音频抽内嵌。
            var effectiveCover = coverPath;
            if (string.IsNullOrEmpty(effectiveCover))
                effectiveCover = await ProbeSideCarCoverAsync(songId, profile);

            // 优先：同目录侧车封面图（folder.jpg / cover.jpg 等），命中即返回，避免下载整首音频提取内嵌封面
            if (!string.IsNullOrEmpty(effectiveCover) && IsCoverImageExtension(effectiveCover))
            {
                try
                {
                    using var cs = await _webDav.OpenReadAsync(effectiveCover);
                    var cms = new MemoryStream();
                    await cs.CopyToAsync(cms);
                    cms.Position = 0;
                    if (cms.Length > 0) return cms;
                }
                catch (Exception ex)
                {
                    Log.Debug("NetworkMusicService", $"[CatClaw] WebDAV 侧车封面读取失败，回退内嵌: {ex.Message}");
                }
            }

            // OpenList: 使用 raw_url (CDN 直链) 下载文件头，WebDAV 端点 302 到 CDN 会拒绝 Basic Auth
            var isOpenList = (WebDavServerType)profile.ServerType == WebDavServerType.OpenList;
            if (!isOpenList && _webDav is WebDavService wdsCheck2 && wdsCheck2.CurrentServerType == WebDavServerType.OpenList)
                isOpenList = true;

            if (isOpenList && _webDav is WebDavService openListService)
            {
                try
                {
                    // 优先复用播放用的 /d/ URL（已缓存，无需额外 API 调用）
                    string? downloadUrl = null;
                    if (_streamUrlCache.TryGetValue(songId, out var cached) && cached.expiry > DateTime.UtcNow)
                    {
                        downloadUrl = cached.url;
                        Log.Debug("NetworkMusicService", "[CatClaw] Cover: 复用播放缓存 URL");
                    }

                    // 缓存不可用：获取 CDN raw_url
                    if (string.IsNullOrEmpty(downloadUrl))
                        downloadUrl = await openListService.GetOpenListDownloadUrlAsync(songId);

                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        var rangeReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                        rangeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, TagHeadSize - 1);
                        var rangeResp = await OpenListCoverClient.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead);
                        if (rangeResp.IsSuccessStatusCode)
                        {
                            var ms = new MemoryStream();
                            await rangeResp.Content.CopyToAsync(ms);
                            ms.Position = 0;
                            try
                            {
                                // 非音频头（损坏/伪装文件）直接放弃，避免 TagLib 异常刷屏
                                if (IsAudioHeader(ms))
                                {
                                    var coverBytes = TagReader.ExtractCoverFromStream(ms, songId);
                                    if (coverBytes != null)
                                        return new MemoryStream(coverBytes);
                                }
                            }
                            finally { ms.Dispose(); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("NetworkMusicService", $"[CatClaw] OpenList 封面提取失败: {ex.Message}");
                }
            }

            try
            {
                var (ms, truncated) = await DownloadHeadAsync(songId);
                if (ms != null)
                {
                    try
                    {
                        // 非音频头：放弃提取与整首兜底（头都坏了整首更不可能有封面）
                        if (IsAudioHeader(ms))
                        {
                            var coverBytes = TagReader.ExtractCoverFromStream(ms, songId);
                            if (coverBytes != null)
                            {
                                Log.Debug("NetworkMusicService", $"[CatClaw] WebDAV 内嵌封面提取成功 ({coverBytes.Length} 字节)");
                                return new MemoryStream(coverBytes);
                            }
                            // 头部未抽到封面且文件被截断（封面可能超过头部范围）→ 整首下载兜底
                            if (truncated)
                            {
                                Log.Debug("NetworkMusicService", $"[CatClaw] WebDAV 头部无内嵌封面，整首下载兜底: {songId}");
                                ms.Dispose();
                                using var full = await _webDav.OpenReadAsync(songId);
                                var fms = new MemoryStream();
                                await full.CopyToAsync(fms);
                                fms.Position = 0;
                                var fullCover = TagReader.ExtractCoverFromStream(fms, songId);
                                if (fullCover != null)
                                {
                                    Log.Debug("NetworkMusicService", $"[CatClaw] WebDAV 整首兜底提取封面成功 ({fullCover.Length} 字节)");
                                    return new MemoryStream(fullCover);
                                }
                            }
                        }
                    }
                    finally { ms.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("NetworkMusicService", $"[CatClaw] WebDAV 封面提取失败: {ex.Message}");
            }
        }
        if (profile.Protocol == ProtocolType.SMB)
        {
            // 懒探测侧车封面（同 WebDAV）：命中同目录图片则直接下载返回，跳过内嵌抽取
            var smbCover = coverPath;
            if (string.IsNullOrEmpty(smbCover))
                smbCover = await ProbeSideCarCoverAsync(songId, profile);
            if (!string.IsNullOrEmpty(smbCover) && IsCoverImageExtension(smbCover))
            {
                try
                {
                    _smb.Configure(profile);
                    using var cs = await _smb.OpenReadAsync(smbCover);
                    var cms = new MemoryStream();
                    await cs.CopyToAsync(cms);
                    cms.Position = 0;
                    if (cms.Length > 0) return cms;
                }
                catch (Exception ex)
                {
                    Log.Debug("NetworkMusicService", $"[CatClaw] SMB 侧车封面读取失败，回退内嵌: {ex.Message}");
                }
            }

            try
            {
                var (ms, truncated) = await DownloadSmbHeadAsync(songId, profile);
                if (ms != null)
                {
                    try
                    {
                        // 非音频头：放弃提取与整首兜底
                        if (IsAudioHeader(ms))
                        {
                            var coverBytes = TagReader.ExtractCoverFromStream(ms, songId);
                            if (coverBytes != null)
                            {
                                Log.Debug("NetworkMusicService", $"[CatClaw] SMB 内嵌封面提取成功 ({coverBytes.Length} 字节)");
                                return new MemoryStream(coverBytes);
                            }
                            if (truncated)
                            {
                                Log.Debug("NetworkMusicService", $"[CatClaw] SMB 头部无内嵌封面，整首下载兜底: {songId}");
                                ms.Dispose();
                                _smb.Configure(profile);
                                using var full = await _smb.OpenReadAsync(songId);
                                var fms = new MemoryStream();
                                await full.CopyToAsync(fms);
                                fms.Position = 0;
                                var fullCover = TagReader.ExtractCoverFromStream(fms, songId);
                                if (fullCover != null)
                                {
                                    Log.Debug("NetworkMusicService", $"[CatClaw] SMB 整首兜底提取封面成功 ({fullCover.Length} 字节)");
                                    return new MemoryStream(fullCover);
                                }
                            }
                        }
                    }
                    finally { ms.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("NetworkMusicService", $"[CatClaw] SMB 封面提取失败: {ex.Message}");
            }
        }
        Log.Debug("NetworkMusicService", $"[CatClaw] GetCoverAsync 返回 null (songId={songId})");
        return null;
    }

    /// <summary>
    /// 侧车封面懒探测缓存：键=音频远程路径，值=探测到的侧车封面远程路径（null 表示无）。
    /// 避免同一首歌在会话内重复列举父目录。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> _sideCarCoverCache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 播放/取封面时按需探测同目录侧车封面：列举音频文件父目录，按优先级
    /// （folder &gt; cover &gt; front &gt; album &gt; 同名图）选出封面图，返回其远程路径。
    /// 扫描阶段不再写入 RemoteCoverPath（避免拖慢扫描），封面统一在此处懒探测，
    /// 命中后缓存，无需重新扫描即可拿到侧车封面。
    /// </summary>
    /// <param name="songId">音频文件远程路径（WebDAV 用 '/'，SMB 用 '\'）</param>
    /// <param name="profile">连接配置</param>
    /// <returns>侧车封面远程路径；无可用封面时返回 null</returns>
    private async Task<string?> ProbeSideCarCoverAsync(string songId, ConnectionProfile profile)
    {
        if (string.IsNullOrEmpty(songId)) return null;
        if (_sideCarCoverCache.TryGetValue(songId, out var cached))
            return cached;

        string? result = null;
        try
        {
            // 归一化父目录（兼容 WebDAV 的 '/' 与 SMB 的 '\'）
            var normalized = songId.Replace('\\', '/').TrimEnd('/');
            var parentDir = System.IO.Path.GetDirectoryName(normalized)?.Replace('\\', '/').TrimEnd('/') ?? "";
            if (string.IsNullOrEmpty(parentDir)) return null;
            var audioNameNoExt = System.IO.Path.GetFileNameWithoutExtension(normalized) ?? "";

            System.Collections.Generic.List<RemoteFile>? files = null;
            if (profile.Protocol == ProtocolType.WebDAV && _webDav is WebDavService wds)
            {
                wds.Configure(profile);
                await wds.EnsureDetectedAsync();
                files = await _webDav.ListFilesAsync(parentDir);
            }
            else if (profile.Protocol == ProtocolType.SMB && _smb is SmbService smb)
            {
                smb.Configure(profile);
                files = await _smb.ListFilesAsync(parentDir);
            }
            if (files == null) return null;

            BuildCoverMaps(files, out var exactCoverMap, out var dirCoverMap);
            // 优先：与音频同名的图片（如 song.jpg 配 song.flac）
            if (!string.IsNullOrEmpty(audioNameNoExt)
                && exactCoverMap.TryGetValue($"{parentDir}/{audioNameNoExt}", out var exact))
                result = exact.Path;
            // 回退：目录级通用封面（folder/cover/front/album...）
            else if (dirCoverMap.TryGetValue(parentDir, out var dir))
                result = dir.Path;
        }
        catch (Exception ex)
        {
            Log.Debug("NetworkMusicService", $"[CatClaw] 懒探测侧车封面失败 ({profile.Protocol} {songId}): {ex.Message}");
        }
        finally
        {
            _sideCarCoverCache.TryAdd(songId, result);
        }
        return result;
    }

    /// <summary>
    /// 获取远程歌曲歌词，优先查找外部 .lrc 文件，回退到嵌入标签
    /// </summary>
    /// <param name="remotePath">远程文件路径</param>
    /// <param name="profile">连接配置</param>
    /// <returns>歌词文本，失败时返回 null</returns>
    public async Task<string?> GetLyricsAsync(string remotePath, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.Navidrome)
        {
            // Navidrome: 通过 Subsonic API getLyricsBySongId 获取歌词
            // remotePath 对于 Navidrome 歌曲实际上是 stream URL，使用 RemoteId 作为 songId
            return await _subsonic.GetLyricsAsync(remotePath, profile);
        }
        if (profile.Protocol == ProtocolType.WebDAV)
        {
            _webDav.Configure(profile);
            if (_webDav is WebDavService wdsLyrics) await wdsLyrics.EnsureDetectedAsync();

            var lastDot = remotePath.LastIndexOf('.');
            if (lastDot > 0)
            {
                var lrcPath = remotePath.Substring(0, lastDot) + ".lrc";
                try
                {
                    using var lrcStream = await _webDav.OpenReadAsync(lrcPath);
                    using var reader = new StreamReader(lrcStream);
                    var lrcText = await reader.ReadToEndAsync();
                    Log.Debug("NetworkMusicService", $"[WebDAV] 读取歌词文件 {lrcPath}，长度={lrcText?.Length ?? 0}，前200字符={lrcText?[..Math.Min(200, lrcText?.Length ?? 0)]?.Replace('\n', ' ')}");
                    if (!string.IsNullOrWhiteSpace(lrcText))
                        return lrcText;
                }
                catch { }
            }

            try
            {
                var (ms, _) = await DownloadHeadAsync(remotePath);
                if (ms != null)
                {
                    try
                    {
                        return TagReader.ReadEmbeddedLyricsFromStream(ms, remotePath);
                    }
                    finally { ms.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("NetworkMusicService", $"[CatClaw] WebDAV 歌词提取失败: {ex.Message}");
            }
        }
        if (profile.Protocol == ProtocolType.SMB)
        {
            _smb.Configure(profile);
            var lastDot = remotePath.LastIndexOf('.');
            if (lastDot > 0)
            {
                var lrcPath = remotePath.Substring(0, lastDot) + ".lrc";
                try
                {
                    using var lrcStream = await _smb.OpenReadAsync(lrcPath);
                    using var reader = new StreamReader(lrcStream);
                    var lrcText = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(lrcText))
                        return lrcText;
                }
                catch { }
            }
            try
            {
                var (ms, _) = await DownloadSmbHeadAsync(remotePath, profile);
                if (ms != null)
                {
                    try
                    {
                        return TagReader.ReadEmbeddedLyricsFromStream(ms, remotePath);
                    }
                    finally { ms.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("NetworkMusicService", $"[CatClaw] SMB 歌词提取失败: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// 从远程文件头部数据中解析歌曲元数据，更新歌曲信息
    /// </summary>
    /// <param name="song">待更新元数据的歌曲对象</param>
    /// <param name="profile">连接配置</param>
    /// <returns>更新后的歌曲对象，失败时返回 null</returns>
    public async Task<Song?> FetchSongMetadataAsync(Song song, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.WebDAV)
        {
            var result = await FetchWebDavMetadataAsync(song, profile);
            if (result != null) return result;
        }
        if (profile.Protocol == ProtocolType.SMB)
        {
            var result = await FetchSmbMetadataAsync(song, profile);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// 回填网络歌曲元数据：找到所有缺少元数据的歌曲（快速扫描入库的），
    /// 有界并行从远程服务器下载标签信息，分批单事务入库。
    /// 性能：SQL 直接筛目标（不再全量加载详情表）；固定 worker 数并行（旧版为每首歌
    /// 创建一个 Task，随库规模线性膨胀）；结果分批事务提交（旧版逐首 SaveSongAsync）。
    /// </summary>
    public async Task BackfillMetadataAsync(ConnectionProfile profile,
        IProgress<(int done, int total, string status)>? progress = null,
        CancellationToken ct = default)
    {
        try { await _db.EnsureInitializedAsync(); } catch { }

        var source = profile.Protocol switch
        {
            ProtocolType.SMB => SongSource.SMB,
            ProtocolType.WebDAV => SongSource.WebDAV,
            ProtocolType.Navidrome => SongSource.WebDAV,
            _ => (SongSource?)null
        };
        if (source == null) return;

        List<Song> needsBackfill;
        try
        {
            needsBackfill = await _db.GetNetworkSongsMissingMetadataAsync(source.Value);
        }
        catch (Exception ex)
        {
            Log.Debug("NetworkMusicService", $"[CatClaw] 查询待回填歌曲失败: {ex.Message}");
            return;
        }

        if (needsBackfill.Count == 0) return;

        var total = needsBackfill.Count;
        var done = 0;
        progress?.Report((0, total, $"正在补全元数据 0/{total}"));

        var updated = new ConcurrentBag<Song>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct };
        try
        {
            await Parallel.ForEachAsync(needsBackfill, options, async (song, token) =>
            {
                try
                {
                    var tagged = await FetchSongMetadataAsync(song, profile);
                    if (tagged != null)
                    {
                        if (!string.IsNullOrWhiteSpace(tagged.Title) && tagged.Title != song.Title)
                            song.Title = tagged.Title;
                        if (!string.IsNullOrWhiteSpace(tagged.Artist) && tagged.Artist != "未知艺术家")
                            song.Artist = tagged.Artist;
                        if (!string.IsNullOrWhiteSpace(tagged.Album) && tagged.Album != "未知专辑")
                            song.Album = tagged.Album;
                        if (tagged.Duration > 0) song.Duration = tagged.Duration;
                        if (tagged.Bitrate > 0) song.Bitrate = tagged.Bitrate;
                        if (tagged.Year > 0) song.Year = tagged.Year;
                        if (tagged.TrackNumber > 0) song.TrackNumber = tagged.TrackNumber;
                        song.Genre = tagged.Genre;
                        updated.Add(song);
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("NetworkMusicService", $"[CatClaw] 元数据回填失败: {song.FilePath}, {ex.Message}");
                }
                finally
                {
                    var d = Interlocked.Increment(ref done);
                    if (progress != null && (d % 10 == 0 || d == total))
                        progress.Report((d, total, $"正在补全元数据 {d}/{total}"));
                }
            });
        }
        catch (OperationCanceledException) { }

        // 分批单事务落库（含 ArtistId/AlbumId/SongArtists 持久化，修复旧版
        // "只更新内存字符串、重启后未知艺术家复活"的问题）
        foreach (var batch in updated.Chunk(50))
        {
            if (ct.IsCancellationRequested) break;
            await PersistBackfilledSongsAsync(batch.ToList());
        }

        progress?.Report((total, total, ct.IsCancellationRequested
            ? $"已跳过补全（{done}/{total}）"
            : $"元数据补全完成，共 {total} 首"));
    }

    /// <summary>把回填结果持久化：批量 Ensure 艺术家/专辑并回写 ArtistId/AlbumId/SongArtists，歌曲行单事务批量更新</summary>
    private async Task PersistBackfilledSongsAsync(List<Song> batch)
    {
        if (batch.Count == 0) return;
        try
        {
            // 1. 批量建立艺术家（拆分多艺术家名）
            var nameToId = new Dictionary<string, int>(StringComparer.Ordinal);
            var allNames = batch
                .SelectMany(s => MusicUtility.SplitArtistNames(s.Artist))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (allNames.Count > 0)
            {
                var artistMap = await _db.EnsureArtistsBatchAsync(allNames);
                foreach (var kv in artistMap) nameToId[kv.Key] = kv.Value;
            }

            // 2. 批量建立专辑（主艺术家 + 专辑标题）
            var albumMap = new Dictionary<(string title, int artistId), int>();
            var albumKeys = new List<(string title, int artistId)>();
            foreach (var s in batch)
            {
                if (string.IsNullOrWhiteSpace(s.Album) || s.Album == "未知专辑") continue;
                var primary = MusicUtility.SplitArtistNames(s.Artist).FirstOrDefault() ?? "";
                if (!nameToId.TryGetValue(primary, out var artistId) || artistId <= 0) continue;
                var key = (s.Album, artistId);
                if (!albumKeys.Contains(key)) albumKeys.Add(key);
            }
            if (albumKeys.Count > 0)
            {
                var ensured = await _db.EnsureAlbumsBatchAsync(albumKeys);
                foreach (var kv in ensured) albumMap[kv.Key] = kv.Value;
            }

            // 3. 单事务：更新歌曲行 + 重建 SongArtists 关联
            await _db.RunInTransactionAsync(tran =>
            {
                foreach (var s in batch)
                {
                    var names = MusicUtility.SplitArtistNames(s.Artist);
                    if (names.Count > 0 && nameToId.TryGetValue(names[0], out var aid) && aid > 0)
                        s.ArtistId = aid;
                    if (!string.IsNullOrWhiteSpace(s.Album) && s.Album != "未知专辑" && s.ArtistId > 0
                        && albumMap.TryGetValue((s.Album, s.ArtistId), out var alid) && alid > 0)
                        s.AlbumId = alid;

                    tran.Update(s);

                    if (s.ArtistId > 0)
                    {
                        tran.Execute("DELETE FROM SongArtists WHERE SongId = ?", s.Id);
                        var ids = names
                            .Select(n => nameToId.TryGetValue(n, out var id) ? id : 0)
                            .Where(id => id > 0)
                            .Distinct()
                            .ToList();
                        if (!ids.Contains(s.ArtistId)) ids.Insert(0, s.ArtistId);
                        foreach (var id in ids)
                            tran.Execute("INSERT INTO SongArtists (SongId, ArtistId) VALUES (?, ?)", s.Id, id);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Log.Debug("NetworkMusicService", $"[CatClaw] 回填结果批量入库失败: {ex.Message}");
        }
    }

    /// <summary>判断解析出的标签是否含有效元数据（自适应标签头重试的判定依据）。</summary>
    private static bool HasUsefulMetadata(Song tagSong)
    {
        return (!string.IsNullOrWhiteSpace(tagSong.Artist) && tagSong.Artist != "未知艺术家")
            || (!string.IsNullOrWhiteSpace(tagSong.Album) && tagSong.Album != "未知专辑")
            || tagSong.Duration > 0;
    }

    /// <summary>
    /// 快速判定文件头是否为已知音频格式（magic bytes）。
    /// 用于网络元数据补全：对损坏/截断/伪装成音频的远程文件（半截下载、加密文件等），
    /// 在 TagLib 解析前直接判定跳过，避免大量 CorruptFileException 抛出（第一机会异常刷屏 +
    /// 异常栈分配导致的 GC 压力），同时省掉"小头失败再下大头"的重复请求。
    /// </summary>
    private static bool IsAudioHeader(Stream s)
    {
        try
        {
            if (s == null || !s.CanRead) return false;
            var pos = s.CanSeek ? s.Position : 0;
            if (s.CanSeek) s.Position = 0;
            Span<byte> h = stackalloc byte[16];
            int n = s.Read(h);
            if (s.CanSeek) s.Position = pos;
            if (n < 4) return false;

            // ID3v2 头（MP3/AAC 等）
            if (h[0] == (byte)'I' && h[1] == (byte)'D' && h[2] == (byte)'3') return true;
            // MP3 裸帧同步（11 位全 1：0xFF 0xE0~0xFF）
            if (h[0] == 0xFF && (h[1] & 0xE0) == 0xE0) return true;
            // FLAC
            if (h[0] == (byte)'f' && h[1] == (byte)'L' && h[2] == (byte)'a' && h[3] == (byte)'C') return true;
            // Ogg（Vorbis/Opus/FLAC-in-Ogg）
            if (h[0] == (byte)'O' && h[1] == (byte)'g' && h[2] == (byte)'g' && h[3] == (byte)'S') return true;
            // M4A/MP4（offset 4 处 ftyp）
            if (n >= 8 && h[4] == (byte)'f' && h[5] == (byte)'t' && h[6] == (byte)'y' && h[7] == (byte)'p') return true;
            // WAV（RIFF...WAVE）
            if (n >= 12 && h[0] == (byte)'R' && h[1] == (byte)'I' && h[2] == (byte)'F' && h[3] == (byte)'F'
                && h[8] == (byte)'W' && h[9] == (byte)'A' && h[10] == (byte)'V' && h[11] == (byte)'E') return true;
            // APE
            if (h[0] == (byte)'M' && h[1] == (byte)'A' && h[2] == (byte)'C' && h[3] == (byte)' ') return true;
            // WavPack
            if (h[0] == (byte)'w' && h[1] == (byte)'v' && h[2] == (byte)'p' && h[3] == (byte)'k') return true;
            // AIFF（FORM...AIFF）
            if (n >= 12 && h[0] == (byte)'F' && h[1] == (byte)'O' && h[2] == (byte)'R' && h[3] == (byte)'M'
                && h[8] == (byte)'A' && h[9] == (byte)'I' && h[10] == (byte)'F' && h[11] == (byte)'F') return true;
            // DSD
            if (h[0] == (byte)'D' && h[1] == (byte)'S' && h[2] == (byte)'D' && h[3] == (byte)' ') return true;
            // MusePack
            if (h[0] == (byte)'M' && h[1] == (byte)'P' && h[2] == (byte)'C' && h[3] == (byte)'K') return true;

            return false;
        }
        catch { return false; }
    }

    /// <summary>这些格式的标签头可能很大（内嵌封面/长注释），小头解析失败时需用大头重试。</summary>
    private static bool MayHaveLargeTagHeader(string path)
    {
        var ext = Path.GetExtension(path)?.ToLowerInvariant();
        return ext is ".flac" or ".ape" or ".wav" or ".aiff" or ".aif" or ".dsf" or ".wv";
    }

    /// <summary>
    /// 将解析出的标签字段应用到目标歌曲（ consolidates 原 WebDAV/SMB 两处重复逻辑）。
    /// decodeTitle 为 true 时对标题做 URL 解码（WebDAV 远程标题可能带百分号编码）。
    /// </summary>
    private static void ApplyTagToSong(Song song, Song tagSong, bool decodeTitle)
    {
        if (!string.IsNullOrWhiteSpace(tagSong.Title) && tagSong.Title != song.Title)
        {
            if (decodeTitle)
            {
                var decoded = Uri.UnescapeDataString(tagSong.Title);
                song.Title = decoded != song.Title ? decoded : song.Title;
            }
            else
            {
                song.Title = tagSong.Title;
            }
        }
        song.Artist = !string.IsNullOrWhiteSpace(tagSong.Artist) && tagSong.Artist != "未知艺术家" ? tagSong.Artist : song.Artist;
        // 规范化艺术家名：拆分多值字符串，取第一位作为主艺术家
        if (!string.IsNullOrWhiteSpace(song.Artist))
        {
            var artistNames = MusicUtility.SplitArtistNames(song.Artist);
            if (artistNames.Count > 0)
                song.Artist = artistNames[0];
        }
        song.Album = !string.IsNullOrWhiteSpace(tagSong.Album) && tagSong.Album != "未知专辑" ? tagSong.Album : song.Album;
        song.Duration = tagSong.Duration > 0 ? tagSong.Duration : song.Duration;
        song.Bitrate = tagSong.Bitrate > 0 ? tagSong.Bitrate : song.Bitrate;
        song.Year = tagSong.Year > 0 ? tagSong.Year : song.Year;
        song.TrackNumber = tagSong.TrackNumber > 0 ? tagSong.TrackNumber : song.TrackNumber;
        song.Genre = tagSong.Genre;
    }

    /// <summary>
    /// 从 WebDAV 远程文件头部数据中解析歌曲元数据（标题、艺术家、专辑、时长等）并更新歌曲对象。
    /// 内部会自动处理 OpenList 服务器类型检测与缓存。
    /// </summary>
    /// <param name="song">待更新元数据的歌曲对象，需包含 RemoteId 或 CoverArtPath。</param>
    /// <param name="profile">WebDAV 连接配置。</param>
    /// <returns>更新后的歌曲对象；解析失败或无远程路径时返回 null。</returns>
    private async Task<Song?> FetchWebDavMetadataAsync(Song song, ConnectionProfile profile)
    {
        var remotePath = song.RemoteId ?? song.CoverArtPath;
        if (string.IsNullOrEmpty(remotePath)) return null;

        _webDav.Configure(profile);
        if (_webDav is WebDavService wdsMeta) await wdsMeta.EnsureDetectedAsync();

        var decodedRemotePath = Uri.UnescapeDataString(remotePath);
        // 自适应标签头：易有大标签头的格式（FLAC 等）先小头快读，无有效元数据再用大头重试。
        var headSizes = MayHaveLargeTagHeader(remotePath)
            ? new[] { TagHeadSize, TagHeadSizeLarge }
            : new[] { TagHeadSize };

        Song? bestTag = null;
        foreach (var headSize in headSizes)
        {
            try
            {
                var (ms, _) = await DownloadHeadAsync(remotePath, headSize);
                if (ms == null) continue;
                try
                {
                    // 非音频头（损坏/截断/伪装文件）：直接放弃，不再下载大头重试，
                    // 避免 TagLib 抛 CorruptFileException 刷屏与异常 GC 压力
                    if (!IsAudioHeader(ms)) break;

                    var tagSong = TagReader.ReadFromStream(ms, song.FilePath, decodedRemotePath, song.FileSize);
                    if (tagSong != null)
                    {
                        bestTag = tagSong;
                        if (HasUsefulMetadata(tagSong)) break; // 已拿到有效元数据，无需更大头
                    }
                }
                finally { ms.Dispose(); }
            }
            catch (Exception ex)
            {
                Log.Debug("NetworkMusicService", $"[CatClaw] WebDAV 元数据获取失败(head={headSize}): {ex.Message}");
            }
        }

        if (bestTag != null && HasUsefulMetadata(bestTag))
        {
            ApplyTagToSong(song, bestTag, decodeTitle: true);
            return song;
        }

        // 头部解析无有效元数据：非 faststart M4A/MP4 的 moov（标签+时长）在文件末尾，
        // 256KB/2MB 头内找不到（TagLib 抛 CorruptFileException），Range 下载尾部手动解析
        if (IsM4aPath(decodedRemotePath) && song.FileSize > 0)
        {
            try
            {
                var tail = await DownloadTailAsync(remotePath, song.FileSize);
                if (tail != null)
                {
                    var meta = M4aMetadataReader.ReadAllFromTail(tail, song.FileSize);
                    if (meta != null
                        && (meta.Title != null || meta.Artist != null || meta.Album != null || meta.DurationSeconds > 0))
                    {
                        ApplyTagToSong(song, new Song
                        {
                            Title = meta.Title,
                            Artist = meta.Artist ?? "未知艺术家",
                            Album = meta.Album ?? "未知专辑",
                            Duration = meta.DurationSeconds,
                            Bitrate = meta.Bitrate
                        }, decodeTitle: true);
                        Log.Debug("NetworkMusicService", $"[CatClaw] WebDAV M4A 尾部解析成功: {decodedRemotePath}");
                        return song;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("NetworkMusicService", $"[CatClaw] WebDAV M4A 尾部解析失败: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// 从 SMB 远程文件头部数据中解析歌曲元数据并更新歌曲对象。
    /// </summary>
    /// <param name="song">待更新元数据的歌曲对象，需包含 RemoteId 或 CoverArtPath。</param>
    /// <param name="profile">SMB 连接配置。</param>
    /// <returns>更新后的歌曲对象；解析失败或无远程路径时返回 null。</returns>
    private async Task<Song?> FetchSmbMetadataAsync(Song song, ConnectionProfile profile)
    {
        var remotePath = song.RemoteId ?? song.CoverArtPath;
        if (string.IsNullOrEmpty(remotePath)) return null;

        _smb.Configure(profile);
        // 自适应标签头：易有大标签头的格式（FLAC 等）先小头快读，无有效元数据再用大头重试。
        var headSizes = MayHaveLargeTagHeader(remotePath)
            ? new[] { TagHeadSize, TagHeadSizeLarge }
            : new[] { TagHeadSize };

        Song? bestTag = null;
        foreach (var headSize in headSizes)
        {
            try
            {
                var (ms, _) = await DownloadSmbHeadAsync(remotePath, profile, headSize);
                if (ms == null) continue;
                try
                {
                    // 非音频头：直接放弃，不再下载大头重试（同上，避免 TagLib 异常刷屏）
                    if (!IsAudioHeader(ms)) break;

                    var tagSong = TagReader.ReadFromStream(ms, song.FilePath, remotePath, song.FileSize);
                    if (tagSong != null)
                    {
                        bestTag = tagSong;
                        if (HasUsefulMetadata(tagSong)) break; // 已拿到有效元数据，无需更大头
                    }
                }
                finally { ms.Dispose(); }
            }
            catch (Exception ex)
            {
                Log.Debug("NetworkMusicService", $"[CatClaw] SMB 元数据获取失败(head={headSize}): {ex.Message}");
            }
        }

        if (bestTag != null && HasUsefulMetadata(bestTag))
        {
            ApplyTagToSong(song, bestTag, decodeTitle: false);
            return song;
        }

        // 头部解析无有效元数据：非 faststart M4A/MP4 的 moov（标签+时长）在文件末尾，
        // Range 下载尾部手动解析（同 WebDAV 逻辑）
        if (IsM4aPath(remotePath) && song.FileSize > 0)
        {
            try
            {
                var tail = await DownloadSmbTailAsync(remotePath, profile, song.FileSize);
                if (tail != null)
                {
                    var meta = M4aMetadataReader.ReadAllFromTail(tail, song.FileSize);
                    if (meta != null
                        && (meta.Title != null || meta.Artist != null || meta.Album != null || meta.DurationSeconds > 0))
                    {
                        ApplyTagToSong(song, new Song
                        {
                            Title = meta.Title,
                            Artist = meta.Artist ?? "未知艺术家",
                            Album = meta.Album ?? "未知专辑",
                            Duration = meta.DurationSeconds,
                            Bitrate = meta.Bitrate
                        }, decodeTitle: false);
                        Log.Debug("NetworkMusicService", $"[CatClaw] SMB M4A 尾部解析成功: {remotePath}");
                        return song;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("NetworkMusicService", $"[CatClaw] SMB M4A 尾部解析失败: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// 获取歌曲流 URL，按协议类型构建对应的播放地址
    /// </summary>
    /// <param name="song">歌曲对象</param>
    /// <param name="profile">连接配置</param>
    /// <returns>流播放 URL</returns>
}
