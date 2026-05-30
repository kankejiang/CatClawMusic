using System.Collections.Specialized;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using System.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.Helpers;
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
    private ImageButton _btnSort = null!;
    private ImageButton _btnClear = null!;
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
        _songList.SetItemViewCacheSize(20);
        _songList.GetRecycledViewPool().SetMaxRecycledViews(0, 30);
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;
        _btnLocal = view.FindViewById<Button>(Resource.Id.btn_local)!;
        _btnNetwork = view.FindViewById<Button>(Resource.Id.btn_network)!;
        _btnRefresh = view.FindViewById<ImageButton>(Resource.Id.btn_refresh)!;
        _btnSort = view.FindViewById<ImageButton>(Resource.Id.btn_sort)!;
        _btnClear = view.FindViewById<ImageButton>(Resource.Id.btn_clear)!;
        _searchBox = view.FindViewById<EditText>(Resource.Id.search_box)!;
        _networkProtocolRow = view.FindViewById<LinearLayout>(Resource.Id.network_protocol_row)!;
        _protocolSpinner = view.FindViewById<Spinner>(Resource.Id.spinner_protocol)!;

        _adapter = sp.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += OnSongClicked;
        _adapter.SongLongClicked += OnSongLongClicked;
        _songList.SetAdapter(_adapter);
        _songList.AddOnScrollListener(new SongAdapter.ScrollListener(_adapter));
        (_songList.GetItemAnimator() as DefaultItemAnimator)!.SupportsChangeAnimations = false;
        _songList.SetItemViewCacheSize(20);

        // 初始化 Spinner，先同步 ViewModel 已恢复的协议选择再绑定事件
        _ = RefreshProtocolSpinnerAsync();

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
        _btnSort.Click += OnSortClicked;
        _btnClear.Click += OnClearClicked;

        BindViews();
        if (_viewModel.CurrentTab == "Network")
        {
            _btnLocal.SetTextColor(Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#4A0072")));
            _btnNetwork.SetTextColor(Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.White));
            _btnLocal.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#C0B8CA"));
            _btnNetwork.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.ParseColor("#9B7ED8"));
            _networkProtocolRow.Visibility = ViewStates.Visible;
        }
        else
        {
            _networkProtocolRow.Visibility = ViewStates.Gone;
        }
        
        // 如果已有歌曲，更新适配器
        if (_viewModel.Songs.Count > 0)
            _adapter.UpdateSongs(_viewModel.Songs);
        // 否则，根据当前标签自动加载音乐
        else
        {
            if (_viewModel.CurrentTab == "Local")
                _ = _viewModel.LoadLocalAsync();
            else
                _ = _viewModel.LoadNetworkAsync();
        }
    }

    /// <summary>
    /// 绑定ViewModel属性变化事件和集合变化事件
    /// </summary>
    private void BindViews()
    {
        BindingHelper.BindText(_statusText, _viewModel, nameof(_viewModel.StatusText), _ => _viewModel.StatusText);

        _viewModel.Songs.CollectionChanged += OnSongsCollectionChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.FilteredSongs))
            {
                Activity?.RunOnUiThread(() => _adapter.UpdateSongs(_viewModel.FilteredSongs));
            }
        };

        LibraryViewModel.ProtocolChanged += OnProtocolChanged;

        UpdateTabButtonColor(_btnLocal, _viewModel.LocalTabColor, _viewModel.CurrentTab == "Local");
        UpdateTabButtonColor(_btnNetwork, _viewModel.NetworkTabColor, _viewModel.CurrentTab == "Network");
    }

    private void UnbindViews()
    {
        _viewModel.Songs.CollectionChanged -= OnSongsCollectionChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _protocolSpinner.ItemSelected -= OnProtocolSelected;
        LibraryViewModel.ProtocolChanged -= OnProtocolChanged;
    }

    private void OnProtocolChanged(object? sender, EventArgs e)
    {
        _ = RefreshProtocolSpinnerAsync();
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
    private async Task RefreshProtocolSpinnerAsync()
    {
        await _viewModel.RefreshProtocolOptionsAsync();
        Activity?.RunOnUiThread(() =>
        {
            _protocolAdapter = new ArrayAdapter<string>(Context!,
                Android.Resource.Layout.SimpleSpinnerItem, _viewModel.ProtocolOptions);
            _protocolAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _protocolSpinner.Adapter = _protocolAdapter;
            _protocolSpinner.SetSelection(_viewModel.SelectedProtocolIndex);
            _protocolSpinner.ItemSelected -= OnProtocolSelected;
            _protocolSpinner.ItemSelected += OnProtocolSelected;
        });
    }

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
    /// Fragment恢复可见时刷新网络协议下拉列表
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        if (_viewModel.CurrentTab == "Network")
            _ = RefreshProtocolSpinnerAsync();
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
                a.RunOnUiThread(() =>
                {
                    _adapter.Clear();
                    _adapter.AddRange(_viewModel.Songs);
                });
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

        var dialog = new GlassDialog(ctx)
            .SetTitle(song.Title ?? "未知歌曲", song.Artist ?? "未知艺术家");

        dialog.AddItem("▶  播放", () => OnSongClicked(this, song));
        dialog.AddItem("⏭  下一首播放", () =>
        {
            var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
            queue.AddNext(song);
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(ctx, $"已添加: {song.Title}", ToastLength.Short)?.Show());
        });
        dialog.AddItem("📋  添加到歌单", () => ShowAddToPlaylistDialog(song));
        dialog.AddItem("❤  收藏", async () =>
        {
            var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
            bool isFav = await db.IsFavoriteAsync(song.Id);
            await db.SetFavoriteAsync(song.Id, !isFav);
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(ctx, isFav ? "已取消收藏" : "已收藏", ToastLength.Short)?.Show());
        });
        dialog.AddItem("ℹ  歌曲详情", () => ShowSongInfoDialog(song));

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
                        dialog.AddItem("🏷  " + entry.Title, async () =>
                        {
                            await plugin.OnMenuItemClicked(entryId, song, this);
                        });
                    }
                }
                catch { }
            }
        }

        dialog.Show();
    }

    private async void ShowAddToPlaylistDialog(CoreModels.Song song)
    {
        var ctx = Context;
        if (ctx == null) return;

        var musicLibrary = MainApplication.Services.GetRequiredService<IMusicLibraryService>();
        var playlists = await musicLibrary.GetAllPlaylistsAsync();
        if (playlists.Count == 0)
        {
            Toast.MakeText(ctx, "暂无歌单，请先创建歌单", ToastLength.Short)!.Show();
            return;
        }

        var dialog = new GlassDialog(ctx).SetTitle("添加到歌单");
        foreach (var p in playlists)
        {
            var playlistId = p.Id;
            dialog.AddItem(p.Name, async () =>
            {
                await musicLibrary.AddSongToPlaylistAsync(playlistId, song.Id);
                Activity?.RunOnUiThread(() =>
                    Toast.MakeText(ctx, $"已添加到「{p.Name}」", ToastLength.Short)?.Show());
            });
        }
        dialog.AddNegativeButton("取消");
        dialog.Show();
    }

    private void ShowSongInfoDialog(CoreModels.Song song)
    {
        var ctx = Context;
        if (ctx == null) return;

        var durationStr = TimeSpan.FromMilliseconds(song.Duration).ToString(@"mm\:ss");
        var sourceStr = song.Source switch
        {
            CoreModels.SongSource.Local => "本地",
            CoreModels.SongSource.WebDAV => "网络",
            CoreModels.SongSource.SMB => "SMB",
            CoreModels.SongSource.Cache => "缓存",
            _ => "未知"
        };
        var info = $"艺术家：{song.Artist}\n"
            + $"专辑：{song.Album}\n"
            + $"时长：{durationStr}\n"
            + $"来源：{sourceStr}\n"
            + $"比特率：{(song.Bitrate > 0 ? $"{song.Bitrate / 1000}kbps" : "未知")}\n"
            + $"文件大小：{FormatFileSize(song.FileSize)}\n"
            + $"路径：{song.FilePath}";

        new GlassDialog(ctx)
            .SetTitle(song.Title ?? "未知歌曲")
            .AddMessage(info)
            .AddNegativeButton("确定")
            .Show();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private void OnSortClicked(object? sender, EventArgs e)
    {
        var ctx = Context;
        if (ctx == null) return;

        var dialog = new GlassDialog(ctx).SetTitle("排序");
        dialog.AddItem("文件名", () => ApplyLibrarySort(s => System.IO.Path.GetFileNameWithoutExtension(s.FilePath ?? ""), false));
        dialog.AddItem("入库时间", () => ApplyLibrarySort(s => s.DateAdded.ToString(), false));
        dialog.AddItem("文件大小", () => ApplyLibrarySort(s => s.FileSize.ToString(), true));
        dialog.AddItem("文件夹", () => ApplyLibrarySort(s => System.IO.Path.GetDirectoryName(s.FilePath ?? "") ?? "", false));
        dialog.AddItem("艺术家", () => ApplyLibrarySort(s => s.Artist ?? "", false));
        dialog.AddItem("标题", () => ApplyLibrarySort(s => s.Title ?? "", false));
        dialog.Show();
    }

    private void ApplyLibrarySort(Func<CoreModels.Song, string> keySelector, bool descending)
    {
        var songs = _viewModel.FilteredSongs;
        var sorted = descending
            ? songs.OrderByDescending(keySelector).ToList()
            : songs.OrderBy(keySelector).ToList();
        _adapter.UpdateSongs(sorted);
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        var ctx = Context;
        if (ctx == null) return;

        var type = _viewModel.CurrentTab == "Local" ? "本地音乐库" : "网络音乐库";

        new GlassDialog(ctx)
            .SetTitle("确认清除")
            .AddMessage($"确定要清除{type}中的所有歌曲吗？\n\n此操作不可撤销。")
            .AddNegativeButton("取消")
            .AddPositiveButton("确认清除", async (s) =>
            {
                try
                {
                    var db = MainApplication.Services.GetRequiredService<MusicDatabase>();
                    if (_viewModel.CurrentTab == "Local")
                    {
                        await db.ClearLocalSongsAsync();
                    }
                    else
                    {
                        await db.ClearCachedNetworkSongsAsync();
                    }
                    
                    _viewModel.Songs.Clear();
                    _adapter.Clear();
                    
                    if (_viewModel.CurrentTab == "Local")
                    {
                        _viewModel.StatusText = "本地音乐库已清空";
                    }
                    else
                    {
                        _viewModel.StatusText = "网络音乐库已清空";
                    }

                    Toast.MakeText(ctx, $"{type}已清空", ToastLength.Short)?.Show();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Library] 清除失败: {ex.Message}");
                    Toast.MakeText(ctx, "清除失败", ToastLength.Short)?.Show();
                }
            })
            .Show();
    }

    private Android.App.Dialog? _contextMenuDialog;
}
