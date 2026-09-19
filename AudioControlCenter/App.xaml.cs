using System;
using System.Threading;
using System.Windows;
using AudioControlCenter.Services;
using AudioControlCenter.Views;
using Hardcodet.Wpf.TaskbarNotification;

namespace AudioControlCenter;

public partial class App : Application
{
    /// <summary>全局单例视图模型</summary>
    public static ControlCenterViewModel Vm { get; } = new();

    /// <summary>是否正在退出（区别于隐藏到托盘）</summary>
    public static bool IsExiting { get; private set; }

    private TaskbarIcon? _tray;
    private PopupWindow? _popup;
    private MainWindow? _main;
    private Mutex? _mutex;
    private DateTime _lastSingleClickUtc;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 异常日志（诊断用）
        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {args.Exception}\r\n");
            }
            catch { }
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    $"[{DateTime.Now:HH:mm:ss}] UNHANDLED {args.ExceptionObject}\r\n");
            }
            catch { }
        };

        // 应用主题（0=跟随系统 1=深色 2=浅色）
        ApplyTheme(Vm.Settings.ThemeMode);

        // 单实例
        _mutex = new Mutex(true, "AudioControlCenter_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("音频控制中心已在运行，请查看托盘图标。", "音频控制中心",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 托盘图标
        _tray = new TaskbarIcon
        {
            Icon = TrayIconFactory.Create(),
            ToolTipText = "音频控制中心",
        };
        _tray.TrayLeftMouseUp += Tray_SingleClick;
        _tray.TrayMouseDoubleClick += Tray_DoubleClick;
        _tray.ContextMenu = BuildContextMenu();

        // 低电量通知
        Vm.LowBatteryNotify += OnLowBatteryNotify;

        // 调试辅助：--show-main 启动即打开主窗口；--show-popup 启动即弹出控制中心面板
        if (e.Args.Contains("--show-main"))
            ShowMainWindow();
        else if (e.Args.Contains("--show-popup"))
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, ShowPopup);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private void OnLowBatteryNotify(string mac, string name, int percent)
    {
        try
        {
            _tray?.ShowBalloonTip("蓝牙设备电量不足",
                $"「{name}」剩余电量 {percent}%，请及时充电。",
                BalloonIcon.Warning);
        }
        catch { }
    }

    private void Tray_SingleClick(object sender, RoutedEventArgs e)
    {
        // 双击防抖：350ms 内的第二次单击视为双击的一部分，交给 Tray_DoubleClick
        var now = DateTime.UtcNow;
        if ((now - _lastSingleClickUtc).TotalMilliseconds < 350)
        {
            _lastSingleClickUtc = now;
            return;
        }
        _lastSingleClickUtc = now;
        ShowPopup();
    }

    private void Tray_DoubleClick(object sender, RoutedEventArgs e)
    {
        _popup?.Close();
        ShowMainWindow();
    }

    /// <summary>单击托盘：弹出控制中心面板（屏幕右下角）</summary>
    private void ShowPopup()
    {
        if (_popup == null || !_popup.IsLoaded)
        {
            _popup = new PopupWindow();
            _popup.Closed += (_, _) => _popup = null;
        }
        _popup.ShowNearTray();
    }

    /// <summary>双击托盘：打开主窗口</summary>
    private void ShowMainWindow()
    {
        if (_main == null || !_main.IsLoaded)
        {
            _main = new MainWindow();
            _main.Closed += (_, _) => _main = null;
        }
        if (!_main.IsVisible)
            _main.Show();
        if (_main.WindowState == WindowState.Minimized)
            _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private System.Windows.Controls.ContextMenu BuildContextMenu()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        var open = new System.Windows.Controls.MenuItem { Header = "打开主窗口" };
        open.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(open);

        var refresh = new System.Windows.Controls.MenuItem { Header = "立即刷新" };
        refresh.Click += (_, _) => Vm.RefreshAll();
        menu.Items.Add(refresh);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exit = new System.Windows.Controls.MenuItem { Header = "退出" };
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        return menu;
    }

    private void ExitApp()
    {
        IsExiting = true;
        _popup?.Close();
        _main?.Close();
        Shutdown();
    }

    /// <summary>切换主题：0=跟随系统 1=深色 2=浅色</summary>
    public static void ApplyTheme(int mode)
    {
        try
        {
            if (mode == 0)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var v = key?.GetValue("AppsUseLightTheme");
                mode = (v is int i && i == 1) ? 2 : 1;
            }
            var uri = mode == 2
                ? new Uri("Themes/Light.xaml", UriKind.Relative)
                : new Uri("Themes/Dark.xaml", UriKind.Relative);
            var dic = new ResourceDictionary { Source = uri };
            var appDict = Current.Resources;
            if (appDict.MergedDictionaries.Count > 0)
                appDict.MergedDictionaries[0] = dic;
            else
                appDict.MergedDictionaries.Add(dic);
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        Vm.Audio.Dispose();
        base.OnExit(e);
    }
}
