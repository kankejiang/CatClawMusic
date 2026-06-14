using System.Text.Json;
using System.Text.Encodings.Web;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using Android.Util;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 将艺术家元数据保存为 JSON 文件到公开存储目录，
/// 路径：/storage/emulated/0/CatClawMusic/metadata/{safe_artist_name}.json
/// </summary>
public static class ArtistMetadataSaver
{
    /// <summary>
    /// 将艺术家元数据保存到 JSON 文件。
    /// 如果 metadata 不为 null，则优先使用 metadata 的字段；否则使用 artist 的字段。
    /// </summary>
    public static async Task SaveAsync(Artist artist, ArtistSearchResult? metadata = null)
    {
        var dir = CatClawMusic.UI.MainApplication.MetadataDir;
        if (string.IsNullOrEmpty(dir))
            return;

        try
        {
            Directory.CreateDirectory(dir);

            var safeName = MakeSafeFileName(artist.Name);
            var filePath = Path.Combine(dir, $"{safeName}.json");

            var dto = new ArtistMetadataDto
            {
                // 基础信息
                Name = artist.Name,
                Gender = metadata?.Gender ?? artist.Gender ?? "",
                Region = metadata?.Region ?? artist.Region ?? "",
                Description = metadata?.Description ?? artist.Description ?? "",
                Birthday = metadata?.Birthday ?? artist.Birthday ?? "",

                // 扩展信息（百度百科等）
                RealName = metadata?.RealName ?? "",
                Nickname = metadata?.Nickname ?? "",
                Ethnicity = metadata?.Ethnicity ?? "",
                BirthPlace = metadata?.BirthPlace ?? "",
                Education = metadata?.Education ?? "",
                Zodiac = metadata?.Zodiac ?? "",
                Height = metadata?.Height ?? "",
                Agency = metadata?.Agency ?? "",
                RepresentativeWorks = metadata?.RepresentativeWorks ?? "",
                Occupation = metadata?.Occupation ?? "",
                CoverUrl = metadata?.CoverUrl ?? "",

                SavedAt = DateTime.UtcNow.ToString("o"),
            };

            var json = JsonSerializer.Serialize(dto, JsonOpts);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw", $"保存艺术家元数据失败: {artist.Name}, {ex.Message}");
        }
    }

    /// <summary>
    /// 从 JSON 文件加载艺术家元数据（如果存在）。
    /// </summary>
    public static async Task<ArtistMetadataDto?> LoadAsync(string artistName)
    {
        var dir = CatClawMusic.UI.MainApplication.MetadataDir;
        if (string.IsNullOrEmpty(dir))
            return null;

        try
        {
            var safeName = MakeSafeFileName(artistName);
            var filePath = Path.Combine(dir, $"{safeName}.json");
            if (!File.Exists(filePath))
                return null;

            var json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<ArtistMetadataDto>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("CatClaw", $"加载艺术家元数据失败: {artistName}, {ex.Message}");
            return null;
        }
    }

    /// <summary>生成安全的文件名（去除非法字符，截断过长名称）</summary>
    private static string MakeSafeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (invalid.Contains(c))
                sb.Append('_');
            else
                sb.Append(c);
        }

        var result = sb.ToString().Trim('_');
        // 文件名（含扩展名）不超过 100 字符，避免过长路径问题
        if (result.Length > 90)
            result = result[..90];
        return string.IsNullOrEmpty(result) ? "unknown" : result;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

/// <summary>艺术家元数据 DTO（用于 JSON 序列化，支持扩展字段）</summary>
public class ArtistMetadataDto
{
    // 基础信息
    public string Name { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Region { get; set; } = "";
    public string Description { get; set; } = "";
    public string Birthday { get; set; } = "";

    // 扩展信息（百度百科等丰富来源）
    public string RealName { get; set; } = "";           // 本名
    public string Nickname { get; set; } = "";             // 昵称
    public string Ethnicity { get; set; } = "";            // 民族
    public string BirthPlace { get; set; } = "";           // 出生地
    public string Education { get; set; } = "";            // 毕业院校
    public string Zodiac { get; set; } = "";               // 星座
    public string Height { get; set; } = "";               // 身高
    public string Agency { get; set; } = "";               // 经纪公司
    public string RepresentativeWorks { get; set; } = "";// 代表作品
    public string Occupation { get; set; } = "";           // 职业
    public string CoverUrl { get; set; } = "";

    public string SavedAt { get; set; } = "";
}
