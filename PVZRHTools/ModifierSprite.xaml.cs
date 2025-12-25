using System.Windows.Input;
using HandyControl.Controls;

namespace PVZRHTools;

/// <summary>
///     Sprite.xaml 的交互逻辑
/// </summary>
public partial class ModifierSprite : SimplePanel
{
    public ModifierSprite()
    {
        InitializeComponent();
    }

    private void ContentControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        MainWindow.Instance!.Topmost = true;
        MainWindow.Instance!.Topmost = false;
    }
}