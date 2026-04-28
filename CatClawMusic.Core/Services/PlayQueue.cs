using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 播放队列（支持顺序、随机、循环等模式）
/// </summary>
public class PlayQueue
{
    private List<Song> _originalList = new();  // 原始列表（不变）
    private List<Song> _shuffledList = new();  // 洗牌后的列表（随机模式使用）
    private int _currentIndex = -1;
    private Stack<int> _history = new();  // 历史记录（支持"上一曲"）
    private PlayMode _playMode = PlayMode.Sequential;
    
    /// <summary>
    /// 当前播放模式
    /// </summary>
    public PlayMode PlayMode
    {
        get => _playMode;
        set
        {
            _playMode = value;
            if (_playMode == PlayMode.Shuffle && _shuffledList.Count == 0)
            {
                EnableShuffle();
            }
        }
    }
    
    /// <summary>
    /// 当前歌曲
    /// </summary>
    public Song? CurrentSong
    {
        get
        {
            if (_currentIndex < 0) return null;
            
            return PlayMode == PlayMode.Shuffle
                ? _shuffledList.ElementAtOrDefault(_currentIndex)
                : _originalList.ElementAtOrDefault(_currentIndex);
        }
    }
    
    /// <summary>
    /// 设置播放列表
    /// </summary>
    public void SetSongs(IEnumerable<Song> songs)
    {
        _originalList = songs.ToList();
        _shuffledList = new List<Song>();
        _currentIndex = -1;
        _history.Clear();
        
        if (PlayMode == PlayMode.Shuffle)
        {
            EnableShuffle();
        }
    }
    
    /// <summary>
    /// 开启随机播放（洗牌）
    /// </summary>
    public void EnableShuffle()
    {
        _shuffledList = ShuffleService.Shuffle(_originalList);
        _currentIndex = 0;
        _history.Clear();
    }
    
    /// <summary>
    /// 下一首
    /// </summary>
    public Song? Next()
    {
        if (_originalList.Count == 0) return null;
        
        // 记录当前位置到历史
        if (_currentIndex >= 0)
        {
            _history.Push(_currentIndex);
        }
        
        // 计算下一首索引
        _currentIndex = GetNextIndex();
        
        // 如果遍历完，根据播放模式决定
        if (_currentIndex == -1) return null;
        
        return CurrentSong;
    }
    
    /// <summary>
    /// 上一曲
    /// </summary>
    public Song? Previous()
    {
        if (_history.Count == 0) return CurrentSong;
        
        _currentIndex = _history.Pop();
        return CurrentSong;
    }
    
    /// <summary>
    /// 用户手动选择某首歌
    /// </summary>
    public void SelectSong(int songId)
    {
        if (_currentIndex >= 0)
        {
            _history.Push(_currentIndex);
        }
        
        if (PlayMode == PlayMode.Shuffle)
        {
            _currentIndex = _shuffledList.FindIndex(s => s.Id == songId);
        }
        else
        {
            _currentIndex = _originalList.FindIndex(s => s.Id == songId);
        }
    }
    
    /// <summary>
    /// 获取下一首索引
    /// </summary>
    private int GetNextIndex()
    {
        var list = PlayMode == PlayMode.Shuffle ? _shuffledList : _originalList;
        
        if (PlayMode == PlayMode.SingleRepeat)
        {
            return _currentIndex;  // 单曲循环，返回当前
        }
        
        int nextIndex = _currentIndex + 1;
        
        if (nextIndex >= list.Count)
        {
            if (PlayMode == PlayMode.ListRepeat)
            {
                nextIndex = 0;
            }
            else if (PlayMode == PlayMode.Shuffle)
            {
                _shuffledList = ShuffleService.Shuffle(_originalList);
                nextIndex = 0;
            }
            else
            {
                return -1;
            }
        }
        
        return nextIndex;
    }

    /// <summary>
    /// 获取接下来 N 首预播歌曲（不改变队列状态）
    /// </summary>
    public List<Song> GetUpcomingSongs(int count = 3)
    {
        var upcoming = new List<Song>();
        if (_originalList.Count == 0 || _currentIndex < 0) return upcoming;

        var list = PlayMode == PlayMode.Shuffle ? _shuffledList : _originalList;
        var peekIdx = _currentIndex;

        for (int i = 0; i < count; i++)
        {
            if (PlayMode == PlayMode.SingleRepeat)
            {
                upcoming.Add(list[peekIdx]);
                continue;
            }

            peekIdx++;
            if (peekIdx >= list.Count)
            {
                if (PlayMode == PlayMode.ListRepeat || PlayMode == PlayMode.Shuffle)
                    peekIdx = 0;
                else
                    break;
            }
            upcoming.Add(list[peekIdx]);
        }

        return upcoming;
    }

    /// <summary>
    /// 获取当前播放列表
    /// </summary>
    public IReadOnlyList<Song> GetSongs()
    {
        return PlayMode == PlayMode.Shuffle ? _shuffledList.AsReadOnly() : _originalList.AsReadOnly();
    }
}

/// <summary>
/// 洗牌服务（Fisher-Yates 算法）
/// </summary>
public static class ShuffleService
{
    private static Random _random = new();
    
    /// <summary>
    /// Fisher-Yates 洗牌算法
    /// </summary>
    public static List<T> Shuffle<T>(IList<T> list)
    {
        var result = list.ToList();
        for (int i = result.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }
}

/// <summary>
/// 播放模式
/// </summary>
public enum PlayMode
{
    Sequential,      // 顺序播放
    Shuffle,         // 随机播放
    SingleRepeat,    // 单曲循环
    ListRepeat       // 列表循环
}
