using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using BTAudioSwitcher.Models;

namespace BTAudioSwitcher.Services;

/// <summary>控制中心视图模型：聚合音频 + 蓝牙 + 记忆服务 + 实时监测</summary>
public sealed class ControlCenterViewModel : ObservableObject
{
    public AudioService Audio { get; }
    public BluetoothService Bluetooth { get; }
    public SettingsStore Settings { get; }

    public ObservableCollection<AppSessionItem> Sessions { get; } = new();
    public ObservableCollection<BluetoothDeviceItem> BluetoothDevices { get; } = new();
    public ObservableCollection<ScanDeviceItem> ScanResults { get; } = new();
    public ObservableCollection<AudioDeviceItem> RenderDevices { get; } = new();
    public ObservableCollection<AudioDeviceItem> CaptureDevices { get; } = new();
    public ObservableCollection<BluetoothDeviceItem> ConnectedBluetoothDevices { get; } = new();
    public bool NoConnectedBluetooth => ConnectedBluetoothDevices.Count == 0;

    public bool NoSessions => Sessions.Count == 0;
    private readonly DispatcherTimer _sessionTimer;
    private readonly DispatcherTimer _batteryTimer;
    private readonly DispatcherTimer _btTimer;

    /// <summary>低电量提醒（mac, name, percent），由 App 弹托盘通知</summary>
    public event Action<string, string, int>? LowBatteryNotify;

    private AudioDeviceItem? _selectedDefaultOutput;
    public AudioDeviceItem? SelectedDefaultOutput
    {
        get => _selectedDefaultOutput;
        set
        {
            if (Set(ref _selectedDefaultOutput, value) && value != null && _loading == 0)
            {
                Audio.SetGlobalDefaultOutput(value.DeviceId);
                RefreshSessionsOnly();
            }
        }
    }

