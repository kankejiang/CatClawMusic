#if WINDOWS
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
using WColor = Windows.UI.Color;
using WPoint = Windows.Foundation.Point;
using WGrid = Microsoft.UI.Xaml.Controls.Grid;
using WImage = Microsoft.UI.Xaml.Controls.Image;
using WRectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using WStretch = Microsoft.UI.Xaml.Media.Stretch;
using WHorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using WVerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;
using WSolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using MColor = Microsoft.Maui.Graphics.Color;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Platforms.Windows;

/// <summary>
/// Windows 端雾面动态背景 Handler。
/// PC 专用：封面经 Win2D GPU 真·高斯模糊（雾面玻璃质感）+ 缓慢漂移/缩放/旋转动画，
/// 模拟手机端 FrostedBackground 的流光溢彩效果。电脑性能更强，分辨率提到 512、
/// 模糊半径放大，雾面更细腻。Win2D 失败自动回退 CPU 盒式模糊，保证任何环境都不崩。
/// </summary>
public class FrostedBackgroundHandler : ViewHandler<Controls.FrostedBackground, WGrid>
{
    public static IPropertyMapper<Controls.FrostedBackground, FrostedBackgroundHandler> Mapper =
        new PropertyMapper<Controls.FrostedBackground, FrostedBackgroundHandler>(ViewMapper)
        {
            [nameof(Controls.FrostedBackground.Source)] = MapSource,
            [nameof(Controls.FrostedBackground.IsActive)] = MapIsActive,
            [nameof(Controls.FrostedBackground.TintColor)] = MapTint,
            [nameof(Controls.FrostedBackground.TintOpacity)] = MapTint,
            [nameof(Controls.FrostedBackground.DimAmount)] = MapTint,
            [nameof(Controls.FrostedBackground.Aspect)] = MapAspect,
            [nameof(Controls.FrostedBackground.IsScrolling)] = MapIsScrolling,
        };

    private WImage? _image;
    private WRectangle? _tintOverlay;
    private WRectangle? _dimOverlay;
    private DispatcherTimer? _timer;
    private double _animTime;
    private bool _isActive = true;
    private bool _hasSource;
    private volatile bool _isScrolling;  // 用户正在滑动列表（暂停动画）

    private readonly Random _random = new();
    private readonly float _driftAX;
    private readonly float _driftAY;
    private readonly float _driftBX;
    private readonly float _driftBY;
    private readonly float _driftSpeed;
    private readonly float _breathSpeed;
    private readonly float _breathAmount;
    // 旋转改为有界振荡（±_rotationAmp 度），避免长会话累加转角越过安全角后角上露出底色。
    private readonly float _rotationAmp;
    private readonly float _rotationFreq;

    private static readonly Dictionary<string, WriteableBitmap> _cache = new();

    public FrostedBackgroundHandler() : base(Mapper)
    {
        _driftAX = 0.16f + (float)_random.NextDouble() * 0.10f;
        _driftAY = 0.14f + (float)_random.NextDouble() * 0.09f;
        _driftBX = 0.10f + (float)_random.NextDouble() * 0.07f;
        _driftBY = 0.12f + (float)_random.NextDouble() * 0.08f;
        _driftSpeed = 0.14f + (float)_random.NextDouble() * 0.07f;
        _breathSpeed = 0.18f + (float)_random.NextDouble() * 0.10f;
        _breathAmount = 0.05f + (float)_random.NextDouble() * 0.035f;
        _rotationAmp = 9f + (float)_random.NextDouble() * 6f;        // 9° ~ 15°
        _rotationFreq = 0.06f + (float)_random.NextDouble() * 0.04f; // 慢摆
    }

    protected override WGrid CreatePlatformView()
    {
        // 底色作为无封面/解码失败时的兜底；正常情况模糊图放大 1.7 倍 + 有界旋转，永不露底。
        var grid = new WGrid
        {
            Background = new WSolidColorBrush(WColor.FromArgb(255, 11, 13, 32)),
        };
        _image = new WImage
        {
            Stretch = WStretch.UniformToFill,
            HorizontalAlignment = WHorizontalAlignment.Center,
            VerticalAlignment = WVerticalAlignment.Center,
            RenderTransformOrigin = new WPoint(0.5, 0.5),
            RenderTransform = new CompositeTransform(),
        };
        _tintOverlay = new WRectangle { IsHitTestVisible = false };
        _dimOverlay = new WRectangle { IsHitTestVisible = false };
        grid.Children.Add(_image);
        grid.Children.Add(_tintOverlay);
        grid.Children.Add(_dimOverlay);
        return grid;
    }

    protected override void DisconnectHandler(WGrid platformView)
    {
        StopAnimation();
        _image = null;
        _tintOverlay = null;
        _dimOverlay = null;
        base.DisconnectHandler(platformView);
    }

