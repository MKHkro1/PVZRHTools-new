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
                DataSync.Value.modifierSocket.Shutdown(SocketShutdown.Both);
                DataSync.Value.modifierSocket.Close();
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

        var resources = app.Resources;
        var mergedDictionaries = resources.MergedDictionaries;

        // 移除 HandyControl 皮肤
        var skinDict = mergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString?.Contains("SkinDefault.xaml") == true ||
            d.Source?.OriginalString?.Contains("SkinDark.xaml") == true);
        if (skinDict != null)
            mergedDictionaries.Remove(skinDict);

        // 移除背景板主题颜色（ThemeColorsLight/Dark）
        var themeColorsDict = mergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString?.Contains("ThemeColorsLight.xaml") == true ||
            d.Source?.OriginalString?.Contains("ThemeColorsDark.xaml") == true);
        if (themeColorsDict != null)
            mergedDictionaries.Remove(themeColorsDict);

        // 添加 HandyControl 皮肤
        var newSkinSource = isDarkMode
            ? "pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml"
            : "pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml";
        mergedDictionaries.Insert(0, new ResourceDictionary { Source = new Uri(newSkinSource) });

        // 添加背景板主题颜色（可配置的软编码）
        var newThemeColorsSource = isDarkMode
            ? "/Styles/ThemeColorsDark.xaml"
            : "/Styles/ThemeColorsLight.xaml";
        mergedDictionaries.Insert(1, new ResourceDictionary { Source = new Uri(newThemeColorsSource, UriKind.Relative) });
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