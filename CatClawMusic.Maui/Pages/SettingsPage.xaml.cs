using CatClawMusic.Data;

namespace CatClawMusic.Maui.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly MusicDatabase _db;

    public SettingsPage(MusicDatabase db)
    {
        InitializeComponent();
        _db = db;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            var songs = await _db.GetSongsWithDetailsAsync();
            LibraryStatus.Text = $"歌曲数: {songs.Count}";
        }
        catch
        {
            LibraryStatus.Text = "数据库加载失败";
        }
    }

    private async void OnRescan(object? sender, EventArgs e)
    {
        LibraryStatus.Text = "扫描中...";
        await Task.Delay(500);
        LibraryStatus.Text = "扫描完成 (尚未实现)";
    }
}
