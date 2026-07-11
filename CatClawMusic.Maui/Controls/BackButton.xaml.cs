namespace CatClawMusic.Maui.Controls;

public partial class BackButton : ContentView
{
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

            if (Shell.Current != null && Shell.Current.CurrentState?.Location != null)
            {
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
            System.Diagnostics.Debug.WriteLine($"[BackButton] 返回失败: {ex.Message}");
        }
    }
}
