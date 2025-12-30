using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using HandyControl.Controls;
using PVZRHTools.Animations;

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
        
        // 从悬浮窗打开主窗口时播放激活动画
        WindowAnimations.PlayActivationAnimation(MainWindow.Instance!);
    }

    private void ContentControl_MouseEnter(object sender, MouseEventArgs e)
    {
        // 放大动画
        var scaleXAnim = new DoubleAnimation
        {
            To = 1.15,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var scaleYAnim = new DoubleAnimation
        {
            To = 1.15,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        SpriteScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        SpriteScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);

        // 发光动画
        var glowBlur = new DoubleAnimation
        {
            To = 20,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var glowOpacity = new DoubleAnimation
        {
            To = 0.8,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        
        SpriteGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glowBlur);
        SpriteGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowOpacity);
    }

    private void ContentControl_MouseLeave(object sender, MouseEventArgs e)
    {
        // 缩小动画
        var scaleXAnim = new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        scaleXAnim.FillBehavior = FillBehavior.Stop;
        scaleXAnim.Completed += (_, _) => SpriteScale.ScaleX = 1;
        
        var scaleYAnim = new DoubleAnimation
        {
            To = 1,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        scaleYAnim.FillBehavior = FillBehavior.Stop;
        scaleYAnim.Completed += (_, _) => SpriteScale.ScaleY = 1;
        
        SpriteScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        SpriteScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);

        // 发光消失动画
        var glowBlur = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        glowBlur.FillBehavior = FillBehavior.Stop;
        glowBlur.Completed += (_, _) => SpriteGlow.BlurRadius = 0;
        
        var glowOpacity = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        glowOpacity.FillBehavior = FillBehavior.Stop;
        glowOpacity.Completed += (_, _) => SpriteGlow.Opacity = 0;
        
        SpriteGlow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glowBlur);
        SpriteGlow.BeginAnimation(DropShadowEffect.OpacityProperty, glowOpacity);
    }
}