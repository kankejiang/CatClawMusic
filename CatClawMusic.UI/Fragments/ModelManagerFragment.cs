using Android.OS;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.Helpers;
using CatClawMusic.Core.Services.AI;
using Microsoft.Extensions.DependencyInjection;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;

namespace CatClawMusic.UI.Fragments;

public class ModelManagerFragment : Fragment
{
    private RecyclerView? _rvModels;
    private View? _llEmpty;
    private ModelAdapter? _adapter;
    private List<LlmConfig> _allConfigs = new();

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
        => inflater.Inflate(Resource.Layout.fragment_model_manager, container, false)!;

    public override void OnViewCreated(View view, Bundle? savedInstanceState)
    {
        base.OnViewCreated(view, savedInstanceState);

        view.SetPadding(view.PaddingLeft, view.PaddingTop + MainActivity.StatusBarHeight, view.PaddingRight, view.PaddingBottom);

        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        var btnAdd = view.FindViewById<ImageButton>(Resource.Id.btn_add_model);
        if (btnAdd != null)
            btnAdd.Click += (s, e) => nav.PushFragment("ModelEdit", null);

        _rvModels = view.FindViewById<RecyclerView>(Resource.Id.rv_models)!;
        _llEmpty = view.FindViewById<View>(Resource.Id.ll_empty)!;
        
        _rvModels.SetLayoutManager(new LinearLayoutManager(Context));
        _adapter = new ModelAdapter();
        _adapter.OnEdit += (s, model) => nav.PushFragment("ModelEdit", new Dictionary<string, object> { { "model", model } });
        _adapter.OnDelete += async (s, model) => await DeleteModelAsync(model);
        _adapter.OnToggleEnabled += (s, model) => ToggleModelEnabled(model);
        _adapter.OnToggleFallback += (s, model) => ToggleModelFallback(model);
        _adapter.OnOrderChanged += (s, e) => SaveModelOrder();
        _rvModels.SetAdapter(_adapter);

        // 长按拖拽排序
        var callback = new ModelAdapter.DragCallback(_adapter);
        var touchHelper = new ItemTouchHelper(callback);
        touchHelper.AttachToRecyclerView(_rvModels);

        RefreshModels();
    }

    private void RefreshModels()
    {
        _allConfigs = AgentService.LoadAllConfigs();
        if (_allConfigs.Count == 0)
        {
            var defaultConfig = AgentService.LoadConfig();
            _allConfigs.Add(defaultConfig);
            AgentService.SaveAllConfigs(_allConfigs);
        }
        
        _adapter?.SetModels(_allConfigs);
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (_allConfigs.Count == 0)
        {
            _rvModels?.Post(() =>
            {
                if (_rvModels != null) _rvModels.Visibility = ViewStates.Gone;
                if (_llEmpty != null) _llEmpty.Visibility = ViewStates.Visible;
            });
        }
        else
        {
            _rvModels?.Post(() =>
            {
                if (_rvModels != null) _rvModels.Visibility = ViewStates.Visible;
                if (_llEmpty != null) _llEmpty.Visibility = ViewStates.Gone;
            });
        }
    }

    private void ToggleModelEnabled(LlmConfig model)
    {
        AgentService.SaveConfig(model);
    }

    private void ToggleModelFallback(LlmConfig model)
    {
        AgentService.SaveConfig(model);
    }

    private void SaveModelOrder()
    {
        var orderedModels = _adapter?.GetModels();
        if (orderedModels != null)
        {
            AgentService.SaveAllConfigs(orderedModels);
        }
    }

    private async System.Threading.Tasks.Task DeleteModelAsync(LlmConfig model)
    {
        // 显示确认对话框（毛玻璃风格）
        new GlassDialog(Context!)
            .SetTitle("删除模型")
            .AddMessage($"确定要删除模型 \"{model.Name}\" 吗？")
            .AddItem("🗑  确认删除", () =>
            {
                AgentService.DeleteConfig(model.Name);
                RefreshModels();

                // 如果删除的是当前配置，尝试切换到其他配置
                var currentName = AgentService.GetCurrentConfigName();
                _allConfigs = AgentService.LoadAllConfigs();
                if (_allConfigs.Count > 0 && currentName == model.Name)
                {
                    AgentService.SetCurrentConfigName(_allConfigs[0].Name);
                }
            })
            .AddNegativeButton("取消")
            .Show();
    }

    public override void OnResume()
    {
        base.OnResume();
        RefreshModels();
    }
}
