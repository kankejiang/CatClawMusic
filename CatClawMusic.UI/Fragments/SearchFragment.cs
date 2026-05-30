using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.Services.AI;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using IAgentService = CatClawMusic.UI.Services.AI.IAgentService;
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

    private RecyclerView _chatMessages = null!;
    private EditText _chatInput = null!;
    private View _inputLayout = null!;
    private ImageButton _sendButton = null!;
    private ExploreMessageAdapter _chatAdapter = null!;
    private List<Song> _lastSearchResults = new();

    private string? _wizardProviderId;
    private string? _wizardApiKey;
    private LlmProviderInfo? _wizardProvider;
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

        _chatMessages = view.FindViewById<RecyclerView>(Resource.Id.chat_messages)!;
        _chatInput = view.FindViewById<EditText>(Resource.Id.et_chat_input)!;
        _inputLayout = view.FindViewById<View>(Resource.Id.layout_input)!;
        _sendButton = view.FindViewById<ImageButton>(Resource.Id.btn_send)!;

        var btnAiSettings = view.FindViewById<ImageButton>(Resource.Id.btn_ai_settings)!;
        var btnClearChat = view.FindViewById<ImageButton>(Resource.Id.btn_clear_chat)!;

        _chatAdapter = new ExploreMessageAdapter();
        _chatMessages.SetLayoutManager(new LinearLayoutManager(Context));
        _chatMessages.SetAdapter(_chatAdapter);

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
            ShowInputLayout();
            UpdateViewState();
        };

        UpdateViewState();
    }

    public override void OnResume()
    {
        base.OnResume();
        UpdateViewState();
    }

    private void UpdateViewState()
    {
        var view = View;
        if (view == null) return;

        var hasMessages = _chatAdapter.MessageCount > 0;
        var notConfiguredLayout = view.FindViewById<View>(Resource.Id.layout_not_configured)!;

        if (hasMessages)
        {
            _chatMessages.Visibility = ViewStates.Visible;
            notConfiguredLayout.Visibility = ViewStates.Gone;
        }
        else
        {
            _chatMessages.Visibility = ViewStates.Gone;
            notConfiguredLayout.Visibility = ViewStates.Visible;
        }
    }

    private void HideInputLayout()
    {
        _inputLayout.Visibility = ViewStates.Gone;
        var imm = Context?.GetSystemService(Android.Content.Context.InputMethodService) as Android.Views.InputMethods.InputMethodManager;
        imm?.HideSoftInputFromWindow(_chatInput.WindowToken, 0);
    }

    private void ShowInputLayout()
    {
        _inputLayout.Visibility = ViewStates.Visible;
    }

    private void ShowInputLayoutForKey()
    {
        _inputLayout.Visibility = ViewStates.Visible;
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
        UpdateViewState();

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
        UpdateViewState();
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
                        _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = "正在使用工具...", ToolCalls = msg.ToolCalls });
                        ScrollToBottom();
                    }
                    else if (msg.Role == "tool")
                    {
                        if (msg.Songs != null && msg.Songs.Count > 0)
                        {
                            _lastSearchResults = msg.Songs;
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
                _chatAdapter.UpdateLastMessage(new ExploreMessage { Role = "assistant", Content = response.Content, ToolCalls = response.ToolCalls });
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
                _navigationService.PushFragment("NowPlaying");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Explore] 播放失败: {ex}");
        }
    }

    private void ScrollToBottom()
    {
        if (_chatAdapter.MessageCount > 0)
            _chatMessages.SmoothScrollToPosition(_chatAdapter.MessageCount - 1);
    }
}
