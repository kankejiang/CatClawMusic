using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>正在播放 ViewModel —— partial 分域文件。</summary>
public partial class NowPlayingViewModel
{
    private async Task PreBufferNextSongAsync(Song nextSong)
    {
        try
        {
            Log.Debug("AppViewModels", $"[PreBuffer] 开始预缓冲: {nextSong.Title}");

            // 1. 下载音频到本地缓存（仅网络歌曲）——流式落盘,不再整首进内存
            if (nextSong.Source != SongSource.Local && !AudioCacheService.Instance.IsCached(nextSong.FilePath))
            {
                await AudioCacheService.Instance.CacheAsync(
                    nextSong.FilePath,
                    async (url, token) =>
                    {
                        // 通过 URL 转换器获取可下载的 HTTP URL
                        var proxyUrl = AudioPlayerService.UrlTransformer?.Invoke(url);
                        if (string.IsNullOrEmpty(proxyUrl)) return null;
                        HttpResponseMessage? resp = null;
                        try
                        {
                            resp = await SharedHttpClient.GetAsync(proxyUrl, HttpCompletionOption.ResponseHeadersRead, token);
                            resp.EnsureSuccessStatusCode();
                            return await resp.Content.ReadAsStreamAsync(token);
                        }
                        catch
                        {
                            resp?.Dispose();
                            return null;
                        }
                    });
            }

            // 2. 预取元数据（如果缺少）
            if (nextSong.Duration <= 0 || nextSong.Artist == "未知艺术家" || string.IsNullOrWhiteSpace(nextSong.Artist))
            {
                await FetchAndUpdateSongMetadataAsync(nextSong);
            }

            Log.Debug("AppViewModels", $"[PreBuffer] 预缓冲完成: {nextSong.Title}");
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[PreBuffer] 预缓冲失败: {nextSong.Title}, {ex.Message}");
        }
    }

