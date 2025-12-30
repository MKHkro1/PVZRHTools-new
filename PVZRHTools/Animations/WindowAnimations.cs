using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PVZRHTools.Animations
{
    /// <summary>
    /// 高级 OS 风格窗口动画
    /// </summary>
    public static class WindowAnimations
    {
        /// <summary>
        /// 窗口启动动画 - macOS/Windows 11 风格
        /// 对窗口内容应用动画而非窗口本身
        /// </summary>
        public static void PlayStartupAnimation(Window window)
        {
            // 获取窗口的根内容
            if (window.Content is not FrameworkElement content)
                return;

            // 初始状态
            window.Opacity = 0;
            content.RenderTransformOrigin = new Point(0.5, 0.5);
            var scaleTransform = new ScaleTransform(0.95, 0.95);
            var translateTransform = new TranslateTransform(0, 12);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(scaleTransform);
            transformGroup.Children.Add(translateTransform);
            content.RenderTransform = transformGroup;

            var storyboard = new Storyboard();

            // 窗口淡入动画
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, window);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));

            // 内容缩放动画
            var scaleX = new DoubleAnimation
            {
                From = 0.95,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(350),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(scaleX, content);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.Children[0].ScaleX"));

            var scaleY = new DoubleAnimation
            {
                From = 0.95,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(350),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(scaleY, content);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.Children[0].ScaleY"));

            // 上移动画
            var slideUp = new DoubleAnimation
            {
                From = 12,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(350),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slideUp, content);
            Storyboard.SetTargetProperty(slideUp, new PropertyPath("RenderTransform.Children[1].Y"));

            storyboard.Children.Add(fadeIn);
            storyboard.Children.Add(scaleX);
            storyboard.Children.Add(scaleY);
            storyboard.Children.Add(slideUp);

            storyboard.Begin();
        }

        /// <summary>
        /// 窗口激活动画 - 从后台切回前台时播放
        /// 更慢、幅度更大、更有弹性的版本
        /// </summary>
        public static void PlayActivationAnimation(Window window)
        {
            if (window.Content is not FrameworkElement content)
                return;

            // 确保内容有变换
            if (content.RenderTransform is not TransformGroup)
            {
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(1, 1));
                transformGroup.Children.Add(new TranslateTransform(0, 0));
                content.RenderTransform = transformGroup;
                content.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var storyboard = new Storyboard();

            // 弹性缩放效果 - 更大幅度、更有弹性
            var elasticEase = new ElasticEase 
            { 
                EasingMode = EasingMode.EaseOut, 
                Oscillations = 2,  // 弹跳次数
                Springiness = 5    // 弹性强度
            };

            var scaleX = new DoubleAnimationUsingKeyFrames();
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(0.92, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)), elasticEase));
            Storyboard.SetTarget(scaleX, content);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.Children[0].ScaleX"));

            var scaleY = new DoubleAnimationUsingKeyFrames();
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(0.92, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500)), elasticEase));
            Storyboard.SetTarget(scaleY, content);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.Children[0].ScaleY"));

            // 透明度渐变 - 更明显
            var opacity = new DoubleAnimationUsingKeyFrames();
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(0.7, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            opacity.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(350)), 
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            Storyboard.SetTarget(opacity, window);
            Storyboard.SetTargetProperty(opacity, new PropertyPath(UIElement.OpacityProperty));

            // 轻微上移效果
            var translateY = new DoubleAnimationUsingKeyFrames();
            translateY.KeyFrames.Add(new EasingDoubleKeyFrame(8, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            translateY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400)), 
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            Storyboard.SetTarget(translateY, content);
            Storyboard.SetTargetProperty(translateY, new PropertyPath("RenderTransform.Children[1].Y"));

            storyboard.Children.Add(scaleX);
            storyboard.Children.Add(scaleY);
            storyboard.Children.Add(opacity);
            storyboard.Children.Add(translateY);

            storyboard.Begin();
        }

        /// <summary>
        /// 窗口关闭动画
        /// </summary>
        public static async Task PlayCloseAnimation(Window window)
        {
            if (window.Content is not FrameworkElement content)
                return;

            var storyboard = new Storyboard();

            var fadeOut = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(fadeOut, window);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));

            // 确保内容有变换
            if (content.RenderTransform is not TransformGroup)
            {
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(1, 1));
                transformGroup.Children.Add(new TranslateTransform(0, 0));
                content.RenderTransform = transformGroup;
                content.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            var scaleX = new DoubleAnimation
            {
                To = 0.95,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(scaleX, content);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.Children[0].ScaleX"));

            var scaleY = new DoubleAnimation
            {
                To = 0.95,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            Storyboard.SetTarget(scaleY, content);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.Children[0].ScaleY"));

            storyboard.Children.Add(fadeOut);
            storyboard.Children.Add(scaleX);
            storyboard.Children.Add(scaleY);

            var tcs = new TaskCompletionSource<bool>();
            storyboard.Completed += (s, e) => tcs.SetResult(true);
            storyboard.Begin();
            await tcs.Task;
        }
    }
}
