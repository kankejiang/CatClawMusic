using CatClawMusic.Core.Interfaces;
namespace CatClawMusic.Maui.Controls;

public partial class BackButton : ContentView
{
    public event EventHandler? Clicked;

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(System.Windows.Input.ICommand), typeof(BackButton));

    public System.Windows.Input.ICommand? Command
    {
        get => (System.Windows.Input.ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public BackButton()
    {
        InitializeComponent();
    }

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        try
        {
#if ANDROID
            try
            {
                if (Platform.CurrentActivity is Android.App.Activity act &&
                    act.Window?.DecorView is Android.Views.View v)
                {
                    v.PerformHapticFeedback(Android.Views.FeedbackConstants.ContextClick);
                }
            }
            catch { }
#endif
            if (Command?.CanExecute(null) == true)
            {
                Command.Execute(null);
                return;
            }

            if (Clicked is not null)
            {
                Clicked.Invoke(this, EventArgs.Empty);
                return;
            }

            if (Shell.Current != null && Shell.Current.CurrentState?.Location != null)
            {
                // 若当前处于某 overlay PagerNavigator（如设置/音乐库的二级页）内，
                // 优先用原生 ViewPager2 平滑滑出，而非 Shell 默认 push/pop 动画。
                if (PagerNavigator.Active is { CanPop: true } nav)
                {
                    nav.PopAsync();
                    return;
                }
                await Shell.Current.GoToAsync("..");
                return;
            }

            if (Application.Current?.MainPage is Page p && p.Navigation != null && p.Navigation.ModalStack.Count > 0)
            {
                await p.Navigation.PopModalAsync();
                return;
            }
            if (Application.Current?.MainPage is Page p2 && p2.Navigation != null && p2.Navigation.NavigationStack.Count > 1)
            {
                await p2.Navigation.PopAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Debug("BackButton.xaml", $"[BackButton] 返回失败: {ex.Message}");
        }
    }
}
