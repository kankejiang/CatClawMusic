using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 桌面歌词设置Fragment，配置悬浮歌词的显示样式、字体大小、颜色等
/// </summary>
public class DesktopLyricFragment : Fragment
{
    private const string PrefKey = "desktop_lyric";
    private const string PrefKeyEnabled = "desktop_lyric_enabled";

    private Switch? _swDesktopLyric;
    private Switch? _swFontBold;
    private Switch? _swShowBorder;
    private SeekBar? _sbFontSize;
    private SeekBar? _sbBgAlpha;
    private TextView? _tvFontSizeValue;
    private TextView? _tvBgAlphaValue;
    private TextView? _tvFontColor;
    private View? _colorPreview;
    private RadioGroup? _rgDisplayMode;
    private RadioButton? _rbSingleLine;
    private RadioButton? _rbDoubleLine;
    private bool _isInitialized;

    private static readonly (string hex, string name)[] PresetColors =
    {
        ("#FFFFFF", "白色"),
        ("#FFE082", "暖黄"),
        ("#80D8FF", "天蓝"),
        ("#B9F6CA", "浅绿"),
        ("#FF8A80", "粉红"),
        ("#EA80FC", "浅紫"),
        ("#FF6E40", "橙色"),
        ("#84FFFF", "青色"),
        ("#CCFF90", "亮绿"),
        ("#FF80AB", "玫红"),
    };

    private static ISharedPreferences? GetPrefs()
    {
        var ctx = global::Android.App.Application.Context;
        return ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
    }

