using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace CatClawMusic.UI.Helpers;

/// <summary>
/// 支持批量操作的可观察集合，继承自 <see cref="ObservableCollection{T}"/>。
/// <para>
/// 标准的 ObservableCollection 每次添加/移除元素都会触发 CollectionChanged 事件，
/// 在批量添加大量元素时会导致严重的性能问题（例如 UI 频繁刷新）。
/// 本类通过通知抑制机制解决这个问题：
/// <list type="bullet">
///   <item>在批量操作期间抑制所有 CollectionChanged 通知。</item>
///   <item>批量操作完成后，仅触发一次 Reset 级别的通知，告知监听者集合已整体变更。</item>
///   <item>同时触发 Count 属性变更通知，确保绑定到 Count 的 UI 元素正确更新。</item>
/// </list>
/// </para>
/// </summary>
/// <typeparam name="T">集合中元素的类型。</typeparam>
public class BatchObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>
    /// 通知抑制标志。当为 true 时，OnCollectionChanged 方法不会向监听者传播变更通知。
    /// 使用 try/finally 确保即使批量操作中发生异常，标志也能被正确重置。
    /// </summary>
    private bool _suppressNotification;

    /// <summary>
    /// 批量添加元素到集合末尾。
    /// <para>
    /// 执行流程：
    /// <list type="number">
    ///   <item>设置通知抑制标志，阻止逐个添加时触发多次 CollectionChanged 事件。</item>
    ///   <item>逐个将元素添加到内部 Items 列表。</item>
    ///   <item>在 finally 块中取消抑制标志，并触发一次 Reset 通知和 Count 属性变更通知。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="items">要批量添加的元素集合，不可为 null。</param>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出。</exception>
    public void AddRange(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        _suppressNotification = true;
        try
        {
            foreach (var item in items)
                Items.Add(item);
        }
        finally
        {
            _suppressNotification = false;
            // 使用 Reset 操作类型通知监听者集合已整体变更，而非逐条通知
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            // 手动触发 Count 属性变更通知，因为直接操作 Items 不会自动触发
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        }
    }

    /// <summary>
    /// 批量移除与指定谓词匹配的元素。
    /// <para>
    /// 执行流程：
    /// <list type="number">
    ///   <item>设置通知抑制标志，阻止逐个移除时触发多次 CollectionChanged 事件。</item>
    ///   <item>逐个移除内部 Items 列表中满足条件的元素。</item>
    ///   <item>在 finally 块中取消抑制标志，并触发一次 Reset 通知和 Count 属性变更通知。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="predicate">用于判断元素是否应被移除的谓词。</param>
    /// <returns>被移除的元素数量。</returns>
    public int RemoveAll(Func<T, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        _suppressNotification = true;
        int removed = 0;
        try
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                if (predicate(Items[i]))
                {
                    Items.RemoveAt(i);
                    removed++;
                }
            }
        }
        finally
        {
            _suppressNotification = false;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        }
        return removed;
    }

    /// <summary>
    /// 用指定元素集合替换集合中的所有元素。
    /// <para>
    /// 执行流程：
    /// <list type="number">
    ///   <item>设置通知抑制标志，阻止清空和添加时分别触发 CollectionChanged 事件。</item>
    ///   <item>先清空内部 Items 列表，再逐个添加新元素。</item>
    ///   <item>在 finally 块中取消抑制标志，并触发一次 Reset 通知和 Count 属性变更通知。</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="items">要替换为的新元素集合，不可为 null。</param>
    /// <exception cref="ArgumentNullException">当 items 为 null 时抛出。</exception>
    public void ReplaceAll(IEnumerable<T> items)
    {
        if (items == null) throw new ArgumentNullException(nameof(items));

        _suppressNotification = true;
        try
        {
            Items.Clear();
            foreach (var item in items)
                Items.Add(item);
        }
        finally
        {
            _suppressNotification = false;
            // 使用 Reset 操作类型通知监听者集合已整体变更，而非逐条通知
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            // 手动触发 Count 属性变更通知，因为直接操作 Items 不会自动触发
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        }
    }

    /// <summary>
    /// 重写集合变更通知方法，实现通知抑制机制。
    /// <para>
    /// 当 _suppressNotification 为 true 时，所有 CollectionChanged 事件都不会传播到监听者，
    /// 从而避免批量操作期间产生大量冗余通知导致 UI 频繁刷新。
    /// 当抑制结束后，通过手动调用本方法并传入 Reset 参数来发送一次整体变更通知。
    /// </para>
    /// </summary>
    /// <param name="e">集合变更事件参数。</param>
    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnCollectionChanged(e);
    }
}
