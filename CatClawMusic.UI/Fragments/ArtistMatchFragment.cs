using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.UI.Platforms.Android;
using Microsoft.Extensions.DependencyInjection;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using System.Collections.Concurrent;
using CatClawMusic.UI.Helpers;

namespace CatClawMusic.UI.Fragments;

/// <summary>艺术家元数据匹配页面 - 显示所有艺术家列表</summary>
public class ArtistMatchFragment : Fragment
{
    private RecyclerView? _rvArtists;
    private ProgressBar? _progress;
    private EditText? _etSearch;
    private ArtistMatchAdapter? _adapter;
    private List<Artist> _allArtists = new();
    private Button? _btnAutoMatch;
    private string _autoMatchSource = "多来源";
    private bool _matchCover = true;
    private bool _matchGender = true;
    private bool _matchRegion = true;
    private bool _matchDesc = true;
    private bool _skipExisting = true; // true=跳过已有, false=重新匹配

    private static readonly Dictionary<string, string> SourceChipToName = new()
    {
        ["chip_netease"] = "网易云",
        ["chip_baidubaike"] = "百科",
        ["chip_douban"] = "豆瓣",
        ["chip_qqmusic"] = "QQ音乐"
    };

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
        => inflater.Inflate(Resource.Layout.fragment_artist_match, container, false)!;

    public override void OnViewCreated(View view, Bundle? savedInstanceState)
    {
        base.OnViewCreated(view, savedInstanceState);
        view.SetPadding(view.PaddingLeft, view.PaddingTop + MainActivity.StatusBarHeight, view.PaddingRight, view.PaddingBottom);

        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        _rvArtists = view.FindViewById<RecyclerView>(Resource.Id.rv_artists);
        _progress = view.FindViewById<ProgressBar>(Resource.Id.progress);
        _etSearch = view.FindViewById<EditText>(Resource.Id.et_search);
        _btnAutoMatch = view.FindViewById<Button>(Resource.Id.btn_auto_match);

        _adapter = new ArtistMatchAdapter();
        _adapter.OnArtistClick += (artist) =>
        {
            nav.PushFragment("ArtistMatchDetail", new Dictionary<string, object>
            {
                ["artistId"] = artist.Id,
                ["artistName"] = artist.Name
            });
        };

        _rvArtists!.SetLayoutManager(new LinearLayoutManager(Context));
        _rvArtists.SetAdapter(_adapter);

        _etSearch!.EditorAction += (s, e) =>
        {
            if (e.ActionId == Android.Views.InputMethods.ImeAction.Search)
            {
                FilterArtists(_etSearch.Text?.Trim() ?? "");
                Android.Views.InputMethods.InputMethodManager? imm =
                    Context?.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
                imm?.HideSoftInputFromWindow(_etSearch.WindowToken, 0);
            }
        };

        _etSearch.AfterTextChanged += (s, e) =>
        {
            FilterArtists(_etSearch.Text?.Trim() ?? "");
        };

        if (_btnAutoMatch != null)
        {
            _btnAutoMatch.Click += async (s, e) => await AutoMatchAsync();
        }

        // 匹配字段多选框
        var chipFieldCover = view.FindViewById<Google.Android.Material.Chip.Chip>(Resource.Id.chip_field_cover);
        var chipFieldGender = view.FindViewById<Google.Android.Material.Chip.Chip>(Resource.Id.chip_field_gender);
        var chipFieldRegion = view.FindViewById<Google.Android.Material.Chip.Chip>(Resource.Id.chip_field_region);
        var chipFieldDesc = view.FindViewById<Google.Android.Material.Chip.Chip>(Resource.Id.chip_field_desc);

        if (chipFieldCover != null) chipFieldCover.CheckedChange += (s, e) => _matchCover = e.IsChecked;
        if (chipFieldGender != null) chipFieldGender.CheckedChange += (s, e) => _matchGender = e.IsChecked;
        if (chipFieldRegion != null) chipFieldRegion.CheckedChange += (s, e) => _matchRegion = e.IsChecked;
        if (chipFieldDesc != null) chipFieldDesc.CheckedChange += (s, e) => _matchDesc = e.IsChecked;

        // 跳过已有 / 重新匹配 切换
        var chipModeGroup = view.FindViewById<Google.Android.Material.Chip.ChipGroup>(Resource.Id.chip_group_mode);
        if (chipModeGroup != null)
        {
            chipModeGroup.CheckedChange += (s, e) =>
            {
                var checkedId = chipModeGroup.CheckedChipId;
                var chipName = Context?.Resources?.GetResourceEntryName(checkedId) ?? "";
                _skipExisting = chipName == "chip_mode_skip";
            };
        }

        // 来源切换 Chips
        var chipGroup = view.FindViewById<Google.Android.Material.Chip.ChipGroup>(Resource.Id.chip_group_source);
        if (chipGroup != null)
        {
            chipGroup.CheckedChange += (s, e) =>
            {
                var checkedId = chipGroup.CheckedChipId;
                if (checkedId <= 0) return;
                var chipName = Context?.Resources?.GetResourceEntryName(checkedId) ?? "";
                if (SourceChipToName.TryGetValue(chipName, out var sourceName))
                {
                    _autoMatchSource = sourceName;
                }
            };
        }

        LoadArtistsAsync();
    }

