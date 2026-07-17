using System.Text;
using System.Text.Json;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

public class MemoryItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Category { get; set; } = "preference";
    public string Content { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public int Importance { get; set; } = 5;
}

public class ChatMemoryService
{
    private const string MemoryFileName = "ai_memory.json";
    private const int MaxMemoryItems = 50;
    private const int ConversationChunkSize = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private List<MemoryItem> _memoryItems = new();
    private readonly List<ChatMessage> _recentConversation = new();
    private bool _isLoaded;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public static string MemoryPath => Path.Combine(FileSystem.AppDataDirectory, MemoryFileName);

    public ChatMemoryService()
    {
        _ = LoadFromDiskAsync();
    }

    private async Task LoadFromDiskAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (File.Exists(MemoryPath))
            {
                var json = await File.ReadAllTextAsync(MemoryPath, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    _memoryItems = JsonSerializer.Deserialize<List<MemoryItem>>(json, JsonOptions) ?? new List<MemoryItem>();
                }
            }
            _isLoaded = true;
        }
        catch (Exception ex)
        {
            Log.Debug("ChatMemoryService", $"[ChatMemory] 加载记忆失败：{ex.Message}");
            _memoryItems = new List<MemoryItem>();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveToDiskAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(MemoryPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_memoryItems, JsonOptions);
            await File.WriteAllTextAsync(MemoryPath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Log.Debug("ChatMemoryService", $"[ChatMemory] 保存记忆失败：{ex.Message}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public void RecordMessage(ChatMessage msg)
    {
        if (msg.Role == "tool") return;
        if (string.IsNullOrWhiteSpace(msg.Content)) return;

        _recentConversation.Add(msg);

        if (_recentConversation.Count >= ConversationChunkSize * 2)
        {
            _ = AnalyzeAndExtractMemoryAsync();
        }
    }

    public async Task AnalyzeAndExtractMemoryAsync(Func<string, string, Task<string>>? aiAnalyzer = null)
    {
        if (_recentConversation.Count < 4) return;
        if (aiAnalyzer == null)
        {
            _recentConversation.Clear();
            return;
        }

        var conversationToAnalyze = new List<ChatMessage>(_recentConversation);
        _recentConversation.Clear();

        try
        {
            var convText = FormatConversationForAnalysis(conversationToAnalyze);
            var existingMemory = FormatMemoryForPrompt();

            var systemPrompt = @"你是一个用户画像分析助手。请从用户与AI助手的对话中提取值得长期记住的重要信息。

需要提取的信息类别：
1. user_profile - 用户基本信息（年龄、性别、职业、所在地等）
2. preference - 用户偏好（喜欢的音乐风格、歌手、歌曲类型等）
3. personal - 个人情况（心情、状态、生活事件等）
4. habit - 使用习惯（常用功能、操作偏好等）

规则：
- 只提取明确提到或强烈暗示的信息，不要猜测
- 如果没有值得记住的重要信息，返回空JSON数组
- 信息要简洁具体，每条10-30字
- importance: 1-10分，非常重要的给8-10分，一般偏好给5-7分，次要信息给1-4分
- 如果新信息与已有记忆重复或更新已有记忆，返回的条目id使用已有的id

返回严格的JSON数组格式，不要任何其他文字：
[{""id"":""新生成8位id或已有id"",""category"":""类别"",""content"":""记忆内容"",""importance"":数字}]";

            var userPrompt = $"【已有记忆】\n{existingMemory}\n\n【最近对话】\n{convText}\n\n请从以上对话中提取重要信息：";

            var result = await aiAnalyzer(systemPrompt, userPrompt);
            if (!string.IsNullOrWhiteSpace(result))
            {
                await ProcessExtractedMemoryAsync(result);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("ChatMemoryService", $"[ChatMemory] 记忆提取失败：{ex.Message}");
        }
    }

    private string FormatConversationForAnalysis(List<ChatMessage> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages.TakeLast(ConversationChunkSize * 2))
        {
            var role = msg.Role == "assistant" ? "Yuki" : "用户";
            sb.AppendLine($"{role}: {msg.Content}");
        }
        return sb.ToString();
    }

    private string FormatMemoryForPrompt()
    {
        if (_memoryItems.Count == 0) return "（暂无记忆）";

        var sb = new StringBuilder();
        foreach (var item in _memoryItems.OrderByDescending(m => m.Importance).Take(20))
        {
            var category = item.Category switch
            {
                "user_profile" => "用户画像",
                "preference" => "偏好",
                "personal" => "个人情况",
                "habit" => "习惯",
                _ => "其他"
            };
            sb.AppendLine($"- [{category}] {item.Content} (重要性:{item.Importance})");
        }
        return sb.ToString();
    }

    private async Task ProcessExtractedMemoryAsync(string jsonResult)
    {
        try
        {
            var start = jsonResult.IndexOf('[');
            var end = jsonResult.LastIndexOf(']');
            if (start < 0 || end <= start) return;

            var json = jsonResult.Substring(start, end - start + 1);
            var newItems = JsonSerializer.Deserialize<List<MemoryItem>>(json, JsonOptions);
            if (newItems == null || newItems.Count == 0) return;

            await _lock.WaitAsync();
            try
            {
                bool hasChanges = false;
                foreach (var item in newItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Content)) continue;

                    var existing = _memoryItems.FirstOrDefault(m => m.Id == item.Id);
                    if (existing != null)
                    {
                        existing.Content = item.Content;
                        existing.Category = item.Category;
                        existing.Importance = item.Importance;
                        existing.UpdatedAt = DateTime.Now;
                        hasChanges = true;
                    }
                    else
                    {
                        var duplicate = _memoryItems.FirstOrDefault(m =>
                            m.Category == item.Category &&
                            m.Content.Equals(item.Content, StringComparison.OrdinalIgnoreCase));
                        if (duplicate == null)
                        {
                            _memoryItems.Add(item);
                            hasChanges = true;
                        }
                    }
                }

                if (_memoryItems.Count > MaxMemoryItems)
                {
                    _memoryItems = _memoryItems
                        .OrderByDescending(m => m.Importance)
                        .ThenByDescending(m => m.UpdatedAt ?? m.CreatedAt)
                        .Take(MaxMemoryItems)
                        .ToList();
                    hasChanges = true;
                }

                if (hasChanges)
                {
                    await SaveToDiskAsync();
                }
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception ex)
        {
            Log.Debug("ChatMemoryService", $"[ChatMemory] 处理提取记忆失败：{ex.Message}");
        }
    }

    public async Task AddMemoryAsync(string category, string content, int importance = 5)
    {
        await _lock.WaitAsync();
        try
        {
            var existing = _memoryItems.FirstOrDefault(m =>
                m.Category == category &&
                m.Content.Equals(content, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Importance = Math.Max(existing.Importance, importance);
                existing.UpdatedAt = DateTime.Now;
            }
            else
            {
                _memoryItems.Add(new MemoryItem
                {
                    Category = category,
                    Content = content,
                    Importance = importance
                });
            }

            await SaveToDiskAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public string LoadMemory()
    {
        if (!_isLoaded)
        {
            try
            {
                if (File.Exists(MemoryPath))
                {
                    var json = File.ReadAllText(MemoryPath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        _memoryItems = JsonSerializer.Deserialize<List<MemoryItem>>(json, JsonOptions) ?? new List<MemoryItem>();
                    }
                }
                _isLoaded = true;
            }
            catch { }
        }

        if (_memoryItems.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("以下是关于用户的长期记忆：");

        var categories = new Dictionary<string, string>
        {
            { "user_profile", "【用户画像】" },
            { "preference", "【音乐偏好】" },
            { "personal", "【个人情况】" },
            { "habit", "【使用习惯】" }
        };

        foreach (var cat in categories)
        {
            var items = _memoryItems
                .Where(m => m.Category == cat.Key)
                .OrderByDescending(m => m.Importance)
                .ToList();

            if (items.Count > 0)
            {
                sb.AppendLine(cat.Value);
                foreach (var item in items.Take(10))
                {
                    sb.AppendLine($"- {item.Content}");
                }
            }
        }

        return sb.ToString();
    }

    public List<MemoryItem> GetAllMemories()
    {
        return _memoryItems.OrderByDescending(m => m.Importance).ToList();
    }

    public async Task DeleteMemoryAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            var item = _memoryItems.FirstOrDefault(m => m.Id == id);
            if (item != null)
            {
                _memoryItems.Remove(item);
                await SaveToDiskAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearMemoryAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _memoryItems.Clear();
            _recentConversation.Clear();
            if (File.Exists(MemoryPath))
                File.Delete(MemoryPath);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ForceMemoryExtractionAsync(Func<string, string, Task<string>> aiAnalyzer)
    {
        if (_recentConversation.Count >= 2)
        {
            await AnalyzeAndExtractMemoryAsync(aiAnalyzer);
        }
    }
}
