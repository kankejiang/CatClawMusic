using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Adapters;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Core.Services.AI;
using CatClawMusic.Data;
using CatClawMusic.UI.ViewModels;
using Google.Android.Material.Tabs;
using IAgentService = CatClawMusic.Core.Interfaces.IAgentService;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CatClawMusic.UI.Fragments;

public class SearchFragment : Fragment
{
    private IAgentService _agentService = null!;
    private IMusicLibraryService _musicLibrary = null!;
    private INavigationService _navigationService = null!;
    private IAudioPlayerService? _audioPlayer;
    private PlayQueue? _playQueue;
    private ExploreDataService? _exploreData;
    private NetEaseMusicScraper? _scraper;

    // Explore mode views
    private EditText _searchInput = null!;
    private ImageView _yukiAvatar = null!;
    private TabLayout _tabLayout = null!;
    private RecyclerView _rvDailyRecommend = null!;
    private RecyclerView _rvArtists = null!;
    private RecyclerView _rvAlbums = null!;
    private RecyclerView _rvTopPlayed = null!;
    private RecyclerView _rvRecentAdded = null!;
    private View _layoutExploreMain = null!;
    private readonly RecyclerView[] _tabRecyclerViews = new RecyclerView[5];

    // Explore adapters
    private ExploreSongAdapter _dailyRecommendAdapter = null!;
    private ArtistAdapter _artistAdapter = null!;
    private AlbumAdapter _albumAdapter = null!;
    private ExploreSongAdapter _topPlayedAdapter = null!;
    private ExploreSongAdapter _recentAddedAdapter = null!;

    // Search suggestion views
    private View _cardSearchSuggestions = null!;
    private RecyclerView _rvSearchSuggestions = null!;
    private SearchSuggestionAdapter _suggestionAdapter = null!;
    private CancellationTokenSource? _suggestionCts;

    // Chat mode views
    private View _layoutChatOverlay = null!;
    private RecyclerView _chatMessages = null!;
    private EditText _chatInput = null!;
    private ImageButton _sendButton = null!;
    private ExploreMessageAdapter _chatAdapter = null!;
    private List<Song> _lastSearchResults = new();
    private bool _hasShownSearchCards;
    private ImageView _agentAvatarHeader = null!;
    private TextView _agentNameHeader = null!;

