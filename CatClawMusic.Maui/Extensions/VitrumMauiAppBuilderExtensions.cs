namespace Vitrum;

public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="BlurHostView"/> and <see cref="BlurConsumerView"/> handlers.
    /// Call this in <c>MauiProgram.CreateMauiApp</c> before <c>Build()</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// var builder = MauiApp.CreateBuilder();
    /// builder.UseMauiApp&lt;App&gt;().UseVitrum();
    /// </code>
    /// </example>
    public static MauiAppBuilder UseVitrum(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID
            handlers.AddHandler<BlurHostView, Android.Handlers.BlurHostViewHandler>();
            handlers.AddHandler<BlurConsumerView, Android.Handlers.BlurConsumerViewHandler>();
#endif
        });

        return builder;
    }
}
