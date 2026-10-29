using Windows.Storage.Pickers;
using WinRT.Interop;

namespace CatClawMusic.WinUI.Services;

/// <summary>
/// 文件夹选择（WinUI 3 的 <see cref="FolderPicker"/> 必须用窗口句柄初始化）。
/// 对照 MAUI 的 <c>FolderPicker</c>：这里走系统选择器，不做内置文件浏览器。
/// </summary>
public sealed class FolderPickerService
{
    private IntPtr _windowHandle;

    public void Initialize(IntPtr windowHandle) => _windowHandle = windowHandle;

    /// <summary>选择一个文件夹；用户取消时返回 null</summary>
    public async Task<string?> PickFolderAsync()
    {
        if (_windowHandle == IntPtr.Zero)
            return null;

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
        };
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, _windowHandle);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
