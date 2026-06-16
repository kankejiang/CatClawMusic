using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.Helpers;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using AndroidX.Core.View;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 全屏歌词页面Fragment
/// 功能：显示歌词、高亮当前行、支持拖拽调整播放位置
/// </summary>
public class FullLyricsFragment : Fragment
{
    // 颜色预设
    // 快速预设色（用于取色盘下方的快捷按钮）
    private static readonly string[] PresetColorNames = { "白色", "黑色", "黄色", "薄荷绿", "粉色", "天蓝", "橙色", "珊瑚", "薰衣草", "青色" };
    private static readonly string[] PresetColorHex = { "#FFFFFFFF", "#FF000000", "#FFFFEB3B", "#FF69F0AE", "#FFFF80AB", "#FF64B5F6", "#FFFFAB40", "#FFFF6E6E", "#FFCE93D8", "#FF4DD0E1" };
    private static readonly string[] InactivePresetNames = { "灰色", "深灰", "黑色", "浅灰", "蓝灰", "淡紫", "暖灰", "石板" };
    private static readonly string[] InactivePresetHex = { "#CCBBBBBB", "#CC555555", "#CC000000", "#DDDDDDDD", "#CC90A4AE", "#CCB39DDB", "#CCBDBDBD", "#CC78909C" };
    private static readonly string[] BgColorNames = { "浅色", "深色", "透明" };
    private static readonly string[] BgColorHex = { "#99F0EBE3", "#990F0D16", "#33000000" };

    // ViewModel
    private NowPlayingViewModel _viewModel = null!;
    // 背景封面ImageView
    private ImageView _bgCover = null!;
    // 歌词滚动ScrollView
    private ScrollView _scrollView = null!;
    // 歌词容器LinearLayout
    private LinearLayout _lyricsContainer = null!;
    // 歌曲标题TextView
    private TextView _songTitle = null!;
    // 歌手TextView
    private TextView _songArtist = null!;
    // 播放进度TextView
    private TextView _progressText = null!;
    // 设置按钮ImageButton
    private ImageButton _btnSettings = null!;
    // 拖拽指示器容器
    private RelativeLayout _dragIndicator = null!;
    // 跳转按钮
    private Button _btnJump = null!;
    // 所有歌词TextView列表
    private readonly List<StrokeTextView> _lyricViews = new();
    // 上一次高亮的歌词索引
    private int _lastLyricIndex = -1;
    // 上一次高亮的合唱伙伴索引（用于同时着色）
    private int _lastDuetPartnerIndex = -1;
    private bool _userScrolling;
    // 用户是否正在手动滚动
    // 是否正在拖拽模式
    private bool _isDragging;
    // 拖拽时选中的歌词索引
    private int _draggedLyricIndex = -1;
    // 滚动恢复Handler（用户停止滚动后3秒恢复）
    private readonly Handler _scrollResumeHandler = new(Looper.MainLooper!);
    // 拖拽恢复Handler（拖拽结束后3秒恢复）
    private readonly Handler _dragResumeHandler = new(Looper.MainLooper!);
    // 上一次的封面路径
    private string? _lastCoverSource;
    
    // SharedPreferences用于保存用户设置
    private ISharedPreferences? _prefs;
    // 是否允许拖拽调整进度
    private bool _allowDragSeek;
    // 歌词字体大小
    private int _lyricFontSize = 20;
    // 歌词对齐方式：0=左，1=中，2=右
    private int _lyricAlignment = 1;
    // 歌词样式：0=逐行，1=逐字
    private int _lyricStyle = 0;
    // 歌词颜色设置（直接存 ARGB 值）
    private Color _lyricActiveColor = Color.White;
    private Color _lyricInactiveColor = Color.ParseColor("#CCBBBBBB");
    private int _lyricActiveArgb = unchecked((int)0xFFFFFFFF);
    private int _lyricInactiveArgb = unchecked((int)0xCCBBBBBB);
    private int _lyricColorMode = 0; // 0=自适应, 1=自定义
    private float _currentBgLuminance = 0.3f; // 封面亮度，用于透明遮罩下的歌词自适应
    private int _lyricBgColorIndex = 0;
    // 歌词是否加粗
    private bool _lyricBold = true;
    // 背景遮罩View
    private View? _bgDimOverlay;
    // 译文TextView列表，用于自适应颜色更新
    private readonly List<WeakReference<TextView>> _translationViews = new();

