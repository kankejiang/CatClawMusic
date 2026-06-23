using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Adapters;

public class ChatMessageAdapter : RecyclerView.Adapter
{
    private readonly List<ChatMessage> _messages = new();

    public void AddMessage(ChatMessage message)
    {
        _messages.Add(message);
        NotifyItemInserted(_messages.Count - 1);
    }

    public void UpdateLastMessage(ChatMessage message)
    {
        if (_messages.Count > 0)
        {
            _messages[_messages.Count - 1] = message;
            NotifyItemChanged(_messages.Count - 1);
        }
    }

    public void Clear()
    {
        _messages.Clear();
        NotifyDataSetChanged();
    }

    public int MessageCount => _messages.Count;

    public override int ItemCount => _messages.Count;

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is ChatViewHolder vh)
        {
            if (position >= _messages.Count) return;
            vh.Bind(_messages[position]);
        }
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!.Inflate(Resource.Layout.item_chat_message, parent, false)!;
        return new ChatViewHolder(view);
    }
}

public class ChatViewHolder : RecyclerView.ViewHolder
{
    private readonly View _layoutUser;
    private readonly View _layoutAssistant;
    private readonly View _layoutTool;
    private readonly TextView _tvUserMessage;
    private readonly TextView _tvAssistantMessage;
    private readonly LinearLayout _layoutToolCalls;
    private readonly TextView _tvToolInfo;

    public ChatViewHolder(View view) : base(view)
    {
        _layoutUser = view.FindViewById<View>(Resource.Id.layout_user)!;
        _layoutAssistant = view.FindViewById<View>(Resource.Id.layout_assistant)!;
        _layoutTool = view.FindViewById<View>(Resource.Id.layout_tool)!;
        _tvUserMessage = view.FindViewById<TextView>(Resource.Id.tv_user_message)!;
        _tvAssistantMessage = view.FindViewById<TextView>(Resource.Id.tv_assistant_message)!;
        _layoutToolCalls = view.FindViewById<LinearLayout>(Resource.Id.layout_tool_calls)!;
        _tvToolInfo = view.FindViewById<TextView>(Resource.Id.tv_tool_info)!;
    }

    public void Bind(ChatMessage message)
    {
        _layoutUser.Visibility = ViewStates.Gone;
        _layoutAssistant.Visibility = ViewStates.Gone;
        _layoutTool.Visibility = ViewStates.Gone;

        if (message.Role == "user")
        {
            _layoutUser.Visibility = ViewStates.Visible;
            _tvUserMessage.Text = message.Content;
        }
        else if (message.Role == "assistant")
        {
            _layoutAssistant.Visibility = ViewStates.Visible;
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
            var toolName = message.Name ?? "工具";
            _tvToolInfo.Text = $"{toolName} 已执行";
        }
    }
}
