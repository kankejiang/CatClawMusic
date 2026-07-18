using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>专辑列表页面，展示本地音乐库中的所有专辑，支持搜索、筛选、排序和网格/列表视图切换。</summary>
public partial class AlbumsPage : ContentPage
{
    private readonly AlbumsViewModel _viewModel;
    private bool _isFirstAppearing = true;

    /// <summary>初始化 <see cref="AlbumsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">专辑列表页面对应的视图模型。</param>
    public AlbumsPage(AlbumsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>当页面显示在屏幕上时触发，首次出现时加载专辑列表数据。</summary>
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

    /// <summary>搜索按钮点击事件 - 切换搜索框可见性</summary>
    private void OnSearchTapped(object? sender, EventArgs e)
    {
        _viewModel.IsSearchVisible = !_viewModel.IsSearchVisible;
    }

    /// <summary>筛选 chip 点击事件</summary>
    private void OnFilterChipTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is AlbumsViewModel.FilterChip chip)
        {
            _viewModel.SelectFilter(chip.FilterKey);
        }
    }

    /// <summary>排序 chip 点击事件</summary>
    private void OnSortChipTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is AlbumsViewModel.SortOption option)
        {
            _viewModel.SelectSort(option.Key);
        }
    }

    /// <summary>视图切换按钮点击事件</summary>
    private void OnViewToggleTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.Parent is Grid viewToggle)
        {
            // Determine which button was tapped based on column
            var column = Grid.GetColumn(border);
            _viewModel.IsGridView = column == 0;
        }
    }

    /// <summary>年代 rail 点击事件</summary>
    private void OnEraTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is AlbumsViewModel.EraRailItem era)
        {
            _viewModel.SelectEra(era.Key);
        }
    }

    /// <summary>专辑点击事件 - 导航到专辑详情页</summary>
    private async void OnAlbumTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is AlbumWithCount album)
        {
            _viewModel.SelectedAlbum = album;
            await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title ?? string.Empty)}");
        }
    }
}
