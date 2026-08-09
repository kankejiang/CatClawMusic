using CatClawMusic.Maui.Controls;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace CatClawMusic.Maui.Platforms.Windows;

public class SwapChainHostHandler : ViewHandler<SwapChainHost, CanvasSwapChainPanel>
{
    private CanvasDevice? _device;
    private CanvasSwapChain? _swapChain;

    public SwapChainHostHandler() : base(new PropertyMapper<SwapChainHost>()) { }

    public CanvasSwapChain? SwapChain => _swapChain;

    protected override CanvasSwapChainPanel CreatePlatformView()
        => new CanvasSwapChainPanel();

    protected override void ConnectHandler(CanvasSwapChainPanel platformView)
    {
        base.ConnectHandler(platformView);
        platformView.SizeChanged += OnPanelSizeChanged;
    }

    protected override void DisconnectHandler(CanvasSwapChainPanel platformView)
    {
        platformView.SizeChanged -= OnPanelSizeChanged;
        base.DisconnectHandler(platformView);
    }

    private void OnPanelSizeChanged(object? sender, Microsoft.UI.Xaml.SizeChangedEventArgs e)
    {
        try
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
            {
                EnsureSwapChain();
            }
        }
        catch { }
    }

    public void EnsureSwapChain()
    {
        if (PlatformView == null) return;

        var scale = PlatformView.XamlRoot?.RasterizationScale ?? 1.0;
        var w = (float)(PlatformView.ActualWidth * scale);
        var h = (float)(PlatformView.ActualHeight * scale);
        var dpi = (float)(96.0 * scale);

        if (w < 1 || h < 1) return;

        _device ??= CanvasDevice.GetSharedDevice();

        if (_swapChain == null)
        {
            _swapChain = new CanvasSwapChain(_device, w, h, dpi);
            PlatformView.SwapChain = _swapChain;
        }
        else if (Math.Abs(_swapChain.Size.Width - w) > 0.5f || Math.Abs(_swapChain.Size.Height - h) > 0.5f)
        {
            _swapChain.ResizeBuffers(w, h, dpi);
        }
    }

    public void Render(Action<CanvasDrawingSession> draw)
    {
        if (_swapChain == null || PlatformView == null) return;

        try
        {
            using (var ds = _swapChain.CreateDrawingSession(global::Windows.UI.Color.FromArgb(0, 0, 0, 0)))
            {
                draw(ds);
            }
            _swapChain.Present();
        }
        catch { }
    }
}