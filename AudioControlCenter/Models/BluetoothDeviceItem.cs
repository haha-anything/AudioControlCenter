using BTAudioSwitcher.Interop;
using BTAudioSwitcher.Services;

namespace BTAudioSwitcher.Models;

/// <summary>蓝牙设备（UI 模型，包装 KS 枚举结果）</summary>
public sealed class BluetoothDeviceItem : ObservableObject
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>品牌徽章（根据名称自动匹配，可被用户备注覆盖）</summary>
    public DeviceBrandHelper.BrandInfo Brand
    {
        get
        {
            if (!string.IsNullOrEmpty(UserBrand))
                return DeviceBrandHelper.FromUserBrand(UserBrand);
            return DeviceBrandHelper.Guess(Name);
        }
    }

    public string BrandInitial => DeviceBrandHelper.Initial(Brand.DisplayName, Brand.Key);
    public string BrandColor => Brand.AccentHex;
    public string BrandIcon => Brand.Icon;

    /// <summary>用户手动备注的品牌（覆盖自动匹配）</summary>
    private string? _userBrand;
    public string? UserBrand
    {
        get => _userBrand;
        set
        {
            if (Set(ref _userBrand, value))
            {
                OnPropertyChanged(nameof(Brand));
                OnPropertyChanged(nameof(BrandInitial));
                OnPropertyChanged(nameof(BrandColor));
                OnPropertyChanged(nameof(BrandIcon));
            }
        }
    }

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

    private int? _batteryPercent;
    /// <summary>电池电量 0-100；null = 读不到</summary>
    public int? BatteryPercent
    {
        get => _batteryPercent;
        set
        {
            if (Set(ref _batteryPercent, value))
            {
                OnPropertyChanged(nameof(BatteryText));
                OnPropertyChanged(nameof(HasBattery));
                OnPropertyChanged(nameof(IsLowBattery));
            }
        }
    }

    public bool HasBattery => BatteryPercent.HasValue;
    public string BatteryText => BatteryPercent.HasValue ? $"电量 {BatteryPercent}%" : "";
    public bool IsLowBattery => BatteryPercent is <= 20;

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

/// <summary>BLE 扫描发现的设备（UI 模型）</summary>
public sealed class ScanDeviceItem : ObservableObject
{
    public ulong Address { get; init; }
    public string Name { get; init; } = "";

    /// <summary>品牌徽章（根据名称自动匹配）</summary>
    public DeviceBrandHelper.BrandInfo Brand => DeviceBrandHelper.Guess(Name);
    public string BrandInitial => DeviceBrandHelper.Initial(Brand.DisplayName, Brand.Key);
    public string BrandColor => Brand.AccentHex;
    public string BrandIcon => Brand.Icon;

    public short Rssi { get; init; }

    private bool _isPairing;
    public bool IsPairing
    {
        get => _isPairing;
        set => Set(ref _isPairing, value);
    }

    private bool _isPaired;
    public bool IsPaired
    {
        get => _isPaired;
        set => Set(ref _isPaired, value);
    }

    public string PairButtonText => IsPaired ? "已配对" : "配对";
    public string MacText => Interop.BtWinrt.UlongToMac(Address);

    public void RefreshPairState() => OnPropertyChanged(nameof(PairButtonText));
}
