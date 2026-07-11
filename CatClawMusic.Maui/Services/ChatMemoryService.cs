using System.Text;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// AI 聊天长期记忆服务，将对话记录持久化到 Markdown 文件
/// </summary>
public class ChatMemoryService
{
    /// <summary>
    /// 记忆文件名
    /// </summary>
    private const string MemoryFileName = "ai_memory.md";

    /// <summary>
    /// 获取记忆文件完整路径
    /// </summary>
    public static string MemoryPath => Path.Combine(FileSystem.AppDataDirectory, MemoryFileName);

    /// <summary>
    /// 异步追加一条聊天消息到记忆文件
    /// </summary>
    /// <param name="msg">聊天消息</param>
    public async Task AppendMessageAsync(ChatMessage msg)
    {
        try
        {
            if (msg.Role == "tool") return;

            var roleName = msg.Role == "assistant" ? "Yuki" : "大人";
            var line = $"[{DateTime.Now:HH:mm}] {roleName}: {msg.Content}";

            var dir = Path.GetDirectoryName(MemoryPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.AppendAllTextAsync(MemoryPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatMemory] 追加消息失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 异步追加一条重要长期记忆（带日期标记）
    /// </summary>
    /// <param name="summary">记忆摘要</param>
    public async Task AppendImportantMemoryAsync(string summary)
    {
        try
        {
            var line = $"## {DateTime.Now:yyyy-MM-dd}\n{summary}\n";

            var dir = Path.GetDirectoryName(MemoryPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await File.AppendAllTextAsync(MemoryPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatMemory] 追加重要记忆失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 读取记忆内容，文件不存在时返回空字符串
    /// </summary>
    /// <returns>记忆 Markdown 内容</returns>
    public static string LoadMemory()
    {
        try
        {
            if (File.Exists(MemoryPath))
            {
                return File.ReadAllText(MemoryPath, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatMemory] 读取记忆失败：{ex.Message}");
        }
        return string.Empty;
    }
}
