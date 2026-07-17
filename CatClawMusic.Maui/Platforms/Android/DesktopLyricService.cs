using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Util;
using Android.Views;
using ALog = Android.Util.Log;
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
    private HorizontalScrollView? _scrollView;
    private TextView? _lockButton;
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
    private bool _hasScrolled;

    /// <summary>桌面歌词是否正在显示</summary>
    public bool IsShowing { get; private set; }

    public void Show()
    {
        if (IsShowing) return;

        var ctx = Application.Context;

        // 检查悬浮窗权限
        if (!Settings.CanDrawOverlays(ctx))
        {
            ALog.Warn(Tag, "Show() aborted: overlay permission not granted");
            return;
        }

        // 使用 JavaCast 确保正确获取 IWindowManager 代理
        var wmObj = ctx.GetSystemService(Context.WindowService);
        if (wmObj == null)
        {
            ALog.Warn(Tag, "Show() aborted: WindowManager service not available");
            return;
        }
        _windowManager = wmObj.JavaCast<IWindowManager>();
        if (_windowManager == null)
        {
            ALog.Warn(Tag, "Show() aborted: JavaCast<IWindowManager> returned null");
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
            // 如果设置中已锁定，立即应用锁定状态（隐藏按钮 + 触摸穿透）
            if (_locked) ApplyLockState();
            ALog.Info(Tag, "Show() success: overlay view added to WindowManager");
        }
        catch (Exception ex)
        {
            ALog.Error(Tag, $"Show() AddView failed: {ex.GetType().Name}: {ex.Message}");
            ALog.Error(Tag, Java.Lang.Throwable.FromException(ex), $"AddView stacktrace");
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
            ALog.Info(Tag, "Hide() success: overlay view removed");
        }
        catch (Exception ex)
        {
            ALog.Error(Tag, $"Hide() RemoveView failed: {ex.GetType().Name}: {ex.Message}");
        }

        _overlayView = null;
        _textViewBase = null;
        _textViewFill = null;
        _container = null;
        _scrollView = null;
        _lockButton = null;
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
        ApplyLockState();
        UpdateFillInternal();
    }

    public async Task<bool> CheckPermissionAsync()
    {
        var granted = Settings.CanDrawOverlays(Application.Context);
        ALog.Info(Tag, $"CheckPermissionAsync: CanDrawOverlays={granted}");
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
        // 容器：垂直布局，歌词在上，锁定按钮在下
        _container = new LinearLayout(ctx)
        {
            Orientation = global::Android.Widget.Orientation.Vertical,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        _container.SetGravity(GravityFlags.CenterHorizontal);
        int padH = (int)(16 * _density);
        int padV = (int)(8 * _density);
        _container.SetPadding(padH, padV, padH, padV);

        // 使用 HorizontalScrollView 实现长歌词水平滚动，FrameLayout 叠加两层 TextView
        _scrollView = new HorizontalScrollView(ctx)
        {
            HorizontalScrollBarEnabled = false,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };

        var frame = new FrameLayout(ctx);
        frame.LayoutParameters = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent);

        // 未唱层
        _textViewBase = new TextView(ctx)
        {
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
        };
        _textViewBase.SetTextColor(_textColor);
        _textViewBase.SetTextSize(ComplexUnitType.Sp, _fontSize);
        _textViewBase.SetShadowLayer(3f * _density, 0, 1f * _density, Color.Black);
        _textViewBase.SetSingleLine(true);
        _textViewBase.Ellipsize = null;

        // 已唱层（裁剪）
        _textViewFill = new LyricFillTextView(ctx)
        {
            LayoutParameters = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
        };
        _textViewFill.SetTextColor(ColorStateList.ValueOf(_highlightColor));
        _textViewFill.SetTextSize(ComplexUnitType.Sp, _fontSize);
        _textViewFill.SetShadowLayer(3f * _density, 0, 1f * _density, Color.Black);
        _textViewFill.SetSingleLine(true);
        _textViewFill.Ellipsize = null;

        frame.AddView(_textViewBase);
        frame.AddView(_textViewFill);
        _scrollView.AddView(frame);

        _container.AddView(_scrollView);

        // 锁定按钮：文字"🔒"，居中放在歌词下方
        _lockButton = new TextView(ctx)
        {
            Text = "🔒",
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WrapContent,
                ViewGroup.LayoutParams.WrapContent)
            {
                TopMargin = (int)(4 * _density),
                Gravity = GravityFlags.CenterHorizontal
            }
        };
        _lockButton.SetTextSize(ComplexUnitType.Sp, 14f);
        _lockButton.SetPadding(0, 0, 0, 0);
        _lockButton.Click += (s, e) => ToggleLock();

        _container.AddView(_lockButton);

        // 设置背景
        UpdateContainerBackground();

        _overlayView = _container;

        // WindowManager 布局参数
        var displayMetrics = ctx.Resources.DisplayMetrics;
        var y = (int)(displayMetrics.HeightPixels * _posYRatio);

        _layoutParams = new WindowManagerLayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.WrapContent,
            WindowManagerTypes.ApplicationOverlay,
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
        if (_textViewBase == null || _textViewFill == null || _scrollView == null) return;
        _textViewBase.Text = _currentText;
        _textViewFill.Text = _currentText;
        _hasScrolled = false;
        // 使用 OnPreDrawListener 在 layout 完成后重置滚动位置和居中/左对齐
        var vto = _scrollView.ViewTreeObserver;
        vto.AddOnPreDrawListener(new ResetScrollPreDraw(this));
        // 重置填充
        UpdateFillInternal();
    }

    /// <summary>检查是否需要开始滚动（填充进度达到 50% 时触发）</summary>
    private void CheckScrollTrigger()
    {
        if (_hasScrolled || _scrollView == null) return;
        if (_fillProgress < 0.5) return;

        _scrollView.Post(() =>
        {
            if (_scrollView == null || _hasScrolled) return;
            var scrollRange = _scrollView.GetChildAt(0)?.Width ?? 0;
            var visibleWidth = _scrollView.Width;
            if (scrollRange <= visibleWidth) return; // 文本不超长，无需滚动

            _hasScrolled = true;
            // 平滑滚动到最右侧，显示完整歌词
            _scrollView.SmoothScrollTo(scrollRange - visibleWidth, 0);
        });
    }

    private void UpdateFillInternal()
    {
        if (_textViewFill is not LyricFillTextView fillView) return;
        fillView.SetFillProgress(_fillProgress);
        // 检查是否需要触发滚动
        CheckScrollTrigger();
    }

    /// <summary>更新悬浮窗 Y 位置并持久化</summary>
    internal void UpdatePosition(int y)
    {
        if (_layoutParams == null || _windowManager == null || _overlayView == null) return;
        _layoutParams.Y = y;
        try { _windowManager.UpdateViewLayout(_overlayView, _layoutParams); }
        catch (Exception ex) { ALog.Warn(Tag, $"UpdatePosition failed: {ex.Message}"); }

        // 持久化位置
        var displayMetrics = Application.Context.Resources.DisplayMetrics;
        var ratio = (double)y / displayMetrics.HeightPixels;
        Services.LyricsSettingsService.Instance.DesktopPosY = ratio;
    }

    /// <summary>切换锁定状态</summary>
    private void ToggleLock()
    {
        _locked = !_locked;
        Services.LyricsSettingsService.Instance.DesktopLocked = _locked;
        ApplyLockState();
    }

    /// <summary>应用锁定状态：更新按钮可见性和 WindowManager flags</summary>
    private void ApplyLockState()
    {
        if (_lockButton != null)
        {
            // 锁定后隐藏锁定按钮（需通过通知栏关闭再开启解锁）
            _lockButton.Visibility = _locked ? ViewStates.Gone : ViewStates.Visible;
        }

        if (_layoutParams == null || _windowManager == null || _overlayView == null) return;

        // 锁定时添加 NotTouchable 让触摸穿透到下方控件
        if (_locked)
            _layoutParams.Flags |= WindowManagerFlags.NotTouchable;
        else
            _layoutParams.Flags &= ~WindowManagerFlags.NotTouchable;

        try
        {
            _windowManager.UpdateViewLayout(_overlayView, _layoutParams);
            ALog.Info(Tag, $"ApplyLockState: locked={_locked}, flags={_layoutParams.Flags}");
        }
        catch (Exception ex)
        {
            ALog.Warn(Tag, $"ApplyLockState UpdateViewLayout failed: {ex.Message}");
        }
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
                        catch (Exception ex) { ALog.Warn(Tag, $"Drag UpdateViewLayout failed: {ex.Message}"); }
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

    /// <summary>OnPreDraw 监听器：在 layout 完成后重置滚动位置和居中/左对齐，仅触发一次</summary>
    private class ResetScrollPreDraw : Java.Lang.Object, ViewTreeObserver.IOnPreDrawListener
    {
        private readonly DesktopLyricService _service;

        public ResetScrollPreDraw(DesktopLyricService service) => _service = service;

        public bool OnPreDraw()
        {
            var scrollView = _service._scrollView;
            if (scrollView == null) return true;

            // 移除自身（仅触发一次）
            try { scrollView.ViewTreeObserver.RemoveOnPreDrawListener(this); }
            catch { }

            var child = scrollView.GetChildAt(0);
            if (child != null)
            {
                var contentWidth = child.Width;
                var visibleWidth = scrollView.Width;
                // 短歌词居中，长歌词左对齐（确保开头可见）
                var gravity = contentWidth <= visibleWidth
                    ? GravityFlags.CenterHorizontal
                    : GravityFlags.Left;
                var lp = (FrameLayout.LayoutParams)child.LayoutParameters;
                if (lp.Gravity != gravity)
                {
                    lp.Gravity = gravity;
                    child.LayoutParameters = lp;
                }
            }
            // 重置到最左侧
            scrollView.ScrollTo(0, 0);
            return true;
        }
    }
}
