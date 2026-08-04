// ------------------------------------------------------------------------------
//  InitializeComponent 兜底文件（仅当 XamlCompiler 未生成 App.g.i.cs 时启用）
//
//  正常情况下本文件【不参与编译】：App 的 _contentLoaded / InitializeComponent
//  由 XamlCompiler 生成的 App.g.i.cs 提供。csproj 中 EnsureAppInitializeComponent
//  target（BeforeTargets="CoreCompile"）会在 Release 下检测生成代码是否存在，
//  若 App.g.i.cs 与 App.g.cs 均不存在（XamlCompiler 极端异常），才把本文件
//  动态加入编译，避免 CS0103（InitializeComponent 缺失）。
// ------------------------------------------------------------------------------

namespace CatClawMusic.Maui.WinUI
{
    partial class App : global::Microsoft.Maui.MauiWinUIApplication
    {
        private bool _contentLoaded;

        /// <summary>
        /// InitializeComponent()
        /// </summary>
        public void InitializeComponent()
        {
            if (_contentLoaded)
                return;

            _contentLoaded = true;

            global::System.Uri resourceLocator = new global::System.Uri("ms-appx:///Platforms/Windows/App.xaml");
            global::Microsoft.UI.Xaml.Application.LoadComponent(this, resourceLocator);
        }
    }
}
