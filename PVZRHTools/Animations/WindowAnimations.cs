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
        private static WindowState _previousState = WindowState.Normal;

        /// <summary>
        /// 检查是否启用动画
        /// </summary>
        private static bool IsAnimationEnabled(Window window)
        {
            if (window.DataContext is ModifierViewModel vm)
            {
                return vm.EnableAnimations;
            }
            return true; // 默认启用
        }

        /// <summary>
        /// 窗口启动动画 - macOS/Windows 11 风格
        /// 对窗口内容应用动画而非窗口本身
        /// </summary>
        public static void PlayStartupAnimation(Window window)
        {
            // 检查是否启用动画
            if (!IsAnimationEnabled(window))
            {
                window.Opacity = 1;
                return;
            }

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
            // 检查是否启用动画
            if (!IsAnimationEnabled(window))
                return;

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
            // 检查是否启用动画
            if (!IsAnimationEnabled(window))
                return;

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

        /// <summary>
        /// 添加窗口状态变化动画（最小化/还原）
        /// </summary>
        public static void AddWindowStateAnimation(Window window)
        {
            _previousState = window.WindowState;

            window.StateChanged += (s, e) =>
            {
                // 检查是否启用动画
                if (!IsAnimationEnabled(window))
                    return;

                if (window.Content is not FrameworkElement content)
                    return;

                // 确保有变换
                EnsureTransformGroup(content);

                if (_previousState == WindowState.Minimized && window.WindowState == WindowState.Normal)
                {
                    // 从最小化还原 - 弹出动画
                    PlayRestoreAnimation(window, content);
                }
                else if (window.WindowState == WindowState.Maximized && _previousState == WindowState.Normal)
                {
                    // 最大化动画
                    PlayMaximizeAnimation(content);
                }
                else if (window.WindowState == WindowState.Normal && _previousState == WindowState.Maximized)
                {
                    // 从最大化还原
                    PlayUnmaximizeAnimation(content);
                }

                _previousState = window.WindowState;
            };
        }

        private static void EnsureTransformGroup(FrameworkElement content)
        {
            if (content.RenderTransform is not TransformGroup)
            {
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(1, 1));
                transformGroup.Children.Add(new TranslateTransform(0, 0));
                content.RenderTransform = transformGroup;
                content.RenderTransformOrigin = new Point(0.5, 0.5);
            }
        }


        private static void PlayRestoreAnimation(Window window, FrameworkElement content)
        {
            var storyboard = new Storyboard();

            // 从小变大 + 淡入
            window.Opacity = 0;

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fadeIn, window);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));

            var scaleX = new DoubleAnimationUsingKeyFrames();
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1.02, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), 
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            scaleX.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), 
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            Storyboard.SetTarget(scaleX, content);
            Storyboard.SetTargetProperty(scaleX, new PropertyPath("RenderTransform.Children[0].ScaleX"));

            var scaleY = new DoubleAnimationUsingKeyFrames();
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(0.8, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1.02, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200)), 
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            scaleY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), 
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            Storyboard.SetTarget(scaleY, content);
            Storyboard.SetTargetProperty(scaleY, new PropertyPath("RenderTransform.Children[0].ScaleY"));

            // 从下方滑入
            var translateY = new DoubleAnimationUsingKeyFrames();
            translateY.KeyFrames.Add(new EasingDoubleKeyFrame(30, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            translateY.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300)), 
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            Storyboard.SetTarget(translateY, content);
            Storyboard.SetTargetProperty(translateY, new PropertyPath("RenderTransform.Children[1].Y"));

            storyboard.Children.Add(fadeIn);
            storyboard.Children.Add(scaleX);
            storyboard.Children.Add(scaleY);
            storyboard.Children.Add(translateY);

            storyboard.Begin();
        }

        private static void PlayMaximizeAnimation(FrameworkElement content)
        {
            var scaleAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(250) };
            scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
            scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.03, KeyTime.FromPercent(0.4), new CubicEase { EasingMode = EasingMode.EaseOut }));
            scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
            scaleAnim.FillBehavior = FillBehavior.Stop;

            if (content.RenderTransform is TransformGroup tg && tg.Children[0] is ScaleTransform scale)
            {
                scaleAnim.Completed += (_, _) => { scale.ScaleX = 1; scale.ScaleY = 1; };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            }
        }

        private static void PlayUnmaximizeAnimation(FrameworkElement content)
        {
            var scaleAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(200) };
            scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
            scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.97, KeyTime.FromPercent(0.3), new CubicEase { EasingMode = EasingMode.EaseOut }));
            scaleAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
            scaleAnim.FillBehavior = FillBehavior.Stop;

            if (content.RenderTransform is TransformGroup tg && tg.Children[0] is ScaleTransform scale)
            {
                scaleAnim.Completed += (_, _) => { scale.ScaleX = 1; scale.ScaleY = 1; };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            }
        }
    }
}
