using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>歌单列表页面，展示用户创建与系统的歌单集合。</summary>
public partial class PlaylistPage : ContentPage
{
    private readonly PlaylistViewModel _viewModel;
    private bool _isFirstAppearing = true;
    private Entry? _playlistNameEntry;

    /// <summary>初始化 <see cref="PlaylistPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">歌单列表页面对应的视图模型。</param>
    public PlaylistPage(PlaylistViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

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
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnPlaylistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Playlist playlist)
        {
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }
            await Shell.Current.GoToAsync($"playlistdetail?playlistId={playlist.Id}&name={Uri.EscapeDataString(playlist.Name)}");
        }
    }

    /// <summary>点击歌单项的菜单按钮时触发，弹出操作菜单以执行重命名、删除等操作。</summary>
    /// <param name="sender">事件源，通常为携带歌单上下文的图片按钮。</param>
    /// <param name="e">事件参数。</param>
    private async void OnPlaylistMenuClicked(object? sender, EventArgs e)
    {
        if (sender is ImageButton button && button.BindingContext is Playlist playlist)
        {
            if (playlist.IsSystem)
                return;

            var action = await DisplayActionSheet(
                playlist.Name, "取消", null,
                "重命名歌单", "删除歌单");

            if (action == "重命名歌单")
            {
                var newName = await DisplayPromptAsync("重命名歌单", "请输入新的歌单名称", "确定", "取消", initialValue: playlist.Name, maxLength: 30);
                if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == playlist.Name) return;

                await _viewModel.RenamePlaylistAsync(playlist.Id, newName.Trim());
                await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
            }
            else if (action == "删除歌单")
            {
                var confirm = await DisplayAlert("确认删除", $"确定要删除歌单「{playlist.Name}」吗？\n歌曲不会被删除。", "删除", "取消");
                if (confirm)
                {
                    await _viewModel.DeletePlaylistAsync(playlist.Id);
                    await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);
                }
            }
        }
    }

    /// <summary>点击新建歌单按钮时触发，打开自定义输入弹窗以创建新的歌单。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnCreatePlaylistClicked(object? sender, TappedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("[PlaylistPage] OnCreatePlaylistClicked 触发");
        try
        {
            ShowCreatePlaylistPopup();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistPage] OnCreatePlaylistClicked 异常: {ex}");
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
        cancelTap.Tapped += (_, _) => CreatePlaylistPopup.Close();
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

        System.Diagnostics.Debug.WriteLine($"[PlaylistPage] 开始创建歌单: '{name}'");
        CreatePlaylistPopup.Close();

        try
        {
            var newId = await _viewModel.CreatePlaylistAsync(name);
            System.Diagnostics.Debug.WriteLine($"[PlaylistPage] CreatePlaylistAsync 返回 newId={newId}");
            await _viewModel.LoadPlaylistsCommand.ExecuteAsync(null);

            if (newId > 0)
            {
                await Shell.Current.GoToAsync($"playlistdetail?playlistId={newId}&name={Uri.EscapeDataString(name)}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistPage] 创建歌单异常: {ex}");
        }
    }
}
