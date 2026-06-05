using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Services.AI;
using Microsoft.Extensions.DependencyInjection;
using INavigationService = CatClawMusic.Core.Interfaces.INavigationService;

namespace CatClawMusic.UI.Fragments;

public class AiSettingsFragment : Fragment
{
    private Switch? _switchEnabled;
    private TextView? _tvCurrentModel;
    private TextView? _tvModelCount;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
        => inflater.Inflate(Resource.Layout.fragment_ai_settings, container, false)!;

    public override void OnViewCreated(View view, Bundle? savedInstanceState)
    {
        base.OnViewCreated(view, savedInstanceState);

        view.SetPadding(view.PaddingLeft, view.PaddingTop + MainActivity.StatusBarHeight, view.PaddingRight, view.PaddingBottom);

        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        _switchEnabled = view.FindViewById<Switch>(Resource.Id.switch_ai_enabled);
        _tvCurrentModel = view.FindViewById<TextView>(Resource.Id.tv_current_model);
        _tvModelCount = view.FindViewById<TextView>(Resource.Id.tv_model_count);

        // 点击当前模型卡片 -> 模型管理
        var cardCurrentModel = view.FindViewById<View>(Resource.Id.card_current_model);
        if (cardCurrentModel != null)
            cardCurrentModel.Click += (s, e) => nav.PushFragment("ModelManager");

        // 点击添加模型
        var cardAddModel = view.FindViewById<View>(Resource.Id.card_add_model);
        if (cardAddModel != null)
            cardAddModel.Click += (s, e) => nav.PushFragment("ModelEdit");

        // 艺术家元数据匹配
        var cardArtistMatch = view.FindViewById<View>(Resource.Id.card_artist_match);
        if (cardArtistMatch != null)
            cardArtistMatch.Click += (s, e) => nav.PushFragment("ArtistMatch");

        LoadConfig();

        _switchEnabled?.CheckedChange += (s, e) =>
        {
            var config = AgentService.LoadConfig();
            config.Enabled = e.IsChecked;
            AgentService.SaveConfig(config);
        };
    }

    public override void OnResume()
    {
        base.OnResume();
        LoadConfig();
    }

    private void LoadConfig()
    {
        var config = AgentService.LoadConfig();
        _switchEnabled?.Post(() => { if (_switchEnabled != null) _switchEnabled.Checked = config.Enabled; });

        var currentName = AgentService.GetCurrentConfigName();
        _tvCurrentModel?.Post(() => { if (_tvCurrentModel != null) _tvCurrentModel.Text = currentName; });

        var allConfigs = AgentService.LoadAllConfigs();
        _tvModelCount?.Post(() => { if (_tvModelCount != null) _tvModelCount.Text = $"{allConfigs.Count} 个模型"; });
    }
}
