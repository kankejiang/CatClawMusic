using Android.Content;
using Android.Util;
using Android.Views;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 正方形 FrameLayout，强制宽高相等（取较小值）
/// </summary>
public class SquareFrameLayout : FrameLayout
{
    public SquareFrameLayout(Context context) : base(context) { }
    public SquareFrameLayout(Context context, IAttributeSet attrs) : base(context, attrs) { }
    public SquareFrameLayout(Context context, IAttributeSet attrs, int defStyleAttr) : base(context, attrs, defStyleAttr) { }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        int w = MeasureSpec.GetSize(widthMeasureSpec);
        int h = MeasureSpec.GetSize(heightMeasureSpec);

        // LinearLayout weight 第一轮测量：高度为 0，先透传给 base 让子控件正确测量
        if (w == 0 || h == 0)
        {
            base.OnMeasure(widthMeasureSpec, heightMeasureSpec);
            return;
        }

        // 取宽高的较小值作为正方形边长，子控件也会按正方形约束测量
        int size = Math.Min(w, h);
        int exactSpec = MeasureSpec.MakeMeasureSpec(size, MeasureSpecMode.Exactly);
        base.OnMeasure(exactSpec, exactSpec);
    }
}
