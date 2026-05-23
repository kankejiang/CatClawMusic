using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Collections.Concurrent;

namespace CatClawMusic.UI.Adapters;

public class SongAdapter : RecyclerView.Adapter
{
    private List<Song> _songs = new();
    private readonly INetworkMusicService? _networkMusic;
    private ConnectionProfile? _cachedNavidromeProfile;
    private ConnectionProfile? _cachedWebDavProfile;
    private ConnectionProfile? _cachedSmbProfile;
    private bool _profilesLookedUp;
    private int _currentPlayingSongId = -1;
    private bool _isPlaying = false;
    private volatile bool _isScrolling;

    private static readonly ConcurrentDictionary<string, Task> _loadingCovers = new();
    private static readonly SemaphoreSlim _coverLoadSemaphore = new(2, 2);
    private static readonly Handler _mainHandler = new(Looper.MainLooper!);
    private static readonly BitmapLruCache _bitmapCache;

    static SongAdapter()
    {
        var maxMemory = (int)(Java.Lang.Runtime.GetRuntime()!.MaxMemory() / 1024);
        var cacheSize = maxMemory / 8;
        _bitmapCache = new BitmapLruCache(cacheSize);
    }

    private class BitmapLruCache : LruCache
    {
        public BitmapLruCache(int size) : base(size) { }

        protected override int SizeOf(Java.Lang.Object? key, Java.Lang.Object? value)
        {
            if (value is Bitmap b)
                return b.ByteCount / 1024;
            var bitmap = value?.JavaCast<Bitmap>();
            if (bitmap == null) return 0;
            return bitmap.ByteCount / 1024;
        }

        protected override void EntryRemoved(bool evicted, Java.Lang.Object? key, Java.Lang.Object? oldValue, Java.Lang.Object? newValue)
        {
            if (oldValue is Bitmap ob && !ob.IsRecycled) ob.Recycle();
        }

        public Bitmap? GetBitmap(string key)
        {
            var val = Get(key);
            if (val == null) return null;
            if (val is Bitmap b) return b;
            return val.JavaCast<Bitmap>();
        }

        public void PutBitmap(string key, Bitmap bitmap) => Put(key, bitmap);
    }

    public event EventHandler<Song>? SongClicked;
    public event EventHandler<Song>? SongLongClicked;
    public View? LastLongClickedView { get; private set; }

    public SongAdapter(INetworkMusicService? networkMusic = null)
    {
        _networkMusic = networkMusic;
        HasStableIds = true;
    }

    public void SetScrolling(bool scrolling) => _isScrolling = scrolling;