    private void FilterArtists(string query)
    {
        if (_adapter == null) return;
        var filtered = string.IsNullOrEmpty(query)
            ? _allArtists
            : _allArtists.Where(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        _adapter.UpdateArtists(filtered);
    }

    private async Task LoadArtistsAsync()
    {
        try
        {
            _progress!.Visibility = ViewStates.Visible;
            var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
            await db.EnsureInitializedAsync();
            _allArtists = await db.GetAllArtistsAsync();
            Activity?.RunOnUiThread(() =>
            {
                _adapter?.UpdateArtists(_allArtists);
                _progress!.Visibility = ViewStates.Gone;
            });
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistMatch] 加载艺术家列表失败: {ex.Message}");
            Activity?.RunOnUiThread(() => _progress!.Visibility = ViewStates.Gone);
        }
    }

    /// <summary>检查艺术家是否缺少选中的字段</summary>
    private bool ArtistNeedsMatch(Artist artist)
    {
        if (_matchCover && (string.IsNullOrEmpty(artist.Cover) || !System.IO.File.Exists(artist.Cover)))
            return true;
        if (_matchGender && string.IsNullOrEmpty(artist.Gender))
            return true;
        if (_matchRegion && string.IsNullOrEmpty(artist.Region))
            return true;
        if (_matchDesc && string.IsNullOrEmpty(artist.Description))
            return true;
        return false;
    }

    /// <summary>一键自动匹配 - 根据选中的字段和来源</summary>
    private async Task AutoMatchAsync()
    {
        if (!_matchCover && !_matchGender && !_matchRegion && !_matchDesc)
        {
            Activity?.RunOnUiThread(() => Toast.MakeText(Context, "请至少选择一个匹配字段", ToastLength.Short)?.Show());
            return;
        }

        // 检查文件读写权限（刮削照片需要写入 /storage/emulated/0/CatClawMusic/）
        var permService = MainApplication.Services.GetService<CatClawMusic.Core.Interfaces.IPermissionService>();
        if (permService != null)
        {
            // Android 11+ 需要"管理所有文件"权限；旧版本检查常规存储权限
            bool hasPermission;
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
            {
                hasPermission = await permService.CheckManageStoragePermissionAsync();
                if (!hasPermission)
                {
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(Context, "需要「管理所有文件」权限来保存照片到 CatClawMusic 目录", ToastLength.Long)?.Show());
                    await permService.RequestManageStoragePermissionAsync();
                    // 用户需在系统设置中手动授权，返回后重新检查
                    hasPermission = await permService.CheckManageStoragePermissionAsync();
                }
            }
            else
            {
                hasPermission = await permService.CheckStoragePermissionAsync();
                if (!hasPermission)
                {
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(Context, "需要文件读写权限来保存艺术家照片，正在请求权限…", ToastLength.Long)?.Show());
                    hasPermission = await permService.RequestStoragePermissionAsync();
                }
            }

