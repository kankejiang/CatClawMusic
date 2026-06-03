using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Services.AI;

namespace CatClawMusic.UI.Adapters;

public class ModelAdapter : RecyclerView.Adapter
{
    private readonly List<LlmConfig> _models = new();
    public event EventHandler<LlmConfig>? OnEdit;
    public event EventHandler<LlmConfig>? OnDelete;
    public event EventHandler<LlmConfig>? OnToggleEnabled;

    public void SetModels(List<LlmConfig> models)
    {
        _models.Clear();
        _models.AddRange(models);
        NotifyDataSetChanged();
    }

    public override int ItemCount => _models.Count;

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is ModelViewHolder vh)
        {
            vh.Bind(_models[position]);
        }
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!
            .Inflate(Resource.Layout.item_model, parent, false)!;
        return new ModelViewHolder(view, model => OnEdit?.Invoke(this, model),
            model => OnDelete?.Invoke(this, model),
            model => OnToggleEnabled?.Invoke(this, model));
    }

    private class ModelViewHolder : RecyclerView.ViewHolder
    {
        private readonly TextView _tvName;
        private readonly TextView _tvProviderModel;
        private readonly TextView _tvApiUrl;
        private readonly Switch _switchEnabled;
        private readonly ImageButton _btnMenu;
        private LlmConfig? _currentModel;

        public ModelViewHolder(View itemView, Action<LlmConfig> onEdit, Action<LlmConfig> onDelete,
            Action<LlmConfig> onToggleEnabled) : base(itemView)
        {
            _tvName = itemView.FindViewById<TextView>(Resource.Id.tv_name)!;
            _tvProviderModel = itemView.FindViewById<TextView>(Resource.Id.tv_provider_model)!;
            _tvApiUrl = itemView.FindViewById<TextView>(Resource.Id.tv_api_url)!;
            _switchEnabled = itemView.FindViewById<Switch>(Resource.Id.switch_enabled)!;
            _btnMenu = itemView.FindViewById<ImageButton>(Resource.Id.btn_menu)!;

            _switchEnabled.CheckedChange += (s, e) =>
            {
                if (_currentModel != null)
                {
                    _currentModel.Enabled = e.IsChecked;
                    onToggleEnabled(_currentModel);
                }
            };

            _btnMenu.Click += (s, e) => ShowMenu(itemView.Context!, onEdit, onDelete);
            itemView.Click += (s, e) =>
            {
                if (_currentModel != null)
                    onEdit(_currentModel);
            };
        }

        public void Bind(LlmConfig model)
        {
            _currentModel = model;
            _tvName.Text = model.Name;
            var providerName = GetProviderName(model.Provider);
            _tvProviderModel.Text = $"{providerName} · {model.Model}";
            _tvApiUrl.Text = model.ApiUrl;
            _switchEnabled.Checked = model.Enabled;
        }

        private string GetProviderName(string providerId)
        {
            var providers = AgentService.GetProviders();
            var provider = providers.FirstOrDefault(p => p.Id == providerId);
            return provider?.Name ?? providerId;
        }

        private void ShowMenu(Android.Content.Context context, Action<LlmConfig> onEdit, Action<LlmConfig> onDelete)
        {
            if (_currentModel == null) return;
            
            var popup = new PopupMenu(context, _btnMenu);
            popup.Menu!.Add("编辑");
            popup.Menu.Add("删除");
            popup.MenuItemClick += (s, e) =>
            {
                if (e.Item!.TitleFormatted!.ToString() == "编辑")
                    onEdit(_currentModel);
                else if (e.Item.TitleFormatted.ToString() == "删除")
                    onDelete(_currentModel);
            };
            popup.Show();
        }
    }
}
