using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace QuietShelf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplicationAccentColorManager.Apply(
            Color.FromRgb(0x35, 0x66, 0x4F),
            ApplicationTheme.Light,
            systemGlassColor: false,
            systemAccentColor: false);

        new MainWindow().Show();
    }
}
