namespace CatClawMusic.Core.Interfaces;

/// <summary>对话框服务</summary>
public interface IDialogService
{
    Task ShowAlertAsync(string title, string message, string buttonText = "确定");
    Task<bool> ShowConfirmAsync(string title, string message, string acceptText = "确定", string cancelText = "取消");
}
