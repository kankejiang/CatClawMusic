// resize_icon.csx - C# Script to resize app icon for Android
// Usage: dotnet script resize_icon.csx

using System;
using System.Drawing;
using System.Drawing.Imaging;

class IconResizer
{
    static void Main()
    {
        string sourcePath = @"D:\WorkBuddy\CatClawMusic\CatClawMusic.UI\Resources\app_icon.png";
        
        // Android mipmap densities and sizes
        var densities = new[]
        {
            new { Folder = @"D:\WorkBuddy\CatClawMusic\CatClawMusic.UI\Platforms\Android\Resources\mipmap-hdpi", Size = 72 },
            new { Folder = @"D:\WorkBuddy\CatClawMusic\CatClawMusic.UI\Platforms\Android\Resources\mipmap-xhdpi", Size = 96 },
            new { Folder = @"D:\WorkBuddy\CatClawMusic\CatClawMusic.UI\Platforms\Android\Resources\mipmap-xxhdpi", Size = 144 },
            new { Folder = @"D:\WorkBuddy\CatClawMusic\CatClawMusic.UI\Platforms\Android\Resources\mipmap-xxxhdpi", Size = 192 },
        };
        
        Console.WriteLine($"Reading source icon: {sourcePath}");
        
        using (var sourceImage = new Bitmap(sourcePath))
        {
            Console.WriteLine($"Source size: {sourceImage.Width}x{sourceImage.Height}");
            
            foreach (var density in densities)
            {
                string outputPath = System.IO.Path.Combine(density.Folder, "ic_launcher.png");
                
                Console.WriteLine($"Generating {density.Size}x{density.Size} -> {outputPath}");
                
                using (var resized = new Bitmap(sourceImage, new Size(density.Size, density.Size)))
                {
                    resized.Save(outputPath, ImageFormat.Png);
                }
            }
        }
        
        Console.WriteLine("Done! Icons resized successfully.");
    }
}
