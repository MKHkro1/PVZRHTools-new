using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using HandyControl.Themes;
using ToolModData;
using static ToolModData.Modifier;

namespace PVZRHTools;

public partial class App : Application
{
    public static bool inited;

    static App()
    {
        DataSync = new Lazy<DataSync>();
    }

    public static Lazy<DataSync> DataSync { get; set; }

    public static InitData? InitData { get; set; }

  /// <summary>
  /// 当存档需要完整恢复但 InitData 尚未就绪时，暂存待应用的设置。
  /// </summary>
    public static ModifierSaveModel? PendingSaveModel { get; set; }

    /// <summary>
    /// 修改器 UI 尚未就绪时，暂存游戏端推送的游戏内快捷键。
    /// </summary>
    public static List<int>? PendingGameInGameHotkeys { get; set; }

    public static bool IsBepInEx => Directory.Exists("BepInEx");

    public void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception + "\n" + e.Exception.InnerException + "\n" +
                        e.Exception.InnerException?.InnerException);
        e.Handled = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (inited)
            try
            {
                DataSync.Value.SendData(new Exit());
                Thread.Sleep(100);
                var socket = DataSync.Value.modifierSocket;
                if (socket != null && DataSync.Value.IsConnected)
                {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

        inited = false;
    }

    public static void SwitchTheme(bool isDarkMode)
    {
        var app = Current as App;
        if (app == null) return;

        var mergedDictionaries = app.Resources.MergedDictionaries;

        // ★ 1) HandyControl 皮肤：把 Skin 字典插到【最高优先级】—— 2026-09-24 根因修复·实证版
        //   探针实证（D:\Web\二创\.hcprobe，独立 WPF 程序直接测 HandyControl 3.5.1）：
        //     · Theme 实例的 MergedDictionaries = [0]=Skin*.xaml（35 个 *Color 键）+ [1]=Theme.xaml(952 键)
        //     · Theme.xaml 的调色板画笔（RegionBrush / BorderBrush / BackgroundBrush / PrimaryTextBrush…）
        //       形如 Color="{DynamicResource <名>Color}" —— **动态引用**，真值就在 Skin 字典那 35 个键里
        //     · 因此"深色皮肤是否可见"取决于 **Skin 字典在资源查找顺序中的位置**
        //   WPF 合并字典是【逆序查找：后加入者优先】，而旧实现 Insert(0, skin) 把皮肤扔到
        //   【最低优先级】，被其后所有字典压制 ⇒ 深色皮肤形同不存在
        //   （这就是 hc:ComboBox / hc:NumericUpDown / HC 按钮 / 复选框在深色下仍为浅色的真因）。
        //   安全性：Skin 字典只含颜色键、**不含任何 Style** ⇒ 不会盖掉 SimpleTheme 的简约隐式样式。
        // 1b) 同时设置 hc:Theme 实例的 Skin（官方途径，双保险；即便取不到，第 1 步也独立生效）
        var hcTheme = FindThemeDictionary(mergedDictionaries);
        if (hcTheme != null)
            hcTheme.Skin = isDarkMode ? HandyControl.Data.SkinType.Dark : HandyControl.Data.SkinType.Default;

        // ★ 2) 本地令牌字典（ThemeColorsLight/Dark）：**原地替换**，保持它在合并列表中的位置。
        var themeColorsIndex = -1;
        for (var i = 0; i < mergedDictionaries.Count; i++)
        {
            var src = mergedDictionaries[i].Source?.OriginalString;
            if (src?.Contains("ThemeColorsLight.xaml") == true || src?.Contains("ThemeColorsDark.xaml") == true)
            {
                themeColorsIndex = i;
                break;
            }
        }

        var newThemeColors = new ResourceDictionary
        {
            Source = new Uri(isDarkMode ? "/Styles/ThemeColorsDark.xaml" : "/Styles/ThemeColorsLight.xaml",
                UriKind.Relative)
        };

        if (themeColorsIndex >= 0)
            mergedDictionaries[themeColorsIndex] = newThemeColors;
        else
            mergedDictionaries.Add(newThemeColors);

        // ★ 3) 皮肤字典：先移除上一次加的那份，再 Add 到**末尾**（最高优先级）
        var staleSkin = mergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString?.Contains("SkinDefault.xaml") == true ||
            d.Source?.OriginalString?.Contains("SkinDark.xaml") == true);
        if (staleSkin != null)
            mergedDictionaries.Remove(staleSkin);

        mergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(isDarkMode
                ? "pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml"
                : "pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml")
        });
    }

    /// <summary>
    /// 在合并字典里递归寻找 HandyControl 的 Theme 实例（Theme 实例会把自己的皮肤/样式
    /// 放在自身的 MergedDictionaries 里，所以只看一层不够）。找不到返回 null，调用方容忍。
    /// </summary>
    private static Theme? FindThemeDictionary(IEnumerable<ResourceDictionary> dictionaries)
    {
        foreach (var dictionary in dictionaries)
        {
            if (dictionary is Theme theme) return theme;

            var nested = FindThemeDictionary(dictionary.MergedDictionaries);
            if (nested != null) return nested;
        }

        return null;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            if (e.Args[0] == CommandLineToken)
            {
                DataSync = new Lazy<DataSync>(new DataSync(Convert.ToInt32(e.Args[1])));
                
                // 延迟读取 InitData.json，等待游戏完成 LateInit 并发送最新数据
                // 如果游戏在 5 秒内发送了 InitData（通过 case 0），就使用发送的数据
                // 如果 5 秒后还没有收到，就从文件读取作为后备方案
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    // 如果还没有收到游戏发送的 InitData，就从文件读取
                    if (InitData == null)
                    {
                        try
                        {
                            if (File.Exists("./PVZRHTools/InitData.json"))
                            {
                                InitData = JsonSerializer.Deserialize(File.ReadAllText("./PVZRHTools/InitData.json"),
                                    InitDataSGC.Default.InitData);
                                // 如果 MainWindow 已经创建，重新加载词条列表
                                var mainWindow = Current.MainWindow as MainWindow;
                                if (mainWindow != null)
                                {
                                    mainWindow.ViewModel.ReloadBuffsFromInitData();
                                }
                            }
                        }
                        catch
                        {
                            // 静默处理错误
                        }
                    }
                };
                timer.Start();
            }
            else
            {
                Shutdown();
            }
        }
        catch (IndexOutOfRangeException)
        {
            MessageBox.Show("请直接启动游戏本体，修改窗口不允许单独启动。\n若你已经启动了游戏，说明修改器安装错误，请把修改器压缩包里所有文件解压至游戏本体exe所在文件夹中，然后直接启动游戏。");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString());
            Environment.Exit(0);
        }
    }
}