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

/// <summary>艺术家元数据匹配详情页 - 搜索并选择正确的封面</summary>
public class ArtistMatchDetailFragment : Fragment
{
    private RecyclerView? _rvResults;
    private ProgressBar? _progress;
    private TextView? _tvEmpty;
    private ImageView? _ivCurrentCover;
    private TextView? _tvArtistName;
    private TextView? _tvCoverStatus;
    private TextView? _tvArtistGender;
    private TextView? _tvArtistRegion;
    private TextView? _tvArtistDesc;
    private EditText? _etSearchKeyword;
    private ArtistSearchResultAdapter? _adapter;
    private string _artistName = "";
    private int _artistId;
    private string _currentSource = "网易云";

    // 来源名称 → Scraper 映射
    private static readonly Dictionary<string, string> SourceChipToName = new()
    {
        ["chip_netease"] = "网易云",
        ["chip_ai"] = "AI搜索",
        ["chip_qqmusic"] = "QQ音乐"
    };

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
        => inflater.Inflate(Resource.Layout.fragment_artist_match_detail, container, false)!;

    public override void OnViewCreated(View view, Bundle? savedInstanceState)
    {
        base.OnViewCreated(view, savedInstanceState);
        view.SetPadding(view.PaddingLeft, view.PaddingTop + MainActivity.StatusBarHeight, view.PaddingRight, view.PaddingBottom);

        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        _ivCurrentCover = view.FindViewById<ImageView>(Resource.Id.iv_current_cover);
        _tvArtistName = view.FindViewById<TextView>(Resource.Id.tv_artist_name);
        _tvCoverStatus = view.FindViewById<TextView>(Resource.Id.tv_cover_status);
        _tvArtistGender = view.FindViewById<TextView>(Resource.Id.tv_artist_gender);
        _tvArtistRegion = view.FindViewById<TextView>(Resource.Id.tv_artist_region);
        _tvArtistDesc = view.FindViewById<TextView>(Resource.Id.tv_artist_desc);
        _rvResults = view.FindViewById<RecyclerView>(Resource.Id.rv_search_results);
        _progress = view.FindViewById<ProgressBar>(Resource.Id.progress);
        _tvEmpty = view.FindViewById<TextView>(Resource.Id.tv_empty);
        _etSearchKeyword = view.FindViewById<EditText>(Resource.Id.et_search_keyword);

        _artistId = Arguments?.GetInt("artistId", 0) ?? 0;
        _artistName = Arguments?.GetString("artistName") ?? "";

        var tvTitle = view.FindViewById<TextView>(Resource.Id.tv_title);
        if (tvTitle != null) tvTitle.Text = _artistName;

        if (_tvArtistName != null) _tvArtistName.Text = _artistName;

        // 预填搜索关键字
        if (_etSearchKeyword != null)
        {
            _etSearchKeyword.Text = _artistName;
            _etSearchKeyword.EditorAction += (s, e) =>
            {
                if (e.ActionId == Android.Views.InputMethods.ImeAction.Search)
                {
                    var keyword = _etSearchKeyword.Text?.Trim() ?? "";
                    if (!string.IsNullOrEmpty(keyword))
                        SearchBySourceAsync(keyword, _currentSource);
                    Android.Views.InputMethods.InputMethodManager? imm =
                        Context?.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
                    imm?.HideSoftInputFromWindow(_etSearchKeyword.WindowToken, 0);
                }
            };
        }

        var btnSearch = view.FindViewById<ImageButton>(Resource.Id.btn_search);
        if (btnSearch != null)
        {
            btnSearch.Click += (s, e) =>
            {
                var keyword = _etSearchKeyword?.Text?.Trim() ?? _artistName;
                if (!string.IsNullOrEmpty(keyword))
                    SearchBySourceAsync(keyword, _currentSource);
                Android.Views.InputMethods.InputMethodManager? imm =
                    Context?.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
                if (_etSearchKeyword != null) imm?.HideSoftInputFromWindow(_etSearchKeyword.WindowToken, 0);
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
                    _currentSource = sourceName;
                    var keyword = _etSearchKeyword?.Text?.Trim() ?? _artistName;
                    if (!string.IsNullOrEmpty(keyword))
                        SearchBySourceAsync(keyword, _currentSource);
                }
            };
        }