    private string? _wizardProviderId;
    private LlmProviderInfo? _wizardProvider;
    private string? _wizardApiKey;
    private List<string> _wizardModels = new();
    private bool _isInWizard;
    private bool _waitingForKeyInput;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_search, container, false)!;

    public override void OnViewCreated(View view, Bundle? savedInstanceState)
    {
        base.OnViewCreated(view, savedInstanceState);

        _agentService = MainApplication.Services.GetRequiredService<IAgentService>();
        _musicLibrary = MainApplication.Services.GetRequiredService<IMusicLibraryService>();
        _navigationService = MainApplication.Services.GetRequiredService<INavigationService>();
        _audioPlayer = MainApplication.Services.GetService<IAudioPlayerService>();
        _playQueue = MainApplication.Services.GetService<PlayQueue>();

        InitExploreViews(view);
        InitChatViews(view);

        // 同步初始化轻量级服务引用（GetService 对单例只是返回缓存实例，不会阻塞）
        _exploreData = MainApplication.Services.GetService<ExploreDataService>();
        _scraper = MainApplication.Services.GetService<NetEaseMusicScraper>();

        // 在后台线程加载重量级数据，避免阻塞主线程导致 ANR
        _ = Task.Run(async () =>
        {
            if (_scraper != null)
            {
                Activity?.RunOnUiThread(() => _artistAdapter.SetScraper(_scraper));
            }
            await LoadExploreDataAsync();
        });
    }

    private void InitExploreViews(View view)
    {
        _layoutExploreMain = view.FindViewById<View>(Resource.Id.layout_explore_main)!;
        _searchInput = view.FindViewById<EditText>(Resource.Id.et_search_input)!;
        _yukiAvatar = view.FindViewById<ImageView>(Resource.Id.iv_yuki_avatar)!;
        _tabLayout = view.FindViewById<TabLayout>(Resource.Id.tab_layout)!;

        _rvDailyRecommend = view.FindViewById<RecyclerView>(Resource.Id.rv_daily_recommend)!;
        _rvArtists = view.FindViewById<RecyclerView>(Resource.Id.rv_artists)!;
        _rvAlbums = view.FindViewById<RecyclerView>(Resource.Id.rv_albums)!;
        _rvTopPlayed = view.FindViewById<RecyclerView>(Resource.Id.rv_top_played)!;
        _rvRecentAdded = view.FindViewById<RecyclerView>(Resource.Id.rv_recent_added)!;

        _tabRecyclerViews[0] = _rvDailyRecommend;
        _tabRecyclerViews[1] = _rvArtists;
        _tabRecyclerViews[2] = _rvAlbums;
        _tabRecyclerViews[3] = _rvTopPlayed;
        _tabRecyclerViews[4] = _rvRecentAdded;

        // 每日推荐 - 垂直网格
        _dailyRecommendAdapter = new ExploreSongAdapter { ShowPlayAllButton = true };
        var dailyGrid = new GridLayoutManager(Context, 2);
        dailyGrid.SetSpanSizeLookup(new DailyRecommendSpanLookup(_dailyRecommendAdapter));
        _rvDailyRecommend.SetLayoutManager(dailyGrid);
        _rvDailyRecommend.SetAdapter(_dailyRecommendAdapter);
        _rvDailyRecommend.SetItemViewCacheSize(20);
        _rvDailyRecommend.GetRecycledViewPool().SetMaxRecycledViews(1, 30);
        _dailyRecommendAdapter.OnSongClick += async (s, song) => await PlaySongAsync(song);
        _dailyRecommendAdapter.OnPlayAllClick += (s, e) => PlayAllDailyRecommend();

        // 艺术家 - 垂直网格
        _artistAdapter = new ArtistAdapter();
        if (_scraper != null) _artistAdapter.SetScraper(_scraper);
        _rvArtists.SetLayoutManager(new GridLayoutManager(Context, 3));
        _rvArtists.SetAdapter(_artistAdapter);
        _artistAdapter.OnArtistClick += (s, artist) => ShowArtistDetail(artist);

        // 专辑 - 垂直网格
        _albumAdapter = new AlbumAdapter();
        _rvAlbums.SetLayoutManager(new GridLayoutManager(Context, 2));
        _rvAlbums.SetAdapter(_albumAdapter);
        _albumAdapter.OnAlbumClick += (s, album) => ShowAlbumDetail(album);

        // 最多播放 - 垂直列表（显示播放次数）
        _topPlayedAdapter = new ExploreSongAdapter { ShowPlayCount = true };
        _rvTopPlayed.SetLayoutManager(new LinearLayoutManager(Context));
        _rvTopPlayed.SetAdapter(_topPlayedAdapter);
        _topPlayedAdapter.OnSongClick += async (s, song) => await PlaySongAsync(song);

        // 最新音乐 - 垂直列表
        _recentAddedAdapter = new ExploreSongAdapter();
        _rvRecentAdded.SetLayoutManager(new LinearLayoutManager(Context));
        _rvRecentAdded.SetAdapter(_recentAddedAdapter);
        _recentAddedAdapter.OnSongClick += async (s, song) => await PlaySongAsync(song);

        // 初始化 Tab
        _tabLayout.AddTab(_tabLayout.NewTab().SetText("每日推荐"), 0, true);
        _tabLayout.AddTab(_tabLayout.NewTab().SetText("艺术家"), 1);
        _tabLayout.AddTab(_tabLayout.NewTab().SetText("专辑"), 2);
        _tabLayout.AddTab(_tabLayout.NewTab().SetText("最多播放"), 3);
        _tabLayout.AddTab(_tabLayout.NewTab().SetText("最新音乐"), 4);

        _tabLayout.TabSelected += (s, e) =>
        {
            var index = e.Tab?.Position ?? 0;
            SwitchTab(index);
        };

        // 搜索框回车进入聊天模式并搜索
        _searchInput.EditorAction += (s, e) =>
        {
            if (e.ActionId == Android.Views.InputMethods.ImeAction.Search)
            {
                var keyword = _searchInput.Text?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    HideSearchSuggestions();
                    EnterChatMode();
                    _chatInput.Text = keyword;
                    SendMessage();
                }
                e.Handled = true;
            }
        };

        // 搜索框文本变化时显示搜索建议
        _searchInput.AfterTextChanged += (s, e) => OnSearchInputChanged();

        // 搜索框获取焦点时，如果有文字则显示建议
        _searchInput.FocusChange += (s, e) =>
        {
            if (e.HasFocus && !string.IsNullOrWhiteSpace(_searchInput.Text?.ToString()?.Trim()))
                OnSearchInputChanged();
            else if (!e.HasFocus)
                HideSearchSuggestions();
        };

        // 搜索建议下拉框
        _cardSearchSuggestions = view.FindViewById<View>(Resource.Id.card_search_suggestions)!;
        _rvSearchSuggestions = view.FindViewById<RecyclerView>(Resource.Id.rv_search_suggestions)!;
        _suggestionAdapter = new SearchSuggestionAdapter();
        _rvSearchSuggestions.SetLayoutManager(new LinearLayoutManager(Context));
        _rvSearchSuggestions.SetAdapter(_suggestionAdapter);
        _suggestionAdapter.OnArtistClick += (s, artist) =>
        {
            HideSearchSuggestions();
            _searchInput.ClearFocus();
            var imm = Context?.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
            imm?.HideSoftInputFromWindow(_searchInput.WindowToken, 0);
            ShowArtistDetail(artist);
        };
        _suggestionAdapter.OnSongClick += async (s, song) =>
        {
            HideSearchSuggestions();
            _searchInput.ClearFocus();
            var imm = Context?.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
            imm?.HideSoftInputFromWindow(_searchInput.WindowToken, 0);
            await PlaySongAsync(song);
        };

        // 点击 Yuki 头像进入聊天模式
        _yukiAvatar.Click += (s, e) => EnterChatMode();
    }

    private void InitChatViews(View view)
    {
        _layoutChatOverlay = view.FindViewById<View>(Resource.Id.layout_chat_overlay)!;
        _chatMessages = view.FindViewById<RecyclerView>(Resource.Id.chat_messages)!;
        _chatInput = view.FindViewById<EditText>(Resource.Id.et_chat_input)!;
        _sendButton = view.FindViewById<ImageButton>(Resource.Id.btn_send)!;
        _agentAvatarHeader = view.FindViewById<ImageView>(Resource.Id.iv_agent_avatar_header)!;
        _agentNameHeader = view.FindViewById<TextView>(Resource.Id.tv_agent_name)!;

        var btnChatBack = view.FindViewById<ImageButton>(Resource.Id.btn_chat_back)!;
        var btnAiSettings = view.FindViewById<ImageButton>(Resource.Id.btn_ai_settings)!;
        var btnClearChat = view.FindViewById<ImageButton>(Resource.Id.btn_clear_chat)!;

        _chatAdapter = new ExploreMessageAdapter();
        _chatMessages.SetLayoutManager(new LinearLayoutManager(Context));
        _chatMessages.SetAdapter(_chatAdapter);

        UpdateAgentHeader();

        _chatAdapter.OnSongPlay += async (s, song) => await PlaySongAsync(song);
        _chatAdapter.OnWizardCancel += (s, step) => OnWizardCancelled(step);
        _chatAdapter.OnWizardNext += (s, step) => _ = OnWizardNextAsync(step);

        _sendButton.Click += (s, e) => SendMessage();

        _chatInput.EditorAction += (s, e) =>
        {
            if (e.ActionId == Android.Views.InputMethods.ImeAction.Send)
            {
                SendMessage();
                e.Handled = true;
            }
        };

        btnChatBack.Click += (s, e) => ExitChatMode();
        btnAiSettings.Click += (s, e) => _navigationService.PushFragment("AiSettings");
        btnClearChat.Click += (s, e) =>
        {
            _agentService.ClearConversation();
            _chatAdapter.Clear();
            _lastSearchResults.Clear();
            _wizardProviderId = null;
            _wizardApiKey = null;
            _wizardProvider = null;
            _wizardModels.Clear();
            _isInWizard = false;
            _waitingForKeyInput = false;
            UpdateAgentHeader();
        };
    }

    private void SwitchTab(int index)
    {
        for (var i = 0; i < _tabRecyclerViews.Length; i++)
        {
            _tabRecyclerViews[i].Visibility = i == index ? ViewStates.Visible : ViewStates.Gone;
        }
    }

    private void OnSearchInputChanged()
    {
        var keyword = _searchInput.Text?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(keyword) || keyword.Length < 1)
        {
            HideSearchSuggestions();
            return;
        }
        _ = SearchSuggestionsAsync(keyword);
    }

    private async Task SearchSuggestionsAsync(string keyword)
    {
        _suggestionCts?.Cancel();
        _suggestionCts?.Dispose();
        _suggestionCts = new CancellationTokenSource();
        var ct = _suggestionCts.Token;

        try
        {
            await Task.Delay(250, ct);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested) return;

        try
        {
            var artistsTask = _exploreData?.GetArtistsWithSongCountAsync() ?? Task.FromResult(new List<ArtistWithCount>());
            var songsTask = _musicLibrary.SearchAsync(keyword);

            await Task.WhenAll(artistsTask, songsTask);
            if (ct.IsCancellationRequested) return;

            var allArtists = artistsTask.Result;
            var matchedArtists = allArtists
                .Where(a => a.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();

            var matchedSongs = songsTask.Result.Take(3).ToList();

            Activity?.RunOnUiThread(() =>
            {
                if (ct.IsCancellationRequested) return;
                if (matchedArtists.Count == 0 && matchedSongs.Count == 0)
                {
                    HideSearchSuggestions();
                    return;
                }
                _suggestionAdapter.UpdateSuggestions(matchedArtists, matchedSongs);
                _cardSearchSuggestions.Visibility = ViewStates.Visible;
            });
        }
        catch (System.OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Explore] 搜索建议失败: {ex.Message}");
        }
    }

    private void HideSearchSuggestions()
    {
        _suggestionCts?.Cancel();
        _cardSearchSuggestions.Visibility = ViewStates.Gone;
        _suggestionAdapter.Clear();
    }

    private void EnterChatMode()
    {
        HideSearchSuggestions();
        _layoutChatOverlay.Visibility = ViewStates.Visible;
        _layoutExploreMain.Visibility = ViewStates.Gone;
    }

    private void ExitChatMode()
    {
        _layoutChatOverlay.Visibility = ViewStates.Gone;
        _layoutExploreMain.Visibility = ViewStates.Visible;
        var imm = Context?.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
        imm?.HideSoftInputFromWindow(_chatInput.WindowToken, 0);
    }

    private async Task LoadExploreDataAsync()
    {
        if (_exploreData == null) return;
        try
        {
            // 读取来源筛选设置（SharedPreferences 读取可能耗时，已在后台线程）
            var prefs = Activity?.GetSharedPreferences("playlist_sort", Android.Content.FileCreationMode.Private);
            var sourceFilter = prefs?.GetString("source_filter_-1", "all") ?? "all";
            _exploreData.SetSourceFilter(sourceFilter);

            var dailyTask = _exploreData.GetDailyRecommendAsync();
            var artistsTask = _exploreData.GetArtistsWithSongCountAsync();
            var albumsTask = _exploreData.GetAlbumsWithSongCountAsync();
            var topPlayedTask = _exploreData.GetTopPlayedSongsAsync(20);
            var recentTask = _exploreData.GetRecentlyAddedSongsAsync(20);

            await Task.WhenAll(dailyTask, artistsTask, albumsTask, topPlayedTask, recentTask);

            Activity?.RunOnUiThread(() =>
            {
                _dailyRecommendAdapter.UpdateSongs(dailyTask.Result);
                _artistAdapter.UpdateArtists(artistsTask.Result);
                _albumAdapter.UpdateAlbums(albumsTask.Result);
                _topPlayedAdapter.UpdateSongs(topPlayedTask.Result);
                _recentAddedAdapter.UpdateSongs(recentTask.Result);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Explore] 加载数据失败: {ex}");
        }
    }

    public override void OnResume()
    {
        base.OnResume();
        // 确保 _exploreData 已初始化（防止 OnViewCreated 中的 Task.Run 尚未完成）
        _exploreData ??= MainApplication.Services.GetService<ExploreDataService>();
        // 每次回到探索页刷新数据（后台线程执行避免阻塞 UI）
        _ = Task.Run(() => LoadExploreDataAsync());
    }

    private void ShowArtistDetail(ArtistWithCount artist)
    {
        _navigationService.PushFragment("ArtistDetail", new Dictionary<string, object> { ["artistName"] = artist.Name });
    }

    private void ShowAlbumDetail(AlbumWithCount album)
    {
        _navigationService.PushFragment("AlbumDetail", new Dictionary<string, object>
        {
            ["albumTitle"] = album.Title,
            ["albumArtist"] = album.ArtistName
        });
    }

    // ═══════════ Chat 功能（保留原有逻辑）═══════════

    private void HideInputLayout()
    {
        _chatInput.Visibility = ViewStates.Gone;
        _sendButton.Visibility = ViewStates.Gone;
        var imm = Context?.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
        imm?.HideSoftInputFromWindow(_chatInput.WindowToken, 0);
    }

    private void ShowInputLayout()
    {
        _chatInput.Visibility = ViewStates.Visible;
        _sendButton.Visibility = ViewStates.Visible;
    }

    private void ShowInputLayoutForKey()
    {
        _chatInput.Visibility = ViewStates.Visible;
        _sendButton.Visibility = ViewStates.Visible;
        _chatInput.RequestFocus();
        var imm = Context?.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
        imm?.ShowSoftInput(_chatInput, 0);
    }

    private void SendMessage()
    {
        var text = _chatInput.Text?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        _chatInput.Text = "";

        if (_waitingForKeyInput)
        {
            _waitingForKeyInput = false;
            HideInputLayout();
            ProcessWizardKeyInput(text);
            return;
        }

        _chatAdapter.AddMessage(new ExploreMessage { Role = "user", Content = text });
        ScrollToBottom();

        if (TryHandleCommand(text))
            return;

        var configured = _agentService.IsConfigured;
        if (configured)
        {
            _ = SendMessageToAiAsync(text);
        }
        else
        {
            _ = SearchAndReplyAsync(text);
        }
    }

    private bool TryHandleCommand(string text)
    {
        if (text == "添加模型")
        {
            StartAddModelWizard();
            return true;
        }

        var playMatch = Regex.Match(text, @"^播放[\s]+(.+)$");
        if (playMatch.Success)
        {
            var keyword = playMatch.Groups[1].Value.Trim();
            _ = PlayByKeywordAsync(keyword);
            return true;
        }

        if (text == "播放") { _ = ResumePlaybackAsync(); return true; }
        if (text == "暂停") { _ = PausePlaybackAsync(); return true; }
        if (text == "上一曲") { _ = PreviousTrackAsync(); return true; }
        if (text == "下一曲") { _ = NextTrackAsync(); return true; }

        var playlistMatch = Regex.Match(text, @"^创建歌单[\s]+(.+)$");
        if (playlistMatch.Success)
        {
            var name = playlistMatch.Groups[1].Value.Trim();
            _ = CreatePlaylistAsync(name);
            return true;
        }

        return false;
    }

    private void StartAddModelWizard()
    {
        _isInWizard = true;
        _waitingForKeyInput = false;
        _wizardProviderId = null;
        _wizardApiKey = null;
        _wizardProvider = null;
        _wizardModels.Clear();

        HideInputLayout();

        var providers = AgentService.GetProviders();
        _chatAdapter.AddMessage(new ExploreMessage
        {
            Role = "wizard",
            WizardStep = 1,
            WizardProviders = providers
        });
        ScrollToBottom();
    }

    private void OnWizardCancelled(int step)
    {
        var lastMsg = _chatAdapter.GetLastMessage();
        if (lastMsg != null && lastMsg.IsWizard)
        {
            lastMsg.WizardCompleted = true;
            _chatAdapter.UpdateLastMessage(lastMsg);
        }

        _isInWizard = false;
        _waitingForKeyInput = false;
        _chatAdapter.AddMessage(new ExploreMessage { Role = "assistant", Content = "已取消添加模型" });
        ScrollToBottom();
        ShowInputLayout();
    }

    private void ExitWizard()
    {
        _isInWizard = false;
        _waitingForKeyInput = false;
        ShowInputLayout();
    }

    private async Task OnWizardNextAsync(int step)
    {
        var lastMsg = _chatAdapter.GetLastMessage();
        if (lastMsg == null || !lastMsg.IsWizard) return;

        if (step == 1)
        {
            _wizardProviderId = lastMsg.Content;
            var providers = AgentService.GetProviders();
            _wizardProvider = providers.FirstOrDefault(p => p.Id == _wizardProviderId);
            if (_wizardProvider == null) return;

            lastMsg.WizardCompleted = true;
            _chatAdapter.UpdateLastMessage(lastMsg);

            _chatAdapter.AddMessage(new ExploreMessage
            {
                Role = "assistant",
                Content = $"已选择「{_wizardProvider.Name}」，请在下方输入 API Key"
            });
            ScrollToBottom();

            _waitingForKeyInput = true;
            ShowInputLayoutForKey();
        }
        else if (step == 2)
        {
            _wizardApiKey = lastMsg.Content;
            if (string.IsNullOrWhiteSpace(_wizardApiKey)) return;

            lastMsg.WizardCompleted = true;
            _chatAdapter.UpdateLastMessage(lastMsg);

            HideInputLayout();

            var wizardMsg = new ExploreMessage
            {
                Role = "wizard",
                WizardStep = 3,
                WizardModels = _wizardProvider?.PresetModels?.ToList() ?? new List<string>(),
                PresetModels = _wizardProvider?.PresetModels
            };
            _chatAdapter.AddMessage(wizardMsg);
            ScrollToBottom();

            if (_wizardProvider?.PresetModels == null || _wizardProvider.PresetModels.Length == 0)
                _ = FetchWizardModelsAsync(wizardMsg);
        }
        else if (step == 3)
        {
            var selectedModel = lastMsg.Content;
            if (string.IsNullOrWhiteSpace(selectedModel)) return;

            lastMsg.WizardCompleted = true;
            _chatAdapter.UpdateLastMessage(lastMsg);

            var config = new LlmConfig
            {
                Name = $"{_wizardProvider?.Name}-{selectedModel}",
                Provider = _wizardProviderId ?? "custom",
                ApiUrl = _wizardProvider?.DefaultApiUrl ?? "",
                ApiKey = _wizardApiKey ?? "",
                Model = selectedModel,
                Temperature = 0.7,
                MaxTokens = 2048,
                Enabled = true
            };

            AgentService.SaveConfig(config);

            var existingConfigName = AgentService.GetCurrentConfigName();
            var hasExisting = !string.IsNullOrEmpty(existingConfigName);

            if (hasExisting)
            {
                _chatAdapter.AddMessage(new ExploreMessage
                {
                    Role = "wizard",
                    WizardStep = 4,
                    Content = config.Name,
                    WizardExtra = config.Name
                });
                ScrollToBottom();
            }
            else
            {
                AgentService.SetCurrentConfigName(config.Name);
                _chatAdapter.AddMessage(new ExploreMessage
                {
                    Role = "assistant",
                    Content = $"已配置完毕！模型「{config.Name}」已启用，现在可以使用 AI 对话功能了 🎉"
                });
                ScrollToBottom();
                ExitWizard();
            }
        }
        else if (step == 4)
        {
            var enableNew = lastMsg.Content == "yes";
            var newConfigName = lastMsg.WizardExtra;

            if (enableNew && !string.IsNullOrEmpty(newConfigName))
            {
                AgentService.SetCurrentConfigName(newConfigName);
                _chatAdapter.AddMessage(new ExploreMessage
                {
                    Role = "assistant",
                    Content = $"已启用新模型「{newConfigName}」🎉"
                });
            }
            else
            {
                var currentName = AgentService.GetCurrentConfigName();
                _chatAdapter.AddMessage(new ExploreMessage
                {
                    Role = "assistant",
                    Content = $"保持使用当前模型「{currentName}」，新模型已保存但未启用"
                });
            }
            ScrollToBottom();
            ExitWizard();
        }
    }

    private void ProcessWizardKeyInput(string key)
    {
        _wizardApiKey = key;

        _chatAdapter.AddMessage(new ExploreMessage
        {
            Role = "wizard",
            WizardStep = 2,
            Content = key
        });
        ScrollToBottom();
    }

    private async Task FetchWizardModelsAsync(ExploreMessage wizardMsg)
    {
        var apiUrl = _wizardProvider?.DefaultApiUrl ?? "";
        var apiKey = _wizardApiKey ?? "";

        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            Activity?.RunOnUiThread(() =>
            {
                wizardMsg.WizardModels = new List<string>();
                _chatAdapter.UpdateLastMessage(wizardMsg);
            });
            return;
        }

        try
        {
            var modelsUrl = BuildModelsUrl(apiUrl);
            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, modelsUrl);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            using var response = await client.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var models = ParseModels(body);
                models.Sort((a, b) => string.Compare(a, b, StringComparison.Ordinal));
                _wizardModels = models;

                Activity?.RunOnUiThread(() =>
                {
                    if (models.Count > 0)
                    {
                        wizardMsg.WizardModels = models;
                    }
                    else
                    {
                        wizardMsg.WizardModels = wizardMsg.PresetModels?.ToList() ?? new List<string>();
                    }
                    _chatAdapter.UpdateLastMessage(wizardMsg);
                    ScrollToBottom();
                });
            }
            else
            {
                Activity?.RunOnUiThread(() =>
                {
                    wizardMsg.WizardModels = wizardMsg.PresetModels?.ToList() ?? new List<string>();
                    _chatAdapter.UpdateLastMessage(wizardMsg);
                });
            }
        }
        catch
        {
            Activity?.RunOnUiThread(() =>
            {
                wizardMsg.WizardModels = wizardMsg.PresetModels?.ToList() ?? new List<string>();
                _chatAdapter.UpdateLastMessage(wizardMsg);
            });
        }
    }

    private static string BuildModelsUrl(string apiUrl)
    {
        var url = apiUrl.TrimEnd('/');
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url = url[..^"/chat/completions".Length];
        if (url.Contains("/compatible-mode/", StringComparison.OrdinalIgnoreCase))
            url = url[..url.IndexOf("/compatible-mode/", StringComparison.OrdinalIgnoreCase)] + "/api/v1/models";
        else if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) || url.EndsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            url = url.TrimEnd('/') + "/models";
        else
            url = url + "/v1/models";
        return url;
    }

    private static List<string> ParseModels(string body)
    {
        var models = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var id))
                    {
                        var idStr = id.GetString();
                        if (!string.IsNullOrEmpty(idStr))
                            models.Add(idStr);
                    }
                }
            }
        }
        catch { }
        return models;
    }

    private async Task PlayByKeywordAsync(string keyword)
    {
        var thinkingMsg = new ExploreMessage { Role = "assistant", Content = $"正在搜索「{keyword}」..." };
        _chatAdapter.AddMessage(thinkingMsg);
        ScrollToBottom();

        try
        {
            var results = await _musicLibrary.SearchAsync(keyword);
            _lastSearchResults = results;

            if (results.Count == 0)
            {
                Activity?.RunOnUiThread(() =>
                {
                    _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = $"没有找到「{keyword}」相关的歌曲" });
                    ScrollToBottom();
                });
                return;
            }

            var firstSong = results[0];
            if (_audioPlayer != null && _playQueue != null)
            {
                _playQueue.SetSongs(results);
                _playQueue.SelectSong(firstSong.Id);
                if (!string.IsNullOrEmpty(firstSong.FilePath))
                    await _audioPlayer.PlayAsync(firstSong.FilePath);
            }

            Activity?.RunOnUiThread(() =>
            {
                _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = $"正在播放「{firstSong.Title}」- {firstSong.Artist}" });
                ScrollToBottom();
            });
        }
        catch (Exception ex)
        {
            Activity?.RunOnUiThread(() =>
            {
                _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = $"播放失败: {ex.Message}" });
                ScrollToBottom();
            });
        }
    }

    private async Task ResumePlaybackAsync()
    {
        if (_audioPlayer == null) { Reply("播放器未就绪"); return; }
        if (_audioPlayer.IsPlaying) { Reply("正在播放中"); return; }
        var currentSong = _playQueue?.CurrentSong;
        if (currentSong != null) { await _audioPlayer.ResumeAsync(); Reply($"继续播放「{currentSong.Title}」"); }
        else Reply("当前没有可播放的歌曲");
    }

    private async Task PausePlaybackAsync()
    {
        if (_audioPlayer == null) { Reply("播放器未就绪"); return; }
        if (!_audioPlayer.IsPlaying) { Reply("当前没有在播放"); return; }
        await _audioPlayer.PauseAsync();
        var currentSong = _playQueue?.CurrentSong;
        Reply(currentSong != null ? $"已暂停「{currentSong.Title}」" : "已暂停");
    }

    private async Task PreviousTrackAsync()
    {
        if (_playQueue == null || _audioPlayer == null) { Reply("播放器未就绪"); return; }
        var prev = _playQueue.Previous();
        if (prev != null) { if (!string.IsNullOrEmpty(prev.FilePath)) await _audioPlayer.PlayAsync(prev.FilePath); Reply($"上一曲：「{prev.Title}」"); }
        else Reply("没有上一曲了");
    }

    private async Task NextTrackAsync()
    {
        if (_playQueue == null || _audioPlayer == null) { Reply("播放器未就绪"); return; }
        var next = _playQueue.Next();
        if (next != null) { if (!string.IsNullOrEmpty(next.FilePath)) await _audioPlayer.PlayAsync(next.FilePath); Reply($"下一曲：「{next.Title}」"); }
        else Reply("没有下一曲了");
    }

    private async Task CreatePlaylistAsync(string name)
    {
        try { await _musicLibrary.CreatePlaylistAsync(name); Activity?.RunOnUiThread(() => Reply($"歌单「{name}」已创建")); }
        catch (Exception ex) { Activity?.RunOnUiThread(() => Reply($"创建歌单失败: {ex.Message}")); }
    }

    private void Reply(string message)
    {
        Activity?.RunOnUiThread(() =>
        {
            _chatAdapter.AddMessage(new ExploreMessage { Role = "assistant", Content = message });
            ScrollToBottom();
        });
    }

    private async Task SearchAndReplyAsync(string keyword)
    {
        var thinkingMsg = new ExploreMessage { Role = "assistant", Content = "搜索中..." };
        _chatAdapter.AddMessage(thinkingMsg);
        ScrollToBottom();

        try
        {
            var results = await _musicLibrary.SearchAsync(keyword);
            _lastSearchResults = results;
            Activity?.RunOnUiThread(() =>
            {
                _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "search", SearchKeyword = keyword, Songs = results });
                ScrollToBottom();
            });
        }
        catch (Exception ex)
        {
            Activity?.RunOnUiThread(() =>
            {
                _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = $"搜索失败: {ex.Message}" });
                ScrollToBottom();
            });
        }
    }

    private async Task SendMessageToAiAsync(string userMessage)
    {
        _hasShownSearchCards = false;
        var thinkingMsg = new ExploreMessage { Role = "assistant", Content = "思考中..." };
        _chatAdapter.AddMessage(thinkingMsg);
        ScrollToBottom();

        try
        {
            var response = await _agentService.SendMessageAsync(userMessage, onPartialMessage: msg =>
            {
                Activity?.RunOnUiThread(() =>
                {
                    if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        if (!string.IsNullOrWhiteSpace(msg.Content))
                        {
                            _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = msg.Content });
                            _chatAdapter.AddMessage(new ExploreMessage { Role = "assistant", Content = "正在使用工具...", ToolCalls = msg.ToolCalls });
                        }
                        else
                        {
                            _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = "正在使用工具...", ToolCalls = msg.ToolCalls });
                        }
                        ScrollToBottom();
                    }
                    else if (msg.Role == "tool")
                    {
                        if (msg.Songs != null && msg.Songs.Count > 0)
                        {
                            _lastSearchResults = msg.Songs;
                            _hasShownSearchCards = true;
                            _chatAdapter.AddMessage(new ExploreMessage { Role = "search", SearchKeyword = msg.Name == "search_music" ? "AI搜索" : "", Songs = msg.Songs });
                        }
                        else
                        {
                            _chatAdapter.AddMessage(new ExploreMessage { Role = "tool", Name = msg.Name, Content = msg.Content });
                        }
                        _chatAdapter.AddMessage(new ExploreMessage { Role = "assistant", Content = "继续思考..." });
                        ScrollToBottom();
                    }
                });
            });

            Activity?.RunOnUiThread(() =>
            {
                if (_hasShownSearchCards)
                {
                    _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = "已为你找到相关歌曲~" });
                }
                else if (!string.IsNullOrWhiteSpace(response.Content))
                {
                    _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = response.Content });
                }
                else
                {
                    _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = "已为你完成操作~" });
                }
                ScrollToBottom();
            });
        }
        catch (Exception ex)
        {
            Activity?.RunOnUiThread(() =>
            {
                _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = $"出错了: {ex.Message}" });
                ScrollToBottom();
            });
        }
    }

    private async void PlayAllDailyRecommend()
    {
        try
        {
            var songs = await _exploreData!.GetDailyRecommendAsync();
            if (songs.Count == 0 || _audioPlayer == null || _playQueue == null) return;
            _lastSearchResults = songs;
            _playQueue.SetSongs(songs);
            _playQueue.SelectSong(songs[0].Id);
            if (!string.IsNullOrEmpty(songs[0].FilePath))
            {
                await _audioPlayer.PlayAsync(songs[0].FilePath);
                _ = RecordPlayAsync(songs[0]);
            }
            _navigationService.PushFragment("NowPlaying");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Explore] 全部播放失败: {ex}");
        }
    }

    private async Task PlaySongAsync(Song song)
    {
        try
        {
            if (_audioPlayer == null || _playQueue == null) return;
            var currentSongInQueue = _playQueue.CurrentSong;
            if (currentSongInQueue != null && currentSongInQueue.Id == song.Id)
            {
                if (_audioPlayer.IsPlaying) await _audioPlayer.PauseAsync();
                else await _audioPlayer.ResumeAsync();
            }
            else
            {
                var playList = _lastSearchResults.Count > 0 ? _lastSearchResults.ToList() : new List<Song> { song };
                _playQueue.SetSongs(playList);
                _playQueue.SelectSong(song.Id);
                if (!string.IsNullOrEmpty(song.FilePath))
                    await _audioPlayer.PlayAsync(song.FilePath);
                _ = RecordPlayAsync(song);
                _navigationService.PushFragment("NowPlaying");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Explore] 播放失败: {ex}");
        }
    }

    private async Task RecordPlayAsync(Song song)
    {
        try
        {
            var db = MainApplication.Services.GetService<MusicDatabase>();
            if (db == null) return;
            await db.EnsureInitializedAsync();
            await db.RecordPlayAsync(song.Id);
            var playlistVm = MainApplication.Services.GetService(typeof(PlaylistViewModel)) as PlaylistViewModel;
            if (playlistVm != null)
            {
                playlistVm.MarkDirty();
                _ = playlistVm.RefreshSystemPlaylistCountsAsync();
            }
        }
        catch { }
    }

    private void ScrollToBottom()
    {
        if (_chatAdapter.MessageCount > 0)
            _chatMessages.SmoothScrollToPosition(_chatAdapter.MessageCount - 1);
    }

    private void UpdateAgentHeader()
    {
        var agent = _agentService.GetCurrentAgent();
        _agentNameHeader.Text = agent.Name;
        _chatAdapter.CurrentAgent = agent;

        if (!string.IsNullOrEmpty(agent.AvatarDrawableName))
        {
            var ctx = Context!;
            var resId = ctx.Resources?.GetIdentifier(agent.AvatarDrawableName, "drawable", ctx.PackageName) ?? 0;
            if (resId != 0)
            {
                _agentAvatarHeader.SetImageResource(resId);
                _agentAvatarHeader.Visibility = ViewStates.Visible;
                return;
            }
        }
        _agentAvatarHeader.Visibility = ViewStates.Gone;
    }

    /// <summary>每日推荐 GridLayoutManager 的 SpanSizeLookup，让 header 占满整行</summary>
    private class DailyRecommendSpanLookup : GridLayoutManager.SpanSizeLookup
    {
        private readonly ExploreSongAdapter _adapter;
        public DailyRecommendSpanLookup(ExploreSongAdapter adapter) => _adapter = adapter;
        public override int GetSpanSize(int position)
            => _adapter.ShowPlayAllButton && position == 0 ? 2 : 1;
    }
}
