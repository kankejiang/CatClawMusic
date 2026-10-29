using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.WinUI.Infrastructure;
using CatClawMusic.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.WinUI.ViewModels;

/// <summary>
/// 音乐库页：扫描目录 → 曲库列表（歌曲 / 歌手 / 专辑），并驱动播放。
/// 数据全部来自复用的 <see cref="IMusicLibraryService"/>。
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private const string KeyMusicFolders = "music_folders";

    private readonly IMusicLibraryService _library;
    private readonly PlaybackService _playback;
    private readonly FolderPickerService _folderPicker;
    private readonly JsonSettingsStore _settings;

    private List<Song> _allSongs = [];
    private string _searchText = string.Empty;
    private string? _artistFilter;
    private string? _albumFilter;

    public LibraryViewModel(
        IMusicLibraryService library,
        PlaybackService playback,
        FolderPickerService folderPicker,
        JsonSettingsStore settings)
    {
        _library = library;
        _playback = playback;
        _folderPicker = folderPicker;
        _settings = settings;
        StatusText = "还没有扫描音乐";
    }

    public ObservableCollection<SongRow> Songs { get; } = [];
    public ObservableCollection<ArtistRow> Artists { get; } = [];
    public ObservableCollection<AlbumRow> Albums { get; } = [];

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>当前生效的筛选条件描述（空字符串表示无筛选）</summary>
    public string FilterText
    {
        get
        {
            if (_artistFilter is not null)
                return $"歌手：{_artistFilter}";
            if (_albumFilter is not null)
                return $"专辑：{_albumFilter}";
            return string.Empty;
        }
    }

    public bool HasFilter => _artistFilter is not null || _albumFilter is not null;

    public bool IsEmpty => Songs.Count == 0;

    /// <summary>用户输入的搜索词（属性名避开 <c>SearchText</c>，方便 XAML 直接绑定）</summary>
    public string SearchTextValue
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    /// <summary>已保存的音乐文件夹（换行分隔，供设置/空状态展示）</summary>
    public string FoldersText => GetFolders() is { Length: > 0 } folders
        ? string.Join("\n", folders)
        : "尚未选择音乐文件夹";

    /// <summary>加载曲库（页面首次显示时调用）</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        _allSongs = await _library.GetAllSongsAsync();
        RebuildGroups();
        ApplyFilter();

        StatusText = _allSongs.Count == 0
            ? "曲库是空的，先选一个音乐文件夹"
            : $"共 {_allSongs.Count} 首歌曲";

        OnPropertyChanged(nameof(FoldersText));

        // 首次启动：目录已配置但库是空的 → 自动扫描一次（用户手动清空后不会再触发）。
        if (_allSongs.Count == 0 && GetFolders() is { Length: > 0 } folders)
            await ScanAsync(folders.ToList());
    }

    /// <summary>选择文件夹并扫描（追加到已保存的目录列表）</summary>
    [RelayCommand]
    public async Task PickAndScanAsync()
    {
        var folder = await _folderPicker.PickFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
            return;

        var folders = GetFolders().ToList();
        if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(folder);
            _settings.Set(KeyMusicFolders, folders.ToArray());
        }

        await ScanAsync(folders);
    }

    /// <summary>按已保存的目录重新扫描</summary>
    [RelayCommand]
    public async Task RescanAsync()
    {
        var folders = GetFolders().ToList();
        if (folders.Count == 0)
        {
            await PickAndScanAsync();
            return;
        }

        await ScanAsync(folders);
    }

    [RelayCommand]
    private void PlaySong(SongRow? row)
    {
        if (row is null)
            return;

        _ = _playback.PlayAsync(Songs.Select(r => r.Song).ToList(), row.Song);
    }

    [RelayCommand]
    private Task PlayAllAsync() => _playback.PlayAllAsync(Songs.Select(r => r.Song).ToList());

    [RelayCommand]
    private void PlayNext(SongRow? row)
    {
        if (row is not null)
            _playback.AddNext(row.Song);
    }

    [RelayCommand]
    private void SelectArtist(ArtistRow? row)
    {
        if (row is null)
            return;

        _artistFilter = row.Name;
        _albumFilter = null;
        ApplyFilter();
        OnFilterChanged();
    }

    [RelayCommand]
    private void SelectAlbum(AlbumRow? row)
    {
        if (row is null)
            return;

        _albumFilter = row.Name;
        _artistFilter = null;
        ApplyFilter();
        OnFilterChanged();
    }

    [RelayCommand]
    private void ClearFilter()
    {
        _artistFilter = null;
        _albumFilter = null;
        ApplyFilter();
        OnFilterChanged();
    }

    /// <summary>筛选变化时由页面切回「歌曲」标签</summary>
    public event EventHandler? FilterApplied;

    private async Task ScanAsync(List<string> folders)
    {
        IsBusy = true;
        StatusText = "正在扫描…";

        try
        {
            var imported = await _library.ScanLocalAsync(folders);
            _allSongs = await _library.GetAllSongsAsync();
            RebuildGroups();
            ApplyFilter();

            StatusText = $"扫描完成：本次导入 {imported.Count} 首，曲库共 {_allSongs.Count} 首";
            OnPropertyChanged(nameof(FoldersText));
        }
        catch (Exception ex)
        {
            StatusText = $"扫描失败：{ex.Message}";
            Log.Error("library", $"扫描失败：{ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildGroups()
    {
        Artists.Clear();
        foreach (var group in _allSongs
                     .GroupBy(s => s.Artist)
                     .OrderBy(g => g.Key, StringComparer.CurrentCulture))
        {
            Artists.Add(new ArtistRow(group.Key, group.Count()));
        }

        Albums.Clear();
        foreach (var group in _allSongs
                     .GroupBy(s => s.Album)
                     .OrderBy(g => g.Key, StringComparer.CurrentCulture))
        {
            Albums.Add(new AlbumRow(group.Key, group.First().Artist, group.Count()));
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<Song> query = _allSongs;

        if (_artistFilter is not null)
            query = query.Where(s => s.Artist == _artistFilter);

        if (_albumFilter is not null)
            query = query.Where(s => s.Album == _albumFilter);

        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var keyword = _searchText.Trim();
            query = query.Where(s =>
                s.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                s.Artist.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                s.Album.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        Songs.Clear();
        foreach (var song in query)
            Songs.Add(new SongRow(song));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(HasFilter));
    }

    private void OnFilterChanged()
    {
        OnPropertyChanged(nameof(FilterText));
        OnPropertyChanged(nameof(HasFilter));
        FilterApplied?.Invoke(this, EventArgs.Empty);
    }

    private string[] GetFolders() => _settings.Get<string[]>(KeyMusicFolders, []) ?? [];
}