            if (!hasPermission)
            {
                Activity?.RunOnUiThread(() =>
                    Toast.MakeText(Context, "未获得文件读写权限，照片将保存到应用专属目录", ToastLength.Long)?.Show());
                // 不阻止匹配流程，照片会 fallback 到 app 专属目录
            }
        }

        // 先获取数据库和艺术家列表（多来源分支也需要）
        var neteaseScraper = MainApplication.Services.GetService<NetEaseMusicScraper>();
        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        var artists = await db.GetAllArtistsAsync();

        List<Artist> needMatchArtists;
        if (_skipExisting)
        {
            needMatchArtists = artists.Where(a =>
            {
                if (_matchCover)
                {
                    var hasCover = !string.IsNullOrEmpty(a.Cover) && System.IO.File.Exists(a.Cover);
                    if (!hasCover && neteaseScraper != null)
                    {
                        var cachedPath = neteaseScraper.GetCachedCoverPath(a.Name);
                        if (cachedPath != null) hasCover = true;
                    }
                    if (!hasCover) return true;
                }
                if (_matchGender && string.IsNullOrEmpty(a.Gender)) return true;
                if (_matchRegion && string.IsNullOrEmpty(a.Region)) return true;
                if (_matchDesc && string.IsNullOrEmpty(a.Description)) return true;
                if (string.IsNullOrEmpty(a.Birthday)) return true;
                return false;
            }).ToList();
        }
        else
        {
            needMatchArtists = artists;
        }

        if (needMatchArtists.Count == 0)
        {
            Activity?.RunOnUiThread(() => Toast.MakeText(Context, "所有艺术家已无缺少的字段", ToastLength.Short)?.Show());
            return;
        }

        // 多来源并行匹配：同时搜索所有来源，合并结果
        if (_autoMatchSource == "多来源")
        {
            await AutoMatchMultiSourceAsync(needMatchArtists, db);
            return;
        }

        // --- 以下为单来源匹配逻辑 ---
        var scrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>();
        var scraper = scrapers.FirstOrDefault(s => s.SourceName == _autoMatchSource);

        // 多源聚合来源（QQ音乐 / iTunes / Wikipedia）用 MultiSourcePhotoScraper
        if (scraper == null && MainApplication.Services.GetService<MultiSourcePhotoScraper>() is { } ms
            && new[] { "QQ音乐", "iTunes", "Wikipedia" }.Contains(_autoMatchSource))
        {
            scraper = ms;
        }

        if (scraper == null)
        {
            Activity?.RunOnUiThread(() => Toast.MakeText(Context, "刮削服务未就绪", ToastLength.Short)?.Show());
            return;
        }

        _btnAutoMatch!.Enabled = false;
        _btnAutoMatch.Text = $"匹配中 0/{needMatchArtists.Count}";
        _progress!.Visibility = ViewStates.Visible;

        var matched = 0;
        for (var i = 0; i < needMatchArtists.Count; i++)
        {
            var artist = needMatchArtists[i];
            try
            {
                ArtistSearchResult? bestMatch = null;

                if (_autoMatchSource == "网易云" && neteaseScraper != null)
                {
                    // 网易云：封面用专用方法，元数据从搜索结果 + 详情接口获取
                    if (_matchCover)
                    {
                        var cachePath = await neteaseScraper.GetArtistCoverAsync(artist.Name);
                        if (cachePath != null)
                            artist.Cover = cachePath;
                    }

                    // 搜索基本信息（获取 Description、CoverUrl）
                    if (_matchCover || _matchGender || _matchRegion || _matchDesc)
                    {
                        var results = await scraper.SearchArtistsAsync(artist.Name, 3);
                        bestMatch = results.FirstOrDefault(r =>
                            r.Name.Equals(artist.Name, StringComparison.OrdinalIgnoreCase))
                            ?? results.FirstOrDefault();
                    }

                    // 额外获取详细信息（性别/地区/生日/简介）—— 无论搜索结果如何都尝试
                    var needDetail = (_matchGender && string.IsNullOrEmpty(artist.Gender))
                                    || (_matchRegion && string.IsNullOrEmpty(artist.Region))
                                    || (_matchDesc && string.IsNullOrEmpty(artist.Description))
                                    || string.IsNullOrEmpty(artist.Birthday);
                    if (needDetail)
                    {
                        var artistInfo = await neteaseScraper.GetArtistInfoAsync(artist.Name);
                        if (artistInfo != null)
                        {
                            if (bestMatch == null) bestMatch = new ArtistSearchResult();
                            if (string.IsNullOrEmpty(bestMatch.Gender) && !string.IsNullOrEmpty(artistInfo.Gender))
                                bestMatch.Gender = artistInfo.Gender;
                            if (string.IsNullOrEmpty(bestMatch.Region) && !string.IsNullOrEmpty(artistInfo.Country))
                                bestMatch.Region = artistInfo.Country;
                            if (string.IsNullOrEmpty(bestMatch.Description) && !string.IsNullOrEmpty(artistInfo.Description))
                                bestMatch.Description = artistInfo.Description;
                            if (string.IsNullOrEmpty(bestMatch.Birthday) && !string.IsNullOrEmpty(artistInfo.Birthday))
                                bestMatch.Birthday = artistInfo.Birthday;
                        }
                    }
                }
                else
                {
                    // 其他来源：搜索 → 取最佳匹配
                    List<ArtistSearchResult>? results;
                    
                    // 多源聚合（QQ/iTunes/Wikipedia）走 MultiSourcePhotoScraper
                    var multiSource = MainApplication.Services.GetService<MultiSourcePhotoScraper>();
                    var sourcePrefix = _autoMatchSource switch
                    {
                        "QQ音乐" => "多源聚合·QQ",
                        "iTunes" => "多源聚合·iTunes",
                        "Wikipedia" => "多源聚合·Wikipedia",
                        _ => null
                    };
                    
                    if (multiSource != null && sourcePrefix != null)
                    {
                        results = await multiSource.SearchArtistsAsync(artist.Name, 10);
                        bestMatch = results.FirstOrDefault(r =>
                            r.Source == sourcePrefix && r.Name.Equals(artist.Name, StringComparison.OrdinalIgnoreCase))
                            ?? results.FirstOrDefault(r => r.Source == sourcePrefix)
                            ?? results.FirstOrDefault(r =>
                                r.Name.Equals(artist.Name, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        results = await scraper.SearchArtistsAsync(artist.Name, 3);
                        bestMatch = results.FirstOrDefault(r =>
                            r.Name.Equals(artist.Name, StringComparison.OrdinalIgnoreCase))
                            ?? results.FirstOrDefault();
                    }

                    // 封面
                    if (_matchCover && bestMatch?.CoverUrl != null)
                    {
                        var coverScraper = (multiSource != null && sourcePrefix != null) ? (IArtistMetadataScraper)multiSource : scraper;
                        var cachePath = await coverScraper.DownloadAndCacheArtistCoverAsync(bestMatch.CoverUrl, artist.Name);
                        if (cachePath != null)
                            artist.Cover = cachePath;
                    }
                }

                // 更新元数据字段
                if (bestMatch != null)
                {
                    if (_matchGender && !string.IsNullOrEmpty(bestMatch.Gender) && (_skipExisting ? string.IsNullOrEmpty(artist.Gender) : true))
                        artist.Gender = bestMatch.Gender;
                    if (_matchRegion && !string.IsNullOrEmpty(bestMatch.Region) && (_skipExisting ? string.IsNullOrEmpty(artist.Region) : true))
                        artist.Region = CountryCodeToName(bestMatch.Region);
                    if (_matchDesc && !string.IsNullOrEmpty(bestMatch.Description) && (_skipExisting ? string.IsNullOrEmpty(artist.Description) : true))
                        artist.Description = bestMatch.Description;
                    if (!string.IsNullOrEmpty(bestMatch.Birthday) && (_skipExisting ? string.IsNullOrEmpty(artist.Birthday) : true))
                        artist.Birthday = bestMatch.Birthday;
                }

                await db.UpdateArtistAsync(artist);
                // 保存元数据到公开目录
                await ArtistMetadataSaver.SaveAsync(artist, bestMatch);
                matched++;
            }
            catch { }

            var idx = i + 1;
            Activity?.RunOnUiThread(() =>
            {
                _btnAutoMatch.Text = $"匹配中 {idx}/{needMatchArtists.Count}";
            });

            await Task.Delay(300);
        }

        Activity?.RunOnUiThread(() =>
        {
            _progress!.Visibility = ViewStates.Gone;
            _btnAutoMatch.Enabled = true;
            _btnAutoMatch.Text = "一键匹配";
            Toast.MakeText(Context, $"已匹配 {matched}/{needMatchArtists.Count} 位艺术家（{_autoMatchSource}）", ToastLength.Long)?.Show();
            _adapter?.UpdateArtists(_allArtists);
        });
    }

    /// <summary>多来源并行一键匹配</summary>
    private async Task AutoMatchMultiSourceAsync(List<Artist> needMatchArtists, MusicDatabase db)
    {
        var allScrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>().ToList();
        if (allScrapers.Count == 0)
        {
            Activity?.RunOnUiThread(() => Toast.MakeText(Context, "没有可用的刮削服务", ToastLength.Short)?.Show());
            return;
        }

        _btnAutoMatch!.Enabled = false;
        _btnAutoMatch.Text = $"匹配中 0/{needMatchArtists.Count}";
        _progress!.Visibility = ViewStates.Visible;

        var matched = 0;
        for (var i = 0; i < needMatchArtists.Count; i++)
        {
            var artist = needMatchArtists[i];
            try
            {
                // 并行搜索所有来源
                var searchTasks = allScrapers.Select(s => s.SearchArtistsAsync(artist.Name, 3)).ToArray();
                var resultsArray = await Task.WhenAll(searchTasks);
                var allResults = resultsArray.SelectMany(r => r).ToList();

                // 按名称分组，合并各来源的字段（相互补齐）
                var mergedResults = allResults
                    .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var list = g.ToList();
                        if (list.Count == 1) return list[0];
                        // 以第一个结果为底，用其他来源的非空字段补齐
                        var first = list[0];
                        foreach (var r in list.Skip(1))
                        {
                            if (string.IsNullOrEmpty(first.Gender) && !string.IsNullOrEmpty(r.Gender))
                                first.Gender = r.Gender;
                            if (string.IsNullOrEmpty(first.Region) && !string.IsNullOrEmpty(r.Region))
                                first.Region = r.Region;
                            if (string.IsNullOrEmpty(first.Description) && !string.IsNullOrEmpty(r.Description))
                                first.Description = r.Description;
                            if (string.IsNullOrEmpty(first.Birthday) && !string.IsNullOrEmpty(r.Birthday))
                                first.Birthday = r.Birthday;
                            if (string.IsNullOrEmpty(first.CoverUrl) && !string.IsNullOrEmpty(r.CoverUrl))
                                first.CoverUrl = r.CoverUrl;
                            if (string.IsNullOrEmpty(first.Alias) && !string.IsNullOrEmpty(r.Alias))
                                first.Alias = r.Alias;
                        }
                        first.Source = string.Join("·", list.Select(x => x.Source.Split('·')[0].Trim()).Distinct());
                        return first;
                    })
                    .ToList();

                // 取最佳匹配
                var bestMatch = mergedResults
                    .FirstOrDefault(r => r.Name.Equals(artist.Name, StringComparison.OrdinalIgnoreCase))
                    ?? mergedResults.FirstOrDefault();

                // 如果最佳匹配来自网易云，额外获取详细信息补齐元数据
                if (bestMatch != null && bestMatch.Source.Contains("网易云"))
                {
                    var netease = allScrapers.OfType<NetEaseMusicScraper>().FirstOrDefault();
                    if (netease != null)
                    {
                        var artistInfo = await netease.GetArtistInfoAsync(artist.Name);
                        if (artistInfo != null)
                        {
                            if (string.IsNullOrEmpty(bestMatch.Gender) && !string.IsNullOrEmpty(artistInfo.Gender))
                                bestMatch.Gender = artistInfo.Gender;
                            if (string.IsNullOrEmpty(bestMatch.Region) && !string.IsNullOrEmpty(artistInfo.Country))
                                bestMatch.Region = artistInfo.Country;
                            if (string.IsNullOrEmpty(bestMatch.Description) && !string.IsNullOrEmpty(artistInfo.Description))
                                bestMatch.Description = artistInfo.Description;
                            if (string.IsNullOrEmpty(bestMatch.Birthday) && !string.IsNullOrEmpty(artistInfo.Birthday))
                                bestMatch.Birthday = artistInfo.Birthday;
                        }
                    }
                }

                if (bestMatch != null)
                {
                    // 下载封面：根据结果来源选用正确的 Scraper
                    if (_matchCover && !string.IsNullOrEmpty(bestMatch.CoverUrl))
                    {
                        IArtistMetadataScraper? coverScraper = null;
                        var sourcePrefix = bestMatch.Source.Split('·')[0].Trim();
                        coverScraper = allScrapers.FirstOrDefault(s => s.SourceName == sourcePrefix);
                        if (coverScraper == null && sourcePrefix == "多源聚合")
                            coverScraper = allScrapers.OfType<MultiSourcePhotoScraper>().FirstOrDefault();
                        if (coverScraper == null)
                            coverScraper = allScrapers.FirstOrDefault();

                        if (coverScraper != null)
                        {
                            var cachePath = await coverScraper.DownloadAndCacheArtistCoverAsync(bestMatch.CoverUrl, artist.Name);
                            if (cachePath != null)
                                artist.Cover = cachePath;
                        }
                    }

                    // 更新元数据字段
                    if (_matchGender && !string.IsNullOrEmpty(bestMatch.Gender)
                        && (_skipExisting ? string.IsNullOrEmpty(artist.Gender) : true))
                        artist.Gender = bestMatch.Gender;
                    if (_matchRegion && !string.IsNullOrEmpty(bestMatch.Region)
                        && (_skipExisting ? string.IsNullOrEmpty(artist.Region) : true))
                        artist.Region = CountryCodeToName(bestMatch.Region);
                    if (_matchDesc && !string.IsNullOrEmpty(bestMatch.Description)
                        && (_skipExisting ? string.IsNullOrEmpty(artist.Description) : true))
                        artist.Description = bestMatch.Description;
                    if (!string.IsNullOrEmpty(bestMatch.Birthday)
                        && (_skipExisting ? string.IsNullOrEmpty(artist.Birthday) : true))
                        artist.Birthday = bestMatch.Birthday;

                    await db.UpdateArtistAsync(artist);
                    matched++;
                }
            }
            catch { }

            var idx = i + 1;
            Activity?.RunOnUiThread(() =>
            {
                _btnAutoMatch!.Text = $"匹配中 {idx}/{needMatchArtists.Count}";
            });

            await Task.Delay(300);
        }

        Activity?.RunOnUiThread(() =>
        {
            _progress!.Visibility = ViewStates.Gone;
            _btnAutoMatch.Enabled = true;
            _btnAutoMatch.Text = "一键匹配";
            Toast.MakeText(Context, $"已匹配 {matched}/{needMatchArtists.Count} 位艺术家（多来源）", ToastLength.Long)?.Show();
            _adapter?.UpdateArtists(_allArtists);
        });
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
}

