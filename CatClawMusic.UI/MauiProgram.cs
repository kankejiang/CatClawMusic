using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.Pages;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace CatClawMusic.UI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("站酷快乐体2016修订版.ttf", "HappyZcool2016");
                fonts.AddFont("298-CAI978.ttf", "CAI978");
            });

        // 配置数据库
        string dbPath = Path.Combine(FileSystem.AppDataDirectory, "catclaw.db");
        var database = new MusicDatabase(dbPath);
        
        // 依赖注入 - Core Services
        builder.Services.AddSingleton(database);
        builder.Services.AddSingleton<ISubsonicService, SubsonicService>();
        builder.Services.AddSingleton<INetworkFileService, WebDavService>();
        builder.Services.AddSingleton<INetworkMusicService, NetworkMusicService>();
        builder.Services.AddSingleton<IAudioPlayerService, Platforms.Android.AudioPlayerService>();
        builder.Services.AddSingleton<ILyricsService, LyricsService>();
        builder.Services.AddSingleton<IMusicLibraryService, MusicLibraryService>();
        builder.Services.AddSingleton<PlayQueue>();
        
        // 注册 ViewModels
        builder.Services.AddTransient<ViewModels.LibraryViewModel>();
        builder.Services.AddTransient<ViewModels.NowPlayingViewModel>();
        builder.Services.AddTransient<ViewModels.SettingsViewModel>();
        builder.Services.AddTransient<ViewModels.SearchViewModel>();
        builder.Services.AddTransient<ViewModels.PlaylistViewModel>();
        builder.Services.AddTransient<ViewModels.WebDavSettingsViewModel>();
        builder.Services.AddTransient<ViewModels.NavidromeSettingsViewModel>();
        
        // 注册页面（支持构造函数注入）
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<NowPlayingPage>();
        builder.Services.AddTransient<PlaylistPage>();
        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<WebDavSettingsPage>();
        builder.Services.AddTransient<NavidromeSettingsPage>();

        return builder.Build();
    }
}
