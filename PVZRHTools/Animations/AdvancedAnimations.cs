using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace PVZRHTools.Animations
{
    /// <summary>
    /// 高级动画效果 - 视觉增强和交互反馈
    /// </summary>
    public static class AdvancedAnimations
    {
        /// <summary>
        /// 检查是否启用动画
        /// </summary>
        private static bool IsAnimationEnabled()
        {
            if (MainWindow.Instance?.DataContext is ModifierViewModel vm)
            {
                return vm.EnableAnimations;
            }
            return true;
        }

        #region 窗口拖动倾斜动画

        private static bool _isDragging = false;
        private static Point _lastMousePosition;
        private static SkewTransform? _windowSkew;

        /// <summary>
        /// 为窗口添加拖动时的倾斜效果
        /// </summary>
        public static void AddWindowDragTilt(Window window)
        {
            if (window.Content is not FrameworkElement content)
                return;

            TransformGroup transformGroup;
            if (content.RenderTransform is TransformGroup existingGroup)
            {
                transformGroup = existingGroup;
            }
            else
            {
                transformGroup = new TransformGroup();
                if (content.RenderTransform != null && content.RenderTransform != Transform.Identity)
                {
                    transformGroup.Children.Add(content.RenderTransform);
                }
                content.RenderTransform = transformGroup;
            }

            _windowSkew = new SkewTransform(0, 0);
            transformGroup.Children.Add(_windowSkew);
            content.RenderTransformOrigin = new Point(0.5, 0.5);

            window.PreviewMouseMove += (s, e) =>
            {
                if (!IsAnimationEnabled() || !_isDragging || _windowSkew == null)
                {
                    return;
                }

                var currentPos = e.GetPosition(window);
                var deltaX = currentPos.X - _lastMousePosition.X;
                var targetSkewX = Math.Clamp(deltaX * 0.15, -5, 5);

                var skewAnim = new DoubleAnimation
                {
                    To = targetSkewX,
                    Duration = TimeSpan.FromMilliseconds(50),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                _windowSkew.BeginAnimation(SkewTransform.AngleXProperty, skewAnim);
                _lastMousePosition = currentPos;
            };

            window.PreviewMouseLeftButtonDown += (s, e) =>
            {
                _isDragging = true;
                _lastMousePosition = e.GetPosition(window);
            };

            window.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_isDragging && _windowSkew != null)
                {
                    _isDragging = false;
                    if (!IsAnimationEnabled())
                    {
                        _windowSkew.AngleX = 0;
                        return;
                    }
                    var resetAnim = new DoubleAnimation
                    {
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 8 }
                    };
                    resetAnim.FillBehavior = FillBehavior.Stop;
                    resetAnim.Completed += (_, _) => _windowSkew.AngleX = 0;
                    _windowSkew.BeginAnimation(SkewTransform.AngleXProperty, resetAnim);
                }
            };
        }

        #endregion

        #region 按钮涟漪效果

        /// <summary>
        /// 为按钮添加 Material Design 风格的涟漪效果
        /// </summary>
        public static void AddRippleEffect(Button button)
        {
            var rippleCanvas = new Canvas { ClipToBounds = true, IsHitTestVisible = false };
            var originalContent = button.Content;
            var grid = new Grid();
            grid.Children.Add(rippleCanvas);
            
            if (originalContent is UIElement uiElement)
                grid.Children.Add(uiElement);
            else
                grid.Children.Add(new ContentPresenter { Content = originalContent });

            button.Content = grid;

            button.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (!IsAnimationEnabled()) return;
                var clickPoint = e.GetPosition(button);
                
                var ripple = new Ellipse
                {
                    Fill = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                    Width = 0, Height = 0,
                    RenderTransformOrigin = new Point(0.5, 0.5)
                };

                Canvas.SetLeft(ripple, clickPoint.X);
                Canvas.SetTop(ripple, clickPoint.Y);
                rippleCanvas.Children.Add(ripple);

                var maxSize = Math.Max(button.ActualWidth, button.ActualHeight) * 2.5;

                var sizeAnim = new DoubleAnimation { From = 0, To = maxSize, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var opacityAnim = new DoubleAnimation { From = 1, To = 0, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var leftAnim = new DoubleAnimation { From = clickPoint.X, To = clickPoint.X - maxSize / 2, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var topAnim = new DoubleAnimation { From = clickPoint.Y, To = clickPoint.Y - maxSize / 2, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };

                opacityAnim.Completed += (_, _) => rippleCanvas.Children.Remove(ripple);

                ripple.BeginAnimation(FrameworkElement.WidthProperty, sizeAnim);
                ripple.BeginAnimation(FrameworkElement.HeightProperty, sizeAnim);
                ripple.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
                ripple.BeginAnimation(Canvas.LeftProperty, leftAnim);
                ripple.BeginAnimation(Canvas.TopProperty, topAnim);
            };
        }

        #endregion

        #region 边框呼吸灯动画

        public static Storyboard CreateBorderBreathingAnimation(Border border, Color glowColor)
        {
            var effect = new DropShadowEffect { Color = glowColor, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.3 };
            border.Effect = effect;

            var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

            var blurAnim = new DoubleAnimation { From = 10, To = 25, Duration = TimeSpan.FromMilliseconds(1500), AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(blurAnim, border);
            Storyboard.SetTargetProperty(blurAnim, new PropertyPath("Effect.BlurRadius"));

            var opacityAnim = new DoubleAnimation { From = 0.3, To = 0.6, Duration = TimeSpan.FromMilliseconds(1500), AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Storyboard.SetTarget(opacityAnim, border);
            Storyboard.SetTargetProperty(opacityAnim, new PropertyPath("Effect.Opacity"));

            storyboard.Children.Add(blurAnim);
            storyboard.Children.Add(opacityAnim);

            return storyboard;
        }

        #endregion

        #region 进度条流光动画

        public static void AddProgressBarShimmer(ProgressBar progressBar)
        {
            if (!IsAnimationEnabled()) return;
            var shimmerBrush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0), SpreadMethod = GradientSpreadMethod.Repeat };
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(100, 255, 255, 255), 0.5));
            shimmerBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));

            var shimmerAnim = new DoubleAnimation { From = -1, To = 2, Duration = TimeSpan.FromMilliseconds(1500), RepeatBehavior = RepeatBehavior.Forever };
            shimmerBrush.BeginAnimation(LinearGradientBrush.OpacityProperty, shimmerAnim);
        }

        #endregion

        #region 数字滚动动画

        public static void AnimateNumberChange(TextBlock textBlock, double fromValue, double toValue, int durationMs = 500)
        {
            if (!IsAnimationEnabled())
            {
                textBlock.Text = toValue.ToString();
                return;
            }

            var currentValue = fromValue;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            var startTime = DateTime.Now;
            
            timer.Tick += (s, e) =>
            {
                var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                var progress = Math.Min(elapsed / durationMs, 1);
                var easedProgress = 1 - Math.Pow(1 - progress, 3);
                currentValue = fromValue + (toValue - fromValue) * easedProgress;
                textBlock.Text = Math.Round(currentValue).ToString();

                if (progress >= 1)
                {
                    timer.Stop();
                    textBlock.Text = toValue.ToString();
                }
            };
            timer.Start();
        }

        public static void AddNumberScrollAnimation(TextBlock textBlock)
        {
            double lastValue = 0;
            if (double.TryParse(textBlock.Text, out var initialValue))
                lastValue = initialValue;

            var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            dpd?.AddValueChanged(textBlock, (s, e) =>
            {
                if (double.TryParse(textBlock.Text, out var newValue) && Math.Abs(newValue - lastValue) > 0.01)
                {
                    var oldValue = lastValue;
                    lastValue = newValue;
                    AnimateNumberChange(textBlock, oldValue, newValue);
                }
            });
        }

        #endregion

        #region 磁吸效果

        public static void AddMagneticEffect(FrameworkElement element, double strength = 0.15)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            var translate = new TranslateTransform(0, 0);
            element.RenderTransform = translate;

            element.MouseMove += (s, e) =>
            {
                if (!IsAnimationEnabled()) return;
                var mousePos = e.GetPosition(element);
                var centerX = element.ActualWidth / 2;
                var centerY = element.ActualHeight / 2;
                var offsetX = (mousePos.X - centerX) * strength;
                var offsetY = (mousePos.Y - centerY) * strength;

                var animX = new DoubleAnimation { To = offsetX, Duration = TimeSpan.FromMilliseconds(100), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var animY = new DoubleAnimation { To = offsetY, Duration = TimeSpan.FromMilliseconds(100), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                translate.BeginAnimation(TranslateTransform.XProperty, animX);
                translate.BeginAnimation(TranslateTransform.YProperty, animY);
            };

            element.MouseLeave += (s, e) =>
            {
                if (!IsAnimationEnabled())
                {
                    translate.X = 0; translate.Y = 0;
                    return;
                }
                var animX = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 8 } };
                animX.FillBehavior = FillBehavior.Stop;
                animX.Completed += (_, _) => translate.X = 0;
                var animY = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 8 } };
                animY.FillBehavior = FillBehavior.Stop;
                animY.Completed += (_, _) => translate.Y = 0;
                translate.BeginAnimation(TranslateTransform.XProperty, animX);
                translate.BeginAnimation(TranslateTransform.YProperty, animY);
            };
        }

        #endregion

        #region 弹性边界滚动

        public static void AddElasticScrollBoundary(System.Windows.Controls.ScrollViewer scrollViewer)
        {
            if (scrollViewer.Content is not FrameworkElement content) return;

            var translate = new TranslateTransform(0, 0);
            content.RenderTransform = translate;
            bool isOverscrolling = false;
            double overscrollAmount = 0;

            scrollViewer.ScrollChanged += (s, e) =>
            {
                if (!IsAnimationEnabled()) return;
                bool atTop = e.VerticalOffset <= 0;
                bool atBottom = e.VerticalOffset >= scrollViewer.ScrollableHeight;

                if (atTop && e.VerticalChange < 0)
                {
                    overscrollAmount = Math.Min(overscrollAmount + Math.Abs(e.VerticalChange) * 0.3, 50);
                    isOverscrolling = true;
                    var anim = new DoubleAnimation { To = overscrollAmount, Duration = TimeSpan.FromMilliseconds(50) };
                    translate.BeginAnimation(TranslateTransform.YProperty, anim);
                }
                else if (atBottom && e.VerticalChange > 0)
                {
                    overscrollAmount = Math.Max(overscrollAmount - Math.Abs(e.VerticalChange) * 0.3, -50);
                    isOverscrolling = true;
                    var anim = new DoubleAnimation { To = overscrollAmount, Duration = TimeSpan.FromMilliseconds(50) };
                    translate.BeginAnimation(TranslateTransform.YProperty, anim);
                }
                else if (isOverscrolling)
                {
                    isOverscrolling = false;
                    overscrollAmount = 0;
                    var bounceBack = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(400), EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 6 } };
                    bounceBack.FillBehavior = FillBehavior.Stop;
                    bounceBack.Completed += (_, _) => translate.Y = 0;
                    translate.BeginAnimation(TranslateTransform.YProperty, bounceBack);
                }
            };
        }

        #endregion

        #region 3D 翻转切换

        public static void Play3DFlipAnimation(FrameworkElement element, bool flipHorizontal = true)
        {
            if (!IsAnimationEnabled()) return;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(1, 1);
            element.RenderTransform = scale;

            var property = flipHorizontal ? ScaleTransform.ScaleXProperty : ScaleTransform.ScaleYProperty;
            var flipOut = new DoubleAnimation { To = 0, Duration = TimeSpan.FromMilliseconds(150), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            var flipIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(150), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            flipOut.Completed += (s, e) => scale.BeginAnimation(property, flipIn);
            scale.BeginAnimation(property, flipOut);
        }

        #endregion

        #region 复制成功动画

        public static void PlayCopySuccessAnimation(FrameworkElement sourceElement)
        {
            if (!IsAnimationEnabled()) return;
            var parent = sourceElement.Parent as Panel;
            if (parent == null) return;

            var checkMark = new Path
            {
                Data = Geometry.Parse("M 2,6 L 5,9 L 10,3"),
                Stroke = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                StrokeThickness = 2, Width = 16, Height = 16, Opacity = 0,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(0.5, 0.5));
            transformGroup.Children.Add(new TranslateTransform(0, 0));
            checkMark.RenderTransform = transformGroup;

            var position = sourceElement.TranslatePoint(new Point(sourceElement.ActualWidth / 2, 0), parent);
            Canvas.SetLeft(checkMark, position.X - 8);
            Canvas.SetTop(checkMark, position.Y);

            if (parent is Canvas canvas)
                canvas.Children.Add(checkMark);
            else
            {
                var tempCanvas = new Canvas { IsHitTestVisible = false };
                parent.Children.Add(tempCanvas);
                tempCanvas.Children.Add(checkMark);
            }

            var fadeIn = new DoubleAnimation { From = 0, To = 1, Duration = TimeSpan.FromMilliseconds(150) };
            var scaleAnim = new DoubleAnimation { From = 0.5, To = 1.2, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new ElasticEase { EasingMode = EasingMode.EaseOut } };
            var moveUp = new DoubleAnimation { From = 0, To = -30, Duration = TimeSpan.FromMilliseconds(500), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            var fadeOut = new DoubleAnimation { To = 0, BeginTime = TimeSpan.FromMilliseconds(400), Duration = TimeSpan.FromMilliseconds(200) };
            fadeOut.Completed += (s, e) => { if (parent is Canvas c) c.Children.Remove(checkMark); };

            checkMark.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            ((ScaleTransform)((TransformGroup)checkMark.RenderTransform).Children[0]).BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            ((ScaleTransform)((TransformGroup)checkMark.RenderTransform).Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            ((TranslateTransform)((TransformGroup)checkMark.RenderTransform).Children[1]).BeginAnimation(TranslateTransform.YProperty, moveUp);
            checkMark.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }

        #endregion

        #region 状态切换色调变化

        public static void PlayStateChangeAnimation(FrameworkElement element, bool isEnabled)
        {
            if (!IsAnimationEnabled())
            {
                element.Opacity = isEnabled ? 1.0 : 0.5;
                return;
            }
            var targetOpacity = isEnabled ? 1.0 : 0.5;
            var opacityAnim = new DoubleAnimation { To = targetOpacity, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            element.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

            if (element is Control control && control.Background is SolidColorBrush brush)
            {
                var originalColor = brush.Color;
                var targetColor = isEnabled ? originalColor : Color.FromArgb(originalColor.A, (byte)(originalColor.R * 0.7 + 128 * 0.3), (byte)(originalColor.G * 0.7 + 128 * 0.3), (byte)(originalColor.B * 0.7 + 128 * 0.3));
                var colorAnim = new ColorAnimation { To = targetColor, Duration = TimeSpan.FromMilliseconds(300), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
            }
        }

        #endregion
    }
}
