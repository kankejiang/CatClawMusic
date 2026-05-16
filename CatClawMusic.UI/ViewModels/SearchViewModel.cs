using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.UI.ViewModels;

/// <summary>
/// 搜索ViewModel，使用防抖机制处理用户输入并异步搜索歌曲
/// </summary>
public partial class SearchViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;

    public ObservableCollection<Song> SearchResults { get; } = new();

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _resultCount = "";

    // 防抖机制
    private CancellationTokenSource? _searchCts;
    private const int DebounceDelayMs = 300;

    /// <summary>
    /// 搜索文本变化时触发，使用防抖机制调用SearchAsync
    /// </summary>
    partial void OnSearchTextChanged(string value) => _ = SearchAsync(value);

    /// <summary>
    /// 初始化搜索ViewModel
    /// </summary>
    public SearchViewModel(IMusicLibraryService musicLibrary)
    {
        _musicLibrary = musicLibrary;
    }

    /// <summary>
    /// 执行搜索，使用防抖机制避免频繁查询
    /// </summary>
    public async Task SearchAsync(string keyword)
    {
        // 取消之前的搜索任务
        _searchCts?.Cancel();
        _searchCts?.Dispose();

        if (string.IsNullOrWhiteSpace(keyword))
        {
            SearchResults.Clear();
            ResultCount = "";
            return;
        }

        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        // 防抖延迟
        try
        {
            await Task.Delay(DebounceDelayMs, ct);
        }
        catch (TaskCanceledException)
        {
            return; // 新的搜索已启动，放弃本次
        }

        if (ct.IsCancellationRequested) return;

        IsSearching = true;
        SearchResults.Clear();
        try
        {
            var results = await _musicLibrary.SearchAsync(keyword);
            if (ct.IsCancellationRequested) return;

            foreach (var song in results)
                SearchResults.Add(song);
            ResultCount = SearchResults.Count > 0 ? $"找到 {SearchResults.Count} 首歌曲" : "未找到结果";
        }
        catch { ResultCount = "搜索失败"; }
        finally { IsSearching = false; }
    }
}
