using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Collections.Concurrent;
using TagLibFile = TagLib.File;

namespace CatClawMusic.UI.Adapters;

/// <summary>
/// 歌曲列表适配器，支持封面加载、播放状态高亮和增量更新
/// </summary>
public class SongAdapter : RecyclerView.Adapter
{
    private List<Song> _songs = new();
    private readonly INetworkMusicService? _networkMusic;
    private ConnectionProfile? _cachedNavidromeProfile;
    private ConnectionProfile? _cachedWebDavProfile;
    private bool _profilesLookedUp;
    private int _currentPlayingSongId = -1;
    private bool _isPlaying = false;

    // 封面加载的并发控制：去重 + 限流
    private static readonly ConcurrentDictionary<string, Task> _loadingCovers = new();
    private static readonly SemaphoreSlim _coverLoadSemaphore = new(4, 4); // 最多 4 个并发

    /// <summary>
    /// 歌曲点击事件
    /// </summary>
    public event EventHandler<Song>? SongClicked;
    /// <summary>
    /// 歌曲长按事件
    /// </summary>
    public event EventHandler<Song>? SongLongClicked;
    /// <summary>
    /// 最后一次长按时对应的锚点视图
    /// </summary>
    public View? LastLongClickedView { get; private set; }

    /// <summary>
    /// 创建歌曲适配器实例
    /// </summary>
    public SongAdapter(INetworkMusicService? networkMusic = null)
    {
        _networkMusic = networkMusic;
    }

    /// <summary>
    /// 全量更新歌曲列表
    /// </summary>
    public void UpdateSongs(IEnumerable<Song> songs)
    {
        _songs = songs.ToList();
        NotifyDataSetChanged();
    }

    /// <summary>
    /// 更新当前播放状态，高亮正在播放的歌曲
    /// </summary>
    public void UpdatePlayState(int currentSongId, bool isPlaying)
    {
        _currentPlayingSongId = currentSongId;
        _isPlaying = isPlaying;
        NotifyDataSetChanged();
    }

    /// <summary>增量追加歌曲，使用 NotifyItemRangeInserted 避免全量刷新</summary>
    public void AddRange(IList<Song> songs)
    {
        if (songs.Count == 0) return;
        int startPos = _songs.Count;
        _songs.AddRange(songs);
        NotifyItemRangeInserted(startPos, songs.Count);
    }

    /// <summary>清空所有歌曲</summary>
    public void Clear()
    {
        int count = _songs.Count;
        _songs.Clear();
        if (count > 0) NotifyItemRangeRemoved(0, count);
    }

    /// <summary>
    /// 歌曲总数
    /// </summary>
    public override int ItemCount => _songs.Count;

