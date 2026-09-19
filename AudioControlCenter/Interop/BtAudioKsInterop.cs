using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace AudioControlCenter.Interop;

#region COM 接口定义

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorCom
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
    int RegisterEndpointNotificationCallback(IntPtr pClient);
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    int GetCount(out uint pcDevices);
    int Item(uint nDevice, out IMMDevice ppDevice);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
    int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    int GetState(out uint pdwState);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    int GetCount(out uint cProps);
    int GetAt(uint iProp, out PROPERTYKEY pkey);
    int GetValue(ref PROPERTYKEY key, out PropVariant pv);
    int SetValue(ref PROPERTYKEY key, ref PropVariant pv);
    int Commit();
}

[ComImport]
[Guid("2A07407E-6497-4A18-9787-32F79BD0D98F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDeviceTopology
{
    int GetConnectorCount(out uint pCount);
    int GetConnector(uint nIndex, out IConnector ppConnector);
    int GetSubunitCount(out uint pCount);
    int GetSubunit(uint nIndex, out IntPtr ppSubunit);
    int GetPartById(uint nId, out IntPtr ppPart);
    int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrDeviceId);
    int GetSignalPath(IntPtr pStart, IntPtr pEnd, int bRejectMixedPaths, out IntPtr ppSignalPath);
}

[ComImport]
[Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IConnector
{
    int GetType(out uint pType);
    int GetDataFlow(out EDataFlow pFlow);
    int ConnectTo(IConnector pConnectTo);
    int Disconnect();
    int IsConnected(out int pbConnected);
    int GetConnectedTo(out IConnector ppConTo);
    int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string ppConId);
    int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string ppDeviceId);
}