    private AudioDeviceItem? _selectedDefaultInput;
    public AudioDeviceItem? SelectedDefaultInput
    {
        get => _selectedDefaultInput;
        set
        {
            if (Set(ref _selectedDefaultInput, value) && value != null && _loading == 0)
            {
                Audio.SetGlobalDefaultInput(value.DeviceId);
                RefreshSessionsOnly();
            }
        }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    private bool _showAllBluetooth;
    /// <summary>蓝牙设备区是否展开显示全部</summary>
    public bool ShowAllBluetooth
    {
        get => _showAllBluetooth;
        set
        {
            if (Set(ref _showAllBluetooth, value))
            {
                OnPropertyChanged(nameof(ShowAllButtonText));
                OnPropertyChanged(nameof(VisibleBluetoothDevices));
                OnPropertyChanged(nameof(ShowAllButtonVisibility));
            }
        }
    }

    public string ShowAllButtonText => ShowAllBluetooth ? "收起" : "查看全部";
    public bool ShowAllButtonVisibility => BluetoothDevices.Count > 3;

    private int _selectedTab = 0;
    /// <summary>左侧导航：0=功能区 1=个性化区 2=设置区</summary>
    public int SelectedTab
    {
        get => _selectedTab;
        set => Set(ref _selectedTab, value);
    }

    private int _loading; // 加载期间禁止触发 setter 副作用

    /// <summary>界面基准字号（个性化区可调，应用到全局）</summary>
    public int FontSize
    {
        get => Settings.FontSize;
        set
        {
            if (Set(ref _fontSize, value))
            {
                Settings.FontSize = value;
                App.ApplyTheme(Settings.ThemeMode); // 重建资源后更新 BaseFontSize
            }
        }
    }
    private int _fontSize;

    /// <summary>最近蓝牙设备显示数量</summary>
    public int RecentDevicesCount
    {
        get => Settings.RecentDevicesCount;
        set
        {
            if (Set(ref _recentDevicesCount, value))
            {
                Settings.RecentDevicesCount = value;
                OnPropertyChanged(nameof(VisibleBluetoothDevices));
            }
        }
    }
    private int _recentDevicesCount;

    /// <summary>新设备激活时自动设为默认输出（个性化区快捷开关）</summary>
    public bool AutoSwitchOutput
    {
        get => Settings.AutoSwitchOutput;
        set
        {
            if (Set(ref _autoSwitchOutput, value))
            {
                Settings.AutoSwitchOutput = value;
                StatusText = value ? "已开启：新设备连接时自动切换为默认输出" : "已关闭自动切换";
            }
        }
    }
    private bool _autoSwitchOutput;

    /// <summary>蓝牙连接/断开弹系统通知</summary>
    public bool DeviceChangeNotify
    {
        get => Settings.DeviceChangeNotify;
        set
        {
            if (Set(ref _deviceChangeNotify, value))
                Settings.DeviceChangeNotify = value;
        }
    }
    private bool _deviceChangeNotify;

    public ControlCenterViewModel()
    {
        Audio = new AudioService();
        Bluetooth = new BluetoothService();
        Settings = new SettingsStore();
        Settings.Load();
        Bluetooth.Settings = Settings;
        Bluetooth.DevicesChanged += OnBluetoothDevicesChanged;
        Bluetooth.ScanResultsChanged += OnScanResultsChanged;

        // ========== 事件驱动（省资源）：设备变化/默认变化即时响应，替代高频轮询 ==========
        Audio.DeviceStructureChanged += OnAudioDeviceStructureChanged;
        Audio.DefaultDeviceChanged += OnDefaultDeviceChangedBySystem;
        Audio.OutputDeviceActivated += OnOutputDeviceActivated;
        Audio.SessionCreated += OnSessionCreatedHint;

        // 会话实时监测：5 秒低频兜底（新应用发声由 SessionCreated 事件即时响应；此轮询兜底会话退出/事件丢失）
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _sessionTimer.Tick += (_, _) => RefreshSessionsOnly();
        // 蓝牙连接状态低频兜底：30 秒（事件驱动为主，此处仅在事件丢失时兜底）
        _btTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _btTimer.Tick += async (_, _) => await RefreshBluetoothStateAsync();
        // 电量定时刷新：60 秒低频；连接状态变化时也会补一次
        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _batteryTimer.Tick += async (_, _) => await RefreshBatteriesAsync();

        _sessionTimer.Start();
        _btTimer.Start();
        _batteryTimer.Start();

        _fontSize = Settings.FontSize;
        _recentDevicesCount = Settings.RecentDevicesCount;
        _autoSwitchOutput = Settings.AutoSwitchOutput;
        _deviceChangeNotify = Settings.DeviceChangeNotify;
    }

    // ---------- 事件驱动处理 ----------

    private readonly object _eventLock = new();
    private DateTime _lastAutoSwitchUtc = DateTime.MinValue;
    private DateTime _lastDeviceNotifyUtc = DateTime.MinValue;

    /// <summary>新音频会话创建（可能来自任意线程）→ UI 线程刷新会话</summary>
    private void OnSessionCreatedHint()
    {
        try
        {
            if (_loading > 0) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                RefreshSessionsOnly();
            else
                dispatcher.BeginInvoke(RefreshSessionsOnly);
        }
        catch { }
    }

    /// <summary>音频设备结构变化（插入/拔出/连接/断开）→ 刷新设备与蓝牙状态</summary>
    private void OnAudioDeviceStructureChanged()
    {
        try
        {
            if (_loading > 0) return;
            _ = RefreshBluetoothStateAsync(); // 设备结构变化驱动蓝牙状态刷新
        }
        catch { }
    }

    /// <summary>系统默认设备被外部改动（Windows 设置/其他软件）→ 同步 UI</summary>
    private void OnDefaultDeviceChangedBySystem()
    {
        try
        {
            if (_loading > 0) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                RefreshDefaultSelection();
            else
                dispatcher.BeginInvoke(RefreshDefaultSelection);
        }
        catch { }
    }

    /// <summary>新输出设备激活 → 自动切换规则（可在个性化区开关）</summary>
    private void OnOutputDeviceActivated(string deviceId)
    {
        try
        {
            if (_loading > 0) return;
            if (!Settings.AutoSwitchOutput) return;
            // 防抖：3 秒内只自动切换一次，避免误切
            var now = DateTime.UtcNow;
            lock (_eventLock)
            {
                if ((now - _lastAutoSwitchUtc).TotalSeconds < 3) return;
                _lastAutoSwitchUtc = now;
            }
            var dev = Audio.RenderDevices.FirstOrDefault(d =>
                string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
            if (dev == null || dev.IsDefault || !dev.IsPresent) return;
            // 只对蓝牙/刚刚激活的设备自动切换（避免拔插线缆误切）
            if (Audio.SetGlobalDefaultOutput(deviceId))
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.CheckAccess())
                    RefreshDefaultSelection();
                else
                    dispatcher.BeginInvoke(RefreshDefaultSelection);
                StatusText = $"已自动切换到输出设备：{dev.Name}";
            }
        }
        catch { }
    }

