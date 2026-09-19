using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AudioControlCenter.Services;

/// <summary>
/// 本地记忆存储（JSON）。记录每个应用的自定义音量等偏好，
/// 设备覆盖由 Windows 系统自身持久化（SetPersistedDefaultAudioEndpoint），此处只存音量。
/// </summary>
public sealed class SettingsStore
{
    private readonly string _path;
    private AppSettings _data = new();

    public sealed class AppSettings
    {
        /// <summary>进程名（小写）→ 应用音量 0-1（null 表示未自定义）</summary>
        public Dictionary<string, float> VolumeByApp { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>蓝牙 MAC → 最近连接时间（Ticks）；用于"最近设备"排序</summary>
        public Dictionary<string, long> BluetoothLastConnected { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>蓝牙 MAC → 已通知低电量值（避免重复提醒）</summary>
        public Dictionary<string, int> BatteryLowNotified { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>主题：0=跟随系统 1=深色 2=浅色</summary>
        public int ThemeMode { get; set; } = 0;

        /// <summary>开机自启</summary>
        public bool AutoStart { get; set; } = false;

        /// <summary>低电量提醒</summary>
        public bool LowBatteryAlert { get; set; } = true;
    }

    public SettingsStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioControlCenter", "settings.json");
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                _data = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            _data = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public float? GetVolume(string processName)
        => _data.VolumeByApp.TryGetValue(processName, out var v) ? v : null;

    /// <summary>主题：0=跟随系统 1=深色 2=浅色</summary>
    public int ThemeMode
    {
        get => _data.ThemeMode;
        set { _data.ThemeMode = value; Save(); }
    }

    public bool AutoStart
    {
        get => _data.AutoStart;
        set { _data.AutoStart = value; Save(); ApplyAutoStart(); }
    }

    public bool LowBatteryAlert
    {
        get => _data.LowBatteryAlert;
        set { _data.LowBatteryAlert = value; Save(); }
    }

    /// <summary>写开机自启注册表</summary>
    private void ApplyAutoStart()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            const string name = "AudioControlCenter";
            if (_data.AutoStart)
                key.SetValue(name, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(name, false);
        }
        catch { }
    }

    public void SetVolume(string processName, float volume)
    {
        _data.VolumeByApp[processName] = volume;
        Save();
    }

    /// <summary>记录蓝牙设备最近连接时间</summary>
    public void TouchBluetooth(string mac)
    {
        _data.BluetoothLastConnected[mac] = DateTime.UtcNow.Ticks;
        Save();
    }

    public long GetBluetoothLastConnected(string mac)
        => _data.BluetoothLastConnected.TryGetValue(mac, out var t) ? t : 0;

    /// <summary>该电量是否已通知过低电量</summary>
    public bool IsLowBatteryNotified(string mac, int percent)
        => _data.BatteryLowNotified.TryGetValue(mac, out var p) && p <= percent;

    public void MarkLowBatteryNotified(string mac, int percent)
    {
        _data.BatteryLowNotified[mac] = percent;
        Save();
    }
}
