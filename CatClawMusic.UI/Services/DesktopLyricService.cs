using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.ComponentModel;

namespace CatClawMusic.UI.Services;

/// <summary>桌面歌词悬浮窗服务，管理悬浮窗的显示/隐藏、拖拽、样式设置和歌词同步</summary>
public class DesktopLyricService : Java.Lang.Object, IDisposable
{
    private const string PrefKey = "desktop_lyric";
    private const string PrefKeyEnabled = "desktop_lyric_enabled";
    private const string PrefKeyFontSize = "desktop_lyric_font_size";
    private const string PrefKeyFontColor = "desktop_lyric_font_color";
    private const string PrefKeyBgAlpha = "desktop_lyric_bg_alpha";
    private const string PrefKeyDisplayMode = "desktop_lyric_display_mode";
    private const string PrefKeyShowBorder = "desktop_lyric_show_border";
    private const string PrefKeyShowControls = "desktop_lyric_show_controls";


    private const string PrefKeyFontBold = "desktop_lyric_font_bold";
    private const string PrefKeyPosX = "desktop_lyric_pos_x";
    private const string PrefKeyPosY = "desktop_lyric_pos_y";

    private static DesktopLyricService? _instance;
    /// <summary>获取单例实例</summary>
    public static DesktopLyricService Instance => _instance ??= new DesktopLyricService();

    private IWindowManager? _windowManager;
    private View? _lyricView;
    private TextView? _lyricSingleView;
    private TextView? _lyricPrevView;
    private TextView? _lyricNextView;
    private LinearLayout? _doubleLayout;
    private LinearLayout? _controlsLayout;
    private ImageButton? _btnPrev;
    private ImageButton? _btnPlayPause;
    private ImageButton? _btnNext;
    private ImageButton? _btnLike;
    private TextView? _lockButton;

    private View? _rootLayout;
    private IAudioPlayerService? _audioPlayer;
    private NowPlayingViewModel? _nowPlayingVm;
    private Context? _context;
    private bool _isShowing;
    private bool _isLocked;

    private bool _isDragging;
    private float _initialX;
    private float _initialY;
    private float _initialTouchX;
    private float _initialTouchY;
    private int _savedX = -1;
    private int _savedY = -1;

    private float _fontSize = 20f;
    private string _fontColor = "#FFFFFF";
    private float _bgAlpha = 0f;
    private int _displayMode = 0;
    private bool _showBorder = true;
    private bool _showControls = true;
    private bool _fontBold;


    private Handler? _fadeHandler;
    private Action? _fadeAction;
    private Handler? _lockHideHandler;
    private Action? _lockHideAction;
    private int _currentLyricIndex = -1;
    private readonly Handler _mainHandler = new(Looper.MainLooper!); // 澶嶇敤 Handler锛岄伩鍏嶉绻佸垱寤?

    private DesktopLyricService() { }

    /// <summary>获取 SharedPreferences 实例</summary>
    private static ISharedPreferences? GetPrefs()
    {
        var ctx = global::Android.App.Application.Context;
        return ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
    }

    /// <summary>初始化服务，绑定上下文、获取播放器和 ViewModel 依赖、加载偏好设置并订阅事件</summary>
    public void Initialize(Context context)
    {
        Android.Util.Log.Info("DesktopLyricService", "Initialize called");

        _context = context;
        _windowManager = context.GetSystemService(Context.WindowService) as IWindowManager;
        _audioPlayer = MainApplication.Services.GetRequiredService<IAudioPlayerService>();
        _nowPlayingVm = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        _fadeHandler = new Handler(Looper.MainLooper!);

        LoadPreferences();

        if (_audioPlayer != null)
        {
            _audioPlayer.StateChanged -= OnPlaybackStateChanged;
            _audioPlayer.StateChanged += OnPlaybackStateChanged;
            _audioPlayer.PositionChanged -= OnPositionChanged;
            _audioPlayer.PositionChanged += OnPositionChanged;
        }

        if (_nowPlayingVm != null)
        {
            _nowPlayingVm.PropertyChanged -= OnViewModelPropertyChanged;
            _nowPlayingVm.PropertyChanged += OnViewModelPropertyChanged;
        }

        var prefs = GetPrefs();
        var enabled = prefs?.GetBoolean(PrefKeyEnabled, false) ?? false;
        Android.Util.Log.Info("DesktopLyricService", $"DesktopLyric enabled: {enabled}");

        if (enabled)
        {
            _mainHandler.PostDelayed(() =>
            {
                if (_context != null)
                    Show(_context);
            }, 1500);
        }
    }

