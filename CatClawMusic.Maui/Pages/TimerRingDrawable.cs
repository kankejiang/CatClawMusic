using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using static CatClawMusic.Maui.Controls.PopupUiHelpers;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using System.Collections.Generic;
using System.IO;

namespace CatClawMusic.Maui.Pages;

/// <summary>NowPlayingPage 底部操作栏功能：定时关闭 / 均衡器 / 切换横屏 / 更多</summary>

public class TimerRingDrawable : IDrawable
{
    /// <summary>剩余比例 0~1</summary>
    public float Progress { get; set; } = 1f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var cx = dirtyRect.Width / 2;
        var cy = dirtyRect.Height / 2;
        var r = Math.Min(cx, cy) - 10;

        // 轨道
        canvas.StrokeColor = new Color(1, 1, 1, 0.12f);
        canvas.StrokeSize = 9;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawEllipse(cx - r, cy - r, r * 2, r * 2);

        // 进度弧（从12点方向顺时针）
        if (Progress > 0.001f)
        {
            var sweep = Progress * 360f;
            canvas.StrokeColor = Color.FromArgb("#8C7BFF");
            canvas.StrokeSize = 9;
            canvas.StrokeLineCap = LineCap.Round;
            // DrawArc: 角度0=3点钟方向，逆时针为正 → 从90°(12点)开始，顺时针扫过 sweep
            canvas.DrawArc(cx - r, cy - r, r * 2, r * 2, 90f, 90f - sweep, true, false);
        }
    }
}
