namespace CatClawMusic.UI;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // 锁定浅色模式，统一 UI
        UserAppTheme = AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
