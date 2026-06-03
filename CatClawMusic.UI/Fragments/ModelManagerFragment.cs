using Android.OS;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.UI.Adapters;
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
        _rvModels.SetAdapter(_adapter);

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
        // 保存修改
        AgentService.SaveConfig(model);
    }

    private async System.Threading.Tasks.Task DeleteModelAsync(LlmConfig model)
    {
        // 显示确认对话框
        var dialog = new AndroidX.AppCompat.App.AlertDialog.Builder(Context!)
            .SetTitle("删除模型")
            .SetMessage($"确定要删除模型 \"{model.Name}\" 吗？")
            .SetPositiveButton("删除", async (s, e) =>
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
            .SetNegativeButton("取消", (s, e) => { })
            .Create();
        dialog?.Show();
    }

    public override void OnResume()
    {
        base.OnResume();
        RefreshModels();
    }
}
