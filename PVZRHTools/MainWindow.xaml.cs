using System.IO;
using System.Text.Json;
using System.ComponentModel;
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
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using HandyControl.Tools;
using PVZRHTools.Animations;
using ToolModData;

/// <summary>
///     Interaction logic for MainWindow.xaml
/// </summary>

namespace PVZRHTools
{
    public partial class MainWindow : Window
    {
        private sealed class StartupLoadingContext
        {
            // Keep XAML overlay visible until real VM is ready.
            public bool IsLoading { get; } = true;
        }

        // Win32 API for window resizing
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_NCACTIVATE = 0x0086;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int HTCLIENT = 1;
        private const int BORDER_WIDTH = 8;
        private const int SW_RESTORE = 9;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        
        // 用于跟踪是否是首次激活（避免启动时重复播放动画）
        private bool _isFirstActivation = true;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy,
            uint uFlags);

        public MainWindow()
        {
            InitializeComponent();
            Title = $"PVZ融合版修改器{ModifierVersion.GameVersion}-{ModifierVersion.Version} B站@梧萱梦汐X 制作";
            WindowTitle.Content = Title;
            Instance = this;
            ModifierSprite = new ModifierSprite();
            Sprite.Show(ModifierSprite);
            ModifierSprite.Hide();
            DataContext = new StartupLoadingContext();

            // Create real ViewModel after first frame rendered,
            // so the loading overlay can appear immediately.
            ContentRendered += MainWindow_ContentRendered;
            
            // 应用初始主题（如果已保存）。
            // ★★ 这里**不能**判 ViewModel：构造函数 L97 刚把 DataContext 设成 StartupLoadingContext，
            //    真正的 ViewModel 要等 ContentRendered → BeginViewModelInitialization 才挂上。
            //    判 ViewModel 会让本分支**永远为 false** ⇒ 启动时深色主题完全没被应用
            //    （用户实测：切到深色、重启后界面仍是浅色）。
            // ★ 顺序：先换字典（HC 官方皮肤 + 令牌），再延迟压色（压色取色依赖已切换的令牌）。
            if (IsSavedDarkMode())
            {
                App.SwitchTheme(true);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ApplyThemeWithAnimation(true);
                }), DispatcherPriority.Loaded);
            }
            
            // 窗口加载完成后播放启动动画
            Loaded += MainWindow_Loaded;
            SizeChanged += MainWindow_SizeChanged;
            
            // 窗口激活时播放过渡动画（从后台切回前台）
            Activated += MainWindow_Activated;
            Deactivated += MainWindow_Deactivated;
        }

        private void MainWindow_ContentRendered(object? sender, EventArgs e)
        {
            ContentRendered -= MainWindow_ContentRendered;
            BeginViewModelInitialization();
        }

        private void BeginViewModelInitialization()
        {
            if (ShouldWaitForInitDataBeforeLoad())
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                var attempts = 0;
                timer.Tick += (_, _) =>
                {
                    attempts++;
                    if (App.InitData != null || attempts >= 100)
                    {
                        timer.Stop();
                        FinishViewModelInitialization();
                    }
                };
                timer.Start();
                return;
            }

            FinishViewModelInitialization();
        }

        private static bool ShouldWaitForInitDataBeforeLoad()
        {
            var settingsPath = ModifierPaths.GetSaveSettingsPath();
            if (!File.Exists(settingsPath))
            {
                return false;
            }

            try
            {
                var s = JsonSerializer.Deserialize(File.ReadAllText(settingsPath),
                    ModifierSaveModelSGC.Default.ModifierSaveModel);
                return s.NeedSave && App.InitData == null;
            }
            catch
            {
                return false;
            }
        }

        private void FinishViewModelInitialization()
        {
            try
            {
                var pendingSaveCodes = App.PendingSaveModel?.InGameHotkeyCodes;
                var (vm, loadedSettings) = CreateRealViewModel();
                DataContext = vm;
                // ★ 自定义面板（「自由度排版」）独立装载：不依赖「保存设置(NeedSave)」那条门。
                //   实测踩坑：NeedSave=false 时 CreateRealViewModel 走 `new ModifierViewModel(s.Hotkeys)`，
                //   设置 s 根本不经过 ApplySavedSettings ⇒ 面板收藏永不恢复、且之后的持久化会把旧收藏覆盖成
                //   「只有内存里那点」（表现为"重启后收藏丢失"）。这里对两条路径统一补一次装载（幂等）。
                if (loadedSettings is ModifierSaveModel ls)
                {
                    vm.CustomPanel.LoadFrom(ls.CustomPanelItems, ls.CustomPanelColumns);
                    vm.CustomPanel.LoadPresets(ls.CustomPanelPresets);
                }
                // 侧栏收放平滑动画（2026-09-25 重设计）：XAML 的 EnterActions 在本机被 MC3074 拒绝，
                // 改由 code-behind 监听 SidebarCollapsed → BeginAnimation 驱动标签 MaxWidth/Opacity。
                vm.PropertyChanged -= OnSidebarStateChanged;
                vm.PropertyChanged += OnSidebarStateChanged;
                App.inited = true;
                if (ViewModel != null)
                {
                    ViewModel.ApplyPendingSaveIfNeeded();

                    List<int>? savedCodes = null;
                    if (pendingSaveCodes is { Count: > 0 })
                    {
                        savedCodes = pendingSaveCodes;
                    }
                    else if (loadedSettings?.InGameHotkeyCodes is { Count: > 0 })
                    {
                        savedCodes = loadedSettings.Value.InGameHotkeyCodes;
                    }

                    var gameCodes = App.PendingGameInGameHotkeys;
                    App.PendingGameInGameHotkeys = null;

                    // 等 DataGrid/ComboBox 完成绑定后再恢复快捷键，避免界面仍显示默认值
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ViewModel == null)
                        {
                            return;
                        }

                        ViewModel.RestoreInGameHotkeys(savedCodes, gameCodes);
                        if (ViewModel.NeedSave)
                        {
                            ViewModel.SyncAll();
                        }
                    }), DispatcherPriority.Loaded);
                }
            }
            catch
            {
                // Fallback: keep app usable even if save file is corrupted.
                DataContext = new ModifierViewModel();
                App.inited = true;
            }
        }

        /// <summary>
        ///     侧栏收放的标签平滑动画（code-behind 驱动，原因见 FinishViewModelInitialization 注释）。
        ///     展开：MaxWidth 0→180 / Opacity 0→1（0.2s EaseOut）；收起：180→0 / 1→0（0.18s）。
        ///     动画目标 = 各 TabItem 模板里 FontSize=13.5 的标题 TextBlock（图标 15px 排除在外）。
        ///     FillBehavior.Stop 让动画结束后回落到 Style Setter 的状态值（XAML DataTrigger 已设兜底）。
        /// </summary>
        private void OnSidebarStateChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ModifierViewModel.SidebarCollapsed)) return;
            if (DataContext is not ModifierViewModel vm) return;
            var collapsed = vm.SidebarCollapsed;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MainTabControl == null) return;
                foreach (var obj in MainTabControl.Items)
                {
                    if (obj is not TabItem tab) continue;
                    if (VisualTreeHelper.GetChildrenCount(tab) == 0) continue; // 未实例化的容器跳过
                    foreach (var tb in CollectHeaderLabels(tab))
                    {
                        var dur = TimeSpan.FromMilliseconds(collapsed ? 180 : 200);
                        var wAnim = new DoubleAnimation(collapsed ? 0 : 180, dur)
                        {
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                            FillBehavior = FillBehavior.Stop
                        };
                        var oAnim = new DoubleAnimation(collapsed ? 0 : 1, dur) { FillBehavior = FillBehavior.Stop };
                        tb.BeginAnimation(TextBlock.MaxWidthProperty, wAnim);
                        tb.BeginAnimation(TextBlock.OpacityProperty, oAnim);
                    }
                }
            }), DispatcherPriority.Loaded);
        }

        /// <summary>收集 TabItem 模板内的标题 TextBlock：排除图标（FontFamily=Segoe MDL2 Assets 的 15px 字）。</summary>
        private static IEnumerable<TextBlock> CollectHeaderLabels(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock tb &&
                    tb.FontSize == 13.5 &&
                    !string.Equals(tb.FontFamily?.Source, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
                {
                    yield return tb;
                }

                foreach (var inner in CollectHeaderLabels(child))
                    yield return inner;
            }
        }

        private static (ModifierViewModel Vm, ModifierSaveModel? Settings) CreateRealViewModel()
        {
            var settingsPath = ModifierPaths.GetSaveSettingsPath();
            if (File.Exists(settingsPath))
            {
                try
                {
                    var s = JsonSerializer.Deserialize(File.ReadAllText(settingsPath),
                        ModifierSaveModelSGC.Default.ModifierSaveModel);
                    if (s.NeedSave && App.InitData == null)
                    {
                        App.PendingSaveModel = s;
                        var vm = new ModifierViewModel(s.Hotkeys);
                        vm.NeedSave = s.NeedSave;
                        return (vm, s);
                    }

                    if (s.NeedSave)
                    {
                        return (new ModifierViewModel(s), s);
                    }

                    return (new ModifierViewModel(s.Hotkeys), s);
                }
                catch
                {
                    File.Delete(settingsPath);
                }
            }

            return (new ModifierViewModel(), null);
        }
        
        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            // 跳过首次激活（启动时已有启动动画）
            if (_isFirstActivation)
            {
                _isFirstActivation = false;
                return;
            }

            // ★ 2026-09-25 移除「窗口激活动画」：它把**整个窗口内容**做 0.92→1.0 的弹性缩放 +
            //   透明度 0.7→1（500ms），表现为「每次点一下修改器，整屏抖闪一下」——用户明确要求去掉。
            //   这里不再调用 WindowAnimations.PlayActivationAnimation；控件级动画（按钮/标签页等）不受影响。
            // WindowAnimations.PlayActivationAnimation(this);
            RefreshWindowFrame();
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            RefreshWindowFrame();
        }

        public void BringToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Show();
            Activate();
            Focus();

            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
                RefreshWindowFrame();
            }
        }

        private void RefreshWindowFrame()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Force immediate non-client frame recalculation to remove transient white strips.
            SetWindowPos(
                hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE |
                SWP_FRAMECHANGED);
        }
        
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 播放 OS 风格启动动画
            WindowAnimations.PlayStartupAnimation(this);
            
            // 添加窗口状态变化动画（最小化/还原/最大化）
            WindowAnimations.AddWindowStateAnimation(this);
            
            // 添加窗口拖动倾斜效果
            AdvancedAnimations.AddWindowDragTilt(this);
            
            // ★★ 2026-09-26 实测结论：**本窗口不能启用系统级 Mica**（已实机复现并回退）。
            //   现象：解开 AcrylicHelper.EnableMica(this) 后，窗口**整片空白**（截图仅 8.8KB，只剩标题栏碎片）。
            //   根因：EnableMica 做两件在本窗口上互斥的事——
            //     ① `DwmExtendFrameIntoClientArea(hwnd, -1,-1,-1,-1)` 把 DWM 框架扩到整个客户区；
            //     ② `window.Background = Brushes.Transparent`。
            //   而 MainWindow 是 `WindowStyle=None` + **`AllowsTransparency=False`**（见 MainWindow.xaml），
            //   非 AllowsTransparency 的窗口没有 WPF 合成层来承载"透明客户区" ⇒ 内容被清空、只剩 DWM 背板。
            //   要让 Mica 生效必须改成 AllowsTransparency=True，但那会连带：
            //     · 本窗口已有的自绘标题栏/圆角/拖拽倾斜(AdvancedAnimations.AddWindowDragTilt)/启动动画 全部重测；
            //     · 失去硬件加速的部分渲染路径，且 ResizeMode=CanResizeWithGrip 的抓手行为变化。
            //   收益（一点系统模糊）远小于风险 ⇒ **不启用系统 Mica**。毛玻璃观感改由"应用内分层"实现：
            //     WindowBackdropBrush（窗口渐变）+ GlassHostBrush（半透明内容宿主）+ GlassSurfaceBrush（半透明卡片）
            //     + CardShadowEffect（卡片外阴影）⇒ 既有磨砂层次，又不依赖 DWM、跨系统一致、零窗口级风险。
            //   （若将来真要上 Mica：先把窗口改成 AllowsTransparency=True，再逐项重测自绘标题栏/圆角/
            //     拖拽倾斜/启动动画/缩放手柄，并确认 ResizeMode 与 UseLayoutRounding 的渲染路径没退化。）
            
            // 为所有按钮添加交互动画
            ApplyAnimationsToControls(this);
            
            // 设置旗帜波词条选择下拉框的固定宽度
            SetFlagWaveComboBoxDropDownWidth();
            
            // 确保标题栏背景延伸到窗口最右边
            UpdateTitleBarBorderWidth();
            
            // 确保在窗口加载后应用主题（延迟执行，确保所有控件都已加载）
            // ★ 同样不能判 ViewModel —— 这里是 Loaded，VM 可能仍未挂上（见构造函数处的说明）。
            // ★ 先换字典 → 再压色：压色器取色依赖**已切换**的令牌，且它自身可逆（见 ApplyThemeWithAnimation）。
            if (IsSavedDarkMode())
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    App.SwitchTheme(true);
                    ApplyThemeWithAnimation(true);
                }), DispatcherPriority.Loaded);
            }
        }

        /// <summary>
        /// 启动时判断"已保存的主题是否为深色"。
        /// ★ 刻意**不依赖 ViewModel**：构造函数与 Loaded 阶段 DataContext 还是 StartupLoadingContext，
        ///   真正的 ViewModel 要到 ContentRendered → FinishViewModelInitialization 才挂上（L183）。
        ///   判 ViewModel 会让启动时的换肤分支**永远为 false**。
        /// 取值顺序（都不需要 VM 就绪）：
        ///   ① App.PendingSaveModel —— 存档已加载但 InitData 未就绪时暂存的设置（App.xaml.cs:30）；
        ///   ② 直接读 BepInEx\config\ModifierSettings.json（与 CreateRealViewModel 同一路径、同一序列化上下文）；
        /// 都取不到 ⇒ 视为浅色（与既有"默认浅色"一致）。
        /// </summary>
        private static bool IsSavedDarkMode()
        {
            try
            {
                if (App.PendingSaveModel is { } pending) return pending.IsDarkMode;
            }
            catch
            {
                // 存档尚未反序列化完成 —— 继续读文件
            }

            try
            {
                var settingsPath = ModifierPaths.GetSaveSettingsPath();
                if (File.Exists(settingsPath))
                {
                    var s = JsonSerializer.Deserialize(File.ReadAllText(settingsPath),
                        ModifierSaveModelSGC.Default.ModifierSaveModel);
                    return s.IsDarkMode;
                }
            }
            catch
            {
                // 存档损坏 / 不可读 —— 视为浅色（CreateRealViewModel 也会在此时删档重建）
            }

            return false;
        }
        
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 窗口大小改变时，确保标题栏背景延伸到窗口最右边
            UpdateTitleBarBorderWidth();
        }

        // ==================================================================
        // 自定义面板（「自由度排版」功能区，2026-09-25）
        // ==================================================================

        /// <summary>
        /// 列数下拉：由 XAML 的 <c>SelectedValue</c> 双向绑定直接写 VM.CustomPanel.Columns
        /// （Tag 是字符串，WPF 自动转 int）⇒ 不需要 code-behind 处理器；
        /// 列数变化后由 <see cref="HookCustomPanel"/> 订阅 PropertyChanged 触发重排。
        /// ★ 早前版本用 SelectionChanged + <c>DataContext is ModifierViewModel</c> 判断，
        ///   而该下拉位于 <c>DataContext="{Binding CustomPanel}"</c> 子树上（其 DataContext 是 CustomPanelViewModel）
        ///   ⇒ 判断恒假、处理器静默 return，表现为「下拉能选但列数永远不变」（实测踩过）。
        /// </summary>
        private void HookCustomPanel()
        {
            if (ViewModel is not { } vm) return;
            vm.CustomPanel.PropertyChanged -= CustomPanel_PropertyChanged;
            vm.CustomPanel.PropertyChanged += CustomPanel_PropertyChanged;
        }

        private void CustomPanel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CustomPanelViewModel.Columns)) return;
            if (ViewModel is not { } vm) return;
            ApplyCustomPanelColumns(vm.CustomPanel.Columns);
        }

        /// <summary>「保存配置」按钮：显式保存面板布局 + 短暂显示「已保存 ✓」反馈（1.2 秒后还原按钮文本）。
        /// 布局本就在每次改动时自动落盘（CustomPanel.Changed → PersistCustomPanel），重启自动载入；
        /// 该按钮是玩家的明确保存入口，幂等。</summary>
        private void CustomPanelSaveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel?.CustomPanel is { } panel)
                panel.SaveNow();

            if (CustomPanelSaveBtn is { } btn)
            {
                var original = btn.TryFindResource("Custom.Panel.Save") as string ?? "保存配置";
                btn.Content = btn.TryFindResource("Custom.Panel.SaveDone") as string ?? "已保存 ✓";
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1.2)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    btn.Content = original;
                };
                timer.Start();
            }
        }

        /// <summary>
        /// 把「列数」落到 UniformGrid 上。
        /// 用 FindName 拿不到 ItemsPanelTemplate 里的元素（模板内的名字不在窗口命名域），
        /// 因此遍历视觉树反查 ItemsControl 下的 UniformGrid（面板主体）。
        /// 拿不到就静默跳过——下一次布局/选中事件会再试，不会留下错误状态。
        /// </summary>
        private void ApplyCustomPanelColumns(int columns)
        {
            var host = FindUniformGrid(CustomPanelItems);
            if (host is null) return;

            var effective = columns is >= 1 and <= 3
                ? columns
                : (CustomPanelItems.ActualWidth >= 900 ? 3 : CustomPanelItems.ActualWidth >= 560 ? 2 : 1);
            if (host.Columns != effective) host.Columns = effective;
            if (host.Rows != 0) host.Rows = 0;
        }

        private static UniformGrid? FindUniformGrid(DependencyObject? root)
        {
            if (root is null) return null;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is UniformGrid ug) return ug;
                var inner = FindUniformGrid(child);
                if (inner is not null) return inner;
            }

            return null;
        }

        /// <summary>面板列数/窗口宽度变化时重算列数（自动模式下跟随宽度）。</summary>
        private void CustomPanelLayoutUpdated()
        {
            if (DataContext is not ModifierViewModel vm) return;
            ApplyCustomPanelColumns(vm.CustomPanel.Columns);
        }

        private void CustomPanelItems_LayoutUpdated(object? sender, EventArgs e)
            => CustomPanelLayoutUpdated();

        /// <summary>
        /// 切到「我的面板」页时：① 把列数下拉同步成 VM 里的值（首次进入/重启后）；
        /// ② 应用一次列数（此时视觉树才真正生成，之前拿不到 UniformGrid）。
        /// </summary>
        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is not TabControl) return;
            if (DataContext is not ModifierViewModel vm) return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 列数下拉由 SelectedValue 双向绑定自行同步，这里只需把列数落到 UniformGrid
                ApplyCustomPanelColumns(vm.CustomPanel.Columns);
            }), DispatcherPriority.Loaded);

            HookCustomPanel();

            // ★ 2026-09-26 性能修复记录：**「首次打开词条页要 7.8 秒」的根因与修法**（实测数据）
            //   症状：用户反馈首次打开「旅行词条修改」很慢。实测切页→布局完成 = **7873 ms**，
            //         之后再切 = 22~83 ms（一次性成本，不是每帧问题）。
            //   排除项：离线基准证明**解析不是瓶颈** —— 466 条词条 / 25K 字符 / 平均 1.6 个富文本标签，
            //           全量剥离标签一次仅 **0.16 ms**（见 .tools/buffperf）。所以慢必在 WPF 布局层。
            //   真因（两条叠加）：
            //     ① `DataGrid.Small` 这个样式键**全工程从未定义**，5 个词条 DataGrid 全都
            //        `Style="{DynamicResource DataGrid.Small}"` ⇒ DynamicResource 缺键**静默回落**
            //        HandyControl 默认 DataGrid（大行高/大内边距）。与之前 Expander.Small 是同一类缺陷。
            //     ② **虚拟化被外层 ScrollViewer 废掉**：词条页的 DataGrid 处在页面级 ScrollViewer 内，
            //        内容被赋予无限可用高度 ⇒ DataGrid 把自己撑到"显示全部行"（实测高 **9794 px**、
            //        281 行**全部实例化**）⇒ 一次性实例化并测量全部富文本行 = 7.8 秒。
            //   修法（两处，已在别处落地）：
            //     · Styles/SimpleTheme.xaml 补上 `DataGrid.Small` 定义（含显式虚拟化开关）；
            //     · MainWindow.xaml 给 5 个词条 DataGrid 加 **MaxHeight="420"** ⇒ 有界高度 ⇒
            //       转为 DataGrid 内部滚动 + 按视口虚拟化。
            //   效果（实测）：首次打开 **7873 → 336 ms**；**已实例化行 281 → 18**（仅视口内）；
            //                 高度 9794 → 420 px。后续切换稳定在 17~83 ms。
            //   ⇒ 通用经验：**WPF 里"DataGrid 放进外层 ScrollViewer"会让虚拟化完全失效** ——
            //     凡是在可滚动容器里放长列表，必须给它**有界高度**（固定/最大高度或 Grid 星号行），
            //     否则它会退化成"一次性实例化全部行"，行内容越复杂越慢（富文本/模板列尤其明显）。

            // ★ 2026-09-26 审美升级：切页**淡入 + 轻推移**入场（20px → 0，250~300ms 弹簧缓动）。
            //   工程里 AnimateTabSwitch 早就写好了但**从未被调用**（与 AnimatedStyles.xaml 同款死代码），
            //   这里把它接到切页事件上，一处生效于全部 15 个页面。
            //   注意：动画作用在**内容 Border**（Tab0ContentBorder）而不是 TabItem 上，
            //   避免影响侧栏选中态的绘制；且 AnimateTabSwitch 内部自带「动画开关关闭则直接 return」。
            try
            {
                if (FindName("Tab0ContentBorder") is FrameworkElement pageHost)
                {
                    ControlAnimations.AnimateTabSwitch(pageHost);
                }
            }
            catch
            {
                // 入场动画失败绝不影响切页本身
            }
        }

        private void UpdateTitleBarBorderWidth()
        {
            var titleBarBorder = FindName("TitleBarBorder") as Border;
            if (titleBarBorder != null)
            {
                // 确保 Border 填充整个可用空间
                titleBarBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
                titleBarBorder.VerticalAlignment = VerticalAlignment.Stretch;
                titleBarBorder.Margin = new Thickness(0, 0, 0, 0);
            }
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
        /// 卡片「玻璃质感」增强（2026-09-26 新增）：
        ///   ① 顶部 1px 高光条 —— 模拟玻璃上沿受光（深色模式下最出效果）；
        ///   ② 悬停时把默认外阴影换成**强调色光晕**，勾勒卡片边缘。
        /// 全部走 code-behind 的**每元素独立**对象（绝不复用样式里的共享 Freezable ——
        /// 那会导致 "该对象已密封或已冻结" 崩溃，本次已在按钮上踩过一次）。
        /// 高光条插在卡片 Border 的 Child 之外是做不到的（Border 只有一个 Child），
        /// 所以做法是：把原 Child 包进一个 Grid，高光条作为 Grid 的第 2 个子项叠在上层，
        /// IsHitTestVisible=False ⇒ 不影响任何点击与命中测试。
        /// </summary>
        private void AttachCardGlassPolish(Border card)
        {
            try
            {
                // ---- ① 顶部高光条（只加一次；重复调用幂等）----
                if (card.Child is not Grid host)
                {
                    var original = card.Child;
                    host = new Grid();
                    card.Child = host;
                    if (original != null)
                    {
                        // 原内容从 Border.Child 摘下来挂进 Grid（Border.Child 已被设为 host，故这里是移动）
                        host.Children.Add(original);
                    }

                    var highlight = new Border
                    {
                        Height = 1,
                        VerticalAlignment = VerticalAlignment.Top,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        IsHitTestVisible = false,
                        Margin = new Thickness(1, 1, 1, 0),
                        SnapsToDevicePixels = true,
                        CornerRadius = new CornerRadius(10, 10, 0, 0),
                        Background = TryFindResource("GlassHighlightBrush") as Brush
                    };
                    Panel.SetZIndex(highlight, 1);
                    host.Children.Add(highlight);
                }

                // ---- ② 悬停光晕（事件每元素各挂一次）----
                var normalShadow = TryFindResource("CardShadowEffect") as Effect;
                var hoverGlow = TryFindResource("CardHoverGlowEffect") as Effect;

                card.MouseEnter += (_, _) =>
                {
                    if (!ControlAnimations.IsAnimationEnabledPublic()) return;
                    if (hoverGlow != null) card.Effect = hoverGlow;
                };
                card.MouseLeave += (_, _) =>
                {
                    if (!ControlAnimations.IsAnimationEnabledPublic()) return;
                    card.Effect = normalShadow;
                };
            }
            catch
            {
                // 观感增强失败绝不影响功能：静默跳过（卡片仍可用）
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

                // ★ 2026-09-26 审美升级：卡片悬停**轻抬 + 强调色光晕**。
                //   为什么放这里而不是写进 SettingsCard 样式（实机崩溃教训）：
                //     Style 的 Setter 值在 WPF 里是**共享且会被冻结**的，而 AddHoverLift 会对
                //     RenderTransform 调 BeginAnimation ⇒ 若 transform 来自 Setter 共享实例，
                //     第一次悬停就抛 "该对象已密封或已冻结"（本会话已在按钮上复现过一次）。
                //   AddHoverLift 自己保证「每元素各自 new TranslateTransform」，所以走这条路是安全的。
                //   作用域：只给 SettingsCard（本工程卡片容器的唯一键），不给整页上百个元素挂事件。
                if (child is Border cardBorder && cardBorder.Style is Style cs &&
                    cs == TryFindResource("SettingsCard") as Style)
                {
                    ControlAnimations.AddHoverLift(cardBorder);
                    AttachCardGlassPolish(cardBorder);
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
        public ModifierViewModel? ViewModel => DataContext as ModifierViewModel;

        // ==========================================================================================
        //  ★ 换肤快照表（2026-09-24 第二轮）—— 让"逐控件压色"变成对称且可逆
        //
        //  问题：压色器把颜色以**本地值**写进控件的 Background/Foreground/BorderBrush/Fill。
        //        本地值在 App.SwitchTheme 换掉资源字典之后依然存活，而阈值启发式在两个方向上
        //        并不对称（浅色方向的规则覆盖不全），于是「深色 → 切回浅色」会留下黑底白字。
        //
        //  修法：每次写入之前先把该控件该属性的**原始状态**记下来：
        //        · hadLocal = false → 该属性本来没有本地值（颜色来自 {DynamicResource} / Style）
        //          ⇒ 回滚用 ClearValue，让 DynamicResource 重新接管，天然跟随新主题；
        //        · hadLocal = true  → 记的是**原始画笔对象引用**（不是颜色！），
        //          ⇒ 回滚用 SetValue 还原，能恢复共享画笔 / 渐变画笔 / Frozen 画笔。
        //
        //  时序：ApplyThemeWithAnimation 第一步就 RestoreThemeSnapshots()，
        //        然后才按新主题压色并重新采快照 ⇒ 深→浅→深→浅 任意次数都收敛到正确外观。
        // ==========================================================================================
        private sealed class ThemeSnapshot
        {
            public DependencyObject Element = null!;
            public DependencyProperty Property = null!;
            public object? Value;
            public bool HadLocal;
        }

        /// <summary>键 = 控件实例 + 属性名（如 "Background"）。用引用相等比较，避免控件重写 Equals 干扰。</summary>
        private static readonly Dictionary<(object, string), ThemeSnapshot> ThemeSnapshots = new();

        /// <summary>
        /// 在**写入之前**记录某控件某属性的原始状态。重复写同一 (控件, 属性) 时保留**最早**那次记录，
        /// 否则第二次写入会把"被自己涂过的颜色"当成原始值，回滚就回不去了。
        /// </summary>
        private static void Snapshot(DependencyObject element, DependencyProperty dp)
        {
            if (element == null || dp == null) return;
            var key = ((object)element, dp.Name);
            if (ThemeSnapshots.ContainsKey(key)) return;

            var localValue = element.ReadLocalValue(dp);
            var hadLocal = localValue != DependencyProperty.UnsetValue;
            ThemeSnapshots[key] = new ThemeSnapshot
            {
                Element = element,
                Property = dp,
                // ★ 存的是**对象引用**（画笔），不是颜色 —— 这样回滚能恢复 {DynamicResource} 画笔
                Value = hadLocal ? localValue : null,
                HadLocal = hadLocal
            };
        }

        /// <summary>
        /// 遍历快照还原所有被涂过的属性，然后清空快照（下次重新采）。
        /// ApplyThemeWithAnimation 的**第一步**必须调用它。
        /// </summary>
        private static void RestoreThemeSnapshots()
        {
            if (ThemeSnapshots.Count == 0) return;

            foreach (var snapshot in ThemeSnapshots.Values)
            {
                try
                {
                    if (snapshot.HadLocal)
                        snapshot.Element.SetValue(snapshot.Property, snapshot.Value);
                    else
                        snapshot.Element.ClearValue(snapshot.Property); // 让 {DynamicResource} / Style 重新接管
                }
                catch
                {
                    // 单个元素回滚失败（已从树上摘除、属性只读等）不能拖垮整轮换肤
                }
            }
            ThemeSnapshots.Clear();
        }

        /// <summary>
        /// 统一的"给控件某属性换色"入口 —— <b>所有</b>压色写入都必须走这里。
        /// 它负责：① 先采快照；② 动画结束后把终值提升为本地值。
        ///
        /// ★ 为什么不再就地改共享画笔的颜色：`brush.Color = x` 会污染**来自 DynamicResource 的共享
        ///   画笔**（同一个 SolidColorBrush 实例被整棵树的多个控件共用），改了它等于改令牌本身，
        ///   回滚时无从分辨谁改的。所以这里一律**替换成新画笔**。
        /// </summary>
        private void ApplyThemeBrush(DependencyObject element, DependencyProperty dp, SolidColorBrush? nextBrush, TimeSpan duration)
        {
            if (element == null || dp == null || nextBrush == null) return;

            // ★ 采快照必须在写入之前
            Snapshot(element, dp);

            if (duration <= TimeSpan.Zero)
            {
                element.SetValue(dp, nextBrush);
                return;
            }

            // 过渡动画：在**新画笔**上做，不碰任何既有（可能是共享的）画笔。
            try
            {
                var fromBrush = element.GetValue(dp) as SolidColorBrush;
                var animated = new SolidColorBrush(nextBrush.Color);

                if (fromBrush != null && !fromBrush.IsFrozen && fromBrush.Color != nextBrush.Color)
                {
                    var animation = new System.Windows.Media.Animation.ColorAnimation(fromBrush.Color, nextBrush.Color, duration)
                    {
                        EasingFunction = new System.Windows.Media.Animation.PowerEase
                        {
                            EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut,
                            Power = 2
                        }
                    };
                    // FillBehavior.Stop ⇒ 动画到点后不再参与属性值计算，露出"兜底本地值"，避免长期持有动画时钟
                    animation.FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop;
                    animated.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                }

                element.SetValue(dp, animated);

                // 动画跑完后把终值提升为干净的本地画笔（不留动画时钟）
                var timer = new DispatcherTimer { Interval = duration };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    try
                    {
                        // 只有当该属性仍然是我们这条动画画笔时才提升，避免覆盖期间发生的更新
                        if (ReferenceEquals(element.GetValue(dp), animated))
                        {
                            element.SetValue(dp, nextBrush);
                        }
                    }
                    catch
                    {
                        // 元素已离树 —— 忽略
                    }
                };
                timer.Start();
            }
            catch
            {
                // 动画不可用（属性被 Freeze、元素已离树等）→ 直接落在终值上
                element.SetValue(dp, nextBrush);
            }
        }

        /// <summary>
        /// 只改颜色、保留原画笔类型时的便捷入口（渐变画笔、Frozen 画笔等不适合替换的场合）。
        /// 内部仍然先 Snapshot。
        /// </summary>
        private void ApplyThemeColor(DependencyObject element, DependencyProperty dp, Color target, TimeSpan duration)
        {
            ApplyThemeBrush(element, dp, new SolidColorBrush(target), duration);
        }

        /// <summary>
        /// 【2026-09-24 第二轮重写】逐控件"压色"换肤。
        ///
        /// 性质：对称 + 可逆 + 目标色来自令牌。三点缺一不可：
        ///   1. 第一步 RestoreThemeSnapshots() —— 还原上一次写下的所有本地值；
        ///   2. 目标色用 TryFindResource 从**当前主题**的令牌里取（调用方必须先跑 App.SwitchTheme(…)），
        ///      取不到才回退旧字面量，保底不崩；
        ///   3. 每次写入都经 Snapshot(元素, 属性)，就地改共享画笔的做法已彻底移除。
        ///
        /// ★ 本方法**不再**调用 App.SwitchTheme —— 换字典是调用方的第一步，这里只负责压色。
        /// </summary>
        public void ApplyThemeWithAnimation(bool isDarkMode)
        {
            // ★★★ 第 0 步：先回滚上一次的涂色（对称可逆的关键）
            RestoreThemeSnapshots();

            // ---- 目标色：全部从已切换的资源字典解析；失败回退旧字面量 ----
            var windowBackground = ResolveThemeColor("WindowBackgroundBrush", isDarkMode ? Color.FromArgb(0xFF, 0x17, 0x18, 0x1A) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            var windowForeground = ResolveThemeColor("WindowForegroundBrush", isDarkMode ? Color.FromArgb(0xFF, 0xE8, 0xEA, 0xED) : Color.FromArgb(0xFF, 0x1F, 0x23, 0x29));
            var tabControlBackground = ResolveThemeColor("TabControlBackgroundBrush", isDarkMode ? Color.FromArgb(0xFF, 0x1A, 0x1B, 0x1D) : Color.FromArgb(0xFF, 0xF7, 0xF8, 0xFA));
            var tabControlBorder = ResolveThemeColor("TabControlBorderBrush", isDarkMode ? Color.FromArgb(0xFF, 0x30, 0x32, 0x36) : Color.FromArgb(0xFF, 0xE6, 0xE8, 0xEC));
            var contentBackground = ResolveThemeColor("ContentBackgroundBrush", isDarkMode ? Color.FromArgb(0xFF, 0x1E, 0x1F, 0x22) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            var contentBorder = ResolveThemeColor("ContentBorderBrush", isDarkMode ? Color.FromArgb(0xFF, 0x30, 0x32, 0x36) : Color.FromArgb(0xFF, 0xE6, 0xE8, 0xEC));
            var textForeground = ResolveThemeColor("TextForegroundBrush", isDarkMode ? Color.FromArgb(0xFF, 0xE8, 0xEA, 0xED) : Color.FromArgb(0xFF, 0x1F, 0x23, 0x29));
            var titleForeground = ResolveThemeColor("TitleForegroundBrush", isDarkMode ? Color.FromArgb(0xFF, 0x6F, 0xBF, 0x7A) : Color.FromArgb(0xFF, 0xE0, 0x56, 0x8F));
            var labelForeground = ResolveThemeColor("LabelForegroundBrush", isDarkMode ? Color.FromArgb(0xFF, 0x6F, 0xBF, 0x7A) : Color.FromArgb(0xFF, 0xE0, 0x56, 0x8F));
            var titleBarBackground = ResolveThemeColor("TitleBarBackgroundBrush", isDarkMode ? Color.FromArgb(0xFF, 0x1E, 0x1F, 0x22) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
            // 窗口边框现在是 LinearGradientBrush 两段 —— 直接取用资源里的那个实例
            var windowBorderBrush = TryFindResource("WindowBorderBrush") as Brush;

            // 过渡时长：正确性优先，动画在 ApplyThemeBrush 内部，且**不碰共享画笔**。
            // 设为 TimeSpan.Zero 即可整体退化为瞬时切换（一处开关，落点唯一）。
            var duration = TimeSpan.FromMilliseconds(300);

            // 窗口背景 / 前景
            // 注意：这两个属性的值**同时来自 XAML 的 {DynamicResource}**，所以这里写本地值会遮住动态资源；
            // 但 RestoreThemeSnapshots() 记下了"原本无本地值"，下次换肤会 ClearValue 交还给 DynamicResource。
            if (Background is SolidColorBrush)
                ApplyThemeColor(this, Window.BackgroundProperty, windowBackground, duration);

            if (Foreground is SolidColorBrush)
                ApplyThemeColor(this, Window.ForegroundProperty, windowForeground, duration);

            // 窗口边框（渐变）—— 直接替换画笔实例
            if (windowBorderBrush != null && !ReferenceEquals(BorderBrush, windowBorderBrush))
            {
                Snapshot(this, Window.BorderBrushProperty);
                BorderBrush = windowBorderBrush;
            }

            // 主窗口根 Grid 背景
            var rootGrid = Content as Grid;
            if (rootGrid != null)
            {
                if (rootGrid.Background == null || rootGrid.Background == Brushes.Transparent)
                {
                    Snapshot(rootGrid, Panel.BackgroundProperty);
                    rootGrid.Background = new SolidColorBrush(windowBackground);
                }
                else if (rootGrid.Background is SolidColorBrush)
                {
                    ApplyThemeColor(rootGrid, Panel.BackgroundProperty, windowBackground, duration);
                }
            }

            // TabControl 背景 / 边框
            var tabControl = FindVisualChild<TabControl>(this);
            if (tabControl != null)
            {
                if (tabControl.Background is SolidColorBrush)
                    ApplyThemeColor(tabControl, Control.BackgroundProperty, tabControlBackground, duration);
                else
                {
                    Snapshot(tabControl, Control.BackgroundProperty);
                    tabControl.Background = new SolidColorBrush(tabControlBackground);
                }

                if (tabControl.BorderBrush is SolidColorBrush)
                    ApplyThemeColor(tabControl, Control.BorderBrushProperty, tabControlBorder, duration);
                else
                {
                    Snapshot(tabControl, Control.BorderBrushProperty);
                    tabControl.BorderBrush = new SolidColorBrush(tabControlBorder);
                }

                // TabItem 的内容 Border：直接落到终值（这里原先就是"直接设置"，保留原语义但补快照）
                foreach (TabItem tabItem in tabControl.Items)
                {
                    if (tabItem.Content is Border contentBorderElement)
                    {
                        Snapshot(contentBorderElement, Border.BackgroundProperty);
                        Snapshot(contentBorderElement, Border.BorderBrushProperty);
                        contentBorderElement.Background = new SolidColorBrush(contentBackground);
                        contentBorderElement.BorderBrush = new SolidColorBrush(contentBorder);
                    }
                }
            }

            // 标题栏前景 / 背景
            if (WindowTitle != null)
            {
                if (WindowTitle.Foreground is SolidColorBrush)
                    ApplyThemeColor(WindowTitle, Control.ForegroundProperty, titleForeground, duration);
            }

            var titleBar = FindName("TitleBarBorder") as Border;
            if (titleBar != null)
            {
                Snapshot(titleBar, Border.BackgroundProperty);
                titleBar.Background = new SolidColorBrush(titleBarBackground);
            }

            // 全树压色
            UpdateAllControlsTheme(this, contentBackground, contentBorder, textForeground, titleForeground, labelForeground, duration, isDarkMode, rootGrid);

            // 深色方向再补一轮"漏网的白底"（它内部的所有写入同样经过 Snapshot，浅色方向由回滚负责）
            if (isDarkMode)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ForceUpdateWhiteBackgrounds(true);
                    // 再次延迟，确保动态加载的控件也已完全渲染
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        ForceUpdateWhiteBackgrounds(true);
                    }), DispatcherPriority.Render);
                }), DispatcherPriority.Loaded);
            }

            // ★ 2026-09-26 修复（用户反馈）：**强调色渐变与光晕必须跟随主题**。
            //   问题：我把 AccentGradientBrush / AccentGlowEffect 在 ThemeColors*.xaml 里各写了一份
            //   **硬编码粉色**，但深色模式的强调色其实是**绿色**（LabelForegroundBrush = #6FBF7A）
            //   ⇒ 深色下"保存配置/保存为新配置"仍是粉的、卡片悬停还散粉色背光，看着像浅色模式残留。
            //   修法：在换肤的**唯一收口处**（本方法）用**主题强调色**现算渐变与光晕，
            //   覆盖掉字典里那份静态值 ⇒ 浅色=粉、深色=绿，且以后换任何主题都自动跟随。
            ApplyAccentDerivedTokens(labelForeground);
        }

        /// <summary>
        /// 由「主题强调色」派生并覆盖两个令牌：
        ///   · <c>AccentGradientBrush</c> —— 主行动按钮渐变（亮→原色→暗，三段）
        ///   · <c>AccentGlowEffect</c>   —— 主行动按钮的同色柔和投影
        ///   · <c>CardHoverGlowEffect</c>—— 卡片悬停光晕（同一强调色，避免与按钮撞色）
        /// 全部由基色按亮度比例算出，深/浅色都成立；换主题自动跟随，不需要在字典里各维护一份。
        /// 只写窗口级资源（<c>Resources</c>），作用域明确、不动 App 级字典（避免污染其它窗口）。
        /// </summary>
        private void ApplyAccentDerivedTokens(Color accent)
        {
            try
            {
                static Color Scale(Color c, double factor)
                    => Color.FromRgb(
                        (byte)Math.Clamp(c.R * factor, 0, 255),
                        (byte)Math.Clamp(c.G * factor, 0, 255),
                        (byte)Math.Clamp(c.B * factor, 0, 255));

                // 渐变：亮 → 原色 → 暗（相对亮度比例，浅色/深色主题都自然）
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                grad.GradientStops.Add(new GradientStop(Scale(accent, 1.18), 0));
                grad.GradientStops.Add(new GradientStop(accent, 0.55));
                grad.GradientStops.Add(new GradientStop(Scale(accent, 0.86), 1));
                if (grad.CanFreeze) grad.Freeze();
                Resources["AccentGradientBrush"] = grad;

                // 按钮投影 / 卡片悬停光晕：同色，分别给不同强度
                var buttonGlow = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = accent,
                    BlurRadius = 14,
                    ShadowDepth = 2,
                    Direction = 270,
                    Opacity = 0.45
                };
                if (buttonGlow.CanFreeze) buttonGlow.Freeze();
                Resources["AccentGlowEffect"] = buttonGlow;

                var cardGlow = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = accent,
                    BlurRadius = 12,      // ★ 从 20 收到 12：用户反馈"散发的粉色背光"太大太散
                    ShadowDepth = 0,
                    Direction = 270,
                    Opacity = 0.22        // ★ 从 0.45 收到 0.22：只做"边缘提亮"，不做大片光晕
                };
                if (cardGlow.CanFreeze) cardGlow.Freeze();
                Resources["CardHoverGlowEffect"] = cardGlow;
            }
            catch
            {
                // 派生失败（取不到资源等）→ 保留字典里的静态值，不影响功能
            }
        }

        /// <summary>
        /// 从当前资源字典解析主题令牌（SolidColorBrush / 其它 Brush 都支持）。
        /// 解析不到时回退调用方给的旧字面量 —— 保底不崩，也不至于把界面涂成透明。
        /// </summary>
        private Color ResolveThemeColor(string resourceKey, Color fallback)
        {
            try
            {
                var found = TryFindResource(resourceKey);
                switch (found)
                {
                    case SolidColorBrush solid:
                        return solid.Color;
                    case LinearGradientBrush gradient when gradient.GradientStops.Count > 0:
                        return gradient.GradientStops[gradient.GradientStops.Count - 1].Color;
                }
            }
            catch
            {
                // 资源字典正在切换等瞬时状态 —— 回退字面量
            }
            return fallback;
        }

        private void UpdateAllControlsTheme(DependencyObject parent, Color contentBackground, Color contentBorder, Color textForeground, Color titleForeground, Color labelForeground, TimeSpan duration, bool isDarkMode, Grid? rootGrid = null)
        {
            // 递归更新所有控件的颜色
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                // 处理 Border
                if (child is Border border)
                {
                    // 更新所有 Border 的背景（白色背景改为深色，深色背景改为白色）
                    var targetBg = contentBackground;
                    if (border.Background is SolidColorBrush bgBrush)
                    {
                        var currentBg = bgBrush.Color;
                        // 检查是否是白色或浅色背景（更宽松的条件：R、G、B 都大于 180）
                        // 对于纯白色（#FFFFFFFF），直接强制更新，不依赖动画
                        bool needsUpdate = false;
                        bool isPureWhite = currentBg.A > 0 && currentBg.R > 200 && currentBg.G > 200 && currentBg.B > 200;
                        
                        if (isDarkMode && currentBg.A > 0 && currentBg.R > 180 && currentBg.G > 180 && currentBg.B > 180)
                        {
                            needsUpdate = true;
                        }
                        // 浅色模式：深色背景改为白色（更宽松的条件：R、G、B 都小于 60）
                        else if (!isDarkMode && currentBg.A > 0 && currentBg.R < 60 && currentBg.G < 60 && currentBg.B < 60)
                        {
                            needsUpdate = true;
                        }
                        
                        if (needsUpdate)
                        {
                            // 对于纯白色背景或冻结画笔，直接强制设置，不依赖动画
                            if (isPureWhite || bgBrush.IsFrozen)
                            {
                                Snapshot(border, Border.BackgroundProperty);
                                border.Background = new SolidColorBrush(targetBg);
                            }
                            else
                            {
                                ApplyThemeColor(border, Border.BackgroundProperty, targetBg, duration);
                            }
                        }
                        // 即使不满足条件，如果是纯白色，也强制更新（确保硬编码的白色被更新）
                        else if (isPureWhite && isDarkMode)
                        {
                            Snapshot(border, Border.BackgroundProperty);
                            border.Background = new SolidColorBrush(targetBg);
                        }
                    }
                    // 如果背景为 null 或透明，直接设置背景色（因为已移除硬编码）
                    else if (border.Background == null || border.Background == Brushes.Transparent)
                    {
                        // 对于 TabItem 的 Content Border（Margin.Top == 8 且 BorderThickness.Top == 2），直接设置
                        if (border.Margin.Top == 8 && border.BorderThickness.Top == 2)
                        {
                            Snapshot(border, Border.BackgroundProperty);
                            Snapshot(border, Border.BorderBrushProperty);
                            border.Background = new SolidColorBrush(targetBg);
                            border.BorderBrush = new SolidColorBrush(contentBorder);
                        }
                        // 其他 Border 如果有子元素，也设置背景色
                        else if (VisualTreeHelper.GetChildrenCount(border) > 0 ||
                                 border.ActualWidth > 0 || border.ActualHeight > 0)
                        {
                            Snapshot(border, Border.BackgroundProperty);
                            border.Background = new SolidColorBrush(targetBg);
                        }
                    }
                    // 如果背景是硬编码的白色（通过检查 XAML 中的 #FFFFFFFF），直接强制设置
                    else if (isDarkMode)
                    {
                        // 尝试通过反射或其他方式检查是否是硬编码的白色
                        // 这里我们直接强制设置，因为如果背景不是 SolidColorBrush，可能是其他类型
                        // 但为了安全，我们只在深色模式下强制设置
                        var borderType = border.Background?.GetType();
                        if (borderType != null && borderType != typeof(SolidColorBrush))
                        {
                            // 如果是其他类型的画笔，也尝试设置为目标背景
                            Snapshot(border, Border.BackgroundProperty);
                            border.Background = new SolidColorBrush(targetBg);
                        }
                    }

                    // 更新所有粉色边框（#FFE0568F）为深色模式下的绿色（#FF6FBF7A）
                    if (border.BorderBrush is SolidColorBrush borderBrush)
                    {
                        var currentBorderColor = borderBrush.Color;
                        // 检查是否是粉色边框（#FFE0568F 或类似的粉色）- 更宽松的条件
                        if (isDarkMode && currentBorderColor.A > 0 && 
                            currentBorderColor.R > 180 && currentBorderColor.G < 160 && currentBorderColor.B > 140)
                        {
                            ApplyThemeColor(border, Border.BorderBrushProperty, contentBorder, duration);
                        }
                        // 浅色模式：绿色边框改为粉色
                        else if (!isDarkMode && currentBorderColor.A > 0 &&
                                 currentBorderColor.R < 120 && currentBorderColor.G > 140 && currentBorderColor.B < 120)
                        {
                            ApplyThemeColor(border, Border.BorderBrushProperty, contentBorder, duration);
                        }
                    }
                    // 处理渐变边框（LinearGradientBrush）- 直接替换
                    else if (border.BorderBrush is LinearGradientBrush gradientBrush && isDarkMode)
                    {
                        // 检查渐变中是否包含粉色，如果是则替换为绿色渐变（旧称；现为两段中性+强调渐变）
                        bool hasPink = false;
                        foreach (var stop in gradientBrush.GradientStops)
                        {
                            if (stop.Color.R > 200 && stop.Color.G < 150 && stop.Color.B > 150)
                            {
                                hasPink = true;
                                break;
                            }
                        }
                        if (hasPink)
                        {
                            var themedBorderBrush = TryFindResource("WindowBorderBrush") as Brush;
                            if (themedBorderBrush != null)
                            {
                                Snapshot(border, Border.BorderBrushProperty);
                                border.BorderBrush = themedBorderBrush;
                            }
                        }
                    }
                }
                // 处理 Panel 类型的背景（Grid, StackPanel 等）
                else if (child is Panel panel)
                {
                    if (panel.Background is SolidColorBrush panelBgBrush)
                    {
                        var currentBg = panelBgBrush.Color;
                        // 检查是否是白色或浅色背景（更宽松的条件：R、G、B 都大于 180）
                        if (isDarkMode && currentBg.A > 0 && currentBg.R > 180 && currentBg.G > 180 && currentBg.B > 180)
                        {
                            ApplyThemeColor(panel, Panel.BackgroundProperty, contentBackground, duration);
                        }
                        // 浅色模式：深色背景改为白色（更宽松的条件：R、G、B 都小于 60）
                        else if (!isDarkMode && currentBg.A > 0 && currentBg.R < 60 && currentBg.G < 60 && currentBg.B < 60)
                        {
                            ApplyThemeColor(panel, Panel.BackgroundProperty, contentBackground, duration);
                        }
                    }
                    // 如果背景为 null 或透明，设置背景色
                    else if ((panel.Background == null || panel.Background == Brushes.Transparent))
                    {
                        // 主窗口的根 Grid 使用 WindowBackground
                        if (rootGrid != null && panel == rootGrid)
                        {
                            Snapshot(panel, Panel.BackgroundProperty);
                            panel.Background = new SolidColorBrush(ResolveThemeColor("WindowBackgroundBrush",
                                isDarkMode ? Color.FromArgb(0xFF, 0x17, 0x18, 0x1A) : Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)));
                        }
                        // 其他 Panel 使用 ContentBackground（浅色和深色模式都需要设置）
                        // 更积极地设置：只要 Panel 有子元素或实际大小，就设置背景
                        else if (panel.Children.Count > 0 || panel.ActualWidth > 0 || panel.ActualHeight > 0)
                        {
                            Snapshot(panel, Panel.BackgroundProperty);
                            panel.Background = new SolidColorBrush(contentBackground);
                        }
                    }
                }
                // 处理 ScrollViewer
                else if (child is System.Windows.Controls.ScrollViewer scrollViewer)
                {
                    if (scrollViewer.Background is SolidColorBrush svBgBrush)
                    {
                        var currentBg = svBgBrush.Color;
                        if (isDarkMode && currentBg.A > 0 && currentBg.R > 200 && currentBg.G > 200 && currentBg.B > 200)
                        {
                            ApplyThemeColor(scrollViewer, Control.BackgroundProperty, contentBackground, duration);
                        }
                        else if (!isDarkMode && currentBg.A > 0 && currentBg.R < 50 && currentBg.G < 50 && currentBg.B < 50)
                        {
                            ApplyThemeColor(scrollViewer, Control.BackgroundProperty, contentBackground, duration);
                        }
                    }
                    // 强制设置 ScrollViewer 的背景色（即使为 null 或透明，浅色和深色模式都需要设置）
                    else
                    {
                        Snapshot(scrollViewer, Control.BackgroundProperty);
                        scrollViewer.Background = new SolidColorBrush(contentBackground);
                    }
                }
                // 处理 HandyControl 的 ScrollViewer
                else if (child is HandyControl.Controls.ScrollViewer hcScrollViewer)
                {
                    if (hcScrollViewer.Background is SolidColorBrush hcSvBgBrush)
                    {
                        var currentBg = hcSvBgBrush.Color;
                        if (isDarkMode && currentBg.A > 0 && currentBg.R > 200 && currentBg.G > 200 && currentBg.B > 200)
                        {
                            ApplyThemeColor(hcScrollViewer, Control.BackgroundProperty, contentBackground, duration);
                        }
                        else if (!isDarkMode && currentBg.A > 0 && currentBg.R < 50 && currentBg.G < 50 && currentBg.B < 50)
                        {
                            ApplyThemeColor(hcScrollViewer, Control.BackgroundProperty, contentBackground, duration);
                        }
                    }
                    // 强制设置 HandyControl ScrollViewer 的背景色（即使为 null 或透明，浅色和深色模式都需要设置）
                    else
                    {
                        Snapshot(hcScrollViewer, Control.BackgroundProperty);
                        hcScrollViewer.Background = new SolidColorBrush(contentBackground);
                    }
                }
                // 处理 Control 类型的背景和前景色（包括 Button, TextBox, ComboBox, CheckBox, Label 等）
                else if (child is System.Windows.Controls.Control control)
                {
                    // 更新背景色
                    if (control.Background is SolidColorBrush controlBgBrush)
                    {
                        var currentBg = controlBgBrush.Color;
                        // 检查是否是白色或浅色背景
                        if (isDarkMode && currentBg.A > 0 && currentBg.R > 200 && currentBg.G > 200 && currentBg.B > 200)
                        {
                            ApplyThemeColor(control, Control.BackgroundProperty, contentBackground, duration);
                        }
                        // 浅色模式：深色背景改为白色
                        else if (!isDarkMode && currentBg.A > 0 && currentBg.R < 50 && currentBg.G < 50 && currentBg.B < 50)
                        {
                            ApplyThemeColor(control, Control.BackgroundProperty, contentBackground, duration);
                        }
                    }

                    // 对 ComboBox 做更强制的处理，确保在深/浅色切换时"盒子"背景正确更新
                    if (control is System.Windows.Controls.ComboBox stdCombo)
                    {
                        var targetBg = contentBackground;
                        var targetBorder = contentBorder;

                        // 处理背景色（一律走 ApplyThemeBrush：内部先快照，且不就地改共享画笔）
                        ApplyThemeBrush(stdCombo, Control.BackgroundProperty, new SolidColorBrush(targetBg), duration);

                        // 处理边框色
                        ApplyThemeBrush(stdCombo, Control.BorderBrushProperty, new SolidColorBrush(targetBorder), duration);
                    }
                    else if (control is HandyControl.Controls.ComboBox handyCombo)
                    {
                        var targetBg = contentBackground;
                        var targetBorder = contentBorder;

                        // 处理背景色
                        ApplyThemeBrush(handyCombo, Control.BackgroundProperty, new SolidColorBrush(targetBg), duration);

                        // 处理边框色
                        ApplyThemeBrush(handyCombo, Control.BorderBrushProperty, new SolidColorBrush(targetBorder), duration);
                    }

                    // 更新前景色（深色/灰色文字改为白色）
                    if (control.Foreground is SolidColorBrush controlFgBrush)
                    {
                        var currentFg = controlFgBrush.Color;
                        var shouldUpdate = false;
                        Color targetFg = textForeground;

                        // 检查是否是标题标签（粉色 #FFE0568F 或绿色 #FF6FBF7A）
                        if ((currentFg.R > 200 && currentFg.G < 100 && currentFg.B > 150) || // 粉色
                            (currentFg.R < 100 && currentFg.G > 150 && currentFg.B < 100)) // 绿色
                        {
                            targetFg = labelForeground;
                            shouldUpdate = true;
                        }
                        // 检查是否是深色或灰色文本（需要改为白色）
                        // 包括 #FF333333、#FF666666 等灰色
                        else if (isDarkMode && currentFg.A > 200 && 
                                 (currentFg.R < 150 && currentFg.G < 150 && currentFg.B < 150))
                        {
                            targetFg = textForeground;
                            shouldUpdate = true;
                        }
                        // 浅色模式：白色文字改为深色
                        else if (!isDarkMode && currentFg.A > 200 &&
                                 (currentFg.R > 200 && currentFg.G > 200 && currentFg.B > 200))
                        {
                            targetFg = textForeground;
                            shouldUpdate = true;
                        }

                        if (shouldUpdate)
                        {
                            ApplyThemeColor(control, Control.ForegroundProperty, targetFg, duration);
                        }
                    }
                }
                // 处理 TextBlock（不是 Control，需要单独处理）
                else if (child is TextBlock textBlock)
                {
                    // 更新前景色
                    if (textBlock.Foreground is SolidColorBrush textBlockFgBrush)
                    {
                        var currentFg = textBlockFgBrush.Color;
                        // 检查是否是深色或灰色文本（需要改为白色）
                        if (isDarkMode && currentFg.A > 200 && 
                            (currentFg.R < 150 && currentFg.G < 150 && currentFg.B < 150))
                        {
                            ApplyThemeColor(textBlock, TextBlock.ForegroundProperty, textForeground, duration);
                        }
                        // 浅色模式：白色文字改为深色
                        else if (!isDarkMode && currentFg.A > 200 &&
                                 (currentFg.R > 200 && currentFg.G > 200 && currentFg.B > 200))
                        {
                            ApplyThemeColor(textBlock, TextBlock.ForegroundProperty, textForeground, duration);
                        }
                    }
                }
                // 处理 Rectangle（线条）
                else if (child is System.Windows.Shapes.Rectangle rectangle)
                {
                    // 更新 Rectangle 的 Fill（粉色线条改为绿色）
                    if (rectangle.Fill is SolidColorBrush rectFillBrush)
                    {
                        var currentFill = rectFillBrush.Color;
                        // 检查是否是粉色（#FFE0568F 或类似）
                        if (isDarkMode && currentFill.A > 0 && 
                            currentFill.R > 180 && currentFill.G < 160 && currentFill.B > 140)
                        {
                            ApplyThemeColor(rectangle, System.Windows.Shapes.Shape.FillProperty, labelForeground, duration);
                        }
                        // 浅色模式：绿色线条改为粉色
                        else if (!isDarkMode && currentFill.A > 0 &&
                                 currentFill.R < 120 && currentFill.G > 140 && currentFill.B < 120)
                        {
                            ApplyThemeColor(rectangle, System.Windows.Shapes.Shape.FillProperty, labelForeground, duration);
                        }
                    }
                }
                // 处理 Label（标题文字）
                else if (child is Label label)
                {
                    // 更新 Label 的前景色
                    if (label.Foreground is SolidColorBrush labelFgBrush)
                    {
                        var currentFg = labelFgBrush.Color;
                        var shouldUpdate = false;
                        Color targetFg = textForeground;

                        // 检查是否是标题标签（粉色 #FFE0568F 或绿色 #FF6FBF7A）
                        if ((currentFg.R > 180 && currentFg.G < 160 && currentFg.B > 140) || // 粉色
                            (currentFg.R < 120 && currentFg.G > 140 && currentFg.B < 120)) // 绿色
                        {
                            targetFg = labelForeground;
                            shouldUpdate = true;
                        }
                        // 检查是否是深色或灰色文本（需要改为白色）
                        else if (isDarkMode && currentFg.A > 200 && 
                                 (currentFg.R < 150 && currentFg.G < 150 && currentFg.B < 150))
                        {
                            targetFg = textForeground;
                            shouldUpdate = true;
                        }
                        // 浅色模式：白色文字改为深色
                        else if (!isDarkMode && currentFg.A > 200 &&
                                 (currentFg.R > 200 && currentFg.G > 200 && currentFg.B > 200))
                        {
                            targetFg = textForeground;
                            shouldUpdate = true;
                        }

                        if (shouldUpdate)
                        {
                            ApplyThemeColor(label, Control.ForegroundProperty, targetFg, duration);
                        }
                    }
                }
                // 处理 TabItem（TabControl 的标签页）
                else if (child is TabItem tabItem)
                {
                    // 更新 TabItem 的背景
                    if (tabItem.Background is SolidColorBrush tabItemBgBrush)
                    {
                        var currentBg = tabItemBgBrush.Color;
                        if (isDarkMode && currentBg.A > 0 && currentBg.R > 200 && currentBg.G > 200 && currentBg.B > 200)
                        {
                            ApplyThemeColor(tabItem, Control.BackgroundProperty, contentBackground, duration);
                        }
                        else if (!isDarkMode && currentBg.A > 0 && currentBg.R < 50 && currentBg.G < 50 && currentBg.B < 50)
                        {
                            ApplyThemeColor(tabItem, Control.BackgroundProperty, contentBackground, duration);
                        }
                    }
                    else if (tabItem.Background == null || tabItem.Background == Brushes.Transparent)
                    {
                        // TabItem 内容区域应该使用 ContentBackground
                        Snapshot(tabItem, Control.BackgroundProperty);
                        tabItem.Background = new SolidColorBrush(contentBackground);
                    }
                    
                    // 特别处理 TabItem 的 Content（通常是 Border）
                    // TabItem 的内容区域（Border）需要直接处理
                    if (tabItem.Content is Border tabContentBorder)
                    {
                        // 更新 Border 的背景 - 强制设置，不依赖动画（因为可能是硬编码的画笔）
                        var targetBg = contentBackground;
                        if (tabContentBorder.Background is SolidColorBrush contentBgBrush)
                        {
                            var currentBg = contentBgBrush.Color;
                            // 检查是否是白色背景（#FFFFFFFF）或深色背景需要切换
                            bool needsUpdate = false;
                            if (isDarkMode && currentBg.A > 0 && currentBg.R > 180 && currentBg.G > 180 && currentBg.B > 180)
                            {
                                needsUpdate = true;
                            }
                            else if (!isDarkMode && currentBg.A > 0 && currentBg.R < 60 && currentBg.G < 60 && currentBg.B < 60)
                            {
                                needsUpdate = true;
                            }
                            
                            if (needsUpdate)
                            {
                                // 如果画笔被冻结，直接创建新画笔；否则走统一入口（内部先快照）
                                if (contentBgBrush.IsFrozen)
                                {
                                    Snapshot(tabContentBorder, Border.BackgroundProperty);
                                    tabContentBorder.Background = new SolidColorBrush(targetBg);
                                }
                                else
                                {
                                    ApplyThemeColor(tabContentBorder, Border.BackgroundProperty, targetBg, duration);
                                }
                            }
                        }
                        else
                        {
                            // 如果背景为 null 或透明，强制设置背景色
                            Snapshot(tabContentBorder, Border.BackgroundProperty);
                            tabContentBorder.Background = new SolidColorBrush(targetBg);
                        }
                        
                        // 更新 Border 的边框颜色（粉色改为绿色）
                        var targetBorderColor = contentBorder;
                        if (tabContentBorder.BorderBrush is SolidColorBrush contentBorderBrush)
                        {
                            var currentBorderColor = contentBorderBrush.Color;
                            bool needsBorderUpdate = false;
                            if (isDarkMode && currentBorderColor.A > 0 && 
                                currentBorderColor.R > 180 && currentBorderColor.G < 160 && currentBorderColor.B > 140)
                            {
                                needsBorderUpdate = true;
                            }
                            else if (!isDarkMode && currentBorderColor.A > 0 &&
                                     currentBorderColor.R < 120 && currentBorderColor.G > 140 && currentBorderColor.B < 120)
                            {
                                needsBorderUpdate = true;
                            }
                            
                            if (needsBorderUpdate)
                            {
                                if (contentBorderBrush.IsFrozen)
                                {
                                    Snapshot(tabContentBorder, Border.BorderBrushProperty);
                                    tabContentBorder.BorderBrush = new SolidColorBrush(targetBorderColor);
                                }
                                else
                                {
                                    ApplyThemeColor(tabContentBorder, Border.BorderBrushProperty, targetBorderColor, duration);
                                }
                            }
                        }
                        else if (tabContentBorder.BorderBrush == null)
                        {
                            Snapshot(tabContentBorder, Border.BorderBrushProperty);
                            tabContentBorder.BorderBrush = new SolidColorBrush(targetBorderColor);
                        }
                    }
                }

                // 递归处理子元素
                UpdateAllControlsTheme(child, contentBackground, contentBorder, textForeground, titleForeground, labelForeground, duration, isDarkMode, rootGrid);
            }
        }

        /// <summary>
        /// 强制更新所有白色背景为深色背景（用于确保所有硬编码的白色背景都被更新）。
        /// ★ 只在深色方向执行 —— 浅色方向由 ApplyThemeWithAnimation 开头的 RestoreThemeSnapshots() 负责，
        ///   这是"对称且可逆"的两半，缺一不可。
        /// ★ 目标色来自当前主题令牌（不再写死 #FF212125 / #FF2FA42F）。
        /// ★ 本方法内**所有**写入都经过 Snapshot(...)，且不再就地改共享画笔。
        /// </summary>
        private void ForceUpdateWhiteBackgrounds(bool isDarkMode)
        {
            if (!isDarkMode) return; // 只在深色模式下执行

            var targetBg = ResolveThemeColor("ContentBackgroundBrush", Color.FromArgb(0xFF, 0x1E, 0x1F, 0x22));
            var targetBorder = ResolveThemeColor("ContentBorderBrush", Color.FromArgb(0xFF, 0x30, 0x32, 0x36));
            var targetText = ResolveThemeColor("TextForegroundBrush", Color.FromArgb(0xFF, 0xE8, 0xEA, 0xED));
            
            // 递归查找所有控件
            void UpdateControls(DependencyObject parent)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    
                    // 处理 Border - 最优先处理，因为这是主要内容区域
                    if (child is Border border)
                    {
                        // 强制更新白色背景 - 不检查任何条件，直接设置
                        bool shouldUpdate = false;
                        if (border.Background is SolidColorBrush bgBrush)
                        {
                            var currentBg = bgBrush.Color;
                            // 检查是否是白色或浅色背景（R、G、B 都大于 200）
                            if (currentBg.A > 0 && currentBg.R > 200 && currentBg.G > 200 && currentBg.B > 200)
                            {
                                shouldUpdate = true;
                            }
                        }
                        else if (border.Background == null || border.Background == Brushes.Transparent)
                        {
                            // 如果背景为 null 或透明，且 Border 有子元素，设置背景色
                            if (VisualTreeHelper.GetChildrenCount(border) > 0 || 
                                border.ActualWidth > 0 || border.ActualHeight > 0)
                            {
                                shouldUpdate = true;
                            }
                        }
                        
                        // 强制设置背景色（不依赖原画笔）
                        if (shouldUpdate)
                        {
                            Snapshot(border, Border.BackgroundProperty);
                            border.Background = new SolidColorBrush(targetBg);
                            // 确保设置生效
                            border.InvalidateVisual();
                        }
                        
                        // 强制更新粉色边框为绿色
                        if (border.BorderBrush is SolidColorBrush borderBrush)
                        {
                            var currentBorderColor = borderBrush.Color;
                            // 检查是否是粉色边框（R > 180, G < 160, B > 140）
                            if (currentBorderColor.A > 0 && 
                                currentBorderColor.R > 180 && currentBorderColor.G < 160 && currentBorderColor.B > 140)
                            {
                                Snapshot(border, Border.BorderBrushProperty);
                                border.BorderBrush = new SolidColorBrush(targetBorder);
                            }
                        }
                    }
                    // 处理 Panel（Grid, StackPanel 等）
                    else if (child is Panel panel)
                    {
                        if (panel.Background is SolidColorBrush panelBgBrush)
                        {
                            var currentBg = panelBgBrush.Color;
                            if (currentBg.A > 0 && currentBg.R > 200 && currentBg.G > 200 && currentBg.B > 200)
                            {
                                Snapshot(panel, Panel.BackgroundProperty);
                                panel.Background = new SolidColorBrush(targetBg);
                            }
                        }
                        else if (panel.Background == null || panel.Background == Brushes.Transparent)
                        {
                            if (panel.Children.Count > 0 || panel.ActualWidth > 0 || panel.ActualHeight > 0)
                            {
                                Snapshot(panel, Panel.BackgroundProperty);
                                panel.Background = new SolidColorBrush(targetBg);
                            }
                        }
                    }
                    // 处理 Control（Button, TextBox, ComboBox 等）
                    else if (child is Control control)
                    {
                        if (control.Background is SolidColorBrush controlBgBrush)
                        {
                            var currentBg = controlBgBrush.Color;
                            if (currentBg.A > 0 && currentBg.R > 200 && currentBg.G > 200 && currentBg.B > 200)
                            {
                                Snapshot(control, Control.BackgroundProperty);
                                control.Background = new SolidColorBrush(targetBg);
                            }
                        }
                        
                        // 更新前景色（黑色文字改为白色）
                        if (control.Foreground is SolidColorBrush controlFgBrush)
                        {
                            var currentFg = controlFgBrush.Color;
                            if (currentFg.A > 200 && currentFg.R < 100 && currentFg.G < 100 && currentFg.B < 100)
                            {
                                Snapshot(control, Control.ForegroundProperty);
                                control.Foreground = new SolidColorBrush(targetText);
                            }
                        }
                    }
                    // 处理 TextBlock
                    else if (child is TextBlock textBlock)
                    {
                        if (textBlock.Foreground is SolidColorBrush textFgBrush)
                        {
                            var currentFg = textFgBrush.Color;
                            if (currentFg.A > 200 && currentFg.R < 100 && currentFg.G < 100 && currentFg.B < 100)
                            {
                                Snapshot(textBlock, TextBlock.ForegroundProperty);
                                textBlock.Foreground = new SolidColorBrush(targetText);
                            }
                        }
                    }
                    // 处理 ScrollViewer
                    else if (child is System.Windows.Controls.ScrollViewer scrollViewer)
                    {
                        if (scrollViewer.Background is SolidColorBrush svBgBrush)
                        {
                            var currentBg = svBgBrush.Color;
                            if (currentBg.A > 0 && currentBg.R > 200 && currentBg.G > 200 && currentBg.B > 200)
                            {
                                Snapshot(scrollViewer, Control.BackgroundProperty);
                                scrollViewer.Background = new SolidColorBrush(targetBg);
                            }
                        }
                        else if (scrollViewer.Background == null || scrollViewer.Background == Brushes.Transparent)
                        {
                            Snapshot(scrollViewer, Control.BackgroundProperty);
                            scrollViewer.Background = new SolidColorBrush(targetBg);
                        }
                    }
                    // 处理 HandyControl 的 ScrollViewer
                    else if (child is HandyControl.Controls.ScrollViewer hcScrollViewer)
                    {
                        if (hcScrollViewer.Background is SolidColorBrush hcSvBgBrush)
                        {
                            var currentBg = hcSvBgBrush.Color;
                            if (currentBg.A > 0 && currentBg.R > 200 && currentBg.G > 200 && currentBg.B > 200)
                            {
                                Snapshot(hcScrollViewer, Control.BackgroundProperty);
                                hcScrollViewer.Background = new SolidColorBrush(targetBg);
                            }
                        }
                        else if (hcScrollViewer.Background == null || hcScrollViewer.Background == Brushes.Transparent)
                        {
                            Snapshot(hcScrollViewer, Control.BackgroundProperty);
                            hcScrollViewer.Background = new SolidColorBrush(targetBg);
                        }
                    }
                    
                    // 递归处理子元素
                    UpdateControls(child);
                }
            }
            
            // 从窗口根元素开始遍历 - 多次遍历确保覆盖所有情况
            UpdateControls(this);
            
            // 额外延迟多次，确保所有控件都已完全加载和渲染
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateControls(this);
                // 再次延迟，确保所有动态加载的控件也被更新
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateControls(this);
                }), DispatcherPriority.Render);
            }), DispatcherPriority.Loaded);
        }

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
                // 向上遍历可视化树，检查是否在 Popup 中。
                // ★ 2026-09-24 修复：原实现无条件 VisualTreeHelper.GetParent(current)，
                //   当滚轮落点是文本节点（System.Windows.Documents.Run 等 ContentElement）时，
                //   VisualTreeHelper 会抛 InvalidOperationException("'…Run' is not a Visual or Visual3D")
                //   （用户实测报错框，栈顶即本方法）。ContentElement 必须走逻辑树。
                var current = source;
                while (current != null)
                {
                    if (current is Popup popup && popup.IsOpen)
                    {
                        // 如果鼠标在打开的 Popup 中，不处理此事件，让 Popup 自己处理
                        return;
                    }

                    current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                        ? VisualTreeHelper.GetParent(current)
                        : LogicalTreeHelper.GetParent(current);
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

        // ===== 旅行词条搜索（5.3.1 B1）：按纯文本过滤 Tab3 全部 5 个词条 DataGrid =====
        // ★ 2026-09-25 两处改动：
        //   ① 崩溃修复：原先用 CollectionViewSource.GetDefaultView(src).Filter —— 但 VM 里这 5 个
        //      列表是 **BindingList<T>**，其默认视图是 BindingListCollectionView，**CanFilter=false**，
        //      赋值 Filter 直接抛 NotSupportedException（用户实测：搜索框一输入就弹异常框）。
        //      现改为「把过滤后的 List 直接赋给 DataGrid.ItemsSource」。
        //   ② 性能/交互：原先挂 TextChanged，每敲一个字符就把 5 个网格 × 数百行的 TMP 富文本
        //      逐条剥标签（TmpMarkup.ToPlainText 是解析器），实测"输入后要等一阵才刷新"。
        //      现改为**输入 + 确认按钮 / Enter** 两段式：只在明确提交时算一次；
        //      并把「渲染文本 → 纯文本」的结果按词条对象缓存，重复搜索不再重解析。
        private string _travelBuffSearchKeyword = string.Empty;
        private bool _travelBuffSearchHooked;

        /// <summary>词条纯文本缓存：TmpMarkup.ToPlainText 是解析器，同一词条只剥一次。</summary>
        private readonly Dictionary<TravelBuffVM, string> _travelBuffPlainTextCache = new();

        private void TravelBuffSearchButton_Click(object sender, RoutedEventArgs e)
        {
            CommitTravelBuffSearch();
        }

        private void TravelBuffSearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitTravelBuffSearch();
                e.Handled = true;
            }
        }

        /// <summary>提交当前输入框内容并重放过滤（按钮 / Enter 都走这里）。</summary>
        private void CommitTravelBuffSearch()
        {
            _travelBuffSearchKeyword = TravelBuffSearchBox?.Text ?? string.Empty;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ApplyTravelBuffFilters();
            sw.Stop();
            if (sw.ElapsedMilliseconds >= 200)
                System.Diagnostics.Debug.WriteLine($"[词条搜索] '{_travelBuffSearchKeyword}' 用时 {sw.ElapsedMilliseconds} ms");
        }

        /// <summary>按当前关键词重放 5 个词条网格的过滤（提交搜索 / VM 集合实例变化时调用）。</summary>
        private void ApplyTravelBuffFilters()
        {
            if (DataContext is not ModifierViewModel vm) return;
            HookTravelBuffSearchReload(vm);
            ApplyTravelBuffFilter(TravelBuffs, vm.TravelBuffs);
            ApplyTravelBuffFilter(InGameBuffs, vm.InGameBuffs);
            ApplyTravelBuffFilter(Debuffs, vm.Debuffs);
            ApplyTravelBuffFilter(InGameInvestBuffs, vm.InGameInvestBuffs);
            ApplyTravelBuffFilter(InGameDebuffs, vm.InGameDebuffs);
        }

        /// <summary>VM 重建 BindingList（InitData 重载）后重放过滤，避免界面停留在旧实例的快照上。</summary>
        private void HookTravelBuffSearchReload(ModifierViewModel vm)
        {
            if (_travelBuffSearchHooked) return;
            _travelBuffSearchHooked = true;
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(ModifierViewModel.TravelBuffs)
                    or nameof(ModifierViewModel.InGameBuffs)
                    or nameof(ModifierViewModel.Debuffs)
                    or nameof(ModifierViewModel.InGameInvestBuffs)
                    or nameof(ModifierViewModel.InGameDebuffs))
                {
                    _travelBuffPlainTextCache.Clear();
                    Dispatcher.BeginInvoke(new Action(ApplyTravelBuffFilters), DispatcherPriority.Background);
                }
            };
        }

        /// <summary>取词条的纯文本（剥 TMP 标签），按对象缓存；词条本身不可变，故可长期复用。</summary>
        private string GetTravelBuffPlainText(TravelBuffVM item)
        {
            if (item is null) return string.Empty;
            if (_travelBuffPlainTextCache.TryGetValue(item, out var cached)) return cached;

            var text = item.TravelBuff?.Text;
            var plain = string.IsNullOrEmpty(text) ? string.Empty : PVZRHTools.Utils.TmpMarkup.ToPlainText(text);
            _travelBuffPlainTextCache[item] = plain;
            return plain;
        }

        private void ApplyTravelBuffFilter(System.Windows.Controls.DataGrid? grid,
            System.ComponentModel.BindingList<TravelBuffVM>? source)
        {
            if (grid is null || source is null) return;

            // 过滤按纯文本匹配：查询词与词条原文都先剥 TMP 标签（显示仍走渲染层，原文不改）
            var keyword = PVZRHTools.Utils.TmpMarkup.ToPlainText(_travelBuffSearchKeyword).Trim();

            if (keyword.Length == 0)
            {
                if (!ReferenceEquals(grid.ItemsSource, source))
                    grid.ItemsSource = source;
                return;
            }

            var filtered = new List<TravelBuffVM>(source.Count);
            foreach (var item in source)
            {
                var plain = GetTravelBuffPlainText(item);
                if (plain.Length == 0) continue;
                if (plain.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    filtered.Add(item);
            }

            // 过滤结果与当前一致时不动 ItemsSource，避免无谓的重建与滚动条跳动
            if (grid.ItemsSource is List<TravelBuffVM> prev && prev.Count == filtered.Count)
            {
                var same = true;
                for (var i = 0; i < prev.Count; i++)
                {
                    if (!ReferenceEquals(prev[i], filtered[i])) { same = false; break; }
                }
                if (same) return;
            }

            grid.ItemsSource = filtered;
        }

        public void TitleBar_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton is MouseButtonState.Pressed && e.RightButton is MouseButtonState.Released &&
                e.MiddleButton is MouseButtonState.Released) DragMove();
        }

        private void ResetWindowSizeButton_Click(object sender, RoutedEventArgs e)
        {
            // Restore the modifier window to its default design size.
            if (WindowState != WindowState.Normal)
                WindowState = WindowState.Normal;

            Width = 800;
            Height = 450;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            // 最小化修饰窗口（标题栏原「关于」入口已迁至杂项 Tab7，原位让给最小化）。
            WindowState = WindowState.Minimized;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (DataContext is ModifierViewModel vm)
                vm.Save();
            GlobalHotKey.Destroy();
            Application.Current.Shutdown();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            GlobalHotKey.Awake();
            if (DataContext is ModifierViewModel vm)
            {
                foreach (var hvm in from hvm in vm.Hotkeys where hvm.CurrentKeyB != Key.None select hvm)
                    hvm.UpdateHotKey();
            }
            
            // 添加窗口消息钩子以支持边框拖拽调整大小
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            hwndSource?.AddHook(WndProc);
        }

        /// <summary>
        /// 处理窗口消息，实现边框拖拽调整大小
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCCALCSIZE)
            {
                // Remove system non-client area to avoid the white strip above custom title bar.
                handled = true;
                return IntPtr.Zero;
            }

            if (msg == WM_NCACTIVATE)
            {
                // Tell Windows NC area is handled to avoid inactive white border painting.
                handled = true;
                return new IntPtr(1);
            }

            if (msg == WM_NCPAINT)
            {
                // Prevent default non-client repaint, which can flash white edges.
                handled = true;
                return IntPtr.Zero;
            }

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
            long packed = lParam.ToInt64();
            int x = unchecked((short)(packed & 0xFFFF));
            int y = unchecked((short)((packed >> 16) & 0xFFFF));

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
                var mergedDictionaries = Application.Current.Resources.MergedDictionaries;

                // 先移除当前加载的语言资源（避免误删主题/控件样式）
                ResourceDictionary? currentLangDict = null;
                foreach (var dict in mergedDictionaries)
                {
                    var source = dict.Source?.OriginalString;
                    if (source != null && source.Contains("Lang.", StringComparison.OrdinalIgnoreCase))
                    {
                        currentLangDict = dict;
                        break;
                    }
                }

                if (currentLangDict != null)
                    mergedDictionaries.Remove(currentLangDict);

                // 根据下拉框选择加载对应语言资源
                ResourceDictionary lang;
                var selectedText = (string?)((ComboBoxItem?)e.AddedItems[0]!)!.Content;
                if (selectedText == "简体中文")
                    lang = LangZH_CN;
                else if (selectedText == "English")
                    lang = LangEN_US;
                else
                    lang = LangRU_RU;

                mergedDictionaries.Add(lang);
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