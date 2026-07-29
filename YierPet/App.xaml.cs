using System.Windows;

namespace YierPet;

public partial class App : Application
{
    private PetController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sheet = SpriteSheet.Load();
        if (sheet == null)
        {
            MessageBox.Show(
                "spritesheet.webp 缺失或解码失败。",
                "无法加载精灵图集",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        _controller = new PetController(sheet);
    }
}
