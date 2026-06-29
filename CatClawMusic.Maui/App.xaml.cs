using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // 应用主题
        try
        {
            var themeService = MauiProgram.Services.GetRequiredService<IThemeService>();
            themeService.ApplyTheme();
        }
        catch { }

        // 设置 LyricsService 的 PluginManager（属性注入，避免循环依赖）
        var lyricsService = MauiProgram.Services.GetService<ILyricsService>() as LyricsService;
        if (lyricsService != null)
        {
            lyricsService.PluginManager = MauiProgram.Services.GetRequiredService<IPluginManager>();
        }

        // 初始化所有已启用的插件（fire-and-forget）
        _ = Task.Run(async () =>
        {
            try { await MauiProgram.Services.GetRequiredService<IPluginManager>().InitializeAllAsync(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] PluginManager init failed: {ex.Message}");
            }
        });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