/// <summary>艺术家匹配列表适配器</summary>
public class ArtistMatchAdapter : RecyclerView.Adapter
{
    private List<Artist> _artists = new();
    private static readonly ConcurrentDictionary<string, Android.Graphics.Bitmap?> _coverCache = new();
    private static readonly Android.OS.Handler _mainHandler = new(Android.OS.Looper.MainLooper!);

    public event Action<Artist>? OnArtistClick;

    public void UpdateArtists(List<Artist> artists)
    {
        _artists = artists;
        NotifyDataSetChanged();
    }

    public override int ItemCount => _artists.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_artist_search_result, parent, false)!;
        return new ArtistMatchViewHolder(view);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is ArtistMatchViewHolder vh)
        {
            var artist = _artists[position];
            vh.Bind(artist, _coverCache, _mainHandler);
            vh.ItemView.Click -= vh.OnClick;
            vh.ItemView.Click += vh.OnClick;
            vh.SetArtist(artist, OnArtistClick);
        }
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is ArtistMatchViewHolder vh)
            vh.CancelLoad();
        base.OnViewRecycled(holder);
    }
}

public class ArtistMatchViewHolder : RecyclerView.ViewHolder
{
    private readonly ImageView _cover;
    private readonly TextView _name;
    private readonly TextView _alias;
    private readonly TextView _info;
    private readonly TextView _source;
    private Artist? _currentArtist;
    private Action<Artist>? _clickHandler;
    private CancellationTokenSource? _cts;

