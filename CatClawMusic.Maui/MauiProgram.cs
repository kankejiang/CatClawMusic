using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui;

public static class MauiProgram
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Database (singleton — one SQLite connection)
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "catclaw.db");
        var db = new MusicDatabase(dbPath);
        builder.Services.AddSingleton(db);

        // Play queue (singleton — shared across app)
        builder.Services.AddSingleton<PlayQueue>();

        // Lyrics service (from Core, fully portable)
        builder.Services.AddSingleton<ILyricsService, LyricsService>();

        // ViewModels
        builder.Services.AddSingleton<NowPlayingViewModel>();
        builder.Services.AddSingleton<LibraryViewModel>();

        // Pages
        builder.Services.AddTransient<Pages.NowPlayingPage>();
        builder.Services.AddTransient<Pages.LibraryPage>();
        builder.Services.AddTransient<Pages.SearchPage>();
        builder.Services.AddTransient<Pages.SettingsPage>();

        Services = builder.Services.BuildServiceProvider();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
