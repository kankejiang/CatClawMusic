namespace CatClawMusic.Core.ColorQuantize;

public readonly struct PixelRGBA
{
    public PixelRGBA(byte r, byte g, byte b, byte a)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    public readonly byte R;
    public readonly byte G;
    public readonly byte B;
    public readonly byte A;

    public override bool Equals(object? obj) => obj is PixelRGBA other && R == other.R && G == other.G && B == other.B && A == other.A;
    public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
}

public readonly struct PixelRGB
{
    public readonly byte R;
    public readonly byte G;
    public readonly byte B;

    public PixelRGB(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
    }

    public override bool Equals(object? obj) => obj is PixelRGB other && R == other.R && G == other.G && B == other.B;
    public override int GetHashCode() => (R << 16) | (G << 8) | B;
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";

    public PixelRGB WithR(byte r) => new(r, G, B);
    public PixelRGB WithG(byte g) => new(R, g, B);
    public PixelRGB WithB(byte b) => new(R, G, b);
}