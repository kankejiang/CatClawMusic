namespace CatClawMusic.Core.ColorQuantize;

// https://github.com/lokesh/quantize/blob/master/src/quantize.js

public class Mmcq
{
    public static IColorPalette Quantize(IEnumerable<PixelRGB> pixels, int maxColors)
    {
        var mmcq = new Mmcq();
        return mmcq.InternalQuantize(pixels, maxColors);
    }

    internal const int Sigbits = 5;
    internal const int HistoSize = 1 << (3 * Sigbits);
    internal const int Rshift = 8 - Sigbits;
    private const int MaxIterations = 1000;
    private const double FractByPopulation = 0.75;

    internal static int GetColorIndex(int r, int g, int b) =>
        (r << (2 * Sigbits)) | (g << Sigbits) | b;

    private readonly HashSet<PixelRGB> _uniqueColors = new();
    private readonly int[] _histo = new int[HistoSize];

    private IColorPalette InternalQuantize(IEnumerable<PixelRGB> pixels, int maxColors)
    {
        if (maxColors < 1 || maxColors > 256)
            throw new ArgumentException("Max colors must be between 1 and 256.", nameof(maxColors));
        if (pixels == null || !pixels.Any())
            throw new ArgumentNullException(nameof(pixels));

        byte rmin = 255, rmax = 0;
        byte gmin = 255, gmax = 0;
        byte bmin = 255, bmax = 0;

        foreach (var pixel in pixels)
        {
            byte rval = (byte)(pixel.R >> Rshift);
            byte gval = (byte)(pixel.G >> Rshift);
            byte bval = (byte)(pixel.B >> Rshift);

            int colorIndex = GetColorIndex(rval, gval, bval);
            _histo[colorIndex]++;

            rmin = Math.Min(rmin, rval);
            rmax = Math.Max(rmax, rval);
            gmin = Math.Min(gmin, gval);
            gmax = Math.Max(gmax, gval);
            bmin = Math.Min(bmin, bval);
            bmax = Math.Max(bmax, bval);

            _uniqueColors.Add(pixel);
        }

        if (_uniqueColors.Count <= maxColors)
            return new SimpleColorMap(_uniqueColors);

        var vbox = VBox.FromHisto(new PixelRGB(rmin, gmin, bmin), new PixelRGB(rmax, gmax, bmax), _histo);

        var pq = new PQueue<VBox>(new VBoxComparer());
        pq.Push(vbox);

        Iter(pq, FractByPopulation * maxColors);

        var pq2 = new PQueue<VBox>(new VBoxVolumeComparer());
        while (pq.Size() > 0)
        {
            pq2.Push(pq.Pop());
        }

        Iter(pq2, maxColors);

        var finalVBoxes = new List<VBox>();
        while (pq2.Size() > 0)
        {
            finalVBoxes.Add(pq2.Pop());
        }

        return new CMap(finalVBoxes);
    }

    private void Iter(PQueue<VBox> pq, double target)
    {
        int niters = 0;
        while (niters < MaxIterations)
        {
            if (pq.Size() >= target) return;
            if (niters++ > MaxIterations) return;
            if (pq.Size() == 0) return;

            var vbox = pq.Pop();
            if (vbox.Count == 0)
            {
                pq.Push(vbox);
            }
            else if (vbox.Count == 1)
            {
                pq.Push(vbox);
            }
            else
            {
                var (vbox1, vbox2) = MedianCutApply(vbox);
                pq.Push(vbox1);
                pq.Push(vbox2);
            }
        }
    }

