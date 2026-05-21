using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Collections.Concurrent;

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

    private static readonly ConcurrentDictionary<string, Task> _loadingCovers = new();
    private static readonly SemaphoreSlim _coverLoadSemaphore = new(4, 4);

    public event EventHandler<Song>? SongClicked;
    public event EventHandler<Song>? SongLongClicked;
    public View? LastLongClickedView { get; private set; }

    public SongAdapter(INetworkMusicService? networkMusic = null)
    {
        _networkMusic = networkMusic;
        HasStableIds = true;
    }

    public void UpdateSongs(IEnumerable<Song> songs)
    {
        _songs = songs.ToList();
        NotifyDataSetChanged();
    }

    public void UpdatePlayState(int currentSongId, bool isPlaying)
    {
        var oldId = _currentPlayingSongId;
        _currentPlayingSongId = currentSongId;
        _isPlaying = isPlaying;

        if (oldId == currentSongId) return;

        for (int i = 0; i < _songs.Count; i++)
        {
            if (_songs[i].Id == oldId || _songs[i].Id == currentSongId)
                NotifyItemChanged(i);
        }
    }

    public void AddRange(IList<Song> songs)
    {
        if (songs.Count == 0) return;
        int startPos = _songs.Count;
        _songs.AddRange(songs);
        NotifyItemRangeInserted(startPos, songs.Count);
    }

    public void Clear()
    {
        int count = _songs.Count;
        _songs.Clear();
        if (count > 0) NotifyItemRangeRemoved(0, count);
    }

    public override int ItemCount => _songs.Count;

    public override long GetItemId(int position)
    {
        return _songs[position].Id;
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_song, parent, false)!;
        return new SongViewHolder(view, OnSongClick, OnSongLongClick);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        ((SongViewHolder)holder).Bind(_songs[position], this);
    }

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
        private int _boundSongId;
        private CancellationTokenSource? _coverCts;
        private string? _loadedCoverPath;
        private static readonly Handler _mainHandler = new(Looper.MainLooper!);
        private static Android.Graphics.Color? _cachedTitleColor;

        public SongViewHolder(View view, Action<int> onClick, Action<int, View>? onLongClick) : base(view)
        {
            _title = view.FindViewById<TextView>(Resource.Id.song_title)!;
            _artist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
            _album = view.FindViewById<TextView>(Resource.Id.song_album)!;
            _cover = view.FindViewById<ImageView>(Resource.Id.song_cover)!;
            _pauseIcon = view.FindViewById<Helpers.WaveformView>(Resource.Id.playing_pause_icon)!;
            _title.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
            _artist.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
            _album.ImportantForAutofill = Android.Views.ImportantForAutofill.No;
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

            if (song.Id == adapter._currentPlayingSongId)
            {
                _title.SetTextColor(Android.Graphics.Color.ParseColor("#9B7ED8"));
                _pauseIcon.SetPlaying(adapter._isPlaying);
            }
            else
            {
                if (_cachedTitleColor == null)
                {
                    var typedValue = new Android.Util.TypedValue();
                    _cachedTitleColor = ItemView.Context?.Theme?.ResolveAttribute(Resource.Attribute.catClawTextPrimary, typedValue, true) == true
                        ? new Android.Graphics.Color(typedValue.Data)
                        : Android.Graphics.Color.Black;
                }
                _title.SetTextColor(_cachedTitleColor.Value);
                _pauseIcon.SetPlaying(false);
            }

            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = new CancellationTokenSource();
            var ct = _coverCts.Token;

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
                var cacheKey = $"song_{song.Id}";
                if (!_loadingCovers.ContainsKey(cacheKey))
                {
                    var loadTask = LoadCoverWithThrottleAsync(song, adapter, ct);
                    _loadingCovers[cacheKey] = loadTask;
                    _ = loadTask.ContinueWith(_ => _loadingCovers.TryRemove(cacheKey, out _));
                }
            }
        }

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
                    if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                    {
                        var retriever = new global::Android.Media.MediaMetadataRetriever();
                        try
                        {
                            retriever.SetDataSource(global::Android.App.Application.Context,
                                global::Android.Net.Uri.Parse(song.FilePath));
                            var embedded = retriever.GetEmbeddedPicture();
                            if (embedded != null && embedded.Length > 0)
                                coverBytes = embedded;
                        }
                        catch { }
                        finally { retriever.Release(); }
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
                    var coverPath = GetCoverCachedPath(song.Id);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(coverPath)!);
                    await System.IO.File.WriteAllBytesAsync(coverPath, coverBytes, ct);

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
            catch (System.OperationCanceledException) { }
            catch { }
            finally
            {
                _coverLoadSemaphore.Release();
            }
        }
    }
}
