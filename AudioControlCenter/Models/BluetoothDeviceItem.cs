using AudioControlCenter.Interop;
using AudioControlCenter.Services;

namespace AudioControlCenter.Models;

/// <summary>蓝牙设备（UI 模型，包装 KS 枚举结果）</summary>
public sealed class BluetoothDeviceItem : ObservableObject
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set => Set(ref _isConnected, value);
    }

    private bool _hasKsControl;
    /// <summary>是否有可一键控制的 KS 过滤器（Intel 智音已连接设备可能没有）</summary>
    public bool HasKsControl
    {
        get => _hasKsControl;
        set => Set(ref _hasKsControl, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => Set(ref _isBusy, value);
    }

    /// <summary>用于刷新连接状态的音频服务（由 ViewModel 注入）</summary>
    public AudioService? OwnerAudio { get; set; }

    public BtAudioDevice Source { get; init; } = new();

    public string ConnectButtonText => IsConnected ? "断开" : "连接";
    public string StatusText => IsConnected ? "已连接" : (HasKsControl ? "未连接" : "系统管理");
    public bool CanToggle => HasKsControl && !IsBusy;

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanToggle));
    }
}
