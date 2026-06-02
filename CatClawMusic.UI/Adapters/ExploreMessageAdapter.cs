using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.Services.AI;

namespace CatClawMusic.UI.Adapters;

public class ExploreMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public List<ToolCall>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public string? Name { get; set; }
    public List<Song>? Songs { get; set; }
    public string? SearchKeyword { get; set; }
    public int WizardStep { get; set; }
    public LlmProviderInfo[]? WizardProviders { get; set; }
    public List<string>? WizardModels { get; set; }
    public bool WizardCompleted { get; set; }
    public string? WizardExtra { get; set; }
    public string[]? PresetModels { get; set; }

    public bool IsSearchResults => Songs != null && Songs.Count > 0;
    public bool IsWizard => Role == "wizard";
}

public class ExploreMessageAdapter : RecyclerView.Adapter
{
    private readonly List<ExploreMessage> _messages = new();
    private BuiltinAgent _currentAgent = BuiltinAgent.Yuki;
    public event EventHandler<Song>? OnSongPlay;
    public event EventHandler<int>? OnWizardCancel;
    public event EventHandler<int>? OnWizardNext;

    public BuiltinAgent CurrentAgent
    {
        get => _currentAgent;
        set => _currentAgent = value;
    }

    public void AddMessage(ExploreMessage message)
    {
        _messages.Add(message);
        NotifyItemInserted(_messages.Count - 1);
    }

    public void UpdateLastMessage(ExploreMessage message)
    {
        if (_messages.Count > 0)
        {
            _messages[_messages.Count - 1] = message;
            NotifyItemChanged(_messages.Count - 1);
        }
    }

    public ExploreMessage? GetLastMessage()
    {
        return _messages.Count > 0 ? _messages[_messages.Count - 1] : null;
    }

    public void Clear()
    {
        _messages.Clear();
        NotifyDataSetChanged();
    }

    public int MessageCount => _messages.Count;

    public ExploreMessage? GetMessageAtInternal(int position)
    {
        return position >= 0 && position < _messages.Count ? _messages[position] : null;
    }

    public override int ItemCount => _messages.Count;

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is ExploreViewHolder vh)
            vh.Bind(_messages[position], _currentAgent);
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_chat_message, parent, false)!;
        return new ExploreViewHolder(view,
            song => OnSongPlay?.Invoke(this, song),
            step => OnWizardCancel?.Invoke(this, step),
            step => OnWizardNext?.Invoke(this, step));
    }
}

public class ExploreViewHolder : RecyclerView.ViewHolder
{
    private readonly View _layoutUser;
    private readonly View _layoutAssistant;
    private readonly View _layoutTool;
    private readonly View _layoutSearchResults;
    private readonly View _layoutWizard;
    private readonly TextView _tvUserMessage;
    private readonly TextView _tvAssistantMessage;
    private readonly LinearLayout _layoutToolCalls;
    private readonly TextView _tvToolInfo;
    private readonly TextView _tvSearchSummary;
    private readonly RecyclerView _rvSongCards;
    private readonly Action<Song> _onSongPlay;
    private readonly ImageView _ivAgentAvatar;
    private readonly ImageView _ivAgentAvatarTool;
    private readonly ImageView _ivAgentAvatarSearch;

    private readonly TextView _tvWizardTitle;
    private readonly RadioGroup _rgProviders;
    private readonly View _layoutApikeyInput;
    private readonly EditText _etWizardApikey;
    private readonly View _layoutModelSelect;
    private readonly Spinner _spinnerWizardModels;
    private readonly TextView _tvWizardModelStatus;
    private readonly Button _btnWizardCancel;
    private readonly Button _btnWizardNext;

    private readonly Action<int> _onWizardCancel;
    private readonly Action<int> _onWizardNext;

