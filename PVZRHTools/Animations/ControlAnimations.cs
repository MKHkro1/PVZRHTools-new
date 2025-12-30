using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace PVZRHTools.Animations
{
    /// <summary>
    /// 控件交互动画 - 高级 OS 风格
    /// </summary>
    public static class ControlAnimations
    {
        /// <summary>
        /// 为按钮添加点击缩放动画
        /// </summary>
        public static void AddButtonPressAnimation(Button button)
        {
            if (button.RenderTransform is not ScaleTransform)
            {
                button.RenderTransform = new ScaleTransform(1, 1);
                button.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            button.PreviewMouseLeftButtonDown += (s, e) =>
            {
                var scale = (ScaleTransform)button.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, 
                    CreateQuickAnimation(0.95, 80));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, 
                    CreateQuickAnimation(0.95, 80));
            };

            button.PreviewMouseLeftButtonUp += (s, e) =>
            {
                var scale = (ScaleTransform)button.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, 
                    CreateSpringAnimation(1.0, 150));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, 
                    CreateSpringAnimation(1.0, 150));
            };

            button.MouseLeave += (s, e) =>
            {
                var scale = (ScaleTransform)button.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, 
                    CreateQuickAnimation(1.0, 100));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, 
                    CreateQuickAnimation(1.0, 100));
            };
        }

        /// <summary>
        /// 为元素添加悬停发光效果
        /// </summary>
        public static void AddHoverGlow(UIElement element, Color glowColor)
        {
            var dropShadow = new DropShadowEffect
            {
                Color = glowColor,
                BlurRadius = 0,
                ShadowDepth = 0,
                Opacity = 0
            };
            
            if (element is FrameworkElement fe)
                fe.Effect = dropShadow;

            element.MouseEnter += (s, e) =>
            {
                dropShadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
                    CreateQuickAnimation(15, 200));
                dropShadow.BeginAnimation(DropShadowEffect.OpacityProperty,
                    CreateQuickAnimation(0.6, 200));
            };

            element.MouseLeave += (s, e) =>
            {
                dropShadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty,
                    CreateQuickAnimation(0, 200));
                dropShadow.BeginAnimation(DropShadowEffect.OpacityProperty,
                    CreateQuickAnimation(0, 200));
            };
        }

        /// <summary>
        /// 为元素添加悬停上浮效果
        /// </summary>
        public static void AddHoverLift(UIElement element)
        {
            if (element.RenderTransform is not TranslateTransform)
            {
                element.RenderTransform = new TranslateTransform(0, 0);
            }

            element.MouseEnter += (s, e) =>
            {
                var translate = (TranslateTransform)element.RenderTransform;
                translate.BeginAnimation(TranslateTransform.YProperty,
                    CreateSpringAnimation(-3, 200));
            };

            element.MouseLeave += (s, e) =>
            {
                var translate = (TranslateTransform)element.RenderTransform;
                translate.BeginAnimation(TranslateTransform.YProperty,
                    CreateSpringAnimation(0, 200));
            };
        }

        /// <summary>
        /// TabItem 切换动画
        /// </summary>
        public static void AnimateTabSwitch(FrameworkElement content)
        {
            content.Opacity = 0;
            var translate = new TranslateTransform(20, 0);
            content.RenderTransform = translate;

            var fadeIn = CreateQuickAnimation(1, 250);
            var slideIn = CreateSpringAnimation(0, 300);

            content.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            translate.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }

        /// <summary>
        /// 列表项入场动画
        /// </summary>
        public static void AnimateListItemEntrance(FrameworkElement item, int index)
        {
            item.Opacity = 0;
            var translate = new TranslateTransform(0, 20);
            item.RenderTransform = translate;

            var delay = TimeSpan.FromMilliseconds(index * 30);

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300),
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var slideUp = new DoubleAnimation
            {
                From = 20,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(350),
                BeginTime = delay,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            item.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            translate.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        /// <summary>
        /// 成功反馈动画（绿色闪烁）
        /// </summary>
        public static void PlaySuccessFeedback(FrameworkElement element)
        {
            var originalBackground = (element as Control)?.Background;
            
            var colorAnim = new ColorAnimation
            {
                To = Color.FromArgb(60, 76, 175, 80),
                Duration = TimeSpan.FromMilliseconds(150),
                AutoReverse = true
            };

            if (element is Control control && control.Background is SolidColorBrush brush)
            {
                var animBrush = new SolidColorBrush(brush.Color);
                control.Background = animBrush;
                animBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
            }
        }

        /// <summary>
        /// 错误抖动动画
        /// </summary>
        public static void PlayErrorShake(FrameworkElement element)
        {
            var translate = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
            element.RenderTransform = translate;

            var shake = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(400)
            };
            
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(-8, KeyTime.FromPercent(0.1)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(8, KeyTime.FromPercent(0.3)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(-6, KeyTime.FromPercent(0.5)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(6, KeyTime.FromPercent(0.7)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(-2, KeyTime.FromPercent(0.9)));
            shake.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));

            translate.BeginAnimation(TranslateTransform.XProperty, shake);
        }

        /// <summary>
        /// 为 TabItem 添加悬停和选中动画
        /// </summary>
        public static void AddTabItemAnimation(TabItem tabItem)
        {
            // 只使用缩放变换，不使用位移，避免位置偏移
            tabItem.RenderTransformOrigin = new Point(0.5, 0.5);
            tabItem.RenderTransform = new ScaleTransform(1, 1);

            // 悬停动画 - 只缩放，不位移
            tabItem.MouseEnter += (s, e) =>
            {
                if (!tabItem.IsSelected)
                {
                    var scale = (ScaleTransform)tabItem.RenderTransform;
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateQuickAnimation(1.03, 150));
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateQuickAnimation(1.03, 150));
                }
            };

            tabItem.MouseLeave += (s, e) =>
            {
                var scale = (ScaleTransform)tabItem.RenderTransform;
                // 使用 FillBehavior.Stop 确保动画结束后回到原始值
                var animX = CreateQuickAnimation(1, 150);
                var animY = CreateQuickAnimation(1, 150);
                animX.FillBehavior = FillBehavior.Stop;
                animY.FillBehavior = FillBehavior.Stop;
                animX.Completed += (_, _) => scale.ScaleX = 1;
                animY.Completed += (_, _) => scale.ScaleY = 1;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
            };

            // 点击动画
            tabItem.PreviewMouseLeftButtonDown += (s, e) =>
            {
                var scale = (ScaleTransform)tabItem.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateQuickAnimation(0.95, 80));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateQuickAnimation(0.95, 80));
            };

            tabItem.PreviewMouseLeftButtonUp += (s, e) =>
            {
                var scale = (ScaleTransform)tabItem.RenderTransform;
                // 点击释放后确保回到原位
                var animX = CreateQuickAnimation(1, 150);
                var animY = CreateQuickAnimation(1, 150);
                animX.FillBehavior = FillBehavior.Stop;
                animY.FillBehavior = FillBehavior.Stop;
                animX.Completed += (_, _) => scale.ScaleX = 1;
                animY.Completed += (_, _) => scale.ScaleY = 1;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
            };
        }

        /// <summary>
        /// 为 TabControl 添加内容切换动画
        /// </summary>
        public static void AddTabControlAnimation(TabControl tabControl)
        {
            tabControl.SelectionChanged += (s, e) =>
            {
                if (e.Source == tabControl && tabControl.SelectedContent is FrameworkElement content)
                {
                    // 内容淡入 + 滑入动画
                    content.Opacity = 0;
                    content.RenderTransformOrigin = new Point(0, 0.5);
                    var translate = new TranslateTransform(25, 0);
                    content.RenderTransform = translate;

                    var fadeIn = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(250),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    var slideIn = new DoubleAnimation
                    {
                        From = 25,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    content.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    translate.BeginAnimation(TranslateTransform.XProperty, slideIn);
                }
            };
        }

        /// <summary>
        /// 为 Expander 添加展开/折叠动画和悬停效果
        /// </summary>
        public static void AddExpanderAnimation(Expander expander)
        {
            // 设置变换
            expander.RenderTransformOrigin = new Point(0.5, 0);
            expander.RenderTransform = new ScaleTransform(1, 1);

            // 悬停动画 - 轻微放大
            expander.MouseEnter += (s, e) =>
            {
                var scale = (ScaleTransform)expander.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateQuickAnimation(1.008, 150));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateQuickAnimation(1.008, 150));
            };

            expander.MouseLeave += (s, e) =>
            {
                var scale = (ScaleTransform)expander.RenderTransform;
                var animX = CreateQuickAnimation(1, 150);
                var animY = CreateQuickAnimation(1, 150);
                animX.FillBehavior = FillBehavior.Stop;
                animY.FillBehavior = FillBehavior.Stop;
                animX.Completed += (_, _) => scale.ScaleX = 1;
                animY.Completed += (_, _) => scale.ScaleY = 1;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
            };

            // 展开时整体动画 - 从顶部展开效果
            expander.Expanded += (s, e) =>
            {
                // 对整个 Expander 做一个弹性缩放动画
                var scale = (ScaleTransform)expander.RenderTransform;
                
                var bounceY = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(350)
                };
                bounceY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
                bounceY.KeyFrames.Add(new EasingDoubleKeyFrame(1.02, KeyTime.FromPercent(0.3), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceY.KeyFrames.Add(new EasingDoubleKeyFrame(0.99, KeyTime.FromPercent(0.6), new CubicEase { EasingMode = EasingMode.EaseInOut }));
                bounceY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceY.FillBehavior = FillBehavior.Stop;
                bounceY.Completed += (_, _) => scale.ScaleY = 1;
                
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounceY);

                // 内容淡入动画
                if (expander.Content is FrameworkElement content)
                {
                    content.Opacity = 0;
                    var transformGroup = new TransformGroup();
                    transformGroup.Children.Add(new ScaleTransform(1, 0.8));
                    transformGroup.Children.Add(new TranslateTransform(0, -15));
                    content.RenderTransform = transformGroup;
                    content.RenderTransformOrigin = new Point(0.5, 0);

                    var fadeIn = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    var scaleYAnim = new DoubleAnimation
                    {
                        From = 0.8,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(350),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    var slideDown = new DoubleAnimation
                    {
                        From = -15,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(350),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    content.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                    ((ScaleTransform)((TransformGroup)content.RenderTransform).Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
                    ((TranslateTransform)((TransformGroup)content.RenderTransform).Children[1]).BeginAnimation(TranslateTransform.YProperty, slideDown);
                }
            };

            // 折叠时动画
            expander.Collapsed += (s, e) =>
            {
                var scale = (ScaleTransform)expander.RenderTransform;
                
                var shrink = new DoubleAnimationUsingKeyFrames
                {
                    Duration = TimeSpan.FromMilliseconds(200)
                };
                shrink.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
                shrink.KeyFrames.Add(new EasingDoubleKeyFrame(0.98, KeyTime.FromPercent(0.5), new CubicEase { EasingMode = EasingMode.EaseOut }));
                shrink.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
                shrink.FillBehavior = FillBehavior.Stop;
                shrink.Completed += (_, _) => scale.ScaleY = 1;
                
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
            };

            // 点击头部时的反馈动画
            expander.PreviewMouseLeftButtonDown += (s, e) =>
            {
                var scale = (ScaleTransform)expander.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateQuickAnimation(0.985, 80));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateQuickAnimation(0.985, 80));
            };

            expander.PreviewMouseLeftButtonUp += (s, e) =>
            {
                var scale = (ScaleTransform)expander.RenderTransform;
                var animX = CreateQuickAnimation(1, 150);
                var animY = CreateQuickAnimation(1, 150);
                animX.FillBehavior = FillBehavior.Stop;
                animY.FillBehavior = FillBehavior.Stop;
                animX.Completed += (_, _) => scale.ScaleX = 1;
                animY.Completed += (_, _) => scale.ScaleY = 1;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
            };
        }

        private static DoubleAnimation CreateQuickAnimation(double to, int durationMs)
        {
            return new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
        }

        private static DoubleAnimation CreateSpringAnimation(double to, int durationMs)
        {
            return new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new ElasticEase 
                { 
                    EasingMode = EasingMode.EaseOut,
                    Oscillations = 1,
                    Springiness = 8
                }
            };
        }
    }
}
