namespace CatClawMusic.Core.ColorQuantize;

// 集成自 ColorThiefSharp (github.com/AlphaBs/ColorThiefSharp)，纯 .NET Standard 2.0、
// 零依赖的 color-thief median-cut 移植。MIT License。用于从封面提取主导色（雾面着色）。
public class ColorThief
{
    public class SamplingOptions
    {
        public int Quality { get; set; } = 10;
        public byte MinimumAlpha { get; set; } = 125;
        public byte MaximumRed { get; set; } = 250;
        public byte MaximumGreen { get; set; } = 250;
        public byte MaximumBlue { get; set; } = 250;
    }

    private static void validateOptions(int colorCount, SamplingOptions options)
    {
        if (colorCount < 2 || colorCount > 20)
        {
            throw new ArgumentException("colorCount should be between 2 and 20.");
        }

        if (options.Quality < 1)
        {
            throw new ArgumentException("quality should be greater than 0.");
        }
    }

    public static PixelRGB GetColor(IEnumerable<PixelRGBA> pixels)
    {
        return GetColor(pixels, new SamplingOptions());
    }

    public static PixelRGB GetColor(IEnumerable<PixelRGBA> pixels, SamplingOptions options)
    {
        return GetPalette(pixels, 1, options).First();
    }

    public static IReadOnlyCollection<PixelRGB> GetPalette(IEnumerable<PixelRGBA> pixels, int colorCount = 10)
    {
        return GetPalette(pixels, colorCount, new SamplingOptions());
    }

    public static IReadOnlyCollection<PixelRGB> GetPalette(IEnumerable<PixelRGBA> pixels, int colorCount, SamplingOptions options)
    {
        validateOptions(colorCount, options);
        var rgbPixels = createPixelArray(pixels, options).ToList();
        var colorPalette = Mmcq.Quantize(rgbPixels, colorCount);
        return colorPalette;
    }

    private static IEnumerable<PixelRGB> createPixelArray(IEnumerable<PixelRGBA> pixels, SamplingOptions options)
    {
        int offset = 0;
        foreach (var pixel in pixels)
        {
            if (offset % options.Quality == 0)
            {
                if (pixel.A >= options.MinimumAlpha && !(pixel.R > options.MaximumRed && pixel.G > options.MaximumGreen && pixel.B > options.MaximumBlue))
                {
                    yield return new PixelRGB(pixel.R, pixel.G, pixel.B);
                }
            }
            offset++;
        }
    }
}
