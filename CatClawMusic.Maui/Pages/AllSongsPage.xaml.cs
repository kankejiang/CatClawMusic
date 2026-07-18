using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// "全部歌曲"二级页面：按 HTML 原型实现，支持搜索、多维度排序、A-Z 索引、播放/随机。
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

    // === 返回 ===

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay())
            return;
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else
            await Shell.Current.GoToAsync("..");
    }

    // === 排序芯片点击 ===

    private void OnSortChipTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string key)
        {
            _vm.ToggleSort(key);
        }
    }

    // === 歌曲选择 ===

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            ((CollectionView)sender!).SelectedItem = null;
            await _vm.PlaySongCommand.ExecuteAsync(song);
        }
    }

    // === 更多按钮 ===

    private async void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Song song) return;

        var action = await DisplayActionSheet(song.Title, "取消", null,
            "添加到播放队列", "添加到歌单", "查看歌曲详情", "分享");

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

    // === A-Z 索引点击 ===

    private void OnIndexTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is int index && index >= 0 && index < _vm.Songs.Count)
        {
            var targetSong = _vm.Songs[index];
            _songList?.ScrollTo(targetSong, position: ScrollToPosition.MakeVisible);
        }
    }

    private CollectionView? _songList;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        // 找到歌曲列表 CollectionView
        _songList = FindSongList(this);
    }

    private CollectionView? FindSongList(Element? element)
    {
        if (element == null) return null;
        if (element is CollectionView cv
            && cv.ItemsSource == (BindingContext as AllSongsViewModel)?.Songs)
            return cv;

        if (element is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child is Element childElement)
                {
                    var found = FindSongList(childElement);
                    if (found != null) return found;
                }
            }
        }
        else if (element is IContentView { Content: Element content })
        {
            return FindSongList(content);
        }
        return null;
    }
}
