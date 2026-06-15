using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using System.Collections.Generic;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.ViewModels;
using Google.Android.Material.Button;
using Google.Android.Material.Chip;
using Google.Android.Material.Dialog;
using Google.Android.Material.TextField;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>艺术家详情页面</summary>
public class ArtistDetailFragment : Fragment
{
    private ImageView _artistPhoto = null!;
    private TextView _artistName = null!;
    private TextView _artistAlias = null!;
    private TextView _artistGender = null!;
    private TextView _artistBirthday = null!;
    private TextView _artistCountry = null!;
    private TextView _artistRealname = null!;      // 本名
    private TextView _artistExtraInfo = null!;     // 扩展信息（民族/出生地/星座/经纪公司等）
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
    private volatile bool _autoMatchCancelled; // 编辑弹窗保存后取消自动匹配

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
        _artistGender = view.FindViewById<TextView>(Resource.Id.tv_artist_gender)!;
        _artistBirthday = view.FindViewById<TextView>(Resource.Id.tv_artist_birthday)!;
        _artistCountry = view.FindViewById<TextView>(Resource.Id.tv_artist_country)!;
        _artistRealname = view.FindViewById<TextView>(Resource.Id.tv_artist_realname)!;
        _artistExtraInfo = view.FindViewById<TextView>(Resource.Id.tv_artist_extra_info)!;
        _artistDesc = view.FindViewById<TextView>(Resource.Id.tv_artist_desc)!;
        _songCount = view.FindViewById<TextView>(Resource.Id.tv_song_count)!;
        _songList = view.FindViewById<RecyclerView>(Resource.Id.rv_songs)!;

        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back)!;
        var btnEdit = view.FindViewById<ImageButton>(Resource.Id.btn_edit)!;
        var btnPlayAll = view.FindViewById<MaterialButton>(Resource.Id.btn_play_all)!;
        var tvTitle = view.FindViewById<TextView>(Resource.Id.tv_title)!;

        tvTitle.Text = _artistNameStr;
        _artistName.Text = _artistNameStr;

        btnBack.Click += (s, e) => _navigationService.GoBack();
        btnEdit.Click += (s, e) => ShowEditDialog();

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
            // 1. 优先从数据库 Artist 表读取已保存的信息
            var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
            Artist? artistRecord = null;
            try
            {
                var allArtists = await db.GetAllArtistsAsync();
                artistRecord = allArtists.FirstOrDefault(a => a.Name == _artistNameStr);
            }
            catch { }

            // 2. 如果数据库有信息，直接显示
            if (artistRecord != null)
            {
                Activity?.RunOnUiThread(() =>
                {
                    if (!string.IsNullOrEmpty(artistRecord.Gender))
                    {
                        _artistGender.Text = artistRecord.Gender + "  ·  ";
                        _artistGender.Visibility = ViewStates.Visible;
                    }
                    if (!string.IsNullOrEmpty(artistRecord.Birthday))
                    {
                        _artistBirthday.Text = artistRecord.Birthday + "  ·  ";
                        _artistBirthday.Visibility = ViewStates.Visible;
                    }
                    if (!string.IsNullOrEmpty(artistRecord.Region))
                    {
                        _artistCountry.Text = artistRecord.Region;
                        _artistCountry.Visibility = ViewStates.Visible;
                    }
                    if (!string.IsNullOrEmpty(artistRecord.Description))
                    {
                        _artistDesc.Text = artistRecord.Description;
                        _artistDesc.Visibility = ViewStates.Visible;
                    }
                });

                // 从 Artist.Cover 加载封面
                if (!string.IsNullOrEmpty(artistRecord.Cover) && System.IO.File.Exists(artistRecord.Cover))
                {
                    var coverPath = artistRecord.Cover;
                    await Task.Run(() =>
                    {
                        var bitmap = DecodeSampledBitmap(coverPath, 192, 192);
                        if (bitmap != null)
                        {
                            Activity?.RunOnUiThread(() =>
                            {
                                try { _artistPhoto.SetImageBitmap(bitmap); } catch { }
                            });
                        }
                    });
                }
            }

            // 3. 加载歌曲列表
            var songs = await _exploreData.GetSongsByArtistAsync(_artistNameStr);
            _songs = songs;

            Activity?.RunOnUiThread(() =>
            {
                _songCount.Text = $"{songs.Count} 首歌曲";
                _songAdapter.UpdateSongs(songs);
            });

            // 4. 如果数据库没有封面，从本地歌曲加载
            if (artistRecord == null || string.IsNullOrEmpty(artistRecord.Cover))
            {
                var localCover = await Task.Run(() => LoadLocalArtistCover(songs));
                if (localCover != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        try { _artistPhoto.SetImageBitmap(localCover); } catch { }
                    });
                }
            }

            // 5. 如果数据库缺少信息，从网络补充
            bool needGender = artistRecord == null || string.IsNullOrEmpty(artistRecord.Gender);
            bool needBirthday = artistRecord == null || string.IsNullOrEmpty(artistRecord.Birthday);
            bool needRegion = artistRecord == null || string.IsNullOrEmpty(artistRecord.Region);
            bool needDesc = artistRecord == null || string.IsNullOrEmpty(artistRecord.Description);
            bool needCover = artistRecord == null || string.IsNullOrEmpty(artistRecord.Cover);

            if (needGender || needBirthday || needRegion || needDesc || needCover)
            {
                await LoadOnlineInfoAsync(needCover, needGender, needBirthday, needRegion, needDesc, artistRecord, db);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistDetail] 加载失败: {ex}");
        }
    }

    private static readonly Dictionary<string, string> SourceChipToScraperName = new()
    {
        ["chip_ai"] = "AI搜索",
        ["chip_netease"] = "网易云",
        ["chip_qqmusic"] = "QQ音乐"
    };

    private async void ShowEditDialog()
    {
        if (Context == null) return;

        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        var scrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>().ToList();

        var view = LayoutInflater.From(Context)!.Inflate(Resource.Layout.dialog_artist_edit, null)!;
        var chipGroup = view.FindViewById<ChipGroup>(Resource.Id.chip_source_group)!;
        var btnSearch = view.FindViewById<MaterialButton>(Resource.Id.btn_search_metadata)!;
        var layoutSearching = view.FindViewById<LinearLayout>(Resource.Id.layout_searching)!;
        var tvResult = view.FindViewById<TextView>(Resource.Id.tv_match_result)!;
        var tilGender = view.FindViewById<TextInputLayout>(Resource.Id.til_gender)!;
        var tilRegion = view.FindViewById<TextInputLayout>(Resource.Id.til_region)!;
        var tilBirthday = view.FindViewById<TextInputLayout>(Resource.Id.til_birthday)!;
        var tilDesc = view.FindViewById<TextInputLayout>(Resource.Id.til_description)!;
        var btnCancel = view.FindViewById<MaterialButton>(Resource.Id.btn_cancel)!;
        var btnSave = view.FindViewById<MaterialButton>(Resource.Id.btn_save)!;

        // 加载当前艺术家数据
        Artist? artistRecord = null;
        try
        {
            var allArtists = await db.GetAllArtistsAsync();
            artistRecord = allArtists.FirstOrDefault(a => a.Name == _artistNameStr);
        }
        catch { }

        // 预填现有数据
        if (artistRecord != null)
        {
            if (tilGender.EditText != null) tilGender.EditText.Text = artistRecord.Gender ?? "";
            if (tilRegion.EditText != null) tilRegion.EditText.Text = artistRecord.Region ?? "";
            if (tilBirthday.EditText != null) tilBirthday.EditText.Text = artistRecord.Birthday ?? "";
            if (tilDesc.EditText != null) tilDesc.EditText.Text = artistRecord.Description ?? "";
        }

        var dialog = new MaterialAlertDialogBuilder(Context)
            .SetView(view)
            .SetCancelable(true)
            .Create()!;

        // 搜索元数据
        btnSearch.Click += async (s, e) =>
        {
            var selectedChipId = chipGroup.CheckedChipId;
            if (selectedChipId == View.NoId) return;

            btnSearch.Enabled = false;
            tvResult.Visibility = ViewStates.Gone;
            layoutSearching.Visibility = ViewStates.Visible;

            try
            {
                IArtistMetadataScraper? selectedScraper = null;
                foreach (var scraper in scrapers)
                {
                    if (scraper.SourceName == "AI搜索" && selectedChipId == Resource.Id.chip_ai)
                        selectedScraper = scraper;
                    else if (scraper.SourceName == "网易云" && selectedChipId == Resource.Id.chip_netease)
                        selectedScraper = scraper;
                    else if (scraper.SourceName == "QQ音乐" && selectedChipId == Resource.Id.chip_qqmusic)
                        selectedScraper = scraper;
                }

                if (selectedScraper != null)
                {
                    var results = await selectedScraper.SearchArtistsAsync(_artistNameStr, 3);
                    var best = results.FirstOrDefault(r =>
                        r.Name.Equals(_artistNameStr, StringComparison.OrdinalIgnoreCase))
                        ?? results.FirstOrDefault();

                    if (best != null)
                    {
                        // 自动填充到编辑框
                        System.Diagnostics.Debug.WriteLine($"[ArtistEdit] AI结果: Gender={best.Gender}, Region={best.Region}, Birthday={best.Birthday}, Desc={best.Description}");

                        if (tilGender.EditText != null && !string.IsNullOrWhiteSpace(best.Gender))
                            tilGender.EditText.Text = best.Gender;
                        if (tilRegion.EditText != null && !string.IsNullOrWhiteSpace(best.Region))
                            tilRegion.EditText.Text = CountryCodeToName(best.Region);
                        if (tilBirthday.EditText != null && !string.IsNullOrWhiteSpace(best.Birthday))
                            tilBirthday.EditText.Text = best.Birthday;
                        if (tilDesc.EditText != null && !string.IsNullOrWhiteSpace(best.Description))
                            tilDesc.EditText.Text = best.Description;

                        var sourceName = SourceChipToScraperName.Values.FirstOrDefault() ?? "AI搜索";
                        tvResult.Text = $"已从{sourceName}获取到信息，已自动填写";
                        tvResult.Visibility = ViewStates.Visible;
                    }
                    else
                    {
                        tvResult.Text = "未找到匹配结果";
                        tvResult.Visibility = ViewStates.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                tvResult.Text = $"搜索失败: {ex.Message}";
                tvResult.Visibility = ViewStates.Visible;
            }
            finally
            {
                layoutSearching.Visibility = ViewStates.Gone;
                btnSearch.Enabled = true;
            }
        };

        // 保存
        btnSave.Click += async (s, e) =>
        {
            var gender = tilGender.EditText?.Text?.Trim() ?? "";
            var region = tilRegion.EditText?.Text?.Trim() ?? "";
            var birthday = tilBirthday.EditText?.Text?.Trim() ?? "";
            var desc = tilDesc.EditText?.Text?.Trim() ?? "";

            try
            {
                await db.EnsureInitializedAsync();

                // 确保艺术家存在
                var artistId = await db.EnsureArtistAsync(_artistNameStr);
                var allArtists = await db.GetAllArtistsAsync();
                artistRecord = allArtists.FirstOrDefault(a => a.Id == artistId);

                if (artistRecord != null)
                {
                    artistRecord.Gender = string.IsNullOrEmpty(gender) ? null : gender;
                    artistRecord.Region = string.IsNullOrEmpty(region) ? null : region;
                    artistRecord.Birthday = string.IsNullOrEmpty(birthday) ? null : birthday;
                    artistRecord.Description = string.IsNullOrEmpty(desc) ? null : desc;
                    await db.UpdateArtistAsync(artistRecord);
                }

                dialog.Dismiss();

                // 停止后台自动匹配
                _autoMatchCancelled = true;

                // 刷新显示
                if (artistRecord != null)
                    RefreshArtistDisplay(artistRecord);

                Activity?.RunOnUiThread(() =>
                    Toast.MakeText(Context, "保存成功", ToastLength.Short)?.Show());
            }
            catch (Exception ex)
            {
                Activity?.RunOnUiThread(() =>
                    Toast.MakeText(Context, $"保存失败: {ex.Message}", ToastLength.Short)?.Show());
            }
        };

        // 取消
        btnCancel.Click += (s, e) => dialog.Dismiss();

        dialog.Show();

        // 设置 dialog 固定宽度
        dialog.Window?.SetLayout(
            (int)(Context.Resources!.DisplayMetrics!.WidthPixels * 0.92),
            ViewGroup.LayoutParams.WrapContent);
    }

    private void RefreshArtistDisplay(Artist artist)
    {
        _artistGender.Visibility = ViewStates.Gone;
        _artistBirthday.Visibility = ViewStates.Gone;
        _artistCountry.Visibility = ViewStates.Gone;
        _artistDesc.Visibility = ViewStates.Gone;

        if (!string.IsNullOrEmpty(artist.Gender))
        {
            _artistGender.Text = artist.Gender + "  ·  ";
            _artistGender.Visibility = ViewStates.Visible;
        }
        if (!string.IsNullOrEmpty(artist.Birthday))
        {
            _artistBirthday.Text = artist.Birthday + "  ·  ";
            _artistBirthday.Visibility = ViewStates.Visible;
        }
        if (!string.IsNullOrEmpty(artist.Region))
        {
            _artistCountry.Text = artist.Region;
            _artistCountry.Visibility = ViewStates.Visible;
        }
        if (!string.IsNullOrEmpty(artist.Description))
        {
            _artistDesc.Text = artist.Description;
            _artistDesc.Visibility = ViewStates.Visible;
        }
    }

    /// <summary>从网络来源补充缺失的艺术家信息</summary>
    private async Task LoadOnlineInfoAsync(bool needCover, bool needGender, bool needBirthday, bool needRegion, bool needDesc, Artist? artistRecord, MusicDatabase db)
    {
        var scrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>();

        // 尝试所有来源
        foreach (var scraper in scrapers)
        {
            if (_autoMatchCancelled) break;

            try
            {
                var results = await scraper.SearchArtistsAsync(_artistNameStr, 3);
                var best = results.FirstOrDefault(r =>
                    r.Name.Equals(_artistNameStr, StringComparison.OrdinalIgnoreCase))
                    ?? results.FirstOrDefault();
                if (best == null) continue;

                bool updated = false;

                Activity?.RunOnUiThread(() =>
                {
                    if (needGender && !string.IsNullOrEmpty(best.Gender) && _artistGender.Visibility != ViewStates.Visible)
                    {
                        _artistGender.Text = best.Gender + "  ·  ";
                        _artistGender.Visibility = ViewStates.Visible;
                    }
                    if (needBirthday && !string.IsNullOrEmpty(best.Birthday) && _artistBirthday.Visibility != ViewStates.Visible)
                    {
                        _artistBirthday.Text = best.Birthday + "  ·  ";
                        _artistBirthday.Visibility = ViewStates.Visible;
                    }
                    if (needRegion && !string.IsNullOrEmpty(best.Region) && _artistCountry.Visibility != ViewStates.Visible)
                    {
                        var region = CountryCodeToName(best.Region);
                        _artistCountry.Text = region;
                        _artistCountry.Visibility = ViewStates.Visible;
                    }
                    if (needDesc && !string.IsNullOrEmpty(best.Description) && _artistDesc.Visibility != ViewStates.Visible)
                    {
                        _artistDesc.Text = best.Description;
                        _artistDesc.Visibility = ViewStates.Visible;
                    }

                    // 扩展信息（本名、民族、出生地、星座、经纪公司、代表作品等）
                    var extraParts = new List<string>();
                    if (!string.IsNullOrEmpty(best.RealName))
                        extraParts.Add($"本名: {best.RealName}");
                    if (!string.IsNullOrEmpty(best.Nickname))
                        extraParts.Add($"昵称: {best.Nickname}");
                    if (!string.IsNullOrEmpty(best.Ethnicity))
                        extraParts.Add($"民族: {best.Ethnicity}");
                    if (!string.IsNullOrEmpty(best.BirthPlace))
                        extraParts.Add($"出生地: {best.BirthPlace}");
                    if (!string.IsNullOrEmpty(best.Education))
                        extraParts.Add($"毕业: {best.Education}");
                    if (!string.IsNullOrEmpty(best.Zodiac))
                        extraParts.Add($"星座: {best.Zodiac}");
                    if (!string.IsNullOrEmpty(best.Height))
                        extraParts.Add($"身高: {best.Height}");
                    if (!string.IsNullOrEmpty(best.Agency))
                        extraParts.Add($"经纪: {best.Agency}");
                    if (!string.IsNullOrEmpty(best.RepresentativeWorks))
                        extraParts.Add($"代表作: {best.RepresentativeWorks}");
                    if (!string.IsNullOrEmpty(best.Occupation))
                        extraParts.Add($"职业: {best.Occupation}");

                    // 显示本名（单独一行，更醒目）
                    if (extraParts.Any() && _artistRealname.Visibility != ViewStates.Visible)
                    {
                        Activity?.RunOnUiThread(() =>
                        {
                            // 第一行：本名（如果有的话）
                            if (!string.IsNullOrEmpty(best.RealName))
                            {
                                _artistRealname.Text = $"本名：{best.RealName}";
                                _artistRealname.Visibility = ViewStates.Visible;
                            }
                            // 第二行：其余扩展信息
                            var otherParts = extraParts.Where(p => !p.StartsWith("本名")).ToList();
                            if (otherParts.Count > 0)
                            {
                                _artistExtraInfo.Text = string.Join("  ·  ", otherParts);
                                _artistExtraInfo.Visibility = ViewStates.Visible;
                            }
                        });
                    }
                });

                // 封面
                if (needCover && !string.IsNullOrEmpty(best.CoverUrl))
                {
                    var cachePath = await scraper.DownloadAndCacheArtistCoverAsync(best.CoverUrl, _artistNameStr);
                    if (cachePath != null)
                    {
                        var bitmap = DecodeSampledBitmap(cachePath, 192, 192);
                        if (bitmap != null)
                        {
                            Activity?.RunOnUiThread(() =>
                            {
                                try { _artistPhoto.SetImageBitmap(bitmap); } catch { }
                            });
                        }

                        // 保存到数据库
                        if (artistRecord != null)
                        {
                            artistRecord.Cover = cachePath;
                            updated = true;
                        }
                    }
                }

                // 保存元数据到数据库
                if (artistRecord != null)
                {
                    if (!string.IsNullOrEmpty(best.Gender) && string.IsNullOrEmpty(artistRecord.Gender))
                    {
                        artistRecord.Gender = best.Gender;
                        updated = true;
                    }
                    if (!string.IsNullOrEmpty(best.Birthday) && string.IsNullOrEmpty(artistRecord.Birthday))
                    {
                        artistRecord.Birthday = best.Birthday;
                        updated = true;
                    }
                    if (!string.IsNullOrEmpty(best.Region) && string.IsNullOrEmpty(artistRecord.Region))
                    {
                        artistRecord.Region = CountryCodeToName(best.Region);
                        updated = true;
                    }
                    if (!string.IsNullOrEmpty(best.Description) && string.IsNullOrEmpty(artistRecord.Description))
                    {
                        artistRecord.Description = best.Description;
                        updated = true;
                    }

                    if (updated) await db.UpdateArtistAsync(artistRecord);
                }

                // 如果所有信息都已获取，停止尝试
                if (!needCover || (artistRecord?.Cover != null))
                    if (!needGender || (artistRecord?.Gender != null))
                        if (!needRegion || (artistRecord?.Region != null))
                            if (!needDesc || (artistRecord?.Description != null))
                                return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArtistDetail] {scraper.SourceName} 获取信息失败: {ex.Message}");
            }
        }

        // 最后尝试网易云 GetArtistInfoAsync（返回更详细的简介）
        if (_scraper != null && needDesc)
        {
            try
            {
                var artistInfo = await _scraper.GetArtistInfoAsync(_artistNameStr);
                if (artistInfo != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        if (!string.IsNullOrEmpty(artistInfo.Alias) && _artistAlias.Visibility != ViewStates.Visible)
                        {
                            _artistAlias.Text = artistInfo.Alias;
                            _artistAlias.Visibility = ViewStates.Visible;
                        }
                        if (!string.IsNullOrEmpty(artistInfo.Country) && _artistCountry.Visibility != ViewStates.Visible)
                        {
                            _artistCountry.Text = artistInfo.Country;
                            _artistCountry.Visibility = ViewStates.Visible;
                        }
                        if (!string.IsNullOrEmpty(artistInfo.Description) && _artistDesc.Visibility != ViewStates.Visible)
                        {
                            _artistDesc.Text = artistInfo.Description;
                            _artistDesc.Visibility = ViewStates.Visible;
                        }
                    });

                    // 保存到数据库
                    if (artistRecord != null)
                    {
                        bool updated = false;
                        if (!string.IsNullOrEmpty(artistInfo.Country) && string.IsNullOrEmpty(artistRecord.Region))
                        {
                            artistRecord.Region = artistInfo.Country;
                            updated = true;
                        }
                        if (!string.IsNullOrEmpty(artistInfo.Description) && string.IsNullOrEmpty(artistRecord.Description))
                        {
                            artistRecord.Description = artistInfo.Description;
                            updated = true;
                        }
                        if (updated) await db.UpdateArtistAsync(artistRecord);
                    }
                }
            }
            catch { }
        }
    }

    private Android.Graphics.Bitmap? LoadLocalArtistCover(List<Song> songs)
    {
        foreach (var song in songs)
        {
            try
            {
                if (!string.IsNullOrEmpty(song.CoverArtPath) && System.IO.File.Exists(song.CoverArtPath))
                {
                    var bitmap = DecodeSampledBitmap(song.CoverArtPath, 192, 192);
                    if (bitmap != null) return bitmap;
                }
            }
            catch { }

            try
            {
                if (song.MediaStoreId > 0)
                {
                    var bitmap = Platforms.Android.MediaStoreCoverHelper.LoadCoverFromMediaStore(song.MediaStoreId, 192);
                    if (bitmap != null) return bitmap;
                }
            }
            catch { }

            try
            {
                if (!string.IsNullOrEmpty(song.FilePath)
                    && !song.FilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && !song.FilePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
                {
                    var coverBytes = Core.Services.TagReader.ExtractCoverArt(song.FilePath);
                    if (coverBytes != null && coverBytes.Length > 0)
                    {
                        return DecodeSampledBitmapFromBytes(coverBytes, 192, 192);
                    }
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>降采样解码图片，避免 OOM</summary>
    private static Android.Graphics.Bitmap? DecodeSampledBitmap(string path, int reqWidth, int reqHeight)
    {
        var options = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        Android.Graphics.BitmapFactory.DecodeFile(path, options);
        var inSampleSize = CalculateInSampleSize(options, reqWidth, reqHeight);
        options.InJustDecodeBounds = false;
        options.InSampleSize = inSampleSize;
        options.InPreferredConfig = Android.Graphics.Bitmap.Config.Rgb565;
        return Android.Graphics.BitmapFactory.DecodeFile(path, options);
    }

    private static Android.Graphics.Bitmap? DecodeSampledBitmapFromBytes(byte[] bytes, int reqWidth, int reqHeight)
    {
        var options = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
        var inSampleSize = CalculateInSampleSize(options, reqWidth, reqHeight);
        options.InJustDecodeBounds = false;
        options.InSampleSize = inSampleSize;
        options.InPreferredConfig = Android.Graphics.Bitmap.Config.Rgb565;
        return Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options);
    }

    private static int CalculateInSampleSize(Android.Graphics.BitmapFactory.Options options, int reqWidth, int reqHeight)
    {
        var height = options.OutHeight;
        var width = options.OutWidth;
        var inSampleSize = 1;
        if (height > reqHeight || width > reqWidth)
        {
            var halfHeight = height / 2;
            var halfWidth = width / 2;
            while ((halfHeight / inSampleSize) >= reqHeight && (halfWidth / inSampleSize) >= reqWidth)
                inSampleSize *= 2;
        }
        return inSampleSize;
    }

    /// <summary>ISO 国家代码转可读名称</summary>
    private static string CountryCodeToName(string code) => code.ToUpperInvariant() switch
    {
        "CN" => "中国", "HK" => "中国香港", "TW" => "中国台湾", "MO" => "中国澳门",
        "JP" => "日本", "KR" => "韩国", "KP" => "朝鲜",
        "US" => "美国", "GB" => "英国", "UK" => "英国",
        "FR" => "法国", "DE" => "德国", "IT" => "意大利", "ES" => "西班牙",
        "RU" => "俄罗斯", "BR" => "巴西", "IN" => "印度",
        "AU" => "澳大利亚", "CA" => "加拿大", "NZ" => "新西兰",
        "SE" => "瑞典", "NO" => "挪威", "DK" => "丹麦", "FI" => "芬兰",
        "NL" => "荷兰", "BE" => "比利时", "CH" => "瑞士", "AT" => "奥地利",
        "PT" => "葡萄牙", "PL" => "波兰", "CZ" => "捷克",
        "IE" => "爱尔兰", "GR" => "希腊", "TR" => "土耳其",
        "TH" => "泰国", "VN" => "越南", "PH" => "菲律宾", "MY" => "马来西亚",
        "SG" => "新加坡", "ID" => "印度尼西亚", "MM" => "缅甸",
        "MX" => "墨西哥", "AR" => "阿根廷", "CL" => "智利", "CO" => "哥伦比亚",
        "IL" => "以色列", "SA" => "沙特阿拉伯", "AE" => "阿联酋",
        "ZA" => "南非", "EG" => "埃及", "NG" => "尼日利亚",
        _ => code
    };

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
