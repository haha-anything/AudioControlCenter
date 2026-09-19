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

    public void SetVolume(string processName, float volume)
    {
        _data.VolumeByApp[processName] = volume;
        Save();
    }
}
