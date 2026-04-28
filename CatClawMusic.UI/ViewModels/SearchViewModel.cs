using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

public class SearchViewModel : BindableObject
{
    private readonly IMusicLibraryService _musicLibrary;

    public ObservableCollection<Song> SearchResults { get; } = new();

    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            _isSearching = value;
            OnPropertyChanged();
        }
    }

    private string _resultCount = "";
    public string ResultCount
    {
        get => _resultCount;
        set
        {
            _resultCount = value;
            OnPropertyChanged();
        }
    }

    public SearchViewModel(IMusicLibraryService musicLibrary)
    {
        _musicLibrary = musicLibrary;
    }

    public async Task SearchAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            SearchResults.Clear();
            ResultCount = "";
            return;
        }

        IsSearching = true;
        try
        {
            var results = await _musicLibrary.SearchAsync(keyword);
            SearchResults.Clear();
            foreach (var song in results)
            {
                SearchResults.Add(song);
            }
            ResultCount = SearchResults.Count > 0 ? $"找到 {SearchResults.Count} 首歌曲" : "未找到结果";
        }
        catch
        {
            ResultCount = "搜索失败";
        }
        finally
        {
            IsSearching = false;
        }
    }
}
