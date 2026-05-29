using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.Services.AI;
using IAgentService = CatClawMusic.UI.Services.AI.IAgentService;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class SearchFragment : Fragment
{
    private IAgentService _agentService = null!;
    private INavigationService _navigationService = null!;
    private RecyclerView _chatMessages = null!;
    private EditText _chatInput = null!;
    private ImageButton _btnSend = null!;
    private ChatMessageAdapter _adapter = null!;
    private View _layoutNotConfigured = null!;
    private View _layoutInput = null!;
    private bool _isSending;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_search, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);

        _agentService = MainApplication.Services.GetRequiredService<IAgentService>();
        _navigationService = MainApplication.Services.GetRequiredService<INavigationService>();

        _chatMessages = view.FindViewById<RecyclerView>(Resource.Id.chat_messages)!;
        _chatInput = view.FindViewById<EditText>(Resource.Id.et_chat_input)!;
        _btnSend = view.FindViewById<ImageButton>(Resource.Id.btn_send)!;
        _layoutNotConfigured = view.FindViewById<View>(Resource.Id.layout_not_configured)!;
        _layoutInput = view.FindViewById<View>(Resource.Id.layout_input)!;

        _adapter = new ChatMessageAdapter();
        _chatMessages.SetLayoutManager(new LinearLayoutManager(Context));
        _chatMessages.SetAdapter(_adapter);

        _btnSend.Click += (s, e) => SendMessage();
        _chatInput.EditorAction += (s, e) =>
        {
            if (e.ActionId == Android.Views.InputMethods.ImeAction.Send)
            {
                SendMessage();
                e.Handled = true;
            }
        };

        var btnAiSettings = view.FindViewById<ImageButton>(Resource.Id.btn_ai_settings);
        if (btnAiSettings != null)
            btnAiSettings.Click += (s, e) => _navigationService.PushFragment("AiSettings");

        var btnClearChat = view.FindViewById<ImageButton>(Resource.Id.btn_clear_chat);
        if (btnClearChat != null)
            btnClearChat.Click += (s, e) =>
            {
                _agentService.ClearConversation();
                _adapter.Clear();
            };

        var btnGoSettings = view.FindViewById<View>(Resource.Id.btn_go_ai_settings);
        if (btnGoSettings != null)
            btnGoSettings.Click += (s, e) => _navigationService.PushFragment("AiSettings");

        UpdateConfiguredState();
    }

    public override void OnResume()
    {
        base.OnResume();
        UpdateConfiguredState();
    }

    private void UpdateConfiguredState()
    {
        var configured = _agentService.IsConfigured;
        _chatMessages.Visibility = configured ? ViewStates.Visible : ViewStates.Gone;
        _layoutInput.Visibility = configured ? ViewStates.Visible : ViewStates.Gone;
        _layoutNotConfigured.Visibility = configured ? ViewStates.Gone : ViewStates.Visible;
    }

    private void SendMessage()
    {
        if (_isSending) return;
        var text = _chatInput?.Text?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        _chatInput!.Text = "";
        _isSending = true;

        _adapter.AddMessage(new ChatMessage { Role = "user", Content = text });
        ScrollToBottom();

        var thinkingMsg = new ChatMessage { Role = "assistant", Content = "思考中..." };
        _adapter.AddMessage(thinkingMsg);
        ScrollToBottom();

        _ = SendMessageAsync(text);
    }

    private async Task SendMessageAsync(string userMessage)
    {
        try
        {
            var response = await _agentService.SendMessageAsync(userMessage, onPartialMessage: msg =>
            {
                Activity?.RunOnUiThread(() =>
                {
                    if (msg.Role == "assistant" && msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        _adapter.UpdateLastMessage(new ChatMessage
                        {
                            Role = "assistant",
                            Content = "正在使用工具...",
                            ToolCalls = msg.ToolCalls
                        });
                        ScrollToBottom();
                    }
                    else if (msg.Role == "tool")
                    {
                        _adapter.AddMessage(msg);
                        _adapter.AddMessage(new ChatMessage { Role = "assistant", Content = "继续思考..." });
                        ScrollToBottom();
                    }
                });
            });

            Activity?.RunOnUiThread(() =>
            {
                _adapter.UpdateLastMessage(response);
                ScrollToBottom();
            });
        }
        catch (Exception ex)
        {
            Activity?.RunOnUiThread(() =>
            {
                _adapter.UpdateLastMessage(new ChatMessage
                {
                    Role = "assistant",
                    Content = $"出错了: {ex.Message}"
                });
                ScrollToBottom();
            });
        }
        finally
        {
            _isSending = false;
        }
    }

    private void ScrollToBottom()
    {
        if (_adapter.MessageCount > 0)
            _chatMessages.SmoothScrollToPosition(_adapter.MessageCount - 1);
    }
}
