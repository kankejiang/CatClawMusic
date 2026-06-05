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

/// <summary>艺术家元数据匹配页面 - 显示所有艺术家列表</summary>
public class ArtistMatchFragment : Fragment
{
    private RecyclerView? _rvArtists;
    private ProgressBar? _progress;
    private EditText? _etSearch;
    private ArtistMatchAdapter? _adapter;
    private List<Artist> _allArtists = new();
    private Button? _btnAutoMatch;
    private string _autoMatchSource = "网易云";

    private static readonly Dictionary<string, string> SourceChipToName = new()
    {
        ["chip_netease"] = "网易云",
        ["chip_musicbrainz"] = "MusicBrainz",
        ["chip_wikidata"] = "Wikidata",
        ["chip_ai"] = "AI 搜索"
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

    /// <summary>一键自动匹配 - 跳过已有封面的艺术家，使用选定的来源</summary>
    private async Task AutoMatchAsync()
    {
        var scrapers = MainApplication.Services.GetServices<IArtistMetadataScraper>();
        var scraper = scrapers.FirstOrDefault(s => s.SourceName == _autoMatchSource);

        if (scraper == null)
        {
            Activity?.RunOnUiThread(() => Toast.MakeText(Context, "刮削服务未就绪", ToastLength.Short)?.Show());
            return;
        }

        // AI 搜索特殊检查
        if (scraper is AiArtistScraper aiScraper && !aiScraper.IsConfigured)
        {
            Activity?.RunOnUiThread(() => Toast.MakeText(Context, "AI 服务未配置，请先在设置中配置 AI 模型", ToastLength.Long)?.Show());
            return;
        }

        var neteaseScraper = MainApplication.Services.GetService<NetEaseMusicScraper>();
        var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
        var artists = await db.GetAllArtistsAsync();

        // 过滤出没有封面的艺术家
        var noCoverArtists = artists.Where(a =>
        {
            if (!string.IsNullOrEmpty(a.Cover) && System.IO.File.Exists(a.Cover)) return false;
            if (neteaseScraper != null)
            {
                var cachedPath = neteaseScraper.GetCachedCoverPath(a.Name);
                if (cachedPath != null) return false;
            }
            return true;
        }).ToList();

        if (noCoverArtists.Count == 0)
        {
            Activity?.RunOnUiThread(() => Toast.MakeText(Context, "所有艺术家都已有封面", ToastLength.Short)?.Show());
            return;
        }

        _btnAutoMatch!.Enabled = false;
        _btnAutoMatch.Text = $"匹配中 0/{noCoverArtists.Count}";
        _progress!.Visibility = ViewStates.Visible;

        var matched = 0;
        for (var i = 0; i < noCoverArtists.Count; i++)
        {
            var artist = noCoverArtists[i];
            try
            {
                string? cachePath = null;

                if (_autoMatchSource == "网易云" && neteaseScraper != null)
                {
                    // 网易云有专用的 GetArtistCoverAsync（自动搜索+下载）
                    cachePath = await neteaseScraper.GetArtistCoverAsync(artist.Name);
                }
                else
                {
                    // 其他来源：搜索 → 取第一个匹配 → 下载封面
                    var results = await scraper.SearchArtistsAsync(artist.Name, 3);
                    var bestMatch = results.FirstOrDefault(r =>
                        r.Name.Equals(artist.Name, StringComparison.OrdinalIgnoreCase))
                        ?? results.FirstOrDefault();
                    if (bestMatch?.CoverUrl != null)
                    {
                        cachePath = await scraper.DownloadAndCacheArtistCoverAsync(bestMatch.CoverUrl, artist.Name);
                    }
                }

                if (cachePath != null)
                {
                    artist.Cover = cachePath;
                    await db.UpdateArtistAsync(artist);
                    matched++;
                }
            }
            catch { }

            var idx = i + 1;
            Activity?.RunOnUiThread(() =>
            {
                _btnAutoMatch.Text = $"匹配中 {idx}/{noCoverArtists.Count}";
            });

            await Task.Delay(300);
        }

        Activity?.RunOnUiThread(() =>
        {
            _progress!.Visibility = ViewStates.Gone;
            _btnAutoMatch.Enabled = true;
            _btnAutoMatch.Text = "一键匹配";
            Toast.MakeText(Context, $"已匹配 {matched}/{noCoverArtists.Count} 位艺术家（{_autoMatchSource}）", ToastLength.Long)?.Show();
            _adapter?.UpdateArtists(_allArtists);
        });
    }
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
        _alias.Text = "";
        _info.Text = artist.Cover != null ? "已有封面" : "未设置封面";
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

        Android.Graphics.Bitmap? bitmap = null;
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

    public void CancelLoad()
    {
        _cts?.Cancel();
    }
}
