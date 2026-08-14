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
    private async Task LoadCoverAsync(Song song, CancellationToken ct)
    {
        Log.Debug("AppViewModels", $"[CoverArt] 开始加载封面: {song.Title} (Id={song.Id}, Protocol={song.Protocol}, CoverArtPath={song.CoverArtPath?[..Math.Min(60, song.CoverArtPath?.Length ?? 0)] ?? "null"})");
        string? coverPath = null;

        // 在线歌曲（封面为 http/https URL）判定：播放页不再把在线封面缓存到本地，
        // 改为内存字节直显（见步骤 1b），以免本地缓存文件堆积或显示错误。
        var isOnlineCover = !string.IsNullOrEmpty(song.CoverArtPath)
            && (song.CoverArtPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || song.CoverArtPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        // 在线封面下载到的内存字节（播放页/通知"用完即弃"），不写缓存目录。
        byte[]? onlineCoverBytes = null;

        // 缓存命中：已下采样到播放页尺寸（1000px）的封面文件，直接复用避免重复解码大图。
        // ⚠ 信任条件增加"实际尺寸校验"：旧版会把 300px 缩略图误拷进 1000px 桶
        //（DownsampleToCache 对源≤目标只做 File.Copy，配合解码失败的回退路径），
        // 播放页盲目信任后放大显示 = 低清。实际尺寸 <400（低于任何显示需求）视为污染 → 删除重取。
        // ⚠ 在线封面跳过本地缓存命中（既是读也是写都不发生），保证每次直接取最新封面。
        var npCached = Services.CoverHelper.GetCachedPath(song.Id, Services.CoverHelper.NowPlayingSize);
        if (!isOnlineCover && File.Exists(npCached) && Services.CoverHelper.IsValidImageFilePublic(npCached))
        {
            var dim = Services.CoverHelper.MaxDimensionPublic(npCached);
            if (dim >= 400 || dim == 0) // 0 = 解码器读不出尺寸（多为合法但头部怪异的图），交给 UI 解码，不误删
            {
                CurrentCoverPath = npCached;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    CoverImage = ImageSource.FromFile(npCached);
                    HasCover = true;
                });
#if ANDROID || WINDOWS
                try { (_audioService as Services.AudioPlayerService)?.UpdateCoverPath(npCached); } catch { }
#endif
                return;
            }
        }
        // npCached 损坏或实际尺寸过小（旧污染）→ 删除，走完整解析管线（含内嵌封面全分辨率提取）
        //（在线封面不清理也不使用本地缓存，见步骤 1b）
        if (!isOnlineCover && File.Exists(npCached))
        {
            try { File.Delete(npCached); } catch { }
        }

        // 1. Check existing CoverArtPath
        if (!string.IsNullOrEmpty(song.CoverArtPath) && File.Exists(song.CoverArtPath))
        {
            coverPath = song.CoverArtPath;
        }

        // 1b. Navidrome/Subsonic/在线插件: CoverArtPath 是 http(s) 封面 URL。
        // ⚠ 在线封面不落盘：直接下载到内存字节，播放页用 StreamImageSource 直显（不写 cover_{Id}.jpg），
        //    通知封面也用内存字节"用完即弃"。彻底避免在线封面本地缓存文件堆积/显示错误。
        if (coverPath == null && isOnlineCover)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var bytes = await SharedHttpClient.GetByteArrayAsync(song.CoverArtPath, ct);
                if (bytes != null && bytes.Length > 0)
                {
                    onlineCoverBytes = bytes;          // 内存暂存，不写缓存目录
                    coverPath = song.CoverArtPath;     // 保持 URL，作为封面标识走内存直显
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Debug("AppViewModels", $"[CoverArt] URL下载失败: {ex.Message}");
            }
        }

        // 2. Check cached cover（带 URL 指纹：URL 变化时不会命中旧图；非 URL 封面源回退旧格式 cover_{Id}.jpg）
        // （在线封面不读本地缓存，直接走步骤 1b 的内存直显）
        if (coverPath == null && !isOnlineCover)
        {
            var cachedPath = Services.CoverHelper.GetHttpCoverCachePath(song.Id, song.CoverArtPath);
            if (!File.Exists(cachedPath))
            {
                var legacy = Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
                if (File.Exists(legacy)) cachedPath = legacy;
            }
            if (File.Exists(cachedPath))
                coverPath = cachedPath;
        }

        // 3. Extract embedded cover
        if (coverPath == null && !string.IsNullOrEmpty(song.FilePath))
        {
            ct.ThrowIfCancellationRequested();

            // Android SAF content:// 路径、远程 http(s):// URL 和 smb://（通过本地代理转 http）：用 MediaMetadataRetriever.GetEmbeddedPicture() 提取
            // 注意：WebDAV/SMB 协议的歌曲跳过此路径（MediaMetadataRetriever 无法处理带 user:pass@ 的 URL），改由步骤 6 处理
#if ANDROID
            string? extractUri = null;
            if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                extractUri = song.FilePath;
            }
            else if ((song.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || song.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                && song.Protocol != ProtocolType.WebDAV && song.Protocol != ProtocolType.SMB)
            {
                try
                {
                    var networkSvc = MauiProgram.Services.GetService<INetworkMusicService>();
                    if (networkSvc != null)
                    {
                        var resolved = await networkSvc.ResolveWebDavPlaybackUrlAsync(song.FilePath);
                        extractUri = string.IsNullOrEmpty(resolved) ? song.FilePath : resolved;
                    }
                    else
                    {
                        extractUri = song.FilePath;
                    }
                }
                catch
                {
                    extractUri = song.FilePath;
                }
            }
            else if (song.FilePath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)
                && song.Protocol != ProtocolType.SMB)
            {
                var proxy = SmbStreamProxy.Current;
                proxy?.Start();
                extractUri = proxy?.ToProxyUrl(song.FilePath);
            }
            if (extractUri != null)
            {
                coverPath = await Task.Run(() =>
                    ExtractCoverFromContentUri(extractUri, song.Id), ct);
            }
            else