[ComImport]
[Guid("28F54685-06FD-11D2-B27A-00A0C9223196")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IKsControl
{
    int KsProperty(ref KSPROPERTY property, uint propertyLength, IntPtr propertyData, uint dataLength, out uint bytesReturned);
    int KsMethod(ref KSMETHOD method, uint methodLength, IntPtr methodData, uint dataLength, out uint bytesReturned);
    int KsEvent(ref KSEVENT ksEvent, uint eventLength, IntPtr eventData, uint dataLength, out uint bytesReturned);
}

[StructLayout(LayoutKind.Sequential)]
internal struct KSPROPERTY
{
    public Guid Set;
    public uint Id;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KSMETHOD
{
    public Guid Set;
    public uint Id;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KSEVENT
{
    public Guid Set;
    public uint Id;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropVariant
{
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public IntPtr p1;
    public IntPtr p2;

    public static void Clear(ref PropVariant pv)
        => Ole32Interop.PropVariantClear(ref pv);
}

internal static class Ole32Interop
{
    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, ref Guid riid, out IntPtr ppv);

    [DllImport("ole32.dll")]
    internal static extern int PropVariantClear(ref PropVariant pvar);
}

#endregion

/// <summary>
/// 蓝牙音频设备模型（由音频端点的 KS 拓扑聚合而来）。
/// </summary>
public sealed class BtAudioDevice
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";          // MAC 或端点 id（分组键）
    public bool IsConnected { get; set; }
    public List<BtAudioEndpoint> Endpoints { get; } = new();
}

public sealed class BtAudioEndpoint
{
    public string DeviceId { get; set; } = "";     // KS 端点设备 id（\\?\BTHENUM... 或 bthhfenum）
    public IntPtr KsControl { get; set; }          // IKsControl COM 指针
    public bool IsActive { get; set; }
    public string FriendlyName { get; set; } = "";
}

/// <summary>
/// 蓝牙音频连接管理：枚举蓝牙音频端点、一键连接/断开。
/// 原理移植自 ToothTray / yasb：通过音频拓扑找到蓝牙 KS 过滤器，发送
/// KSPROPSETID_BtAudio / ONESHOT_RECONNECT|DISCONNECT 属性。
/// </summary>
public static class BtAudioKs
{
    public const uint DEVICE_STATE_ACTIVE = 0x00000001;
    public const uint DEVICE_STATE_ALL = 0x0000000F;
    public const uint CLSCTX_ALL = 23;

    private static readonly Guid KSPROPSETID_BtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private const uint KSPROPERTY_ONESHOT_RECONNECT = 0;
    private const uint KSPROPERTY_ONESHOT_DISCONNECT = 1;
    private const uint KSPROPERTY_TYPE_GET = 0x00000001;

    private static readonly Guid IID_IDeviceTopology = new("2A07407E-6497-4A18-9787-32F79BD0D98F");
    private static readonly Guid IID_IConnector = new("9C2C4058-23F5-41DE-877A-DF3AF236A09E");
    private static readonly Guid IID_IKsControl = new("28F54685-06FD-11D2-B27A-00A0C9223196");

    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");

    private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14,
    };

    private static readonly Regex _macRegex = new(@"[0-9A-Fa-f]{12}", RegexOptions.Compiled);

    private static IMMDeviceEnumerator CreateEnumerator()
    {
        var clsid = CLSID_MMDeviceEnumerator;
        var iid = IID_IMMDeviceEnumerator;
        int hr = Ole32Interop.CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_ALL, ref iid, out IntPtr ptr);
        if (hr < 0)
            throw new COMException($"CoCreateInstance(MMDeviceEnumerator) failed: 0x{hr:X8}");
        return (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(ptr);
    }

    private static string? ReadFriendlyName(IPropertyStore store)
    {
        try
        {
            var key = PKEY_Device_FriendlyName;
            int hr = store.GetValue(ref key, out PropVariant pv);
            try
            {
                if (hr < 0 || pv.vt != 31 /* VT_LPWSTR */ || pv.p1 == IntPtr.Zero)
                    return null;
                return Marshal.PtrToStringUni(pv.p1);
            }
            finally
            {
                PropVariant.Clear(ref pv);
            }
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractMac(string deviceId)
    {
        var m = _macRegex.Match(deviceId);
        return m.Success ? m.Value.ToUpperInvariant() : "";
    }

    /// <summary>枚举所有蓝牙音频设备（按 MAC 聚合为物理设备）。</summary>
    public static List<BtAudioDevice> EnumerateDevices()
    {
        var result = new List<BtAudioDevice>();
        var byMac = new Dictionary<string, BtAudioDevice>(StringComparer.OrdinalIgnoreCase);
        var byId = new Dictionary<string, BtAudioDevice>(StringComparer.OrdinalIgnoreCase);

        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = CreateEnumerator();
            int hr = enumerator.EnumAudioEndpoints(EDataFlow.eAll, DEVICE_STATE_ALL, out var collection);
            if (hr < 0)
                return result;

            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                collection.Item(i, out var device);
                if (device == null) continue;

                // 端点的 KS 连接对端是否为蓝牙
                BtAudioEndpoint? ep = TryGetBtEndpoint(enumerator, device);
                if (ep == null)
                {
                    Marshal.ReleaseComObject(device);
                    continue;
                }

                var mac = ExtractMac(ep.DeviceId);
                BtAudioDevice dev;
                if (mac.Length > 0 && byMac.TryGetValue(mac, out var byMacDev))
                {
                    dev = byMacDev;
                }
                else if (mac.Length == 0 && byId.TryGetValue(ep.DeviceId, out var byIdDev))
                {
                    dev = byIdDev;
                }
                else
                {
                    dev = new BtAudioDevice
                    {
                        Key = mac.Length > 0 ? mac : ep.DeviceId,
                        Name = ep.FriendlyName,
                    };
                    result.Add(dev);
                    if (mac.Length > 0) byMac[mac] = dev; else byId[ep.DeviceId] = dev;
                }

                // 名称优先取第一个（A2DP 立体声）端点名
                if (dev.Name.Length == 0 || string.IsNullOrWhiteSpace(dev.Name))
                    dev.Name = ep.FriendlyName;

                dev.Endpoints.Add(ep);
                dev.IsConnected |= ep.IsActive;
                Marshal.ReleaseComObject(device);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"EnumerateDevices failed: {ex}");
        }
        finally
        {
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }

        // 按连接状态排序：已连接在前
        return result.OrderByDescending(d => d.IsConnected).ThenBy(d => d.Name).ToList();
    }

    /// <summary>检查一个音频端点是否连接到蓝牙 KS 过滤器，若是则返回其 KS 信息。</summary>
    private static BtAudioEndpoint? TryGetBtEndpoint(IMMDeviceEnumerator enumerator, IMMDevice device)
    {
        try
        {
            device.GetState(out uint state);
            var iid = IID_IDeviceTopology;
            int hr = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out IntPtr topoPtr);
            if (hr < 0 || topoPtr == IntPtr.Zero)
                return null;

            var topo = (IDeviceTopology)Marshal.GetObjectForIUnknown(topoPtr);
            try
            {
                topo.GetConnectorCount(out uint connCount);
                for (uint c = 0; c < connCount; c++)
                {
                    topo.GetConnector(c, out var connector);
                    if (connector == null) continue;
                    try
                    {
                        connector.GetDeviceIdConnectedTo(out string otherId);
                        if (string.IsNullOrEmpty(otherId) || !otherId.StartsWith(@"\\?\bth", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // 找到蓝牙 KS 端点
                        enumerator.GetDevice(otherId, out var ksDevice);
                        if (ksDevice == null) continue;

                        var ksIid = IID_IKsControl;
                        int ksHr = ksDevice.Activate(ref ksIid, CLSCTX_ALL, IntPtr.Zero, out IntPtr ksPtr);
                        var friendly = ReadFriendlyNameFrom(ksDevice);

                        // 连接状态取音频端点状态（蓝牙断开时端点 UNPLUGGED）
                        bool active = (state & DEVICE_STATE_ACTIVE) != 0;

                        if (ksHr >= 0 && ksPtr != IntPtr.Zero)
                        {
                            return new BtAudioEndpoint
                            {
                                DeviceId = otherId,
                                KsControl = ksPtr,
                                IsActive = active,
                                FriendlyName = friendly ?? "",
                            };
                        }
                        Marshal.ReleaseComObject(ksDevice);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(connector);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(topo);
            }
        }
        catch
        {
            // 个别端点无拓扑，跳过
        }
        return null;
    }

    private static string? ReadFriendlyNameFrom(IMMDevice device)
    {
        try
        {
            device.OpenPropertyStore(0 /* STGM_READ */, out var store);
            if (store == null) return null;
            try
            {
                return ReadFriendlyName(store);
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>连接（true）或断开（false）一个蓝牙音频设备。返回是否成功发出指令。</summary>
    public static bool SetConnected(BtAudioDevice device, bool connect)
    {
        if (device.Endpoints.Count == 0)
            return false;

        uint propId = connect ? KSPROPERTY_ONESHOT_RECONNECT : KSPROPERTY_ONESHOT_DISCONNECT;
        bool any = false;
        foreach (var ep in device.Endpoints)
        {
            if (ep.KsControl == IntPtr.Zero)
                continue;
            try
            {
                var ks = (IKsControl)Marshal.GetObjectForIUnknown(ep.KsControl);
                var prop = new KSPROPERTY { Set = KSPROPSETID_BtAudio, Id = propId, Flags = KSPROPERTY_TYPE_GET };
                int hr = ks.KsProperty(ref prop, (uint)Marshal.SizeOf<KSPROPERTY>(), IntPtr.Zero, 0, out _);
                if (hr >= 0)
                    any = true;
            }
            catch
            {
                // 单个端点失败不影响其他
            }
        }
        return any;
    }
}
