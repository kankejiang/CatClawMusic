using Android.App;
using Android.Views;
using Android.Widget;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using AndroidX.RecyclerView.Widget;

namespace CatClawMusic.UI.Helpers;

public static class BindingHelper
{
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

    public static void BindCollection<T>(RecyclerView recyclerView, ObservableCollection<T> collection, RecyclerView.Adapter adapter)
    {
        collection.CollectionChanged += (s, e) =>
            RunOnUi(recyclerView, () => adapter.NotifyDataSetChanged());
    }

    private static void RunOnUi(View view, Action action)
    {
        if (view.Context is Activity activity)
            activity.RunOnUiThread(() => action());
        else
            action();
    }

    public static void RunOnUiThread(Action action)
    {
        var instance = MainActivity.Instance;
        if (instance != null)
            instance.RunOnUiThread(() => action());
        else
            action();
    }
}
