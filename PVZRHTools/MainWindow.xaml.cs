using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FastHotKeyForWPF;
using HandyControl.Controls;
using HandyControl.Tools.Extension;
using ComboBox = HandyControl.Controls.ComboBox;
using ScrollViewer = HandyControl.Controls.ScrollViewer;
using Window = System.Windows.Window;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Interop;

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