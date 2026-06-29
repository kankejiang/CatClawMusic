// Windows 入口点 — 当 MAUI 源生成器未自动生成时使用
#if WINDOWS
using Microsoft.UI.Xaml;

namespace CatClawMusic.Maui.WinUI;

public static class Program
{
    [global::System.Runtime.InteropServices.DllImport("Microsoft.ui.xaml.dll")]
    private static extern void XamlCheckProcessRequirements();

    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.UI.Xaml.Markup.Compiler", "3.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute]
    [global::System.STAThreadAttribute]
    static void Main(string[] args)
    {
        XamlCheckProcessRequirements();
        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = MauiProgram.CreateMauiApp();
        });
    }
}
#endif
