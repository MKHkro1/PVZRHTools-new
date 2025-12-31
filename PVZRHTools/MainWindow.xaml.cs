using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FastHotKeyForWPF;
using HandyControl.Controls;
using HandyControl.Tools.Extension;
using ComboBox = HandyControl.Controls.ComboBox;
using ScrollViewer = HandyControl.Controls.ScrollViewer;
using Window = System.Windows.Window;
using Button = System.Windows.Controls.Button;
using TabControl = System.Windows.Controls.TabControl;
using TabItem = System.Windows.Controls.TabItem;
using Expander = System.Windows.Controls.Expander;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using Slider = System.Windows.Controls.Slider;
using ToolTip = System.Windows.Controls.ToolTip;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Interop;
using PVZRHTools.Animations;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>

namespace PVZRHTools
{
    public partial class MainWindow : Window
    {
        // Win32 API for window resizing
        private const int WM_NCHITTEST = 0x0084;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int HTCLIENT = 1;
        private const int BORDER_WIDTH = 15;
        
        // 用于跟踪是否是首次激活（避免启动时重复播放动画）
        private bool _isFirstActivation = true;

        public MainWindow()
        {
            InitializeComponent();
            Title = $"PVZ融合版修改器{ModifierVersion.GameVersion}-{ModifierVersion.Version} B站@梧萱梦汐X 制作";
            WindowTitle.Content = Title;
            Instance = this;
            ModifierSprite = new ModifierSprite();
            Sprite.Show(ModifierSprite);
            ModifierSprite.Hide();
            if (File.Exists((App.IsBepInEx ? "BepInEx/config" : "UserData") + "/ModifierSettings.json"))
                try
                {
                    var s = JsonSerializer.Deserialize(
                        File.ReadAllText((App.IsBepInEx ? "BepInEx/config" : "UserData") + "/ModifierSettings.json"),
                        ModifierSaveModelSGC.Default.ModifierSaveModel);
                    DataContext = s.NeedSave ? new ModifierViewModel(s) : new ModifierViewModel(s.Hotkeys);
                }
                catch
                {
                    File.Delete((App.IsBepInEx ? "BepInEx/config" : "UserData") + "/ModifierSettings.json");
                    DataContext = new ModifierViewModel();
                }
            else
                DataContext = new ModifierViewModel();

            App.inited = true;
            
            // 窗口加载完成后播放启动动画
            Loaded += MainWindow_Loaded;
            
            // 窗口激活时播放过渡动画（从后台切回前台）
            Activated += MainWindow_Activated;
        }
        
        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            // 跳过首次激活（启动时已有启动动画）
            if (_isFirstActivation)
            {
                _isFirstActivation = false;
                return;
            }
            
            // 播放激活过渡动画
            WindowAnimations.PlayActivationAnimation(this);
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 播放 OS 风格启动动画
            WindowAnimations.PlayStartupAnimation(this);
            
            // 尝试启用 Windows 11 云母效果（如果可用）
            if (AcrylicHelper.IsWindows11OrNewer())
            {
                // 可选：启用云母或亚克力效果
                // AcrylicHelper.EnableMica(this);
            }
            
            // 为所有按钮添加交互动画
            ApplyAnimationsToControls(this);
        }
        
