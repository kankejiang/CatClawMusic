using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CatClawMusic.WinUI.Services;

/// <summary>文件选择（当前只用于选择 .ccp 插件）。与文件夹选择同理，需要窗口句柄。</summary>
public sealed class FilePickerService
{
    private IntPtr _windowHandle;

    public void Initialize(IntPtr windowHandle) => _windowHandle = windowHandle;

    public async Task<string?> PickPluginFileAsync()
    {
        if (_windowHandle == IntPtr.Zero)
            return null;

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
        };
        picker.FileTypeFilter.Add(".ccp");
        picker.FileTypeFilter.Add(".dll");

        InitializeWithWindow.Initialize(picker, _windowHandle);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
