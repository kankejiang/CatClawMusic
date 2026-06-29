using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly MusicDatabase _db;

    // === Observable Properties ===
    
    [ObservableProperty]
    private ObservableCollection<Song> _dailyRecommendSongs = new();

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _currentTabIndex;

    [ObservableProperty]
    private string _sectionTitle = "每日推荐";

    [ObservableProperty]
    private ObservableCollection<Artist> _artists = new();

    [ObservableProperty]
    private ObservableCollection<Album> _albums = new();

    [ObservableProperty]
    private ObservableCollection<Song> _topPlayedSongs = new();

    [ObservableProperty]
    private ObservableCollection<Song> _recentAddedSongs = new();

    [ObservableProperty]
    private string _agentName = "Yuki";

    [ObservableProperty]
    private ObservableCollection<object> _chatMessages = new();

    [ObservableProperty]
    private string _chatInput = "";

    [ObservableProperty]
    private bool _isChatMode;

    // === Commands ===
    
    public IRelayCommand<int> SwitchTabCommand { get; }
    public IAsyncRelayCommand LoadDataCommand { get; }
    public IAsyncRelayCommand LoadExploreDataCommand { get; }
    public IRelayCommand EnterChatModeCommand { get; }
    public IRelayCommand ExitChatModeCommand { get; }
    public IAsyncRelayCommand SendMessageCommand { get; }

    public event EventHandler? EnterChatModeRequested;
    public event EventHandler? ExitChatModeRequested;

    public SearchViewModel(MusicDatabase db)
    {
        _db = db;

        // Initialize commands
        SwitchTabCommand = new RelayCommand<int>(SwitchTab);
        LoadDataCommand = new AsyncRelayCommand(LoadDataAsync);
        LoadExploreDataCommand = new AsyncRelayCommand(LoadExploreDataAsync);
        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync);
        EnterChatModeCommand = new RelayCommand(EnterChatMode);
        ExitChatModeCommand = new RelayCommand(ExitChatMode);

        // Load initial data
        _ = LoadDataAsync();
    }

    private void SwitchTab(int index)
    {
        CurrentTabIndex = index;
        SectionTitle = index switch
        {
            0 => "每日推荐",
            1 => "艺术家",
            2 => "专辑",
            3 => "最多播放",
            4 => "最新音乐",
            _ => "每日推荐"
        };
    }

    private void EnterChatMode()
    {
        IsChatMode = true;
        EnterChatModeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExitChatMode()
    {
        IsChatMode = false;
        ExitChatModeRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadDataAsync()
    {
        try
        {
            IsLoading = true;

            // Load daily recommend songs
            var songs = await _db.GetSongsWithDetailsAsync();
            DailyRecommendSongs = new ObservableCollection<Song>(songs.Take(20).ToList());

            System.Diagnostics.Debug.WriteLine($"[SearchViewModel] Loaded {songs.Count} songs");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SearchViewModel] 加载数据失败: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task LoadExploreDataAsync()
    {
        // Same as LoadDataAsync - loads explore data
        await LoadDataAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        // TODO: Implement search suggestions with debounce
        System.Diagnostics.Debug.WriteLine($"[SearchViewModel] Search query: {value}");
    }

    public async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;

        var userMessage = ChatInput;
        ChatInput = "";

        // Add user message
        ChatMessages.Add(new
        {
            Role = "user",
            Content = userMessage
        });

        // Simulate AI response
        await Task.Delay(500);
        
        ChatMessages.Add(new
        {
            Role = "assistant",
            Content = $"收到您的消息：{userMessage}"
        });
    }
}

public class ExploreMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}
