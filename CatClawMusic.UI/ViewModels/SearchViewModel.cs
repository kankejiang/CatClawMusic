using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.UI.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly IMusicLibraryService _musicLibrary;

    public ObservableCollection<Song> SearchResults { get; } = new();

    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _resultCount = "";

    public SearchViewModel(IMusicLibraryService musicLibrary)
    {
        _musicLibrary = musicLibrary;
    }

    public async Task SearchAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) { SearchResults.Clear(); ResultCount = ""; return; }
        IsSearching = true;
        try
        {
            foreach (var song in await _musicLibrary.SearchAsync(keyword))
                SearchResults.Add(song);
            ResultCount = SearchResults.Count > 0 ? $"找到 {SearchResults.Count} 首歌曲" : "未找到结果";
        }
        catch { ResultCount = "搜索失败"; }
        finally { IsSearching = false; }
    }
}
