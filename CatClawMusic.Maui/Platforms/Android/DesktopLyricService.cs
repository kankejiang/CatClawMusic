using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using Application = Android.App.Application;
using Color = Android.Graphics.Color;
using AUri = Android.Net.Uri;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>
/// Android 桌面歌词悬浮窗服务：使用 WindowManager 在其他应用上方显示歌词。
/// 双层 TextView 叠加 + Canvas 裁剪实现逐字填充效果。
/// </summary>
public class DesktopLyricService : IDesktopLyricService
{
    private const string Tag = "DesktopLyric";

    private IWindowManager? _windowManager;
    private global::Android.Views.View? _overlayView;
    private TextView? _textViewBase;   // 未唱层
    private TextView? _textViewFill;   // 已唱层（裁剪）
    private LinearLayout? _container;
    private WindowManagerLayoutParams? _layoutParams;
    private float _density;
    private string _currentText = "";
    private double _fillProgress = -1;
    private Color _textColor = Color.White;
    private Color _highlightColor = Color.Yellow;
    private float _fontSize = 20f;
    private double _bgOpacity = 0.3;
    private bool _locked;
    private float _posYRatio = 0.75f;

    /// <summary>桌面歌词是否正在显示</summary>
    public bool IsShowing { get; private set; }

    public void Show()
    {
        if (IsShowing) return;

        var ctx = Application.Context;

        // 检查悬浮窗权限
        if (!Settings.CanDrawOverlays(ctx))
        {
            Log.Warn(Tag, "Show() aborted: overlay permission not granted");
            return;
        }

        // 使用 JavaCast 确保正确获取 IWindowManager 代理
        var wmObj = ctx.GetSystemService(Context.WindowService);
        if (wmObj == null)
        {
            Log.Warn(Tag, "Show() aborted: WindowManager service not available");
            return;
        }
        _windowManager = wmObj.JavaCast<IWindowManager>();
        if (_windowManager == null)
        {
            Log.Warn(Tag, "Show() aborted: JavaCast<IWindowManager> returned null");
            return;
        }

        _density = ctx.Resources.DisplayMetrics.Density;

        // 读取设置
        LoadSettings();

        // 创建悬浮 View
        CreateOverlayView(ctx);

        try
        {
            _windowManager!.AddView(_overlayView, _layoutParams);
            IsShowing = true;
            Log.Info(Tag, "Show() success: overlay view added to WindowManager");
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"Show() AddView failed: {ex.GetType().Name}: {ex.Message}");
            Log.Error(Tag, Java.Lang.Throwable.FromException(ex), $"AddView stacktrace");
            // 清理已创建的 View，避免泄漏
            _overlayView = null;
            _container = null;
            _textViewBase = null;
            _textViewFill = null;
        }
    }

    public void Hide()
    {
        if (!IsShowing || _windowManager == null || _overlayView == null)
        {
            IsShowing = false;
            return;
        }

        try
        {
            _windowManager.RemoveView(_overlayView);
            Log.Info(Tag, "Hide() success: overlay view removed");
        }
        catch (Exception ex)
        {
            Log.Error(Tag, $"Hide() RemoveView failed: {ex.GetType().Name}: {ex.Message}");
        }

        _overlayView = null;
        _textViewBase = null;
        _textViewFill = null;
        _container = null;
        IsShowing = false;
    }

    public void UpdateLyric(string? text)
    {
        _currentText = text ?? "";
        UpdateTextInternal();
    }

    public void UpdateFillProgress(double progress)
    {
        _fillProgress = progress;
        UpdateFillInternal();
    }

    public void SetLyrics(LrcLyrics? lyrics)
    {
        // 桌面歌词仅显示当前行，不需要完整歌词数据
    }

    public void ApplySettings()
    {
        LoadSettings();
        if (!IsShowing) return;

        UpdateTextStyle();
        UpdateFillInternal();
    }

    public async Task<bool> CheckPermissionAsync()
    {
        var granted = Settings.CanDrawOverlays(Application.Context);
        Log.Info(Tag, $"CheckPermissionAsync: CanDrawOverlays={granted}");
        return await Task.FromResult(granted);
    }

    public Task<bool> RequestPermissionAsync()
    {
        var intent = new Intent(Settings.ActionManageOverlayPermission,
            AUri.Parse($"package:{Application.Context.PackageName}"));
        intent.AddFlags(ActivityFlags.NewTask);
        Application.Context.StartActivity(intent);
        return Task.FromResult(false);
    }

    // ═══════════════════════════════════════
    // 内部实现
    // ═══════════════════════════════════════

    private void LoadSettings()
    {
        var s = Services.LyricsSettingsService.Instance;
        _fontSize = (float)s.DesktopFontSize;
        _textColor = ParseColor(s.DesktopTextColor, Color.White);
        _highlightColor = ParseColor(s.DesktopHighlightColor, Color.Yellow);
        _locked = s.DesktopLocked;
        _bgOpacity = s.DesktopBgOpacity;
        _posYRatio = (float)s.DesktopPosY;
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return Color.ParseColor(hex); }
        catch { return fallback; }
    }

    private void CreateOverlayView(Context ctx)
    {
        // 容器：圆角半透明背景
        _container = new LinearLayout(ctx)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        _container.SetGravity(GravityFlags.Center);
        int padH = (int)(16 * _density);
        int padV = (int)(8 * _density);
        _container.SetPadding(padH, padV, padH, padV);

        // 使用 FrameLayout 叠加两层 TextView
        var frame = new FrameLayout(ctx);
        frame.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent);

        // 未唱层
        _textViewBase = new TextView(ctx)
        {
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent,
                GravityFlags.Center)
        };
        _textViewBase.Gravity = GravityFlags.Center;
        _textViewBase.SetTextColor(_textColor);
        _textViewBase.SetTextSize(ComplexUnitType.Sp, _fontSize);
        _textViewBase.SetShadowLayer(3f * _density, 0, 1f * _density, Color.Black);
        _textViewBase.SetSingleLine(false);
        _textViewBase.SetMaxLines(2);
        _textViewBase.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;

        // 已唱层（裁剪）
        _textViewFill = new LyricFillTextView(ctx)
        {
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent,
                GravityFlags.Center)
        };
        _textViewFill.Gravity = GravityFlags.Center;
        _textViewFill.SetTextColor(ColorStateList.ValueOf(_highlightColor));
        _textViewFill.SetTextSize(ComplexUnitType.Sp, _fontSize);
        _textViewFill.SetShadowLayer(3f * _density, 0, 1f * _density, Color.Black);
        _textViewFill.SetSingleLine(false);
        _textViewFill.SetMaxLines(2);
        _textViewFill.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;

        frame.AddView(_textViewBase);
        frame.AddView(_textViewFill);
        _container.AddView(frame);

        // 设置背景
        UpdateContainerBackground();

        _overlayView = _container;

        // WindowManager 布局参数
        var displayMetrics = ctx.Resources.DisplayMetrics;
        var y = (int)(displayMetrics.HeightPixels * _posYRatio);

        _layoutParams = new WindowManagerLayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent,
            Build.VERSION.SdkInt >= BuildVersionCodes.O
                ? WindowManagerTypes.ApplicationOverlay
                : WindowManagerTypes.Phone,
            WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal | WindowManagerFlags.LayoutNoLimits,
            Format.Rgba8888)
        {
            Gravity = GravityFlags.Top | GravityFlags.CenterHorizontal,
            X = 0,
            Y = y,
        };

        // 触摸监听：拖动移动位置
        _overlayView.SetOnTouchListener(new DragTouchListener(this));
    }

    private void UpdateContainerBackground()
    {
        if (_container == null) return;
        if (_bgOpacity <= 0.01)
        {
            _container.SetBackgroundColor(Color.Transparent);
            return;
        }
        int alpha = (int)(_bgOpacity * 255);
        var drawable = new global::Android.Graphics.Drawables.GradientDrawable();
        drawable.SetColor(Color.Argb(alpha, 0, 0, 0));
        drawable.SetCornerRadius(16f * _density);
        _container.SetBackgroundDrawable(drawable);
    }

    private void UpdateTextStyle()
    {
        if (_textViewBase == null || _textViewFill == null) return;
        _textViewBase.SetTextColor(_textColor);
        _textViewFill.SetTextColor(ColorStateList.ValueOf(_highlightColor));
        _textViewBase.SetTextSize(ComplexUnitType.Sp, _fontSize);
        _textViewFill.SetTextSize(ComplexUnitType.Sp, _fontSize);
        UpdateContainerBackground();
    }

    private void UpdateTextInternal()
    {
        if (_textViewBase == null || _textViewFill == null) return;
        _textViewBase.Text = _currentText;
        _textViewFill.Text = _currentText;
        // 重置填充
        UpdateFillInternal();
    }

    private void UpdateFillInternal()
    {
        if (_textViewFill is not LyricFillTextView fillView) return;
        fillView.SetFillProgress(_fillProgress);
    }

    /// <summary>更新悬浮窗 Y 位置并持久化</summary>
    internal void UpdatePosition(int y)
    {
        if (_layoutParams == null || _windowManager == null || _overlayView == null) return;
        _layoutParams.Y = y;
        try { _windowManager.UpdateViewLayout(_overlayView, _layoutParams); }
        catch (Exception ex) { Log.Warn(Tag, $"UpdatePosition failed: {ex.Message}"); }

        // 持久化位置
        var displayMetrics = Application.Context.Resources.DisplayMetrics;
        var ratio = (double)y / displayMetrics.HeightPixels;
        Services.LyricsSettingsService.Instance.DesktopPosY = ratio;
    }

    /// <summary>带裁剪的 TextView，通过 Canvas.clipRect 实现逐字填充</summary>
    private class LyricFillTextView : TextView
    {
        private double _progress = -1;

        public LyricFillTextView(Context context) : base(context) { }

        public void SetFillProgress(double progress)
        {
            _progress = progress;
            Invalidate();
        }

        protected override void OnDraw(Canvas? canvas)
        {
            if (canvas == null) return;
            if (_progress > 0 && _progress < 1)
            {
                var w = Width * (float)_progress;
                canvas.Save();
                canvas.ClipRect(0, 0, w, Height);
                base.OnDraw(canvas);
                canvas.Restore();
            }
            else if (_progress >= 1)
            {
                base.OnDraw(canvas);
            }
            // _progress <= 0 不绘制（未唱）
        }
    }

    /// <summary>拖动监听器：实现悬浮窗位置拖动</summary>
    private class DragTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
    {
        private readonly DesktopLyricService _service;
        private float _startX, _startY;
        private int _initX, _initY;
        private bool _moved;

        public DragTouchListener(DesktopLyricService service) => _service = service;

        public bool OnTouch(global::Android.Views.View? v, MotionEvent? e)
        {
            if (e == null || _service._locked) return false;

            switch (e.Action)
            {
                case MotionEventActions.Down:
                    _startX = e.RawX;
                    _startY = e.RawY;
                    _initX = _service._layoutParams?.X ?? 0;
                    _initY = _service._layoutParams?.Y ?? 0;
                    _moved = false;
                    return true;

                case MotionEventActions.Move:
                    var dx = e.RawX - _startX;
                    var dy = e.RawY - _startY;
                    if (Math.Abs(dx) > 5 || Math.Abs(dy) > 5)
                        _moved = true;
                    if (_moved && _service._layoutParams != null)
                    {
                        _service._layoutParams.X = _initX + (int)dx;
                        _service._layoutParams.Y = _initY + (int)dy;
                        try
                        {
                            _service._windowManager?.UpdateViewLayout(_service._overlayView, _service._layoutParams);
                        }
                        catch (Exception ex) { Log.Warn(Tag, $"Drag UpdateViewLayout failed: {ex.Message}"); }
                    }
                    return true;

                case MotionEventActions.Up:
                    if (_moved && _service._layoutParams != null)
                    {
                        // 持久化位置
                        var dm = Application.Context.Resources.DisplayMetrics;
                        var ratio = (double)_service._layoutParams.Y / dm.HeightPixels;
                        Services.LyricsSettingsService.Instance.DesktopPosY = ratio;
                    }
                    return true;
            }
            return false;
        }
    }
}
