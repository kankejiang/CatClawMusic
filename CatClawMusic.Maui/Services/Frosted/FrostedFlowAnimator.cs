using System;

namespace CatClawMusic.Maui.Services.Frosted;

/// <summary>
/// 流光动画计时器（Halcyon 的 animTime + colorStage 简化版），两端共用同一套推进逻辑。
/// - animTime：累计动画秒，暂停时冻结（滑动/非激活时把 playing 置 false 即暂停）；
/// - colorStage：颜色阶段数，随 animTime/周期 前进并带一阶滞后，制造"停留后过渡"的感觉。
///   颜色数组由 <see cref="FrostedFlowProcessor.InterpolateColors"/> 按 colorStage 取值。
/// </summary>
public sealed class FrostedFlowAnimator
{
    private readonly float _period;
    private readonly float _lag;      // 滞后系数：越大跟得越快

    /// <summary>累计动画秒（可读给 Render 做光点运动）</summary>
    public float AnimTime { get; private set; }

    /// <summary>当前颜色阶段数（可带小数）</summary>
    public float ColorStage { get; private set; }

    public FrostedFlowAnimator(float colorInterpPeriod, float lag = 3.0f)
    {
        _period = colorInterpPeriod > 0f ? colorInterpPeriod : 5f;
        _lag = lag;
    }

    /// <summary>
    /// 全局动画减速系数：光点漂移 / 色彩阶段 / 封面流旋转都乘它。
    /// &lt;1 时整体变慢。调这里一处，Android 与 Windows 两端同时生效。
    /// </summary>
    public const float TimeScale = 0.45f;

    /// <summary>推进一帧。dt 为增量秒；playing 为 false 时动画冻结。</summary>
    public void Advance(float dt, bool playing)
    {
        if (dt <= 0f) return;
        if (playing) AnimTime += dt * TimeScale;

        // 目标阶段 = 时间/周期 + 1（Halcyon 保持 target 领先一拍，用 spring 逼近）
        float target = AnimTime / _period + 1f;
        float coeff = 1f - (float)Math.Exp(-_lag * dt);
        ColorStage += (target - ColorStage) * coeff;
    }

    /// <summary>重置计时（可选的初始化同步）。</summary>
    public void Reset()
    {
        AnimTime = 0f;
        ColorStage = 0f;
    }
}