    public void UpdateSongs(IEnumerable<Song> songs)
    {
        var oldList = _songs;
        var newList = songs.ToList();
        _songs = newList;
        var diffResult = DiffUtil.CalculateDiff(new SongDiffCallback(oldList, newList));
        diffResult.DispatchUpdatesTo(this);
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

    private void OnSongClick(int position)
    {
        if (position >= 0 && position < _songs.Count)
            SongClicked?.Invoke(this, _songs[position]);
    }

    private void OnSongLongClick(int position, View anchor)
    {
        if (position >= 0 && position < _songs.Count)
        {
            LastLongClickedView = anchor;
            SongLongClicked?.Invoke(this, _songs[position]);
        }
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

    internal async Task<ConnectionProfile?> GetNetworkProfileAsync(ProtocolType protocol)
    {
        if (protocol == ProtocolType.Navidrome && _cachedNavidromeProfile != null) return _cachedNavidromeProfile;
        if (protocol == ProtocolType.WebDAV && _cachedWebDavProfile != null) return _cachedWebDavProfile;
        if (protocol == ProtocolType.SMB && _cachedSmbProfile != null) return _cachedSmbProfile;
        if (_networkMusic == null || _profilesLookedUp) return null;
        _profilesLookedUp = true;
        try
        {
            var profiles = await _networkMusic.GetProfilesAsync();
            _cachedNavidromeProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome && p.IsEnabled);
            _cachedWebDavProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.WebDAV && p.IsEnabled);
            _cachedSmbProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.SMB && p.IsEnabled);
            return protocol == ProtocolType.Navidrome ? _cachedNavidromeProfile
                : protocol == ProtocolType.SMB ? _cachedSmbProfile : _cachedWebDavProfile;
        }
        catch { return null; }
    }

    private static Bitmap? DecodeSampledBitmap(string path, int reqWidth, int reqHeight)
    {
        try
        {
            var options = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, options);

            var imageHeight = options.OutHeight;
            var imageWidth = options.OutWidth;
            if (imageWidth <= 0 || imageHeight <= 0) return null;

            int inSampleSize = 1;
            if (imageHeight > reqHeight || imageWidth > reqWidth)
            {
                var halfHeight = imageHeight / 2;
                var halfWidth = imageWidth / 2;
                while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth)
                    inSampleSize *= 2;
            }

            options.InJustDecodeBounds = false;
            options.InSampleSize = inSampleSize;
            options.InPreferredConfig = Bitmap.Config.Rgb565;
            return BitmapFactory.DecodeFile(path, options);
        }
        catch { return null; }
    }

    private class SongDiffCallback : DiffUtil.Callback
    {
        private readonly List<Song> _oldList;
        private readonly List<Song> _newList;

        public SongDiffCallback(List<Song> oldList, List<Song> newList)
        {
            _oldList = oldList;
            _newList = newList;
        }

        public override int OldListSize => _oldList.Count;
        public override int NewListSize => _newList.Count;

        public override bool AreItemsTheSame(int oldItemPosition, int newItemPosition)
            => _oldList[oldItemPosition].Id == _newList[newItemPosition].Id;

        public override bool AreContentsTheSame(int oldItemPosition, int newItemPosition)
        {
            var old = _oldList[oldItemPosition];
            var @new = _newList[newItemPosition];
            return old.Title == @new.Title
                && old.Artist == @new.Artist
                && old.Album == @new.Album
                && old.Source == @new.Source;
        }
    }

    private class SongViewHolder : RecyclerView.ViewHolder
    {
        private readonly TextView _title, _artist, _album;
        private readonly ImageView _cover;
        private readonly Helpers.WaveformView _pauseIcon;
        private int _boundSongId;
        private CancellationTokenSource? _coverCts;
        private string? _loadedCoverKey;
        private static Color? _cachedTitleColor;

        public SongViewHolder(View view, Action<int> onClick, Action<int, View>? onLongClick) : base(view)
        {
            _title = view.FindViewById<TextView>(Resource.Id.song_title)!;
            _artist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
            _album = view.FindViewById<TextView>(Resource.Id.song_album)!;
            _cover = view.FindViewById<ImageView>(Resource.Id.song_cover)!;
            _pauseIcon = view.FindViewById<Helpers.WaveformView>(Resource.Id.playing_pause_icon)!;
            _title.ImportantForAutofill = ImportantForAutofill.No;
            _artist.ImportantForAutofill = ImportantForAutofill.No;
            _album.ImportantForAutofill = ImportantForAutofill.No;
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
                _title.SetTextColor(Color.ParseColor("#9B7ED8"));
                _pauseIcon.SetPlaying(adapter._isPlaying);
            }
            else
            {
                if (_cachedTitleColor == null)
                {
                    var typedValue = new Android.Util.TypedValue();
                    _cachedTitleColor = ItemView.Context?.Theme?.ResolveAttribute(Resource.Attribute.catClawTextPrimary, typedValue, true) == true
                        ? new Color(typedValue.Data)
                        : Color.Black;
                }
                _title.SetTextColor(_cachedTitleColor.Value);
                _pauseIcon.SetPlaying(false);
            }

            _coverCts?.Cancel();
            _coverCts?.Dispose();
            _coverCts = new CancellationTokenSource();
            var ct = _coverCts.Token;

            var coverKey = $"cover_{song.Id}";
            var cachedBitmap = _bitmapCache.GetBitmap(coverKey);
            if (cachedBitmap != null && !cachedBitmap.IsRecycled)
            {
                _cover.SetImageBitmap(cachedBitmap);
                _loadedCoverKey = coverKey;
                return;
            }

            var coverPath = GetCoverCachedPath(song.Id);
            if (System.IO.File.Exists(coverPath))
            {
                if (_loadedCoverKey != coverKey)
                {
                    _loadedCoverKey = coverKey;
                    _ = LoadCachedCoverAsync(coverPath, coverKey, song.Id, ct);
                }
            }
            else
            {
                _loadedCoverKey = null;
                _cover.SetImageResource(Resource.Drawable.cover_default);

                if (!adapter._isScrolling)
                {
                    var loadKey = $"song_{song.Id}";
                    if (!_loadingCovers.ContainsKey(loadKey))
                    {
                        var loadTask = LoadCoverWithThrottleAsync(song, adapter, ct);
                        _loadingCovers[loadKey] = loadTask;
                        _ = loadTask.ContinueWith(_ => _loadingCovers.TryRemove(loadKey, out _));
                    }
                }
            }
        }

        private async Task LoadCachedCoverAsync(string coverPath, string coverKey, int songId, CancellationToken ct)
        {
            var bitmap = await Task.Run(() => DecodeSampledBitmap(coverPath, 120, 120), ct);
            ct.ThrowIfCancellationRequested();
            _mainHandler.Post(() =>
            {
                if (_boundSongId != songId) { bitmap?.Recycle(); return; }
                if (bitmap != null)
                {
                    _bitmapCache.PutBitmap(coverKey, bitmap);
                    _cover.SetImageBitmap(bitmap);
                }
                else
                {
                    _cover.SetImageResource(Resource.Drawable.cover_default);
                    try { System.IO.File.Delete(coverPath); } catch { }
                }
            });
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
                Application.Context.CacheDir!.AbsolutePath, "covers");
            return System.IO.Path.Combine(cacheDir, $"cover_{songId}.jpg");
        }

        private async Task LoadCoverWithThrottleAsync(Song song, SongAdapter adapter, CancellationToken ct)
        {
            await Task.Delay(100, ct).ConfigureAwait(false);
            await _coverLoadSemaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                byte[]? coverBytes = null;

                if (song.Source == SongSource.WebDAV || song.Source == SongSource.SMB)
                {
                    var coverId = song.CoverArtPath ?? song.RemoteId;
                    if (!string.IsNullOrEmpty(coverId))
                    {
                        try
                        {
                            var isNavidrome = !string.IsNullOrEmpty(song.FilePath) && song.FilePath.Contains("stream.view?id=");
                            var protocol = isNavidrome ? ProtocolType.Navidrome
                                : song.Source == SongSource.SMB ? ProtocolType.SMB : ProtocolType.WebDAV;
                            var profile = await adapter.GetNetworkProfileAsync(protocol);
                            if (profile != null && adapter._networkMusic != null)
                            {
                                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                                cts.CancelAfter(TimeSpan.FromSeconds(5));
                                var stream = await adapter._networkMusic.GetCoverAsync(coverId, profile);
                                if (stream != null)
                                {
                                    using var ms = new MemoryStream();
                                    await stream.CopyToAsync(ms, cts.Token);
                                    cts.Token.ThrowIfCancellationRequested();
                                    coverBytes = ms.ToArray();
                                }
                            }
                        }
                        catch (System.OperationCanceledException) { }
                        catch { }
                    }
                }
                else
                {
                    if (song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                    {
                        coverBytes = await Task.Run(() =>
                        {
                            try
                            {
                                var retriever = new Android.Media.MediaMetadataRetriever();
                                try
                                {
                                    retriever.SetDataSource(Application.Context,
                                        Android.Net.Uri.Parse(song.FilePath));
                                    var embedded = retriever.GetEmbeddedPicture();
                                    return embedded != null && embedded.Length > 0 ? embedded : null;
                                }
                                finally { retriever.Release(); }
                            }
                            catch { return null; }
                        }, ct);
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
                        var pluginManager = MainApplication.Services.GetService(typeof(IPluginManager)) as IPluginManager;
                        if (pluginManager != null)
                        {
                            var coverProviders = pluginManager.GetEnabledPlugins<ICoverProviderPlugin>();
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

                    var coverKey = $"cover_{song.Id}";
                    var bitmap = await Task.Run(() => DecodeSampledBitmap(coverPath, 120, 120), ct);
                    ct.ThrowIfCancellationRequested();

                    _mainHandler.Post(() =>
                    {
                        if (_boundSongId == song.Id)
                        {
                            if (bitmap != null)
                            {
                                _bitmapCache.PutBitmap(coverKey, bitmap);
                                _cover.SetImageBitmap(bitmap);
                                _loadedCoverKey = coverKey;
                            }
                            else
                            {
                                _cover.SetImageResource(Resource.Drawable.cover_default);
                                try { System.IO.File.Delete(coverPath); } catch { }
                            }
                        }
                        else
                        {
                            if (bitmap != null)
                                _bitmapCache.PutBitmap(coverKey, bitmap);
                            else
                                bitmap?.Recycle();
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

    public class ScrollListener : RecyclerView.OnScrollListener
    {
        private readonly SongAdapter _adapter;

        public ScrollListener(SongAdapter adapter)
        {
            _adapter = adapter;
        }

        public override void OnScrollStateChanged(RecyclerView recyclerView, int newState)
        {
            _adapter.SetScrolling(newState != 0);
            if (newState == 0)
            {
                recyclerView.Post(() =>
                {
                    var lm = recyclerView.GetLayoutManager() as LinearLayoutManager;
                    if (lm == null) return;
                    int first = lm.FindFirstVisibleItemPosition();
                    int last = lm.FindLastVisibleItemPosition();
                    for (int i = first; i <= last; i++)
                    {
                        var vh = recyclerView.FindViewHolderForAdapterPosition(i);
                        if (vh is SongViewHolder svh && svh.BindingAdapterPosition == i)
                        {
                            if (i < _adapter._songs.Count)
                            {
                                var song = _adapter._songs[i];
                                var coverKey = $"cover_{song.Id}";
                                var cachedBitmap = _bitmapCache.GetBitmap(coverKey);
                                if (cachedBitmap != null && !cachedBitmap.IsRecycled) continue;

                                var coverPath = GetCoverCachedPathStatic(song.Id);
                                if (!System.IO.File.Exists(coverPath))
                                {
                                    var loadKey = $"song_{song.Id}";
                                    if (!_loadingCovers.ContainsKey(loadKey))
                                        svh.Bind(song, _adapter);
                                }
                                else
                                {
                                    svh.Bind(song, _adapter);
                                }
                            }
                        }
                    }
                });
            }
        }

        private static string GetCoverCachedPathStatic(int songId)
        {
            var cacheDir = System.IO.Path.Combine(
                Application.Context.CacheDir!.AbsolutePath, "covers");
            return System.IO.Path.Combine(cacheDir, $"cover_{songId}.jpg");
        }
    }
}