    public ArtistMatchViewHolder(View view) : base(view)
    {
        _cover = view.FindViewById<ImageView>(Resource.Id.iv_cover)!;
        _name = view.FindViewById<TextView>(Resource.Id.tv_name)!;
        _alias = view.FindViewById<TextView>(Resource.Id.tv_alias)!;
        _info = view.FindViewById<TextView>(Resource.Id.tv_info)!;
        _source = view.FindViewById<TextView>(Resource.Id.tv_source)!;
    }

    public void SetArtist(Artist artist, Action<Artist>? clickHandler)
    {
        _currentArtist = artist;
        _clickHandler = clickHandler;
    }

    public void OnClick(object? sender, EventArgs e)
    {
        if (_currentArtist != null && _clickHandler != null)
            _clickHandler?.Invoke(_currentArtist);
    }

    public void Bind(Artist artist, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache, Android.OS.Handler mainHandler)
    {
        _name.Text = artist.Name;

        // 性别 + 国籍
        var aliasParts = new List<string>();
        if (!string.IsNullOrEmpty(artist.Gender)) aliasParts.Add(artist.Gender);
        if (!string.IsNullOrEmpty(artist.Region)) aliasParts.Add(artist.Region);
        _alias.Text = aliasParts.Count > 0 ? string.Join(" · ", aliasParts) : "";
        _alias.Visibility = aliasParts.Count > 0 ? ViewStates.Visible : ViewStates.Gone;

        // 简介摘要（截断显示）
        if (!string.IsNullOrEmpty(artist.Description))
        {
            var desc = artist.Description.Length > 30 ? artist.Description[..30] + "…" : artist.Description;
            _info.Text = desc;
        }
        else
        {
            _info.Text = artist.Cover != null ? "已有封面" : "未设置封面";
        }

        _source.Text = "本地";
        _source.SetBackgroundColor(Android.Graphics.Color.Transparent);

        if (coverCache.TryGetValue(artist.Name, out var cached) && cached != null)
        {
            mainHandler.Post(() => { try { _cover.SetImageBitmap(cached); } catch { } });
            return;
        }

        _cover.SetImageResource(Resource.Drawable.ic_person);
        _ = LoadCoverAsync(artist, coverCache, mainHandler);
    }

