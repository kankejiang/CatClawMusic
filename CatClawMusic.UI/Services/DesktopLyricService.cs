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

public class DesktopLyricService : Java.Lang.Object, IDisposable
{
    private const string PrefKey = "desktop_lyric";
    private const string PrefKeyEnabled = "desktop_lyric_enabled";
    private const string PrefKeyFontSize = "desktop_lyric_font_size";
    private const string PrefKeyFontColor = "desktop_lyric_font_color";
    private const string PrefKeyBgAlpha = "desktop_lyric_bg_alpha";
    private const string PrefKeyDisplayMode = "desktop_lyric_display_mode";
    private const string PrefKeyShowBorder = "desktop_lyric_show_border";
    private const string PrefKeyFontBold = "desktop_lyric_font_bold";
    private const string PrefKeyPosX = "desktop_lyric_pos_x";
    private const string PrefKeyPosY = "desktop_lyric_pos_y";

    private static DesktopLyricService? _instance;
    public static DesktopLyricService Instance => _instance ??= new DesktopLyricService();

    private IWindowManager? _windowManager;
    private View? _lyricView;
    private TextView? _lyricSingleView;
    private TextView? _lyricPrevView;
    private TextView? _lyricNextView;
    private LinearLayout? _doubleLayout;
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
    private bool _fontBold;

    private Handler? _fadeHandler;
    private Action? _fadeAction;
    private int _currentLyricIndex = -1;

    private DesktopLyricService() { }

