using CatClawMusic.WinUI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace CatClawMusic.WinUI.Views;

/// <summary>
/// 歌单页：左列歌单、右列歌曲。增删改走 <see cref="PlaylistsViewModel"/>，
/// 对话框（新建 / 重命名 / 删除确认）留在视图层。
/// </summary>
public sealed partial class PlaylistsView : UserControl
{
    public PlaylistsView(PlaylistsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Loaded += OnLoaded;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(PlaylistsViewModel.HasSongs)
                or nameof(PlaylistsViewModel.HasSelection)
                or nameof(PlaylistsViewModel.CanEditSelection))
            {
                UpdateState();
            }
        };
    }

    public PlaylistsViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.LoadAsync();
        UpdateState();
    }

    private void UpdateState()
    {
        var hasSelection = ViewModel.HasSelection;
        var hasSongs = ViewModel.HasSongs;

        SongsList.Visibility = hasSongs ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasSongs ? Visibility.Collapsed : Visibility.Visible;

        if (!hasSelection)
        {
            EmptyTitle.Text = "还没有歌单";
            EmptyHint.Text = "点左上角「＋ 新建」创建一个歌单，再从音乐库里把歌加进来。";
        }
        else
        {
            EmptyTitle.Text = "这个歌单还没有歌";
            EmptyHint.Text = "到音乐库里右键一首歌 →「添加到歌单」，或者先新建一个歌单。";
        }

        RenameButton.IsEnabled = ViewModel.CanEditSelection;
        DeleteButton.IsEnabled = ViewModel.CanEditSelection;
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e)
    {
        var name = await PromptAsync("新建歌单", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
            return;

        await ViewModel.CreateAsync(name);
        UpdateState();
    }

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        var name = await PromptAsync("重命名歌单", ViewModel.SelectedName);
        if (string.IsNullOrWhiteSpace(name))
            return;

        await ViewModel.RenameAsync(name);
        UpdateState();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanEditSelection)
            return;

        var dialog = new ContentDialog
        {
            Title = "删除歌单",
            Content = $"确定删除「{ViewModel.SelectedName}」吗？歌单里的歌曲不会被删除。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        await ViewModel.DeleteAsync();
        UpdateState();
    }

    private void OnPlayAllClick(object sender, RoutedEventArgs e)
    {
        ViewModel.PlayAllCommand.Execute(null);
    }

    private void OnSongClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SongRow row)
            ViewModel.PlaySongCommand.Execute(row);
    }

    private async void OnSongRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement source || source.DataContext is not SongRow row)
            return;

        var flyout = new MenuFlyout();

        var play = new MenuFlyoutItem { Text = "播放" };
        play.Click += (_, _) => ViewModel.PlaySongCommand.Execute(row);
        flyout.Items.Add(play);

        // 系统歌单（收藏 / 最近播放）不允许移除，收藏状态在音乐库右键里切换。
        if (ViewModel.CanEditSelection)
        {
            var remove = new MenuFlyoutItem { Text = "从歌单移除" };
            remove.Click += async (_, _) => await ViewModel.RemoveSongAsync(row);
            flyout.Items.Add(remove);
        }

        flyout.ShowAt(source, e.GetPosition(source));
    }

    private async Task<string?> PromptAsync(string title, string initial)
    {
        var input = new TextBox
        {
            Text = initial,
            PlaceholderText = "歌单名称",
        };
        input.SelectAll();

        var dialog = new ContentDialog
        {
            Title = title,
            Content = input,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? input.Text?.Trim() : null;
    }
}