        _adapter = new ArtistSearchResultAdapter();
        _adapter.OnResultClick += async (result) =>
        {
            await ApplyCoverAsync(result);
        };

        _rvResults!.SetLayoutManager(new LinearLayoutManager(Context));
        _rvResults.SetAdapter(_adapter);

        LoadCurrentCoverAsync();
        SearchBySourceAsync(_artistName, _currentSource);
        LoadArtistInfoAsync(); // 后台加载艺术家元数据
    }

    private async Task LoadCurrentCoverAsync()
    {
        try
        {
            var scraper = MainApplication.Services.GetService<NetEaseMusicScraper>();
            var cachedPath = scraper?.GetCachedCoverPath(_artistName);

            await Task.Run(() =>
            {
                Android.Graphics.Bitmap? bitmap = null;

                try
                {
                    var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
                    var artists = db.GetAllArtistsAsync().GetAwaiter().GetResult();
                    var artist = artists.FirstOrDefault(a => a.Id == _artistId);
                    if (artist?.Cover != null && System.IO.File.Exists(artist.Cover))
                    {
                        bitmap = DecodeSampledBitmap(artist.Cover, 112, 112);
                    }
                }
                catch { }

                if (bitmap == null && cachedPath != null)
                {
                    try { bitmap = DecodeSampledBitmap(cachedPath, 112, 112); } catch { }
                }

                if (bitmap != null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        try { _ivCurrentCover?.SetImageBitmap(bitmap); } catch { }
                        if (_tvCoverStatus != null) _tvCoverStatus.Text = "当前封面";
                    });
                }
                else
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        if (_tvCoverStatus != null) _tvCoverStatus.Text = "未设置封面";
                    });
                }
            });
        }
        catch { }
    }

    /// <summary>后台从多个来源加载艺术家性别、国籍和简介——并行搜索所有来源</summary>
    private async Task LoadArtistInfoAsync()
    {
        try
        {
            var allScrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>().ToList();
            if (allScrapers.Count == 0) return;

            // 并行搜索所有来源
            var searchTasks = allScrapers.Select(s => s.SearchArtistsAsync(_artistName, 3)).ToArray();
            var resultsArray = await Task.WhenAll(searchTasks);
            var allResults = resultsArray.SelectMany(r => r).ToList();

            // 合并结果，优先使用信息最完整的
            ArtistSearchResult? best = null;
            foreach (var r in allResults)
            {
                var score = 0;
                if (!string.IsNullOrEmpty(r.Gender)) score++;
                if (!string.IsNullOrEmpty(r.Region)) score++;
                if (!string.IsNullOrEmpty(r.Description)) score++;
                if (!string.IsNullOrEmpty(r.Birthday)) score++;

                if (score == 0) continue;
                if (best == null)
                {
                    best = r;
                }
                else
                {
                    var bestScore = 0;
                    if (!string.IsNullOrEmpty(best.Gender)) bestScore++;
                    if (!string.IsNullOrEmpty(best.Region)) bestScore++;
                    if (!string.IsNullOrEmpty(best.Description)) bestScore++;
                    if (!string.IsNullOrEmpty(best.Birthday)) bestScore++;
                    if (score > bestScore) best = r;
                }
            }

            if (best != null)
            {
                Activity?.RunOnUiThread(() => UpdateArtistInfo(new List<ArtistSearchResult> { best }));

                // 同时保存元数据到 DB 和 JSON 文件
                try
                {
                    var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
                    var artist = (await db.GetAllArtistsAsync()).FirstOrDefault(a => a.Name == _artistName);
                    if (artist != null)
                    {
                        if (!string.IsNullOrEmpty(best.Gender)) artist.Gender = best.Gender;
                        if (!string.IsNullOrEmpty(best.Birthday)) artist.Birthday = best.Birthday;
                        if (!string.IsNullOrEmpty(best.Region)) artist.Region = CountryCodeToName(best.Region);
                        if (!string.IsNullOrEmpty(best.Description)) artist.Description = best.Description;
                        await db.UpdateArtistAsync(artist);
                        await ArtistMetadataSaver.SaveAsync(artist, best);
                    }
                }
                catch (Exception ex)
                {
                    Android.Util.Log.Warn("CatClaw", $"LoadArtistInfoAsync 保存元数据失败: {ex.Message}");
                }
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistMatchDetail] 加载艺术家信息失败: {ex.Message}");
        }
    }

    /// <summary>按指定来源搜索</summary>
    private async Task SearchBySourceAsync(string keyword, string sourceName)
    {
        if (string.IsNullOrEmpty(keyword)) return;

        _progress!.Visibility = ViewStates.Visible;
        _tvEmpty!.Visibility = ViewStates.Gone;

        try
        {
            List<ArtistSearchResult> results;

            // 多来源并行搜索：同时搜索所有来源，合并结果
            if (sourceName == "多来源")
            {
                var allScrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>().ToList();
                var msp = MainApplication.Services.GetService<MultiSourcePhotoScraper>();
                if (msp != null && !allScrapers.Any(s => s is MultiSourcePhotoScraper))
                    allScrapers.Add(msp);

                var searchTasks = allScrapers.Select(s => s.SearchArtistsAsync(keyword, 3)).ToArray();
                var resultsArray = await Task.WhenAll(searchTasks);
                results = resultsArray.SelectMany(r => r).ToList();

                // 按名称分组，合并各来源的字段（相互补齐）
                results = results
                    .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var list = g.ToList();
                        if (list.Count == 1) return list[0];
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
                    .Take(10)
                    .ToList();

                Activity?.RunOnUiThread(() =>
                {
                    _progress.Visibility = ViewStates.Gone;
                    if (results.Count == 0)
                    {
                        _tvEmpty.Visibility = ViewStates.Visible;
                        _tvEmpty.Text = "未找到匹配结果";
                    }
                    else
                    {
                        _adapter?.UpdateResults(results);
                        UpdateArtistInfo(results);
                    }
                });
                return;
            }

            // 多源聚合来源（QQ音乐 / iTunes / Wikipedia）
            var multiSource = MainApplication.Services.GetService<MultiSourcePhotoScraper>();
            var sourcePrefix = sourceName switch
            {
                "QQ音乐" => "多源聚合·QQ",
                "iTunes" => "多源聚合·iTunes",
                "Wikipedia" => "多源聚合·Wikipedia",
                _ => null
            };

            if (multiSource != null && sourcePrefix != null)
            {
                results = await multiSource.SearchArtistsAsync(keyword, 10);
                results = results.Where(r => r.Source == sourcePrefix).ToList();
            }
            else
            {
                var scrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>();
                var scraper = scrapers.FirstOrDefault(s => s.SourceName == sourceName);

                if (scraper == null)
                {
                    Activity?.RunOnUiThread(() =>
                    {
                        _progress.Visibility = ViewStates.Gone;
                        _tvEmpty.Visibility = ViewStates.Visible;
                        _tvEmpty.Text = "刮削服务未就绪";
                    });
                    return;
                }

                results = await scraper.SearchArtistsAsync(keyword, 10);
            }

            Activity?.RunOnUiThread(() =>
            {
                _progress.Visibility = ViewStates.Gone;
                if (results.Count == 0)
                {
                    _tvEmpty.Visibility = ViewStates.Visible;
                    _tvEmpty.Text = "未找到匹配结果";
                }
                else
                {
                    _adapter?.UpdateResults(results);
                    // 从最佳匹配结果更新艺术家信息
                    UpdateArtistInfo(results);
                }
            });
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistMatchDetail] 搜索失败: {ex.Message}");
            Activity?.RunOnUiThread(() =>
            {
                _progress.Visibility = ViewStates.Gone;
                _tvEmpty.Visibility = ViewStates.Visible;
                _tvEmpty.Text = "搜索失败，请检查网络";
            });
        }
    }

    /// <summary>从搜索结果更新艺术家性别、生日、国籍/地区和简介</summary>
    private void UpdateArtistInfo(List<ArtistSearchResult> results)
    {
        var best = results.FirstOrDefault(r =>
            r.Name.Equals(_artistName, StringComparison.OrdinalIgnoreCase))
            ?? results.FirstOrDefault();
        if (best == null) return;

        if (!string.IsNullOrEmpty(best.Gender))
        {
            if (_tvArtistGender != null)
            {
                _tvArtistGender.Text = best.Gender + "  ·  ";
                _tvArtistGender.Visibility = ViewStates.Visible;
            }
        }

        if (!string.IsNullOrEmpty(best.Birthday))
        {
            // 在性别和国籍之间显示生日
            if (_tvArtistRegion != null)
            {
                _tvArtistRegion.Text = best.Birthday + "  ·  " + (best.Region != null ? CountryCodeToName(best.Region) : "");
                _tvArtistRegion.Visibility = ViewStates.Visible;
            }
        }
        else if (!string.IsNullOrEmpty(best.Region))
        {
            var region = CountryCodeToName(best.Region);
            if (_tvArtistRegion != null)
            {
                _tvArtistRegion.Text = region;
                _tvArtistRegion.Visibility = ViewStates.Visible;
            }
        }

        if (!string.IsNullOrEmpty(best.Description))
        {
            if (_tvArtistDesc != null)
            {
                _tvArtistDesc.Text = best.Description;
                _tvArtistDesc.Visibility = ViewStates.Visible;
            }
        }
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
        "IS" => "冰岛", "HU" => "匈牙利", "RO" => "罗马尼亚",
        "UA" => "乌克兰", "HR" => "克罗地亚", "RS" => "塞尔维亚",
        // Wikidata 实体 ID
        "Q148" => "中国", "Q17" => "日本", "Q884" => "韩国",
        "Q30" => "美国", "Q145" => "英国", "Q142" => "法国",
        "Q183" => "德国", "Q38" => "意大利", "Q39" => "瑞士",
        "Q40" => "奥地利", "Q29" => "西班牙", "Q159" => "俄罗斯",
        "Q408" => "澳大利亚", "Q16" => "加拿大", "Q55" => "荷兰",
        "Q35" => "丹麦", "Q20" => "挪威", "Q34" => "瑞典",
        "Q33" => "芬兰", "Q668" => "印度", "Q155" => "巴西",
        "Q96" => "墨西哥", "Q252" => "印度尼西亚", "Q869" => "泰国",
        "Q881" => "越南", "Q928" => "菲律宾", "Q833" => "马来西亚",
        "Q334" => "新加坡", "Q842" => "沙特阿拉伯", "Q79" => "埃及",
        _ => code
    };

    private async Task ApplyCoverAsync(ArtistSearchResult result)
    {
        if (string.IsNullOrEmpty(result.CoverUrl))
        {
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(Context, "该结果没有封面图片", ToastLength.Short)?.Show());
            return;
        }

        // 检查文件读写权限
        var permService = MainApplication.Services.GetService<CatClawMusic.Core.Interfaces.IPermissionService>();
        if (permService != null)
        {
            bool hasPermission;
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
            {
                hasPermission = await permService.CheckManageStoragePermissionAsync();
                if (!hasPermission)
                {
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(Context, "需要「管理所有文件」权限来保存照片到 CatClawMusic 目录", ToastLength.Long)?.Show());
                    await permService.RequestManageStoragePermissionAsync();
                    hasPermission = await permService.CheckManageStoragePermissionAsync();
                }
            }
            else
            {
                hasPermission = await permService.CheckStoragePermissionAsync();
                if (!hasPermission)
                {
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(Context, "需要文件读写权限来保存照片，正在请求权限…", ToastLength.Long)?.Show());
                    hasPermission = await permService.RequestStoragePermissionAsync();
                }
            }

            if (!hasPermission)
            {
                Activity?.RunOnUiThread(() =>
                    Toast.MakeText(Context, "未获得文件读写权限，照片将保存到应用专属目录", ToastLength.Long)?.Show());
            }
        }

        Activity?.RunOnUiThread(() =>
        {
            _progress!.Visibility = ViewStates.Visible;
        });

        try
        {
            // 多源聚合结果优先用 MultiSourcePhotoScraper 下载
            IArtistMetadataScraper? scraper = null;
            if (result.Source.StartsWith("多源聚合"))
            {
                scraper = MainApplication.Services.GetService<MultiSourcePhotoScraper>();
            }
            if (scraper == null)
            {
                var scrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>();
                scraper = scrapers.FirstOrDefault(s => s.SourceName == result.Source);
            }
            if (scraper == null)
            {
                scraper = MainApplication.Services.GetService<NetEaseMusicScraper>() as IArtistMetadataScraper;
            }
            if (scraper == null) return;

            var cachePath = await scraper.DownloadAndCacheArtistCoverAsync(result.CoverUrl, _artistName);

            if (cachePath != null)
            {
                var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
                var artists = await db.GetAllArtistsAsync();
                var artist = artists.FirstOrDefault(a => a.Id == _artistId);
                if (artist != null)
                {
                    artist.Cover = cachePath;
                    // 同时保存元数据
                    if (!string.IsNullOrEmpty(result.Gender)) artist.Gender = result.Gender;
                    if (!string.IsNullOrEmpty(result.Birthday)) artist.Birthday = result.Birthday;
                    if (!string.IsNullOrEmpty(result.Region)) artist.Region = CountryCodeToName(result.Region);
                    if (!string.IsNullOrEmpty(result.Description)) artist.Description = result.Description;
                    await db.UpdateArtistAsync(artist);
                    await ArtistMetadataSaver.SaveAsync(artist, result);
                }
            }

            Activity?.RunOnUiThread(() =>
            {
                _progress!.Visibility = ViewStates.Gone;

                if (cachePath != null && System.IO.File.Exists(cachePath))
                {
                    var bitmap = DecodeSampledBitmap(cachePath, 112, 112);
                    if (bitmap != null)
                    {
                        _ivCurrentCover?.SetImageBitmap(bitmap);
                        if (_tvCoverStatus != null) _tvCoverStatus.Text = "已更新封面";
                    }
                }

                Toast.MakeText(Context, "封面已更新", ToastLength.Short)?.Show();
            });
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ArtistMatchDetail] 应用封面失败: {ex.Message}");
            Activity?.RunOnUiThread(() =>
            {
                _progress!.Visibility = ViewStates.Gone;
                Toast.MakeText(Context, "更新失败", ToastLength.Short)?.Show();
            });
        }
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
}