#endif
            if (!song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase)
                && !song.FilePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !song.FilePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !song.FilePath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)
                && File.Exists(song.FilePath))
            {
                coverPath = await Task.Run(() =>
                    TagReader.ExtractCoverArtToFile(song.FilePath, _coverCacheDir), ct);
            }
        }

        // 4. Try IMusicLibraryService (handles network covers etc.)
        if (coverPath == null)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                // await using 确保异常路径下 stream 也能被释放
                await using var stream = await _musicLibrary.GetAlbumCoverAsync(song);
                if (stream != null)
                {
                    var cachedPath = Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
                    await using (var fs = File.Create(cachedPath))
                        await stream.CopyToAsync(fs, ct);
                    coverPath = cachedPath;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* ignore */ }
        }

        // 5. Navidrome 旧数据兼容: CoverArtPath 是 coverArt ID（非URL），通过 INetworkMusicService 下载封面
        if (coverPath == null
            && song.Protocol == ProtocolType.Navidrome
            && !string.IsNullOrEmpty(song.CoverArtPath)
            && !song.CoverArtPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !song.CoverArtPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var networkSvc = MauiProgram.Services.GetService<INetworkMusicService>();
                if (networkSvc != null)
                {
                    var profiles = await networkSvc.GetProfilesAsync();
                    var profile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome);
                    if (profile != null)
                    {
                        // await using 确保异常路径下 stream 也能被释放
                        await using var stream = await networkSvc.GetCoverAsync(song.CoverArtPath, profile);
                        if (stream != null)
                        {
                            var cachedPath = Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
                            await using (var fs = File.Create(cachedPath))
                                await stream.CopyToAsync(fs, ct);
                            coverPath = cachedPath;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Debug("AppViewModels", $"[CoverArt] Navidrome旧数据封面获取失败: {ex.Message}");
            }
        }

        // 6. WebDAV/SMB: 通过 INetworkMusicService 下载文件头并提取内嵌封面
        // MediaMetadataRetriever.SetDataSource 无法处理带 user:pass@ 的 WebDAV URL，需走 NetworkMusicService
        if (coverPath == null
            && (song.Protocol == ProtocolType.WebDAV || song.Protocol == ProtocolType.SMB)
            && !string.IsNullOrEmpty(song.RemoteId))
        {
            Log.Debug("AppViewModels", $"[CoverArt] 步骤6: 尝试 {song.Protocol} 封面提取 (RemoteId={song.RemoteId?[..Math.Min(40, song.RemoteId?.Length ?? 0)]})");
            try
            {
                ct.ThrowIfCancellationRequested();
                var networkSvc = MauiProgram.Services.GetService<INetworkMusicService>();
                if (networkSvc != null)
                {
                    var profiles = await networkSvc.GetProfilesAsync();
                    var profile = profiles.FirstOrDefault(p => p.Protocol == song.Protocol && p.IsEnabled);
                    if (profile != null)
                    {
                        Log.Debug("AppViewModels", $"[CoverArt] 步骤6: 找到配置 {profile.Name}, 调用 GetCoverAsync...");
                        // await using 确保异常路径下 stream 也能被释放
                        await using var stream = await networkSvc.GetCoverAsync(song.RemoteId, profile, song.RemoteCoverPath);
                        if (stream != null)
                        {
                            var cachedPath = Path.Combine(_coverCacheDir, $"cover_{song.Id}.jpg");
                            await using (var fs = File.Create(cachedPath))
                                await stream.CopyToAsync(fs, ct);
                            coverPath = cachedPath;
                            Log.Debug("AppViewModels", $"[CoverArt] 步骤6: 封面提取成功 -> {cachedPath}");
                        }
                        else
                        {
                            Log.Debug("AppViewModels", $"[CoverArt] 步骤6: GetCoverAsync 返回 null");
                        }
                    }
                    else
                    {
                        Log.Debug("AppViewModels", $"[CoverArt] 步骤6: 未找到 {song.Protocol} 配置");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Debug("AppViewModels", $"[CoverArt] WebDAV/SMB 封面获取失败: {ex.Message}");
            }
        }

        ct.ThrowIfCancellationRequested();

        Log.Debug("AppViewModels", $"[CoverArt] 封面加载完成: {song.Title}, coverPath={(coverPath != null ? "找到" : "未找到")}");

        if (coverPath != null)
        {
            // 最终路径校验：损坏则回退默认封面
            if (!coverPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !coverPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !Services.CoverHelper.IsValidImageFilePublic(coverPath))
            {
                Log.Debug("AppViewModels", $"[CoverArt] 最终路径校验失败，回退默认封面: {coverPath}");
                coverPath = null;
            }
        }

        if (coverPath != null)
        {
            // 在线歌曲封面（http/https URL）：内存字节直显，不写本地缓存文件。
            // - 播放页：StreamImageSource 直接读内存字节（用完即弃，不落盘）
            // - 通知：UpdateCoverBytes 用内存字节解码（用完即弃）
            if (coverPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || coverPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                CurrentCoverPath = coverPath;
                var bytes = onlineCoverBytes;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (bytes is { Length: > 0 })
                        CoverImage = ImageSource.FromStream(ct => Task.FromResult<Stream>(new MemoryStream(bytes)));
                    else
                        CoverImage = ImageSource.FromUri(new Uri(coverPath));
                    HasCover = true;
                });
#if ANDROID
                try { (_audioService as Services.AudioPlayerService)?.UpdateCoverBytes(bytes, coverPath); }
                catch { }
#elif WINDOWS
                try { (_audioService as Services.AudioPlayerService)?.UpdateCoverPath(null); }
                catch { }
#endif
                return;
            }

            // 限制播放页封面最大边长 1000px：
            // - 网络封面（http/https）保持原 URL 直显（Android 解码期会按显示尺寸再降采样）；
            // - 本方法各分支刚提取出的原始文件：直接下采样到 1000 并清理临时文件；
            // - 已是 song.CoverArtPath 缩略图等情况：按播放页尺寸重新解析（源太小会自动从音频重新提取全分辨率）。
            string finalPath;
            if (coverPath != song.CoverArtPath && File.Exists(coverPath))
            {
                var bucketed = Services.CoverHelper.GetCachedPath(song.Id, Services.CoverHelper.NowPlayingSize);
                finalPath = Services.CoverHelper.DownsampleToCache(coverPath, bucketed, Services.CoverHelper.NowPlayingSize)
                    ? bucketed
                    : coverPath;
                if (finalPath == bucketed && coverPath != bucketed)
                {
                    try { File.Delete(coverPath); } catch { }
                }
            }
            else
            {
                var resolved = Services.CoverHelper.ResolveSingleCover(song, Services.CoverHelper.NowPlayingSize);
                finalPath = resolved ?? coverPath;
            }

            CurrentCoverPath = finalPath;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                // 使用 FileImageSource 而非 StreamImageSource：
                // 1. 避免 StreamImageSource 内部取消机制导致 FrostedBackground 加载失败
                // 2. 让 CachingFileImageSourceService 命中内存缓存，减少重复解码
                CoverImage = ImageSource.FromFile(finalPath);
                HasCover = true;
            });