    /// <summary>
    /// 创建歌曲列表项ViewHolder实例
    /// </summary>
    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_song, parent, false)!;
        return new SongViewHolder(view, OnSongClick, OnSongLongClick);
    }

    /// <summary>
    /// 绑定歌曲数据到ViewHolder
    /// </summary>
    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        ((SongViewHolder)holder).Bind(_songs[position], this);
    }

    /// <summary>
    /// ViewHolder回收时取消正在进行的加载任务
    /// </summary>
    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        ((SongViewHolder)holder).CancelLoad();
        base.OnViewRecycled(holder);
    }

    private void OnSongClick(int position) => SongClicked?.Invoke(this, _songs[position]);
    private void OnSongLongClick(int position, View anchor)
    {
        LastLongClickedView = anchor;
        SongLongClicked?.Invoke(this, _songs[position]);
    }

    /// <summary>
    /// 根据协议类型获取已启用的网络连接配置
    /// </summary>
    internal async Task<ConnectionProfile?> GetNetworkProfileAsync(ProtocolType protocol)
    {
        if (protocol == ProtocolType.Navidrome && _cachedNavidromeProfile != null) return _cachedNavidromeProfile;
        if (protocol == ProtocolType.WebDAV && _cachedWebDavProfile != null) return _cachedWebDavProfile;
        if (_networkMusic == null || _profilesLookedUp) return null;
        _profilesLookedUp = true;
        try
        {
            var profiles = await _networkMusic.GetProfilesAsync();
            _cachedNavidromeProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome && p.IsEnabled);
            _cachedWebDavProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.WebDAV && p.IsEnabled);
            return protocol == ProtocolType.Navidrome ? _cachedNavidromeProfile : _cachedWebDavProfile;
        }
        catch { return null; }
    }

    private class SongViewHolder : RecyclerView.ViewHolder
    {
        private readonly TextView _title, _artist, _album;
        private readonly ImageView _cover;
        private readonly Helpers.WaveformView _pauseIcon;
        private int _boundSongId; // 当前绑定的歌曲 ID，防止封面加载错位
        private CancellationTokenSource? _coverCts;
        private string? _loadedCoverPath; // 记录上次加载的封面路径，避免重复 SetImageURI
        private static readonly Handler _mainHandler = new(Looper.MainLooper!);

        public SongViewHolder(View view, Action<int> onClick, Action<int, View>? onLongClick) : base(view)
        {
            _title = view.FindViewById<TextView>(Resource.Id.song_title)!;
            _artist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
            _album = view.FindViewById<TextView>(Resource.Id.song_album)!;
            _cover = view.FindViewById<ImageView>(Resource.Id.song_cover)!;
            _pauseIcon = view.FindViewById<Helpers.WaveformView>(Resource.Id.playing_pause_icon)!;
            view.Click += (s, e) => onClick(BindingAdapterPosition);
            if (onLongClick != null)
                view.LongClick += (s, e) => { onLongClick(BindingAdapterPosition, (View)s!); };
        }

        public void Bind(Song song, SongAdapter adapter)
        {
            _title.Text = song.Title ?? "未知歌曲";
            _artist.Text = string.IsNullOrEmpty(song.Artist) ? "未知艺术家" : song.Artist;
            _album.Text = song.Album ?? "";
            _boundSongId = song.Id;

            // 设置播放状态视觉效果
            if (song.Id == adapter._currentPlayingSongId)
            {
                _title.SetTextColor(Android.Graphics.Color.ParseColor("#9B7ED8")); // 使用主题色
                _pauseIcon.SetPlaying(adapter._isPlaying);
            }
            else
            {
                var typedValue = new Android.Util.TypedValue();
                var color = ItemView.Context?.Theme?.ResolveAttribute(Resource.Attribute.catClawTextPrimary, typedValue, true) == true
                    ? new Android.Graphics.Color(typedValue.Data)
                    : Android.Graphics.Color.Black;
                _title.SetTextColor(color);
                _pauseIcon.SetPlaying(false);
            }

            // 取消上一个加载任务，防止旧任务覆盖新 ViewHolder 的封面
            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = new CancellationTokenSource();
            var ct = _coverCts.Token;

            // 加载封面：优先缓存，其次后台提取/下载
            var coverPath = GetCoverCachedPath(song.Id);
            if (System.IO.File.Exists(coverPath))
            {
                if (_loadedCoverPath != coverPath)
                {
                    _cover.SetImageURI(global::Android.Net.Uri.Parse(coverPath));
                    _loadedCoverPath = coverPath;
                }
            }
            else
            {
                _loadedCoverPath = null;
                _cover.SetImageResource(Resource.Drawable.cover_default);
                // 后台提取/下载封面（带去重和并发控制）
                var cacheKey = $"song_{song.Id}";
                if (!_loadingCovers.ContainsKey(cacheKey))
                {
                    var loadTask = LoadCoverWithThrottleAsync(song, adapter, ct);
                    _loadingCovers[cacheKey] = loadTask;
                    _ = loadTask.ContinueWith(_ => _loadingCovers.TryRemove(cacheKey, out _));
                }
            }
        }

        /// <summary>取消当前加载任务（ViewHolder 回收时调用）</summary>
        public void CancelLoad()
        {
            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = null;
        }

        private static string GetCoverCachedPath(int songId)
        {
            var cacheDir = System.IO.Path.Combine(
                global::Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
            return System.IO.Path.Combine(cacheDir, $"cover_{songId}.jpg");
        }

        /// <summary>后台加载封面（带并发限流和取消支持）</summary>
        private async Task LoadCoverWithThrottleAsync(Song song, SongAdapter adapter, CancellationToken ct)
        {
            await _coverLoadSemaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                byte[]? coverBytes = null;

                if (song.Source == SongSource.WebDAV)
                {
                    var coverId = song.CoverArtPath ?? song.RemoteId;
                    if (!string.IsNullOrEmpty(coverId))
                    {
                        var isNavidrome = !string.IsNullOrEmpty(song.FilePath) && song.FilePath.Contains("stream.view?id=");
                        var protocol = isNavidrome ? ProtocolType.Navidrome : ProtocolType.WebDAV;
                        var profile = await adapter.GetNetworkProfileAsync(protocol);
                        if (profile != null && adapter._networkMusic != null)
                        {
                            using var stream = await adapter._networkMusic.GetCoverAsync(coverId, profile);
                            if (stream != null)
                            {
                                using var ms = new MemoryStream();
                                await stream.CopyToAsync(ms, ct);
                                ct.ThrowIfCancellationRequested();
                                coverBytes = ms.ToArray();
                            }
                        }
                    }
                }
                else
                {
                    // 本地歌曲：从文件提取内嵌封面
                    if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                    {
                        var ctx = global::Android.App.Application.Context;
                        using var stream = ctx.ContentResolver!.OpenInputStream(
                            global::Android.Net.Uri.Parse(song.FilePath));
                        if (stream != null)
                        {
                            var abstraction = new CatClawMusic.Core.Services.ReadOnlyFileAbstraction(
                                song.FilePath, stream);
                            using var tagFile = TagLibFile.Create(abstraction);
                            ct.ThrowIfCancellationRequested();
                            if (tagFile.Tag.Pictures is { Length: > 0 })
                                coverBytes = tagFile.Tag.Pictures[0].Data.Data;
                        }
                    }
                    else if (System.IO.File.Exists(song.FilePath))
                    {
                        coverBytes = await Task.Run(() =>
                            CatClawMusic.Core.Services.TagReader.ExtractCoverArt(song.FilePath), ct);
                    }
                }

                if (coverBytes == null)
                {
                    try
                    {
                        var pluginManager = MainApplication.Services.GetService(typeof(CatClawMusic.Core.Interfaces.IPluginManager)) as CatClawMusic.Core.Interfaces.IPluginManager;
                        if (pluginManager != null)
                        {
                            var coverProviders = pluginManager.GetEnabledPlugins<CatClawMusic.Core.Interfaces.ICoverProviderPlugin>();
                            foreach (var provider in coverProviders)
                            {
                                ct.ThrowIfCancellationRequested();
                                try
                                {
                                    if (!provider.IsAvailable) continue;
                                    coverBytes = await provider.GetCoverAsync(song);
                                    if (coverBytes != null) break;
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }

                ct.ThrowIfCancellationRequested();
                if (coverBytes != null)
                {
                    // 缓存到本地
                    var coverPath = GetCoverCachedPath(song.Id);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(coverPath)!);
                    await System.IO.File.WriteAllBytesAsync(coverPath, coverBytes, ct);

                    // 回到主线程更新 ImageView（检查 songId 防止错位）
                    _mainHandler.Post(() =>
                    {
                        if (_boundSongId == song.Id && System.IO.File.Exists(coverPath))
                        {
                            _cover.SetImageURI(global::Android.Net.Uri.Parse(coverPath));
                            _loadedCoverPath = coverPath;
                        }
                    });
                }
            }
            catch (System.OperationCanceledException) { /* 任务被取消，静默退出 */ }
            catch { /* 静默失败，封面非必需 */ }
            finally
            {
                _coverLoadSemaphore.Release();
            }
        }
    }
}