    /// <summary>
    /// 创建桌面歌词设置视图，初始化所有设置控件和事件处理器
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? savedInstanceState)
    {
        var view = inflater.Inflate(Resource.Layout.fragment_desktop_lyric, container, false)!;
        var nav = MainApplication.Services.GetRequiredService<INavigationService>();
        var permissionService = MainApplication.Services.GetRequiredService<IPermissionService>();

        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back);
        if (btnBack != null)
            btnBack.Click += (s, e) => nav.GoBack();

        var service = DesktopLyricService.Instance;

        _swDesktopLyric = view.FindViewById<Switch>(Resource.Id.sw_desktop_lyric);
        _swFontBold = view.FindViewById<Switch>(Resource.Id.sw_font_bold);
        _swShowBorder = view.FindViewById<Switch>(Resource.Id.sw_show_border);
        _sbFontSize = view.FindViewById<SeekBar>(Resource.Id.sb_font_size);
        _sbBgAlpha = view.FindViewById<SeekBar>(Resource.Id.sb_bg_alpha);
        _tvFontSizeValue = view.FindViewById<TextView>(Resource.Id.tv_font_size_value);
        _tvBgAlphaValue = view.FindViewById<TextView>(Resource.Id.tv_bg_alpha_value);
        _tvFontColor = view.FindViewById<TextView>(Resource.Id.tv_font_color);
        _colorPreview = view.FindViewById<View>(Resource.Id.color_preview);
        _rgDisplayMode = view.FindViewById<RadioGroup>(Resource.Id.rg_display_mode);
        _rbSingleLine = view.FindViewById<RadioButton>(Resource.Id.rb_single_line);
        _rbDoubleLine = view.FindViewById<RadioButton>(Resource.Id.rb_double_line);

        var prefs = GetPrefs();

        if (prefs != null)
        {
            var enabled = prefs.GetBoolean(PrefKeyEnabled, false);
            if (_swDesktopLyric != null)
                _swDesktopLyric.Checked = enabled;
        }

        var fontSize = service.GetFontSize();
        if (_sbFontSize != null)
        {
            _sbFontSize.Progress = (int)fontSize;
            UpdateFontSizeLabel(fontSize);
        }

        var bgAlpha = service.GetBackgroundAlpha();
        if (_sbBgAlpha != null)
        {
            _sbBgAlpha.Progress = (int)(bgAlpha * 100);
            UpdateBgAlphaLabel(bgAlpha);
        }

        if (_swFontBold != null)
            _swFontBold.Checked = service.GetFontBold();

        if (_swShowBorder != null)
            _swShowBorder.Checked = service.GetShowBorder();

        var currentColor = service.GetFontColor();
        UpdateColorPreview(currentColor);

        var displayMode = service.GetDisplayMode();
        if (_rbSingleLine != null && _rbDoubleLine != null)
        {
            _rbSingleLine.Checked = displayMode == 0;
            _rbDoubleLine.Checked = displayMode == 1;
        }

        SetupEventHandlers(service, nav, permissionService);

        var btnFontColor = view.FindViewById<View>(Resource.Id.btn_font_color);
        if (btnFontColor != null)
        {
            btnFontColor.Click += (s, e) => ShowColorPickerDialog();
        }

        _isInitialized = true;
        return view;
    }

    /// <summary>
    /// 设置各控件的事件处理器（开关、滑块、模式选择等）
    /// </summary>
    private void SetupEventHandlers(DesktopLyricService service, INavigationService nav, IPermissionService permissionService)
    {
        if (_swDesktopLyric != null)
        {
            _swDesktopLyric.CheckedChange += async (s, e) =>
            {
                if (!_isInitialized) return;

                var prefs = GetPrefs();
                prefs?.Edit()?.PutBoolean(PrefKeyEnabled, e.IsChecked)?.Apply();

                if (e.IsChecked)
                {
                    var hasPermission = await permissionService.CheckOverlayPermissionAsync();
                    if (!hasPermission)
                    {
                        _swDesktopLyric.Checked = false;
                        Toast.MakeText(Context, "请先在设置中开启悬浮窗权限", ToastLength.Long)?.Show();
                        return;
                    }

                    if (Context != null)
                        service.Show(Context);
                }
                else
                {
                    service.Hide();
                }
            };
        }

        if (_sbFontSize != null)
        {
            _sbFontSize.ProgressChanged += (s, e) =>
            {
                if (!_isInitialized || e.FromUser == false) return;
                var size = e.Progress;
                service.SetFontSize(size);
                UpdateFontSizeLabel(size);
            };
        }

        if (_sbBgAlpha != null)
        {
            _sbBgAlpha.ProgressChanged += (s, e) =>
            {
                if (!_isInitialized || e.FromUser == false) return;
                var alpha = e.Progress / 100f;
                service.SetBackgroundAlpha(alpha);
                UpdateBgAlphaLabel(alpha);
            };
        }

        if (_swFontBold != null)
        {
            _swFontBold.CheckedChange += (s, e) =>
            {
                if (!_isInitialized) return;
                service.SetFontBold(e.IsChecked);
            };
        }

        if (_swShowBorder != null)
        {
            _swShowBorder.CheckedChange += (s, e) =>
            {
                if (!_isInitialized) return;
                service.SetShowBorder(e.IsChecked);
            };
        }

        if (_rgDisplayMode != null)
        {
            _rgDisplayMode.CheckedChange += (s, e) =>
            {
                if (!_isInitialized) return;
                var mode = e.CheckedId == Resource.Id.rb_double_line ? 1 : 0;
                service.SetDisplayMode(mode);
            };
        }
    }

    /// <summary>
    /// 更新字体大小数值标签
    /// </summary>
    private void UpdateFontSizeLabel(float sizeSp)
    {
        if (_tvFontSizeValue != null)
            _tvFontSizeValue.Text = $"{sizeSp:F0}sp";
    }

    /// <summary>
    /// 更新背景透明度数值标签
    /// </summary>
    private void UpdateBgAlphaLabel(float alpha)
    {
        if (_tvBgAlphaValue != null)
            _tvBgAlphaValue.Text = $"{(int)(alpha * 100)}%";
    }

    /// <summary>
    /// 更新颜色预览视图和颜色名称标签
    /// </summary>
    private void UpdateColorPreview(string hex)
    {
        if (_colorPreview != null)
        {
            try
            {
                _colorPreview.SetBackgroundColor(Color.ParseColor(hex));
            }
            catch
            {
                _colorPreview.SetBackgroundColor(Color.White);
            }
        }

        if (_tvFontColor != null)
        {
            var name = GetColorName(hex);
            _tvFontColor.Text = name;
        }
    }

    private Android.App.Dialog? _colorDialog;

    /// <summary>
    /// 显示颜色选择器对话框，提供预设颜色供用户选择
    /// </summary>
    private void ShowColorPickerDialog()
    {
        if (Context == null) return;

        var builder = new Android.App.AlertDialog.Builder(Context);
        builder.SetTitle("选择歌词颜色");

        var gridLayout = new GridLayout(Context)
        {
            ColumnCount = 5,
            RowCount = 2,
            Orientation = GridOrientation.Horizontal,
        };

        var density = Context.Resources?.DisplayMetrics?.Density ?? 2f;
        var padding = (int)(12 * density);
        var circleSize = (int)(40 * density);

        foreach (var (hex, name) in PresetColors)
        {
            var drawable = new Android.Graphics.Drawables.GradientDrawable();
            drawable.SetShape(Android.Graphics.Drawables.ShapeType.Oval);
            try { drawable.SetColor(Color.ParseColor(hex)); }
            catch { drawable.SetColor(Color.White); }
            drawable.SetStroke(1, Color.ParseColor("#33000000"));

            var colorView = new View(Context) { Background = drawable };

            var lp = new GridLayout.LayoutParams();
            lp.Width = circleSize;
            lp.Height = circleSize;
            lp.SetMargins(padding, padding, padding, padding);
            colorView.LayoutParameters = lp;

            var capturedHex = hex;
            colorView.Click += (s, e) =>
            {
                DesktopLyricService.Instance.SetFontColor(capturedHex);
                UpdateColorPreview(capturedHex);
                _colorDialog?.Dismiss();
            };

            gridLayout.AddView(colorView);
        }

        builder.SetView(gridLayout);
        builder.SetNegativeButton("取消", (s, e) => { });
        _colorDialog = builder.Create();
        _colorDialog?.Show();
    }

    /// <summary>
    /// 根据颜色十六进制值获取预设颜色名称
    /// </summary>
    private static string GetColorName(string hex)
    {
        foreach (var (h, name) in PresetColors)
        {
            if (string.Equals(h, hex, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return hex;
    }
}