        /// <summary>
        /// 递归为所有控件应用动画效果
        /// </summary>
        private void ApplyAnimationsToControls(DependencyObject parent)
        {
            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                // 为按钮添加点击动画
                if (child is Button button)
                {
                    ControlAnimations.AddButtonPressAnimation(button);
                    ControlAnimations.AddHoverGlow(button, Color.FromRgb(255, 105, 180)); // 粉色发光
                }
                
                // 为 TabControl 添加内容切换动画
                if (child is TabControl tabControl)
                {
                    ControlAnimations.AddTabControlAnimation(tabControl);
                    
                    // 为每个 TabItem 添加动画
                    foreach (var item in tabControl.Items)
                    {
                        if (item is TabItem tabItem)
                        {
                            ControlAnimations.AddTabItemAnimation(tabItem);
                        }
                    }
                }
                
                // 为 Expander 添加动画（礼盒修改、数值修改、场地特性等）
                if (child is Expander expander)
                {
                    ControlAnimations.AddExpanderAnimation(expander);
                }
                
                // 为 CheckBox 添加切换动画
                if (child is CheckBox checkBox)
                {
                    ControlAnimations.AddCheckBoxAnimation(checkBox);
                }
                
                // 为 ToggleButton 添加切换动画（排除 CheckBox）
                if (child is ToggleButton toggleButton && child is not CheckBox)
                {
                    ControlAnimations.AddToggleButtonAnimation(toggleButton);
                }
                
                // 为 TextBox 添加聚焦动画
                if (child is TextBox textBox)
                {
                    ControlAnimations.AddTextBoxFocusAnimation(textBox);
                }
                
                // 为 ComboBox 添加下拉动画
                if (child is System.Windows.Controls.ComboBox comboBox)
                {
                    ControlAnimations.AddComboBoxAnimation(comboBox);
                }
                
                // 为 Slider 添加滑动动画
                if (child is Slider slider)
                {
                    ControlAnimations.AddSliderAnimation(slider);
                }
                
                // 为 ListBox 的项添加悬停动画
                if (child is ListBox listBox)
                {
                    listBox.Loaded += (s, e) =>
                    {
                        foreach (var item in listBox.Items)
                        {
                            if (listBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem listBoxItem)
                            {
                                ControlAnimations.AddListItemHoverAnimation(listBoxItem);
                            }
                        }
                    };
                }
                
                // 为 DataGrid 行添加悬停动画
                if (child is DataGrid dataGrid)
                {
                    dataGrid.LoadingRow += (s, e) =>
                    {
                        ControlAnimations.AddDataGridRowAnimation(e.Row);
                    };
                }
                
                // 为 ContextMenu 添加弹出动画
                if (child is FrameworkElement fe && fe.ContextMenu != null)
                {
                    ControlAnimations.AddContextMenuAnimation(fe.ContextMenu);
                }
                
                // 为 ToolTip 添加淡入动画
                if (child is FrameworkElement element && element.ToolTip is ToolTip toolTip)
                {
                    ControlAnimations.AddToolTipAnimation(toolTip);
                }
                
                // 递归处理子元素
                ApplyAnimationsToControls(child);
            }
        }

        public static MainWindow? Instance { get; set; }
        public static ResourceDictionary LangEN_US => new() { Source = new Uri("/Lang.en-us.xaml", UriKind.Relative) };
        public static ResourceDictionary LangRU_RU => new() { Source = new Uri("/Lang.ru-ru.xaml", UriKind.Relative) };
        public static ResourceDictionary LangZH_CN => new() { Source = new Uri("/Lang.zh-cn.xaml", UriKind.Relative) };
        public ModifierSprite ModifierSprite { get; set; }
        public ModifierViewModel ViewModel => (ModifierViewModel)DataContext;

        public void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = false;
        }

        public void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            var scrollViewer = (ScrollViewer)sender;
            scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta);
        }

        public void TitleBar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton is MouseButtonState.Pressed && e.RightButton is MouseButtonState.Released &&
                e.MiddleButton is MouseButtonState.Released) DragMove();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            ViewModel.Save();
            GlobalHotKey.Destroy();
            Application.Current.Shutdown();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            GlobalHotKey.Awake();
            foreach (var hvm in from hvm in ViewModel.Hotkeys where hvm.CurrentKeyB != Key.None select hvm)
                hvm.UpdateHotKey();
            
            // 添加窗口消息钩子以支持边框拖拽调整大小
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            hwndSource?.AddHook(WndProc);
        }

        /// <summary>
        /// 处理窗口消息，实现边框拖拽调整大小
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                handled = true;
                var result = GetHitTestResult(lParam);
                return new IntPtr(result);
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 根据鼠标位置判断点击区域
        /// </summary>
        private int GetHitTestResult(IntPtr lParam)
        {
            // 获取鼠标屏幕坐标
            int x = (short)(lParam.ToInt32() & 0xFFFF);
            int y = (short)((lParam.ToInt32() >> 16) & 0xFFFF);

            // 转换为窗口坐标
            var point = PointFromScreen(new Point(x, y));

            // 判断鼠标位置
            bool isLeft = point.X < BORDER_WIDTH;
            bool isRight = point.X > ActualWidth - BORDER_WIDTH;
            bool isTop = point.Y < BORDER_WIDTH;
            bool isBottom = point.Y > ActualHeight - BORDER_WIDTH;

            if (isTop && isLeft) return HTTOPLEFT;
            if (isTop && isRight) return HTTOPRIGHT;
            if (isBottom && isLeft) return HTBOTTOMLEFT;
            if (isBottom && isRight) return HTBOTTOMRIGHT;
            if (isLeft) return HTLEFT;
            if (isRight) return HTRIGHT;
            if (isTop) return HTTOP;
            if (isBottom) return HTBOTTOM;

            return HTCLIENT;
        }

        private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (App.inited && sender is ComboBox)
            {
                Application.Current.Resources.MergedDictionaries.RemoveAt(2);
                ResourceDictionary lang;
                if ((string?)((ComboBoxItem?)e.AddedItems[0]!).Content == "简体中文")
                    lang = LangZH_CN;
                else if ((string?)((ComboBoxItem?)e.AddedItems[0]!).Content == "English")
                    lang = LangEN_US;
                else
                    lang = LangRU_RU;

                Application.Current.Resources.MergedDictionaries.Add(lang);
                OnApplyTemplate();
            }
        }

        private void LockWheatPlant_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }
    }
}