using AudioControlCenter.Interop;

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

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => Set(ref _isBusy, value);
    }

    public BtAudioDevice Source { get; init; } = new();

    public string ConnectButtonText => IsConnected ? "断开" : "连接";
    public string StatusText => IsConnected ? "已连接" : "未连接";

    public void RefreshStatus()
    {
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(StatusText));
    }
}
