using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CatClawMusic.Maui.Helpers;

/// <summary>
/// 支持批量替换的 ObservableCollection：ReplaceAll 只触发一次 Reset 通知，
/// 且始终复用同一集合实例——替换 ItemsSource 引用会让 CollectionView 失活全部行、
/// 丢失滚动位置，而同一实例 + 单次 Reset 通知保持视图与滚动状态。
/// 搜索/排序等高频过滤场景用 ReplaceAll 替代 new ObservableCollection(...)。
/// </summary>
public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public ObservableRangeCollection() { }

    public ObservableRangeCollection(IEnumerable<T> collection) : base(collection) { }

    /// <summary>整体替换内容，只发一次 Reset + Count/Item[] 通知。</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>追加一批元素，只发一次 Add 通知（替代 Clear+逐条 Add 的 N 次通知）。</summary>
    public void AddRange(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));
        var list = items as ICollection<T> ?? items.ToList();
        if (list.Count == 0) return;

        CheckReentrancy();
        foreach (var item in list)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (System.Collections.IList)list));
    }
}
