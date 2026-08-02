using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

// 记录 first-chance 异常（模拟 VS 输出窗口「引发的异常」）
var firstChanceCount = 0;
AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
{
    firstChanceCount++;
    Console.WriteLine($"    [FirstChance] {e.Exception.GetType().Name}: {e.Exception.Message}");
};

if (args.Length == 0)
{
    Console.WriteLine("usage: HeadParseTest <file> [file...]");
    return;
}

foreach (var file in args)
{
    if (!File.Exists(file)) { Console.WriteLine($"missing: {file}"); continue; }
    var full = File.ReadAllBytes(file);
    Console.WriteLine($"\n===== {Path.GetFileName(file)}  full={full.Length} bytes =====");

    foreach (var headSize in new[] { 256 * 1024, 2 * 1024 * 1024 })
    {
        if (full.Length <= headSize) { Console.WriteLine($"head={headSize / 1024}KB: 文件全长 {full.Length} 无需截断（等价整文件）"); }
        firstChanceCount = 0;
        var head = full.Take(Math.Min(headSize, full.Length)).ToArray();
        using var ms = new MemoryStream(head);
        Song? song;
        try
        {
            song = TagReader.ReadFromStream(ms, file, Path.GetFileName(file), full.Length);
        }
        catch (Exception ex)
        {
            song = null;
            Console.WriteLine($"head={headSize / 1024}KB -> EXCEPTION {ex.GetType().Name}: {ex.Message} | firstChance={firstChanceCount}");
        }
        if (song != null)
            Console.WriteLine($"head={headSize / 1024}KB -> Title={song.Title} | Artist={song.Artist} | Album={song.Album} | Dur={song.Duration}s | firstChance={firstChanceCount}");

        // 模拟修复：头解析无有效元数据（未知艺术家/未知专辑/无时长）→ 下载尾部 8MB 手动解析 moov
        var needsTail = song == null
            || (song.Artist == "未知艺术家" && song.Album == "未知专辑" && song.Duration <= 0);
        if (needsTail)
        {
            var tailSize = Math.Min(8 * 1024 * 1024, full.Length);
            var tail = full.Skip((int)(full.Length - tailSize)).Take((int)tailSize).ToArray();
            firstChanceCount = 0;
            var meta = M4aMetadataReader.ReadAllFromTail(tail, full.Length);
            Console.WriteLine($"    tail(8MB) -> {(meta == null ? "NULL" : $"Title={meta.Title} | Artist={meta.Artist} | Album={meta.Album} | Dur={meta.DurationSeconds}s | Bitrate={meta.Bitrate}")} | firstChance={firstChanceCount}");
        }

        // 封面提取（与 GetCoverAsync 内嵌回退同路径）
        firstChanceCount = 0;
        var ms2 = new MemoryStream(head);
        try
        {
            var cover = TagReader.ExtractCoverFromStream(ms2, Path.GetFileName(file));
            Console.WriteLine($"    cover(head={headSize / 1024}KB) -> {(cover != null ? $"{cover.Length} bytes" : "null")} | firstChance={firstChanceCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    cover(head={headSize / 1024}KB) -> EXCEPTION {ex.GetType().Name}: {ex.Message} | firstChance={firstChanceCount}");
        }
        finally { ms2.Dispose(); }
    }
}
