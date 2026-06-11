using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using HandyControl.Themes;
using ToolModData;
using static ToolModData.Modifier;

namespace PVZRHTools;

public partial class App : Application
{
    private const string MutexName = "Infinite75.PVZRHTools";
    private static Mutex? _mutex;

    public static bool inited;

    static App()
    {
        DataSync = new Lazy<DataSync>(() => throw new InvalidOperationException("DataSync 尚未初始化"));
    }

    public static Lazy<DataSync> DataSync { get; set; }

    public static InitData? InitData { get; set; }

    public static bool IsBepInEx => Directory.Exists("BepInEx");

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

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
        {
            try
            {
                DataSync.Value.SendData(new Exit());
                Thread.Sleep(100);
                DataSync.Value.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
        }

        inited = false;
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }

    public static void SwitchTheme(bool isDarkMode)
    {
        var app = Current as App;
        if (app == null)
        {
            return;
        }

        var resources = app.Resources;
        var mergedDictionaries = resources.MergedDictionaries;

        var skinDict = mergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString?.Contains("SkinDefault.xaml") == true ||
            d.Source?.OriginalString?.Contains("SkinDark.xaml") == true);
        if (skinDict != null)
        {
            mergedDictionaries.Remove(skinDict);
        }

        var themeColorsDict = mergedDictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString?.Contains("ThemeColorsLight.xaml") == true ||
            d.Source?.OriginalString?.Contains("ThemeColorsDark.xaml") == true);
        if (themeColorsDict != null)
        {
            mergedDictionaries.Remove(themeColorsDict);
        }

        var newSkinSource = isDarkMode
            ? "pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml"
            : "pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml";
        mergedDictionaries.Insert(0, new ResourceDictionary { Source = new Uri(newSkinSource) });

        var newThemeColorsSource = isDarkMode
            ? "/Styles/ThemeColorsDark.xaml"
            : "/Styles/ThemeColorsLight.xaml";
        mergedDictionaries.Insert(1, new ResourceDictionary { Source = new Uri(newThemeColorsSource, UriKind.Relative) });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                ActivateExistingWindow();
                Environment.Exit(0);
                return;
            }

            if (e.Args.Length >= 1 &&
                (e.Args[0] == CommandLineToken || e.Args[0] == RunModifierArgument))
            {
                DataSync = new Lazy<DataSync>(() => new DataSync());

                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(5)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (InitData != null)
                    {
                        return;
                    }

                    TryLoadInitDataFromDisk();
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
            MessageBox.Show(
                "请直接启动游戏本体，修改窗口不允许单独启动。\n" +
                "若已启动游戏仍出现此提示，请将 PVZRHTools.exe 放在游戏根目录，或保留 PVZRHTools 子目录中的旧版布局。");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString());
            Environment.Exit(0);
        }
    }

    private static void TryLoadInitDataFromDisk()
    {
        try
        {
            string initDataPath = ModifierPaths.GetInitDataPath();
            if (!File.Exists(initDataPath))
            {
                return;
            }

            InitData = JsonSerializer.Deserialize(File.ReadAllText(initDataPath), InitDataSGC.Default.InitData);
            if (Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ViewModel.ReloadBuffsFromInitData();
            }
        }
        catch
        {
        }
    }

    private static void ActivateExistingWindow()
    {
        using var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName))
        {
            if (process.Id == currentProcess.Id || process.MainWindowHandle == IntPtr.Zero)
            {
                continue;
            }

            SetForegroundWindow(process.MainWindowHandle);
            if (IsIconic(process.MainWindowHandle))
            {
                ShowWindow(process.MainWindowHandle, 1);
            }

            break;
        }
    }
}
