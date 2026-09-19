using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AudioControlCenter.Interop;
using AudioControlCenter.Models;

namespace AudioControlCenter.Services;

/// <summary>
/// 蓝牙服务：枚举蓝牙音频设备、连接/断开（套壳 ToothTray / yasb 的 KS 方案）。
/// </summary>
public sealed class BluetoothService
{
    private readonly Dictionary<string, BluetoothDeviceItem> _items = new(StringComparer.OrdinalIgnoreCase);

    public event Action? DevicesChanged;

    public IReadOnlyList<BluetoothDeviceItem> Devices => _items.Values.ToList();

    /// <summary>刷新蓝牙设备列表（连接状态由音频端点状态推断）</summary>
    public void Refresh(AudioService? audio = null)
    {
        var devices = BtAudioKs.EnumerateDevices();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dev in devices)
        {
            // 连接状态：任一音频端点名包含设备名且为 Active
            dev.IsConnected = audio != null && IsConnectedByName(audio, dev.Name);

            seen.Add(dev.Key);
            if (_items.TryGetValue(dev.Key, out var item))
            {
                item.IsConnected = dev.IsConnected;
                item.HasKsControl = dev.HasKsControl;
                item.RefreshStatus();
            }
            else
            {
                _items[dev.Key] = new BluetoothDeviceItem
                {
                    Key = dev.Key,
                    Name = dev.Name,
                    IsConnected = dev.IsConnected,
                    HasKsControl = dev.HasKsControl,
                    Source = dev,
                };
            }
        }

        // 移除已消失的设备
        var removed = _items.Keys.Where(k => !seen.Contains(k)).ToList();
        foreach (var k in removed)
            _items.Remove(k);

        DevicesChanged?.Invoke();
    }

    private static bool IsConnectedByName(AudioService audio, string btName)
    {
        if (string.IsNullOrWhiteSpace(btName)) return false;
        return audio.RenderDevices.Any(d => d.IsPresent && d.Name.Contains(btName, StringComparison.OrdinalIgnoreCase))
            || audio.CaptureDevices.Any(d => d.IsPresent && d.Name.Contains(btName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>连接或断开蓝牙设备，完成后刷新状态</summary>
    public async Task<bool> SetConnectedAsync(BluetoothDeviceItem item, bool connect)
    {
        if (item.IsBusy) return false;
        item.IsBusy = true;

        try
        {
            bool ok = await Task.Run(() => BtAudioKs.SetConnected(item.Source, connect));
            // 稍等让系统完成蓝牙连接/断开
            await Task.Delay(connect ? 1200 : 400);
            if (item.OwnerAudio != null)
                Refresh(item.OwnerAudio);
            else
                Refresh();
            return ok;
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}