    /// <summary>从 SharedPreferences 加载用户偏好设置</summary>
    private void LoadPreferences()
    {
        var prefs = GetPrefs();
        if (prefs == null) return;

        _savedX = prefs.GetInt(PrefKeyPosX, -1);
        _savedY = prefs.GetInt(PrefKeyPosY, -1);
        _fontSize = prefs.GetFloat(PrefKeyFontSize, 20f);
        _fontColor = prefs.GetString(PrefKeyFontColor, "#FFFFFF") ?? "#FFFFFF";
        _bgAlpha = prefs.GetFloat(PrefKeyBgAlpha, 0f);
        _displayMode = prefs.GetInt(PrefKeyDisplayMode, 0);
        _showBorder = prefs.GetBoolean(PrefKeyShowBorder, true);
        _showControls = prefs.GetBoolean(PrefKeyShowControls, true);

        _fontBold = prefs.GetBoolean(PrefKeyFontBold, false);
    }

    /// <summary>显示桌面歌词悬浮窗</summary>
    public void Show(Context context)
    {
        Android.Util.Log.Info("DesktopLyricService", $"Show called, _isShowing={_isShowing}");

        if (_isShowing)
        {
            Android.Util.Log.Warn("DesktopLyricService", "Show: already showing, returning");
            return;
        }

        var wm = AcquireWindowManager(context);
        if (wm == null)
        {
            Android.Util.Log.Error("DesktopLyricService", "Show: cannot get WindowManager from any context source");
            _mainHandler.Post(() =>
            {
                Toast.MakeText(context, "无法获取悬浮窗服务，请重启应用后重试", ToastLength.Long)?.Show();
            });
            return;
        }
        _windowManager = wm;
        _context = context;
        _isLocked = false;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var canDraw = global::Android.Provider.Settings.CanDrawOverlays(context);
            Android.Util.Log.Info("DesktopLyricService", $"CanDrawOverlays={canDraw}");
            if (!canDraw)
            {
                Android.Util.Log.Warn("DesktopLyricService", "Show: overlay permission denied");
                _mainHandler.Post(() =>
                {
                    Toast.MakeText(context, "请先在系统设置中开启「显示在其他应用上层」权限", ToastLength.Long)?.Show();
                });
                return;
            }
        }
        else
        {
            Android.Util.Log.Info("DesktopLyricService", "SDK < M, skipping overlay check");
        }

