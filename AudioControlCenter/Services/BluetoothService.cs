using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AudioControlCenter.Interop;
using AudioControlCenter.Models;

namespace AudioControlCenter.Services;

/// <summary>
/// 蓝牙服务：枚举蓝牙音频设备、连接/断开（套壳 ToothTray / yasb 的 KS 方案）、
/// 电量读取（GATT Battery）、BLE 扫描配对、最近设备跟踪。
/// </summary>
public sealed class BluetoothService : IDisposable
{
    private readonly Dictionary<string, BluetoothDeviceItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly BleScanner _scanner = new();
    private readonly Dictionary<ulong, ScanDeviceItem> _scanResults = new();

    public event Action? DevicesChanged;
    public event Action? ScanResultsChanged;

    public IReadOnlyList<BluetoothDeviceItem> Devices => _items.Values.ToList();
    public IReadOnlyList<ScanDeviceItem> ScanResults => _scanResults.Values.OrderByDescending(s => s.Rssi).ToList();
    public bool IsScanning => _scanner.IsScanning;

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
            if (ok && connect)
                Settings?.TouchBluetooth(item.Key);
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

    /// <summary>记忆存储（注入用于最近设备排序与低电量去重）</summary>
    public SettingsStore? Settings { get; set; }

    /// <summary>最近连接的蓝牙设备（按最后连接时间倒序，取 count 个）</summary>
    public List<BluetoothDeviceItem> GetRecentDevices(int count)
    {
        if (Settings == null) return new List<BluetoothDeviceItem>();
        return _items.Values
            .Where(d => Settings.GetBluetoothLastConnected(d.Key) > 0)
            .OrderByDescending(d => Settings.GetBluetoothLastConnected(d.Key))
            .ThenBy(d => d.Name)
            .Take(count)
            .ToList();
    }

    // ---------- 电量 ----------

    private bool _refreshingBattery;

    /// <summary>异步刷新全部设备电量；低电量时回调 (mac, name, percent)（仅首次进入低电量区间时）</summary>
    public async Task RefreshBatteriesAsync(Action<string, string, int>? lowBatteryNotifier = null)
    {
        if (_refreshingBattery) return;
        _refreshingBattery = true;
        try
        {
            foreach (var item in _items.Values.ToList())
            {
                var mac = item.Key;
                int? pct;
                try
                {
                    pct = await BtWinrt.ReadBatteryPercentAsync(BtWinrt.MacToUlong(mac));
                }
                catch
                {
                    pct = null;
                }
                item.BatteryPercent = pct;

                if (pct is int p && p <= 20 && Settings != null && !Settings.IsLowBatteryNotified(mac, p))
                {
                    Settings.MarkLowBatteryNotified(mac, p);
                    lowBatteryNotifier?.Invoke(mac, item.Name, p);
                }
            }
        }
        finally
        {
            _refreshingBattery = false;
        }
    }

    // ---------- BLE 扫描 / 配对 ----------

    public void StartScan()
    {
        _scanner.DeviceFound -= OnDeviceFound;
        _scanner.DeviceFound += OnDeviceFound;
        _scanner.Start();
        ScanResultsChanged?.Invoke();
    }

    public void StopScan()
    {
        _scanner.DeviceFound -= OnDeviceFound;
        _scanner.Stop();
        ScanResultsChanged?.Invoke();
    }

    private void OnDeviceFound(BleScanResult result)
    {
        // 回调可能在任意线程，UI 层需调度
        var ui = System.Windows.Application.Current?.Dispatcher;
        if (ui == null || ui.CheckAccess())
            MergeScanResult(result);
        else
            ui.BeginInvoke(() => MergeScanResult(result));
    }

    private void MergeScanResult(BleScanResult result)
    {
        if (!_scanResults.TryGetValue(result.Address, out var item))
        {
            _scanResults[result.Address] = new ScanDeviceItem
            {
                Address = result.Address,
                Name = result.Name,
                Rssi = result.Rssi,
            };
        }
        ScanResultsChanged?.Invoke();
    }

    /// <summary>配对扫描到的设备；完成后更新状态</summary>
    public async Task<bool> PairAsync(ScanDeviceItem item)
    {
        if (item.IsPairing) return false;
        item.IsPairing = true;
        try
        {
            bool paired = await BtWinrt.PairAsync(item.Address);
            item.IsPaired = paired;
            item.RefreshPairState();
            return paired;
        }
        finally
        {
            item.IsPairing = false;
        }
    }

    /// <summary>清理扫描结果</summary>
    public void ClearScanResults()
    {
        _scanResults.Clear();
        ScanResultsChanged?.Invoke();
    }

    public void Dispose()
    {
        _scanner.Dispose();
    }
}
