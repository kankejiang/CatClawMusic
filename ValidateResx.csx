using System;
using System.IO;
using System.Xml;

string resourcesDir = @"c:\Code\CatClawMusic\CatClawMusic.Maui\Resources";

string[] resxFiles = Directory.GetFiles(resourcesDir, "AppResources.*.resx");

int successCount = 0;
int failCount = 0;

foreach (string filePath in resxFiles)
{
    string fileName = Path.GetFileName(filePath);
    try
    {
        XmlDocument doc = new XmlDocument();
        doc.Load(filePath);
        
        if (doc.DocumentElement != null && doc.DocumentElement.Name == "root")
        {
            Console.WriteLine($"✓ {fileName} - XML格式正确，根元素: root");
            successCount++;
        }
        else
        {
            Console.WriteLine($"✗ {fileName} - 根元素不正确");
            failCount++;
        }
    }
    catch (XmlException ex)
    {
        Console.WriteLine($"✗ {fileName} - XML解析错误: {ex.Message}");
        failCount++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"✗ {fileName} - 错误: {ex.Message}");
        failCount++;
    }
}

Console.WriteLine();
Console.WriteLine($"========================================");
Console.WriteLine($"验证完成: 成功 {successCount} 个，失败 {failCount} 个");
