using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using TagLibFile = TagLib.File;

namespace CatClawMusic.UI.Adapters;

public class SongAdapter : RecyclerView.Adapter
{
    private List<Song> _songs = new();
    private readonly INetworkMusicService? _networkMusic;
    private ConnectionProfile? _cachedProfile;
    private bool _profileLookedUp;

    public event EventHandler<Song>? SongClicked;

    public SongAdapter(INetworkMusicService? networkMusic = null)
    {
        _networkMusic = networkMusic;
    }

    public void UpdateSongs(IEnumerable<Song> songs)
    {
        _songs = songs.ToList();
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

    public override int ItemCount => _songs.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_song, parent, false)!;
        return new SongViewHolder(view, OnSongClick);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        ((SongViewHolder)holder).Bind(_songs[position], this);
    }

    private void OnSongClick(int position) => SongClicked?.Invoke(this, _songs[position]);

    /// <summary>懒加载获取已启用的 Navidrome 连接配置</summary>
    internal async Task<ConnectionProfile?> GetNetworkProfileAsync()
    {
        if (_cachedProfile != null) return _cachedProfile;
        if (_networkMusic == null || _profileLookedUp) return null;
        _profileLookedUp = true;
        try
        {
            var profiles = await _networkMusic.GetProfilesAsync();
            _cachedProfile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome && p.IsEnabled);
            return _cachedProfile;
        }
        catch { return null; }
    }

    private class SongViewHolder : RecyclerView.ViewHolder
    {
        private readonly TextView _title, _artist, _album;
        private readonly ImageView _cover;
        private int _boundSongId; // 当前绑定的歌曲 ID，防止封面加载错位

        public SongViewHolder(View view, Action<int> onClick) : base(view)
        {
            _title = view.FindViewById<TextView>(Resource.Id.song_title)!;
            _artist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
            _album = view.FindViewById<TextView>(Resource.Id.song_album)!;
            _cover = view.FindViewById<ImageView>(Resource.Id.song_cover)!;
            view.Click += (s, e) => onClick(BindingAdapterPosition);
        }

        public void Bind(Song song, SongAdapter adapter)
        {
            _title.Text = song.Title ?? "未知歌曲";
            _artist.Text = song.Artist ?? "未知艺术家";
            _album.Text = song.Album ?? "";
            _boundSongId = song.Id;

            // 加载封面：优先缓存，其次后台提取/下载
            var coverPath = GetCoverCachedPath(song.Id);
            if (System.IO.File.Exists(coverPath))
            {
                _cover.SetImageURI(global::Android.Net.Uri.Parse(coverPath));
            }
            else
            {
                _cover.SetImageResource(Resource.Drawable.cover_default);
                // 后台提取/下载封面
                Task.Run(() => LoadCoverAsync(song, adapter));
            }
        }

        private static string GetCoverCachedPath(int songId)
        {
            var cacheDir = System.IO.Path.Combine(
                global::Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
            return System.IO.Path.Combine(cacheDir, $"cover_{songId}.jpg");
        }

        /// <summary>后台加载封面（本地歌曲提取内嵌封面，网络歌曲通过 API 下载）</summary>
        private async Task LoadCoverAsync(Song song, SongAdapter adapter)
        {
            try
            {
                byte[]? coverBytes = null;

                if (song.Source == SongSource.WebDAV)
                {
                    // 网络歌曲：通过 Subsonic API 下载封面
                    if (!string.IsNullOrEmpty(song.CoverArtPath))
                    {
                        var profile = await adapter.GetNetworkProfileAsync();
                        if (profile != null && adapter._networkMusic != null)
                        {
                            var stream = await adapter._networkMusic.GetCoverAsync(song.CoverArtPath, profile);
                            if (stream != null)
                            {
                                using var ms = new MemoryStream();
                                await stream.CopyToAsync(ms);
                                coverBytes = ms.ToArray();
                                stream.Dispose();
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
                            if (tagFile.Tag.Pictures is { Length: > 0 })
                                coverBytes = tagFile.Tag.Pictures[0].Data.Data;
                        }
                    }
                    else if (System.IO.File.Exists(song.FilePath))
                    {
                        coverBytes = CatClawMusic.Core.Services.TagReader.ExtractCoverArt(song.FilePath);
                    }
                }

                if (coverBytes != null)
                {
                    // 缓存到本地
                    var coverPath = GetCoverCachedPath(song.Id);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(coverPath)!);
                    await System.IO.File.WriteAllBytesAsync(coverPath, coverBytes);

                    // 回到主线程更新 ImageView（检查 songId 防止错位）
                    var handler = new Handler(Looper.MainLooper!);
                    handler.Post(() =>
                    {
                        if (_boundSongId == song.Id && System.IO.File.Exists(coverPath))
                        {
                            _cover.SetImageURI(global::Android.Net.Uri.Parse(coverPath));
                        }
                    });
                }
            }
            catch { /* 静默失败，封面非必需 */ }
        }
    }
}
