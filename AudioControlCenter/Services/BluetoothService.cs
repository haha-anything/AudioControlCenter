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

    /// <summary>刷新蓝牙设备列表（连接状态一并更新）</summary>
    public void Refresh()
    {
        var devices = BtAudioKs.EnumerateDevices();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dev in devices)
        {
            seen.Add(dev.Key);
            if (_items.TryGetValue(dev.Key, out var item))
            {
                item.IsConnected = dev.IsConnected;
                item.RefreshStatus();
            }
            else
            {
                _items[dev.Key] = new BluetoothDeviceItem
                {
                    Key = dev.Key,
                    Name = dev.Name,
                    IsConnected = dev.IsConnected,
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
            Refresh();
            return ok;
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}
