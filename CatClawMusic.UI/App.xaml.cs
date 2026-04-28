using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        
        // 设置主页面
        MainPage = new AppShell();
    }
}