    private static void MapSource(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        handler.LoadSourceAsync(view.Source, view.CacheKey);
    }

    private static void MapIsActive(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        // PC 端忽略 IsActive（其绑定 IsPlaying）：雾面动态背景应常驻显示并漂移动画，
        // 不随播放状态显隐。真正的启用开关由控件 IsVisible(FrostedBackgroundEnabled) 决定。
        handler._isActive = view.IsActive;
        handler.UpdateAnimationState();
    }

    private static void MapIsScrolling(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        handler._isScrolling = view.IsScrolling;
        handler.UpdateAnimationState();
    }

    private static void MapTint(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        handler.UpdateTint(view.TintColor, view.TintOpacity, view.DimAmount);
    }

    private static void MapAspect(FrostedBackgroundHandler handler, Controls.FrostedBackground view)
    {
        if (handler._image == null) return;
        handler._image.Stretch = view.Aspect switch
        {
            Microsoft.Maui.Aspect.AspectFit => WStretch.Uniform,
            Microsoft.Maui.Aspect.Fill => WStretch.Fill,
            _ => WStretch.UniformToFill,
        };
    }

    private async void LoadSourceAsync(Microsoft.Maui.Controls.ImageSource? source, string? cacheKey)
    {
        _hasSource = false;
        try
        {
            WriteableBitmap? bitmap = null;
            if (!string.IsNullOrEmpty(cacheKey) && _cache.TryGetValue(cacheKey, out var cached))
            {
                bitmap = cached;
            }
            else
            {
                bitmap = await ProcessSourceAsync(source);
                if (bitmap != null && !string.IsNullOrEmpty(cacheKey))
                    _cache[cacheKey] = bitmap;
            }

            if (_image != null && bitmap != null)
            {
                _image.Source = bitmap;
                _hasSource = true;
                UpdateAnimationState();
            }
        }
        catch (Exception ex)
        {
            Log.Debug("FrostedBackgroundHandler", $"[FrostedBackground] Windows load failed: {ex.Message}");
        }
    }

