using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Services.AI;

namespace CatClawMusic.UI.Adapters;

public class ConfigEntryAdapter : RecyclerView.Adapter
{
    private List<LlmConfigEntry> _entries = new();

    public event Action<string>? OnEntryClick;
    public event Action<string, bool>? OnEntryEnabledChanged;
    public event Action<string>? OnEntryDelete;

    public void SetEntries(List<LlmConfigEntry> entries)
    {
        _entries = entries;
        NotifyDataSetChanged();
    }

    public override int ItemCount => _entries.Count;

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is not ConfigEntryViewHolder vh) return;
        var entry = _entries[position];

        vh.Name.Text = entry.DisplayName;
        vh.Provider.Text = GetProviderName(entry.Provider);
        vh.Model.Text = $"{entry.Model}  |  {entry.ApiUrl}";
        vh.Active.Visibility = entry.IsActive ? ViewStates.Visible : ViewStates.Gone;
        vh.EnabledSwitch.SetOnCheckedChangeListener(null);
        vh.EnabledSwitch.Checked = entry.Enabled;
        vh.EnabledSwitch.SetOnCheckedChangeListener(new CheckedChangeListener((_, isChecked) =>
        {
            OnEntryEnabledChanged?.Invoke(entry.Id, isChecked);
        }));

        vh.Delete.Click += (s, e) => OnEntryDelete?.Invoke(entry.Id);
        vh.ItemView.Click += (s, e) => OnEntryClick?.Invoke(entry.Id);
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!
            .Inflate(Resource.Layout.item_config_entry, parent, false)!;
        return new ConfigEntryViewHolder(view);
    }

    private static string GetProviderName(string providerId)
    {
        var providers = AgentService.GetProviders();
        var provider = Array.Find(providers, p => p.Id == providerId);
        return provider?.Name ?? providerId;
    }

    private class ConfigEntryViewHolder : RecyclerView.ViewHolder
    {
        public TextView Name { get; }
        public TextView Provider { get; }
        public TextView Model { get; }
        public TextView Active { get; }
        public Switch EnabledSwitch { get; }
        public ImageButton Delete { get; }

        public ConfigEntryViewHolder(View view) : base(view)
        {
            Name = view.FindViewById<TextView>(Resource.Id.tv_entry_name)!;
            Provider = view.FindViewById<TextView>(Resource.Id.tv_entry_provider)!;
            Model = view.FindViewById<TextView>(Resource.Id.tv_entry_model)!;
            Active = view.FindViewById<TextView>(Resource.Id.tv_entry_active)!;
            EnabledSwitch = view.FindViewById<Switch>(Resource.Id.switch_entry_enabled)!;
            Delete = view.FindViewById<ImageButton>(Resource.Id.btn_entry_delete)!;
        }
    }

    private class CheckedChangeListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly Action<CompoundButton, bool> _callback;
        public CheckedChangeListener(Action<CompoundButton, bool> callback) => _callback = callback;
        public void OnCheckedChanged(CompoundButton? buttonView, bool isChecked)
        {
            if (buttonView != null) _callback(buttonView, isChecked);
        }
    }
}
