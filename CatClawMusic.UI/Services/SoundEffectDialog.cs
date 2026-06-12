using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Services;

/// <summary>
/// 音效对话框 — 预设音效选择 + MAX Audio + MISOUND
/// </summary>
public class SoundEffectDialog : Dialog
{
    private readonly IAudioPlayerService _playerService;
    private readonly SoundEffectManager _sfxManager;
    private int _audioSessionId;

    // ===== 栈式导航 =====
    private readonly Stack<string> _navStack = new();
    private readonly Dictionary<string, LinearLayout> _screens = new();
    private readonly Dictionary<string, string> _screenTitles = new();
    private TextView? _headerTitle;
    private TextView? _backBtn;

    // UI — 音效预设
    private TextView? _currentPresetLabel;
    private LinearLayout? _presetGrid;

    // UI — MAX Audio
    private Switch? _maxAudioSwitch;
    private Java.Lang.Object? _maxAudioReverb;

    private bool _isInitializing = true;

    private const string PrefsName = "catclaw_sfx_dialog";
    private const string KeyMaxAudioEnabled = "maxaudio_enabled";

    public SoundEffectDialog(Activity context, IAudioPlayerService playerService)
        : base(context, Android.Resource.Style.ThemeOverlayMaterialDark)
    {
        _playerService = playerService;
        _sfxManager = MainApplication.Services.GetRequiredService<SoundEffectManager>();
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        RequestWindowFeature((int)WindowFeatures.NoTitle);
        SetContentView(CreateView());
        Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        Window?.SetGravity(GravityFlags.Bottom);
        Window?.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        Init();
    }

    private int Dp(int dp) => (int)(dp * (Context.Resources?.DisplayMetrics?.Density ?? 1f));

    private View CreateView()
    {
        var density = Context.Resources?.DisplayMetrics?.Density ?? 1f;
        int dp(int v) => (int)(v * density);

        var root = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        root.SetBackgroundColor(Color.ParseColor("#E6222222"));

        // ===== Header =====
        var header = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        header.SetGravity(GravityFlags.CenterVertical);
        header.SetPadding(dp(16), dp(14), dp(16), dp(6));

        _backBtn = new TextView(Context)
        {
            Text = "\u2039 ",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent),
            Visibility = ViewStates.Gone,
        };
        _backBtn.SetTextColor(Color.White);
        _backBtn.TextSize = 24f;
        _backBtn.Gravity = GravityFlags.Center;
        _backBtn.SetPadding(dp(0), dp(0), dp(8), dp(0));
        _backBtn.Click += (s, e) => NavigateBack();
        header.AddView(_backBtn);