/// <summary>搜索结果列表适配器（多来源）</summary>
public class ArtistSearchResultAdapter : RecyclerView.Adapter
{
    private List<ArtistSearchResult> _results = new();
    private static readonly ConcurrentDictionary<string, Android.Graphics.Bitmap?> _coverCache = new();
    private static readonly Android.OS.Handler _mainHandler = new(Android.OS.Looper.MainLooper!);

    public event Action<ArtistSearchResult>? OnResultClick;

    public void UpdateResults(List<ArtistSearchResult> results)
    {
        _results = results;
        NotifyDataSetChanged();
    }

    public override int ItemCount => _results.Count;

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_artist_search_result, parent, false)!;
        return new SearchResultViewHolder(view);
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is SearchResultViewHolder vh)
        {
            var result = _results[position];
            vh.Bind(result, _coverCache, _mainHandler);
            vh.ItemView.Click -= vh.OnClick;
            vh.ItemView.Click += vh.OnClick;
            vh.SetResult(result, OnResultClick);
        }
    }

    public override void OnViewRecycled(Java.Lang.Object holder)
    {
        if (holder is SearchResultViewHolder vh)
            vh.CancelLoad();
        base.OnViewRecycled(holder);
    }
}

public class SearchResultViewHolder : RecyclerView.ViewHolder
{
    private readonly ImageView _cover;
    private readonly TextView _name;
    private readonly TextView _alias;
    private readonly TextView _info;
    private readonly TextView _source;
    private ArtistSearchResult? _currentResult;
    private Action<ArtistSearchResult>? _clickHandler;
    private CancellationTokenSource? _cts;

