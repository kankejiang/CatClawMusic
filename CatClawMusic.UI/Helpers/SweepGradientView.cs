using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 扫描渐变背景视图，用于播放页面的动态渐变背景效果
/// <para>使用 Android SweepGradient（扫描/扇形渐变）从中心点向外辐射渐变色</para>
/// <para>支持动态更新颜色、旋转角度，常配合封面取色结果生成沉浸式背景</para>
/// </summary>
public class SweepGradientView : View
{
    private SweepGradient? _shader;
    private readonly Paint _paint;
    private readonly Matrix _matrix;
    private float _cx, _cy;
    private int[]? _colors;
    private float[]? _positions;

    public SweepGradientView(Context context) : base(context)
    {
        _paint = new Paint { Dither = true };
        _matrix = new Matrix();
        SetLayerType(LayerType.Hardware, null);
    }

    public SweepGradientView(Context context, IAttributeSet attrs) : base(context, attrs)
    {
        _paint = new Paint { Dither = true };
        _matrix = new Matrix();
        SetLayerType(LayerType.Hardware, null);
    }

    public SweepGradientView(Context context, IAttributeSet attrs, int defStyleAttr) : base(context, attrs, defStyleAttr)
    {
        _paint = new Paint { Dither = true };
        _matrix = new Matrix();
        SetLayerType(LayerType.Hardware, null);
    }

    /// <summary>设置渐变颜色和位置分布</summary>
    public void SetGradient(int[] colors, float[] positions)
    {
        _colors = colors;
        _positions = positions;
        UpdateShader();
    }

    /// <summary>仅更新渐变颜色（保持位置分布不变）</summary>
    public void UpdateColors(int[] colors)
    {
        _colors = colors;
        UpdateShader();
    }

    /// <summary>设置渐变旋转角度（度数），通过矩阵变换旋转整个渐变</summary>
    public void SetRotationAngle(float angleDegrees)
    {
        _matrix.Reset();
        _matrix.SetRotate(angleDegrees, _cx, _cy);
        _shader?.SetLocalMatrix(_matrix);
        Invalidate();
    }

    /// <summary>重新创建 SweepGradient 着色器并应用到画笔</summary>
    private void UpdateShader()
    {
        if (_colors == null || _positions == null || Width <= 0 || Height <= 0) return;

        // 渐变中心：水平居中，垂直偏上（0.28），使渐变效果更贴合播放页布局
        _cx = Width * 0.5f;
        _cy = Height * 0.28f;

        _shader = new SweepGradient(_cx, _cy, _colors, _positions);
        _paint.SetShader(_shader);
        _matrix.Reset();
        _matrix.SetRotate(0, _cx, _cy);
        _shader.SetLocalMatrix(_matrix);
        Invalidate();
    }

    /// <summary>视图尺寸变化时重新创建着色器</summary>
    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        if (w > 0 && h > 0 && _colors != null)
            UpdateShader();
    }

    /// <summary>使用着色器画笔绘制渐变背景覆盖整个画布</summary>
    protected override void OnDraw(Canvas canvas)
    {
        if (_shader != null)
            canvas.DrawPaint(_paint);
    }
}