    private async Task LoadCoverAsync(Artist artist, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache, Android.OS.Handler mainHandler)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var artistName = artist.Name;

        // 查询该艺术家第一首歌曲的封面路径作为 fallback
        string? sampleCover = null;
        try
        {
            if (string.IsNullOrEmpty(artist.Cover) || !System.IO.File.Exists(artist.Cover))
            {
                var db = MainApplication.Services.GetService<MusicDatabase>();
                if (db != null)
                {
                    var songs = await db.GetSongsByArtistAsync(artistName);
                    sampleCover = songs
                        .FirstOrDefault(s => !string.IsNullOrEmpty(s.CoverArtPath) && System.IO.File.Exists(s.CoverArtPath))?.CoverArtPath
                        ?? songs.FirstOrDefault(s => !string.IsNullOrEmpty(s.FilePath) && System.IO.File.Exists(s.FilePath))?.FilePath;
                }
            }
        }
        catch { }

        Android.Graphics.Bitmap? bitmap = null;
        var fallbackPath = sampleCover;
        try
        {
            bitmap = await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    if (!string.IsNullOrEmpty(artist.Cover) && System.IO.File.Exists(artist.Cover))
                    {
                        var b = DecodeSampledBitmap(artist.Cover, 96, 96);
                        if (b != null) return b;
                    }
                }
                catch { }