    /// <summary>重新同步默认设备选中（系统外部修改后 UI 跟随）</summary>
    private void RefreshDefaultSelection()
    {
        _loading++;
        try
        {
            Audio.RefreshDevices();
            RefreshDeviceCollections();
            SelectedDefaultOutput = RenderDevices.FirstOrDefault(d => d.IsDefault);
            SelectedDefaultInput = CaptureDevices.FirstOrDefault(d => d.IsDefault);
        }
        finally
        {
            _loading--;
        }
    }

    /// <summary>只刷新蓝牙连接状态（不重建设备列表）</summary>
    private async Task RefreshBluetoothStateAsync()
    {
        try
        {
            Audio.RefreshDevices();
            Bluetooth.Refresh(Audio);
            var changed = new List<(string Name, bool Connected)>();
            for (int i = 0; i < BluetoothDevices.Count; i++)
            {
                var src = Bluetooth.Devices.FirstOrDefault(x => x.Key == BluetoothDevices[i].Key);
                if (src != null && BluetoothDevices[i].IsConnected != src.IsConnected)
                {
                    BluetoothDevices[i].IsConnected = src.IsConnected;
                    BluetoothDevices[i].RefreshStatus();
                    changed.Add((src.Name, src.IsConnected));
                }
            }
            ConnectedBluetoothDevices.Clear();
            foreach (var b in BluetoothDevices.Where(x => x.IsConnected)) ConnectedBluetoothDevices.Add(b);
            OnPropertyChanged(nameof(NoConnectedBluetooth));
            OnPropertyChanged(nameof(VisibleBluetoothDevices));
            OnPropertyChanged(nameof(RecentQuickDevices));

            // 连接状态变化 → 触发通知（App 弹托盘提示）+ 补一次电量
            if (changed.Count > 0 && Settings.DeviceChangeNotify)
            {
                var now = DateTime.UtcNow;
                bool allow;
                lock (_eventLock)
                {
                    allow = (now - _lastDeviceNotifyUtc).TotalSeconds >= 3;
                    if (allow) _lastDeviceNotifyUtc = now;
                }
                if (allow)
                {
                    var first = changed[0];
                    DeviceStateNotify?.Invoke(first.Name, first.Connected);
                }
                _ = RefreshBatteriesAsync();
            }
        }
        catch { }
    }

    /// <summary>蓝牙设备连接/断开事件（App 弹系统通知）</summary>
    public event Action<string, bool>? DeviceStateNotify;
    private void OnBluetoothDevicesChanged()
    {
        OnPropertyChanged(nameof(ShowAllButtonVisibility));
    }

    private void OnScanResultsChanged()
    {
        // 将扫描结果同步到 UI 集合（保持去重）
        ScanResults.Clear();
        foreach (var s in Bluetooth.ScanResults)
            ScanResults.Add(s);
        if (IsScanning)
            ScanStatusText = $"扫描中… 已发现 {ScanResults.Count} 个设备";
    }

