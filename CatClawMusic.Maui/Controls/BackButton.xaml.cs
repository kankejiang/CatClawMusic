namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 通用左上角返回按钮。
/// 点击后调用 Shell.GoToAsync("..") 返回上一级路由；
/// 若不在 Shell 导航上下文中则回退到 Navigation.PopAsync。
/// </summary>
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
            // 触觉反馈（轻量级，失败不阻塞返回逻辑）
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

            // 优先使用 Shell 相对路由返回
            if (Shell.Current != null && Shell.Current.CurrentState?.Location != null)
            {
                await Shell.Current.GoToAsync("..");
                return;
            }

            // 兜底：使用 Navigation 栈
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
