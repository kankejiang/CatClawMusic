using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 播放队列（支持顺序、随机、循环等模式）
/// </summary>
public class PlayQueue
{
    /// <summary>原始播放列表（顺序模式使用）</summary>
    private List<Song> _originalList = new();
    /// <summary>洗牌后的播放列表（随机模式使用）</summary>
    private List<Song> _shuffledList = new();
    /// <summary>当前播放索引</summary>
    private int _currentIndex = -1;
    /// <summary>播放历史记录栈（支持"上一曲"）</summary>
    private Stack<int> _history = new();
    /// <summary>当前播放模式</summary>
    private PlayMode _playMode = PlayMode.ListRepeat;
    /// <summary>歌曲 ID 到索引的映射（O(1) 查找）</summary>
    private Dictionary<int, int> _songIdToIndex = new();

    /// <summary>
    /// 当前播放模式
    /// </summary>
    public PlayMode PlayMode
    {
        get => _playMode;
        set
        {
            var oldMode = _playMode;
            _playMode = value;

            if (_playMode == PlayMode.Shuffle && _shuffledList.Count == 0)
            {
                EnableShuffle();
            }
            else if (oldMode == PlayMode.Shuffle && _playMode != PlayMode.Shuffle && _currentIndex >= 0)
            {
                var currentSong = _shuffledList.ElementAtOrDefault(_currentIndex);
                if (currentSong != null && _songIdToIndex.TryGetValue(currentSong.Id, out var idx))
                {
                    _currentIndex = idx;
                }
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

            var list = PlayMode == PlayMode.Shuffle ? _shuffledList : _originalList;
            return _currentIndex < list.Count ? list[_currentIndex] : null;
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
        RebuildIndex();

        if (PlayMode == PlayMode.Shuffle)
        {
            EnableShuffle();
        }
    }

    /// <summary>
    /// 重建歌曲 ID 到索引的映射
    /// </summary>
    private void RebuildIndex()
    {
        _songIdToIndex.Clear();
        for (int i = 0; i < _originalList.Count; i++)
            _songIdToIndex[_originalList[i].Id] = i;
    }
    
    /// <summary>
    /// 开启随机播放（洗牌），保持当前歌曲在洗牌列表中的位置
    /// </summary>
    public void EnableShuffle()
    {
        var currentSong = _currentIndex >= 0 ? _originalList.ElementAtOrDefault(_currentIndex) : null;
        _shuffledList = ShuffleService.Shuffle(_originalList);
        _history.Clear();

        if (currentSong != null)
        {
            var idx = _shuffledList.FindIndex(s => s.Id == currentSong.Id);
            if (idx >= 0)
            {
                _shuffledList.RemoveAt(idx);
                _shuffledList.Insert(0, currentSong);
            }
            _currentIndex = 0;
        }
        else
        {
            _currentIndex = 0;
        }
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

        var song = CurrentSong;
        if (song == null)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayQueue] Next 得到越界索引，已重置: mode={PlayMode}, index={_currentIndex}, original={_originalList.Count}, shuffled={_shuffledList.Count}");
            _currentIndex = -1;
        }
        return song;
    }
    
    /// <summary>
    /// 上一曲
    /// </summary>
    public Song? Previous()
    {
        if (_history.Count == 0) return CurrentSong;

        var idx = _history.Pop();
        var list = PlayMode == PlayMode.Shuffle ? _shuffledList : _originalList;
        if (idx < 0 || idx >= list.Count)
        {
            System.Diagnostics.Debug.WriteLine($"[PlayQueue] Previous 历史索引越界: mode={PlayMode}, idx={idx}, list.Count={list.Count}");
            return CurrentSong;
        }
        _currentIndex = idx;
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

        if (PlayMode == PlayMode.Shuffle && _shuffledList.Count > 0)
        {
            var idx = _shuffledList.FindIndex(s => s.Id == songId);
            _currentIndex = idx >= 0 ? idx : -1;
        }
        else
        {
            if (_songIdToIndex.TryGetValue(songId, out var idx))
            {
                _currentIndex = idx;
            }
            else
            {
                _currentIndex = -1;
            }
        }
    }

    /// <summary>
    /// 将一首歌插入到当前播放位置之后（下一首播放）
    /// </summary>
    public void AddNext(Song song)
    {
        if (_currentIndex >= 0 && _currentIndex + 1 < _originalList.Count)
        {
            _originalList.Insert(_currentIndex + 1, song);
        }
        else
        {
            _originalList.Add(song);
        }
        if (_shuffledList.Count > 0)
        {
            _shuffledList.Insert(_currentIndex + 1, song);
        }
        RebuildIndex();
    }

    public void AddToEnd(Song song)
    {
        _originalList.Add(song);
        if (_shuffledList.Count > 0)
            _shuffledList.Add(song);
        RebuildIndex();
    }
    
    /// <summary>
    /// 预览下一首（不改变队列状态）
    /// </summary>
    public Song? PeekNext()
    {
        if (_originalList.Count == 0) return null;
        var list = PlayMode == PlayMode.Shuffle ? _shuffledList : _originalList;

        if (PlayMode == PlayMode.SingleRepeat)
            return CurrentSong;

        int nextIndex = _currentIndex + 1;
        if (nextIndex >= list.Count)
        {
            if (PlayMode == PlayMode.ListRepeat || PlayMode == PlayMode.Shuffle)
                nextIndex = 0;
            else
                return null;
        }

        return list.ElementAtOrDefault(nextIndex);
    }

    /// <summary>
    /// 获取下一首索引
    /// </summary>
    private int GetNextIndex()
    {
        var list = PlayMode == PlayMode.Shuffle ? _shuffledList : _originalList;

        if (PlayMode == PlayMode.SingleRepeat)
        {
            // 单曲循环，返回当前；如果当前索引已越界，直接返回 0（若列表非空）
            if (_currentIndex >= 0 && _currentIndex < list.Count) return _currentIndex;
            return list.Count > 0 ? 0 : -1;
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
        if (list.Count == 0 || _currentIndex >= list.Count) return upcoming;

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
    /// <summary>
    /// Fisher-Yates 洗牌算法
    /// </summary>
    public static List<T> Shuffle<T>(IList<T> list)
    {
        var result = list.ToList();
        for (int i = result.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
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
