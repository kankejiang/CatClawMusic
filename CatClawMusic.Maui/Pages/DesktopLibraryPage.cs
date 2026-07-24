using System;
using System.Linq;
using System.Reflection;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式音乐库页：包装 LibraryPage，摘出其 Content 并压缩底部留白，
/// 生命周期通过反射委托给内部 LibraryPage 实例（复用全部 869 行逻辑，零重复）。
/// 横屏下将各卡片重新组织为双列网格布局，充分利用横向空间。</summary>
public class DesktopLibraryPage : ContentPage
{
    private readonly LibraryPage _inner;

    public DesktopLibraryPage(MusicDatabase db, PlayQueue queue, LibraryViewModel vm, IServiceProvider sp)
    {
        _inner = new LibraryPage(db, queue, vm, sp);

        // 摘出内部页面的 Content（Grid > ScrollView > VerticalStackLayout）
        var content = _inner.Content;
        _inner.Content = null;

        // 横屏布局重构：从 VerticalStackLayout 中取出各卡片，重新组织为双列网格
        if (content is Grid grid
            && grid.Children.Count > 0
            && grid.Children[0] is ScrollView sv
            && sv.Content is VerticalStackLayout vsl)
        {
            // 横屏无底部 TabBar，把 132dp 底部留白压到 18dp
            vsl.Padding = new Thickness(18, 8, 18, 18);

            // 取出各卡片：Hero / 资料库 / 数据洞察 / 存储占用 / 最近添加
            // AppPopup 不是 Border，单独保留
            var cards = vsl.Children.OfType<Border>().ToList();
            var popup = vsl.Children.OfType<Controls.AppPopup>().FirstOrDefault();
            if (cards.Count >= 5)
            {
                // 重新组织：Hero 全宽 → [资料库 | 数据洞察] 并排 → [存储占用 | 最近添加] 并排
                var hero = cards[0];
                var libraryCards = cards[1];
                var dataInsight = cards[2];
                var storage = cards[3];
                var recent = cards[4];

                // 清空原 VerticalStackLayout（含 AppPopup），稍后按新顺序重建
                vsl.Children.Clear();

                // Hero 保持全宽，收紧边距
                hero.Margin = new Thickness(0, 0, 0, 10);
                vsl.Children.Add(hero);

                // 双列网格容器：资料库 | 数据洞察
                var row1 = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new() { Width = new GridLength(1, GridUnitType.Star) },
                        new() { Width = new GridLength(1, GridUnitType.Star) }
                    },
                    ColumnSpacing = 14,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                libraryCards.Margin = new Thickness(0);
                dataInsight.Margin = new Thickness(0);
                Grid.SetColumn(libraryCards, 0);
                Grid.SetColumn(dataInsight, 1);
                row1.Children.Add(libraryCards);
                row1.Children.Add(dataInsight);
                vsl.Children.Add(row1);

                // 双列网格容器：存储占用 | 最近添加
                var row2 = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new() { Width = new GridLength(1, GridUnitType.Star) },
                        new() { Width = new GridLength(1, GridUnitType.Star) }
                    },
                    ColumnSpacing = 14
                };
                storage.Margin = new Thickness(0);
                recent.Margin = new Thickness(0);
                Grid.SetColumn(storage, 0);
                Grid.SetColumn(recent, 1);
                row2.Children.Add(storage);
                row2.Children.Add(recent);
                vsl.Children.Add(row2);

                // AppPopup 重新加回（DiscoverSourcePopup 弹窗控件）
                if (popup != null)
                    vsl.Children.Add(popup);
            }

            // 直接使用内部的 ScrollView 作为页面 Content，
            // 避免 DesktopMainPage.CreatePageContent 再包一层 ScrollView 导致双重滚动嵌套
            // （双重 ScrollView 会使内层测量异常，下方的数据洞察/存储占用等卡片无法显示）
            Content = sv;
            BindingContext = _inner.BindingContext;
            return;
        }

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
            typeof(LibraryPage)
                .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(_inner, null);
        }
        catch { }
    }
}
