using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AudioControlCenter.Models;

namespace AudioControlCenter.Services;

/// <summary>控制中心视图模型：聚合音频 + 蓝牙 + 记忆服务</summary>
public sealed class ControlCenterViewModel : ObservableObject
{
    public AudioService Audio { get; }
    public BluetoothService Bluetooth { get; }
    public SettingsStore Settings { get; }

    public ObservableCollection<AppSessionItem> Sessions { get; } = new();
    public ObservableCollection<BluetoothDeviceItem> BluetoothDevices { get; } = new();
    public ObservableCollection<AudioDeviceItem> RenderDevices { get; } = new();
    public ObservableCollection<AudioDeviceItem> CaptureDevices { get; } = new();

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

    private bool _isRefreshing;
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set => Set(ref _isRefreshing, value);
    }

    private int _loading; // 加载期间禁止触发 setter 副作用

    public ControlCenterViewModel()
    {
        Audio = new AudioService();
        Bluetooth = new BluetoothService();
        Settings = new SettingsStore();
        Settings.Load();
    }

    /// <summary>全量刷新（设备 + 蓝牙 + 会话）</summary>
    public void RefreshAll()
    {
        _loading++;
        try
        {
            Audio.RefreshDevices();
            Bluetooth.Refresh();

            RenderDevices.Clear();
            foreach (var d in Audio.RenderDevices) RenderDevices.Add(d);
            CaptureDevices.Clear();
            foreach (var d in Audio.CaptureDevices) CaptureDevices.Add(d);

            SelectedDefaultOutput = RenderDevices.FirstOrDefault(d => d.IsDefault);
            SelectedDefaultInput = CaptureDevices.FirstOrDefault(d => d.IsDefault);

            BluetoothDevices.Clear();
            foreach (var b in Bluetooth.Devices) BluetoothDevices.Add(b);

            RefreshSessionsOnly();
            StatusText = $"已刷新 · {RenderDevices.Count} 个输出 · {CaptureDevices.Count} 个输入 · {BluetoothDevices.Count} 个蓝牙设备";
        }
        finally
        {
            _loading--;
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
        await Bluetooth.SetConnectedAsync(item, !item.IsConnected);
        BluetoothDevices.Clear();
        foreach (var b in Bluetooth.Devices) BluetoothDevices.Add(b);
        // 蓝牙状态变化后音频设备也会变，刷新一下
        RefreshAll();
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
