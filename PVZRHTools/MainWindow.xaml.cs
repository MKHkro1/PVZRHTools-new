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
using ToolModData;

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
            
            // 添加窗口状态变化动画（最小化/还原/最大化）
            WindowAnimations.AddWindowStateAnimation(this);
            
            // 添加窗口拖动倾斜效果
            AdvancedAnimations.AddWindowDragTilt(this);
            
            // 尝试启用 Windows 11 云母效果（如果可用）
            if (AcrylicHelper.IsWindows11OrNewer())
            {
                // 可选：启用云母或亚克力效果
                // AcrylicHelper.EnableMica(this);
            }
            
            // 为所有按钮添加交互动画
            ApplyAnimationsToControls(this);
            
            // 设置旗帜波词条选择下拉框的固定宽度
            SetFlagWaveComboBoxDropDownWidth();
        }
        
        /// <summary>
        /// 设置所有旗帜波词条选择 CheckComboBox 的下拉框固定宽度
        /// </summary>
        private void SetFlagWaveComboBoxDropDownWidth()
        {
            var comboBoxes = new[]
            {
                FlagWaveBuffIdsComboBox,
                FlagWave1BuffsComboBox,
                FlagWave2BuffsComboBox,
                FlagWave3BuffsComboBox,
                FlagWave4BuffsComboBox,
                FlagWave5BuffsComboBox,
                FlagWave6BuffsComboBox,
                FlagWave7BuffsComboBox,
                FlagWave8BuffsComboBox,
                FlagWave9BuffsComboBox,
                FlagWave10BuffsComboBox
            };
            
            // 固定的下拉框宽度（足够宽以容纳长文本）
            const double fixedDropDownWidth = 800;
            
            foreach (var comboBox in comboBoxes)
            {
                if (comboBox != null)
                {
                    // 使用定时器定期检查下拉框状态，当下拉框打开时设置固定宽度
                    System.Windows.Threading.DispatcherTimer timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(50) // 每50ms检查一次
                    };
                    
                    bool wasOpen = false;
                    timer.Tick += (sender, e) =>
                    {
                        if (comboBox.IsDropDownOpen)
                        {
                            if (!wasOpen)
                            {
                                // 下拉框刚打开，延迟设置宽度以确保 Popup 已创建
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try
                                    {
                                        // 通过模板查找 Popup（类似 ControlAnimations.cs 中的方法）
                                        Popup? popup = comboBox.Template?.FindName("PART_Popup", comboBox) as Popup;
                                        if (popup == null)
                                        {
                                            // 如果模板查找失败，尝试使用 FindVisualChild
                                            popup = FindVisualChild<Popup>(comboBox);
                                        }
                                        
                                        if (popup != null && popup.IsOpen)
                                        {
                                            // 设置固定的下拉框宽度，不受文本长度影响
                                            popup.Width = fixedDropDownWidth;
                                            popup.MinWidth = fixedDropDownWidth;
                                            popup.MaxWidth = fixedDropDownWidth;
                                        }
                                    }
                                    catch
                                    {
                                        // 忽略错误，避免影响其他功能
                                    }
                                }), System.Windows.Threading.DispatcherPriority.Loaded);
                            }
                            wasOpen = true;
                        }
                        else
                        {
                            wasOpen = false;
                        }
                    };
                    
                    timer.Start();
                }
            }
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
                
                // 为 ProgressBar 添加流光效果
                if (child is ProgressBar progressBar)
                {
                    AdvancedAnimations.AddProgressBarShimmer(progressBar);
                }
                
                // 为重要按钮添加磁吸效果（可选，根据按钮名称判断）
                if (child is Button btn && btn.Name != null && 
                    (btn.Name.Contains("Important") || btn.Name.Contains("Main") || btn.Name.Contains("Primary")))
                {
                    AdvancedAnimations.AddMagneticEffect(btn, 0.1);
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

        // 丝滑滚动 - 小幅度 + 平滑插值
        private readonly Dictionary<System.Windows.Controls.ScrollViewer, double> _targetOffsets = new();
        private System.Windows.Threading.DispatcherTimer? _scrollTimer;

        public void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 检查是否有任何 ComboBox 的下拉列表是打开的，如果有则忽略此事件
            // 方法1：检查鼠标是否在 Popup 中
            var source = e.OriginalSource as DependencyObject;
            if (source != null)
            {
                // 向上遍历可视化树，检查是否在 Popup 中
                var current = source;
                while (current != null)
                {
                    if (current is Popup popup && popup.IsOpen)
                    {
                        // 如果鼠标在打开的 Popup 中，不处理此事件，让 Popup 自己处理
                        return;
                    }
                    current = VisualTreeHelper.GetParent(current);
                }
            }
            
            // 方法2：检查整个窗口是否有打开的 ComboBox（更可靠的方法）
            if (HasOpenComboBox(this))
            {
                // 如果有打开的 ComboBox，不处理此事件
                return;
            }
            
            e.Handled = true;
            
            // 获取 ScrollViewer
            System.Windows.Controls.ScrollViewer? sv = null;
            if (sender is System.Windows.Controls.ScrollViewer scrollViewer)
                sv = scrollViewer;
            else if (sender is HandyControl.Controls.ScrollViewer hcScrollViewer)
                sv = hcScrollViewer;
            
            if (sv == null) return;

            // 初始化目标位置
            if (!_targetOffsets.ContainsKey(sv))
                _targetOffsets[sv] = sv.VerticalOffset;

            // 小幅度滚动：e.Delta 是 120，除以 4 = 30 像素
            double scrollAmount = e.Delta / 4.0;
            _targetOffsets[sv] -= scrollAmount;
            _targetOffsets[sv] = Math.Max(0, Math.Min(_targetOffsets[sv], sv.ScrollableHeight));

            // 启动平滑滚动定时器
            if (_scrollTimer == null)
            {
                _scrollTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16) // ~60fps
                };
                _scrollTimer.Tick += ScrollTimer_Tick;
            }
            
            if (!_scrollTimer.IsEnabled)
                _scrollTimer.Start();
        }

        /// <summary>
        /// 检查窗口是否有打开的 ComboBox 下拉列表
        /// </summary>
        private static bool HasOpenComboBox(DependencyObject parent)
        {
            if (parent == null) return false;
            
            // 检查当前元素是否是 ComboBox 且下拉列表打开
            if (parent is System.Windows.Controls.ComboBox comboBox && comboBox.IsDropDownOpen)
            {
                return true;
            }
            
            // 检查当前元素是否是 HandyControl 的 ComboBox 且下拉列表打开
            if (parent is HandyControl.Controls.ComboBox hcComboBox && hcComboBox.IsDropDownOpen)
            {
                return true;
            }
            
            // 检查当前元素是否是 HandyControl 的 CheckComboBox 且下拉列表打开
            if (parent is HandyControl.Controls.CheckComboBox checkComboBox && checkComboBox.IsDropDownOpen)
            {
                return true;
            }
            
            // 递归检查子元素
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (HasOpenComboBox(child))
                {
                    return true;
                }
            }
            
            return false;
        }

        private void ScrollTimer_Tick(object? sender, EventArgs e)
        {
            bool anyActive = false;
            var toRemove = new List<System.Windows.Controls.ScrollViewer>();

            foreach (var kvp in _targetOffsets.ToList())
            {
                var sv = kvp.Key;
                var target = kvp.Value;
                var current = sv.VerticalOffset;
                var diff = target - current;

                if (Math.Abs(diff) > 0.5)
                {
                    // 平滑插值，每帧移动 20% 的距离
                    var newOffset = current + diff * 0.2;
                    sv.ScrollToVerticalOffset(newOffset);
                    anyActive = true;
                }
                else
                {
                    sv.ScrollToVerticalOffset(target);
                    toRemove.Add(sv);
                }
            }

            foreach (var sv in toRemove)
                _targetOffsets.Remove(sv);

            if (!anyActive)
                _scrollTimer?.Stop();
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

        private bool _isUpdatingComboBox = false; // 标志：是否正在更新多选框（防止循环触发）

        private void FlagWaveBuffIdsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingComboBox)
            {
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] FlagWaveBuffIdsComboBox_SelectionChanged: 跳过（正在更新多选框）");
                return; // 如果正在更新多选框，跳过此事件
            }
            
            if (DataContext is ModifierViewModel vm && sender is HandyControl.Controls.CheckComboBox comboBox)
            {
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] FlagWaveBuffIdsComboBox_SelectionChanged: 多选框选择改变");
                var selectedItems = comboBox.SelectedItems;
                var ids = new List<int>();
                
                // 计算Advanced Buff的数量（InGameBuffs中不包含Debuff）
                int advancedCount = App.InitData.Value.AdvBuffs.Length; // Advanced Buff的数量
                int ultimateCount = App.InitData.Value.UltiBuffs.Length; // Ultimate Buff的数量
                int ultimateStartIndex = advancedCount; // Ultimate在InGameBuffs中的起始索引
                
                foreach (var item in selectedItems)
                {
                    if (item is TravelBuffVM buffVm)
                    {
                        int encodedId;
                        if (buffVm.TravelBuff.Debuff)
                        {
                            // Debuff: 编码ID = 2000 + OriginalId（游戏中的字典键）
                            encodedId = 2000 + buffVm.TravelBuff.OriginalId;
                        }
                        else if (buffVm.TravelBuff.Index >= ultimateStartIndex)
                        {
                            // Ultimate: 编码ID = 1000 + 数组索引（参考 HeiTa 的实现）
                            // 数组索引 = buffVm.TravelBuff.Index - ultimateStartIndex
                            // 例如：如果 ultimateStartIndex=85，buffVm.TravelBuff.Index=87，则数组索引=2，编码ID=1002
                            int arrayIndex = buffVm.TravelBuff.Index - ultimateStartIndex;
                            encodedId = 1000 + arrayIndex; // Ultimate: 编码ID = 1000 + 数组索引
                            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 下拉框选择Ultimate: Index={buffVm.TravelBuff.Index}, ultimateStartIndex={ultimateStartIndex}, 数组索引={arrayIndex}, 编码ID={encodedId}");
                        }
                        else
                        {
                            // Advanced: 编码ID = OriginalId（游戏中的字典键）
                            encodedId = buffVm.TravelBuff.OriginalId;
                        }
                        ids.Add(encodedId);
                    }
                }
                // 将所有选中的词条作为一个旗帜波（兼容旧的多选框行为）
                // 添加 -1 分隔符表示一个旗子结束
                if (ids.Count > 0)
                {
                    ids.Add(-1);
                }
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] FlagWaveBuffIdsComboBox_SelectionChanged: 准备设置 ids = [{string.Join(", ", ids)}]");
                vm.FlagWaveBuffIds = ids;
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] FlagWaveBuffIdsComboBox_SelectionChanged: 已设置 vm.FlagWaveBuffIds");
            }
        }

        private void FlagWaveBuffOrderApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ModifierViewModel vm) return;

            var text = FlagWaveBuffOrderIdsTextBox?.Text;
            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] ========== 按钮点击开始 ==========");
            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 输入文本: '{text}'");
            if (string.IsNullOrWhiteSpace(text))
            {
                vm.FlagWaveBuffIds = new List<int>();
                try
                {
                    if (FlagWaveBuffIdsComboBox != null) FlagWaveBuffIdsComboBox.SelectedItems?.Clear();
                }
                catch { }
                return;
            }

            var ids = new List<int>();
            int advancedCount = App.InitData.Value.AdvBuffs.Length; // Advanced Buff的数量
            int ultimateCount = App.InitData.Value.UltiBuffs.Length; // Ultimate Buff的数量
            int ultimateStartIndex = advancedCount; // Ultimate在InGameBuffs中的起始索引
            
            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 解析开始 - advancedCount={advancedCount}, ultimateCount={ultimateCount}, ultimateStartIndex={ultimateStartIndex}, InGameBuffs.Count={vm.InGameBuffs?.Count ?? 0}");
            
            // 检查是否包含括号，如果包含则使用新格式解析
            bool hasBrackets = text.Contains('(') || text.Contains('（') || text.Contains(')') || text.Contains('）');
            
            if (hasBrackets)
            {
                // 新格式：使用括号分组，每个括号代表一个旗子
                // 例如：(a1,u2)()(d3,a0)
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 检测到括号格式，使用新格式解析");
                
                // 提取所有括号内的内容
                var bracketPattern = new System.Text.RegularExpressions.Regex(@"[(（]([^)）]*)[)）]");
                var matches = bracketPattern.Matches(text);
                
                if (matches.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 警告: 未找到任何括号，但文本包含括号字符");
                }
                
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    string bracketContent = match.Groups[1].Value.Trim();
                    System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 解析括号内容: '{bracketContent}'");
                    
                    // 如果括号为空，添加分隔符 -1 表示这个旗子不解锁任何词条
                    if (string.IsNullOrWhiteSpace(bracketContent))
                    {
                        ids.Add(-1); // 使用 -1 作为分隔符，表示一个旗子结束（即使这个旗子没有词条）
                        System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 空括号，添加分隔符 -1");
                        continue;
                    }
                    
                    // 解析括号内的词条（支持逗号/中文逗号/分号/空格分隔）
                    var parts = bracketContent.Split(new[] { ',', '，', ';', '；', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    foreach (var p in parts)
                    {
                        var part = p.Trim();
                        int id = ParseBuffId(part, vm, advancedCount, ultimateCount, ultimateStartIndex);
                        
                        if (id >= 0)
                        {
                            ids.Add(id);
                            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] ✓ 添加编码ID: {id} (来自输入: '{part}')");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] ✗ 警告: 无法解析输入 '{part}'，跳过");
                        }
                    }
                    
                    // 每个括号结束后添加分隔符 -1（除非是最后一个括号）
                    ids.Add(-1); // 使用 -1 作为分隔符，表示一个旗子的词条结束
                    System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 括号结束，添加分隔符 -1");
                }
            }
            else
            {
                // 旧格式：兼容原有的逗号分隔格式（所有词条作为一个旗帜波）
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 未检测到括号，使用旧格式解析（所有词条作为一个旗帜波）");
                
                var parts = text.Split(new[] { ',', '，', ';', '；', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var p in parts)
                {
                    var part = p.Trim();
                    int id = ParseBuffId(part, vm, advancedCount, ultimateCount, ultimateStartIndex);
                    
                    if (id >= 0)
                    {
                        ids.Add(id);
                        System.Diagnostics.Debug.WriteLine($"[旗帜波词条] ✓ 添加编码ID: {id} (来自输入: '{part}')");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[旗帜波词条] ✗ 警告: 无法解析输入 '{part}'，跳过");
                    }
                }
                
                // 旧格式：添加分隔符 -1 表示一个旗子结束
                if (ids.Count > 0)
                {
                    ids.Add(-1);
                }
            }

            // 覆盖顺序：按输入顺序直接作为"每旗解锁"的顺序
            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] ========== 解析完成 ==========");
            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 最终 ids 列表: [{string.Join(", ", ids)}]");
            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 准备设置 vm.FlagWaveBuffIds");
            vm.FlagWaveBuffIds = ids;
            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 已设置 vm.FlagWaveBuffIds，当前值: [{string.Join(", ", vm.FlagWaveBuffIds)}]");
            
            // 更新文本框显示（如果用户修改了文本，这里会重新生成）
            // 注意：这里不更新文本框，因为用户可能正在编辑，只有在按钮点击时才更新

            // 同步到多选框（仅用于可视化勾选；多选框本身不保证顺序）
            try
            {
                if (FlagWaveBuffIdsComboBox != null)
                {
                    _isUpdatingComboBox = true; // 设置标志，防止触发 SelectionChanged 事件
                    System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 开始同步到多选框，设置 _isUpdatingComboBox = true");
                    FlagWaveBuffIdsComboBox.SelectedItems?.Clear();
                    
                    // 遍历所有旗帜波的词条
                    if (vm.FlagWaveBuffIds != null)
                    {
                        foreach (var encodedId in vm.FlagWaveBuffIds)
                        {
                            // 跳过 -1 分隔符
                            if (encodedId == -1)
                                continue;
                                
                            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 同步到多选框: 处理编码ID={encodedId}");
                            
                            bool found = false;
                        
                        if (encodedId >= 2000)
                        {
                            // Debuff: 解码为游戏中的原始ID（字典键）
                            int gameOriginalId = encodedId - 2000;
                            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] Debuff: 解码为 gameOriginalId={gameOriginalId}");
                            
                            // 在FlagWaveBuffIdsComboBox中查找对应的项（使用OriginalId匹配）
                            foreach (var comboItem in FlagWaveBuffIdsComboBox.Items)
                            {
                                if (comboItem is TravelBuffVM comboBuffVm && 
                                    comboBuffVm.TravelBuff.OriginalId == gameOriginalId &&
                                    comboBuffVm.TravelBuff.Debuff == true)
                                {
                                    FlagWaveBuffIdsComboBox.SelectedItems?.Add(comboItem);
                                    System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 找到Debuff项: Index={comboBuffVm.TravelBuff.Index}, OriginalId={comboBuffVm.TravelBuff.OriginalId}, Text={comboBuffVm.TravelBuff.Text}");
                                    found = true;
                                    break;
                                }
                            }
                        }
                        else if (encodedId >= 1000)
                        {
                            // Ultimate: 解码为数组索引
                            int arrayIndex = encodedId - 1000;
                            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] Ultimate: 解码为数组索引={arrayIndex}, ultimateStartIndex={ultimateStartIndex}");
                            
                            // 在FlagWaveBuffIdsComboBox中查找对应的项（使用数组索引匹配）
                            // Ultimate词条的Index = ultimateStartIndex + arrayIndex
                            int targetIndex = ultimateStartIndex + arrayIndex;
                            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] Ultimate: 查找 Index={targetIndex} 的项");
                            
                            foreach (var comboItem in FlagWaveBuffIdsComboBox.Items)
                            {
                                if (comboItem is TravelBuffVM comboBuffVm && 
                                    !comboBuffVm.TravelBuff.Debuff &&
                                    comboBuffVm.TravelBuff.Index == targetIndex)
                                {
                                    FlagWaveBuffIdsComboBox.SelectedItems?.Add(comboItem);
                                    System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 找到Ultimate项: Index={comboBuffVm.TravelBuff.Index}, OriginalId={comboBuffVm.TravelBuff.OriginalId}, Text={comboBuffVm.TravelBuff.Text}");
                                    found = true;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            // Advanced: 解码为游戏中的原始ID（字典键）
                            int gameOriginalId = encodedId;
                            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] Advanced: 解码为 gameOriginalId={gameOriginalId}");
                            
                            // 在FlagWaveBuffIdsComboBox中查找对应的项（使用OriginalId匹配）
                            foreach (var comboItem in FlagWaveBuffIdsComboBox.Items)
                            {
                                if (comboItem is TravelBuffVM comboBuffVm && 
                                    !comboBuffVm.TravelBuff.Debuff &&
                                    comboBuffVm.TravelBuff.OriginalId == gameOriginalId &&
                                    comboBuffVm.TravelBuff.Index < ultimateStartIndex) // 确保是Advanced词条
                                {
                                    FlagWaveBuffIdsComboBox.SelectedItems?.Add(comboItem);
                                    System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 找到Advanced项: Index={comboBuffVm.TravelBuff.Index}, OriginalId={comboBuffVm.TravelBuff.OriginalId}, Text={comboBuffVm.TravelBuff.Text}");
                                    found = true;
                                    break;
                                }
                            }
                        }
                        
                            if (!found)
                            {
                                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 警告: 未找到编码ID={encodedId} 对应的多选框项");
                            }
                        }
                    }
                    _isUpdatingComboBox = false; // 清除标志
                    System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 多选框同步完成，设置 _isUpdatingComboBox = false");
                }
            }
            catch 
            { 
                _isUpdatingComboBox = false; // 确保在异常情况下也清除标志
            }
        }

        private void LockWheatPlant_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void LockPresent_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is ModifierViewModel vm && sender is ComboBox comboBox)
            {
                // 如果 SelectedValue 无效，尝试从 ComboBox 的文本中解析 ID
                if (comboBox.SelectedValue == null && !string.IsNullOrWhiteSpace(comboBox.Text))
                {
                    string text = comboBox.Text.Trim();
                    // 尝试解析为纯数字ID
                    if (int.TryParse(text, out int plantId) && vm.Plants2.ContainsKey(plantId))
                    {
                        vm.LockPresent = plantId;
                    }
                    // 或者尝试从"ID : 名称"格式中提取ID
                    else if (text.Contains(" : "))
                    {
                        string idPart = text.Split(new[] { " : " }, StringSplitOptions.None)[0];
                        if (int.TryParse(idPart, out int parsedId) && vm.Plants2.ContainsKey(parsedId))
                        {
                            vm.LockPresent = parsedId;
                        }
                    }
                }
            }
        }

        private void LockPresent1_LostFocus(object sender, RoutedEventArgs e)
        {
            ParseLockPresentComboBox(sender, (vm, id) => vm.LockPresent1 = id);
        }

        private void LockPresent2_LostFocus(object sender, RoutedEventArgs e)
        {
            ParseLockPresentComboBox(sender, (vm, id) => vm.LockPresent2 = id);
        }

        private void LockPresent3_LostFocus(object sender, RoutedEventArgs e)
        {
            ParseLockPresentComboBox(sender, (vm, id) => vm.LockPresent3 = id);
        }

        private void LockPresent4_LostFocus(object sender, RoutedEventArgs e)
        {
            ParseLockPresentComboBox(sender, (vm, id) => vm.LockPresent4 = id);
        }

        private void LockPresent5_LostFocus(object sender, RoutedEventArgs e)
        {
            ParseLockPresentComboBox(sender, (vm, id) => vm.LockPresent5 = id);
        }

        private void ParseLockPresentComboBox(object sender, Action<ModifierViewModel, int> setValue)
        {
            if (DataContext is ModifierViewModel vm && sender is ComboBox comboBox)
            {
                // 如果 SelectedValue 无效，尝试从 ComboBox 的文本中解析 ID
                if (comboBox.SelectedValue == null && !string.IsNullOrWhiteSpace(comboBox.Text))
                {
                    string text = comboBox.Text.Trim();
                    // 尝试解析为纯数字ID
                    if (int.TryParse(text, out int plantId) && vm.Plants2.ContainsKey(plantId))
                    {
                        setValue(vm, plantId);
                    }
                    // 或者尝试从"ID : 名称"格式中提取ID
                    else if (text.Contains(" : "))
                    {
                        string idPart = text.Split(new[] { " : " }, StringSplitOptions.None)[0];
                        if (int.TryParse(idPart, out int parsedId) && vm.Plants2.ContainsKey(parsedId))
                        {
                            setValue(vm, parsedId);
                        }
                    }
                }
            }
        }

        private void CreatePlant_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ModifierViewModel vm)
            {
                // 如果PlantType无效，尝试从ComboBox的文本中解析ID
                if (!vm.Plants2.ContainsKey(vm.PlantType) && PlantType != null)
                {
                    string text = PlantType.Text?.Trim() ?? "";
                    // 尝试解析为纯数字ID
                    if (int.TryParse(text, out int plantId) && vm.Plants2.ContainsKey(plantId))
                    {
                        vm.PlantType = plantId;
                    }
                    // 或者尝试从"ID : 名称"格式中提取ID
                    else if (text.Contains(" : "))
                    {
                        string idPart = text.Split(new[] { " : " }, StringSplitOptions.None)[0];
                        if (int.TryParse(idPart, out int parsedId) && vm.Plants2.ContainsKey(parsedId))
                        {
                            vm.PlantType = parsedId;
                        }
                    }
                }
            }
        }

        private void CreateZombie_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ModifierViewModel vm)
            {
                // 如果ZombieType无效，尝试从ComboBox的文本中解析ID
                if (!vm.Zombies.ContainsKey(vm.ZombieType) && ZombieType != null)
                {
                    string text = ZombieType.Text?.Trim() ?? "";
                    // 尝试解析为纯数字ID
                    if (int.TryParse(text, out int zombieId) && vm.Zombies.ContainsKey(zombieId))
                    {
                        vm.ZombieType = zombieId;
                    }
                    // 或者尝试从"ID : 名称"格式中提取ID
                    else if (text.Contains(" : "))
                    {
                        string idPart = text.Split(new[] { " : " }, StringSplitOptions.None)[0];
                        if (int.TryParse(idPart, out int parsedId) && vm.Zombies.ContainsKey(parsedId))
                        {
                            vm.ZombieType = parsedId;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 查找可视化树中的子元素
        /// </summary>
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;
            
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        // 存储定时器用于定期检查Text变化
        private System.Windows.Threading.DispatcherTimer? _plantTypeSearchTimer;
        private System.Windows.Threading.DispatcherTimer? _zombieTypeSearchTimer;
        private string _lastPlantSearchText = "";
        private string _lastZombieSearchText = "";
        
        // 存储CollectionViewSource用于过滤
        private System.Windows.Data.CollectionViewSource? _plantTypeCollectionView;
        private System.Windows.Data.CollectionViewSource? _zombieTypeCollectionView;

        /// <summary>
        /// PlantType ComboBox的Loaded事件处理，设置搜索功能
        /// </summary>
        private void PlantType_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && DataContext is ModifierViewModel vm)
            {
                // 创建CollectionViewSource用于过滤
                _plantTypeCollectionView = new System.Windows.Data.CollectionViewSource
                {
                    Source = vm.Plants2
                };
                
                // 延迟查找内部TextBox，确保它已经创建
                comboBox.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    // 尝试查找内部TextBox
                    var textBox = FindVisualChild<System.Windows.Controls.TextBox>(comboBox);
                    if (textBox != null)
                    {
                        // 直接订阅TextBox的TextChanged事件
                        textBox.TextChanged += (s, args) =>
                        {
                            if (s is System.Windows.Controls.TextBox tb)
                            {
                                FilterPlantType(comboBox, tb.Text);
                            }
                        };
                    }
                    
                    // 同时创建定时器作为备用方案
                    _plantTypeSearchTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(30) // 每30ms检查一次，更频繁
                    };
                    _plantTypeSearchTimer.Tick += (s, args) =>
                    {
                        string text = comboBox.Text ?? "";
                        FilterPlantType(comboBox, text);
                    };
                    _plantTypeSearchTimer.Start();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// PlantType ComboBox的GotFocus事件处理
        /// </summary>
        private void PlantType_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && _plantTypeSearchTimer != null)
            {
                _plantTypeSearchTimer.Start();
            }
        }

        /// <summary>
        /// PlantType ComboBox的LostFocus事件处理
        /// </summary>
        private void PlantType_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_plantTypeSearchTimer != null)
            {
                _plantTypeSearchTimer.Stop();
            }
        }

        /// <summary>
        /// PlantType ComboBox的PreviewKeyDown事件处理
        /// </summary>
        private void PlantType_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                // 延迟执行过滤，等待文本更新
                comboBox.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FilterPlantType(comboBox, null);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        /// <summary>
        /// PlantType ComboBox的PreviewTextInput事件处理
        /// </summary>
        private void PlantType_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                // 获取即将输入的文本
                string newText = (comboBox.Text ?? "") + e.Text;
                // 延迟执行过滤，等待文本更新
                comboBox.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FilterPlantType(comboBox, newText);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        /// <summary>
        /// PlantType ComboBox的KeyDown事件处理
        /// </summary>
        private void PlantType_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                // 延迟执行过滤，等待文本更新
                comboBox.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FilterPlantType(comboBox, null);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        /// <summary>
        /// PlantType ComboBox的TextInput事件处理
        /// </summary>
        private void PlantType_TextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                // 获取即将输入的文本
                string newText = (comboBox.Text ?? "") + e.Text;
                // 延迟执行过滤，等待文本更新
                comboBox.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FilterPlantType(comboBox, newText);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        /// <summary>
        /// 过滤PlantType列表
        /// </summary>
        private void FilterPlantType(ComboBox comboBox, string? currentText = null)
        {
            if (DataContext is not ModifierViewModel vm) return;

            // 如果没有提供文本，尝试获取
            if (currentText == null)
            {
                // 方法1: 直接使用ComboBox的Text属性
                try
                {
                    currentText = comboBox.Text ?? "";
                }
                catch
                {
                    // 方法2: 查找内部TextBox
                    var textBox = FindVisualChild<System.Windows.Controls.TextBox>(comboBox);
                    if (textBox != null)
                    {
                        currentText = textBox.Text ?? "";
                    }
                    else
                    {
                        currentText = "";
                    }
                }
            }

            // 如果文本没有变化，不执行过滤
            if (currentText == _lastPlantSearchText) return;
            _lastPlantSearchText = currentText;

            string searchText = currentText.Trim();

            // 如果搜索文本为空，恢复原始ItemsSource，让系统自带搜索处理
            if (string.IsNullOrEmpty(searchText))
            {
                // 不设置ItemsSource，让系统恢复
                return;
            }

            // 检查是否包含非ASCII字符（中文等），如果只包含数字和ASCII字符，让系统自带搜索处理
            bool containsNonAscii = false;
            foreach (char c in searchText)
            {
                if (c > 127)
                {
                    containsNonAscii = true;
                    break;
                }
            }

            // 如果只包含ASCII字符（可能是纯数字ID），不进行自定义过滤，让系统处理
            if (!containsNonAscii)
            {
                return;
            }

            // 包含中文等非ASCII字符，进行自定义过滤
            var filtered = new Dictionary<int, string>();
            foreach (var kvp in vm.Plants2)
            {
                // 支持搜索ID（数字）和名称（中文）
                if (kvp.Key.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(kvp.Key, kvp.Value);
                }
            }

            // 更新ItemsSource
            comboBox.ItemsSource = filtered;

            // 如果过滤后有结果，自动打开下拉列表
            if (filtered.Count > 0)
            {
                comboBox.IsDropDownOpen = true;
            }
        }

        /// <summary>
        /// ZombieType ComboBox的Loaded事件处理，设置搜索功能
        /// </summary>
        private void ZombieType_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && DataContext is ModifierViewModel vm)
            {
                // 创建CollectionViewSource用于过滤
                _zombieTypeCollectionView = new System.Windows.Data.CollectionViewSource
                {
                    Source = vm.Zombies
                };
                
                // 延迟查找内部TextBox，确保它已经创建
                comboBox.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    // 尝试查找内部TextBox
                    var textBox = FindVisualChild<System.Windows.Controls.TextBox>(comboBox);
                    if (textBox != null)
                    {
                        // 直接订阅TextBox的TextChanged事件
                        textBox.TextChanged += (s, args) =>
                        {
                            if (s is System.Windows.Controls.TextBox tb)
                            {
                                FilterZombieType(comboBox, tb.Text);
                            }
                        };
                    }
                    
                    // 同时创建定时器作为备用方案
                    _zombieTypeSearchTimer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(30) // 每30ms检查一次，更频繁
                    };
                    _zombieTypeSearchTimer.Tick += (s, args) =>
                    {
                        string text = comboBox.Text ?? "";
                        FilterZombieType(comboBox, text);
                    };
                    _zombieTypeSearchTimer.Start();
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// ZombieType ComboBox的GotFocus事件处理
        /// </summary>
        private void ZombieType_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is ComboBox comboBox && _zombieTypeSearchTimer != null)
            {
                _zombieTypeSearchTimer.Start();
            }
        }

        /// <summary>
        /// ZombieType ComboBox的LostFocus事件处理
        /// </summary>
        private void ZombieType_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_zombieTypeSearchTimer != null)
            {
                _zombieTypeSearchTimer.Stop();
            }
        }

        /// <summary>
        /// ZombieType ComboBox的PreviewKeyDown事件处理
        /// </summary>
        private void ZombieType_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                // 延迟执行过滤，等待文本更新
                comboBox.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FilterZombieType(comboBox, null);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        /// <summary>
        /// ZombieType ComboBox的PreviewTextInput事件处理
        /// </summary>
        private void ZombieType_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                // 获取即将输入的文本
                string newText = (comboBox.Text ?? "") + e.Text;
                // 延迟执行过滤，等待文本更新
                comboBox.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FilterZombieType(comboBox, newText);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        /// <summary>
        /// ZombieType ComboBox的KeyDown事件处理
        /// </summary>
        private void ZombieType_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                // 延迟执行过滤，等待文本更新
                comboBox.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FilterZombieType(comboBox, null);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        /// <summary>
        /// ZombieType ComboBox的TextInput事件处理
        /// </summary>
        private void ZombieType_TextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (sender is ComboBox comboBox)
            {
                // 获取即将输入的文本
                string newText = (comboBox.Text ?? "") + e.Text;
                // 延迟执行过滤，等待文本更新
                comboBox.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    FilterZombieType(comboBox, newText);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        /// <summary>
        /// 过滤ZombieType列表
        /// </summary>
        private void FilterZombieType(ComboBox comboBox, string? currentText = null)
        {
            if (DataContext is not ModifierViewModel vm) return;

            // 如果没有提供文本，尝试获取
            if (currentText == null)
            {
                // 方法1: 直接使用ComboBox的Text属性
                try
                {
                    currentText = comboBox.Text ?? "";
                }
                catch
                {
                    // 方法2: 查找内部TextBox
                    var textBox = FindVisualChild<System.Windows.Controls.TextBox>(comboBox);
                    if (textBox != null)
                    {
                        currentText = textBox.Text ?? "";
                    }
                    else
                    {
                        currentText = "";
                    }
                }
            }

            // 如果文本没有变化，不执行过滤
            if (currentText == _lastZombieSearchText) return;
            _lastZombieSearchText = currentText;

            string searchText = currentText.Trim();

            // 如果搜索文本为空，恢复原始ItemsSource，让系统自带搜索处理
            if (string.IsNullOrEmpty(searchText))
            {
                // 不设置ItemsSource，让系统恢复
                return;
            }

            // 检查是否包含非ASCII字符（中文等），如果只包含数字和ASCII字符，让系统自带搜索处理
            bool containsNonAscii = false;
            foreach (char c in searchText)
            {
                if (c > 127)
                {
                    containsNonAscii = true;
                    break;
                }
            }

            // 如果只包含ASCII字符（可能是纯数字ID），不进行自定义过滤，让系统处理
            if (!containsNonAscii)
            {
                return;
            }

            // 包含中文等非ASCII字符，进行自定义过滤
            var filtered = new Dictionary<int, string>();
            foreach (var kvp in vm.Zombies)
            {
                // 支持搜索ID（数字）和名称（中文）
                if (kvp.Key.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add(kvp.Key, kvp.Value);
                }
            }

            // 更新ItemsSource
            comboBox.ItemsSource = filtered;

            // 如果过滤后有结果，自动打开下拉列表
            if (filtered.Count > 0)
            {
                comboBox.IsDropDownOpen = true;
            }
        }

        /// <summary>
        /// 解析词条ID（支持 A0, U1, D0 格式）
        /// </summary>
        private int ParseBuffId(string part, ModifierViewModel vm, int advancedCount, int ultimateCount, int ultimateStartIndex)
        {
            int id = -1;
            
            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 开始解析输入: '{part}' (长度={part.Length}, 第一个字符='{(part.Length > 0 ? part[0] : '?')}')");
            
            // 支持格式：A0, U1, D0（数字从0开始，使用OriginalId）
            if (part.Length >= 2 && (part[0] == 'A' || part[0] == 'a'))
            {
                if (int.TryParse(part.Substring(1), out var advNum) && advNum >= 0)
                {
                    // 找到第 advNum 个Advanced词条，使用其OriginalId
                    // Advanced词条在 InGameBuffs 的前 advancedCount 个位置
                    if (vm.InGameBuffs != null && advNum < advancedCount && advNum < vm.InGameBuffs.Count)
                    {
                        var buff = vm.InGameBuffs[advNum];
                        if (buff.TravelBuff.Index < ultimateStartIndex) // 确保是Advanced词条
                        {
                            id = buff.TravelBuff.OriginalId; // 使用OriginalId（游戏中的字典键）
                            System.Diagnostics.Debug.WriteLine($"解析 A{advNum}: 找到词条 Index={buff.TravelBuff.Index}, OriginalId={buff.TravelBuff.OriginalId}, 编码ID={id}");
                        }
                    }
                }
            }
            else if (part.Length >= 2 && (part[0] == 'U' || part[0] == 'u'))
            {
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 检测到 U/u 开头，准备解析: part='{part}', 子串='{part.Substring(1)}'");
                if (int.TryParse(part.Substring(1), out var ultNum) && ultNum >= 0)
                {
                    // Ultimate词条：使用 ultNum 作为数组索引（而不是字典键）
                    id = 1000 + ultNum; // Ultimate: 编码ID = 1000 + ultNum（数组索引）
                    System.Diagnostics.Debug.WriteLine($"[旗帜波词条] ✓ 解析 U{ultNum} 成功: ultNum={ultNum}, ultimateCount={ultimateCount}, 编码ID={id} (1000+ultNum)");
                    if (ultNum >= ultimateCount)
                    {
                        System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 警告: U{ultNum} 超出范围 (ultimateCount={ultimateCount})，但已编码为 {id}，将由游戏端处理");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[旗帜波词条] ✗ 解析 U 开头词条失败: 无法解析数字部分 '{part.Substring(1)}'");
                }
            }
            else if (part.Length >= 2 && (part[0] == 'D' || part[0] == 'd'))
            {
                if (int.TryParse(part.Substring(1), out var debNum) && debNum >= 0)
                {
                    // 找到第 debNum 个Debuff词条，使用其OriginalId
                    if (vm.InGameDebuffs != null && debNum < vm.InGameDebuffs.Count)
                    {
                        var buff = vm.InGameDebuffs[debNum];
                        id = 2000 + buff.TravelBuff.OriginalId; // 使用OriginalId（游戏中的字典键）
                        System.Diagnostics.Debug.WriteLine($"解析 D{debNum}: 找到词条 Index={buff.TravelBuff.Index}, OriginalId={buff.TravelBuff.OriginalId}, 编码ID={id}");
                    }
                }
            }
            // 支持旧格式：A:0, U:0, D:0
            else if (part.StartsWith("A:", StringComparison.OrdinalIgnoreCase) || part.StartsWith("A：", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(part.Substring(2), out var advId))
                    id = advId; // Advanced: 0-999
            }
            else if (part.StartsWith("U:", StringComparison.OrdinalIgnoreCase) || part.StartsWith("U：", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(part.Substring(2), out var ultId))
                    id = 1000 + ultId; // Ultimate: 1000-1999
            }
            else if (part.StartsWith("D:", StringComparison.OrdinalIgnoreCase) || part.StartsWith("D：", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(part.Substring(2), out var debId))
                    id = 2000 + debId; // Debuff: 2000-2999
            }
            // 支持直接输入编码ID（但排除以 U/u/A/a/D/d 开头的字符串，避免误解析）
            else if (!part.StartsWith("U", StringComparison.OrdinalIgnoreCase) &&
                     !part.StartsWith("A", StringComparison.OrdinalIgnoreCase) &&
                     !part.StartsWith("D", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(part, out var encodedId))
            {
                id = encodedId;
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 直接解析为编码ID: {id} (来自输入: '{part}')");
            }
            
            // 验证：如果是 U 开头的输入，编码ID应该是 1000+
            if (id >= 0 && (part.StartsWith("U", StringComparison.OrdinalIgnoreCase) || part.StartsWith("U:", StringComparison.OrdinalIgnoreCase) || part.StartsWith("U：", StringComparison.OrdinalIgnoreCase)) && id < 1000)
            {
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] ✗✗✗ 严重错误: Ultimate词条 '{part}' 的编码ID应该是1000+，但实际是{id}！这会导致被错误地解码为Advanced词条！");
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 调试信息: part='{part}', part.Length={part.Length}, part[0]='{(part.Length > 0 ? part[0] : '?')}'");
            }
            
            return id;
        }
        
        /// <summary>
        /// 更新旗帜波词条文本框，将数据转换为括号格式文本
        /// </summary>
        private void UpdateFlagWaveBuffTextBox(List<List<int>> waves)
        {
            if (FlagWaveBuffOrderIdsTextBox == null || waves == null) return;
            
            var parts = new List<string>();
            
            foreach (var wave in waves)
            {
                if (wave == null || wave.Count == 0)
                {
                    parts.Add("()"); // 空括号
                }
                else
                {
                    var buffStrs = new List<string>();
                    foreach (var encodedId in wave)
                    {
                        string buffStr = EncodeBuffIdToString(encodedId);
                        if (!string.IsNullOrEmpty(buffStr))
                        {
                            buffStrs.Add(buffStr);
                        }
                    }
                    parts.Add($"({string.Join(",", buffStrs)})");
                }
            }
            
            string text = string.Join("", parts);
            FlagWaveBuffOrderIdsTextBox.Text = text;
            System.Diagnostics.Debug.WriteLine($"[旗帜波词条] 更新文本框: {text}");
        }
        
        /// <summary>
        /// 将编码ID转换为字符串格式（a1, u2, d3等）
        /// </summary>
        private string EncodeBuffIdToString(int encodedId)
        {
            if (encodedId >= 2000)
            {
                // Debuff: d + (encodedId - 2000)
                return $"d{encodedId - 2000}";
            }
            else if (encodedId >= 1000)
            {
                // Ultimate: u + (encodedId - 1000)
                return $"u{encodedId - 1000}";
            }
            else if (encodedId >= 0)
            {
                // Advanced: a + encodedId
                return $"a{encodedId}";
            }
            return "";
        }
        
        /// <summary>
        /// 处理旗帜波词条选择改变事件（通用方法）
        /// </summary>
        private void HandleFlagWaveBuffSelectionChanged(object sender, SelectionChangedEventArgs e, int waveIndex)
        {
            if (DataContext is ModifierViewModel vm && sender is HandyControl.Controls.CheckComboBox comboBox)
            {
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条高级] FlagWave{waveIndex}BuffsComboBox_SelectionChanged: 多选框选择改变");
                var selectedItems = comboBox.SelectedItems;
                var ids = new List<int>();
                
                // 计算Advanced Buff的数量（InGameBuffs中不包含Debuff）
                int advancedCount = App.InitData.Value.AdvBuffs.Length;
                int ultimateCount = App.InitData.Value.UltiBuffs.Length;
                int ultimateStartIndex = advancedCount;
                
                foreach (var item in selectedItems)
                {
                    if (item is TravelBuffVM buffVm)
                    {
                        int encodedId;
                        if (buffVm.TravelBuff.Debuff)
                        {
                            encodedId = 2000 + buffVm.TravelBuff.OriginalId;
                        }
                        else if (buffVm.TravelBuff.Index >= ultimateStartIndex)
                        {
                            int arrayIndex = buffVm.TravelBuff.Index - ultimateStartIndex;
                            encodedId = 1000 + arrayIndex;
                        }
                        else
                        {
                            encodedId = buffVm.TravelBuff.OriginalId;
                        }
                        ids.Add(encodedId);
                    }
                }
                
                // 根据waveIndex设置对应的属性
                switch (waveIndex)
                {
                    case 1: vm.FlagWave1Buffs = ids; break;
                    case 2: vm.FlagWave2Buffs = ids; break;
                    case 3: vm.FlagWave3Buffs = ids; break;
                    case 4: vm.FlagWave4Buffs = ids; break;
                    case 5: vm.FlagWave5Buffs = ids; break;
                    case 6: vm.FlagWave6Buffs = ids; break;
                    case 7: vm.FlagWave7Buffs = ids; break;
                    case 8: vm.FlagWave8Buffs = ids; break;
                    case 9: vm.FlagWave9Buffs = ids; break;
                    case 10: vm.FlagWave10Buffs = ids; break;
                }
                
                System.Diagnostics.Debug.WriteLine($"[旗帜波词条高级] FlagWave{waveIndex}BuffsComboBox_SelectionChanged: 已设置 ids = [{string.Join(", ", ids)}]");
                
                // 自动同步到Tab2的"词条ID顺序"文本框
                SyncAdvancedFlagWaveBuffsToTextBox(vm);
            }
        }
        
        /// <summary>
        /// 将Tab10中10个旗帜波的词条配置同步到Tab2的"词条ID顺序"文本框
        /// </summary>
        private void SyncAdvancedFlagWaveBuffsToTextBox(ModifierViewModel vm)
        {
            if (FlagWaveBuffOrderIdsTextBox == null) return;
            
            // 收集10个旗帜波的词条列表
            var waves = new List<List<int>>
            {
                vm.FlagWave1Buffs ?? new List<int>(),
                vm.FlagWave2Buffs ?? new List<int>(),
                vm.FlagWave3Buffs ?? new List<int>(),
                vm.FlagWave4Buffs ?? new List<int>(),
                vm.FlagWave5Buffs ?? new List<int>(),
                vm.FlagWave6Buffs ?? new List<int>(),
                vm.FlagWave7Buffs ?? new List<int>(),
                vm.FlagWave8Buffs ?? new List<int>(),
                vm.FlagWave9Buffs ?? new List<int>(),
                vm.FlagWave10Buffs ?? new List<int>()
            };
            
            // 使用现有的UpdateFlagWaveBuffTextBox方法更新文本框
            UpdateFlagWaveBuffTextBox(waves);
            
            System.Diagnostics.Debug.WriteLine($"[旗帜波词条高级] 已同步到Tab2的文本框");
        }
        
        private void FlagWave1BuffsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlagWaveBuffSelectionChanged(sender, e, 1);
        }
        
        private void FlagWave2BuffsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlagWaveBuffSelectionChanged(sender, e, 2);
        }
        
        private void FlagWave3BuffsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlagWaveBuffSelectionChanged(sender, e, 3);
        }
        
        private void FlagWave4BuffsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlagWaveBuffSelectionChanged(sender, e, 4);
        }
        
        private void FlagWave5BuffsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlagWaveBuffSelectionChanged(sender, e, 5);
        }
        
        private void FlagWave6BuffsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlagWaveBuffSelectionChanged(sender, e, 6);
        }
        
        private void FlagWave7BuffsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlagWaveBuffSelectionChanged(sender, e, 7);
        }
        
        private void FlagWave8BuffsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlagWaveBuffSelectionChanged(sender, e, 8);
        }
        
        private void FlagWave9BuffsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlagWaveBuffSelectionChanged(sender, e, 9);
        }
        
        private void FlagWave10BuffsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            HandleFlagWaveBuffSelectionChanged(sender, e, 10);
        }
        
        /// <summary>
        /// CheckComboBox的PreviewMouseWheel事件处理，阻止滚轮事件冒泡到ScrollViewer
        /// 当下拉列表打开时，滚轮操作只影响下拉列表，不影响主界面滚动
        /// </summary>
        private void CheckComboBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is HandyControl.Controls.CheckComboBox comboBox)
            {
                // 如果 CheckComboBox 的下拉列表打开，不阻止事件，让下拉列表自己处理滚轮
                // 这样下拉列表内部的 ScrollViewer 可以正常滚动
                if (comboBox.IsDropDownOpen)
                {
                    e.Handled = false;
                    return;
                }
                
                // 如果下拉列表未打开，阻止事件冒泡到ScrollViewer，防止主界面滚动
                e.Handled = true;
            }
        }

        /// <summary>
        /// 检索分区的搜索框文本变化事件处理
        /// </summary>
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox searchBox || DataContext is not ModifierViewModel vm)
                return;

            string searchText = searchBox.Text?.Trim() ?? "";

            // 如果搜索文本为空，清空所有结果
            if (string.IsNullOrEmpty(searchText))
            {
                PlantsResultsListBox.ItemsSource = null;
                ZombiesResultsListBox.ItemsSource = null;
                BuffsResultsListBox.ItemsSource = null;
                BulletsResultsListBox.ItemsSource = null;
                ItemsResultsListBox.ItemsSource = null;
                
                PlantsExpander.IsExpanded = false;
                ZombiesExpander.IsExpanded = false;
                BuffsExpander.IsExpanded = false;
                BulletsExpander.IsExpanded = false;
                ItemsExpander.IsExpanded = false;
                return;
            }

            // 搜索植物
            var plantsResults = new List<string>();
            if (vm.Plants2 != null)
            {
                foreach (var kvp in vm.Plants2)
                {
                    if (kvp.Key.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        kvp.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        plantsResults.Add($"{kvp.Key} : {kvp.Value}");
                    }
                }
            }
            PlantsResultsListBox.ItemsSource = plantsResults;
            PlantsExpander.IsExpanded = plantsResults.Count > 0;

            // 搜索僵尸
            var zombiesResults = new List<string>();
            if (vm.Zombies != null)
            {
                foreach (var kvp in vm.Zombies)
                {
                    if (kvp.Key.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        kvp.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        zombiesResults.Add($"{kvp.Key} : {kvp.Value}");
                    }
                }
            }
            ZombiesResultsListBox.ItemsSource = zombiesResults;
            ZombiesExpander.IsExpanded = zombiesResults.Count > 0;

            // 搜索词条
            var buffsResults = new List<string>();
            if (vm.AllInGameBuffs != null)
            {
                foreach (var buff in vm.AllInGameBuffs)
                {
                    string buffText = buff.TravelBuff?.Text ?? "";
                    string buffIndex = buff.TravelBuff?.Index.ToString() ?? "";
                    
                    if (buffIndex.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        buffText.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        string buffType = buff.TravelBuff?.Debuff == true ? "Debuff" : "Buff";
                        buffsResults.Add($"{buffIndex} ({buffType}) : {buffText}");
                    }
                }
            }
            BuffsResultsListBox.ItemsSource = buffsResults;
            BuffsExpander.IsExpanded = buffsResults.Count > 0;

            // 搜索子弹
            var bulletsResults = new List<string>();
            if (vm.Bullets2 != null)
            {
                foreach (var kvp in vm.Bullets2)
                {
                    if (kvp.Key.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        kvp.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        bulletsResults.Add($"{kvp.Key} : {kvp.Value}");
                    }
                }
            }
            BulletsResultsListBox.ItemsSource = bulletsResults;
            BulletsExpander.IsExpanded = bulletsResults.Count > 0;

            // 搜索物品
            var itemsResults = new List<string>();
            if (vm.Items != null)
            {
                foreach (var kvp in vm.Items)
                {
                    if (kvp.Key.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                        kvp.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        itemsResults.Add($"{kvp.Key} : {kvp.Value}");
                    }
                }
            }
            ItemsResultsListBox.ItemsSource = itemsResults;
            ItemsExpander.IsExpanded = itemsResults.Count > 0;
        }

        /// <summary>
        /// 播放特效按钮点击事件
        /// </summary>
        private void PlayParticleButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ModifierViewModel vm)
                return;

            if (string.IsNullOrWhiteSpace(vm.ParticleId))
            {
                System.Windows.MessageBox.Show("请输入特效ID", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(vm.ParticleId, out int particleId))
            {
                System.Windows.MessageBox.Show("特效ID必须是数字", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 发送播放特效请求
            vm.PlayParticle(particleId);
        }

        /// <summary>
        /// 播放音效按钮点击事件
        /// </summary>
        private void PlaySoundButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ModifierViewModel vm)
                return;

            if (string.IsNullOrWhiteSpace(vm.SoundId))
            {
                System.Windows.MessageBox.Show("请输入音效ID", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(vm.SoundId, out int soundId))
            {
                System.Windows.MessageBox.Show("音效ID必须是数字", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 发送播放音效请求
            vm.PlaySound(soundId);
        }

        // 存储出怪列表数据
        private Dictionary<int, List<int>>? _zombieListByWave;
        private int _currentWave = 0;

        /// <summary>
        /// 刷新出怪列表按钮点击事件
        /// </summary>
        private void RefreshZombieListButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ModifierViewModel vm)
                return;

            // 发送请求获取出怪列表的命令
            // 发送获取出怪列表的请求（游戏端需要实现GetZombieList的处理）
            App.DataSync.Value.SendData(new InGameActions { GetZombieList = true });
            
            // 更新当前波数显示为"获取中..."
            if (CurrentWaveLabel != null)
            {
                CurrentWaveLabel.Content = "获取中...";
            }
        }

        /// <summary>
        /// 更新出怪列表UI显示
        /// </summary>
        private void UpdateZombieListUI()
        {
            if (ZombieListContainer == null) return;
            if (DataContext is not ModifierViewModel vm) return;

            // 清空现有内容
            ZombieListContainer.Children.Clear();

            // 更新当前波数显示
            if (CurrentWaveLabel != null)
            {
                CurrentWaveLabel.Content = _currentWave > 0 ? $"第 {_currentWave} 波" : "未获取";
            }

            // 如果没有数据，显示提示
            if (_zombieListByWave == null || _zombieListByWave.Count == 0)
            {
                var noDataLabel = new Label
                {
                    Content = "暂无出怪列表数据\n请点击刷新按钮获取",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 20, 0, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128))
                };
                ZombieListContainer.Children.Add(noDataLabel);
                return;
            }

            // 遍历所有波次，创建UI
            foreach (var waveKvp in _zombieListByWave.OrderBy(x => x.Key))
            {
                int waveIndex = waveKvp.Key;
                List<int> zombieTypes = waveKvp.Value;
                
                // 跳过空列表
                if (zombieTypes == null || zombieTypes.Count == 0) continue;

                // 创建波次面板
                var waveBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(135, 206, 235)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 10),
                    Background = waveIndex == _currentWave 
                        ? new SolidColorBrush(Color.FromRgb(240, 255, 240)) 
                        : new SolidColorBrush(Color.FromRgb(255, 255, 255))
                };

                var waveStackPanel = new StackPanel { Orientation = Orientation.Vertical };

                // 波次标题
                string waveTitle = $"第 {waveIndex} 波";
                if (waveIndex % 10 == 0)
                {
                    waveTitle = $"第 {waveIndex / 10} 旗";
                }
                if (waveIndex == _currentWave)
                {
                    waveTitle += " [当前]";
                }

                var waveTitleLabel = new Label
                {
                    Content = waveTitle,
                    FontWeight = FontWeights.Bold,
                    FontSize = 14,
                    Foreground = waveIndex == _currentWave 
                        ? new SolidColorBrush(Color.FromRgb(0, 170, 0)) 
                        : new SolidColorBrush(Color.FromRgb(51, 51, 51)),
                    Margin = new Thickness(0, 0, 0, 10)
                };
                waveStackPanel.Children.Add(waveTitleLabel);

                // 展开/折叠按钮
                var expander = new Expander
                {
                    Header = $"出怪列表 ({zombieTypes.Count} 个)",
                    IsExpanded = waveIndex == _currentWave, // 当前波次默认展开
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var zombieListPanel = new StackPanel { Orientation = Orientation.Vertical };

                // 遍历该波的所有僵尸
                for (int i = 0; i < zombieTypes.Count; i++)
                {
                    int zombieTypeId = zombieTypes[i];
                    int currentIndex = i; // 捕获索引用于闭包

                    var zombieItemPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 5, 0, 5)
                    };

                    // 索引标签
                    var indexLabel = new Label
                    {
                        Content = $"[{i}]",
                        Width = 40,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    zombieItemPanel.Children.Add(indexLabel);

                    // 僵尸类型下拉框
                    var zombieComboBox = new ComboBox
                    {
                        Width = 300,
                        Height = 30,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 10, 0)
                    };

                    // 设置僵尸类型选项
                    if (vm.Zombies != null)
                    {
                        zombieComboBox.ItemsSource = vm.Zombies;
                        zombieComboBox.DisplayMemberPath = "Value";
                        zombieComboBox.SelectedValuePath = "Key";
                        zombieComboBox.SelectedValue = zombieTypeId;
                    }

                    zombieItemPanel.Children.Add(zombieComboBox);

                    // 应用按钮
                    var applyButton = new Button
                    {
                        Content = "应用",
                        Width = 80,
                        Height = 30,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 0, 0)
                    };

                    // 应用按钮点击事件
                    applyButton.Click += (s, e) =>
                    {
                        if (zombieComboBox.SelectedValue is int selectedZombieType)
                        {
                            // 更新出怪列表
                            if (_zombieListByWave != null && _zombieListByWave.ContainsKey(waveIndex))
                            {
                                _zombieListByWave[waveIndex][currentIndex] = selectedZombieType;
                                
                                // 发送修改命令到游戏端
                                // 格式：waveIndex,zombieIndex,zombieType
                                string modifyCommand = $"{waveIndex},{currentIndex},{selectedZombieType}";
                                App.DataSync.Value.SendData(new InGameActions { ModifyZombieList = modifyCommand });
                                
                                System.Windows.MessageBox.Show(
                                    $"已修改第 {waveIndex} 波第 {currentIndex} 个出怪为: {vm.Zombies?.GetValueOrDefault(selectedZombieType, "未知")}",
                                    "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                    };

                    zombieItemPanel.Children.Add(applyButton);

                    zombieListPanel.Children.Add(zombieItemPanel);
                }

                expander.Content = zombieListPanel;
                waveStackPanel.Children.Add(expander);

                waveBorder.Child = waveStackPanel;
                ZombieListContainer.Children.Add(waveBorder);
            }
        }

        /// <summary>
        /// 设置出怪列表数据（由DataSync调用）
        /// </summary>
        public void SetZombieListData(Dictionary<int, List<int>> zombieListByWave, int currentWave)
        {
            try
            {
                _zombieListByWave = zombieListByWave ?? new Dictionary<int, List<int>>();
                _currentWave = currentWave;
                
                // 更新UI
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        UpdateZombieListUI();
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"更新出怪列表UI时出错: {ex.Message}", "错误", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"设置出怪列表数据时出错: {ex.Message}", "错误", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}