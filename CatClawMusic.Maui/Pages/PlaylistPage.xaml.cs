using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;
using CatClawMusic.Core.Interfaces;

#if ANDROID
using CatClawMusic.Maui.Platforms.Android;
#endif

namespace CatClawMusic.Maui.Pages;

/// <summary>歌单列表页面，展示用户创建的歌单集合。Android 端长按歌单项可拖动排序或弹出菜单。</summary>
public partial class PlaylistPage : ContentPage
{
    private readonly PlaylistViewModel _viewModel;
    private readonly IServiceProvider _sp;
    private bool _isFirstAppearing = true;
    private Entry? _playlistNameEntry;
    /// <summary>长按/拖拽收尾后短暂忽略列表选中（防止松手误触发导航），TickCount 毫秒阈值。</summary>
    private long _suppressSelectionUntil;

    // ⋮ 菜单弹窗与重命名/删除弹窗（均为 AppPopup，居中卡片，与新建歌单弹窗同款）
    private AppPopup? _menuPopup;
    private AppPopup? _renamePopup;
    private Entry? _renameEntry;
    private Playlist? _renameTarget;

    /// <summary>初始化 <see cref="PlaylistPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">歌单列表页面对应的视图模型。</param>
    /// <param name="sp">服务提供器，用于横屏时解析 Desktop 页面。</param>
    public PlaylistPage(PlaylistViewModel viewModel, IServiceProvider sp)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _sp = sp;
        BindingContext = viewModel;

#if ANDROID
        // Android：长按歌单 → 拖动排序 / 原地松手弹菜单。列表 Handler 就绪后挂接。
        PlaylistsList.HandlerChanged += OnPlaylistsListHandlerChanged;
#endif
    }

#if ANDROID
    /// <summary>歌单列表原生 RecyclerView 就绪后挂长按拖拽助手（幂等）。</summary>
    private void OnPlaylistsListHandlerChanged(object? sender, EventArgs e)
    {
        if (PlaylistsList.Handler?.PlatformView == null) return;
        PlaylistsList.HandlerChanged -= OnPlaylistsListHandlerChanged;
        PlaylistDragDropHelper.Attach(PlaylistsList, _viewModel.Playlists,
            onLongPressArmed: SuppressSelectionAfterLongPress,
            onOrderChanged: CommitPlaylistOrder);
    }
