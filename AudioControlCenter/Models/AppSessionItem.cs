using BTAudioSwitcher.Services;

namespace BTAudioSwitcher.Models;

/// <summary>应用音频会话（系统音量合成器中的一条）</summary>
public sealed class AppSessionItem : ObservableObject
{
    public uint ProcessId { get; init; }
    public string ProcessName { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string? IconPath { get; init; }

    /// <summary>当前输出设备（该会话所属音频端点）</summary>
    public string CurrentOutputDeviceId { get; set; } = "";
    public string CurrentOutputDeviceName { get; set; } = "";

    /// <summary>当前输入设备（该会话所属音频端点）</summary>
    public string CurrentInputDeviceId { get; set; } = "";
    public string CurrentInputDeviceName { get; set; } = "";

    /// <summary>系统持久化的输出设备覆盖（未设置 = 跟随默认）</summary>
    public string? PersistedOutputDeviceId { get; set; }

    /// <summary>系统持久化的输入设备覆盖</summary>
    public string? PersistedInputDeviceId { get; set; }

    private float _volume = 1f;
    public float Volume
    {
        get => _volume;
        set => Set(ref _volume, value);
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set => Set(ref _isMuted, value);
    }

    /// <summary>UI 展示是否跟随默认设备</summary>
    public string FollowStateText =>
        (PersistedOutputDeviceId == null && PersistedInputDeviceId == null)
            ? "跟随默认"
            : "已自定义";

    public string OutputOverrideText =>
        PersistedOutputDeviceId == null ? "跟随默认" : CurrentOutputDeviceName;

    public string InputOverrideText =>
        PersistedInputDeviceId == null ? "跟随默认" : CurrentInputDeviceName;

    public void RefreshFollowState()
    {
        OnPropertyChanged(nameof(FollowStateText));
        OnPropertyChanged(nameof(OutputOverrideText));
        OnPropertyChanged(nameof(InputOverrideText));
    }
}