        _headerTitle = new TextView(Context)
        {
            Text = "音效",
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f)
        };
        _headerTitle.SetTextColor(Color.White);
        _headerTitle.TextSize = 20f;
        _headerTitle.SetTypeface(null, TypefaceStyle.Bold);

        var closeBtn = new TextView(Context)
        {
            Text = "\u2715",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        closeBtn.SetTextColor(Color.ParseColor("#AAFFFFFF"));
        closeBtn.TextSize = 18f;
        closeBtn.Gravity = GravityFlags.Center;
        closeBtn.SetPadding(dp(12), dp(4), dp(4), dp(4));
        closeBtn.Click += (s, e) => Dismiss();

        header.AddView(_headerTitle);
        header.AddView(closeBtn);
        root.AddView(header);

        // ===== 创建各页面 =====
        var mainScreen = CreateMainScreen(dp);
        var presetScreen = CreatePresetScreen(dp);

        _screens["main"] = mainScreen;
        _screens["presets"] = presetScreen;

        _screenTitles["main"] = "音效";
        _screenTitles["presets"] = "音效选择";

        foreach (var kv in _screens)
        {
            kv.Value.Visibility = kv.Key == "main" ? ViewStates.Visible : ViewStates.Gone;
            root.AddView(kv.Value);
        }

        return root;
    }

    #region Main Screen

    private LinearLayout CreateMainScreen(Func<int, int> dp)
    {
        var screen = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        var scroll = new ScrollView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        var content = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        content.SetPadding(0, 0, 0, dp(16));

        // 音效选择入口
        var sfxRow = new LinearLayout(Context)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent),
            Clickable = true, Focusable = true
        };
        sfxRow.SetGravity(GravityFlags.CenterVertical);
        sfxRow.SetPadding(dp(16), dp(14), dp(16), dp(14));

        var sfxIcon = new TextView(Context) { Text = "\U0001F3A7", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        sfxIcon.TextSize = 22f; sfxIcon.Gravity = GravityFlags.Center; sfxIcon.SetPadding(dp(0), dp(0), dp(12), dp(0));

        var sfxText = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) };
        var sfxTitle = new TextView(Context) { Text = "音效选择", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        sfxTitle.SetTextColor(Color.White); sfxTitle.TextSize = 15f;
        _currentPresetLabel = new TextView(Context) { Text = _sfxManager.CurrentPreset, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        _currentPresetLabel.SetTextColor(Color.ParseColor("#88FFFFFF")); _currentPresetLabel.TextSize = 12f;
        sfxText.AddView(sfxTitle); sfxText.AddView(_currentPresetLabel);

        var sfxChevron = new TextView(Context) { Text = "\u203A", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        sfxChevron.SetTextColor(Color.ParseColor("#88FFFFFF")); sfxChevron.TextSize = 22f; sfxChevron.Gravity = GravityFlags.Center;

        sfxRow.AddView(sfxIcon); sfxRow.AddView(sfxText); sfxRow.AddView(sfxChevron);
        sfxRow.Click += (s, e) => NavigateTo("presets");
        content.AddView(sfxRow);

        content.AddView(MakeDivider(dp));

        // MAX Audio
        content.AddView(MakeMaxAudioSection(dp));

        // 系统音效
        content.AddView(MakeMenuEntry("\u2669", "系统音效", "打开系统声音设置", (s, e) => OpenSystemSoundSettings(), dp));

        scroll.AddView(content);
        screen.AddView(scroll);
        return screen;
    }

    #endregion

    #region Preset Screen

    private LinearLayout CreatePresetScreen(Func<int, int> dp)
    {
        var screen = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        var scroll = new ScrollView(Context)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        var content = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        content.SetPadding(dp(8), dp(8), dp(8), dp(16));

        _presetGrid = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        };
        content.AddView(_presetGrid);

        scroll.AddView(content);
        screen.AddView(scroll);
        return screen;
    }

    private void BuildPresetGrid()
    {
        if (_presetGrid == null) return;
        _presetGrid.RemoveAllViews();

        var presets = SoundEffectManager.Presets;
        var current = _sfxManager.CurrentPreset;

        int dp(int v) => (int)(v * (Context.Resources?.DisplayMetrics?.Density ?? 1f));

        for (int row = 0; row < presets.Length; row += 2)
        {
            var rowLayout = new LinearLayout(Context)
            {
                Orientation = Orientation.Horizontal,
                LayoutParameters = new LinearLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
            };
            rowLayout.SetPadding(0, 0, 0, dp(6));

            for (int col = 0; col < 2 && row + col < presets.Length; col++)
            {
                var preset = presets[row + col];
                var card = CreatePresetCard(preset, preset.Name == current, dp);
                rowLayout.AddView(card);
            }

            _presetGrid.AddView(rowLayout);
        }
    }

    private LinearLayout CreatePresetCard(SoundEffectManager.Preset preset, bool isSelected, Func<int, int> dp)
    {
        var card = new LinearLayout(Context)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f),
            Clickable = true, Focusable = true
        };
        card.SetPadding(dp(8), dp(10), dp(8), dp(10));
        card.SetGravity(GravityFlags.CenterHorizontal);

        // 选中状态背景
        var bgColor = isSelected ? Color.ParseColor("#3344AAFF") : Color.ParseColor("#11FFFFFF");
        var bgDrawable = new GradientDrawable();
        bgDrawable.SetCornerRadius(dp(10));
        bgDrawable.SetColor(bgColor);
        if (isSelected)
        {
            bgDrawable.SetStroke(dp(1), Color.ParseColor("#44AAFF"));
        }
        card.Background = bgDrawable;

        // 图标
        var icon = new TextView(Context)
        {
            Text = preset.Icon,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        icon.TextSize = 28f;
        icon.Gravity = GravityFlags.Center;
        card.AddView(icon);

        // 名称
        var name = new TextView(Context)
        {
            Text = preset.Name,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        name.SetTextColor(isSelected ? Color.ParseColor("#44AAFF") : Color.White);
        name.TextSize = 13f;
        name.SetTypeface(null, isSelected ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        name.Gravity = GravityFlags.Center;
        name.SetPadding(0, dp(4), 0, 0);
        card.AddView(name);

        // 描述
        var desc = new TextView(Context)
        {
            Text = preset.Description,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        desc.SetTextColor(isSelected ? Color.ParseColor("#6644AAFF") : Color.ParseColor("#66FFFFFF"));
        desc.TextSize = 10f;
        desc.Gravity = GravityFlags.Center;
        desc.SetPadding(dp(4), dp(2), dp(4), 0);
        card.AddView(desc);

        card.Click += (s, e) =>
        {
            _sfxManager.ApplyPreset(preset.Name);
            _currentPresetLabel!.Text = preset.Name;
            BuildPresetGrid();
        };

        return card;
    }

    #endregion

    #region UI Helpers

    private LinearLayout MakeMenuEntry(string icon, string title, string subtitle, EventHandler onClick, Func<int, int> dp)
    {
        var row = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent), Clickable = true, Focusable = true };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(dp(16), dp(14), dp(16), dp(14));
        var iconTv = new TextView(Context) { Text = icon, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        iconTv.SetTextColor(Color.ParseColor("#88FFFFFF")); iconTv.TextSize = 18f; iconTv.Gravity = GravityFlags.Center; iconTv.SetPadding(dp(0), dp(0), dp(12), dp(0));
        var textContainer = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) };
        var titleTv = new TextView(Context) { Text = title, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        titleTv.SetTextColor(Color.White); titleTv.TextSize = 15f;
        textContainer.AddView(titleTv);
        var subTv = new TextView(Context) { Text = subtitle, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        subTv.SetTextColor(Color.ParseColor("#88FFFFFF")); subTv.TextSize = 11f;
        textContainer.AddView(subTv);
        var chevron = new TextView(Context) { Text = "\u203A", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        chevron.SetTextColor(Color.ParseColor("#88FFFFFF")); chevron.TextSize = 22f; chevron.Gravity = GravityFlags.Center;
        row.AddView(iconTv); row.AddView(textContainer); row.AddView(chevron);
        row.Click += onClick;
        return row;
    }

    private View MakeDivider(Func<int, int> dp)
    {
        var d = new View(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, dp(1)) };
        d.SetBackgroundColor(Color.ParseColor("#22FFFFFF"));
        return d;
    }

    #endregion

    #region MAX Audio Section

    private LinearLayout MakeMaxAudioSection(Func<int, int> dp)
    {
        var container = new LinearLayout(Context) { Orientation = Orientation.Vertical, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        var row = new LinearLayout(Context) { Orientation = Orientation.Horizontal, LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        row.SetGravity(GravityFlags.CenterVertical); row.SetPadding(dp(16), dp(10), dp(16), 0);
        var titleTv = new TextView(Context) { Text = "MAX Audio 音乐厅氛围", LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f) };
        titleTv.SetTextColor(Color.White); titleTv.TextSize = 15f; titleTv.SetTypeface(null, TypefaceStyle.Bold);
        _maxAudioSwitch = new Switch(Context) { LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) };
        _maxAudioSwitch.SetOnCheckedChangeListener(new MaxAudioSwitchListener(this));
        row.AddView(titleTv); row.AddView(_maxAudioSwitch);
        container.AddView(row);
        var desc = new TextView(Context) { Text = "沉浸式音乐厅混响效果，增强空间感与临场感", LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) };
        desc.SetTextColor(Color.ParseColor("#88FFFFFF")); desc.TextSize = 12f; desc.SetPadding(dp(16), dp(4), dp(16), dp(8));
        container.AddView(desc);
        return container;
    }

    #endregion

    #region Navigation

    private void NavigateTo(string screenName)
    {
        if (!_screens.TryGetValue(screenName, out var target)) return;
        foreach (var kv in _screens) kv.Value.Visibility = ViewStates.Gone;
        target.Visibility = ViewStates.Visible;
        _navStack.Push(screenName);
        _backBtn!.Visibility = ViewStates.Visible;
        _headerTitle!.Text = _screenTitles[screenName];

        if (screenName == "presets")
            BuildPresetGrid();
    }

    private void NavigateBack()
    {
        if (_navStack.Count > 0)
        {
            var current = _navStack.Pop();
            _screens[current].Visibility = ViewStates.Gone;
        }
        string target = _navStack.Count > 0 ? _navStack.Peek() : "main";
        _screens[target].Visibility = ViewStates.Visible;
        _headerTitle!.Text = _screenTitles[target];
        _backBtn!.Visibility = target == "main" ? ViewStates.Gone : ViewStates.Visible;
    }

    public override void OnBackPressed()
    {
        if (_navStack.Count > 0) NavigateBack();
        else base.OnBackPressed();
    }

    #endregion

    #region Init

    private void Init()
    {
        try
        {
            _audioSessionId = _playerService.AudioSessionId;
            if (_audioSessionId > 0)
                _sfxManager.Attach(_audioSessionId);

            _isInitializing = true;

            var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);

            // MAX Audio
            bool maxAudioEnabled = prefs.GetBoolean(KeyMaxAudioEnabled, false);
            _maxAudioSwitch!.Checked = maxAudioEnabled;
            if (maxAudioEnabled)
                ApplyMaxAudio();

            _isInitializing = false;

            _currentPresetLabel!.Text = _sfxManager.CurrentPreset;
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw.SFX", $"音效初始化失败: {ex.Message}");
        }
    }

    private void ApplyMaxAudio()
    {
        try
        {
            if (_maxAudioReverb == null)
            {
                var cls = Java.Lang.Class.ForName("android.media.audiofx.PresetReverb");
                var ctor = cls.GetConstructor(Java.Lang.Integer.Type, Java.Lang.Integer.Type);
                _maxAudioReverb = ctor.NewInstance(
                    Java.Lang.Integer.ValueOf(0),
                    Java.Lang.Integer.ValueOf(_audioSessionId));
            }

            var setEnabled = Java.Lang.Class.ForName("android.media.audiofx.PresetReverb")
                .GetMethod("setEnabled", Java.Lang.Boolean.Type);
            setEnabled.Invoke(_maxAudioReverb, Java.Lang.Boolean.True);

            var setPreset = Java.Lang.Class.ForName("android.media.audiofx.PresetReverb")
                .GetMethod("setPreset", Java.Lang.Short.Type);
            setPreset.Invoke(_maxAudioReverb, Java.Lang.Short.ValueOf(2)); // PRESET_LARGEHALL
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw.SFX", $"MAX Audio 启用失败: {ex.Message}");
        }
    }

    private void DisableMaxAudio()
    {
        try
        {
            if (_maxAudioReverb != null)
            {
                var setEnabled = Java.Lang.Class.ForName("android.media.audiofx.PresetReverb")
                    .GetMethod("setEnabled", Java.Lang.Boolean.Type);
                setEnabled.Invoke(_maxAudioReverb, Java.Lang.Boolean.False);
            }
        }
        catch { }
    }

    #endregion

    #region Event Handlers

    private class MaxAudioSwitchListener : Java.Lang.Object, CompoundButton.IOnCheckedChangeListener
    {
        private readonly WeakReference<SoundEffectDialog> _ref;
        public MaxAudioSwitchListener(SoundEffectDialog d) => _ref = new(d);
        public void OnCheckedChanged(CompoundButton? b, bool enabled)
        {
            if (!_ref.TryGetTarget(out var d) || d._isInitializing) return;
            if (enabled) d.ApplyMaxAudio(); else d.DisableMaxAudio();
            d.SaveSettings();
        }
    }

    #endregion

    #region External Audio Effects

    private void OpenSystemSoundSettings()
    {
        try
        {
            var soundSettings = new Intent(Android.Provider.Settings.ActionSoundSettings);
            soundSettings.SetFlags(ActivityFlags.NewTask);
            Context.StartActivity(soundSettings);
            Toast.MakeText(Context, "已跳转到系统声音设置", ToastLength.Short)?.Show();
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw.SFX", $"打开系统声音设置失败: {ex.Message}");
            Toast.MakeText(Context, "打开系统声音设置失败", ToastLength.Short)?.Show();
        }
    }

    #endregion

    #region Persistence

    private void SaveSettings()
    {
        try
        {
            var prefs = Context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var editor = prefs.Edit();
            editor.PutBoolean(KeyMaxAudioEnabled, _maxAudioSwitch?.Checked ?? false);
            editor.Apply();
        }
        catch { }
    }

    #endregion

    protected override void OnStop() { SaveSettings(); base.OnStop(); }

    public void Release()
    {
        try
        {
            if (_maxAudioReverb != null)
            {
                var cls = Java.Lang.Class.ForName("android.media.audiofx.PresetReverb");
                cls.GetMethod("release").Invoke(_maxAudioReverb);
            }
        }
        catch { }
        _maxAudioReverb = null;
    }
}