#endif

    /// <summary>拖拽结束：按当前内存顺序提交持久化（集合已被拖拽层就地重排）。</summary>
    private async void CommitPlaylistOrder()
    {
        _suppressSelectionUntil = Environment.TickCount64 + 800;
        try
        {
            var orderedIds = _viewModel.Playlists.Select(p => p.Id).ToList();
            await _viewModel.CommitPlaylistOrderAsync(orderedIds);
        }
        catch (Exception ex)
        {
            Log.Debug("PlaylistPage.xaml", $"[PlaylistPage] 提交歌单顺序异常: {ex}");
        }
    }

    /// <summary>长按确认（进入拖拽）：短暂抑制列表选中，防止拖拽启动/松手误触发导航。</summary>
    private void SuppressSelectionAfterLongPress()
        => _suppressSelectionUntil = Environment.TickCount64 + 800;

    /// <summary>当页面显示在屏幕上时触发，首次出现时加载歌单列表数据。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_isFirstAppearing)
        {
            _isFirstAppearing = false;
            await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
        }
        // 非首次切回：数据为空或 AI Agent/其他模块标记了 dirty 时重新加载
        else if (_viewModel.Playlists.Count == 0 || _viewModel.IsDirty)
        {
            await _viewModel.RefreshIfChangedAsync();
        }
    }

    /// <summary>在歌单列表中选中某个歌单时触发，导航到该歌单的详情页。</summary>
    private async void OnPlaylistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Playlist playlist)
        {
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }
            // 长按/拖拽收尾后的第一次选中是松手误触，忽略
            if (Environment.TickCount64 < _suppressSelectionUntil) return;

            if (!DesktopNavigation.TryGoToShell($"playlistdetail?playlistId={playlist.Id}&name={Uri.EscapeDataString(playlist.Name)}"))
                DesktopNavigation.OpenPlaylistDetail(playlist.Id, playlist.Name);
        }
    }

    /// <summary>点击歌单项的 ⋮ 按钮时触发，弹出操作菜单（app 自绘底部抽屉，替代不可靠的系统 ActionSheet）。
    /// 歌单对象沿视觉树解析（TapGestureRecognizer 的 CommandParameter 在部分版本上不可靠，故不使用）。</summary>
    private void OnPlaylistMoreTapped(object? sender, EventArgs e)
    {
        // 沿视觉树向上找到行根，取其 BindingContext 作为目标歌单
        Playlist? playlist = null;
        for (Element? node = sender as Element; node != null; node = node.Parent)
        {
            if (node.BindingContext is Playlist pl) { playlist = pl; break; }
            if (node is CollectionView) break; // 越过列表根仍未命中则放弃
        }
        if (playlist is not { IsSystem: false }) return;

        ShowPlaylistMenuPopup(playlist);
    }

    /// <summary>构建并弹出 ⋮ 菜单（重命名 / 删除）：AppPopup 居中卡片，与新建歌单弹窗同款。</summary>
    private void ShowPlaylistMenuPopup(Playlist playlist)
    {
        CloseMenuPopup();

        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var inactive = (Color)Application.Current!.Resources["ChipInactiveColor"];
        var error = (Color)Application.Current!.Resources["ErrorColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        _menuPopup = new AppPopup { Title = playlist.Name, CloseOnMaskTapped = true };

        _menuPopup.AddContent(new Label
        {
            Text = "选择要执行的操作",
            FontSize = 13,
            TextColor = textSecondary,
            Margin = new Thickness(0, 0, 0, 14)
        });

        // 重命名歌单
        _menuPopup.AddContent(CreateMenuButton(
            "✏", "重命名歌单",
            async () =>
            {
                var menu = _menuPopup;
                if (menu != null) await menu.CloseAsync();
                ShowRenamePopup(playlist);
            },
            textPrimary, cardBg, inactive));

        // 删除歌单
        _menuPopup.AddContent(CreateMenuButton(
            "🗑", "删除歌单",
            async () =>
            {
                var menu = _menuPopup;
                if (menu != null) await menu.CloseAsync();
                ConfirmDeletePlaylist(playlist);
            },
            error, cardBg, inactive));

        _ = ShowCenteredPopupAsync(_menuPopup);
    }

    /// <summary>构建菜单操作按钮（图标 + 文字，居中卡片风格，44+ 高度）。</summary>
    private static Border CreateMenuButton(string icon, string text, Func<Task> onTap,
        Color textColor, Color bgColor, Color pressedBg)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = 28 },
                new() { Width = GridLength.Star }
            },
            ColumnSpacing = 10
        };
        row.Add(new Label
        {
            Text = icon,
            FontSize = 14,
            TextColor = textColor,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        }, 0);
        row.Add(new Label
        {
            Text = text,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = textColor,
            VerticalTextAlignment = TextAlignment.Center
        }, 1);

        var border = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            StrokeThickness = 0,
            BackgroundColor = bgColor,
            Padding = new Thickness(12, 12),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalOptions = LayoutOptions.Fill,
            Content = row
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await onTap())
        });
        return border;
    }

    private void CloseMenuPopup() => _ = CloseMenuPopupAsync();

    private async Task CloseMenuPopupAsync()
    {
        if (_menuPopup != null)
            await _menuPopup.CloseAsync();
    }

    /// <summary>删除确认（AppPopup 自绘，替代不可靠的系统 DisplayAlert）。</summary>
    private void ConfirmDeletePlaylist(Playlist playlist)
    {
        var popup = new AppPopup { Title = "确认删除", CloseOnMaskTapped = true };
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var inactive = (Color)Application.Current!.Resources["ChipInactiveColor"];
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];
        var error = (Color)Application.Current!.Resources["ErrorColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        popup.AddContent(new Label
        {
            Text = $"确定要删除歌单「{playlist.Name}」吗？\n歌曲不会被删除。",
            FontSize = 14,
            TextColor = textSecondary,
            Margin = new Thickness(0, 0, 0, 16)
        });

        var btnRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 12
        };

        var cancelBtn = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = inactive,
            StrokeThickness = 0,
            HeightRequest = 44,
            Content = new Label
            {
                Text = "取消", FontSize = 15, FontAttributes = FontAttributes.Bold,
                TextColor = textSecondary,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
            }
        };
        cancelBtn.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => _ = popup.CloseAsync()) });
        btnRow.Add(cancelBtn, 0);

        var deleteBtn = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = error,
            StrokeThickness = 0,
            HeightRequest = 44,
            Content = new Label
            {
                Text = "删除", FontSize = 15, FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
            }
        };
        deleteBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                await popup.CloseAsync();
                await _viewModel.DeletePlaylistAsync(playlist.Id);
                await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
            })
        });
        btnRow.Add(deleteBtn, 1);

        popup.AddContent(btnRow);

        // 挂到页面根 Grid 显示（关闭时经 Closed 事件自动移除）
        _ = ShowCenteredPopupAsync(popup);
    }

    /// <summary>重命名弹窗（AppPopup + 输入框，替代不可靠的系统 DisplayPromptAsync）。</summary>
    private void ShowRenamePopup(Playlist playlist)
    {
        CloseRenamePopup();

        var primary = (Color)Application.Current!.Resources["PrimaryColor"];
        var inactive = (Color)Application.Current!.Resources["ChipInactiveColor"];
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        _renamePopup = new AppPopup { Title = "重命名歌单", CloseOnMaskTapped = true };
        _renameTarget = playlist;

        _renamePopup.AddContent(new Label
        {
            Text = "请输入新的歌单名称",
            FontSize = 13,
            TextColor = textHint,
            Margin = new Thickness(0, 0, 0, 10)
        });

        _renameEntry = new Entry
        {
            Text = playlist.Name,
            MaxLength = 30,
            FontSize = 15,
            TextColor = textPrimary,
            PlaceholderColor = textHint,
            BackgroundColor = cardBg,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            HorizontalOptions = LayoutOptions.Fill,
            HeightRequest = 44
        };
        var entryBorder = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            Stroke = inactive,
            StrokeThickness = 1,
            BackgroundColor = cardBg,
            Padding = new Thickness(12, 0),
            HorizontalOptions = LayoutOptions.Fill,
            Content = _renameEntry
        };
        _renamePopup.AddContent(entryBorder);

        var btnRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 12,
            Margin = new Thickness(0, 18, 0, 0)
        };

        var cancelBtn = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = inactive,
            StrokeThickness = 0,
            HeightRequest = 44,
            Content = new Label
            {
                Text = "取消", FontSize = 15, FontAttributes = FontAttributes.Bold,
                TextColor = textSecondary,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
            }
        };
        cancelBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => _ = _renamePopup!.CloseAsync())
        });
        btnRow.Add(cancelBtn, 0);

        var confirmBtn = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = primary,
            StrokeThickness = 0,
            HeightRequest = 44,
            Content = new Label
            {
                Text = "确定", FontSize = 15, FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
            }
        };
        confirmBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await OnRenameConfirmedAsync())
        });
        btnRow.Add(confirmBtn, 1);

        _renamePopup.AddContent(btnRow);

        _renameEntry.Completed += async (_, _) => await OnRenameConfirmedAsync();

        _ = ShowCenteredPopupAsync(_renamePopup);

        // 延迟聚焦输入框，等弹窗动画完成
        _ = Task.Delay(300).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { _renameEntry?.Focus(); } catch { }
            }));
    }

    private async Task OnRenameConfirmedAsync()
    {
        var popup = _renamePopup;
        var target = _renameTarget;
        var name = _renameEntry?.Text?.Trim();
        if (popup == null || target == null || string.IsNullOrWhiteSpace(name) || name == target.Name)
        {
            if (popup != null) await popup.CloseAsync();
            return;
        }

        await popup.CloseAsync();
        await _viewModel.RenamePlaylistAsync(target.Id, name!);
        await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
    }

    private void CloseRenamePopup()
    {
        if (_renamePopup != null)
            _ = _renamePopup.CloseAsync();
    }

    /// <summary>把居中弹窗挂到页面根 Grid（全窗覆盖），关闭后自动移除。</summary>
    private async Task ShowCenteredPopupAsync(AppPopup popup)
    {
        if (this.Content is not Grid root) return;

        Grid.SetRow(popup, 0);
        Grid.SetRowSpan(popup, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumn(popup, 0);
        Grid.SetColumnSpan(popup, Math.Max(1, root.ColumnDefinitions.Count));
        root.Children.Add(popup);

        EventHandler? closed = null;
        closed = (_, _) =>
        {
            popup.Closed -= closed;
            try { if (popup.Parent is Layout parent) parent.Children.Remove(popup); } catch { }
        };
        popup.Closed += closed;

        // AppPopup.Open 内部含动画与 PinToScreenHeight，保持与新建歌单弹窗一致
        await MainThread.InvokeOnMainThreadAsync(popup.Open);
    }

    /// <summary>点击"我喜欢的"卡片，导航到全部歌曲（收藏筛选）。</summary>
    private void OnFavoriteCardTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=favorites", source: "favorites");

    /// <summary>点击"最近播放"卡片，导航到全部歌曲（最近播放筛选）。</summary>
    private void OnRecentCardTapped(object? sender, EventArgs e)
        => OpenLibrarySubPage(typeof(AllSongsPage), "library/allsongs?source=recent", source: "recent");

    /// <summary>点击新建歌单按钮时触发，打开自定义输入弹窗以创建新的歌单。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnCreatePlaylistClicked(object? sender, TappedEventArgs e)
    {
        Log.Debug("PlaylistPage.xaml", "[PlaylistPage] OnCreatePlaylistClicked 触发");
        try
        {
            ShowCreatePlaylistPopup();
        }
        catch (Exception ex)
        {
            Log.Debug("PlaylistPage.xaml", $"[PlaylistPage] OnCreatePlaylistClicked 异常: {ex}");
        }
    }

    /// <summary>构建并显示新建歌单输入弹窗（替代 DisplayPromptAsync，避免 MAUI 11 Android 兼容性问题）</summary>
    private void ShowCreatePlaylistPopup()
    {
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var inactiveColor = (Color)Application.Current!.Resources["ChipInactiveColor"];
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        // 清空旧内容（保留标题栏）
        CreatePlaylistPopup.ClearContent();

        // 提示文字
        var hintLabel = new Label
        {
            Text = "请输入歌单名称",
            FontSize = 13,
            TextColor = textHint,
            Margin = new Thickness(0, 0, 0, 10)
        };
        CreatePlaylistPopup.AddContent(hintLabel);

        // 输入框
        _playlistNameEntry = new Entry
        {
            Placeholder = "歌单名称",
            FontSize = 15,
            MaxLength = 30,
            TextColor = textPrimary,
            PlaceholderColor = textHint,
            BackgroundColor = cardBg,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
            HorizontalOptions = LayoutOptions.Fill,
            HeightRequest = 44
        };
        var entryBorder = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            Stroke = inactiveColor,
            StrokeThickness = 1,
            BackgroundColor = cardBg,
            Padding = new Thickness(12, 0),
            HorizontalOptions = LayoutOptions.Fill,
            Content = _playlistNameEntry
        };
        CreatePlaylistPopup.AddContent(entryBorder);

        // 按钮行
        var btnRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = new GridLength(1, GridUnitType.Star) }
            },
            ColumnSpacing = 12,
            Margin = new Thickness(0, 18, 0, 0)
        };

        // 取消按钮
        var cancelBtn = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = inactiveColor,
            StrokeThickness = 0,
            HeightRequest = 44,
            HorizontalOptions = LayoutOptions.Fill,
            Content = new Label
            {
                Text = "取消",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = textSecondary,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        var cancelTap = new TapGestureRecognizer();
        cancelTap.Tapped += (_, _) => { _ = CreatePlaylistPopup.CloseAsync(); };
        cancelBtn.GestureRecognizers.Add(cancelTap);
        btnRow.Add(cancelBtn, 0);

        // 创建按钮
        var confirmBtn = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            BackgroundColor = primaryColor,
            StrokeThickness = 0,
            HeightRequest = 44,
            HorizontalOptions = LayoutOptions.Fill,
            Content = new Label
            {
                Text = "创建",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        var confirmTap = new TapGestureRecognizer();
        confirmTap.Tapped += async (_, _) => await OnCreatePlaylistConfirmedAsync();
        confirmBtn.GestureRecognizers.Add(confirmTap);
        btnRow.Add(confirmBtn, 1);

        CreatePlaylistPopup.AddContent(btnRow);

        // 处理 Entry 的回车键
        _playlistNameEntry.Completed += async (_, _) => await OnCreatePlaylistConfirmedAsync();

        CreatePlaylistPopup.Open();

        // 延迟聚焦输入框，等弹窗动画完成
        _ = Task.Delay(300).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try { _playlistNameEntry?.Focus(); } catch { }
            }));
    }

    /// <summary>点击"创建"按钮或回车时触发，执行歌单创建逻辑</summary>
    private async Task OnCreatePlaylistConfirmedAsync()
    {
        var name = _playlistNameEntry?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            // 输入为空时震动提示（若支持）
            try { _playlistNameEntry?.Focus(); } catch { }
            return;
        }

        Log.Debug("PlaylistPage.xaml", $"[PlaylistPage] 开始创建歌单: '{name}'");
        await CreatePlaylistPopup.CloseAsync();

        try
        {
            var newId = await _viewModel.CreatePlaylistAsync(name);
            Log.Debug("PlaylistPage.xaml", $"[PlaylistPage] CreatePlaylistAsync 返回 newId={newId}");
            await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);

            if (newId > 0)
            {
                if (!DesktopNavigation.TryGoToShell($"playlistdetail?playlistId={newId}&name={Uri.EscapeDataString(name)}"))
                    DesktopNavigation.OpenPlaylistDetail(newId, name);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("PlaylistPage.xaml", $"[PlaylistPage] 创建歌单异常: {ex}");
        }
    }

    /// <summary>
    /// 打开音乐库二级页：竖屏走 Shell 导航；Windows 桌面复用桌面布局并嵌入 ContentArea。
    /// Android 原生旋转方案下不再有横屏 DesktopMainPage，一律走 Shell 导航。
    /// </summary>
    private void OpenLibrarySubPage(Type pageType, string fallbackRoute, string? source = null)
    {
#if WINDOWS
        if (App.IsLandscapeMode())
        {
            Type desktopType = pageType.Name switch
            {
                nameof(AllSongsPage) => typeof(DesktopAllSongsPage),
                _ => pageType
            };

            var page = (ContentPage)_sp.GetRequiredService(desktopType);
            if (!string.IsNullOrEmpty(source) && page is DesktopAllSongsPage desktopAllSongs)
                desktopAllSongs.Source = source;

            DesktopMainPage.Instance?.OpenSubPageEmbedded(page);
            return;
        }
#endif

        DesktopNavigation.TryGoToShell(fallbackRoute);
    }
}