    public ExploreViewHolder(View view, Action<Song> onSongPlay, Action<int> onWizardCancel, Action<int> onWizardNext) : base(view)
    {
        _onSongPlay = onSongPlay;
        _onWizardCancel = onWizardCancel;
        _onWizardNext = onWizardNext;

        _layoutUser = view.FindViewById<View>(Resource.Id.layout_user)!;
        _layoutAssistant = view.FindViewById<View>(Resource.Id.layout_assistant)!;
        _layoutTool = view.FindViewById<View>(Resource.Id.layout_tool)!;
        _layoutSearchResults = view.FindViewById<View>(Resource.Id.layout_search_results)!;
        _layoutWizard = view.FindViewById<View>(Resource.Id.layout_wizard)!;
        _tvUserMessage = view.FindViewById<TextView>(Resource.Id.tv_user_message)!;
        _tvAssistantMessage = view.FindViewById<TextView>(Resource.Id.tv_assistant_message)!;
        _layoutToolCalls = view.FindViewById<LinearLayout>(Resource.Id.layout_tool_calls)!;
        _tvToolInfo = view.FindViewById<TextView>(Resource.Id.tv_tool_info)!;
        _tvSearchSummary = view.FindViewById<TextView>(Resource.Id.tv_search_summary)!;
        _rvSongCards = view.FindViewById<RecyclerView>(Resource.Id.rv_song_cards)!;

        _ivAgentAvatar = view.FindViewById<ImageView>(Resource.Id.iv_agent_avatar)!;
        _ivAgentAvatarTool = view.FindViewById<ImageView>(Resource.Id.iv_agent_avatar_tool)!;
        _ivAgentAvatarSearch = view.FindViewById<ImageView>(Resource.Id.iv_agent_avatar_search)!;

        _tvWizardTitle = view.FindViewById<TextView>(Resource.Id.tv_wizard_title)!;
        _rgProviders = view.FindViewById<RadioGroup>(Resource.Id.rg_providers)!;
        _layoutApikeyInput = view.FindViewById<View>(Resource.Id.layout_apikey_input)!;
        _etWizardApikey = view.FindViewById<EditText>(Resource.Id.et_wizard_apikey)!;
        _layoutModelSelect = view.FindViewById<View>(Resource.Id.layout_model_select)!;
        _spinnerWizardModels = view.FindViewById<Spinner>(Resource.Id.spinner_wizard_models)!;
        _tvWizardModelStatus = view.FindViewById<TextView>(Resource.Id.tv_wizard_model_status)!;
        _btnWizardCancel = view.FindViewById<Button>(Resource.Id.btn_wizard_cancel)!;
        _btnWizardNext = view.FindViewById<Button>(Resource.Id.btn_wizard_next)!;
    }

    public void Bind(ExploreMessage message, BuiltinAgent agent)
    {
        _layoutUser.Visibility = ViewStates.Gone;
        _layoutAssistant.Visibility = ViewStates.Gone;
        _layoutTool.Visibility = ViewStates.Gone;
        _layoutSearchResults.Visibility = ViewStates.Gone;
        _layoutWizard.Visibility = ViewStates.Gone;
        _ivAgentAvatar.Visibility = ViewStates.Gone;
        _ivAgentAvatarTool.Visibility = ViewStates.Gone;
        _ivAgentAvatarSearch.Visibility = ViewStates.Gone;

        if (message.IsWizard && !message.WizardCompleted)
        {
            BindWizard(message);
        }
        else if (message.IsSearchResults)
        {
            _layoutSearchResults.Visibility = ViewStates.Visible;
            SetAvatar(_ivAgentAvatarSearch, agent);
            var keyword = message.SearchKeyword ?? "";
            _tvSearchSummary.Text = string.IsNullOrEmpty(keyword)
                ? $"找到 {message.Songs!.Count} 首相关歌曲"
                : $"「{keyword}」找到 {message.Songs!.Count} 首相关歌曲";

            var maxSongs = Math.Min(message.Songs!.Count, 10);
            var displaySongs = message.Songs!.Take(maxSongs).ToList();

            _rvSongCards.SetLayoutManager(new LinearLayoutManager(ItemView.Context));
            var adapter = new SongCardAdapter(displaySongs);
            adapter.OnSongPlay += (s, song) => _onSongPlay(song);
            _rvSongCards.SetAdapter(adapter);
        }
        else if (message.Role == "user")
        {
            _layoutUser.Visibility = ViewStates.Visible;
            _tvUserMessage.Text = message.Content;
        }
        else if (message.Role == "assistant")
        {
            _layoutAssistant.Visibility = ViewStates.Visible;
            SetAvatar(_ivAgentAvatar, agent);
            _tvAssistantMessage.Text = message.Content ?? "";

            if (message.ToolCalls != null && message.ToolCalls.Count > 0)
            {
                _layoutToolCalls.Visibility = ViewStates.Visible;
                _layoutToolCalls.RemoveAllViews();
                foreach (var tc in message.ToolCalls)
                {
                    var tv = new TextView(ItemView.Context!)
                    {
                        Text = $"🔧 调用工具: {tc.Function.Name}",
                        TextSize = 11
                    };
                    tv.SetTextColor(Android.Content.Res.ColorStateList.ValueOf(
                        Android.Graphics.Color.ParseColor("#888888")));
                    tv.SetPadding(0, 4, 0, 2);
                    _layoutToolCalls.AddView(tv);
                }
            }
            else
            {
                _layoutToolCalls.Visibility = ViewStates.Gone;
            }
        }
        else if (message.Role == "tool")
        {
            _layoutTool.Visibility = ViewStates.Visible;
            SetAvatar(_ivAgentAvatarTool, agent);
            var toolName = message.Name ?? "工具";
            _tvToolInfo.Text = $"{toolName} 已执行";
        }
    }

