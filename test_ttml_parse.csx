// 测试 TTML 对唱歌词解析
#load "CatClawMusic.Core/Services/LyricsService.cs"
#load "CatClawMusic.Core/Models/LrcLyrics.cs"

using CatClawMusic.Core.Services;
using CatClawMusic.Core.Models;
using System;

var service = new LyricsService();
var ttmlContent = File.ReadAllText("test_duet.ttml");
var result = service.ParseTtml(ttmlContent);

if (result == null)
{
    Console.WriteLine("解析失败：返回 null");
}
else
{
    Console.WriteLine($"解析成功：共 {result.Lines.Count} 行");
    Console.WriteLine($"HasPerLineAlignment: {result.HasPerLineAlignment}");
    
    for (int i = 0; i < result.Lines.Count; i++)
    {
        var line = result.Lines[i];
        Console.WriteLine($"[{i}] Time={line.Timestamp}, Text='{line.Text}', Alignment={line.Alignment}, Translation='{line.Translation}'");
        
        if (line.WordTimestamps != null)
        {
            Console.WriteLine($"    WordTimestamps: {line.WordTimestamps.Count} 个");
            foreach (var wt in line.WordTimestamps)
            {
                Console.WriteLine($"      '{wt.Word}' Start={wt.Start} Duration={wt.Duration}");
            }
        }
    }
}
