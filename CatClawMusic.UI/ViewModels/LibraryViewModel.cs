using System.Collections.ObjectModel;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

public class LibraryViewModel : BindableObject
{
    private string _currentTab = "Local";
    
    public ObservableCollection<Song> Songs { get; set; } = new();
    
    public string LocalTabColor => _currentTab == "Local" ? "#FF6B9D" : "#CCCCCC";
    public string NetworkTabColor => _currentTab == "Network" ? "#FF6B9D" : "#CCCCCC";
    
    public Command<string> SwitchTabCommand { get; }
    
    public LibraryViewModel()
    {
        SwitchTabCommand = new Command<string>(SwitchTab);
        LoadLocalSongs();
    }
    
    private void SwitchTab(string tab)
    {
        _currentTab = tab;
        OnPropertyChanged(nameof(LocalTabColor));
        OnPropertyChanged(nameof(NetworkTabColor));
        
        if (tab == "Local")
        {
            LoadLocalSongs();
        }
        else
        {
            LoadNetworkSongs();
        }
    }
    
    private void LoadLocalSongs()
    {
        // TODO: 从数据库加载本地歌曲
        Songs.Clear();
        // 示例数据
        Songs.Add(new Song { Title = "示例歌曲 1", Artist = "艺术家 1", Album = "专辑 1" });
        Songs.Add(new Song { Title = "示例歌曲 2", Artist = "艺术家 2", Album = "专辑 2" });
    }
    
    private void LoadNetworkSongs()
    {
        // TODO: 从 WebDAV 加载网络歌曲
        Songs.Clear();
        Songs.Add(new Song { Title = "[网络] 歌曲 1", Artist = "艺术家 1", Album = "专辑 1", Source = SongSource.WebDAV });
    }
}