                ct.ThrowIfCancellationRequested();

                // 使用歌曲封面作为 fallback（未匹配前）
                if (!string.IsNullOrEmpty(fallbackPath) && System.IO.File.Exists(fallbackPath))
                {
                    try
                    {
                        var b = DecodeSampledBitmap(fallbackPath, 96, 96);
                        if (b != null) return b;
                    }
                    catch { }
                }

                ct.ThrowIfCancellationRequested();

                try
                {
                    var scraper = MainApplication.Services.GetService<NetEaseMusicScraper>();
                    var cachedPath = scraper?.GetCachedCoverPath(artistName);
                    if (cachedPath != null)
                    {
                        var b = DecodeSampledBitmap(cachedPath, 96, 96);
                        if (b != null) return b;
                    }
                }
                catch { }

                return null;
            }, ct);
        }
        catch (System.OperationCanceledException) { return; }
        catch { }

        if (ct.IsCancellationRequested) return;

        if (bitmap != null)
        {
            coverCache.TryAdd(artistName, bitmap);
            mainHandler.Post(() =>
            {
                if (_currentArtist?.Name != artistName) return;
                try { _cover.SetImageBitmap(bitmap); } catch { }
            });
        }
    }

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

    public void CancelLoad()
    {
        _cts?.Cancel();
    }
}
