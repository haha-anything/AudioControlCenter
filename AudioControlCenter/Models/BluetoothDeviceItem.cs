using AudioControlCenter.Interop;
using AudioControlCenter.Services;

namespace AudioControlCenter.Models;

/// <summary>蓝牙设备（UI 模型，包装 KS 枚举结果）</summary>
public sealed class BluetoothDeviceItem : ObservableObject
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>品牌图标（根据名称自动匹配，可被用户备注覆盖）</summary>
    public string BrandIcon => GuessBrandIcon(Name);

    /// <summary>用户手动备注的品牌（覆盖自动匹配）</summary>
    public string? UserBrand { get; set; }

    /// <summary>根据设备名猜测品牌图标</summary>
    public static string GuessBrandIcon(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "📻";
        var n = name.ToLowerInvariant();
        if (n.Contains("srs") || n.Contains("gtk") || n.Contains("gtk")) return "🔊";           // 索尼音箱
        if (n.Contains("wf-") || n.Contains("wh-") || n.Contains("wi-") || n.Contains("mdr-") || n.Contains("sony")) return "🎧"; // 索尼耳机
        if (n.Contains("airpods") || n.Contains("iphone") || n.Contains("ipad") || n.Contains("find my") || n.Contains("bang")) return "🎧"; // 苹果
        if (n.Contains("galaxy") || n.Contains("buds")) return "🎧";                                     // 三星
        if (n.Contains("bose") || n.Contains("qc35") || n.Contains("qc45")) return "🎧";                  // Bose
        if (n.Contains("jbl")) return "🔊";                                                             // JBL
        if (n.Contains("dualsense") || n.Contains("dualshock") || n.Contains("xbox") || n.Contains("controller")) return "🎮"; // 手柄
        if (n.Contains("mouse") || n.Contains("鼠标")) return "🖱️";
        if (n.Contains("keyboard") || n.Contains("键盘")) return "⌨️";
        return "📻";
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

    /// <summary>品牌图标（根据名称自动匹配，可被用户备注覆盖）</summary>
    public string BrandIcon => GuessBrandIcon(Name);

    /// <summary>用户手动备注的品牌（覆盖自动匹配）</summary>
    public string? UserBrand { get; set; }

    /// <summary>根据设备名猜测品牌图标</summary>
    public static string GuessBrandIcon(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "📻";
        var n = name.ToLowerInvariant();
        if (n.Contains("srs") || n.Contains("gtk") || n.Contains("gtk")) return "🔊";           // 索尼音箱
        if (n.Contains("wf-") || n.Contains("wh-") || n.Contains("wi-") || n.Contains("mdr-") || n.Contains("sony")) return "🎧"; // 索尼耳机
        if (n.Contains("airpods") || n.Contains("iphone") || n.Contains("ipad") || n.Contains("find my") || n.Contains("bang")) return "🎧"; // 苹果
        if (n.Contains("galaxy") || n.Contains("buds")) return "🎧";                                     // 三星
        if (n.Contains("bose") || n.Contains("qc35") || n.Contains("qc45")) return "🎧";                  // Bose
        if (n.Contains("jbl")) return "🔊";                                                             // JBL
        if (n.Contains("dualsense") || n.Contains("dualshock") || n.Contains("xbox") || n.Contains("controller")) return "🎮"; // 手柄
        if (n.Contains("mouse") || n.Contains("鼠标")) return "🖱️";
        if (n.Contains("keyboard") || n.Contains("键盘")) return "⌨️";
        return "📻";
    }
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