    /// <summary>全量刷新（设备 + 蓝牙 + 会话）</summary>
    public void RefreshAll()
    {
        _loading++;
        try
        {
            Audio.RefreshDevices();
            Bluetooth.Refresh(Audio);

            RefreshDeviceCollections();

            BluetoothDevices.Clear();
            foreach (var b in Bluetooth.Devices)
            {
                b.OwnerAudio = Audio;
                BluetoothDevices.Add(b);
            }
            ConnectedBluetoothDevices.Clear();
            foreach (var b in BluetoothDevices.Where(x => x.IsConnected)) ConnectedBluetoothDevices.Add(b);
            OnPropertyChanged(nameof(NoConnectedBluetooth));
            OnPropertyChanged(nameof(VisibleBluetoothDevices));
            OnPropertyChanged(nameof(RecentQuickDevices));
            OnPropertyChanged(nameof(ShowAllButtonVisibility));

            RefreshSessionsOnly();
            StatusText = $"已刷新 · {RenderDevices.Count} 个输出 · {CaptureDevices.Count} 个输入 · {BluetoothDevices.Count} 个蓝牙设备";

            // 连接状态变化后补一次电量读取
            _ = RefreshBatteriesAsync();
        }
        finally
        {
            _loading--;
        }
    }

    /// <summary>把 AudioService 设备集合同步到 UI 集合 + 默认选中</summary>
    private void RefreshDeviceCollections()
    {
        RenderDevices.Clear();
        foreach (var d in Audio.RenderDevices) RenderDevices.Add(d);
        CaptureDevices.Clear();
        foreach (var d in Audio.CaptureDevices) CaptureDevices.Add(d);

        SelectedDefaultOutput = RenderDevices.FirstOrDefault(d => d.IsDefault);
        SelectedDefaultInput = CaptureDevices.FirstOrDefault(d => d.IsDefault);
    }

    /// <summary>蓝牙设备显示集合：默认前 3 个（最近优先），展开后全部</summary>
    public System.Collections.Generic.IEnumerable<BluetoothDeviceItem> VisibleBluetoothDevices
    {
        get
        {
            if (ShowAllBluetooth) return BluetoothDevices;
            int n = Math.Max(1, Settings.RecentDevicesCount);
            var recent = Bluetooth.GetRecentDevices(n);
            var recentKeys = recent.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // 最近 n 个优先，其余按名称补足到 n 个
            var list = recent.ToList();
            foreach (var b in BluetoothDevices.Where(x => !recentKeys.Contains(x.Key)).OrderBy(x => x.Name))
            {
                if (list.Count >= n) break;
                list.Add(b);
            }
            return list;
        }
    }

    /// <summary>最近使用设备快捷切换区（功能区顶部大按钮，点击即连）</summary>
    public System.Collections.Generic.IEnumerable<BluetoothDeviceItem> RecentQuickDevices
    {
        get
        {
            int n = Math.Max(1, Settings.RecentDevicesCount);
            var recent = Bluetooth.GetRecentDevices(n);
            if (recent.Count > 0) return recent;
            // 无连接历史时：已连接设备优先，其余补足
            var list = BluetoothDevices.Where(x => x.IsConnected).ToList();
            foreach (var b in BluetoothDevices.Where(x => !x.IsConnected).OrderBy(x => x.Name))
            {
                if (list.Count >= n) break;
                list.Add(b);
            }
            return list;
        }
    }

    /// <summary>只刷新应用会话（设备列表不变）</summary>
    public void RefreshSessionsOnly()
    {
        Audio.RefreshSessions();
        Sessions.Clear();
        foreach (var s in Audio.Sessions)
        {
            ApplyRememberedVolume(s);
            Sessions.Add(s);
        }
        OnPropertyChanged(nameof(NoSessions));
    }