    private void SetAvatar(ImageView imageView, BuiltinAgent agent)
    {
        if (!string.IsNullOrEmpty(agent.AvatarDrawableName))
        {
            var ctx = ItemView.Context!;
            var resId = ctx.Resources?.GetIdentifier(agent.AvatarDrawableName, "drawable", ctx.PackageName) ?? 0;
            if (resId != 0)
            {
                imageView.SetImageResource(resId);
                imageView.Visibility = ViewStates.Visible;
                return;
            }
        }
        imageView.Visibility = ViewStates.Gone;
    }

    private void BindWizard(ExploreMessage message)
    {
        _layoutWizard.Visibility = ViewStates.Visible;
        _rgProviders.Visibility = ViewStates.Gone;
        _layoutApikeyInput.Visibility = ViewStates.Gone;
        _layoutModelSelect.Visibility = ViewStates.Gone;

        var step = message.WizardStep;

        if (step == 1)
        {
            _tvWizardTitle.Text = "选择服务商";
            _rgProviders.Visibility = ViewStates.Visible;
            _btnWizardNext.Text = "下一步";

            _rgProviders.RemoveAllViews();
            if (message.WizardProviders != null)
            {
                for (int i = 0; i < message.WizardProviders.Length; i++)
                {
                    var rb = new RadioButton(ItemView.Context!) { Text = message.WizardProviders[i].Name, Tag = message.WizardProviders[i].Id };
                    rb.SetPadding(0, 8, 0, 8);
                    rb.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
                    _rgProviders.AddView(rb);
                    if (i == 0) rb.Checked = true;
                }
            }
        }
        else if (step == 2)
        {
            _tvWizardTitle.Text = "输入 API Key";
            _layoutApikeyInput.Visibility = ViewStates.Visible;
            _btnWizardNext.Text = "下一步";
            _etWizardApikey.Text = message.Content ?? "";
        }
        else if (step == 3)
        {
            _tvWizardTitle.Text = "选择模型";
            _layoutModelSelect.Visibility = ViewStates.Visible;
            _btnWizardNext.Text = "完成";

            if (message.WizardModels != null && message.WizardModels.Count > 0)
            {
                var items = new List<string> { "选择模型..." };
                items.AddRange(message.WizardModels);
                var adapter = new ArrayAdapter<string>(ItemView.Context!, Android.Resource.Layout.SimpleSpinnerItem, items);
                adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
                _spinnerWizardModels.Adapter = adapter;
                _tvWizardModelStatus.Visibility = ViewStates.Gone;
            }
            else
            {
                var items = new List<string> { "暂无模型" };
                var adapter = new ArrayAdapter<string>(ItemView.Context!, Android.Resource.Layout.SimpleSpinnerItem, items);
                adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
                _spinnerWizardModels.Adapter = adapter;
                _tvWizardModelStatus.Text = "正在获取模型列表...";
                _tvWizardModelStatus.Visibility = ViewStates.Visible;
            }
        }
        else if (step == 4)
        {
            var newModelName = message.Content ?? "新模型";
            _tvWizardTitle.Text = $"是否启用「{newModelName}」？";
            _btnWizardNext.Text = "是";
            _btnWizardCancel.Text = "否";
        }

        _btnWizardCancel.Click -= OnCancelClick;
        _btnWizardNext.Click -= OnNextClick;
        _btnWizardCancel.Click += OnCancelClick;
        _btnWizardNext.Click += OnNextClick;
    }

