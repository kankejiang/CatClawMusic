using CatClawMusic.Core.Interfaces;
using System.Threading.Tasks;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 对话框服务实现 - 封装显示对话框的逻辑
/// </summary>
public class DialogService : IDialogService
{
    /// <summary>显示提示对话框（仅一个确认按钮）</summary>
    /// <param name="title">对话框标题</param>
    /// <param name="message">对话框内容</param>
    /// <param name="buttonText">按钮文本，默认为"确定"</param>
    public async Task ShowAlertAsync(string title, string message, string buttonText = "确定")
    {
        await Application.Current.MainPage.DisplayAlert(title, message, buttonText);
    }

    /// <summary>显示确认对话框（包含确认和取消按钮）</summary>
    /// <param name="title">对话框标题</param>
    /// <param name="message">对话框内容</param>
    /// <param name="acceptText">确认按钮文本，默认为"确定"</param>
    /// <param name="cancelText">取消按钮文本，默认为"取消"</param>
    /// <returns>用户点击确认返回 true，点击取消返回 false</returns>
    public async Task<bool> ShowConfirmAsync(string title, string message, string acceptText = "确定", string cancelText = "取消")
    {
        return await Application.Current.MainPage.DisplayAlert(title, message, acceptText, cancelText);
    }

    /// <summary>
    /// 显示操作表（底部弹出菜单）- 额外方法，不在接口中
    /// </summary>
    /// <param name="title">操作表标题</param>
    /// <param name="cancel">取消按钮文本</param>
    /// <param name="destruction">销毁按钮文本（可选，红色显示）</param>
    /// <param name="buttons">其它按钮文本集合</param>
    /// <returns>用户选择的按钮文本；点击取消时返回 cancel 文本</returns>
    public async Task<string> ShowActionSheetAsync(string title, string cancel, string? destruction = null, params string[] buttons)
    {
        return await Application.Current.MainPage.DisplayActionSheet(title, cancel, destruction, buttons);
    }

    /// <summary>
    /// 显示提示消息（短暂显示）- 额外方法，不在接口中
    /// </summary>
    /// <param name="message">提示消息内容</param>
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
