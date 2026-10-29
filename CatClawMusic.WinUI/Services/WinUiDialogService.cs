using CatClawMusic.Core.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CatClawMusic.WinUI.Services;

/// <summary>
/// 对话框服务（替代 MAUI 的 <c>DisplayAlert</c>）：用 <see cref="ContentDialog"/> 呈现。
/// XamlRoot 由壳层在窗口激活后注入。
/// </summary>
public sealed class WinUiDialogService : IDialogService
{
    private Func<XamlRoot?>? _xamlRootProvider;

    internal void Attach(Func<XamlRoot?> xamlRootProvider) => _xamlRootProvider = xamlRootProvider;

    public async Task ShowAlertAsync(string title, string message, string buttonText = "确定")
    {
        var dialog = CreateDialog(title, message);
        dialog.CloseButtonText = buttonText;
        await ShowAsync(dialog);
    }

    public async Task<bool> ShowConfirmAsync(string title, string message, string acceptText = "确定", string cancelText = "取消")
    {
        var dialog = CreateDialog(title, message);
        dialog.PrimaryButtonText = acceptText;
        dialog.CloseButtonText = cancelText;
        dialog.DefaultButton = ContentDialogButton.Primary;

        var result = await ShowAsync(dialog);
        return result == ContentDialogResult.Primary;
    }

    private static ContentDialog CreateDialog(string title, string message) => new()
    {
        Title = title,
        Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
    };

    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        var xamlRoot = _xamlRootProvider?.Invoke();
        if (xamlRoot is null)
            return ContentDialogResult.None;

        dialog.XamlRoot = xamlRoot;
        return await dialog.ShowAsync();
    }
}
