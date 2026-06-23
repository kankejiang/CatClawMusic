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
    public event EventHandler<LlmConfig>? OnToggleFallback;
    public event EventHandler? OnOrderChanged;

    public void SetModels(List<LlmConfig> models)
    {
        _models.Clear();
        _models.AddRange(models);
        NotifyDataSetChanged();
    }

    public List<LlmConfig> GetModels() => _models.ToList();

    public void MoveItem(int fromPosition, int toPosition)
    {
        if (fromPosition < 0 || fromPosition >= _models.Count || toPosition < 0 || toPosition >= _models.Count)
            return;
        var item = _models[fromPosition];
        _models.RemoveAt(fromPosition);
        _models.Insert(toPosition, item);
        NotifyItemMoved(fromPosition, toPosition);
    }

    public override int ItemCount => _models.Count;

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        if (holder is ModelViewHolder vh)
        {
            if (position >= _models.Count) return;
            vh.Bind(_models[position]);
        }
    }

    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = LayoutInflater.From(parent.Context)!
            .Inflate(Resource.Layout.item_model, parent, false)!;
        return new ModelViewHolder(view, model => OnEdit?.Invoke(this, model),
            model => OnDelete?.Invoke(this, model),
            model => OnToggleEnabled?.Invoke(this, model),
            model => OnToggleFallback?.Invoke(this, model));
    }

    /// <summary>拖拽排序回调</summary>
    public class DragCallback : ItemTouchHelper.SimpleCallback
    {
        private readonly ModelAdapter _adapter;

        public DragCallback(ModelAdapter adapter) : base(ItemTouchHelper.Up | ItemTouchHelper.Down, 0)
        {
            _adapter = adapter;
        }

        public override bool OnMove(RecyclerView? recyclerView, RecyclerView.ViewHolder? viewHolder, RecyclerView.ViewHolder? target)
        {
            if (viewHolder == null || target == null) return false;
            var from = viewHolder.AdapterPosition;
            var to = target.AdapterPosition;
            if (from == -1 || to == -1) return false;

            _adapter.MoveItem(from, to);
            return true;
        }

        public override void OnSwiped(RecyclerView.ViewHolder? viewHolder, int direction) { }

        public override bool IsLongPressDragEnabled => true;

        public override void ClearView(RecyclerView recyclerView, RecyclerView.ViewHolder viewHolder)
        {
            base.ClearView(recyclerView, viewHolder);
            _adapter.OnOrderChanged?.Invoke(_adapter, EventArgs.Empty);
        }
    }

    private class ModelViewHolder : RecyclerView.ViewHolder
    {
        private readonly TextView _tvName;
        private readonly TextView _tvProviderModel;
        private readonly TextView _tvApiUrl;
        private readonly Switch _switchEnabled;
        private readonly CheckBox _cbFallback;
        private readonly ImageButton _btnMenu;
        private LlmConfig? _currentModel;

        public ModelViewHolder(View itemView, Action<LlmConfig> onEdit, Action<LlmConfig> onDelete,
            Action<LlmConfig> onToggleEnabled, Action<LlmConfig> onToggleFallback) : base(itemView)
        {
            _tvName = itemView.FindViewById<TextView>(Resource.Id.tv_name)!;
            _tvProviderModel = itemView.FindViewById<TextView>(Resource.Id.tv_provider_model)!;
            _tvApiUrl = itemView.FindViewById<TextView>(Resource.Id.tv_api_url)!;
            _switchEnabled = itemView.FindViewById<Switch>(Resource.Id.switch_enabled)!;
            _cbFallback = itemView.FindViewById<CheckBox>(Resource.Id.cb_fallback)!;
            _btnMenu = itemView.FindViewById<ImageButton>(Resource.Id.btn_menu)!;

            _switchEnabled.CheckedChange += (s, e) =>
            {
                if (_currentModel != null)
                {
                    _currentModel.Enabled = e.IsChecked;
                    onToggleEnabled(_currentModel);
                }
            };

            _cbFallback.CheckedChange += (s, e) =>
            {
                if (_currentModel != null)
                {
                    _currentModel.FallbackEnabled = e.IsChecked;
                    onToggleFallback(_currentModel);
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
            _cbFallback.Checked = model.FallbackEnabled;
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