    /// <summary>
    /// 创建Fragment视图
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_full_lyrics, container, false)!;

    /// <summary>
    /// 视图创建完成后初始化
    /// </summary>
    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        // 获取ViewModel
        _viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
        // 初始化SharedPreferences
        _prefs = Activity?.GetSharedPreferences("lyric_settings", FileCreationMode.Private);
        // 加载用户设置
        LoadSettings();

        // 初始化控件引用
        _bgCover = view.FindViewById<ImageView>(Resource.Id.lyric_bg_cover)!;
        _scrollView = view.FindViewById<ScrollView>(Resource.Id.lyrics_scroll)!;
        _lyricsContainer = view.FindViewById<LinearLayout>(Resource.Id.lyrics_container)!;
        _songTitle = view.FindViewById<TextView>(Resource.Id.lyric_song_title)!;
        _songArtist = view.FindViewById<TextView>(Resource.Id.lyric_song_artist)!;
        _progressText = view.FindViewById<TextView>(Resource.Id.lyric_progress_text)!;
        _btnSettings = view.FindViewById<ImageButton>(Resource.Id.btn_lyric_settings)!;
        _dragIndicator = view.FindViewById<RelativeLayout>(Resource.Id.drag_indicator)!;
        _btnJump = view.FindViewById<Button>(Resource.Id.btn_jump)!;
        _bgDimOverlay = view.FindViewById(Resource.Id.lyric_bg_dim);

        // 应用毛玻璃模糊效果
        ApplyBlur();
        // 设置滚动监听器
        SetupScrollListener();
        // 设置按钮点击事件
        _btnSettings.Click += (s, e) => ShowSettingsDialog();
        // 设置跳转按钮点击事件
        _btnJump.Click += (s, e) => OnJumpClicked();

        var topBar = view.FindViewById<RelativeLayout>(Resource.Id.lyric_top_bar);
        if (topBar != null)
        {
            var origTop = topBar.PaddingTop;
            topBar.SetPadding(topBar.PaddingLeft, MainActivity.StatusBarHeight + (int)(16 * Resources?.DisplayMetrics?.Density ?? 1), topBar.PaddingRight, topBar.PaddingBottom);
        }

        // 动态设置歌词容器的顶部padding，让歌词从页面底部开始
        _scrollView.ViewTreeObserver.AddOnGlobalLayoutListener(new OnGlobalLayoutListener(this));

        // 绑定ViewModel
        BindViewModel();
        // 同步UI状态
        SyncUI();
    }

    /// <summary>
    /// 设置滚动监听器
    /// </summary>
    private void SetupScrollListener()
    {
        // 监听ScrollView滚动变化
        _scrollView.ViewTreeObserver.ScrollChanged += (s, e) =>
        {
            if (!_isDragging && !_userScrolling) return;
            
            if (_isDragging)
            {
                // 拖拽模式：更新拖拽选中的歌词索引
                UpdateDraggedLyricIndex();
            }
            else if (_userScrolling)
            {
                // 用户手动滚动：3秒后恢复自动滚动
                _scrollResumeHandler.RemoveCallbacksAndMessages(null);
                _scrollResumeHandler.PostDelayed(() => 
                {
                    _userScrolling = false;
                    ScrollToCurrentLyric();
                }, 3000);
            }
        };

        // 设置触摸监听器用于检测拖拽
        _scrollView.SetOnTouchListener(new DragTouchListener(this));
    }

    /// <summary>
    /// 更新拖拽时选中的歌词索引
    /// 计算当前屏幕中央位置对应的歌词
    /// </summary>
    private void UpdateDraggedLyricIndex()
    {
        if (_scrollView == null || _lyricViews.Count == 0) return;
        
        try
        {
            // 计算ScrollView当前可见区域的中心点Y坐标
            var scrollCenterY = _scrollView.ScrollY + _scrollView.Height / 2;
            int closestIndex = -1;
            int closestDistance = int.MaxValue;

            // 遍历所有歌词，找到离中心点最近的那个
            for (int i = 0; i < _lyricViews.Count; i++)
            {
                var lyricView = _lyricViews[i];
                if (lyricView == null) continue;
                
                var wrapper = lyricView.Parent as View;
                if (wrapper == null) continue;

                var lyricCenterY = wrapper.Top + wrapper.Height / 2;
                // 计算歌词中心到ScrollView中心的距离
                var distance = Math.Abs(scrollCenterY - lyricCenterY);

                // 记录距离最近的歌词索引
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            // 更新拖拽选中的索引
            if (closestIndex != _draggedLyricIndex && closestIndex >= 0)
            {
                _draggedLyricIndex = closestIndex;
            }
        }
        catch (System.Exception)
        {
            // 捕获异常，避免崩溃
        }
    }

    /// <summary>
    /// 触摸开始回调
    /// </summary>
    public void OnTouchStart()
    {
        _userScrolling = true;
        _scrollResumeHandler.RemoveCallbacksAndMessages(null);
    }

    /// <summary>
    /// 拖拽中回调
    /// </summary>
    public void OnDragging()
    {
        // 如果不允许拖拽，直接返回
        if (!_allowDragSeek) return;
        
        // 首次检测到拖拽时
        if (!_isDragging)
        {
            _isDragging = true;
            _dragResumeHandler.RemoveCallbacksAndMessages(null);
            // 显示拖拽指示器
            ShowDragIndicator();
        }
        // 更新拖拽选中的歌词
        UpdateDraggedLyricIndex();
    }

    /// <summary>
    /// 触摸结束回调
    /// </summary>
    public void OnTouchEnd()
    {
        if (_isDragging)
        {
            // 拖拽模式：3秒后恢复
            _dragResumeHandler.PostDelayed(() =>
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    _draggedLyricIndex = -1;
                    HideDragIndicator();
                    ScrollToCurrentLyric();
                }
            }, 3000);
        }
        else
        {
            // 普通滚动：3秒后恢复
            _scrollResumeHandler.PostDelayed(() =>
            {
                _userScrolling = false;
                ScrollToCurrentLyric();
            }, 3000);
        }
    }

    /// <summary>
    /// 显示拖拽指示器（水平虚线 + 跳转按钮）
    /// </summary>
    private void ShowDragIndicator()
    {
        // 只有在允许拖拽时才显示指示器
        if (_allowDragSeek && _dragIndicator != null)
        {
            _dragIndicator.Visibility = ViewStates.Visible;
        }
    }

    /// <summary>
    /// 隐藏拖拽指示器
    /// </summary>
    private void HideDragIndicator()
    {
        if (_dragIndicator != null)
        {
            _dragIndicator.Visibility = ViewStates.Gone;
        }
    }

    /// <summary>
    /// 跳转按钮点击事件
    /// 跳转到拖拽选中的歌词位置
    /// </summary>
    private void OnJumpClicked()
    {
        if (_draggedLyricIndex < 0) return;

        var lyrics = _viewModel.CurrentLyrics;
        if (lyrics?.Lines == null || _draggedLyricIndex >= lyrics.Lines.Count) return;

        // 获取选中歌词的时间戳并跳转
        var line = lyrics.Lines[_draggedLyricIndex];
        Activity?.RunOnUiThread(() =>
        {
            _viewModel.CurrentPositionSeconds = (long)line.Timestamp.TotalSeconds;
        });

        // 重置拖拽状态
        _isDragging = false;
        _draggedLyricIndex = -1;
        _dragResumeHandler.RemoveCallbacksAndMessages(null);
        HideDragIndicator();
    }

    /// <summary>
    /// 应用毛玻璃模糊效果
    /// 仅Android 12+支持
    /// </summary>
    private void ApplyBlur()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            _bgCover.SetRenderEffect(RenderEffect.CreateBlurEffect(120f, 120f, Shader.TileMode.Clamp));
    }

    /// <summary>
    /// 更新背景封面
    /// </summary>
    private void UpdateBackground()
    {
        var cover = _viewModel.CoverSource;
        if (cover == _lastCoverSource) return;
        _lastCoverSource = cover;

        try
        {
            var oldDrawable = _bgCover.Drawable as Android.Graphics.Drawables.BitmapDrawable;
            if (oldDrawable?.Bitmap != null && oldDrawable.Bitmap.IsRecycled)
                _bgCover.SetImageResource(Resource.Drawable.cover_default);

            if (!string.IsNullOrEmpty(cover) && System.IO.File.Exists(cover))
            {
                var drawable = Drawable.CreateFromPath(cover);
                if (drawable != null)
                {
                    _bgCover.SetImageDrawable(drawable);
                    // 从封面提取亮度，用于透明遮罩下的自适应歌词颜色
                    ComputeCoverLuminance(drawable);
                    return;
                }
            }
        }
        catch { }

        _bgCover.SetImageResource(Resource.Drawable.cover_default);
        _currentBgLuminance = 0.3f;
    }

    /// <summary>从封面 Drawable 提取平均亮度</summary>
    private void ComputeCoverLuminance(Android.Graphics.Drawables.Drawable drawable)
    {
        try
        {
            var bd = drawable as Android.Graphics.Drawables.BitmapDrawable;
            var bitmap = bd?.Bitmap;
            if (bitmap == null || bitmap.IsRecycled) { _currentBgLuminance = 0.3f; return; }

            // 缩放到 1×1 取平均色
            var scaled = Android.Graphics.Bitmap.CreateScaledBitmap(bitmap, 1, 1, false);
            if (scaled == null) { _currentBgLuminance = 0.3f; return; }

            var pixel = new int[1];
            scaled.GetPixels(pixel, 0, 1, 0, 0, 1, 1);
            var c = new Android.Graphics.Color(pixel[0]);
            _currentBgLuminance = (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;

            if (!ReferenceEquals(scaled, bitmap)) scaled.Recycle();
        }
        catch { _currentBgLuminance = 0.3f; }
    }

    /// <summary>
    /// 绑定ViewModel属性变化事件
    /// </summary>
    private void BindViewModel()
    {
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>
    /// 解绑ViewModel属性变化事件
    /// </summary>
    private void UnbindViewModel()
    {
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Activity?.RunOnUiThread(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(_viewModel.CurrentLyrics): 
                    RebuildLyrics();
                    break;
                case nameof(_viewModel.CurrentLyricIndex): 
                    HighlightCurrentLine();
                    break;
                case nameof(_viewModel.CurrentPosition):
                    UpdateProgress();
                    break;
                case nameof(_viewModel.CurrentLyricProgress):
                    if (_lyricStyle == 1)
                        UpdateCurrentLineGradient();
                    break;
                case nameof(_viewModel.DuetPartnerIndex):
                    HighlightCurrentLine();
                    break;
                case nameof(_viewModel.DuetPartnerProgress):
                    if (_lyricStyle == 1)
                        UpdateCurrentLineGradient();
                    break;
                case nameof(_viewModel.TotalDuration): 
                    UpdateProgress();
                    break;
                case nameof(_viewModel.CurrentSong): 
                    _songTitle.Text = _viewModel.CurrentSong?.Title ?? ""; 
                    _songArtist.Text = _viewModel.CurrentSong?.Artist ?? ""; 
                    break;
                case nameof(_viewModel.CoverSource): 
                    UpdateBackground();
                    break;
            }
        });
    }

    /// <summary>
    /// 同步UI状态
    /// </summary>
    private void SyncUI()
    {
        _songTitle.Text = _viewModel.CurrentSong?.Title ?? "";
        _songArtist.Text = _viewModel.CurrentSong?.Artist ?? "";
        UpdateBackground();
        RebuildLyrics();
        UpdateProgress();
    }

    /// <summary>
    /// 重建歌词视图
    /// </summary>
    /// <summary>
    /// 根据遮罩背景的实际颜色计算自适应歌词颜色（深色遮罩用白字，浅色遮罩用黑字）
    /// </summary>
    private void ApplyAdaptiveColors()
    {
        // 透明/半透明遮罩用封面亮度，不透明遮罩用遮罩自身亮度
        var overlayAlpha = 0f;
        if (_bgDimOverlay?.Background is ColorDrawable cd)
            overlayAlpha = cd.Color.A / 255f;
        float lum = overlayAlpha < 0.5f ? _currentBgLuminance : GetBgOverlayLuminance();

        if (lum >= 0.5f)
        {
            _lyricActiveColor = Color.Black;
            _lyricInactiveColor = Color.ParseColor("#AA333333");
        }
        else
        {
            _lyricActiveColor = Color.White;
            _lyricInactiveColor = Color.ParseColor("#EEBBBBBB");
        }

        // 同步更新译文颜色
        UpdateTranslationColors();
    }

    /// <summary>获取背景遮罩的实际亮度</summary>
    private float GetBgOverlayLuminance()
    {
        if (_bgDimOverlay?.Background is ColorDrawable cd)
        {
            var c = cd.Color;
            return (0.299f * c.R + 0.587f * c.G + 0.114f * c.B) / 255f;
        }
        return 0.1f; // 默认深色
    }

    /// <summary>根据当前活跃/非活跃颜色更新所有译文的字体颜色</summary>
    private void UpdateTranslationColors()
    {
        // 译文色 = 非活跃色的更低透明度版本
        var transColor = new Color(
            (byte)(_lyricInactiveColor.A * 3 / 4),
            _lyricInactiveColor.R,
            _lyricInactiveColor.G,
            _lyricInactiveColor.B);
        var transHighlightColor = new Color(
            (byte)(Math.Min(_lyricActiveColor.A + 40, 255)),
            _lyricActiveColor.R,
            _lyricActiveColor.G,
            _lyricActiveColor.B);

        for (int i = _translationViews.Count - 1; i >= 0; i--)
        {
            if (_translationViews[i].TryGetTarget(out var tv))
                tv.SetTextColor(transColor);
            else
                _translationViews.RemoveAt(i);
        }
    }

    private void RebuildLyrics()
    {
        _lyricsContainer.RemoveAllViews(); 
        _lyricViews.Clear(); 
        _lastLyricIndex = -1;
        _lastDuetPartnerIndex = -1;

        // 自适应模式：根据背景自动设置歌词颜色
        if (_lyricColorMode == 0)
            ApplyAdaptiveColors();

        var lyrics = _viewModel.CurrentLyrics;
        if (lyrics?.Lines == null || lyrics.Lines.Count == 0)
        {
            // 显示"暂无歌词"
            var empty = new TextView(Context) { Text = "暂无歌词" };
            empty.SetTextSize(Android.Util.ComplexUnitType.Sp, _lyricFontSize);
            empty.SetTextColor(new Color(UiHelper.ResolveThemeColor(Context!, Resource.Attribute.catClawTextHint, Color.ParseColor("#B0A8BA").ToArgb())));
            empty.Gravity = GetLyricGravity();
            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = 80; 
            empty.LayoutParameters = lp; 
            _lyricsContainer.AddView(empty); 
            return;
        }

        // 为每一行歌词创建StrokeTextView
        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            // 如果该歌词有逐行对齐方式（TTML/AMLL role 属性），使用每行的 Alignment；
            // 否则使用全局 _lyricAlignment 设置。
            var lineGravity = lyrics.HasPerLineAlignment
                ? GetGravityFromAlignment(line.Alignment)
                : GetLyricGravity();

            var lineLayout = new LinearLayout(Context) { Orientation = Orientation.Vertical };
            lineLayout.SetGravity(lineGravity);
            lineLayout.Tag = i;

            // 对唱歌词左/右对齐时添加不对称 padding，使演唱1靠左、演唱2靠右，与居中 LRC 视觉区分
            if (lyrics.HasPerLineAlignment)
            {
                int edgePadding = (int)(32 * Context!.Resources!.DisplayMetrics!.Density);
                int innerPadding = (int)(8 * Context.Resources.DisplayMetrics.Density);
                // 左对齐行：左侧大 padding 推向左边缘；右对齐行：右侧大 padding 推向右边缘
                if (line.Alignment == 0)
                    lineLayout.SetPadding(edgePadding, 0, innerPadding, 0);
                else if (line.Alignment == 2)
                    lineLayout.SetPadding(innerPadding, 0, edgePadding, 0);
                else
                    lineLayout.SetPadding(edgePadding, 0, edgePadding, 0);
            }

            var tv = new StrokeTextView(Context) { Text = line.Text };
            tv.SetTextSize(Android.Util.ComplexUnitType.Sp, _lyricFontSize);
            tv.SetTypeface(null, _lyricBold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
            tv.Gravity = lineGravity;
            tv.SetLineSpacing(0, 1.4f);
            tv.StrokeEnabled = false; // 不再使用描边
            tv.UnsungColor = _lyricInactiveColor;
            tv.SungColor = _lyricActiveColor;
            tv.SetTextColor(_lyricInactiveColor);
            lineLayout.AddView(tv);

            if (!string.IsNullOrEmpty(line.Translation))
            {
                var transColor = new Color(
                    (byte)(_lyricInactiveColor.A * 3 / 4),
                    _lyricInactiveColor.R,
                    _lyricInactiveColor.G,
                    _lyricInactiveColor.B);
                var transTv = new TextView(Context) { Text = line.Translation };
                transTv.SetTextSize(Android.Util.ComplexUnitType.Sp, _lyricFontSize - 2);
                transTv.SetTextColor(transColor);
                transTv.SetTypeface(null, TypefaceStyle.Normal);
                transTv.Gravity = lineGravity;
                transTv.SetLineSpacing(0, 1.3f);
                var transLp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
                transLp.TopMargin = 4;
                transTv.LayoutParameters = transLp;
                lineLayout.AddView(transTv);
                _translationViews.Add(new WeakReference<TextView>(transTv));
            }

            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = i > 0 ? 16 : 0;
            lineLayout.LayoutParameters = lp;

            _lyricsContainer.AddView(lineLayout);
            _lyricViews.Add(tv);
        }

        HighlightCurrentLine();
        UpdateBgOverlay();
    }

    /// <summary>
    /// 更新背景遮罩颜色
    /// </summary>
    private void UpdateBgOverlay()
    {
        if (_bgDimOverlay == null) return;
        var hex = BgColorHex[Math.Clamp(_lyricBgColorIndex, 0, BgColorHex.Length - 1)];
        _bgDimOverlay.SetBackgroundColor(Color.ParseColor(hex));
        // 遮罩变化后重新计算自适应歌词颜色
        if (_lyricColorMode == 0)
            ApplyAdaptiveColors();
        // 刷新所有歌词视图的颜色
        RefreshLyricColors();
    }

    /// <summary>
    /// 刷新所有歌词视图的颜色（不重置进度）
    /// </summary>
    private void RefreshLyricColors()
    {
        var idx = _lastLyricIndex;
        foreach (var v in _lyricViews)
        {
            v.SetTextColor(_lyricInactiveColor);
            v.UnsungColor = _lyricInactiveColor;
            v.SungColor = _lyricActiveColor;

            var parent = v.Parent as LinearLayout;
            if (parent != null && parent.ChildCount > 1)
            {
                var transTv = parent.GetChildAt(1) as TextView;
                if (transTv != null)
                {
                    var transNormalColor = new Color(
                        (byte)(_lyricInactiveColor.A * 3 / 4),
                        _lyricInactiveColor.R,
                        _lyricInactiveColor.G,
                        _lyricInactiveColor.B);
                    transTv.SetTextColor(transNormalColor);
                }
            }
        }

        // 当前行使用活跃色
        if (idx >= 0 && idx < _lyricViews.Count)
        {
            _lyricViews[idx].SetTextColor(_lyricActiveColor);
            _lyricViews[idx].UnsungColor = _lyricInactiveColor;
            _lyricViews[idx].SungColor = _lyricActiveColor;

            var parent2 = _lyricViews[idx].Parent as LinearLayout;
            if (parent2 != null && parent2.ChildCount > 1)
            {
                var transTv = parent2.GetChildAt(1) as TextView;
                if (transTv != null)
                {
                    var transActiveColor = new Color(
                        (byte)(_lyricActiveColor.A * 3 / 4),
                        _lyricActiveColor.R,
                        _lyricActiveColor.G,
                        _lyricActiveColor.B);
                    transTv.SetTextColor(transActiveColor);
                }
            }
        }

        // 更新译文颜色
        UpdateTranslationColors();
    }

    /// <summary>
    /// 获取歌词对齐方式的GravityFlags
    /// </summary>
    private GravityFlags GetLyricGravity()
    {
        return _lyricAlignment switch
        {
            0 => GravityFlags.Start | GravityFlags.CenterVertical,    // 左对齐
            2 => GravityFlags.End | GravityFlags.CenterVertical,      // 右对齐
            _ => GravityFlags.Center                                  // 居中（默认）
        };
    }

    /// <summary>
    /// 根据指定对齐值获取 GravityFlags（用于逐行对齐）
    /// </summary>
    private static GravityFlags GetGravityFromAlignment(int alignment)
    {
        return alignment switch
        {
            0 => GravityFlags.Start | GravityFlags.CenterVertical,    // 左对齐
            2 => GravityFlags.End | GravityFlags.CenterVertical,      // 右对齐
            _ => GravityFlags.Center                                  // 居中（默认）
        };
    }

    /// <summary>
    /// 高亮当前播放的歌词行（含合唱伙伴行同时着色）
    /// </summary>
    private void HighlightCurrentLine()
    {
        var idx = _viewModel.CurrentLyricIndex;
        var partnerIdx = _viewModel.DuetPartnerIndex;

        // 自适应模式：根据背景自动设置歌词颜色
        if (_lyricColorMode == 0)
            ApplyAdaptiveColors();

        // 只在当前行和合唱伙伴都未变化时跳过
        if (idx == _lastLyricIndex && partnerIdx == _lastDuetPartnerIndex) return;

        // 重置上一行（当前行）
        if (_lastLyricIndex >= 0 && _lastLyricIndex < _lyricViews.Count)
        {
            ResetLineHighlight(_lastLyricIndex);
        }

        // 重置上一轮的合唱伙伴行
        if (_lastDuetPartnerIndex >= 0 && _lastDuetPartnerIndex < _lyricViews.Count
            && _lastDuetPartnerIndex != idx && _lastDuetPartnerIndex != _lastLyricIndex)
        {
            ResetLineHighlight(_lastDuetPartnerIndex);
        }

        // 设置当前行高亮
        if (idx >= 0 && idx < _lyricViews.Count)
        {
            HighlightLine(idx, _lyricFontSize + 4, _lyricActiveColor, _viewModel.CurrentLyricProgress);
        }

        // 设置合唱伙伴行高亮（同时着色，使用稍小字号区分）
        if (partnerIdx >= 0 && partnerIdx < _lyricViews.Count && partnerIdx != idx)
        {
            HighlightLine(partnerIdx, _lyricFontSize + 2, _lyricActiveColor, _viewModel.DuetPartnerProgress);
        }

        _lastLyricIndex = idx;
        _lastDuetPartnerIndex = partnerIdx;

        if (!_userScrolling && !_isDragging)
            ScrollToCurrentLyric();
    }

    /// <summary>将指定行重置为非高亮状态</summary>
    private void ResetLineHighlight(int lineIdx)
    {
        if (lineIdx < 0 || lineIdx >= _lyricViews.Count) return;
        var tv = _lyricViews[lineIdx];
        tv.SetTextSize(Android.Util.ComplexUnitType.Sp, _lyricFontSize);
        tv.SetTextColor(_lyricInactiveColor);
        tv.SetTypeface(null, _lyricBold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        tv.ResetLyricProgress();
        tv.UnsungColor = _lyricInactiveColor;
        tv.SungColor = _lyricActiveColor;
        tv.StrokeEnabled = false;

        // 重置译文颜色
        var parent = tv.Parent as LinearLayout;
        if (parent != null && parent.ChildCount > 1)
        {
            var transTv = parent.GetChildAt(1) as TextView;
            if (transTv != null)
            {
                transTv.SetTextSize(Android.Util.ComplexUnitType.Sp, _lyricFontSize - 2);
                var transNormalColor = new Color(
                    (byte)(_lyricInactiveColor.A * 3 / 4),
                    _lyricInactiveColor.R,
                    _lyricInactiveColor.G,
                    _lyricInactiveColor.B);
                transTv.SetTextColor(transNormalColor);
            }
        }
    }

    /// <summary>将指定行设置高亮状态（含渐变进度）</summary>
    private void HighlightLine(int lineIdx, float fontSize, Color activeColor, float progress)
    {
        if (lineIdx < 0 || lineIdx >= _lyricViews.Count) return;
        var tv = _lyricViews[lineIdx];
        tv.SetTextSize(Android.Util.ComplexUnitType.Sp, fontSize);
        tv.SetTextColor(activeColor);
        tv.UnsungColor = _lyricInactiveColor;
        tv.SungColor = activeColor;
        if (_lyricStyle == 1)
        {
            tv.StrokeEnabled = false;
            tv.LyricProgress = progress;
        }

        // 高亮译文
        var parent = tv.Parent as LinearLayout;
        if (parent != null && parent.ChildCount > 1)
        {
            var transTv = parent.GetChildAt(1) as TextView;
            if (transTv != null)
            {
                transTv.SetTextSize(Android.Util.ComplexUnitType.Sp, _lyricFontSize - 2);
                var transHighlightColor = new Color(
                    (byte)Math.Min(activeColor.A + 40, 255),
                    activeColor.R,
                    activeColor.G,
                    activeColor.B);
                transTv.SetTextColor(transHighlightColor);
            }
        }
    }

    private void UpdateCurrentLineGradient()
    {
        var idx = _viewModel.CurrentLyricIndex;
        if (idx >= 0 && idx < _lyricViews.Count)
            _lyricViews[idx].LyricProgress = _viewModel.CurrentLyricProgress;

        // 同时更新合唱伙伴行的渐变进度
        var partnerIdx = _viewModel.DuetPartnerIndex;
        if (partnerIdx >= 0 && partnerIdx < _lyricViews.Count && partnerIdx != idx)
            _lyricViews[partnerIdx].LyricProgress = _viewModel.DuetPartnerProgress;
    }

    /// <summary>
    /// 滚动到当前播放的歌词位置
    /// 将当前歌词固定在页面中央虚线位置
    /// </summary>
    private void ScrollToCurrentLyric()
    {
        if (_scrollView == null || _viewModel == null) return;
        
        var idx = _viewModel.CurrentLyricIndex;
        if (idx < 0 || idx >= _lyricViews.Count) return;

        var t = _lyricViews[idx];
        if (t == null) return;
        var wrapper = t.Parent as View;
        if (wrapper == null) return;
        
        Activity?.RunOnUiThread(() =>
        {
            try
            {
                var scrollViewHeight = _scrollView.Height;
                if (scrollViewHeight <= 0) return;
                
                var lyricCenterY = wrapper.Top + wrapper.Height / 2;
                // 计算目标滚动位置，使歌词中心与ScrollView中心对齐
                var targetScrollY = lyricCenterY - scrollViewHeight / 2;
                _scrollView.SmoothScrollTo(0, Math.Max(0, targetScrollY));
            }
            catch (System.Exception)
            {
                // 捕获异常，避免崩溃
            }
        });
    }

    /// <summary>
    /// 从SharedPreferences加载用户设置
    /// </summary>
    private void LoadSettings()
    {
        if (_prefs == null) return;
        _allowDragSeek = _prefs.GetBoolean("allow_drag_seek", true);
        _lyricFontSize = _prefs.GetInt("lyric_font_size", 20);
        _lyricAlignment = _prefs.GetInt("lyric_alignment", 1);
        _lyricBgColorIndex = _prefs.GetInt("lyric_bg_color", 0);
        _lyricBold = _prefs.GetBoolean("lyric_bold", true);

        // 读取 ARGB 颜色值（向后兼容旧版索引存储）
        var hasActiveArgb = _prefs.Contains("lyric_active_argb");
        if (hasActiveArgb)
        {
            _lyricActiveArgb = _prefs.GetInt("lyric_active_argb", unchecked((int)0xFFFFFFFF));
        }
        else
        {
            // 旧版迁移：从索引读取
            var activeIdx = _prefs.GetInt("lyric_active_color", 0);
            _lyricActiveArgb = (int)Color.ParseColor(PresetColorHex[Math.Clamp(activeIdx, 0, PresetColorHex.Length - 1)]).ToArgb();
        }
        var hasInactiveArgb = _prefs.Contains("lyric_inactive_argb");
        if (hasInactiveArgb)
        {
            _lyricInactiveArgb = _prefs.GetInt("lyric_inactive_argb", unchecked((int)0xCCBBBBBB));
        }
        else
        {
            var inactiveIdx = _prefs.GetInt("lyric_inactive_color", 0);
            _lyricInactiveArgb = (int)Color.ParseColor(InactivePresetHex[Math.Clamp(inactiveIdx, 0, InactivePresetHex.Length - 1)]).ToArgb();
        }

        _lyricActiveColor = new Color(_lyricActiveArgb);
        _lyricInactiveColor = new Color(_lyricInactiveArgb);
        _lyricColorMode = _prefs.GetInt("lyric_color_mode", 0);

        var catclawPrefs = Activity?.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private);
        _lyricStyle = catclawPrefs?.GetInt("lyric_style", 0) ?? 0;
    }

    /// <summary>
    /// 保存用户设置到SharedPreferences
    /// </summary>
    private void SaveSettings()
    {
        if (_prefs == null) return;
        var e = _prefs.Edit();
        e.PutBoolean("allow_drag_seek", _allowDragSeek);
        e.PutInt("lyric_font_size", _lyricFontSize);
        e.PutInt("lyric_alignment", _lyricAlignment);
        e.PutInt("lyric_active_argb", _lyricActiveArgb);
        e.PutInt("lyric_inactive_argb", _lyricInactiveArgb);
        e.PutInt("lyric_bg_color", _lyricBgColorIndex);
        e.PutInt("lyric_color_mode", _lyricColorMode);
        e.PutBoolean("lyric_bold", _lyricBold);
        e.Apply();
    }

    /// <summary>
    /// 显示歌词设置对话框
    /// </summary>
    private void ShowSettingsDialog()
    {
        if (Context == null || Activity == null) return;

        var dp = (int)Resources!.DisplayMetrics!.Density;
        var tv = new Android.Util.TypedValue();
        var themeColor = Activity.Theme?.ResolveAttribute(global::Android.Resource.Attribute.ColorPrimary, tv, true) == true
            ? new Color(tv.Data) : Color.ParseColor("#9B7ED8");

        var content = new LinearLayout(Context) { Orientation = Orientation.Vertical };
        content.SetPadding(dp * 14, dp * 8, dp * 14, dp * 8);

        var cbDragSeek = new CheckBox(Context) { Text = "允许拖拽调整进度", Checked = _allowDragSeek };
        cbDragSeek.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        cbDragSeek.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        cbDragSeek.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        content.AddView(cbDragSeek);

        var catclawPrefs = Activity.GetSharedPreferences("catclaw_prefs", FileCreationMode.Private);
        var currentLyricsMode = catclawPrefs?.GetInt("lyrics_mode", 0) ?? 0;
        var currentLyricStyle = catclawPrefs?.GetInt("lyric_style", 0) ?? 0;

        var styleLabel = new TextView(Context) { Text = "歌词样式" };
        styleLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        styleLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        styleLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(styleLabel);

        var rgLyricStyle = new RadioGroup(Context) { Orientation = Orientation.Horizontal };
        var rbLineByLine = new RadioButton(Context) { Text = "逐行" };
        var rbWordByWord = new RadioButton(Context) { Text = "逐字" };
        rbLineByLine.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbWordByWord.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbLineByLine.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rbWordByWord.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rgLyricStyle.AddView(rbLineByLine);
        rgLyricStyle.AddView(rbWordByWord);
        rgLyricStyle.Check(currentLyricStyle == 1 ? rbWordByWord.Id : rbLineByLine.Id);
        content.AddView(rgLyricStyle);

        var modeLabel = new TextView(Context) { Text = "歌词模式" };
        modeLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        modeLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        modeLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(modeLabel);

        var rgLyricsMode = new RadioGroup(Context) { Orientation = Orientation.Vertical };
        var modeOptions = new[] { "外挂歌词（.lrc 文件优先）", "内嵌歌词（音频标签优先）", "关闭歌词" };
        for (int i = 0; i < modeOptions.Length; i++)
        {
            var rb = new RadioButton(Context) { Text = modeOptions[i] };
            rb.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
            rb.SetTextColor(Color.ParseColor("#DDFFFFFF"));
            rb.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
            rb.SetPadding(0, dp * 4, 0, dp * 4);
            rgLyricsMode.AddView(rb);
        }
        var initialModeRb = rgLyricsMode.GetChildAt(currentLyricsMode) as RadioButton;
        if (initialModeRb != null)
            rgLyricsMode.Check(initialModeRb.Id);
        content.AddView(rgLyricsMode);

        var fontLabel = new TextView(Context) { Text = "字体大小" };
        fontLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        fontLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        fontLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(fontLabel);

        var fontRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
        fontRow.SetGravity(GravityFlags.CenterVertical);
        var sbFontSize = new SeekBar(Context);
        sbFontSize.Max = 28;
        sbFontSize.Progress = _lyricFontSize;
        sbFontSize.LayoutParameters = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 };
        var tvFontSizeValue = new TextView(Context) { Text = $"{_lyricFontSize}sp" };
        tvFontSizeValue.SetTextColor(Color.White);
        tvFontSizeValue.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        tvFontSizeValue.SetPadding(dp * 8, 0, 0, 0);
        fontRow.AddView(sbFontSize);
        fontRow.AddView(tvFontSizeValue);
        content.AddView(fontRow);

        var cbBold = new CheckBox(Context) { Text = "字体加粗", Checked = _lyricBold };
        cbBold.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        cbBold.SetTextSize(Android.Util.ComplexUnitType.Sp, 13f);
        cbBold.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        cbBold.SetPadding(0, dp * 6, 0, 0);
        content.AddView(cbBold);

        var alignLabel = new TextView(Context) { Text = "对齐方式" };
        alignLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        alignLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        alignLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(alignLabel);

        var rgAlignment = new RadioGroup(Context) { Orientation = Orientation.Horizontal };
        var rbLeft = new RadioButton(Context) { Text = "左" };
        var rbCenter = new RadioButton(Context) { Text = "中" };
        var rbRight = new RadioButton(Context) { Text = "右" };
        rbLeft.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbCenter.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbRight.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbLeft.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rbCenter.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rbRight.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rgAlignment.AddView(rbLeft);
        rgAlignment.AddView(rbCenter);
        rgAlignment.AddView(rbRight);
        rgAlignment.Check(_lyricAlignment switch { 0 => rbLeft.Id, 2 => rbRight.Id, _ => rbCenter.Id });
        content.AddView(rgAlignment);

        // ---- 字体颜色模式（自适应 / 自定义） ----
        var colorModeLabel = new TextView(Context) { Text = "字体颜色" };
        colorModeLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        colorModeLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        colorModeLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(colorModeLabel);

        var rgColorMode = new RadioGroup(Context) { Orientation = Orientation.Horizontal };
        var rbAdaptive = new RadioButton(Context) { Text = "自适应" };
        var rbCustom = new RadioButton(Context) { Text = "自定义" };
        rbAdaptive.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbCustom.SetTextColor(Color.ParseColor("#DDFFFFFF"));
        rbAdaptive.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rbCustom.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
        rgColorMode.AddView(rbAdaptive);
        rgColorMode.AddView(rbCustom);
        rgColorMode.Check(_lyricColorMode == 0 ? rbAdaptive.Id : rbCustom.Id);
        content.AddView(rgColorMode);

        // 自定义颜色容器（自适应模式下隐藏）
        var customColorContainer = new LinearLayout(Context) { Orientation = Orientation.Vertical };
        customColorContainer.Visibility = _lyricColorMode == 0 ? ViewStates.Gone : ViewStates.Visible;

        // ---- 当前行颜色（取色盘） ----
        var activeColorLabel = new TextView(Context) { Text = "当前行颜色" };
        activeColorLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        activeColorLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        activeColorLabel.SetPadding(0, dp * 12, 0, dp * 4);
        customColorContainer.AddView(activeColorLabel);

        var activePicker = new ColorPickerView(Context);
        activePicker.SetColor(_lyricActiveColor);
        customColorContainer.AddView(activePicker);

        // 快速预设色按钮
        var activePresetRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
        for (int i = 0; i < PresetColorNames.Length; i++)
        {
            var btn = new View(Context);
            var c = Color.ParseColor(PresetColorHex[i]);
            var gd = new GradientDrawable();
            gd.SetShape(ShapeType.Oval);
            gd.SetColor(c);
            gd.SetStroke(dp, Color.ParseColor("#33FFFFFF"));
            btn.Background = gd;
            var bLp = new LinearLayout.LayoutParams(dp * 26, dp * 26);
            bLp.SetMargins(dp * 3, dp * 3, dp * 3, dp * 3);
            btn.LayoutParameters = bLp;
            btn.Clickable = true;
            btn.Focusable = true;
            var presetColor = c;
            btn.Click += (s, e) => { activePicker.SetColor(presetColor); };
            activePresetRow.AddView(btn);
        }
        var activePresetScroll = new HorizontalScrollView(Context);
        activePresetScroll.AddView(activePresetRow);
        activePresetScroll.HorizontalScrollBarEnabled = false;
        customColorContainer.AddView(activePresetScroll);

        // ---- 非当前行颜色（取色盘） ----
        var inactiveColorLabel = new TextView(Context) { Text = "非当前行颜色" };
        inactiveColorLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        inactiveColorLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        inactiveColorLabel.SetPadding(0, dp * 12, 0, dp * 4);
        customColorContainer.AddView(inactiveColorLabel);

        var inactivePicker = new ColorPickerView(Context);
        inactivePicker.SetColor(_lyricInactiveColor);
        customColorContainer.AddView(inactivePicker);

        // 快速预设色按钮
        var inactivePresetRow = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
        for (int i = 0; i < InactivePresetNames.Length; i++)
        {
            var btn = new View(Context);
            var c = Color.ParseColor(InactivePresetHex[i]);
            var gd = new GradientDrawable();
            gd.SetShape(ShapeType.Oval);
            gd.SetColor(c);
            gd.SetStroke(dp, Color.ParseColor("#33FFFFFF"));
            btn.Background = gd;
            var bLp = new LinearLayout.LayoutParams(dp * 26, dp * 26);
            bLp.SetMargins(dp * 3, dp * 3, dp * 3, dp * 3);
            btn.LayoutParameters = bLp;
            btn.Clickable = true;
            btn.Focusable = true;
            var presetColor = c;
            btn.Click += (s, e) => { inactivePicker.SetColor(presetColor); };
            inactivePresetRow.AddView(btn);
        }
        var inactivePresetScroll = new HorizontalScrollView(Context);
        inactivePresetScroll.AddView(inactivePresetRow);
        inactivePresetScroll.HorizontalScrollBarEnabled = false;
        customColorContainer.AddView(inactivePresetScroll);
        content.AddView(customColorContainer);

        // ---- 背景遮罩 ----
        var bgColorLabel = new TextView(Context) { Text = "背景遮罩" };
        bgColorLabel.SetTextColor(Color.ParseColor("#B0FFFFFF"));
        bgColorLabel.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
        bgColorLabel.SetPadding(0, dp * 12, 0, dp * 4);
        content.AddView(bgColorLabel);

        var rgBgColor = new RadioGroup(Context) { Orientation = Orientation.Horizontal };
        for (int i = 0; i < BgColorNames.Length; i++)
        {
            var rb = new RadioButton(Context) { Text = $"\u25CF {BgColorNames[i]}" };
            rb.SetTextSize(Android.Util.ComplexUnitType.Sp, 12f);
            rb.SetTextColor(Color.ParseColor("#DDFFFFFF"));
            rb.ButtonTintList = Android.Content.Res.ColorStateList.ValueOf(themeColor);
            rgBgColor.AddView(rb);
        }
        var initBgRb = rgBgColor.GetChildAt(Math.Clamp(_lyricBgColorIndex, 0, BgColorNames.Length - 1)) as RadioButton;
        if (initBgRb != null) rgBgColor.Check(initBgRb.Id);
        content.AddView(rgBgColor);

        cbDragSeek.CheckedChange += (s, e) => { _allowDragSeek = e.IsChecked; SaveSettings(); RebuildLyrics(); };
        rgColorMode.CheckedChange += (s, e) =>
        {
            var newMode = e.CheckedId == rbAdaptive.Id ? 0 : 1;
            if (newMode == _lyricColorMode) return;
            _lyricColorMode = newMode;
            customColorContainer.Visibility = _lyricColorMode == 0 ? ViewStates.Gone : ViewStates.Visible;
            SaveSettings();
            _lastLyricIndex = -999;
            _lastDuetPartnerIndex = -999;
            RebuildLyrics();
        };
        rgLyricStyle.CheckedChange += (s, e) =>
        {
            var newStyle = e.CheckedId == rbWordByWord.Id ? 1 : 0;
            if (newStyle == currentLyricStyle) return;
            currentLyricStyle = newStyle;
            _lyricStyle = newStyle;
            catclawPrefs?.Edit().PutInt("lyric_style", newStyle).Apply();
            var viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
            viewModel.LyricStyle = newStyle;
            viewModel.UpdateLyricSpannable();
            _lastLyricIndex = -999;
            _lastDuetPartnerIndex = -999;
            HighlightCurrentLine();
        };
        rgLyricsMode.CheckedChange += (s, e) =>
        {
            int newMode = -1;
            for (int i = 0; i < rgLyricsMode.ChildCount; i++)
            {
                if (rgLyricsMode.GetChildAt(i).Id == e.CheckedId)
                { newMode = i; break; }
            }
            if (newMode < 0 || newMode == currentLyricsMode) return;
            currentLyricsMode = newMode;
            catclawPrefs?.Edit().PutInt("lyrics_mode", newMode).Apply();
            var viewModel = MainApplication.Services.GetRequiredService<NowPlayingViewModel>();
            viewModel.LyricsMode = newMode;
            _ = viewModel.LoadLyricsAsync(viewModel.CurrentSong);
        };
        sbFontSize.ProgressChanged += (s, e) => { _lyricFontSize = e.Progress; tvFontSizeValue.Text = $"{_lyricFontSize}sp"; };
        sbFontSize.StopTrackingTouch += (s, e) => { SaveSettings(); RebuildLyrics(); };
        cbBold.CheckedChange += (s, e) => { _lyricBold = e.IsChecked; SaveSettings(); _lastLyricIndex = -999; _lastDuetPartnerIndex = -999; HighlightCurrentLine(); };
        rgAlignment.CheckedChange += (s, e) =>
        {
            var newAlign = e.CheckedId == rbLeft.Id ? 0 : e.CheckedId == rbRight.Id ? 2 : 1;
            if (newAlign == _lyricAlignment) return;
            _lyricAlignment = newAlign;
            SaveSettings(); RebuildLyrics();
        };

        // 取色盘事件（仅自定义模式下生效）
        activePicker.ColorChanged += (c) =>
        {
            if (_lyricColorMode != 1) return;
            _lyricActiveColor = c;
            _lyricActiveArgb = c.ToArgb();
            SaveSettings();
            _lastLyricIndex = -999;
            _lastDuetPartnerIndex = -999;
            HighlightCurrentLine();
        };
        inactivePicker.ColorChanged += (c) =>
        {
            if (_lyricColorMode != 1) return;
            _lyricInactiveColor = c;
            _lyricInactiveArgb = c.ToArgb();
            SaveSettings(); RebuildLyrics();
        };
        rgBgColor.CheckedChange += (s, e) =>
        {
            int newIdx = -1;
            for (int i = 0; i < rgBgColor.ChildCount; i++)
            {
                if (rgBgColor.GetChildAt(i).Id == e.CheckedId)
                { newIdx = i; break; }
            }
            if (newIdx < 0 || newIdx == _lyricBgColorIndex) return;
            _lyricBgColorIndex = newIdx;
            SaveSettings(); UpdateBgOverlay();
        };

        new GlassDialog(Context)
            .SetTitle("歌词设置")
            .AddCustomView(content)
            .AddNegativeButton("关闭")
            .Show();
    }

    /// <summary>
    /// 更新播放进度显示
    /// </summary>
    private void UpdateProgress()
    {
        var p = _viewModel.CurrentPosition;
        var d = _viewModel.TotalDuration;
        _progressText.Text = $"{p.Minutes}:{p.Seconds:D2} / {d.Minutes}:{d.Seconds:D2}";
    }

    /// <summary>
    /// Fragment恢复可见时调用
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        UpdateLyricsContainerPadding();
        UpdateProgress();
        _songTitle.Text = _viewModel.CurrentSong?.Title ?? "";
        _songArtist.Text = _viewModel.CurrentSong?.Artist ?? "";
        if (_lyricViews.Count == 0)
            RebuildLyrics();
        else
            HighlightCurrentLine();
        View?.Post(() => UpdateBackground());
    }

    /// <summary>
    /// 从外部通知 Fragment 需要滚动到当前歌词位置。
    /// 用于 Tab 切换时立即定位到当前播放位置，忽略用户之前的滚动状态。
    /// 重置 _userScrolling / _isDragging 状态，确保自动滚动不被跳过。
    /// </summary>
    public void ScrollToCurrentPosition()
    {
        // 清除用户滚动和拖拽状态，确保自动滚动可以执行
        _userScrolling = false;
        _isDragging = false;
        _draggedLyricIndex = -1;

        // 取消所有待执行的恢复回调，避免与本次滚动冲突
        _scrollResumeHandler.RemoveCallbacksAndMessages(null);
        _dragResumeHandler.RemoveCallbacksAndMessages(null);

        // 隐藏拖拽指示器（如果正在显示）
        HideDragIndicator();

        // 强制 HighlightCurrentLine 执行完整逻辑（否则 idx == _lastLyricIndex 时会跳过滚动）
        _lastLyricIndex = -1;
        _lastDuetPartnerIndex = -1;

        // 延迟执行：ViewPager2 切换页面时视图可能尚未完成布局，
        // ScrollView.Height 可能为 0 或 SmoothScrollTo 被后续布局覆盖。
        // 等待 300ms 让布局稳定后再滚动。
        new Handler(Looper.MainLooper!).PostDelayed(() =>
        {
            // 再次确保状态正确（延迟期间可能有其他事件修改了状态）
            _userScrolling = false;
            _lastLyricIndex = -1;
            _lastDuetPartnerIndex = -1;
            HighlightCurrentLine();
            // 兜底：直接调用 ScrollToCurrentLyric，确保滚动一定执行
            ScrollToCurrentLyric();
        }, 300);
    }

    /// <summary>
    /// Fragment销毁时清理资源
    /// </summary>
    public override void OnDestroyView() 
    { 
        UnbindViewModel();
        _scrollResumeHandler.RemoveCallbacksAndMessages(null); 
        _dragResumeHandler.RemoveCallbacksAndMessages(null);
        base.OnDestroyView(); 
    }

    /// <summary>
    /// 布局监听器，用于动态设置歌词容器的顶部padding
    /// </summary>
    private void UpdateLyricsContainerPadding()
    {
        if (_scrollView == null || _lyricsContainer == null) return;
        var scrollViewHeight = _scrollView.Height;
        if (scrollViewHeight <= 0) return;
        var padding = scrollViewHeight / 2;
        _lyricsContainer.SetPadding(
            _lyricsContainer.PaddingLeft,
            padding,
            _lyricsContainer.PaddingRight,
            padding
        );
    }

    private class OnGlobalLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly FullLyricsFragment _fragment;

        public OnGlobalLayoutListener(FullLyricsFragment fragment)
        {
            _fragment = fragment;
        }

        public void OnGlobalLayout()
        {
            try
            {
                _fragment._scrollView.ViewTreeObserver.RemoveOnGlobalLayoutListener(this);
                _fragment.UpdateLyricsContainerPadding();
                
                // 只更新歌词高亮，不完整重建，避免递归
                _fragment.HighlightCurrentLine();
            }
            catch (System.Exception)
            {
                // 捕获异常，避免崩溃
            }
        }
    }

    /// <summary>
    /// 拖拽触摸监听器
    /// 用于检测用户的拖拽手势
    /// </summary>
    private class DragTouchListener : Java.Lang.Object, View.IOnTouchListener
    {
        private readonly FullLyricsFragment _fragment;
        private float _startY = 0;
        private bool _hasDragged = false;

        public DragTouchListener(FullLyricsFragment fragment)
        {
            _fragment = fragment;
        }

        public bool OnTouch(View? v, MotionEvent? e)
        {
            if (e == null) return false;

            switch (e.Action)
            {
                case MotionEventActions.Down:
                    // 按下时记录起始位置
                    _fragment.OnTouchStart();
                    _startY = e.GetY();
                    _hasDragged = false;
                    break;

                case MotionEventActions.Move:
                    // 移动时检测是否超过拖拽阈值
                    var dy = Math.Abs(e.GetY() - _startY);
                    if (!_hasDragged && dy > 20)
                    {
                        _hasDragged = true;
                        _fragment.OnDragging();
                    }
                    else if (_hasDragged)
                    {
                        _fragment.OnDragging();
                    }
                    break;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    // 触摸结束时
                    _fragment.OnTouchEnd();
                    break;
            }
            return false;
        }
    }

}
