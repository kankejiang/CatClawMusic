using Android.App;
using Android.Views;
using Android.Widget;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AndroidX.RecyclerView.Widget;

namespace CatClawMusic.UI.Helpers;

/// <summary>简易数据绑定辅助类，将 ViewModel 属性绑定到 Android View</summary>
public static class BindingHelper
{
    /// <summary>将数据源属性单向绑定到 TextView 的 Text</summary>
    public static void BindText(TextView textView, object? source, string propertyName, Func<object?, string?> getter)
    {
        Update();
        if (source is INotifyPropertyChanged inpc)
        {
            inpc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == propertyName)
                    RunOnUi(textView, Update);
            };
        }
        void Update() => textView.Text = getter(source) ?? "";
    }

    /// <summary>将数据源属性单向绑定到 View 的 Visibility</summary>
    public static void BindVisible(View view, object? source, string propertyName, Func<object?, bool> getter)
    {
        Update();
        if (source is INotifyPropertyChanged inpc)
        {
            inpc.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == propertyName)
                    RunOnUi(view, Update);
            };
        }
        void Update() => view.Visibility = getter(source) ? ViewStates.Visible : ViewStates.Gone;
    }

    /// <summary>将 ObservableCollection 变化绑定到 RecyclerView 刷新</summary>
    public static void BindCollection<T>(RecyclerView recyclerView, ObservableCollection<T> collection, RecyclerView.Adapter adapter)
    {
        collection.CollectionChanged += (s, e) =>
            RunOnUi(recyclerView, () => adapter.NotifyDataSetChanged());
    }

    /// <summary>在 UI 线程执行操作（通过 View 获取 Activity）</summary>
    private static void RunOnUi(View view, Action action)
    {
        if (view.Context is Activity activity)
            activity.RunOnUiThread(() => action());
        else
            action();
    }

    /// <summary>在 UI 线程执行操作（通过 MainActivity.Instance）</summary>
    public static void RunOnUiThread(Action action)
    {
        var instance = MainActivity.Instance;
        if (instance != null)
            instance.RunOnUiThread(() => action());
        else
            action();
    }
}
