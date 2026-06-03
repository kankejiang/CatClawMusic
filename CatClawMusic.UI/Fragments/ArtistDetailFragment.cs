using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.ViewModels;
using Google.Android.Material.Button;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>艺术家详情页面</summary>
public class ArtistDetailFragment : Fragment
{
    private ImageView _artistPhoto = null!;
    private TextView _artistName = null!;
    private TextView _artistAlias = null!;
    private TextView _artistCountry = null!;
    private TextView _artistDesc = null!;
    private TextView _songCount = null!;
    private RecyclerView _songList = null!;
    private ExploreSongAdapter _songAdapter = null!;
    private INavigationService _navigationService = null!;
    private IAudioPlayerService? _audioPlayer;
    private PlayQueue? _playQueue;
    private ExploreDataService? _exploreData;
    private NetEaseMusicScraper? _scraper;
    private List<Song> _songs = new();
    private string _artistNameStr = "";

    public override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _artistNameStr = Arguments?.GetString("artistName", "") ?? "";
    }

    public static ArtistDetailFragment NewInstance(string artistName)
    {
        var args = new Bundle();
        args.PutString("artistName", artistName);
        var fragment = new ArtistDetailFragment { Arguments = args };
        return fragment;
    }

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_artist_detail, container, false)!;

    public override void OnViewCreated(View view, Bundle? savedInstanceState)
    {
        base.OnViewCreated(view, savedInstanceState);

        _navigationService = MainApplication.Services.GetRequiredService<INavigationService>();
        _audioPlayer = MainApplication.Services.GetService<IAudioPlayerService>();
        _playQueue = MainApplication.Services.GetService<PlayQueue>();

        var db = MainApplication.Services.GetService<MusicDatabase>();
        var library = MainApplication.Services.GetService<IMusicLibraryService>() as MusicLibraryService;
        if (db != null)
        {
            _exploreData = MainApplication.Services.GetService<ExploreDataService>();
            _scraper = MainApplication.Services.GetService<NetEaseMusicScraper>();
        }

        _artistPhoto = view.FindViewById<ImageView>(Resource.Id.iv_artist_photo)!;
        _artistName = view.FindViewById<TextView>(Resource.Id.tv_artist_name)!;
        _artistAlias = view.FindViewById<TextView>(Resource.Id.tv_artist_alias)!;
        _artistCountry = view.FindViewById<TextView>(Resource.Id.tv_artist_country)!;
        _artistDesc = view.FindViewById<TextView>(Resource.Id.tv_artist_desc)!;
        _songCount = view.FindViewById<TextView>(Resource.Id.tv_song_count)!;
        _songList = view.FindViewById<RecyclerView>(Resource.Id.rv_songs)!;

        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back)!;
        var btnPlayAll = view.FindViewById<MaterialButton>(Resource.Id.btn_play_all)!;
        var tvTitle = view.FindViewById<TextView>(Resource.Id.tv_title)!;

        tvTitle.Text = _artistNameStr;
        _artistName.Text = _artistNameStr;

        btnBack.Click += (s, e) => _navigationService.GoBack();

        _songAdapter = new ExploreSongAdapter();
        _songList.SetLayoutManager(new LinearLayoutManager(Context));
        _songList.SetAdapter(_songAdapter);
        _songAdapter.OnSongClick += async (s, song) => await PlaySongAsync(song);

        btnPlayAll.Click += (s, e) => PlayAll();

        LoadData();
    }

    private async void LoadData()
    {
        if (_exploreData == null || string.IsNullOrEmpty(_artistNameStr)) return;

        try
        {
            var songs = await _exploreData.GetSongsByArtistAsync(_artistNameStr);
            _songs = songs;

            Activity?.RunOnUiThread(() =>
            {
                _songCount.Text = $"{songs.Count} 首歌曲";
                _songAdapter.UpdateSongs(songs);
            });

            // 优先从本地歌曲加载艺术家封面
            var localCover = await Task.Run(() => LoadLocalArtistCover(songs));
            if (localCover != null)
            {
                Activity?.RunOnUiThread(() =>
                {
                    try { _artistPhoto.SetImageBitmap(localCover); } catch { }
                });
            }

            // 加载艺术家信息和网络封面（作为 fallback）
            if (_scraper != null)
            {
                var infoTask = _scraper.GetArtistInfoAsync(_artistNameStr);

                // 仅当本地封面加载失败时才尝试网络封面
                Task<string?>? coverTask = localCover == null
                    ? _scraper.GetArtistCoverAsync(_artistNameStr)
                    : null;

                if (coverTask != null)
                    await Task.WhenAll(coverTask, infoTask);
                else
                    await infoTask;

                if (coverTask != null)
                {
                    var coverPath = coverTask.Result;
                    if (coverPath != null)
                    {
                        Activity?.RunOnUiThread(() =>
                        {
                            try
                            {
                                var bitmap = Android.Graphics.BitmapFactory.DecodeFile(coverPath);
                                if (bitmap != null)
                                    _artistPhoto.SetImageBitmap(bitmap);
                            }
                            catch { }
                        });
                    }
                }

                var artistInfo = infoTask.Result;
                if (artistInfo != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        if (!string.IsNullOrEmpty(artistInfo.Alias))
                        {
                            _artistAlias.Text = artistInfo.Alias;
                            _artistAlias.Visibility = ViewStates.Visible;
                        }
                        if (!string.IsNullOrEmpty(artistInfo.Country))
                        {
                            _artistCountry.Text = artistInfo.Country;
                            _artistCountry.Visibility = ViewStates.Visible;
                        }
                        if (!string.IsNullOrEmpty(artistInfo.Description))
                        {
                            _artistDesc.Text = artistInfo.Description;
                            _artistDesc.Visibility = ViewStates.Visible;
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistDetail] 加载失败: {ex}");
        }
    }

    private Android.Graphics.Bitmap? LoadLocalArtistCover(List<Song> songs)
    {
        foreach (var song in songs)
        {
            // 1. 从歌曲的 CoverArtPath 加载
            try
            {
                if (!string.IsNullOrEmpty(song.CoverArtPath) && System.IO.File.Exists(song.CoverArtPath))
                {
                    var bitmap = Android.Graphics.BitmapFactory.DecodeFile(song.CoverArtPath);
                    if (bitmap != null) return bitmap;
                }
            }
            catch { }

            // 2. 通过 MediaStore 加载
            try
            {
                if (song.MediaStoreId > 0)
                {
                    var bitmap = Platforms.Android.MediaStoreCoverHelper.LoadCoverFromMediaStore(song.MediaStoreId, 480);
                    if (bitmap != null) return bitmap;
                }
            }
            catch { }

            // 3. 从文件提取嵌入封面
            try
            {
                if (!string.IsNullOrEmpty(song.FilePath)
                    && !song.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && !song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                {
                    var coverBytes = Core.Services.TagReader.ExtractCoverArt(song.FilePath);
                    if (coverBytes != null && coverBytes.Length > 0)
                    {
                        return Android.Graphics.BitmapFactory.DecodeByteArray(coverBytes, 0, coverBytes.Length);
                    }
                }
            }
            catch { }
        }
        return null;
    }

    private async Task PlaySongAsync(Song song)
    {
        try
        {
            if (_audioPlayer == null || _playQueue == null) return;
            var currentSongInQueue = _playQueue.CurrentSong;
            if (currentSongInQueue != null && currentSongInQueue.Id == song.Id)
            {
                if (_audioPlayer.IsPlaying) await _audioPlayer.PauseAsync();
                else await _audioPlayer.ResumeAsync();
            }
            else
            {
                _playQueue.SetSongs(_songs);
                _playQueue.SelectSong(song.Id);
                if (!string.IsNullOrEmpty(song.FilePath))
                    await _audioPlayer.PlayAsync(song.FilePath);
                _ = RecordPlayAsync(song);
                _navigationService.PushFragment("NowPlaying");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistDetail] 播放失败: {ex}");
        }
    }

    private async void PlayAll()
    {
        if (_songs.Count == 0 || _audioPlayer == null || _playQueue == null) return;
        try
        {
            _playQueue.SetSongs(_songs);
            _playQueue.SelectSong(_songs[0].Id);
            if (!string.IsNullOrEmpty(_songs[0].FilePath))
            {
                await _audioPlayer.PlayAsync(_songs[0].FilePath);
                _ = RecordPlayAsync(_songs[0]);
            }
            _navigationService.PushFragment("NowPlaying");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistDetail] 全部播放失败: {ex}");
        }
    }

    private async Task RecordPlayAsync(Song song)
    {
        try
        {
            var db = MainApplication.Services.GetService<MusicDatabase>();
            if (db == null) return;
            await db.EnsureInitializedAsync();
            await db.RecordPlayAsync(song.Id);
            var playlistVm = MainApplication.Services.GetService(typeof(PlaylistViewModel)) as PlaylistViewModel;
            if (playlistVm != null)
            {
                playlistVm.MarkDirty();
                _ = playlistVm.RefreshSystemPlaylistCountsAsync();
            }
        }
        catch { }
    }
}