#if ANDROID || WINDOWS
            try { (_audioService as Services.AudioPlayerService)?.UpdateCoverPath(finalPath); }
            catch { }
#endif
        }
        else
        {
            CurrentCoverPath = null;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                CoverImage = ImageSource.FromFile(DefaultCoverService.GetDefaultCoverPath());
                HasCover = false;
            });

#if ANDROID || WINDOWS
            try { (_audioService as Services.AudioPlayerService)?.UpdateCoverPath(null); }
            catch { }
#endif
        }
    }

#if ANDROID
    /// <summary>
    /// 从 Android SAF content:// URI 提取嵌入封面并缓存为 jpg 文件。
    /// 使用 MediaMetadataRetriever.GetEmbeddedPicture()，支持 content:// 媒体路径。
    /// </summary>
    private string? ExtractCoverFromContentUri(string contentUri, int songId)
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            var retriever = new global::Android.Media.MediaMetadataRetriever();
            try
            {
                retriever.SetDataSource(ctx, global::Android.Net.Uri.Parse(contentUri));
                var bytes = retriever.GetEmbeddedPicture();
                if (bytes == null || bytes.Length == 0) return null;

                Directory.CreateDirectory(_coverCacheDir);
                var outPath = Path.Combine(_coverCacheDir, $"cover_{songId}.jpg");
                // 同步方法内用 Task.Run 将写盘切到线程池线程，避免阻塞当前线程的同步上下文
                Task.Run(() => File.WriteAllBytes(outPath, bytes)).Wait();
                return outPath;
            }
            finally
            {
                retriever.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[CoverArt] content:// 提取失败: {ex.Message}");
            return null;
        }
    }
#endif

    // === Lyrics Loading ===

}
