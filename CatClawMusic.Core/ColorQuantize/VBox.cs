namespace CatClawMusic.Core.ColorQuantize;

// https://github.com/lokesh/quantize/blob/master/src/quantize.js

internal record VBox(PixelRGB Min, PixelRGB Max, int Volume, int Count, PixelRGB Avg)
{
    public static VBox FromHisto(PixelRGB min, PixelRGB max, int[] histo) => 
        new VBox(min, max, 
            calcVolume(min, max), 
            calcCount(min, max, histo), 
            calcAvg(min, max, histo));

    private static int calcVolume(PixelRGB min, PixelRGB max) => 
        (max.R - min.R + 1) * (max.G - min.G + 1) * (max.B - min.B + 1);

    private static int calcCount(PixelRGB min, PixelRGB max, int[] histo)
    {
        int npix = 0;
        for (int i = min.R; i <= max.R; i++)
        {
            for (int j = min.G; j <= max.G; j++)
            {
                for (int k = min.B; k <= max.B; k++)
                {
                    int index = Mmcq.GetColorIndex(i, j, k);
                    npix += histo[index];
                }
            }
        }
        return npix;
    }

    private static PixelRGB calcAvg(PixelRGB min, PixelRGB max, int[] histo)
    {
        int ntot = 0;
        double mult = 1 << (8 - Mmcq.Sigbits);
        double rsum = 0, gsum = 0, bsum = 0;

        if (min.Equals(max))
        {
            return new PixelRGB(
                (byte)(min.R * mult),
                (byte)(min.G * mult),
                (byte)(min.B * mult));
        }
        else
        {
            for (int r = min.R; r <= max.R; r++)
            {
                for (int g = min.G; g <= max.G; g++)
                {
                    for (int b = min.B; b <= max.B; b++)
                    {
                        int histoindex = Mmcq.GetColorIndex(r, g, b);
                        int hval = histo[histoindex];
                        ntot += hval;
                        rsum += hval * (r + 0.5) * mult;
                        gsum += hval * (g + 0.5) * mult;
                        bsum += hval * (b + 0.5) * mult;
                    }
                }
            }

            return ntot > 0
                ? new PixelRGB((byte)(rsum / ntot), (byte)(gsum / ntot), (byte)(bsum / ntot))
                : new PixelRGB((byte)(mult * (min.R + max.R + 1) / 2), (byte)(mult * (min.G + max.G + 1) / 2), (byte)(mult * (min.B + max.B + 1) / 2));
        }
    }

    public bool Contains(PixelRGB pixel)
    {
        int rval = pixel.R >> Mmcq.Rshift;
        int gval = pixel.G >> Mmcq.Rshift;
        int bval = pixel.B >> Mmcq.Rshift;

        return (rval >= Min.R && rval <= Max.R &&
                gval >= Min.G && gval <= Max.G &&
                bval >= Min.B && bval <= Max.B);
    }
}

internal class VBoxComparer : IComparer<VBox>
{
    // 내림차순 정렬
    public int Compare(VBox x, VBox y) => y.Count.CompareTo(x.Count);
}

internal class VBoxVolumeComparer : IComparer<VBox>
{
    // 내림차순 정렬
    public int Compare(VBox x, VBox y)
    {
        long xValue = (long)x.Count * x.Volume;
        long yValue = (long)y.Count * y.Volume;
        return yValue.CompareTo(xValue);
    }
}