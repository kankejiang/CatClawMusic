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

/// <summary>专辑详情页面</summary>
public class AlbumDetailFragment : Fragment
{
    private ImageView _albumCover = null!;
    private TextView _albumName = null!;
    private TextView _albumArtist = null!;
    private TextView _songCount = null!;
    private TextView _albumYear = null!;
    private TextView _albumDesc = null!;
    private RecyclerView _songList = null!;
    private ExploreSongAdapter _songAdapter = null!;
    private INavigationService _navigationService = null!;
    private IAudioPlayerService? _audioPlayer;
    private PlayQueue? _playQueue;
    private ExploreDataService? _exploreData;
    private NetEaseMusicScraper? _scraper;
    private List<Song> _songs = new();
    private string _albumTitleStr = "";
    private string _albumArtistStr = "";

    public override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _albumTitleStr = Arguments?.GetString("album_title", "") ?? "";
        _albumArtistStr = Arguments?.GetString("album_artist", "") ?? "";
    }

    public static AlbumDetailFragment NewInstance(string albumTitle, string albumArtist)
    {
        var args = new Bundle();
        args.PutString("album_title", albumTitle);
        args.PutString("album_artist", albumArtist);
        var fragment = new AlbumDetailFragment { Arguments = args };
        return fragment;
    }

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_album_detail, container, false)!;

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

        _albumCover = view.FindViewById<ImageView>(Resource.Id.iv_album_cover)!;
        _albumName = view.FindViewById<TextView>(Resource.Id.tv_album_name)!;
        _albumArtist = view.FindViewById<TextView>(Resource.Id.tv_album_artist)!;
        _songCount = view.FindViewById<TextView>(Resource.Id.tv_song_count)!;
        _albumYear = view.FindViewById<TextView>(Resource.Id.tv_album_year)!;
        _albumDesc = view.FindViewById<TextView>(Resource.Id.tv_album_desc)!;
        _songList = view.FindViewById<RecyclerView>(Resource.Id.rv_songs)!;

        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back)!;
        var btnPlayAll = view.FindViewById<MaterialButton>(Resource.Id.btn_play_all)!;
        var tvTitle = view.FindViewById<TextView>(Resource.Id.tv_title)!;

        tvTitle.Text = _albumTitleStr;
        _albumName.Text = _albumTitleStr;
        _albumArtist.Text = _albumArtistStr;

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
        if (_exploreData == null || string.IsNullOrEmpty(_albumTitleStr)) return;

        try
        {
            var songs = await _exploreData.GetSongsByAlbumAsync(_albumTitleStr);
            _songs = songs;

            Activity?.RunOnUiThread(() =>
            {
                _songCount.Text = $"{songs.Count} 首歌曲";
                _songAdapter.UpdateSongs(songs);
            });

            // 加载专辑封面和信息
            if (_scraper != null)
            {
                var coverTask = _scraper.GetAlbumCoverAsync(_albumTitleStr, _albumArtistStr);
                var infoTask = _scraper.GetAlbumInfoAsync(_albumTitleStr, _albumArtistStr);
                await Task.WhenAll(coverTask, infoTask);

                var coverPath = coverTask.Result;
                if (coverPath != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        try
                        {
                            var bitmap = Android.Graphics.BitmapFactory.DecodeFile(coverPath);
                            if (bitmap != null)
                                _albumCover.SetImageBitmap(bitmap);
                        }
                        catch { }
                    });
                }

                var albumInfo = infoTask.Result;
                if (albumInfo != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        if (!string.IsNullOrEmpty(albumInfo.Year))
                        {
                            _albumYear.Text = albumInfo.Year;
                            _albumYear.Visibility = ViewStates.Visible;
                        }
                        if (!string.IsNullOrEmpty(albumInfo.Description))
                        {
                            _albumDesc.Text = albumInfo.Description;
                            _albumDesc.Visibility = ViewStates.Visible;
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AlbumDetail] 加载失败: {ex}");
        }
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
            System.Diagnostics.Debug.WriteLine($"[AlbumDetail] 播放失败: {ex}");
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
            System.Diagnostics.Debug.WriteLine($"[AlbumDetail] 全部播放失败: {ex}");
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