        try
        {
            Android.Util.Log.Info("DesktopLyricService", "Step 1: Calculating position");
            var metrics = context.Resources?.DisplayMetrics;
            var posY = _savedY >= 0 ? _savedY : (metrics?.HeightPixels / 3 ?? 400);

            Android.Util.Log.Info("DesktopLyricService", "Step 2: Trying overlay window");
            var result = TryAddOverlayWindow(context, posY);

            if (!result.success)
            {
                Android.Util.Log.Info("DesktopLyricService", "Step 3: Trying fallback window type");
                result = TryAddOverlayWindow(context, posY, fallback: true);
            }

            if (!result.success)
            {
                Android.Util.Log.Error("DesktopLyricService", "All window types failed");
                _mainHandler.Post(() =>
                {
                    Toast.MakeText(context, "悬浮窗启动失败，请在系统设置中确认已开启权限", ToastLength.Long)?.Show();
                });
                return;
            }

            _isShowing = true;
            _lyricView = result.view;

            Android.Util.Log.Info("DesktopLyricService", "Step 4: Refreshing content");
            RefreshLyricContent();
            StartFadeTimer();

            Android.Util.Log.Info("DesktopLyricService", "View added successfully");
        }
        catch (System.Exception ex)
        {
            Android.Util.Log.Error("DesktopLyricService", $"Failed to show: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>添加悬浮窗的结果结构</summary>
    private struct AddResult
    {
        public bool success;
        public View? view;
    }

    /// <summary>尝试以指定窗口类型添加悬浮窗 View</summary>
    private AddResult TryAddOverlayWindow(Context context, int y, bool fallback = false)
    {
        try
        {
            var inflater = LayoutInflater.From(context);
            var view = inflater.Inflate(Resource.Layout.desktop_lyric_view, null);

            var rootLayout = view.FindViewById<ViewGroup>(Resource.Id.desktop_lyric_root);
            var singleView = view.FindViewById<TextView>(Resource.Id.tv_desktop_lyric_single);
            var prevView = view.FindViewById<TextView>(Resource.Id.tv_desktop_lyric_prev);
            var nextView = view.FindViewById<TextView>(Resource.Id.tv_desktop_lyric_next);
            var doubleLayout = view.FindViewById<LinearLayout>(Resource.Id.desktop_lyric_double_layout);
            var controlsLayout = view.FindViewById<LinearLayout>(Resource.Id.desktop_lyric_controls);
            var btnPrev = view.FindViewById<ImageButton>(Resource.Id.btn_desktop_prev);
            var btnPlayPause = view.FindViewById<ImageButton>(Resource.Id.btn_desktop_play_pause);
            var btnNext = view.FindViewById<ImageButton>(Resource.Id.btn_desktop_next);
            var btnLike = view.FindViewById<ImageButton>(Resource.Id.btn_desktop_like);
            var lockBtn = view.FindViewById<TextView>(Resource.Id.tv_desktop_lyric_lock_btn);


            var typeface = _fontBold ? Typeface.DefaultBold : Typeface.Default;
            if (singleView != null)
            {
                singleView.SetTextSize(Android.Util.ComplexUnitType.Sp, _fontSize);
                singleView.Typeface = typeface;
                singleView.SetTextColor(ParseColor(_fontColor));
            }
            if (prevView != null)
            {
                prevView.SetTextSize(Android.Util.ComplexUnitType.Sp, _fontSize);
                prevView.Typeface = typeface;
                prevView.SetTextColor(ParseColor(_fontColor));
            }
            if (nextView != null)
            {
                nextView.SetTextSize(Android.Util.ComplexUnitType.Sp, _fontSize * 0.75f);
                nextView.Typeface = typeface;
                nextView.SetTextColor(ParseColor(_fontColor, 0.5f));
            }

            if (_displayMode == 1)
            {
                if (singleView != null) singleView.Visibility = ViewStates.Gone;
                if (doubleLayout != null) doubleLayout.Visibility = ViewStates.Visible;
            }
            else
            {
                if (singleView != null) singleView.Visibility = ViewStates.Visible;
                if (doubleLayout != null) doubleLayout.Visibility = ViewStates.Gone;
            }

            if (controlsLayout != null)
            {
                controlsLayout.Visibility = _showControls ? ViewStates.Visible : ViewStates.Gone;
                SetupControlButtons(btnPrev, btnPlayPause, btnNext, btnLike);
            }



            var bgDrawable = new Android.Graphics.Drawables.GradientDrawable();
            bgDrawable.SetCornerRadius(16f * (context.Resources?.DisplayMetrics?.Density ?? 2f));
            if (_showBorder)
                bgDrawable.SetStroke(1, ParseColor("#40FFFFFF"));
            var bgColor = _bgAlpha <= 0.01f ? Color.Transparent
                : new Color((int)(_bgAlpha * 255), 0, 0, 0);
            bgDrawable.SetColor(bgColor);
            if (rootLayout != null) rootLayout.Background = bgDrawable;

            rootLayout?.SetOnTouchListener(new TouchListener(this));

            lockBtn?.SetOnClickListener(new LockClickListener(this, lockBtn));

            if (lockBtn != null) lockBtn.Visibility = ViewStates.Visible;

            var windowType = fallback
                ? WindowManagerTypes.Phone
                : (Build.VERSION.SdkInt >= BuildVersionCodes.O
                    ? WindowManagerTypes.ApplicationOverlay
                    : WindowManagerTypes.Phone);

            Android.Util.Log.Info("DesktopLyricService", $"Step 7: Adding view type={windowType} Y={y}");

            var layoutParams = new WindowManagerLayoutParams(
                WindowManagerLayoutParams.MatchParent,
                WindowManagerLayoutParams.WrapContent,
                windowType,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal | WindowManagerFlags.LayoutNoLimits,
                Format.Translucent);

            layoutParams.Gravity = GravityFlags.Top;
            layoutParams.X = 0;
            layoutParams.Y = y;

            _windowManager?.AddView(view, layoutParams);

            _rootLayout = rootLayout;
            _lyricSingleView = singleView;
            _lyricPrevView = prevView;
            _lyricNextView = nextView;
            _doubleLayout = doubleLayout;
            _controlsLayout = controlsLayout;
            _btnPrev = btnPrev;
            _btnPlayPause = btnPlayPause;
            _btnNext = btnNext;
            _btnLike = btnLike;
            _lockButton = lockBtn;


            ScheduleLockHide();

            return new AddResult { success = true, view = view };
        }
        catch (System.Exception ex)
        {
            Android.Util.Log.Error("DesktopLyricService", $"TryAddOverlayWindow failed (fallback={fallback}): {ex.Message}");
            return new AddResult { success = false, view = null };
        }
    }

    private void SetupControlButtons(ImageButton? btnPrev, ImageButton? btnPlayPause, ImageButton? btnNext, ImageButton? btnLike)
    {
        if (_nowPlayingVm == null) return;

        btnPrev?.SetOnClickListener(new ClickListener(() =>
        {
            _mainHandler.Post(() => _nowPlayingVm?.PreviousCommand.Execute(null));
        }));

        btnPlayPause?.SetOnClickListener(new ClickListener(() =>
        {
            _mainHandler.Post(() => _nowPlayingVm?.PlayPauseCommand.Execute(null));
        }));

        btnNext?.SetOnClickListener(new ClickListener(() =>
        {
            _mainHandler.Post(() => _nowPlayingVm?.NextCommand.Execute(null));
        }));

        btnLike?.SetOnClickListener(new ClickListener(() =>
        {
            _mainHandler.Post(() => _nowPlayingVm?.ToggleLikeCommand.Execute(null));
        }));

        UpdatePlayPauseIcon();
        UpdateLikeIcon();
    }

    private void UpdatePlayPauseIcon()
    {
        if (_btnPlayPause == null || _nowPlayingVm == null) return;
        var isPlaying = _nowPlayingVm.PlayPauseIcon == "⏸";
        _btnPlayPause.SetImageResource(isPlaying ? Resource.Drawable.ic_pause : Resource.Drawable.ic_play);
    }

    private void UpdateLikeIcon()
    {
        if (_btnLike == null || _nowPlayingVm == null) return;
        _btnLike.SetImageResource(_nowPlayingVm.IsLiked ? Resource.Drawable.ic_favorite : Resource.Drawable.ic_favorite_border);
    }


    private class ClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly Action _action;
        public ClickListener(Action action) => _action = action;
        public void OnClick(View? v) => _action();
    }

    private class LockClickListener : Java.Lang.Object, View.IOnClickListener
    {
        private readonly DesktopLyricService _service;
        private readonly TextView _btn;
        public LockClickListener(DesktopLyricService service, TextView btn)
        { _service = service; _btn = btn; }
        public void OnClick(View? v)
        {
            _service._isLocked = !_service._isLocked;
            _btn.Text = _service._isLocked ? "🔒" : "🔓";
            _btn.SetTextColor(_service._isLocked
                ? Color.ParseColor("#FF6B81")
                : Color.White);
            _service.ApplyLockFlags();
        }
    }


    private void ShowLockButton()
    {
        if (_lockButton != null)
        {
            _lockButton.Visibility = ViewStates.Visible;
        }
    }

    private void HideLockButton()
    {
        if (_lockButton != null)
        {
            _lockButton.Visibility = ViewStates.Gone;
        }
    }

    private void ApplyLockFlags()
    {
        if (_lyricView == null || _windowManager == null || !_isShowing) return;
        var lp = _lyricView.LayoutParameters as WindowManagerLayoutParams;
        if (lp == null) return;

        if (_isLocked)
            lp.Flags |= WindowManagerFlags.NotTouchable;
        else
            lp.Flags &= ~WindowManagerFlags.NotTouchable;

        try { _windowManager.UpdateViewLayout(_lyricView, lp); }
        catch (System.Exception ex) { Android.Util.Log.Warn("DesktopLyricService", $"ApplyLockFlags UpdateViewLayout failed: {ex.Message}"); }

        if (_isLocked)
        {
            StopFadeTimer();
            HideLockButton();
            ApplyBackgroundAlpha(0f);
        }
    }

    private void ScheduleLockHide()
    {
        StopLockHide();
        _lockHideHandler ??= new Handler(Looper.MainLooper!);
        _lockHideAction = () =>
        {
            if (!_isDragging)
                HideLockButton();
        };
        _lockHideHandler.PostDelayed(_lockHideAction, 2000);
    }

    private void StopLockHide()
    {
        if (_lockHideAction != null && _lockHideHandler != null)
        {
            _lockHideHandler.RemoveCallbacks(_lockHideAction);
            _lockHideAction = null;
        }
    }

    /// <summary>尝试获取 WindowManager，优先使用缓存实例</summary>
    private IWindowManager? AcquireWindowManager(Context context)
    {
        if (_windowManager != null)
        {
            Android.Util.Log.Info("DesktopLyricService", "AcquireWindowManager: using cached instance");
            return _windowManager;
        }

        var sources = new List<(string name, Context ctx)>
        {
            ("Application context", context.ApplicationContext!),
            ("Fragment/Activity context", context),
        };

        var mainActivity = MainActivity.Instance;
        if (mainActivity != null && mainActivity != context)
        {
            sources.Add(("MainActivity context", mainActivity));
        }

        foreach (var (name, ctx) in sources)
        {
            try
            {
                var svc = ctx.GetSystemService(Context.WindowService);
                var svcType = svc != null ? svc.Class.Name : "null";
                Android.Util.Log.Info("DesktopLyricService", $"AcquireWindowManager: {name} 鈫?{svcType}");

                if (svc != null)
                {
                    var wm = svc.JavaCast<IWindowManager>();
                    if (wm != null)
                    {
                        Android.Util.Log.Info("DesktopLyricService", $"AcquireWindowManager: SUCCESS from {name}");
                        return wm;
                    }
                    Android.Util.Log.Warn("DesktopLyricService", $"AcquireWindowManager: JavaCast failed for {name}");
                }
            }
            catch (System.Exception ex)
            {
                Android.Util.Log.Warn("DesktopLyricService", $"AcquireWindowManager: {name} failed: {ex.Message}");
            }
        }

        Android.Util.Log.Error("DesktopLyricService", "AcquireWindowManager: all sources exhausted");
        return null;
    }

    /// <summary>隐藏桌面歌词悬浮窗</summary>
    public void Hide()
    {
        if (!_isShowing || _windowManager == null || _lyricView == null) return;

        try
        {
            StopFadeTimer();
            _windowManager.RemoveView(_lyricView);
            _isShowing = false;
        }
        catch (System.Exception ex)
        {
            Android.Util.Log.Error("DesktopLyricService", $"Failed to hide: {ex.Message}");
        }
    }

    private class TouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly DesktopLyricService _service;
        public TouchListener(DesktopLyricService service) { _service = service; }
        public bool OnTouch(View? v, MotionEvent? e)
        {
            return _service.HandleTouch(v, e);
        }
    }