    private (VBox, VBox) MedianCutApply(VBox vbox)
    {
        if (vbox.Count < 2)
            throw new InvalidOperationException("VBox must have at least 2 colors");

        int rw = vbox.Max.R - vbox.Min.R + 1;
        int gw = vbox.Max.G - vbox.Min.G + 1;
        int bw = vbox.Max.B - vbox.Min.B + 1;
        int maxw = Math.Max(rw, Math.Max(gw, bw));
        
        int total = 0;
        var partialsum = new Dictionary<int, int>();
        var lookaheadsum = new Dictionary<int, int>();

        if (maxw == rw)
        {
            for (int i = vbox.Min.R; i <= vbox.Max.R; i++)
            {
                int sum = 0;
                for (int j = vbox.Min.G; j <= vbox.Max.G; j++)
                {
                    for (int k = vbox.Min.B; k <= vbox.Max.B; k++)
                    {
                        int index = GetColorIndex(i, j, k);
                        sum += _histo[index];
                    }
                }
                total += sum;
                partialsum[i] = total;
            }
        }
        else if (maxw == gw)
        {
            for (int i = vbox.Min.G; i <= vbox.Max.G; i++)
            {
                int sum = 0;
                for (int j = vbox.Min.R; j <= vbox.Max.R; j++)
                {
                    for (int k = vbox.Min.B; k <= vbox.Max.B; k++)
                    {
                        int index = GetColorIndex(j, i, k);
                        sum += _histo[index];
                    }
                }
                total += sum;
                partialsum[i] = total;
            }
        }
        else // maxw == bw
        {
            for (int i = vbox.Min.B; i <= vbox.Max.B; i++)
            {
                int sum = 0;
                for (int j = vbox.Min.R; j <= vbox.Max.R; j++)
                {
                    for (int k = vbox.Min.G; k <= vbox.Max.G; k++)
                    {
                        int index = GetColorIndex(j, k, i);
                        sum += _histo[index];
                    }
                }
                total += sum;
                partialsum[i] = total;
            }
        }

        foreach (var entry in partialsum)
        {
            lookaheadsum[entry.Key] = total - entry.Value;
        }

        char dim = maxw == rw ? 'r' : maxw == gw ? 'g' : 'b';
        return DoCut(dim, vbox, partialsum, lookaheadsum, total);
    }

    private (VBox, VBox) DoCut(char dim, VBox vbox, Dictionary<int, int> partialsum, Dictionary<int, int> lookaheadsum, int total)
    {
        int dim1_val = 0, dim2_val = 0;
        switch(dim)
        {
            case 'r': dim1_val = vbox.Min.R; dim2_val = vbox.Max.R; break;
            case 'g': dim1_val = vbox.Min.G; dim2_val = vbox.Max.G; break;
            case 'b': dim1_val = vbox.Min.B; dim2_val = vbox.Max.B; break;
        }

        for (int i = dim1_val; i <= dim2_val; i++)
        {
            if (partialsum.ContainsKey(i) && partialsum[i] > total / 2)
            {
                int left = i - dim1_val;
                int right = dim2_val - i;
                int d2;

                if (left <= right)
                    d2 = Math.Min(dim2_val - 1, (int)(i + right / 2.0));
                else
                    d2 = Math.Max(dim1_val, (int)(i - 1 - left / 2.0));

                while (d2 < dim2_val && !partialsum.ContainsKey(d2)) d2++;
                
                int count2 = lookaheadsum.TryGetValue(d2, out count2) ? count2 : 0;
                while (count2 == 0 && d2 > dim1_val && partialsum.ContainsKey(d2-1))
                {
                    d2--;
                    lookaheadsum.TryGetValue(d2, out count2);
                }

                VBox vbox1, vbox2;
                byte v1 = (byte)Math.Min(d2, byte.MaxValue);
                byte v2 = (byte)Math.Min(d2 + 1, byte.MaxValue);
                switch(dim)
                {
                    case 'r':
                        vbox1 = VBox.FromHisto(vbox.Min, vbox.Max.WithR(v1), _histo);
                        vbox2 = VBox.FromHisto(vbox.Min.WithR(v2), vbox.Max, _histo);
                        break;
                    case 'g':
                        vbox1 = VBox.FromHisto(vbox.Min, vbox.Max.WithG(v1), _histo);
                        vbox2 = VBox.FromHisto(vbox.Min.WithG(v2), vbox.Max, _histo);
                        break;
                    case 'b':
                        vbox1 = VBox.FromHisto(vbox.Min, vbox.Max.WithB(v1), _histo);
                        vbox2 = VBox.FromHisto(vbox.Min.WithB(v2), vbox.Max, _histo);
                        break;
                    default:
                        throw new InvalidOperationException("Invalid dimension");
                }
                return (vbox1, vbox2);
            }
        }
        throw new InvalidOperationException("No cut found");
    }
}