using CatClawMusic.Core.Interfaces;
using System.Threading.Tasks;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 对话框服务实现 - 封装显示对话框的逻辑
/// </summary>
public class DialogService : IDialogService
{
    public async Task ShowAlertAsync(string title, string message, string buttonText = "确定")
    {
        await Application.Current.MainPage.DisplayAlert(title, message, buttonText);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string acceptText = "确定", string cancelText = "取消")
    {
        return await Application.Current.MainPage.DisplayAlert(title, message, acceptText, cancelText);
    }
    
    /// <summary>
    /// 显示操作表（底部弹出菜单）- 额外方法，不在接口中
    /// </summary>
    public async Task<string> ShowActionSheetAsync(string title, string cancel, string? destruction = null, params string[] buttons)
    {
        return await Application.Current.MainPage.DisplayActionSheet(title, cancel, destruction, buttons);
    }

    /// <summary>
    /// 显示提示消息（短暂显示）- 额外方法，不在接口中
    /// </summary>
    public void ShowToast(string message)
    {
        // MAUI 没有内置的 Toast，可以使用第三方库或自定义实现
        // 这里使用简单的 DisplayAlert 作为替代
        Application.Current.MainPage.Dispatcher.Dispatch(async () =>
        {
            await Application.Current.MainPage.DisplayAlert("提示", message, "确定");
        });
    }
}
