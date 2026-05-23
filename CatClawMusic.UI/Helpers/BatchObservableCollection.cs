using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace CatClawMusic.UI.Helpers;

public class BatchObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotification;

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
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        }
    }

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
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        }
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotification)
            base.OnCollectionChanged(e);
    }
}
