using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CatClawMusic.WinUI.Services;
using CatClawMusic.WinUI.ViewModels;
using CatClawMusic.WinUI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.WinUI.Infrastructure;

/// <summary>
/// 组合根：Core / Data 的服务原样注册，只有平台能力在本工程实现。
/// 新增页面时在这里注册；不要让 View 直接 new 服务。
/// </summary>
public static class ServiceRegistration
{
    public static IServiceProvider Build()
    {
        var services = new ServiceCollection();

        // ── 基础设施 ──
        services.AddSingleton(new JsonSettingsStore(AppPaths.SettingsPath));
        services.AddSingleton<FileLogService>();
        services.AddSingleton<ILogService>(sp => sp.GetRequiredService<FileLogService>());
        services.AddSingleton<IMainThreadDispatcher, WinUiDispatcherService>();

        // ── 平台能力 ──
        services.AddSingleton<WinUiThemeService>();
        services.AddSingleton<IThemeService>(sp => sp.GetRequiredService<WinUiThemeService>());

        services.AddSingleton<WinUiDialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<WinUiDialogService>());

        services.AddSingleton<WinUiNavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<WinUiNavigationService>());

        services.AddSingleton<FolderPickerService>();

        // ── 播放 ──
        services.AddSingleton<IAudioPlayerService, MediaPlayerAudioService>();
        services.AddSingleton<PlaybackService>();

        // ── 数据层（复用 CatClawMusic.Data）──
        services.AddSingleton(_ => new MusicDatabase(AppPaths.DatabasePath));
        services.AddSingleton<IMusicLibraryService>(sp =>
            new MusicLibraryService(sp.GetRequiredService<MusicDatabase>()));

        // ── ViewModel ──
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<PlaylistsViewModel>();

        // ── 页面 ──
        services.AddSingleton<DiscoverView>();
        services.AddSingleton<PlaylistsView>();
        services.AddSingleton<LibraryView>();
        services.AddSingleton<SettingsView>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }
}
