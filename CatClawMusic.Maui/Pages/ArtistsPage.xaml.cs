using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>艺术家列表页面，展示本地音乐库中的所有艺术家，支持搜索、筛选、排序和网格/列表视图切换。</summary>
public partial class ArtistsPage : ContentPage
{
    private readonly ArtistsViewModel _viewModel;
    private bool _isFirstAppearing = true;

    /// <summary>初始化 <see cref="ArtistsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">艺术家列表页面对应的视图模型。</param>
    public ArtistsPage(ArtistsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>当页面显示在屏幕上时触发，首次出现时加载艺术家列表数据。</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_isFirstAppearing)
        {
            _isFirstAppearing = false;
            // 立即加载，页面先以占位图显示，封面后台补足（与 AllSongsPage 行为一致）
            _ = _viewModel.LoadAsync();
        }
    }

    /// <summary>返回按钮点击事件</summary>
    private async void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay())
            return;
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>搜索按钮点击事件</summary>
    private void OnSearchTapped(object? sender, EventArgs e)
    {
        _viewModel.IsSearchVisible = !_viewModel.IsSearchVisible;
        if (_viewModel.IsSearchVisible)
        {
            // Focus the search entry
        }
    }

    /// <summary>筛选 chip 点击事件</summary>
    private void OnFilterChipTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ArtistsViewModel.FilterChip chip)
        {
            _viewModel.SelectFilter(chip.FilterKey);
        }
    }

    /// <summary>排序 chip 点击事件</summary>
    private void OnSortChipTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ArtistsViewModel.SortOption option)
        {
            _viewModel.SelectSort(option.Key);
        }
    }

    /// <summary>视图切换按钮点击事件</summary>
    private void OnViewToggleTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.Parent is Grid viewToggle)
        {
            var column = Grid.GetColumn(border);
            _viewModel.IsGridView = column == 0;
        }
    }

    /// <summary>字母 rail 点击事件</summary>
    private void OnLetterTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ArtistsViewModel.LetterRailItem letter)
        {
            _viewModel.SelectLetter(letter.Key);
        }
    }

    /// <summary>艺术家点击事件 - 导航到艺术家详情页</summary>
    private async void OnArtistTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ArtistWithCount artist)
        {
            _viewModel.SelectedArtist = artist;
            await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name ?? string.Empty)}");
        }
    }

    /// <summary>最常聆听卡片点击事件</summary>
    private async void OnMostPlayedTapped(object? sender, EventArgs e)
    {
        if (_viewModel.MostPlayedArtist != null)
        {
            await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(_viewModel.MostPlayedArtist.Name ?? string.Empty)}");
        }
    }
}
