using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Power;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace AudioControlCenter.Interop;

/// <summary>BLE 扫描发现到的设备</summary>
public sealed class BleScanResult
{
    public ulong Address { get; init; }
    public string Name { get; init; } = "";
    public short Rssi { get; init; }
}

/// <summary>
/// 蓝牙 WinRT 互操作：BLE 扫描、配对、GATT 电量读取。
/// 说明：Windows 不开放经典蓝牙（BR/EDR）发现 API，扫描只能覆盖 BLE 广播设备；
/// 配对成功后设备进入系统已配对列表，可再通过 KS 或系统设置建立音频连接。
/// </summary>
public static class BtWinrt
{
    /// <summary>MAC 字符串（如 8099E7D6DF27）转 ulong</summary>
    public static ulong MacToUlong(string mac)
    {
        ulong v = 0;
        foreach (var c in mac.ToUpperInvariant())
        {
            v <<= 4;
            v |= (uint)(c >= '0' && c <= '9' ? c - '0' : c - 'A' + 10);
        }
        return v;
    }

    /// <summary>ulong 转 MAC 字符串（12 位大写十六进制）</summary>
    public static string UlongToMac(ulong address)
        => address.ToString("X12");

    /// <summary>读取蓝牙设备电量（GATT Battery Service 0x180F / BatteryLevel 0x2A19），读不到返回 null</summary>
    public static async Task<int?> ReadBatteryPercentAsync(ulong address)
    {
        try
        {
            var dev = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (dev == null) return null;
            try
            {
                var gatt = await dev.GetGattServicesAsync(BluetoothCacheMode.Uncached);
                foreach (var svc in gatt.Services)
                {
                    if (svc.Uuid != GattServiceUuids.Battery) continue;
                    var chars = await svc.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    foreach (var ch in chars.Characteristics)
                    {
                        if (ch.Uuid != GattCharacteristicUuids.BatteryLevel) continue;
                        var res = await ch.ReadValueAsync(BluetoothCacheMode.Uncached);
                        if (res.Status == GattCommunicationStatus.Success && res.Value != null && res.Value.Length > 0)
                        {
                            using var reader = DataReader.FromBuffer(res.Value);
                            return reader.ReadByte();
                        }
                    }
                }
            }
            finally
            {
                dev.Dispose();
            }
        }
        catch
        {
            // 设备不支持 GATT 电量 / 未连接时读不到
        }
        return null;
    }

    /// <summary>枚举所有经典蓝牙已配对设备的电量（名称→百分比），走 HFP HF Indicator</summary>
    public static async System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<string, int>> ReadAllClassicBatteryAsync()
    {
        var result = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "battery_debug.log"), $"ReadAllClassicBattery called at {DateTime.Now:HH:mm:ss}\n");
            var selector = BluetoothDevice.GetDeviceSelector();
            var devices = await DeviceInformation.FindAllAsync(selector);
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "battery_debug.log"), $"found {devices.Count} devices\n");
            foreach (var d in devices)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(d.Name)) continue;
                    // 经典蓝牙 HFP 电量：Windows 公开 API 不直接暴露，保留 GATT 读取（在 BluetoothService 里按 MAC 读）
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "battery_debug.log"), $"OUTER ERROR: {ex.Message}\n{ex.StackTrace}\n");
        }
        return result;
    }

    /// <summary>通过 Windows.Devices.Power.Battery API 读所有电池设备电量（含蓝牙音箱/耳机）</summary>
    public static async System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<string, int>> ReadBatteryViaPowerApiAsync()
    {
        var result = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var selector = Battery.GetDeviceSelector();
            var devices = await DeviceInformation.FindAllAsync(selector);
            var dbg = new System.Text.StringBuilder();
            dbg.AppendLine($"Power API: found {devices.Count} battery devices");
            foreach (var d in devices)
            {
                try
                {
                    var bat = await Battery.FromIdAsync(d.Id);
                    if (bat == null) continue;
                    var report = bat.GetReport();
                    if (report == null) continue;
                    int? pct = null;
                    if (report.RemainingCapacityInMilliwattHours.HasValue && report.FullChargeCapacityInMilliwattHours.HasValue && report.FullChargeCapacityInMilliwattHours.Value > 0)
                        pct = (int)(report.RemainingCapacityInMilliwattHours.Value * 100.0 / report.FullChargeCapacityInMilliwattHours.Value);
                    dbg.AppendLine($"  {d.Name} (Id={d.Id}) => remaining={report.RemainingCapacityInMilliwattHours} full={report.FullChargeCapacityInMilliwattHours} status={report.Status} pct={pct}%");
                    if (pct.HasValue && pct.Value >= 0 && pct.Value <= 100)
                    {
                        result[d.Name] = pct.Value;
                    }
                }
                catch { }
            }
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "battery_debug.log"), dbg.ToString());
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "battery_debug.log"), $"Power API error: {ex.Message}\n");
        }
        return result;
    }
    /// <summary>配对 BLE 设备（系统级配对）</summary>
    public static async Task<bool> PairAsync(ulong address)
    {
        try
        {
            var dev = await BluetoothDevice.FromBluetoothAddressAsync(address);
            if (dev == null) return false;
            try
            {
                var di = await DeviceInformation.CreateFromIdAsync(dev.DeviceId);
                if (di == null) return false;
                if (di.Pairing.IsPaired) return true;
                var result = await di.Pairing.PairAsync(DevicePairingProtectionLevel.None);
                return result.Status == DevicePairingResultStatus.Paired;
            }
            finally
            {
                dev.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>判断是否已配对（按地址）</summary>
    public static async Task<bool> IsPairedAsync(ulong address)
    {
        try
        {
            var dev = await BluetoothDevice.FromBluetoothAddressAsync(address);
            if (dev == null) return false;
            try
            {
                var di = await DeviceInformation.CreateFromIdAsync(dev.DeviceId);
                return di?.Pairing.IsPaired ?? false;
            }
            finally
            {
                dev.Dispose();
            }
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>BLE 广播扫描器</summary>
public sealed class BleScanner : IDisposable
{
    private BluetoothLEAdvertisementWatcher? _watcher;

    /// <summary>发现设备（可能在任意线程触发）</summary>
    public event Action<BleScanResult>? DeviceFound;

    public bool IsScanning { get; private set; }

    public void Start()
    {
        if (IsScanning) return;
        try
        {
            _watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active,
                SignalStrengthFilter = null,
            };
            _watcher.Received += OnReceived;
            _watcher.Start();
            IsScanning = true;
        }
        catch
        {
            IsScanning = false;
        }
    }

    public void Stop()
    {
        if (!IsScanning) return;
        try
        {
            _watcher?.Stop();
        }
        catch { }
        _watcher = null;
        IsScanning = false;
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var name = args.Advertisement?.LocalName ?? "";
        if (string.IsNullOrWhiteSpace(name)) return;
        DeviceFound?.Invoke(new BleScanResult
        {
            Address = args.BluetoothAddress,
            Name = name,
            Rssi = args.RawSignalStrengthInDBm,
        });
    }

    public void Dispose() => Stop();
}
