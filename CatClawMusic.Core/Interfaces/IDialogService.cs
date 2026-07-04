namespace CatClawMusic.Core.Interfaces;

/// <summary>对话框服务接口，提供跨平台的提示与确认对话框抽象</summary>
public interface IDialogService
{
    /// <summary>显示提示对话框（仅一个确认按钮）</summary>
    /// <param name="title">对话框标题</param>
    /// <param name="message">提示内容</param>
    /// <param name="buttonText">按钮文本</param>
    Task ShowAlertAsync(string title, string message, string buttonText = "确定");

    /// <summary>显示确认对话框，返回用户是否点击了确认按钮</summary>
    /// <param name="title">对话框标题</param>
    /// <param name="message">提示内容</param>
    /// <param name="acceptText">确认按钮文本</param>
    /// <param name="cancelText">取消按钮文本</param>
    Task<bool> ShowConfirmAsync(string title, string message, string acceptText = "确定", string cancelText = "取消");
}