    private void ApplyRememberedVolume(AppSessionItem s)
    {
        var vol = Settings.GetVolume(s.ProcessName);
        if (vol is float v && v >= 0 && v <= 1)
        {
            s.Volume = v;
        }
    }

    /// <summary>切换蓝牙设备连接</summary>
    public async Task ToggleBluetoothAsync(BluetoothDeviceItem item)
    {
        if (!item.HasKsControl)
        {
            StatusText = $"「{item.Name}」由系统（Intel 智音）管理，请在 Windows 蓝牙设置中操作";
            return;
        }
        await Bluetooth.SetConnectedAsync(item, !item.IsConnected);
        RefreshAll();
        OnPropertyChanged(nameof(RecentQuickDevices));
    }

    // ---------- 蓝牙电量 ----------

    private async Task RefreshBatteriesAsync()
    {
        await Bluetooth.RefreshBatteriesAsync(OnLowBattery);
    }

    private void OnLowBattery(string mac, string name, int percent)
        => LowBatteryNotify?.Invoke(mac, name, percent);

    // ---------- BLE 扫描 / 配对 ----------

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            if (Set(ref _isScanning, value))
                OnPropertyChanged(nameof(ScanButtonText));
        }
    }

    private string _scanStatusText = "";
    public string ScanStatusText
    {
        get => _scanStatusText;
        set => Set(ref _scanStatusText, value);
    }

    public string ScanButtonText => IsScanning ? "停止扫描" : "扫描新设备";

    public void ToggleScan()
    {
        if (IsScanning)
        {
            Bluetooth.StopScan();
            IsScanning = false;
            ScanStatusText = $"已停止 · 共发现 {ScanResults.Count} 个设备";
        }
        else
        {
            ScanResults.Clear();
            Bluetooth.ClearScanResults();
            Bluetooth.StartScan();
            IsScanning = true;
            ScanStatusText = "扫描中… 让设备进入配对模式";
        }
    }

    public async Task PairScanDeviceAsync(ScanDeviceItem item)
    {
        bool ok = await Bluetooth.PairAsync(item);
        StatusText = ok ? $"「{item.Name}」配对成功，可在蓝牙设备列表连接" : $"「{item.Name}」配对失败，请靠近设备重试";
    }

    /// <summary>为应用设置输出设备（null 或空 = 跟随默认）</summary>
    public void ApplySessionOutput(AppSessionItem s, string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            Audio.ClearProcessDefaultEndpointQuiet(s.ProcessId, Models.EDataFlowKind.Render);
            s.PersistedOutputDeviceId = null;
        }
        else
        {
            if (!Audio.SetSessionOutput(s, deviceId))
                return;
        }
        s.RefreshFollowState();
    }

    /// <summary>为应用设置输入设备（null 或空 = 跟随默认）</summary>
    public void ApplySessionInput(AppSessionItem s, string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            Audio.ClearProcessDefaultEndpointQuiet(s.ProcessId, Models.EDataFlowKind.Capture);
            s.PersistedInputDeviceId = null;
        }
        else
        {
            if (!Audio.SetSessionInput(s, deviceId))
                return;
        }
        s.RefreshFollowState();
    }

    /// <summary>应用恢复为跟随全局默认</summary>
    public void ResetSession(AppSessionItem s)
    {
        Audio.ResetSessionToDefault(s);
        s.RefreshFollowState();
    }

    /// <summary>音量滑块变化（节流保存记忆）</summary>
    private System.Threading.Timer? _volumeSaveTimer;

    public void SessionVolumeChanged(AppSessionItem s, float volume)
    {
        Audio.SetSessionVolume(s, volume);
        _volumeSaveTimer?.Dispose();
        _volumeSaveTimer = new System.Threading.Timer(_ =>
        {
            Settings.SetVolume(s.ProcessName, s.Volume);
        }, null, 500, System.Threading.Timeout.Infinite);
    }

    public void SessionMuteChanged(AppSessionItem s, bool muted)
        => Audio.SetSessionMute(s, muted);
}
