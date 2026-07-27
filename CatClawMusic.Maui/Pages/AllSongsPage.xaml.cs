using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// "全部歌曲"二级页面（竖屏）：支持搜索、多维度排序、A-Z 索引、播放/随机。
/// 横屏下使用 <see cref="DesktopAllSongsPage"/>。公共 UI 组件见 <see cref="Controls"/> 命名空间。
/// </summary>
[QueryProperty(nameof(Source), "source")]
public partial class AllSongsPage : ContentPage
{
    private readonly AllSongsViewModel _vm;

    public string Source { get; set; } = "local";

    public AllSongsPage(AllSongsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 立即开始加载（DB 查询在后台线程，页面外壳先渲染；命中缓存则首屏秒出，
        // 封面由 BatchResolveCoversAsync 后台分块解析、经 INPC 自动刷新可见 cell）。
        _ = _vm.LoadAsync(Source);
    }

    // === 事件处理 ===

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay()) return;

        // 横屏嵌入模式：返回到音乐库 tab，保持侧边栏可见
        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("library");
            return;
        }

        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else
            await Shell.Current.GoToAsync("..");
    }

    // ChipList.ChipTapped 事件签名：EventHandler<object?>
    private void OnSortChipTapped(object? sender, object? item)
    {
        if (item is SortOption option)
            _vm.ToggleSort(option.Key);
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            ((CollectionView)sender!).SelectedItem = null;
            await _vm.PlaySongCommand.ExecuteAsync(song);
        }
    }

    private async void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Song song) return;
        var action = await DisplayActionSheet(song.Title, "取消", null,
            "添加到播放队列", "添加到歌单", "查看歌曲详情", "分享");
        await HandleSongAction(action, song);
    }

    private void OnIndexTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is int index && index >= 0 && index < _vm.Songs.Count)
        {
            var targetSong = _vm.Songs[index];
            SongListView?.ScrollTo(targetSong, position: ScrollToPosition.MakeVisible);
        }
    }

    private async Task HandleSongAction(string? action, Song song)
    {
        switch (action)
        {
            case "查看歌曲详情":
                await Shell.Current.GoToAsync($"songdetail?songId={song.Id}");
                break;
            case "添加到播放队列":
                // TODO: 添加到播放队列
                break;
            case "添加到歌单":
                // TODO: 添加到歌单
                break;
        }
    }
}
