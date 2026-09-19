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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private void Tray_SingleClick(object sender, RoutedEventArgs e)
    {
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

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        Vm.Audio.Dispose();
        base.OnExit(e);
    }
}