    /// <summary>
    /// 获取歌曲元数据并更新数据库和 UI。
    /// 在播放时由 LoadCurrentSongAsync 的后台任务调用（对网络歌曲补充元数据）。
    /// </summary>
    private async Task FetchAndUpdateSongMetadataAsync(Song song)
    {
        if (_networkMusic == null) return;
        if (song.Source == SongSource.Local) return;

        try
        {
            // 查找对应的连接配置
            var profiles = await _networkMusic.GetProfilesAsync();
            var profile = profiles.FirstOrDefault(p =>
                (p.Protocol == ProtocolType.SMB && song.Source == SongSource.SMB) ||
                (p.Protocol == ProtocolType.WebDAV && song.Source == SongSource.WebDAV));
            if (profile == null) return;

            var tagged = await _networkMusic.FetchSongMetadataAsync(song, profile);
            if (tagged == null) return;

            // 更新 song 对象
            bool changed = false;
            if (!string.IsNullOrWhiteSpace(tagged.Title) && tagged.Title != song.Title)
            { song.Title = tagged.Title; changed = true; }
            if (!string.IsNullOrWhiteSpace(tagged.Artist) && tagged.Artist != "未知艺术家" && tagged.Artist != song.Artist)
            { song.Artist = tagged.Artist; changed = true; }
            if (!string.IsNullOrWhiteSpace(tagged.Album) && tagged.Album != "未知专辑" && tagged.Album != song.Album)
            { song.Album = tagged.Album; changed = true; }
            if (tagged.Duration > 0 && song.Duration <= 0)
            { song.Duration = tagged.Duration; changed = true; }
            if (tagged.Year > 0) song.Year = tagged.Year;
            if (tagged.TrackNumber > 0) song.TrackNumber = tagged.TrackNumber;
            song.Genre = tagged.Genre;

            if (changed)
            {
                await _db.SaveSongAsync(song);
                // 列表页缓存失效：播放时补全的元数据让"网络音乐"列表立即可见
                AllSongsViewModel.InvalidateCache();

                // 如果正在播放这首歌，更新 UI
                if (_loadedSongId == song.Id)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        Title = song.Title ?? "未知歌曲";
                        Artist = song.Artist ?? "未知艺术家";
                        Album = song.Album ?? "未知专辑";
                        HasAlbum = !string.IsNullOrEmpty(song.Album) && song.Album != "未知专辑";
                        if (song.Duration > 1000)
                        {
                            Duration = song.Duration / 1000.0;
                            TotalTimeDisplay = FormatTime(Duration);
                        }
                    });
                }
            }

            // 自动分类：确保艺术家和专辑记录存在（后台）
            if (changed && !string.IsNullOrWhiteSpace(song.Artist))
            {
                var artistNames = MusicUtility.SplitArtistNames(song.Artist);
                if (artistNames.Count > 0)
                {
                    var artistId = await _db.EnsureArtistAsync(artistNames[0]);
                    if (artistId > 0) song.ArtistId = artistId;
                }
            }
            if (changed && !string.IsNullOrWhiteSpace(song.Album) && song.Album != "未知专辑" && song.ArtistId > 0)
            {
                var albumId = await _db.EnsureAlbumAsync(song.Album, song.ArtistId);
                if (albumId > 0) song.AlbumId = albumId;
            }
            if (changed) await _db.SaveSongAsync(song);
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[NowPlayingVM] 元数据获取失败: {song.Title}, {ex.Message}");
        }
    }

    // === 网络歌曲 → 本地缓存 → 本地文件处理 ===

    /// <summary>
    /// 将网络歌曲解析为本地文件路径（通过缓存）。
    /// 已缓存直接返回；未缓存则通过代理下载整个文件到本地。
    /// </summary>
    private async Task<string?> ResolveToLocalPathAsync(Song song, CancellationToken ct)
    {
        // 已缓存：直接返回
        var cached = AudioCacheService.Instance.GetCachedPath(song.FilePath);
        if (cached != null) return cached;

        // 获取可下载的 HTTP URL
        string? downloadUrl = AudioPlayerService.UrlTransformer?.Invoke(song.FilePath);
        if (string.IsNullOrEmpty(downloadUrl)) return null;

        Log.Debug("AppViewModels", $"[Resolve] 开始缓存: {song.Title}");
        try
        {
            var localPath = await AudioCacheService.Instance.CacheAsync(
                song.FilePath,
                async (url, token) =>
                {
                    HttpResponseMessage? resp = null;
                    try
                    {
                        resp = await CacheHttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
                        resp.EnsureSuccessStatusCode();
                        return await resp.Content.ReadAsStreamAsync(token);
                    }
                    catch
                    {
                        resp?.Dispose();
                        return null;
                    }
                },
                ct);
            Log.Debug("AppViewModels", $"[Resolve] 缓存完成: {song.Title}, {(localPath != null ? "成功" : "失败")}");
            return localPath;
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Resolve] 缓存失败: {song.Title}, {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 从本地缓存文件提取元数据（标题、艺术家、专辑、时长等），更新 song 并写回数据库。
    /// 和扫描本地音乐完全一样的流程。
    /// </summary>
    private async Task LoadMetadataFromLocalFileAsync(Song song, string localPath)
    {
        if (song.Source == SongSource.Local) return; // 本地歌曲不需要
        try
        {
            using var fs = File.OpenRead(localPath);
            var tagged = CatClawMusic.Core.Services.TagReader.ReadFromStream(fs, localPath, Path.GetFileName(localPath), new FileInfo(localPath).Length);
            if (tagged == null) return;

            bool changed = false;
            if (!string.IsNullOrWhiteSpace(tagged.Title) && tagged.Title != song.Title)
            { song.Title = tagged.Title; changed = true; }
            if (!string.IsNullOrWhiteSpace(tagged.Artist) && tagged.Artist != "未知艺术家" && tagged.Artist != song.Artist)
            { song.Artist = tagged.Artist; changed = true; }
            if (!string.IsNullOrWhiteSpace(tagged.Album) && tagged.Album != "未知专辑" && tagged.Album != song.Album)
            { song.Album = tagged.Album; changed = true; }
            if (tagged.Duration > 0 && song.Duration <= 0)
            { song.Duration = tagged.Duration; changed = true; }
            if (tagged.Bitrate > 0) song.Bitrate = tagged.Bitrate;
            if (tagged.Year > 0) song.Year = tagged.Year;
            if (tagged.TrackNumber > 0) song.TrackNumber = tagged.TrackNumber;
            song.Genre = tagged.Genre;

            if (changed)
            {
                await _db.SaveSongAsync(song);
                // 自动分类
                if (!string.IsNullOrWhiteSpace(song.Artist))
                {
                    var artistNames = MusicUtility.SplitArtistNames(song.Artist);
                    if (artistNames.Count > 0)
                    {
                        var artistId = await _db.EnsureArtistAsync(artistNames[0]);
                        if (artistId > 0) song.ArtistId = artistId;
                    }
                }
                if (!string.IsNullOrWhiteSpace(song.Album) && song.Album != "未知专辑" && song.ArtistId > 0)
                {
                    var albumId = await _db.EnsureAlbumAsync(song.Album, song.ArtistId);
                    if (albumId > 0) song.AlbumId = albumId;
                }
                await _db.SaveSongAsync(song);

                // 更新 UI
                if (_loadedSongId == song.Id)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        Title = song.Title ?? "未知歌曲";
                        Artist = song.Artist ?? "未知艺术家";
                        Album = song.Album ?? "未知专辑";
                        HasAlbum = !string.IsNullOrEmpty(song.Album) && song.Album != "未知专辑";
                        if (song.Duration > 1000)
                        {
                            Duration = song.Duration / 1000.0;
                            TotalTimeDisplay = FormatTime(Duration);
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Resolve] 元数据提取失败: {song.Title}, {ex.Message}");
        }
    }

    /// <summary>
    /// 从本地缓存文件提取封面（和扫描本地音乐一样用 TagReader.ExtractCoverArtToFile）。
    /// </summary>
    private async Task LoadCoverFromLocalFileAsync(Song song, string localPath, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var coverPath = await Task.Run(() =>
                CatClawMusic.Core.Services.TagReader.ExtractCoverArtToFile(localPath, _coverCacheDir), ct);

            if (coverPath != null)
            {
                CurrentCoverPath = coverPath;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    CoverImage = ImageSource.FromFile(coverPath);
                    HasCover = true;
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Resolve] 封面提取失败: {song.Title}, {ex.Message}");
        }
    }

    /// <summary>
    /// 从本地缓存文件加载歌词（先找同目录 .lrc 文件，再尝试内嵌歌词）。
    /// 和播放本地音乐完全一样的流程。
    /// </summary>
    private async Task LoadLyricsFromLocalFileAsync(Song song, string localPath, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            // 按歌词来源模式裁剪：仅内嵌跳过外挂 .lrc/.ttml；仅外挂跳过内嵌读取
            var sourceMode = Services.LyricsSettingsService.Instance.LyricsSourceMode;
            bool skipExternal = sourceMode == CatClawMusic.Core.Models.LyricsSourceMode.Embedded;
            bool skipEmbedded = sourceMode == CatClawMusic.Core.Models.LyricsSourceMode.External;

            // 1. 先找同名 .lrc 文件
            string? lyricsText = null;
            if (!skipExternal)
            {
                var lrcPath = Path.ChangeExtension(localPath, ".lrc");
                if (File.Exists(lrcPath))
                {
                    lyricsText = await File.ReadAllTextAsync(lrcPath, ct);
                }
                else
                {
                    // 2. 尝试 .ttml
                    var ttmlPath = Path.ChangeExtension(localPath, ".ttml");
                    if (File.Exists(ttmlPath))
                        lyricsText = await File.ReadAllTextAsync(ttmlPath, ct);
                }
            }

            // 3. 内嵌歌词
            if (!skipEmbedded && string.IsNullOrWhiteSpace(lyricsText))
            {
                using var fs = File.OpenRead(localPath);
                lyricsText = CatClawMusic.Core.Services.TagReader.ReadEmbeddedLyricsFromStream(fs, Path.GetFileName(localPath));
            }

            if (!string.IsNullOrWhiteSpace(lyricsText))
            {
                var parsed = await Task.Run(() => _lyrics.TryParseLyrics(lyricsText), ct);
                if (parsed != null)
                {
                    _currentLyrics = parsed;
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        HasLyrics = true;
                        NoLyricsText = "";
                        OnPropertyChanged(nameof(AllLyricLines));
                    });
                }
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    HasLyrics = false;
                    NoLyricsText = "暂无歌词";
                });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Resolve] 歌词加载失败: {song.Title}, {ex.Message}");
        }
    }
}
