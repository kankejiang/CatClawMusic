using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 全屏歌词页面Fragment
/// 功能：显示歌词、高亮当前行、支持拖拽调整播放位置
/// </summary>
public class FullLyricsFragment : Fragment
{
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
    private readonly List<TextView> _lyricViews = new();
    // 上一次高亮的歌词索引
    private int _lastLyricIndex = -1;
    // 用户是否正在手动滚动
    private bool _userScrolling;
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
    private int _lyricFontSize = 16;
    // 歌词对齐方式：0=左，1=中，2=右
    private int _lyricAlignment = 1;
    // 设置对话框
    private Android.App.Dialog? _settingsDialog;

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

        // 应用毛玻璃模糊效果
        ApplyBlur();
        // 设置滚动监听器
        SetupScrollListener();
        // 设置按钮点击事件
        _btnSettings.Click += (s, e) => ShowSettingsDialog();
        // 设置跳转按钮点击事件
        _btnJump.Click += (s, e) => OnJumpClicked();

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
                
                // 计算歌词视图的中心点Y坐标
                var lyricCenterY = lyricView.Top + lyricView.Height / 2;
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

        // 尝试加载自定义封面
        if (!string.IsNullOrEmpty(cover))
        {
            var drawable = Drawable.CreateFromPath(cover);
            if (drawable != null) { _bgCover.SetImageDrawable(drawable); return; }
        }
        // 使用默认封面
        _bgCover.SetImageResource(Resource.Drawable.cover_default);
    }

    /// <summary>
    /// 绑定ViewModel属性变化事件
    /// </summary>
    private void BindViewModel()
    {
        _viewModel.PropertyChanged += (s, e) => Activity?.RunOnUiThread(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(_viewModel.CurrentLyrics): 
                    RebuildLyrics(); // 歌词变化时重建
                    break;
                case nameof(_viewModel.CurrentLyricIndex): 
                    HighlightCurrentLine(); // 歌词索引变化时高亮
                    break;
                case nameof(_viewModel.CurrentPosition):
                case nameof(_viewModel.TotalDuration): 
                    UpdateProgress(); // 进度变化时更新
                    break;
                case nameof(_viewModel.CurrentSong): 
                    _songTitle.Text = _viewModel.CurrentSong?.Title ?? ""; 
                    _songArtist.Text = _viewModel.CurrentSong?.Artist ?? ""; 
                    break;
                case nameof(_viewModel.CoverSource): 
                    UpdateBackground(); // 封面变化时更新
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
    private void RebuildLyrics()
    {
        _lyricsContainer.RemoveAllViews(); 
        _lyricViews.Clear(); 
        _lastLyricIndex = -1;

        var lyrics = _viewModel.CurrentLyrics;
        if (lyrics?.Lines == null || lyrics.Lines.Count == 0)
        {
            // 显示"暂无歌词"
            var empty = new TextView(Context) { Text = "暂无歌词" };
            empty.SetTextSize(Android.Util.ComplexUnitType.Sp, _lyricFontSize);
            empty.SetTextColor(Color.ParseColor("#B0A8BA"));
            empty.Gravity = GetLyricGravity();
            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = 80; 
            empty.LayoutParameters = lp; 
            _lyricsContainer.AddView(empty); 
            return;
        }

        // 为每一行歌词创建TextView
        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            var line = lyrics.Lines[i];
            var tv = new TextView(Context) { Text = line.Text };
            tv.SetTextSize(Android.Util.ComplexUnitType.Sp, _lyricFontSize);
            tv.SetTextColor(Color.ParseColor("#CCCCCC"));
            tv.Gravity = GetLyricGravity();
            tv.SetLineSpacing(0, 1.4f);
            var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
            lp.TopMargin = i > 0 ? 16 : 0;
            tv.LayoutParameters = lp;
            tv.Tag = i;
            _lyricsContainer.AddView(tv); 
            _lyricViews.Add(tv);
        }

        HighlightCurrentLine();
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
    /// 高亮当前播放的歌词行
    /// </summary>
    private void HighlightCurrentLine()
    {
        var idx = _viewModel.CurrentLyricIndex;
        if (idx == _lastLyricIndex || _lyricViews.Count == 0) return;

        // 重置所有行的样式
        foreach (var v in _lyricViews)
        {
            v.SetTextSize(Android.Util.ComplexUnitType.Sp, _lyricFontSize);
            v.SetTextColor(Color.ParseColor("#CCCCCC"));
            v.Background = null;
        }

        // 高亮当前行
        if (idx >= 0 && idx < _lyricViews.Count)
        {
            _lyricViews[idx].SetTextSize(Android.Util.ComplexUnitType.Sp, _lyricFontSize + 4);
            _lyricViews[idx].SetTextColor(Color.ParseColor("#FFFFFF"));
        }

        _lastLyricIndex = idx;

        // 如果用户没有在滚动或拖拽，自动滚动到当前行
        if (!_userScrolling && !_isDragging)
        {
            ScrollToCurrentLyric();
        }
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
        
        Activity?.RunOnUiThread(() =>
        {
            try
            {
                // 获取ScrollView容器的高度
                var scrollViewHeight = _scrollView.Height;
                if (scrollViewHeight <= 0) return;
                
                // 计算歌词视图中心点相对于容器顶部的位置
                var lyricCenterY = t.Top + t.Height / 2;
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
        _lyricFontSize = _prefs.GetInt("lyric_font_size", 16);
        _lyricAlignment = _prefs.GetInt("lyric_alignment", 1);
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
        e.Apply();
    }

    /// <summary>
    /// 显示歌词设置对话框
    /// </summary>
    private void ShowSettingsDialog()
    {
        if (Context == null || Activity == null) return;

        // 创建对话框
        _settingsDialog = new Android.App.Dialog(Activity);
        _settingsDialog.RequestWindowFeature((int)WindowFeatures.NoTitle);
        _settingsDialog.SetContentView(Resource.Layout.dialog_lyric_settings);

        // 设置窗口背景透明
        var window = _settingsDialog.Window;
        if (window != null)
        {
            window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
            window.SetLayout((int)(Resources.DisplayMetrics.WidthPixels * 0.85), ViewGroup.LayoutParams.WrapContent);
        }

        // 获取控件引用
        var cbDragSeek = _settingsDialog.FindViewById<CheckBox>(Resource.Id.cb_drag_seek)!;
        var sbFontSize = _settingsDialog.FindViewById<SeekBar>(Resource.Id.sb_font_size)!;
        var tvFontSizeValue = _settingsDialog.FindViewById<TextView>(Resource.Id.tv_font_size_value)!;
        var rgAlignment = _settingsDialog.FindViewById<RadioGroup>(Resource.Id.rg_alignment)!;
        var rbLeft = _settingsDialog.FindViewById<RadioButton>(Resource.Id.rb_left)!;
        var rbCenter = _settingsDialog.FindViewById<RadioButton>(Resource.Id.rb_center)!;
        var rbRight = _settingsDialog.FindViewById<RadioButton>(Resource.Id.rb_right)!;
        var btnClose = _settingsDialog.FindViewById<Button>(Resource.Id.btn_close)!;

        // 初始化设置值
        cbDragSeek.Checked = _allowDragSeek;
        sbFontSize.Progress = _lyricFontSize;
        tvFontSizeValue.Text = $"{_lyricFontSize}sp";

        // 初始化对齐方式
        rbCenter.Checked = true;
        if (_lyricAlignment == 0) rbLeft.Checked = true;
        if (_lyricAlignment == 2) rbRight.Checked = true;

        // 设置控件事件
        cbDragSeek.CheckedChange += (s, e) => { _allowDragSeek = e.IsChecked; SaveSettings(); RebuildLyrics(); };
        sbFontSize.ProgressChanged += (s, e) => { _lyricFontSize = e.Progress; tvFontSizeValue.Text = $"{_lyricFontSize}sp"; };
        sbFontSize.StopTrackingTouch += (s, e) => { SaveSettings(); RebuildLyrics(); };
        rgAlignment.CheckedChange += (s, e) =>
        {
            _lyricAlignment = e.CheckedId switch
            {
                Resource.Id.rb_left => 0,
                Resource.Id.rb_right => 2,
                _ => 1
            };
            SaveSettings(); RebuildLyrics();
        };
        btnClose.Click += (s, e) => _settingsDialog.Dismiss();
        _settingsDialog.Show();
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
    public override void OnResume() { base.OnResume(); SyncUI(); }

    /// <summary>
    /// Fragment销毁时清理资源
    /// </summary>
    public override void OnDestroyView() 
    { 
        _scrollResumeHandler.RemoveCallbacksAndMessages(null); 
        _dragResumeHandler.RemoveCallbacksAndMessages(null);
        base.OnDestroyView(); 
    }

    /// <summary>
    /// 布局监听器，用于动态设置歌词容器的顶部padding
    /// </summary>
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
                // 移除监听器，避免重复调用
                _fragment._scrollView.ViewTreeObserver.RemoveOnGlobalLayoutListener(this);
                
                // 获取ScrollView的高度
                var scrollViewHeight = _fragment._scrollView.Height;
                if (scrollViewHeight <= 0) return;
                
                // 设置顶部padding，让第一句歌词从页面中央白色长条位置开始显示
                // scrollViewHeight/2 是ScrollView中心位置，这里直接用这个值
                var topPadding = scrollViewHeight / 2;
                _fragment._lyricsContainer.SetPadding(
                    _fragment._lyricsContainer.PaddingLeft,
                    topPadding,
                    _fragment._lyricsContainer.PaddingRight,
                    _fragment._lyricsContainer.PaddingBottom
                );
                
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
