using System.Collections.Specialized;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.ViewModels;
using CatClawMusic.UI.Helpers;
using Microsoft.Extensions.DependencyInjection;
using CoreModels = CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 音乐库Fragment，显示本地/网络歌曲列表，支持协议切换和刷新
/// </summary>
public class LibraryFragment : Fragment
{
    private LibraryViewModel _viewModel = null!;
    private RecyclerView _songList = null!;
    private TextView _statusText = null!;
    private Button _btnLocal = null!;
    private Button _btnNetwork = null!;
    private ImageButton _btnRefresh = null!;
    private LinearLayout _networkProtocolRow = null!;
    private Spinner _protocolSpinner = null!;
    private ArrayAdapter<string>? _protocolAdapter = null!;
    private EditText _searchBox = null!;
    private SongAdapter _adapter = null!;

    /// <summary>
    /// 创建音乐库视图
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        return inflater.Inflate(Resource.Layout.fragment_library, container, false)!;
    }

    /// <summary>
    /// 视图创建完成后初始化控件，绑定ViewModel和事件处理器
    /// </summary>
    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        var sp = MainApplication.Services;

        _viewModel = sp.GetRequiredService<LibraryViewModel>();
        _songList = view.FindViewById<RecyclerView>(Resource.Id.song_list)!;
        _songList.SetLayoutManager(new LinearLayoutManager(Context));
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;
        _btnLocal = view.FindViewById<Button>(Resource.Id.btn_local)!;
        _btnNetwork = view.FindViewById<Button>(Resource.Id.btn_network)!;
        _btnRefresh = view.FindViewById<ImageButton>(Resource.Id.btn_refresh)!;
        _searchBox = view.FindViewById<EditText>(Resource.Id.search_box)!;
        _networkProtocolRow = view.FindViewById<LinearLayout>(Resource.Id.network_protocol_row)!;
        _protocolSpinner = view.FindViewById<Spinner>(Resource.Id.spinner_protocol)!;

        _adapter = sp.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += OnSongClicked;
        _adapter.SongLongClicked += OnSongLongClicked;
        _songList.SetAdapter(_adapter);

        // 初始化 Spinner，先同步 ViewModel 已恢复的协议选择再绑定事件
        // 顺序很重要：如果 ItemSelected 事件先于 SetSelection 绑定，
        // Spinner 默认 position=0 的事件会将 ViewModel 中已恢复的正确值覆盖
        _protocolAdapter = new ArrayAdapter<string>(Context!,
            Android.Resource.Layout.SimpleSpinnerItem, _viewModel.ProtocolOptions);
        _protocolAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _protocolSpinner.Adapter = _protocolAdapter;
        _protocolSpinner.SetSelection(_viewModel.SelectedProtocolIndex);
        _protocolSpinner.ItemSelected += OnProtocolSelected;

        // 搜索框文本变化时同步到 ViewModel，由 OnSearchQueryChanged 触发列表过滤
        _searchBox.TextChanged += (s, e) =>
        {
            _viewModel.SearchQuery = e.Text?.ToString() ?? "";
        };
        // 键盘搜索键：收起键盘
        _searchBox.EditorAction += (s, e) =>
        {
            if (e.ActionId == Android.Views.InputMethods.ImeAction.Search)
            {
                var imm = (InputMethodManager?)Context?.GetSystemService(
                    Android.Content.Context.InputMethodService);
                imm?.HideSoftInputFromWindow(_searchBox.WindowToken, 0);
            }
        };

        _btnLocal.Click += (s, e) => _viewModel.SwitchTabCommand.Execute("Local");
        _btnNetwork.Click += (s, e) => _viewModel.SwitchTabCommand.Execute("Network");
        _btnRefresh.Click += (s, e) => _viewModel.RefreshCommand.Execute(null);

        BindViews();
        if (_viewModel.CurrentTab == "Network")
        {
            _btnLocal.SetTextColor(Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#4A0072")));
            _btnNetwork.SetTextColor(Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.White));
            _btnLocal.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#C0B8CA"));
            _btnNetwork.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#9B7ED8"));
            _networkProtocolRow.Visibility = ViewStates.Visible;
            _ = _viewModel.LoadNetworkAsync();
        }
        else
        {
            _ = _viewModel.LoadLocalAsync();
        }
        if (_viewModel.Songs.Count > 0)
            _adapter.UpdateSongs(_viewModel.Songs);
    }

    /// <summary>
    /// 绑定ViewModel属性变化事件和集合变化事件
    /// </summary>
    private void BindViews()
    {
        BindingHelper.BindText(_statusText, _viewModel, nameof(_viewModel.StatusText), _ => _viewModel.StatusText);

        _viewModel.Songs.CollectionChanged += OnSongsCollectionChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 搜索过滤变化时全量刷新列表
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.FilteredSongs))
            {
                Activity?.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.FilteredSongs));
            }
        };

        UpdateTabButtonColor(_btnLocal, _viewModel.LocalTabColor, true);
        UpdateTabButtonColor(_btnNetwork, _viewModel.NetworkTabColor, false);
    }

    /// <summary>
    /// 解绑ViewModel事件，防止内存泄漏
    /// </summary>
    private void UnbindViews()
    {
        _viewModel.Songs.CollectionChanged -= OnSongsCollectionChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _protocolSpinner.ItemSelected -= OnProtocolSelected;
    }

    /// <summary>
    /// ViewModel属性变化时更新Tab按钮颜色和协议选择区域可见性
    /// </summary>
    private void OnViewModelPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.LocalTabColor))
        {
            UpdateTabButtonColor(_btnLocal, _viewModel.LocalTabColor, _viewModel.CurrentTab == "Local");
            // 切换到本地 Tab 时隐藏协议选择
            _networkProtocolRow.Visibility = ViewStates.Gone;
        }
        else if (e.PropertyName == nameof(_viewModel.NetworkTabColor))
        {
            UpdateTabButtonColor(_btnNetwork, _viewModel.NetworkTabColor, _viewModel.CurrentTab == "Network");
            // 切换到网络 Tab 时显示协议选择
            _networkProtocolRow.Visibility = _viewModel.CurrentTab == "Network" ? ViewStates.Visible : ViewStates.Gone;
        }
    }

    /// <summary>
    /// 协议选择变化时重新加载网络歌曲列表
    /// </summary>
    private void OnProtocolSelected(object? sender, AdapterView.ItemSelectedEventArgs e)
    {
        if (_viewModel.SelectedProtocolIndex != e.Position)
        {
            _viewModel.SelectedProtocolIndex = e.Position;
            // 协议变化时重新加载
            if (_viewModel.CurrentTab == "Network")
            {
                _viewModel.Songs.Clear();
                _ = _viewModel.LoadNetworkAsync();
            }
        }
    }

    /// <summary>
    /// Fragment销毁时解绑事件
    /// </summary>
    public override void OnDestroyView()
    {
        UnbindViews();
        base.OnDestroyView();
    }

    /// <summary>
    /// 歌曲列表集合变化时增量更新适配器，有搜索过滤时使用过滤结果
    /// </summary>
    private void OnSongsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var a = Activity;
        if (a == null) return;

        // 搜索激活时使用过滤列表全量刷新，确保显示正确
        if (!string.IsNullOrWhiteSpace(_viewModel.SearchQuery))
        {
            a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.FilteredSongs));
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems != null && e.NewItems.Count > 0)
                {
                    var newSongs = e.NewItems.Cast<CoreModels.Song>().ToList();
                    a.RunOnUiThread(() => _adapter.AddRange(newSongs));
                }
                break;

            case NotifyCollectionChangedAction.Reset:
                a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.Songs));
                break;

            case NotifyCollectionChangedAction.Remove:
                a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.Songs));
                break;

            default:
                a.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.Songs));
                break;
        }
    }

    /// <summary>
    /// 更新Tab按钮的背景色和文字颜色
    /// </summary>
    private static void UpdateTabButtonColor(Button btn, string hexColor, bool isActive)
    {
        var color = Android.Graphics.Color.ParseColor(hexColor);
        btn.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(color);
        btn.SetTextColor(isActive
            ? Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.White)
            : Android.Content.Res.ColorStateList.ValueOf(
                Android.Graphics.Color.ParseColor("#4A0072"))); // primaryDark
    }

    /// <summary>
    /// 歌曲点击时设置播放队列并开始播放
    /// </summary>
    private void OnSongClicked(object? sender, CoreModels.Song song)
    {
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        queue.SetSongs(_viewModel.Songs);
        queue.SelectSong(song.Id);
        _ = MainApplication.Services.GetRequiredService<IAudioPlayerService>().PlayAsync(song.FilePath);
        MainApplication.Services.GetRequiredService<NowPlayingViewModel>().SyncWithQueue();
        _ = MainApplication.Services.GetRequiredService<MusicDatabase>().RecordPlayAsync(song.Id);
    }

    /// <summary>
    /// 歌曲长按时显示上下文菜单，包含插件菜单项（如匹配元数据）
    /// </summary>
    private void OnSongLongClicked(object? sender, CoreModels.Song song)
    {
        var anchor = _adapter.LastLongClickedView ?? _songList;
        ShowSongContextMenu(anchor, song);
    }

    /// <summary>
    /// 显示歌曲右键上下文菜单，毛玻璃圆角卡片风格
    /// </summary>
    private void ShowSongContextMenu(View anchor, CoreModels.Song song)
    {
        var ctx = Context;
        if (ctx == null) return;

        var density = ctx.Resources!.DisplayMetrics!.Density;
        var dp = (int)density;

        var menuItems = new List<(string Title, Action Action)>();

        menuItems.Add(("▶  播放", () => OnSongClicked(this, song)));
        menuItems.Add(("⏭  下一首播放", () =>
        {
            var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
            queue.AddNext(song);
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(ctx, $"已添加: {song.Title}", ToastLength.Short)?.Show());
        }));
        menuItems.Add(("📋  添加到歌单", () => { }));
        menuItems.Add(("❤  收藏", () => { }));
        menuItems.Add(("ℹ  歌曲详情", () => { }));

        var pluginManager = MainApplication.Services.GetService<IPluginManager>();
        if (pluginManager != null)
        {
            var contributors = pluginManager.GetEnabledPlugins<IMenuContributorPlugin>();
            foreach (var contributor in contributors)
            {
                try
                {
                    foreach (var entry in contributor.GetMenuItems(song))
                    {
                        if (string.IsNullOrEmpty(entry.Title)) continue;
                        var plugin = contributor;
                        var entryId = entry.Id;
                        menuItems.Add(("🏷  " + entry.Title, async () =>
                        {
                            await plugin.OnMenuItemClicked(entryId, song, this);
                        }));
                    }
                }
                catch { }
            }
        }

        var cardLayout = new LinearLayout(ctx)
        { Orientation = Orientation.Vertical };
        cardLayout.SetPadding(dp * 6, dp * 8, dp * 6, dp * 8);

        var bgDrawable = new Android.Graphics.Drawables.GradientDrawable();
        bgDrawable.SetShape(Android.Graphics.Drawables.ShapeType.Rectangle);
        bgDrawable.SetCornerRadius(24 * density);
        bgDrawable.SetColor(Android.Graphics.Color.ParseColor("#E6281E36"));
        bgDrawable.SetStroke(dp, Android.Graphics.Color.ParseColor("#44FFFFFF"));
        cardLayout.Background = bgDrawable;

        var titleTv = new TextView(ctx) { Text = song.Title ?? "未知歌曲" };
        titleTv.SetTextSize(Android.Util.ComplexUnitType.Sp, 14f);
        titleTv.SetTextColor(Android.Graphics.Color.ParseColor("#E8E0F0"));
        titleTv.SetTypeface(null, Android.Graphics.TypefaceStyle.Bold);
        titleTv.SetPadding(dp * 14, dp * 10, dp * 14, dp * 4);
        titleTv.SetSingleLine(true);
        titleTv.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        cardLayout.AddView(titleTv);

        var subtitleTv = new TextView(ctx) { Text = song.Artist ?? "未知艺术家" };
        subtitleTv.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        subtitleTv.SetTextColor(Android.Graphics.Color.ParseColor("#B0A8BA"));
        subtitleTv.SetPadding(dp * 14, 0, dp * 14, dp * 10);
        subtitleTv.SetSingleLine(true);
        subtitleTv.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        cardLayout.AddView(subtitleTv);

        var divider = new View(ctx);
        divider.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 1);
        divider.SetBackgroundColor(Android.Graphics.Color.ParseColor("#20FFFFFF"));
        cardLayout.AddView(divider);

        foreach (var item in menuItems)
        {
            var itemLayout = new LinearLayout(ctx)
            { Orientation = Orientation.Horizontal };
            itemLayout.SetGravity(Android.Views.GravityFlags.CenterVertical);
            itemLayout.SetPadding(dp * 8, dp * 6, dp * 8, dp * 6);
            itemLayout.SetBackgroundColor(Android.Graphics.Color.Transparent);

            var tv = new TextView(ctx) { Text = item.Title };
            tv.SetTextSize(Android.Util.ComplexUnitType.Sp, 14f);
            tv.SetTextColor(Android.Graphics.Color.ParseColor("#E0D8E8"));
            tv.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 };

            itemLayout.AddView(tv);
            itemLayout.Clickable = true;
            itemLayout.Focusable = true;

            var pressedColor = Android.Graphics.Color.ParseColor("#1A9B7ED8").ToArgb();
            var normalColor = Android.Graphics.Color.Transparent.ToArgb();
            var stateList = new Android.Content.Res.ColorStateList(
                new[] { new[] { Android.Resource.Attribute.StatePressed } , new int[] { } },
                new[] { pressedColor, normalColor }
            );

            var rippleDrawable = new Android.Graphics.Drawables.RippleDrawable(stateList,
                null, new Android.Graphics.Drawables.ShapeDrawable(
                    new Android.Graphics.Drawables.Shapes.RoundRectShape(
                        Enumerable.Repeat(12f * density, 8).ToArray(), null, null)));
            itemLayout.Background = rippleDrawable;

            var capturedAction = item.Action;
            itemLayout.Click += (s, e) =>
            {
                capturedAction();
                _contextMenuDialog?.Dismiss();
            };

            cardLayout.AddView(itemLayout);
        }

        var dialog = new Android.App.Dialog(ctx, Android.Resource.Style.ThemeDeviceDefaultLightNoActionBar);
        dialog.RequestWindowFeature((int)Android.Views.WindowFeatures.NoTitle);
        dialog.SetContentView(cardLayout);
        dialog.Window?.SetBackgroundDrawable(new Android.Graphics.Drawables.ColorDrawable(Android.Graphics.Color.Transparent));
        dialog.Window?.SetDimAmount(0.4f);
        dialog.Window?.SetGravity(Android.Views.GravityFlags.Center);
        dialog.Window?.SetLayout(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);

        _contextMenuDialog = dialog;
        dialog.Show();
    }

    private Android.App.Dialog? _contextMenuDialog;
}
