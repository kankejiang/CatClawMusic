using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CoreModels = CatClawMusic.Core.Models;

namespace CatClawMusic.UI.ViewModels;

public class LibraryViewModel : BindableObject
{
    private readonly IMusicLibraryService _musicLibrary;
    private string _currentTab = "Local";

    public ObservableCollection<CoreModels.Song> Songs { get; set; } = new();

    public string LocalTabColor => _currentTab == "Local" ? "#FF6B9D" : "#CCCCCC";
    public string NetworkTabColor => _currentTab == "Network" ? "#FF6B9D" : "#CCCCCC";

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        set
        {
            _hasError = value;
            OnPropertyChanged();
        }
    }

    private string _errorMessage = "";
    public string ErrorMessage
    {
        get => _errorMessage;
        set
        {
            _errorMessage = value;
            OnPropertyChanged();
        }
    }

    public Command<string> SwitchTabCommand { get; }
    public Command RefreshCommand { get; }

    public LibraryViewModel(IMusicLibraryService musicLibrary)
    {
        _musicLibrary = musicLibrary;
        SwitchTabCommand = new Command<string>(SwitchTab);
        RefreshCommand = new Command(async () => await RefreshAsync());
        LoadLocalSongs();
    }

    private void SwitchTab(string tab)
    {
        _currentTab = tab;
        OnPropertyChanged(nameof(LocalTabColor));
        OnPropertyChanged(nameof(NetworkTabColor));

        if (tab == "Local")
            LoadLocalSongs();
        else
            LoadNetworkSongs();
    }

    public async void LoadLocalSongs()
    {
        IsLoading = true;
        HasError = false;
        StatusText = "正在扫描本地音乐...";
        Songs.Clear();

        try
        {
            var songs = await _musicLibrary.ScanLocalAsync();
            foreach (var song in songs)
            {
                Songs.Add(song);
            }
            StatusText = Songs.Count > 0
                ? $"共 {Songs.Count} 首本地歌曲"
                : "未找到本地音乐，请将音乐文件放入 Music 文件夹";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"扫描失败: {ex.Message}";
            StatusText = "扫描失败，下拉刷新重试";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async void LoadNetworkSongs()
    {
        IsLoading = true;
        HasError = false;
        StatusText = "正在连接 WebDAV...";
        Songs.Clear();

        try
        {
            var songs = await _musicLibrary.ScanNetworkAsync(new CoreModels.ConnectionProfile());
            foreach (var song in songs)
            {
                Songs.Add(song);
            }
            StatusText = Songs.Count > 0
                ? $"共 {Songs.Count} 首网络歌曲"
                : "未配置 WebDAV 连接或未找到歌曲";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"连接失败: {ex.Message}";
            StatusText = "连接失败，请检查设置";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (_currentTab == "Local")
            LoadLocalSongs();
        else
            LoadNetworkSongs();
    }
}