    private void OnCancelClick(object? sender, EventArgs e)
    {
        var msg = GetBoundMessage();
        if (msg == null) return;
        if (msg.WizardStep == 4)
        {
            msg.Content = "no";
            _onWizardNext(msg.WizardStep);
        }
        else
        {
            _onWizardCancel(msg.WizardStep);
        }
    }

    private void OnNextClick(object? sender, EventArgs e)
    {
        var msg = GetBoundMessage();
        if (msg == null) return;

        if (msg.WizardStep == 1)
        {
            var checkedId = _rgProviders.CheckedRadioButtonId;
            if (checkedId < 0) return;
            var rb = _rgProviders.FindViewById<RadioButton>(checkedId);
            if (rb == null) return;
            msg.Content = rb.Tag?.ToString() ?? "";
            _onWizardNext(msg.WizardStep);
        }
        else if (msg.WizardStep == 2)
        {
            var key = _etWizardApikey.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(key)) return;
            msg.Content = key;
            _onWizardNext(msg.WizardStep);
        }
        else if (msg.WizardStep == 3)
        {
            var pos = _spinnerWizardModels.SelectedItemPosition;
            if (pos <= 0 || msg.WizardModels == null || pos > msg.WizardModels.Count) return;
            msg.Content = msg.WizardModels[pos - 1];
            _onWizardNext(msg.WizardStep);
        }
        else if (msg.WizardStep == 4)
        {
            msg.Content = "yes";
            _onWizardNext(msg.WizardStep);
        }
    }

    private ExploreMessage? GetBoundMessage()
    {
        if (BindingAdapterPosition != RecyclerView.NoPosition && BindingAdapter is ExploreMessageAdapter adapter)
        {
            var count = adapter.ItemCount;
            if (BindingAdapterPosition < count)
                return adapter.GetMessageAt(BindingAdapterPosition);
        }
        return null;
    }
}

public static class ExploreMessageAdapterExtensions
{
    public static ExploreMessage? GetMessageAt(this ExploreMessageAdapter adapter, int position)
    {
        return adapter.GetMessageAtInternal(position);
    }
}

public class SongCardAdapter : RecyclerView.Adapter
{
    private readonly List<Song> _songs;
    public event EventHandler<Song>? OnSongPlay;

    public SongCardAdapter(List<Song> songs)
    {
        _songs = songs;
    }

    public override int ItemCount => _songs.Count;

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is SongCardViewHolder vh)
            vh.Bind(_songs[position]);
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_song_card, parent, false)!;
        return new SongCardViewHolder(view, song => OnSongPlay?.Invoke(this, song));
    }
}

public class SongCardViewHolder : RecyclerView.ViewHolder
{
    private readonly TextView _tvTitle;
    private readonly TextView _tvArtist;
    private readonly ImageButton _btnPlay;
    private Song? _currentSong;

    public SongCardViewHolder(View view, Action<Song> onPlay) : base(view)
    {
        _tvTitle = view.FindViewById<TextView>(Resource.Id.tv_title)!;
        _tvArtist = view.FindViewById<TextView>(Resource.Id.tv_artist)!;
        _btnPlay = view.FindViewById<ImageButton>(Resource.Id.btn_play)!;

        _btnPlay.Click += (s, e) =>
        {
            if (_currentSong != null) onPlay(_currentSong);
        };

        view.Click += (s, e) =>
        {
            if (_currentSong != null) onPlay(_currentSong);
        };
    }

    public void Bind(Song song)
    {
        _currentSong = song;
        _tvTitle.Text = song.Title ?? "未知标题";
        _tvArtist.Text = song.Artist ?? "未知艺术家";
    }
}
