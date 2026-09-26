using System;
using System.Threading;
using System.Windows;
using BTAudioSwitcher.Services;
using BTAudioSwitcher.Views;
using Hardcodet.Wpf.TaskbarNotification;

namespace BTAudioSwitcher;

public partial class App : Application
{
    /// <summary>全局单例视图模型</summary>
    public static ControlCenterViewModel Vm { get; } = new();

    /// <summary>是否正在退出（区别于隐藏到托盘）</summary>
    public static bool IsExiting { get; private set; }

    private TaskbarIcon? _tray;
    private PopupWindow? _popup;
    private QuickPanelWindow? _quick;
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
        // 监听系统主题变化（跟随系统模式下自动切换）
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (Vm.Settings.ThemeMode == 0) ApplyTheme(0);
        };

        // 单实例
        _mutex = new Mutex(true, "BT_Audio_Switcher_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("BT Audio Switcher 已在运行，请查看托盘图标。", "BT Audio Switcher",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 托盘图标
        _tray = new TaskbarIcon
        {
            Icon = TrayIconFactory.Create(),
            ToolTipText = "BT Audio Switcher",
        };
        _tray.TrayLeftMouseUp += Tray_SingleClick;
        _tray.TrayMouseDoubleClick += Tray_DoubleClick;
        _tray.ContextMenu = BuildContextMenu();

        // 低电量通知
        Vm.LowBatteryNotify += OnLowBatteryNotify;

        // 蓝牙连接/断开系统通知
        Vm.DeviceStateNotify += OnDeviceStateNotify;

        // 事件驱动：启动音频设备监听（设备变化即时刷新，替代高频轮询）
        Vm.Audio.StartMonitoring();

        // 初始全量刷新
        Vm.RefreshAll();

        // 调试辅助：--show-main 启动即打开主窗口；--show-quick 弹出迷你面板；--show-popup 弹完整控制中心
        if (e.Args.Contains("--show-main"))
            ShowMainWindow();
        else if (e.Args.Contains("--show-quick"))
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, ShowQuickPanel);
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

    /// <summary>蓝牙设备连接/断开通知</summary>
    private void OnDeviceStateNotify(string name, bool connected)
    {
        try
        {
            if (!Vm.Settings.DeviceChangeNotify) return;
            _tray?.ShowBalloonTip(
                connected ? "蓝牙设备已连接" : "蓝牙设备已断开",
                $"「{name}」{(connected ? "已连接" : "已断开")}",
                BalloonIcon.Info);
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
        ShowQuickPanel();
    }

    private void Tray_DoubleClick(object sender, RoutedEventArgs e)
    {
        _quick?.Close();
        ShowMainWindow();
    }

    /// <summary>单击托盘：弹出迷你控制面板（最近设备 + 默认设备 + 全局音量）</summary>
    private void ShowQuickPanel()
    {
        if (_quick == null || !_quick.IsLoaded)
        {
            _quick = new QuickPanelWindow();
            _quick.Closed += (_, _) => _quick = null;
        }
        _quick.ShowNearTray();
    }

    /// <summary>从迷你面板打开完整控制中心（静态入口，供视图调用）</summary>
    public static void ShowFullPanel()
    {
        var app = (App)Current;
        app.ShowPopup();
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

        var open = new System.Windows.Controls.MenuItem { Header = "📋 控制中心面板" };
        open.Click += (_, _) => ShowPopup();
        menu.Items.Add(open);

        var main = new System.Windows.Controls.MenuItem { Header = "🖥️ 打开主窗口" };
        main.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(main);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // ---------- 快速切换输出设备（点即切） ----------
        var outputMenu = new System.Windows.Controls.MenuItem { Header = "🔊 默认输出设备" };
        BuildDeviceSubMenu(outputMenu, isOutput: true);
        menu.Items.Add(outputMenu);

        // ---------- 快速切换输入设备 ----------
        var inputMenu = new System.Windows.Controls.MenuItem { Header = "🎙️ 默认输入设备" };
        BuildDeviceSubMenu(inputMenu, isOutput: false);
        menu.Items.Add(inputMenu);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // ---------- 快捷开关（折叠分组） ----------
        var switches = new System.Windows.Controls.MenuItem { Header = "⚡ 快捷开关" };
        var autoSwitch = new System.Windows.Controls.MenuItem
        {
            Header = "自动切换默认输出",
            IsCheckable = true,
            IsChecked = Vm.Settings.AutoSwitchOutput,
        };
        autoSwitch.Click += (_, _) =>
        {
            Vm.Settings.AutoSwitchOutput = autoSwitch.IsChecked;
            RebuildTrayMenu();
        };
        switches.Items.Add(autoSwitch);

        var notify = new System.Windows.Controls.MenuItem
        {
            Header = "设备变化通知",
            IsCheckable = true,
            IsChecked = Vm.Settings.DeviceChangeNotify,
        };
        notify.Click += (_, _) =>
        {
            Vm.Settings.DeviceChangeNotify = notify.IsChecked;
            RebuildTrayMenu();
        };
        switches.Items.Add(notify);

        var autostart = new System.Windows.Controls.MenuItem
        {
            Header = "开机自启",
            IsCheckable = true,
            IsChecked = Vm.Settings.AutoStart,
        };
        autostart.Click += (_, _) =>
        {
            Vm.Settings.AutoStart = autostart.IsChecked;
            RebuildTrayMenu();
        };
        switches.Items.Add(autostart);

        var battery = new System.Windows.Controls.MenuItem
        {
            Header = "低电量提醒",
            IsCheckable = true,
            IsChecked = Vm.Settings.LowBatteryAlert,
        };
        battery.Click += (_, _) =>
        {
            Vm.Settings.LowBatteryAlert = battery.IsChecked;
            RebuildTrayMenu();
        };
        switches.Items.Add(battery);
        menu.Items.Add(switches);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var themeMenu = new System.Windows.Controls.MenuItem { Header = "🎨 主题" };
        var themes = new[] { ("跟随系统", 0), ("深色", 1), ("浅色", 2) };
        foreach (var (label, mode) in themes)
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = label,
                IsCheckable = true,
                IsChecked = Vm.Settings.ThemeMode == mode,
            };
            var m = mode;
            item.Click += (_, _) =>
            {
                Vm.Settings.ThemeMode = m;
                ApplyTheme(m);
                RebuildTrayMenu();
            };
            themeMenu.Items.Add(item);
        }
        menu.Items.Add(themeMenu);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var refresh = new System.Windows.Controls.MenuItem { Header = "🔄 立即刷新" };
        refresh.Click += (_, _) => Vm.RefreshAll();
        menu.Items.Add(refresh);

        menu.Items.Add(new System.Windows.Controls.Separator());

        var exit = new System.Windows.Controls.MenuItem { Header = "❌ 退出" };
        exit.Click += (_, _) => ExitApp();
        menu.Items.Add(exit);

        return menu;
    }

    /// <summary>构建设备子菜单（输出/输入，点击即切换默认设备；打开时动态刷新）</summary>
    private void BuildDeviceSubMenu(System.Windows.Controls.MenuItem menu, bool isOutput)
    {
        menu.SubmenuOpened += (_, _) =>
        {
            try
            {
                menu.Items.Clear();
                var devices = isOutput ? Vm.Audio.RenderDevices : Vm.Audio.CaptureDevices;
                var defaultId = isOutput ? Vm.Audio.DefaultOutputId : Vm.Audio.DefaultInputId;
                foreach (var d in devices)
                {
                    var item = new System.Windows.Controls.MenuItem
                    {
                        Header = d.IsPresent ? d.Name : $"{d.Name}（未连接）",
                        IsCheckable = true,
                        IsChecked = string.Equals(d.DeviceId, defaultId, StringComparison.OrdinalIgnoreCase),
                        Tag = d,
                    };
                    item.Click += (_, _) =>
                    {
                        // 从 VM 集合取同 id 引用，保证下拉选中项一致
                        var match = (isOutput ? Vm.RenderDevices : Vm.CaptureDevices)
                            .FirstOrDefault(x => string.Equals(x.DeviceId, d.DeviceId, StringComparison.OrdinalIgnoreCase));
                        if (match != null)
                        {
                            if (isOutput) Vm.SelectedDefaultOutput = match;
                            else Vm.SelectedDefaultInput = match;
                        }
                        else
                        {
                            if (isOutput) Vm.Audio.SetGlobalDefaultOutput(d.DeviceId);
                            else Vm.Audio.SetGlobalDefaultInput(d.DeviceId);
                        }
                        Vm.RefreshSessionsOnly();
                    };
                    menu.Items.Add(item);
                }
            }
            catch { }
        };
    }

    private void RebuildTrayMenu()
    {
        if (_tray != null)
            _tray.ContextMenu = BuildContextMenu();
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

            // 按个性化设置更新全局基准字号
            if (Current.Resources["BaseFontSize"] is double oldSize)
            {
                double newSize = Vm?.Settings.FontSize is int fs && fs >= 14 ? fs : oldSize;
                Current.Resources["BaseFontSize"] = newSize;
            }
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
