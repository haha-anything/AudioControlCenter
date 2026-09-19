namespace AudioControlCenter.Models;

/// <summary>音频设备（输出/输入端点）</summary>
public sealed class AudioDeviceItem : ObservableObject
{
    public string DeviceId { get; init; } = "";
    public string Name { get; init; } = "";
    public EDataFlowKind Kind { get; init; }          // Render / Capture
    public bool IsBluetooth { get; init; }

    private bool _isDefault;
    public bool IsDefault
    {
        get => _isDefault;
        set => Set(ref _isDefault, value);
    }

    private bool _isPresent;
    public bool IsPresent
    {
        get => _isPresent;
        set => Set(ref _isPresent, value);
    }

    private string _stateText = "";
    public string StateText
    {
        get => _stateText;
        set => Set(ref _stateText, value);
    }

    public string DisplayName => IsDefault ? $"{Name}（默认）" : Name;
}

public enum EDataFlowKind
{
    Render,
    Capture,
}
