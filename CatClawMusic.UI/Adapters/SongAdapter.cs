using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using TagLibFile = TagLib.File;

namespace CatClawMusic.UI.Adapters;

public class SongAdapter : RecyclerView.Adapter
{
    private List<Song> _songs = new();
    public event EventHandler<Song>? SongClicked;

    public void UpdateSongs(IEnumerable<Song> songs)
    {
        _songs = songs.ToList();
        NotifyDataSetChanged();
    }

    public override int ItemCount => _songs.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_song, parent, false)!;
        return new SongViewHolder(view, OnSongClick);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        ((SongViewHolder)holder).Bind(_songs[position]);
    }

    private void OnSongClick(int position) => SongClicked?.Invoke(this, _songs[position]);

    private class SongViewHolder : RecyclerView.ViewHolder
    {
        private readonly TextView _title, _artist, _album;
        private readonly ImageView _cover;

        public SongViewHolder(View view, Action<int> onClick) : base(view)
        {
            _title = view.FindViewById<TextView>(Resource.Id.song_title)!;
            _artist = view.FindViewById<TextView>(Resource.Id.song_artist)!;
            _album = view.FindViewById<TextView>(Resource.Id.song_album)!;
            _cover = view.FindViewById<ImageView>(Resource.Id.song_cover)!;
            view.Click += (s, e) => onClick(BindingAdapterPosition);
        }

        public void Bind(Song song)
        {
            _title.Text = song.Title ?? "未知歌曲";
            _artist.Text = song.Artist ?? "未知艺术家";
            _album.Text = song.Album ?? "";

            // 加载封面：优先缓存，其次从文件提取
            var coverPath = GetCoverCachedPath(song.Id);
            if (System.IO.File.Exists(coverPath))
            {
                _cover.SetImageURI(global::Android.Net.Uri.Parse(coverPath));
            }
            else
            {
                _cover.SetImageResource(global::Android.Resource.Drawable.IcMenuGallery);
                // 后台提取封面
                Task.Run(() => ExtractAndCacheCover(song));
            }
        }

        private static string GetCoverCachedPath(int songId)
        {
            var cacheDir = Path.Combine(
                global::Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
            return Path.Combine(cacheDir, $"cover_{songId}.jpg");
        }

        private static void ExtractAndCacheCover(Song song)
        {
            try
            {
                byte[]? coverBytes = null;

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

                if (coverBytes != null)
                {
                    var coverPath = GetCoverCachedPath(song.Id);
                    Directory.CreateDirectory(Path.GetDirectoryName(coverPath)!);
                    System.IO.File.WriteAllBytes(coverPath, coverBytes);
                }
            }
            catch { /* 静默失败，封面非必需 */ }
        }
    }
}
