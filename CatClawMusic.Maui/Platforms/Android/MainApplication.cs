using Android.App;
using Android.Runtime;

namespace CatClawMusic.Maui;

/// <summary>Android 应用入口类，负责注册应用级配置并创建 MAUI 应用实例</summary>
[Application]
public class MainApplication : MauiApplication
{
    /// <summary>构造函数，由 Android 运行时通过 JNI 调用</summary>
    /// <param name="handle">JNI 句柄</param>
    /// <param name="ownership">句柄所有权</param>
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    /// <summary>创建并返回 MAUI 应用实例</summary>
    /// <returns>配置完成的 MauiApp 实例</returns>
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