    private static async Task<WriteableBitmap?> ProcessSourceAsync(Microsoft.Maui.Controls.ImageSource? source)
    {
        byte[]? bytes = null;
        if (source is FileImageSource fileSource && !string.IsNullOrEmpty(fileSource.File))
        {
            bytes = await File.ReadAllBytesAsync(fileSource.File);
        }
        else if (source is IStreamImageSource streamSource)
        {
            using var s = await streamSource.GetStreamAsync(System.Threading.CancellationToken.None);
            if (s != null)
            {
                using var ms = new MemoryStream();
                await s.CopyToAsync(ms);
                bytes = ms.ToArray();
            }
        }
        else if (source is UriImageSource uriSource)
        {
            using var client = new System.Net.Http.HttpClient();
            bytes = await client.GetByteArrayAsync(uriSource.Uri);
        }

        if (bytes == null || bytes.Length == 0) return null;

        // PC 端分辨率提到 512：电脑性能更强，高分辨率让雾面更细腻；移动端走另一套实现。
        int smallW = 512, smallH = 0;
        byte[]? buf;
        try
        {
            using var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(bytes.AsBuffer());
            ras.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(ras);
            double ratio = decoder.PixelWidth / (double)decoder.PixelHeight;
            smallW = 512;
            smallH = Math.Max(1, (int)(smallW / ratio));

            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)smallW,
                ScaledHeight = (uint)smallH,
                InterpolationMode = BitmapInterpolationMode.Linear
            };

            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            buf = data.DetachPixelData();
        }
        catch (Exception ex)
        {
            Log.Debug("FrostedBackgroundHandler", $"[FrostedBackground] decode failed: {ex.Message}");
            return null;
        }

        // CPU 盒式模糊 + 色调增强：512 分辨率（PC 端更高品质），两次盒式逼近高斯，细腻雾面。
        // 注：曾尝试 Win2D GPU 高斯，但像素回读管线在本环境产出异常位图（黑图/不随封面变色），
        // 无法无 GUI 调试，故回退到确定稳定的 CPU 实现。
        return ProcessWithCpu(buf, smallW, smallH);
    }

    /// <summary>CPU 盒式模糊 + 色调增强，输出细腻雾面。</summary>
    private static WriteableBitmap ProcessWithCpu(byte[] buf, int w, int h)
    {
        BoxBlur(buf, w, h, Math.Max(8, w / 8));
        BoxBlur(buf, w, h, Math.Max(4, w / 16));
        AdjustTone(buf, 1.6f, 1.12f);

        var wb = new WriteableBitmap(w, h);
        using (Stream stream = wb.PixelBuffer.AsStream())
        {
            stream.Write(buf, 0, buf.Length);
        }
        wb.Invalidate();
        return wb;
    }

    private static void BoxBlur(byte[] pixels, int w, int h, int radius)
    {
        if (radius <= 0) return;
        var temp = new byte[pixels.Length];
        int window = radius * 2 + 1;

        // Horizontal
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int x = -radius; x <= radius; x++)
                {
                    int px = Math.Clamp(x, 0, w - 1);
                    sum += pixels[(row + px) * 4 + c];
                }
                for (int x = 0; x < w; x++)
                {
                    temp[(row + x) * 4 + c] = (byte)(sum / window);
                    int left = Math.Clamp(x - radius, 0, w - 1);
                    int right = Math.Clamp(x + radius + 1, 0, w - 1);
                    sum += pixels[(row + right) * 4 + c] - pixels[(row + left) * 4 + c];
                }
            }
        }

        // Vertical
        Array.Copy(temp, pixels, pixels.Length);
        for (int x = 0; x < w; x++)
        {
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int y = -radius; y <= radius; y++)
                {
                    int py = Math.Clamp(y, 0, h - 1);
                    sum += pixels[(py * w + x) * 4 + c];
                }
                for (int y = 0; y < h; y++)
                {
                    temp[(y * w + x) * 4 + c] = (byte)(sum / window);
                    int top = Math.Clamp(y - radius, 0, h - 1);
                    int bot = Math.Clamp(y + radius + 1, 0, h - 1);
                    sum += pixels[(bot * w + x) * 4 + c] - pixels[(top * w + x) * 4 + c];
                }
            }
        }
        Array.Copy(temp, pixels, pixels.Length);
    }

    private static void AdjustTone(byte[] pixels, float saturation, float brightness)
    {
        float rWeight = 0.299f, gWeight = 0.587f, bWeight = 0.114f;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte a = pixels[i + 3];

            float rf = r * brightness;
            float gf = g * brightness;
            float bf = b * brightness;

            float lum = rf * rWeight + gf * gWeight + bf * bWeight;
            rf = lum + (rf - lum) * saturation;
            gf = lum + (gf - lum) * saturation;
            bf = lum + (bf - lum) * saturation;

            pixels[i] = (byte)Math.Clamp(bf, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(gf, 0, 255);
            pixels[i + 2] = (byte)Math.Clamp(rf, 0, 255);
            pixels[i + 3] = a;
        }
    }

    private void UpdateTint(MColor tintColor, double tintOpacity, double dimAmount)
    {
        if (_tintOverlay != null)
        {
            byte a = (byte)Math.Clamp(tintOpacity * tintColor.Alpha * 255, 0, 255);
            _tintOverlay.Fill = new WSolidColorBrush(WColor.FromArgb(a, (byte)(tintColor.Red * 255), (byte)(tintColor.Green * 255), (byte)(tintColor.Blue * 255)));
        }
        if (_dimOverlay != null)
        {
            byte a = (byte)Math.Clamp(dimAmount * 255, 0, 255);
            _dimOverlay.Fill = new WSolidColorBrush(WColor.FromArgb(a, 0, 0, 0));
        }
    }

    private void UpdateAnimationState()
    {
        // PC 端：雾面动态背景常驻漂移动画（电脑性能足够），不随播放状态停。
        // 仅用户滑动列表时暂停，释放 UI 线程给列表渲染。
        if (_hasSource && !_isScrolling)
            StartAnimation();
        else
            StopAnimation();
    }

    private void StartAnimation()
    {
        if (_timer != null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void StopAnimation()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void OnTick(object? sender, object e)
    {
        if (_image?.Source == null) return;
        _animTime += 0.04;

        float t = (float)(_animTime * _driftSpeed);
        float driftX = (float)(_driftAX * Math.Sin(t * 0.7 + _driftBX) + _driftBX * Math.Sin(t * 1.8 + _driftAX));
        float driftY = (float)(_driftAY * Math.Cos(t * 0.6 + _driftBY) + _driftBY * Math.Cos(t * 1.6 + _driftAY));
        float breath = 1f + _breathAmount * (float)Math.Sin(_animTime * _breathSpeed * 2.0 * Math.PI);
        // 旋转改为有界振荡（±_rotationAmp°）：长会话下转角永不越过安全角，角上不会露出底色。
        float rotation = _rotationAmp * (float)Math.Sin(_animTime * _rotationFreq * 2.0 * Math.PI);

        if (_image.RenderTransform is CompositeTransform ct)
        {
            // 基础缩放 1.72 倍 + 呼吸：1.72 * cos(15°) > 1，漂移/旋转时绝不露底。
            ct.ScaleX = ct.ScaleY = 1.72f * breath;
            // 漂移范围控制在缩放余量内（PC 端幅度调大，动感更明显）
            ct.TranslateX = driftX * _image.ActualWidth * 0.16;
            ct.TranslateY = driftY * _image.ActualHeight * 0.16;
            ct.Rotation = rotation;
        }
    }
}
#endif
