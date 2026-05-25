using Android.Content;
using Android.Graphics;
using Android.Util;
using Android.Views;

namespace CatClawMusic.UI.Helpers;

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

    public void SetGradient(int[] colors, float[] positions)
    {
        _colors = colors;
        _positions = positions;
        UpdateShader();
    }

    public void UpdateColors(int[] colors)
    {
        _colors = colors;
        UpdateShader();
    }

    public void SetRotationAngle(float angleDegrees)
    {
        _matrix.Reset();
        _matrix.SetRotate(angleDegrees, _cx, _cy);
        _shader?.SetLocalMatrix(_matrix);
        Invalidate();
    }

    private void UpdateShader()
    {
        if (_colors == null || _positions == null || Width <= 0 || Height <= 0) return;

        _cx = Width * 0.5f;
        _cy = Height * 0.28f;

        _shader = new SweepGradient(_cx, _cy, _colors, _positions);
        _paint.SetShader(_shader);
        _matrix.Reset();
        _matrix.SetRotate(0, _cx, _cy);
        _shader.SetLocalMatrix(_matrix);
        Invalidate();
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        if (w > 0 && h > 0 && _colors != null)
            UpdateShader();
    }

    protected override void OnDraw(Canvas canvas)
    {
        if (_shader != null)
            canvas.DrawPaint(_paint);
    }
}