    private bool HandleTouch(View? v, MotionEvent? e)
    {
        if (_isLocked || _windowManager == null || _lyricView == null || _rootLayout == null)
            return false;

        var layoutParams = _lyricView.LayoutParameters as WindowManagerLayoutParams;
        if (layoutParams == null) return false;

        switch (e?.Action)
        {
            case MotionEventActions.Down:
                _isDragging = false;
                _initialY = layoutParams.Y;
                _initialTouchX = e.RawX;
                _initialTouchY = e.RawY;
                StopFadeTimer();
                StopLockHide();
                ShowLockButton();
                ApplyBackgroundAlpha(0.35f);
                return true;

            case MotionEventActions.Move:
                var dy = e.RawY - _initialTouchY;
                if (Math.Abs(dy) > 5)
                {
                    _isDragging = true;
                    layoutParams.Y = (int)(_initialY + dy);
                    _windowManager.UpdateViewLayout(_lyricView, layoutParams);
                }
                return true;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_isDragging)
                {
                    _savedY = layoutParams.Y;
                    SavePosition();
                }
                _isDragging = false;
                ScheduleLockHide();
                StartFadeTimer();
                return true;
        }
        return false;
    }


    private void ApplyFontStyle()
    {
        var typeface = _fontBold ? Typeface.DefaultBold : Typeface.Default;
        if (_lyricSingleView != null)
        {
            _lyricSingleView.SetTextSize(Android.Util.ComplexUnitType.Sp, _fontSize);
            _lyricSingleView.Typeface = typeface;
            _lyricSingleView.SetTextColor(ParseColor(_fontColor));
        }
        if (_lyricPrevView != null)
        {
            _lyricPrevView.SetTextSize(Android.Util.ComplexUnitType.Sp, _fontSize);
            _lyricPrevView.Typeface = typeface;
            _lyricPrevView.SetTextColor(ParseColor(_fontColor));
        }
        if (_lyricNextView != null)
        {
            _lyricNextView.SetTextSize(Android.Util.ComplexUnitType.Sp, _fontSize * 0.75f);
            _lyricNextView.Typeface = typeface;
            _lyricNextView.SetTextColor(ParseColor(_fontColor, 0.5f));
        }
    }

    private void ApplyFontColor()
    {
        var color = ParseColor(_fontColor);
        if (_lyricSingleView != null)
            _lyricSingleView.SetTextColor(color);
        if (_lyricPrevView != null)
            _lyricPrevView.SetTextColor(color);
        if (_lyricNextView != null)
            _lyricNextView.SetTextColor(ParseColor(_fontColor, 0.5f));
    }

    private void ApplyBackgroundAlpha(float alpha)
    {
        if (_rootLayout == null) return;

        var bgDrawable = new Android.Graphics.Drawables.GradientDrawable();
        bgDrawable.SetCornerRadius(16f * global::Android.App.Application.Context.Resources?.DisplayMetrics?.Density ?? 16f);

        if (_showBorder)
        {
            bgDrawable.SetStroke(1, ParseColor("#40FFFFFF"));
        }

        var baseColor = _bgAlpha <= 0.01f ? Color.Transparent : ParseColor("#000000");
        var actualAlpha = (int)((_bgAlpha <= 0.01f ? alpha : _bgAlpha) * 255);
        bgDrawable.SetColor(new Color(
            actualAlpha < 0 ? 0 : (actualAlpha > 255 ? 255 : actualAlpha),
            Color.GetRedComponent(baseColor),
            Color.GetGreenComponent(baseColor),
            Color.GetBlueComponent(baseColor)));

        _rootLayout.Background = bgDrawable;
    }

    private void ApplyDisplayMode()
    {
        if (_lyricSingleView == null || _doubleLayout == null) return;

        if (_displayMode == 1)
        {
            _lyricSingleView.Visibility = ViewStates.Gone;
            _doubleLayout.Visibility = ViewStates.Visible;
        }
        else
        {
            _lyricSingleView.Visibility = ViewStates.Visible;
            _doubleLayout.Visibility = ViewStates.Gone;
        }
    }

    private void StartFadeTimer()
    {
        StopFadeTimer();
        _fadeAction = () =>
        {
            if (_isShowing && !_isDragging && _rootLayout != null)
            {
                ApplyBackgroundAlpha(0f);
            }
        };
        _fadeHandler?.PostDelayed(_fadeAction, 1000);
    }

    private void StopFadeTimer()
    {
        if (_fadeAction != null && _fadeHandler != null)
        {
            _fadeHandler.RemoveCallbacks(_fadeAction);
            _fadeAction = null;
        }
    }

    private void RefreshLyricContent()
    {
        if (!_isShowing || _nowPlayingVm == null) return;

        _mainHandler.Post(() =>
        {
            if (_nowPlayingVm.CurrentLyrics != null && _nowPlayingVm.CurrentLyrics.Lines.Count > 0)
            {
                UpdateLyricDisplay(
                    _nowPlayingVm.CurrentLyricLine,
                    _nowPlayingVm.PrevLyricLine,
                    _nowPlayingVm.NextLyricLine);
            }
            else if (!string.IsNullOrEmpty(_nowPlayingVm.CurrentLyricLine))
            {
                UpdateLyricDisplay(_nowPlayingVm.CurrentLyricLine, "", "");
            }
            else
            {
                UpdateLyricDisplay("妗岄潰姝岃瘝", "", "");
            }
        });
    }

    private void UpdateLyricDisplay(string current, string prev, string next)
    {
        if (_lyricSingleView != null)
        {
            _lyricSingleView.Text = string.IsNullOrEmpty(current) ? "♪" : current;
        }
        if (_lyricPrevView != null)
        {
            _lyricPrevView.Text = string.IsNullOrEmpty(current) ? "♪" : current;
        }
        if (_lyricNextView != null)
        {
            _lyricNextView.Text = string.IsNullOrEmpty(next) ? "" : next;
        }
        _currentLyricIndex = _nowPlayingVm?.CurrentLyricIndex ?? -1;
    }

    private void OnPlaybackStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        var prefs = GetPrefs();
        var enabled = prefs?.GetBoolean(PrefKeyEnabled, false) ?? false;

        if (enabled && !_isShowing && _context != null && e.State == PlaybackState.Playing)
        {
            _mainHandler.Post(() => Show(_context));
        }
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (!_isShowing || _nowPlayingVm == null) return;

        _mainHandler.Post(() =>
        {
            if (_nowPlayingVm.CurrentLyrics != null && _nowPlayingVm.CurrentLyrics.Lines.Count > 0)
            {
                if (_nowPlayingVm.CurrentLyricIndex != _currentLyricIndex)
                {
                    UpdateLyricDisplay(
                        _nowPlayingVm.CurrentLyricLine,
                        _nowPlayingVm.PrevLyricLine,
                        _nowPlayingVm.NextLyricLine);
                }
            }
            else
            {
                var line = _nowPlayingVm.CurrentLyricLine;
                if (!string.IsNullOrEmpty(line) && line != "鏆傛棤姝岃瘝" && line != "姝ｅ湪鍔犺浇姝岃瘝...")
                {
                    UpdateLyricDisplay(line, _nowPlayingVm.PrevLyricLine, _nowPlayingVm.NextLyricLine);
                }
            }
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isShowing) return;

        if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLyricLine) ||
            e.PropertyName == nameof(NowPlayingViewModel.CurrentLyrics))
        {
            _mainHandler.Post(() =>
            {
                if (_nowPlayingVm != null)
                {
                    UpdateLyricDisplay(
                        _nowPlayingVm.CurrentLyricLine,
                        _nowPlayingVm.PrevLyricLine,
                        _nowPlayingVm.NextLyricLine);
                }
            });
        }
        else if (e.PropertyName == nameof(NowPlayingViewModel.PlayPauseIcon))
        {
            _mainHandler.Post(UpdatePlayPauseIcon);
        }
        else if (e.PropertyName == nameof(NowPlayingViewModel.IsLiked))
        {
            _mainHandler.Post(UpdateLikeIcon);
        }
    }


    /// <summary>设置字体大小（单位 sp）</summary>
    public void SetFontSize(float fontSizeSp)
    {
        _fontSize = fontSizeSp;
        SavePreference(PrefKeyFontSize, _fontSize);
        if (_isShowing)
        {
            _mainHandler.Post(() => ApplyFontStyle());
        }
    }

    /// <summary>设置字体颜色（十六进制格式，如 #FFFFFF）</summary>
    public void SetFontColor(string colorHex)
    {
        _fontColor = colorHex;
        SavePreference(PrefKeyFontColor, _fontColor);
        if (_isShowing)
        {
            _mainHandler.Post(() => ApplyFontColor());
        }
    }

    /// <summary>设置字体是否加粗</summary>
    public void SetFontBold(bool bold)
    {
        _fontBold = bold;
        SavePreference(PrefKeyFontBold, _fontBold);
        if (_isShowing)
        {
            _mainHandler.Post(() => ApplyFontStyle());
        }
    }

    /// <summary>设置背景不透明度（0~1）</summary>
    public void SetBackgroundAlpha(float alpha)
    {
        _bgAlpha = alpha;
        SavePreference(PrefKeyBgAlpha, _bgAlpha);
        if (_isShowing && !_isDragging)
        {
            _mainHandler.Post(() =>
            {
                ApplyBackgroundAlpha(_bgAlpha);
                if (_bgAlpha > 0.01f)
                {
                    StopFadeTimer();
                }
                else
                {
                    StartFadeTimer();
                }
            });
        }
    }

    /// <summary>设置显示模式（0=单行，1=双行）</summary>
    public void SetDisplayMode(int mode)
    {
        _displayMode = mode;
        SavePreference(PrefKeyDisplayMode, _displayMode);
        if (_isShowing)
        {
            _mainHandler.Post(() => ApplyDisplayMode());
        }
    }

    /// <summary>设置是否显示边框</summary>
    public void SetShowBorder(bool show)
    {
        _showBorder = show;
        SavePreference(PrefKeyShowBorder, _showBorder);
        if (_isShowing)
        {
            _mainHandler.Post(() => ApplyBackgroundAlpha(_isDragging ? 0.35f : _bgAlpha));
        }
    }

    /// <summary>设置是否显示播放控制按钮</summary>
    public void SetShowControls(bool show)
    {
        _showControls = show;
        SavePreference(PrefKeyShowControls, _showControls);
        if (_isShowing)
        {
            _mainHandler.Post(() =>
            {
                if (_controlsLayout != null)
                    _controlsLayout.Visibility = show ? ViewStates.Visible : ViewStates.Gone;
            });
        }
    }

    public float GetFontSize() => _fontSize;
    public string GetFontColor() => _fontColor;
    public bool GetFontBold() => _fontBold;
    public float GetBackgroundAlpha() => _bgAlpha;
    public int GetDisplayMode() => _displayMode;
    public bool GetShowBorder() => _showBorder;
    public bool GetShowControls() => _showControls;

    /// <summary>是否处于锁定状态</summary>
    public bool IsLocked => _isLocked;
    /// <summary>悬浮窗是否正在显示</summary>
    public bool IsShowing => _isShowing;

    /// <summary>切换悬浮窗锁定状态</summary>
    public void ToggleLock()
    {
        _isLocked = !_isLocked;
        if (_isShowing)
            ApplyLockFlags();
    }

    /// <summary>保存悬浮窗位置到 SharedPreferences</summary>
    private void SavePosition()
    {
        var prefs = GetPrefs();
        var editor = prefs?.Edit();
        editor?.PutInt(PrefKeyPosX, _savedX);
        editor?.PutInt(PrefKeyPosY, _savedY);
        editor?.Apply();
    }

    /// <summary>保存 float 类型偏好值</summary>
    private void SavePreference(string key, float value)
    {
        var prefs = GetPrefs();
        prefs?.Edit()?.PutFloat(key, value)?.Apply();
    }

    /// <summary>保存 string 类型偏好值</summary>
    private void SavePreference(string key, string value)
    {
        var prefs = GetPrefs();
        prefs?.Edit()?.PutString(key, value)?.Apply();
    }

    /// <summary>保存 bool 类型偏好值</summary>
    private void SavePreference(string key, bool value)
    {
        var prefs = GetPrefs();
        prefs?.Edit()?.PutBoolean(key, value)?.Apply();
    }

    /// <summary>保存 int 类型偏好值</summary>
    private void SavePreference(string key, int value)
    {
        var prefs = GetPrefs();
        prefs?.Edit()?.PutInt(key, value)?.Apply();
    }

    /// <summary>释放资源，取消事件订阅并隐藏悬浮窗</summary>
    public new void Dispose()
    {
        if (_audioPlayer != null)
        {
            _audioPlayer.StateChanged -= OnPlaybackStateChanged;
            _audioPlayer.PositionChanged -= OnPositionChanged;
        }

        if (_nowPlayingVm != null)
        {
            _nowPlayingVm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        StopFadeTimer();
        Hide();
        base.Dispose();
    }

    /// <summary>解析十六进制颜色字符串为 Color，支持透明度倍乘</summary>
    private static Color ParseColor(string hex, float alphaMultiplier = 1.0f)
    {
        try
        {
            var color = Color.ParseColor(hex);
            if (alphaMultiplier < 1.0f)
            {
                int alpha = (int)(Color.GetAlphaComponent(color) * alphaMultiplier);
                return new Color(
                    alpha < 0 ? 0 : (alpha > 255 ? 255 : alpha),
                    Color.GetRedComponent(color),
                    Color.GetGreenComponent(color),
                    Color.GetBlueComponent(color));
            }
            return color;
        }
        catch
        {
            return Color.White;
        }
    }
}
