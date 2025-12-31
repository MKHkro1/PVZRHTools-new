using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace PVZRHTools.Animations
{
    /// <summary>
    /// 控件交互动画 - 高级 OS 风格
    /// </summary>
    public static class ControlAnimations
    {
        #region CheckBox/ToggleButton 切换动画
        
        /// <summary>
        /// 为 CheckBox 添加切换动画 - 弹性缩放和颜色过渡
        /// </summary>
        public static void AddCheckBoxAnimation(CheckBox checkBox)
        {
            checkBox.RenderTransformOrigin = new Point(0.5, 0.5);
            checkBox.RenderTransform = new ScaleTransform(1, 1);

            checkBox.Checked += (s, e) =>
            {
                var scale = (ScaleTransform)checkBox.RenderTransform;
                // 弹性放大效果
                var bounceAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(400) };
                bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.2, KeyTime.FromPercent(0.3), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.9, KeyTime.FromPercent(0.6), new CubicEase { EasingMode = EasingMode.EaseInOut }));
                bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 8 }));
                bounceAnim.FillBehavior = FillBehavior.Stop;
                bounceAnim.Completed += (_, _) => { scale.ScaleX = 1; scale.ScaleY = 1; };
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounceAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounceAnim);
            };

            checkBox.Unchecked += (s, e) =>
            {
                var scale = (ScaleTransform)checkBox.RenderTransform;
                var shrinkAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(250) };
                shrinkAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.85, KeyTime.FromPercent(0.4), new CubicEase { EasingMode = EasingMode.EaseOut }));
                shrinkAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
                shrinkAnim.FillBehavior = FillBehavior.Stop;
                shrinkAnim.Completed += (_, _) => { scale.ScaleX = 1; scale.ScaleY = 1; };
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkAnim);
            };

            // 悬停效果
            checkBox.MouseEnter += (s, e) =>
            {
                var scale = (ScaleTransform)checkBox.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateQuickAnimation(1.05, 150));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateQuickAnimation(1.05, 150));
            };

            checkBox.MouseLeave += (s, e) =>
            {
                var scale = (ScaleTransform)checkBox.RenderTransform;
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
        /// 为 ToggleButton 添加切换动画
        /// </summary>
        public static void AddToggleButtonAnimation(ToggleButton toggleButton)
        {
            toggleButton.RenderTransformOrigin = new Point(0.5, 0.5);
            toggleButton.RenderTransform = new ScaleTransform(1, 1);

            toggleButton.Checked += (s, e) =>
            {
                var scale = (ScaleTransform)toggleButton.RenderTransform;
                var bounceAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(350) };
                bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromPercent(0.35), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 1, Springiness = 6 }));
                bounceAnim.FillBehavior = FillBehavior.Stop;
                bounceAnim.Completed += (_, _) => { scale.ScaleX = 1; scale.ScaleY = 1; };
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounceAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounceAnim);
            };

            toggleButton.Unchecked += (s, e) =>
            {
                var scale = (ScaleTransform)toggleButton.RenderTransform;
                var shrinkAnim = CreateQuickAnimation(1, 200);
                shrinkAnim.From = 0.9;
                shrinkAnim.FillBehavior = FillBehavior.Stop;
                shrinkAnim.Completed += (_, _) => { scale.ScaleX = 1; scale.ScaleY = 1; };
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkAnim);
            };
        }

        #endregion

        #region TextBox 聚焦动画

        /// <summary>
        /// 为 TextBox 添加聚焦动画 - 边框发光、轻微放大
        /// </summary>
        public static void AddTextBoxFocusAnimation(TextBox textBox)
        {
            textBox.RenderTransformOrigin = new Point(0.5, 0.5);
            textBox.RenderTransform = new ScaleTransform(1, 1);
            
            // 添加发光效果
            var glowEffect = new DropShadowEffect
            {
                Color = Color.FromRgb(255, 105, 180),
                BlurRadius = 0,
                ShadowDepth = 0,
                Opacity = 0
            };
            textBox.Effect = glowEffect;

            textBox.GotFocus += (s, e) =>
            {
                var scale = (ScaleTransform)textBox.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateQuickAnimation(1.02, 200));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateQuickAnimation(1.02, 200));
                
                glowEffect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, CreateQuickAnimation(12, 250));
                glowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, CreateQuickAnimation(0.6, 250));
            };

            textBox.LostFocus += (s, e) =>
            {
                var scale = (ScaleTransform)textBox.RenderTransform;
                var animX = CreateQuickAnimation(1, 200);
                var animY = CreateQuickAnimation(1, 200);
                animX.FillBehavior = FillBehavior.Stop;
                animY.FillBehavior = FillBehavior.Stop;
                animX.Completed += (_, _) => scale.ScaleX = 1;
                animY.Completed += (_, _) => scale.ScaleY = 1;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
                
                var blurAnim = CreateQuickAnimation(0, 200);
                var opacityAnim = CreateQuickAnimation(0, 200);
                blurAnim.FillBehavior = FillBehavior.Stop;
                opacityAnim.FillBehavior = FillBehavior.Stop;
                blurAnim.Completed += (_, _) => glowEffect.BlurRadius = 0;
                opacityAnim.Completed += (_, _) => glowEffect.Opacity = 0;
                glowEffect.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blurAnim);
                glowEffect.BeginAnimation(DropShadowEffect.OpacityProperty, opacityAnim);
            };
        }

        #endregion

        #region 数值变化脉冲动画

        /// <summary>
        /// 播放数值变化脉冲动画
        /// </summary>
        public static void PlayValueChangePulse(FrameworkElement element)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            if (element.RenderTransform is not ScaleTransform)
                element.RenderTransform = new ScaleTransform(1, 1);

            var scale = (ScaleTransform)element.RenderTransform;
            
            var pulseAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(300) };
            pulseAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromPercent(0.3), new CubicEase { EasingMode = EasingMode.EaseOut }));
            pulseAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
            pulseAnim.FillBehavior = FillBehavior.Stop;
            pulseAnim.Completed += (_, _) => { scale.ScaleX = 1; scale.ScaleY = 1; };
            
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnim);

            // 背景闪烁效果
            if (element is Control control)
            {
                var originalBg = control.Background;
                var flashBrush = new SolidColorBrush(Color.FromArgb(80, 255, 105, 180));
                control.Background = flashBrush;
                
                var colorAnim = new ColorAnimation
                {
                    From = Color.FromArgb(80, 255, 105, 180),
                    To = Colors.Transparent,
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                colorAnim.Completed += (_, _) => control.Background = originalBg;
                flashBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
            }
        }

        #endregion

        #region 列表项悬停动画

        /// <summary>
        /// 为 ListBoxItem 添加悬停滑入高亮动画
        /// </summary>
        public static void AddListItemHoverAnimation(ListBoxItem item)
        {
            item.RenderTransformOrigin = new Point(0, 0.5);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(1, 1));
            transformGroup.Children.Add(new TranslateTransform(0, 0));
            item.RenderTransform = transformGroup;

            item.MouseEnter += (s, e) =>
            {
                var scale = (ScaleTransform)((TransformGroup)item.RenderTransform).Children[0];
                var translate = (TranslateTransform)((TransformGroup)item.RenderTransform).Children[1];
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateQuickAnimation(1.02, 150));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateQuickAnimation(1.02, 150));
                translate.BeginAnimation(TranslateTransform.XProperty, CreateQuickAnimation(4, 150));
            };

            item.MouseLeave += (s, e) =>
            {
                var scale = (ScaleTransform)((TransformGroup)item.RenderTransform).Children[0];
                var translate = (TranslateTransform)((TransformGroup)item.RenderTransform).Children[1];
                
                var animScaleX = CreateQuickAnimation(1, 150);
                var animScaleY = CreateQuickAnimation(1, 150);
                var animTransX = CreateQuickAnimation(0, 150);
                animScaleX.FillBehavior = FillBehavior.Stop;
                animScaleY.FillBehavior = FillBehavior.Stop;
                animTransX.FillBehavior = FillBehavior.Stop;
                animScaleX.Completed += (_, _) => scale.ScaleX = 1;
                animScaleY.Completed += (_, _) => scale.ScaleY = 1;
                animTransX.Completed += (_, _) => translate.X = 0;
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, animScaleX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, animScaleY);
                translate.BeginAnimation(TranslateTransform.XProperty, animTransX);
            };
        }

        /// <summary>
        /// 为 DataGrid 行添加悬停动画
        /// </summary>
        public static void AddDataGridRowAnimation(DataGridRow row)
        {
            row.RenderTransformOrigin = new Point(0.5, 0.5);
            row.RenderTransform = new ScaleTransform(1, 1);

            row.MouseEnter += (s, e) =>
            {
                var scale = (ScaleTransform)row.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateQuickAnimation(1.005, 150));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateQuickAnimation(1.01, 150));
            };

            row.MouseLeave += (s, e) =>
            {
                var scale = (ScaleTransform)row.RenderTransform;
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

        #endregion

        #region 右键菜单弹出动画

        /// <summary>
        /// 为 ContextMenu 添加弹出动画
        /// </summary>
        public static void AddContextMenuAnimation(ContextMenu menu)
        {
            menu.RenderTransformOrigin = new Point(0, 0);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(new ScaleTransform(0.8, 0.8));
            transformGroup.Children.Add(new TranslateTransform(0, -10));
            menu.RenderTransform = transformGroup;
            menu.Opacity = 0;

            menu.Opened += (s, e) =>
            {
                var scale = (ScaleTransform)((TransformGroup)menu.RenderTransform).Children[0];
                var translate = (TranslateTransform)((TransformGroup)menu.RenderTransform).Children[1];

                var fadeIn = CreateQuickAnimation(1, 200);
                var scaleXAnim = CreateSpringAnimation(1, 300);
                var scaleYAnim = CreateSpringAnimation(1, 300);
                var translateAnim = CreateQuickAnimation(0, 250);

                menu.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
                translate.BeginAnimation(TranslateTransform.YProperty, translateAnim);
            };

            menu.Closed += (s, e) =>
            {
                // 重置状态
                var scale = (ScaleTransform)((TransformGroup)menu.RenderTransform).Children[0];
                var translate = (TranslateTransform)((TransformGroup)menu.RenderTransform).Children[1];
                scale.ScaleX = 0.8;
                scale.ScaleY = 0.8;
                translate.Y = -10;
                menu.Opacity = 0;
            };
        }

        #endregion

        #region Tooltip 淡入动画

        /// <summary>
        /// 为 ToolTip 添加淡入缩放动画
        /// </summary>
        public static void AddToolTipAnimation(ToolTip toolTip)
        {
            toolTip.RenderTransformOrigin = new Point(0.5, 1);
            toolTip.RenderTransform = new ScaleTransform(0.9, 0.9);
            toolTip.Opacity = 0;

            toolTip.Opened += (s, e) =>
            {
                var scale = (ScaleTransform)toolTip.RenderTransform;
                
                var fadeIn = CreateQuickAnimation(1, 200);
                var scaleAnim = CreateQuickAnimation(1, 250);
                
                toolTip.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
            };

            toolTip.Closed += (s, e) =>
            {
                var scale = (ScaleTransform)toolTip.RenderTransform;
                scale.ScaleX = 0.9;
                scale.ScaleY = 0.9;
                toolTip.Opacity = 0;
            };
        }

        #endregion

        #region 加载状态脉冲动画

        /// <summary>
        /// 播放呼吸灯脉冲动画（用于加载状态）
        /// </summary>
        public static Storyboard CreatePulseAnimation(FrameworkElement element)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new ScaleTransform(1, 1);

            var storyboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

            var scaleXAnim = new DoubleAnimation
            {
                From = 1,
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(800),
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(scaleXAnim, element);
            Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath("RenderTransform.ScaleX"));

            var scaleYAnim = new DoubleAnimation
            {
                From = 1,
                To = 1.05,
                Duration = TimeSpan.FromMilliseconds(800),
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(scaleYAnim, element);
            Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath("RenderTransform.ScaleY"));

            var opacityAnim = new DoubleAnimation
            {
                From = 1,
                To = 0.7,
                Duration = TimeSpan.FromMilliseconds(800),
                AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(opacityAnim, element);
            Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(UIElement.OpacityProperty));

            storyboard.Children.Add(scaleXAnim);
            storyboard.Children.Add(scaleYAnim);
            storyboard.Children.Add(opacityAnim);

            return storyboard;
        }

        /// <summary>
        /// 停止脉冲动画并恢复原状
        /// </summary>
        public static void StopPulseAnimation(FrameworkElement element, Storyboard storyboard)
        {
            storyboard.Stop();
            element.Opacity = 1;
            if (element.RenderTransform is ScaleTransform scale)
            {
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            }
        }

        #endregion

        #region ComboBox 下拉动画

        /// <summary>
        /// 为 ComboBox 添加下拉动画
        /// </summary>
        public static void AddComboBoxAnimation(System.Windows.Controls.ComboBox comboBox)
        {
            comboBox.RenderTransformOrigin = new Point(0.5, 0.5);
            comboBox.RenderTransform = new ScaleTransform(1, 1);

            comboBox.DropDownOpened += (s, e) =>
            {
                var scale = (ScaleTransform)comboBox.RenderTransform;
                var bounceAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(300) };
                bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.03, KeyTime.FromPercent(0.4), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceAnim.FillBehavior = FillBehavior.Stop;
                bounceAnim.Completed += (_, _) => { scale.ScaleX = 1; scale.ScaleY = 1; };
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounceAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounceAnim);
            };

            comboBox.DropDownClosed += (s, e) =>
            {
                var scale = (ScaleTransform)comboBox.RenderTransform;
                var shrinkAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(200) };
                shrinkAnim.KeyFrames.Add(new EasingDoubleKeyFrame(0.98, KeyTime.FromPercent(0.3), new CubicEase { EasingMode = EasingMode.EaseOut }));
                shrinkAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
                shrinkAnim.FillBehavior = FillBehavior.Stop;
                shrinkAnim.Completed += (_, _) => { scale.ScaleX = 1; scale.ScaleY = 1; };
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkAnim);
            };
        }

        #endregion

        #region Slider 滑动动画

        /// <summary>
        /// 为 Slider 添加滑动动画
        /// </summary>
        public static void AddSliderAnimation(Slider slider)
        {
            slider.RenderTransformOrigin = new Point(0.5, 0.5);
            slider.RenderTransform = new ScaleTransform(1, 1);

            slider.ValueChanged += (s, e) =>
            {
                var scale = (ScaleTransform)slider.RenderTransform;
                var pulseAnim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(200) };
                pulseAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.02, KeyTime.FromPercent(0.5), new CubicEase { EasingMode = EasingMode.EaseOut }));
                pulseAnim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
                pulseAnim.FillBehavior = FillBehavior.Stop;
                pulseAnim.Completed += (_, _) => { scale.ScaleX = 1; scale.ScaleY = 1; };
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulseAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulseAnim);
            };

            slider.MouseEnter += (s, e) =>
            {
                var scale = (ScaleTransform)slider.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateQuickAnimation(1.1, 150));
            };

            slider.MouseLeave += (s, e) =>
            {
                var scale = (ScaleTransform)slider.RenderTransform;
                var anim = CreateQuickAnimation(1, 150);
                anim.FillBehavior = FillBehavior.Stop;
                anim.Completed += (_, _) => scale.ScaleY = 1;
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            };
        }

        #endregion

        #region 原有动画方法
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

            // 展开时整体动画 - 更明显的弹性效果
            expander.Expanded += (s, e) =>
            {
                var scale = (ScaleTransform)expander.RenderTransform;
                
                // 整体弹性缩放动画
                var bounceX = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(400) };
                bounceX.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
                bounceX.KeyFrames.Add(new EasingDoubleKeyFrame(1.015, KeyTime.FromPercent(0.25), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceX.KeyFrames.Add(new EasingDoubleKeyFrame(0.995, KeyTime.FromPercent(0.55), new CubicEase { EasingMode = EasingMode.EaseInOut }));
                bounceX.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceX.FillBehavior = FillBehavior.Stop;
                bounceX.Completed += (_, _) => scale.ScaleX = 1;
                
                var bounceY = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(400) };
                bounceY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
                bounceY.KeyFrames.Add(new EasingDoubleKeyFrame(1.03, KeyTime.FromPercent(0.25), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceY.KeyFrames.Add(new EasingDoubleKeyFrame(0.99, KeyTime.FromPercent(0.55), new CubicEase { EasingMode = EasingMode.EaseInOut }));
                bounceY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
                bounceY.FillBehavior = FillBehavior.Stop;
                bounceY.Completed += (_, _) => scale.ScaleY = 1;
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, bounceX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, bounceY);

                // 尝试对内容应用动画
                expander.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (expander.Content is FrameworkElement content)
                    {
                        content.Opacity = 0;
                        content.RenderTransformOrigin = new Point(0.5, 0);
                        var transformGroup = new TransformGroup();
                        transformGroup.Children.Add(new ScaleTransform(1, 0.5));
                        transformGroup.Children.Add(new TranslateTransform(0, -20));
                        content.RenderTransform = transformGroup;

                        var fadeIn = new DoubleAnimation
                        {
                            From = 0, To = 1,
                            Duration = TimeSpan.FromMilliseconds(350),
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };

                        var scaleYAnim = new DoubleAnimation
                        {
                            From = 0.5, To = 1,
                            Duration = TimeSpan.FromMilliseconds(400),
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };

                        var slideDown = new DoubleAnimation
                        {
                            From = -20, To = 0,
                            Duration = TimeSpan.FromMilliseconds(400),
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };

                        content.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                        ((ScaleTransform)((TransformGroup)content.RenderTransform).Children[0]).BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
                        ((TranslateTransform)((TransformGroup)content.RenderTransform).Children[1]).BeginAnimation(TranslateTransform.YProperty, slideDown);
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            };

            // 折叠时动画
            expander.Collapsed += (s, e) =>
            {
                var scale = (ScaleTransform)expander.RenderTransform;
                
                var shrinkX = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(250) };
                shrinkX.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
                shrinkX.KeyFrames.Add(new EasingDoubleKeyFrame(0.99, KeyTime.FromPercent(0.4), new CubicEase { EasingMode = EasingMode.EaseOut }));
                shrinkX.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
                shrinkX.FillBehavior = FillBehavior.Stop;
                shrinkX.Completed += (_, _) => scale.ScaleX = 1;
                
                var shrinkY = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(250) };
                shrinkY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0)));
                shrinkY.KeyFrames.Add(new EasingDoubleKeyFrame(0.97, KeyTime.FromPercent(0.4), new CubicEase { EasingMode = EasingMode.EaseOut }));
                shrinkY.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1), new CubicEase { EasingMode = EasingMode.EaseOut }));
                shrinkY.FillBehavior = FillBehavior.Stop;
                shrinkY.Completed += (_, _) => scale.ScaleY = 1;
                
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrinkX);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrinkY);
            };

            // 点击头部时的反馈动画
            expander.PreviewMouseLeftButtonDown += (s, e) =>
            {
                var scale = (ScaleTransform)expander.RenderTransform;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, CreateQuickAnimation(0.98, 80));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, CreateQuickAnimation(0.98, 80));
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

        #endregion
    }
}