    private static ISharedPreferences? GetPrefs()
    {
        var ctx = global::Android.App.Application.Context;
        return ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
    }

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
            _audioPlayer.StateChanged += OnPlaybackStateChanged;
            _audioPlayer.PositionChanged += OnPositionChanged;
        }

        if (_nowPlayingVm != null)
        {
            _nowPlayingVm.PropertyChanged += OnViewModelPropertyChanged;
        }

        var prefs = GetPrefs();
        var enabled = prefs?.GetBoolean(PrefKeyEnabled, false) ?? false;
        Android.Util.Log.Info("DesktopLyricService", $"DesktopLyric enabled: {enabled}");

        if (enabled)
        {
            var handler = new Handler(Looper.MainLooper!);
            handler.PostDelayed(() =>
            {
                if (_context != null)
                    Show(_context);
            }, 1500);
        }
    }

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
        _fontBold = prefs.GetBoolean(PrefKeyFontBold, false);
    }

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
            new Handler(Looper.MainLooper!).Post(() =>
            {
                Toast.MakeText(context, "无法获取悬浮窗服务，请重启应用后重试", ToastLength.Long)?.Show();
            });
            return;
        }
        _windowManager = wm;
        _context = context;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            var canDraw = global::Android.Provider.Settings.CanDrawOverlays(context);
            Android.Util.Log.Info("DesktopLyricService", $"CanDrawOverlays={canDraw}");
            if (!canDraw)
            {
                Android.Util.Log.Warn("DesktopLyricService", "Show: overlay permission denied");
                var handler = new Handler(Looper.MainLooper!);
                handler.Post(() =>
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
            var posX = _savedX >= 0 ? _savedX : (metrics?.WidthPixels / 4 ?? 200);
            var posY = _savedY >= 0 ? _savedY : (metrics?.HeightPixels / 3 ?? 400);

            Android.Util.Log.Info("DesktopLyricService", "Step 2: Trying overlay window");
            var result = TryAddOverlayWindow(context, posX, posY);

            if (!result.success)
            {
                Android.Util.Log.Info("DesktopLyricService", "Step 3: Trying fallback window type");
                result = TryAddOverlayWindow(context, posX, posY, fallback: true);
            }

            if (!result.success)
            {
                Android.Util.Log.Error("DesktopLyricService", "All window types failed");
                new Handler(Looper.MainLooper!).Post(() =>
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

    private struct AddResult
    {
        public bool success;
        public View? view;
    }

    private AddResult TryAddOverlayWindow(Context context, int x, int y, bool fallback = false)
    {
        try
        {
            var inflater = LayoutInflater.From(context);
            var view = inflater.Inflate(Resource.Layout.desktop_lyric_view, null);

            var rootLayout = view.FindViewById<LinearLayout>(Resource.Id.desktop_lyric_root);
            var singleView = view.FindViewById<TextView>(Resource.Id.tv_desktop_lyric_single);
            var prevView = view.FindViewById<TextView>(Resource.Id.tv_desktop_lyric_prev);
            var nextView = view.FindViewById<TextView>(Resource.Id.tv_desktop_lyric_next);
            var doubleLayout = view.FindViewById<LinearLayout>(Resource.Id.desktop_lyric_double_layout);
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
                prevView.SetTextSize(Android.Util.ComplexUnitType.Sp, _fontSize * 0.75f);
                prevView.Typeface = typeface;
                prevView.SetTextColor(ParseColor(_fontColor, 0.5f));
            }
            if (nextView != null)
            {
                nextView.SetTextSize(Android.Util.ComplexUnitType.Sp, _fontSize * 0.85f);
                nextView.Typeface = typeface;
                nextView.SetTextColor(ParseColor(_fontColor));
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

            var windowType = fallback
                ? WindowManagerTypes.Phone
                : (Build.VERSION.SdkInt >= BuildVersionCodes.O
                    ? WindowManagerTypes.ApplicationOverlay
                    : WindowManagerTypes.Phone);

            Android.Util.Log.Info("DesktopLyricService", $"Step 7: Adding view type={windowType} X={x} Y={y}");

            var layoutParams = new WindowManagerLayoutParams(
                WindowManagerLayoutParams.WrapContent,
                WindowManagerLayoutParams.WrapContent,
                windowType,
                WindowManagerFlags.NotFocusable | WindowManagerFlags.NotTouchModal,
                Format.Translucent);

            layoutParams.Gravity = GravityFlags.Start | GravityFlags.Top;
            layoutParams.X = x;
            layoutParams.Y = y;

            _windowManager?.AddView(view, layoutParams);

            _rootLayout = rootLayout;
            _lyricSingleView = singleView;
            _lyricPrevView = prevView;
            _lyricNextView = nextView;
            _doubleLayout = doubleLayout;
            _lockButton = lockBtn;

            return new AddResult { success = true, view = view };
        }
        catch (System.Exception ex)
        {
            Android.Util.Log.Error("DesktopLyricService", $"TryAddOverlayWindow failed (fallback={fallback}): {ex.Message}");
            return new AddResult { success = false, view = null };
        }
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
            _btn.Text = _service._isLocked ? "🔐" : "🔒";
            _btn.SetTextColor(_service._isLocked
                ? Color.ParseColor("#FF6B81")
                : Color.White);
        }
    }

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
                Android.Util.Log.Info("DesktopLyricService", $"AcquireWindowManager: {name} → {svcType}");

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
                _initialX = layoutParams.X;
                _initialY = layoutParams.Y;
                _initialTouchX = e.RawX;
                _initialTouchY = e.RawY;
                StopFadeTimer();
                ApplyBackgroundAlpha(0.35f);
                return true;

            case MotionEventActions.Move:
                var dx = e.RawX - _initialTouchX;
                var dy = e.RawY - _initialTouchY;
                if (Math.Abs(dx) > 5 || Math.Abs(dy) > 5)
                {
                    _isDragging = true;
                    layoutParams.X = (int)(_initialX + dx);
                    layoutParams.Y = (int)(_initialY + dy);
                    _windowManager.UpdateViewLayout(_lyricView, layoutParams);
                }
                return true;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                if (_isDragging)
                {
                    _savedX = layoutParams.X;
                    _savedY = layoutParams.Y;
                    SavePosition();
                }
                _isDragging = false;
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
            _lyricPrevView.SetTextSize(Android.Util.ComplexUnitType.Sp, _fontSize * 0.75f);
            _lyricPrevView.Typeface = typeface;
            _lyricPrevView.SetTextColor(ParseColor(_fontColor, 0.5f));
        }
        if (_lyricNextView != null)
        {
            _lyricNextView.SetTextSize(Android.Util.ComplexUnitType.Sp, _fontSize * 0.85f);
            _lyricNextView.Typeface = typeface;
            _lyricNextView.SetTextColor(ParseColor(_fontColor));
        }
    }

    private void ApplyFontColor()
    {
        var color = ParseColor(_fontColor);
        if (_lyricSingleView != null)
            _lyricSingleView.SetTextColor(color);
        if (_lyricPrevView != null)
            _lyricPrevView.SetTextColor(ParseColor(_fontColor, 0.5f));
        if (_lyricNextView != null)
            _lyricNextView.SetTextColor(color);
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

        var handler = new Handler(Looper.MainLooper!);
        handler.Post(() =>
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
                UpdateLyricDisplay("桌面歌词", "", "");
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
            _lyricPrevView.Text = string.IsNullOrEmpty(prev) ? "" : prev;
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
            var handler = new Handler(Looper.MainLooper!);
            handler.Post(() => Show(_context));
        }
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (!_isShowing || _nowPlayingVm == null) return;

        var handler = new Handler(Looper.MainLooper!);
        handler.Post(() =>
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
                if (!string.IsNullOrEmpty(line) && line != "暂无歌词" && line != "正在加载歌词...")
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
            var handler = new Handler(Looper.MainLooper!);
            handler.Post(() =>
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
    }

    public void SetFontSize(float fontSizeSp)
    {
        _fontSize = fontSizeSp;
        SavePreference(PrefKeyFontSize, _fontSize);
        if (_isShowing)
        {
            var handler = new Handler(Looper.MainLooper!);
            handler.Post(() => ApplyFontStyle());
        }
    }

    public void SetFontColor(string colorHex)
    {
        _fontColor = colorHex;
        SavePreference(PrefKeyFontColor, _fontColor);
        if (_isShowing)
        {
            var handler = new Handler(Looper.MainLooper!);
            handler.Post(() => ApplyFontColor());
        }
    }

    public void SetFontBold(bool bold)
    {
        _fontBold = bold;
        SavePreference(PrefKeyFontBold, _fontBold);
        if (_isShowing)
        {
            var handler = new Handler(Looper.MainLooper!);
            handler.Post(() => ApplyFontStyle());
        }
    }

    public void SetBackgroundAlpha(float alpha)
    {
        _bgAlpha = alpha;
        SavePreference(PrefKeyBgAlpha, _bgAlpha);
        if (_isShowing && !_isDragging)
        {
            var handler = new Handler(Looper.MainLooper!);
            handler.Post(() =>
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

    public void SetDisplayMode(int mode)
    {
        _displayMode = mode;
        SavePreference(PrefKeyDisplayMode, _displayMode);
        if (_isShowing)
        {
            var handler = new Handler(Looper.MainLooper!);
            handler.Post(() => ApplyDisplayMode());
        }
    }

    public void SetShowBorder(bool show)
    {
        _showBorder = show;
        SavePreference(PrefKeyShowBorder, _showBorder);
        if (_isShowing)
        {
            var handler = new Handler(Looper.MainLooper!);
            handler.Post(() => ApplyBackgroundAlpha(_isDragging ? 0.35f : _bgAlpha));
        }
    }

    public float GetFontSize() => _fontSize;
    public string GetFontColor() => _fontColor;
    public bool GetFontBold() => _fontBold;
    public float GetBackgroundAlpha() => _bgAlpha;
    public int GetDisplayMode() => _displayMode;
    public bool GetShowBorder() => _showBorder;
    public bool IsShowing => _isShowing;

    private void SavePosition()
    {
        var prefs = GetPrefs();
        var editor = prefs?.Edit();
        editor?.PutInt(PrefKeyPosX, _savedX);
        editor?.PutInt(PrefKeyPosY, _savedY);
        editor?.Apply();
    }

    private void SavePreference(string key, float value)
    {
        var prefs = GetPrefs();
        prefs?.Edit()?.PutFloat(key, value)?.Apply();
    }

    private void SavePreference(string key, string value)
    {
        var prefs = GetPrefs();
        prefs?.Edit()?.PutString(key, value)?.Apply();
    }

    private void SavePreference(string key, bool value)
    {
        var prefs = GetPrefs();
        prefs?.Edit()?.PutBoolean(key, value)?.Apply();
    }

    private void SavePreference(string key, int value)
    {
        var prefs = GetPrefs();
        prefs?.Edit()?.PutInt(key, value)?.Apply();
    }

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