    public SearchResultViewHolder(View view) : base(view)
    {
        _cover = view.FindViewById<ImageView>(Resource.Id.iv_cover)!;
        _name = view.FindViewById<TextView>(Resource.Id.tv_name)!;
        _alias = view.FindViewById<TextView>(Resource.Id.tv_alias)!;
        _info = view.FindViewById<TextView>(Resource.Id.tv_info)!;
        _source = view.FindViewById<TextView>(Resource.Id.tv_source)!;
    }

    public void SetResult(ArtistSearchResult result, Action<ArtistSearchResult>? clickHandler)
    {
        _currentResult = result;
        _clickHandler = clickHandler;
    }

    public void OnClick(object? sender, EventArgs e)
    {
        if (_currentResult != null && _clickHandler != null)
            _clickHandler.Invoke(_currentResult);
    }

    public void Bind(ArtistSearchResult result, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache, Android.OS.Handler mainHandler)
    {
        _name.Text = result.Name;
        _alias.Text = result.Alias ?? "";
        _alias.Visibility = string.IsNullOrEmpty(result.Alias) ? ViewStates.Gone : ViewStates.Visible;

        var infoParts = new List<string>();
        if (!string.IsNullOrEmpty(result.ExtraInfo)) infoParts.Add(result.ExtraInfo);
        if (!string.IsNullOrEmpty(result.Description)) infoParts.Add(result.Description);
        _info.Text = infoParts.Count > 0 ? string.Join(" · ", infoParts) : "";

        _source.Text = result.Source;

        var cacheKey = $"{result.Source}_{result.Id}";
        if (coverCache.TryGetValue(cacheKey, out var cached) && cached != null)
        {
            mainHandler.Post(() => { try { _cover.SetImageBitmap(cached); } catch { } });
            return;
        }

        _cover.SetImageResource(Resource.Drawable.ic_person);

        if (!string.IsNullOrEmpty(result.CoverUrl))
        {
            _ = LoadCoverAsync(result, cacheKey, coverCache, mainHandler);
        }
    }

    private async Task LoadCoverAsync(ArtistSearchResult result, string cacheKey, ConcurrentDictionary<string, Android.Graphics.Bitmap?> coverCache, Android.OS.Handler mainHandler)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        var coverUrl = result.CoverUrl!;

        Android.Graphics.Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(async () =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var handler = new System.Net.Http.HttpClientHandler { AllowAutoRedirect = true };
                    using var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(8) };
                    client.DefaultRequestHeaders.Add("User-Agent",
                        "CatClawMusic/1.0 (https://github.com/catclawmusic; contact@catclawmusic.local)");

                    var response = await client.GetAsync(coverUrl);
                    if (!response.IsSuccessStatusCode) return null;

                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    if (bytes.Length > 0)
                    {
                        return DecodeSampledBitmapFromBytes(bytes, 96, 96);
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
            coverCache.TryAdd(cacheKey, bitmap);
            mainHandler.Post(() =>
            {
                if (_currentResult?.Id != result.Id || _currentResult?.Source != result.Source) return;
                try { _cover.SetImageBitmap(bitmap); } catch { }
            });
        }
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

    public void CancelLoad()
    {
        _cts?.Cancel();
    }
}
