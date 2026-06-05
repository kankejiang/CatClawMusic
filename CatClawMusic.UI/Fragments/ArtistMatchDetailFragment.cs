using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CatClawMusic.UI.Platforms.Android;
using Microsoft.Extensions.DependencyInjection;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using System.Collections.Concurrent;

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
    private EditText? _etSearchKeyword;
    private ArtistSearchResultAdapter? _adapter;
    private string _artistName = "";
    private int _artistId;
    private string _currentSource = "网易云";

    // 来源名称 → Scraper 映射
    private static readonly Dictionary<string, string> SourceChipToName = new()
    {
        ["chip_netease"] = "网易云",
        ["chip_musicbrainz"] = "MusicBrainz",
        ["chip_wikidata"] = "Wikidata",
        ["chip_ai"] = "AI 搜索"
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

    /// <summary>按指定来源搜索</summary>
    private async Task SearchBySourceAsync(string keyword, string sourceName)
    {
        if (string.IsNullOrEmpty(keyword)) return;

        _progress!.Visibility = ViewStates.Visible;
        _tvEmpty!.Visibility = ViewStates.Gone;

        try
        {
            var scrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>();
            var scraper = scrapers.FirstOrDefault(s => s.SourceName == sourceName);

            if (scraper == null)
            {
                Activity?.RunOnUiThread(() =>
                {
                    _progress.Visibility = ViewStates.Gone;
                    _tvEmpty.Visibility = ViewStates.Visible;
                    _tvEmpty.Text = sourceName == "AI 搜索" ? "AI 服务未配置，请先在设置中配置 AI 模型" : "刮削服务未就绪";
                });
                return;
            }

            // AI 搜索特殊检查
            if (scraper is AiArtistScraper aiScraper && !aiScraper.IsConfigured)
            {
                Activity?.RunOnUiThread(() =>
                {
                    _progress.Visibility = ViewStates.Gone;
                    _tvEmpty.Visibility = ViewStates.Visible;
                    _tvEmpty.Text = "AI 服务未配置，请先在设置中配置 AI 模型";
                });
                return;
            }

            var results = await scraper.SearchArtistsAsync(keyword, 10);

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

    private async Task ApplyCoverAsync(ArtistSearchResult result)
    {
        if (string.IsNullOrEmpty(result.CoverUrl))
        {
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(Context, "该结果没有封面图片", ToastLength.Short)?.Show());
            return;
        }

        Activity?.RunOnUiThread(() =>
        {
            _progress!.Visibility = ViewStates.Visible;
        });

        try
        {
            var scrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>();
            var scraper = scrapers.FirstOrDefault(s => s.SourceName == result.Source);
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
                    await db.UpdateArtistAsync(artist);
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
