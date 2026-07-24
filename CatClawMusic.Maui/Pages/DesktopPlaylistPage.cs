using System;
using System.Reflection;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式歌单页：包装 DesktopPlaylistView（独立横屏布局，双列网格），
/// 摘出其 Content 放入 DesktopMainPage 的 ContentArea，生命周期通过反射委托。</summary>
public class DesktopPlaylistPage : ContentPage
{
    private readonly DesktopPlaylistView _inner;

    public DesktopPlaylistPage(PlaylistViewModel vm)
    {
        _inner = new DesktopPlaylistView(vm);

        var content = _inner.Content;
        _inner.Content = null;

        Content = content;
        BindingContext = _inner.BindingContext;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        InvokeInner("OnAppearing");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        InvokeInner("OnDisappearing");
    }

    private void InvokeInner(string methodName)
    {
        try
        {
            typeof(DesktopPlaylistView)
                .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_inner, null);
        }
        catch { }
    }
}
