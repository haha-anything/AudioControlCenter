using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using AudioControlCenter.Models;

namespace AudioControlCenter.Services;

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

    private readonly DispatcherTimer _sessionTimer;
    private readonly DispatcherTimer _batteryTimer;

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
    /// <summary>左侧导航：0=首页 1=音频 2=蓝牙 3=设置</summary>
    public int SelectedTab
    {
        get => _selectedTab;
        set => Set(ref _selectedTab, value);
    }

    private int _loading; // 加载期间禁止触发 setter 副作用

    public ControlCenterViewModel()
    {
        Audio = new AudioService();
        Bluetooth = new BluetoothService();
        Settings = new SettingsStore();
        Settings.Load();
        Bluetooth.Settings = Settings;
        Bluetooth.DevicesChanged += OnBluetoothDevicesChanged;
        Bluetooth.ScanResultsChanged += OnScanResultsChanged;

        // 会话实时监测：2 秒一轮（新应用打开/退出自动出现/消失）
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sessionTimer.Tick += (_, _) => RefreshSessionsOnly();

        // 电量定时刷新：30 秒一轮；连接状态变化时也会补一次
        _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _batteryTimer.Tick += async (_, _) => await RefreshBatteriesAsync();

        _sessionTimer.Start();
        _batteryTimer.Start();
    }

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

            RenderDevices.Clear();
            foreach (var d in Audio.RenderDevices) RenderDevices.Add(d);
            CaptureDevices.Clear();
            foreach (var d in Audio.CaptureDevices) CaptureDevices.Add(d);

            SelectedDefaultOutput = RenderDevices.FirstOrDefault(d => d.IsDefault);
            SelectedDefaultInput = CaptureDevices.FirstOrDefault(d => d.IsDefault);

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

    /// <summary>蓝牙设备显示集合：默认前 3 个（最近优先），展开后全部</summary>
    public System.Collections.Generic.IEnumerable<BluetoothDeviceItem> VisibleBluetoothDevices
    {
        get
        {
            if (ShowAllBluetooth) return BluetoothDevices;
            var recent = Bluetooth.GetRecentDevices(3);
            var recentKeys = recent.Select(r => r.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // 最近 3 个优先，其余按名称补足到 3 个
            var list = recent.ToList();
            foreach (var b in BluetoothDevices.Where(x => !recentKeys.Contains(x.Key)).OrderBy(x => x.Name))
            {
                if (list.Count >= 3) break;
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
