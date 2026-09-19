using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

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



/// <summary>Shell32 P/Invoke</summary>
internal static class Shell32Interop
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHLoadIndirectString(string pszSource, System.Text.StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);
}

#endregion

#region SetupAPI（枚举设备接口）

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVICE_INTERFACE_DATA
{
    public uint cbSize;
    public Guid InterfaceClassGuid;
    public uint Flags;
    public IntPtr Reserved;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct SP_DEVICE_INTERFACE_DETAIL_DATA
{
    public uint cbSize;
    public short DevicePath; // 变长字段，仅占位
}

internal static class SetupApiInterop
{
    public const uint DIGCF_DEFAULT = 0x00000001;
    public const uint DIGCF_PRESENT = 0x00000002;
    public const uint DIGCF_DEVICEINTERFACE = 0x00000010;

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern IntPtr SetupDiGetClassDevs(
        ref Guid ClassGuid,
        [MarshalAs(UnmanagedType.LPTStr)] string? Enumerator,
        IntPtr hwndParent,
        uint Flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr DeviceInfoSet,
        IntPtr DeviceInfoData,
        ref Guid InterfaceClassGuid,
        uint MemberIndex,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    internal static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr DeviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
        IntPtr DeviceInterfaceDetailData,
        uint DeviceInterfaceDetailDataSize,
        out uint RequiredSize,
        IntPtr DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);
}

#endregion

/// <summary>蓝牙音频设备模型（按 MAC 聚合为物理设备）。</summary>
public sealed class BtAudioDevice
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";          // MAC（分组键）
    public bool IsConnected { get; set; }
    public bool HasKsControl { get; set; }         // 是否有可用的 KS 过滤器（SST 已连接设备可能没有）
    public List<BtAudioEndpoint> Endpoints { get; } = new();
}

public sealed class BtAudioEndpoint
{
    public string DeviceId { get; set; } = "";     // KS 端点设备接口路径（\\?\BTHENUM... 或 bthhfenum）
    public string MmDeviceId { get; set; } = "";   // 关联的 MMDEVAPI 端点 id（可能为空）
    public string SourceName { get; set; } = "";   // 关联的音频端点友好名（可能为空）
    public IntPtr KsControl { get; set; }          // IKsControl COM 指针
    public bool IsActive { get; set; }
}

/// <summary>
/// 蓝牙音频连接管理：枚举蓝牙音频 KS 过滤器、一键连接/断开。
/// 原理：通过 SetupAPI 枚举音频设备接口（KSCATEGORY_AUDIO），过滤 BTHENUM/BTHHFENUM
/// 实例，直接 Activate IKsControl 发送 KSPROPSETID_BtAudio / ONESHOT_RECONNECT|DISCONNECT。
/// 相比依赖音频端点拓扑的方案（ToothTray），本方案不要求设备处于特定连接状态，
/// 对 Intel 智音（SST）蓝牙同样适用——BTHENUM KS 过滤器始终注册存在。
/// </summary>
public static class BtAudioKs
{
    public const uint DEVICE_STATE_ACTIVE = 0x00000001;
    public const uint DEVICE_STATE_ALL = 0x0000000F;
    public const uint CLSCTX_ALL = 23;

    private static readonly Guid KSCATEGORY_AUDIO = new("6994AD04-93EF-11D0-A3CC-00A0C9223196");
    private static readonly Guid KSPROPSETID_BtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    private const uint KSPROPERTY_ONESHOT_RECONNECT = 0;
    private const uint KSPROPERTY_ONESHOT_DISCONNECT = 1;
    private const uint KSPROPERTY_TYPE_GET = 0x00000001;

    private static readonly Guid IID_IKsControl = new("28F54685-06FD-11D2-B27A-00A0C9223196");

    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");

    private static readonly PROPERTYKEY PKEY_Device_InstanceId = new()
    {
        fmtid = new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"),
        pid = 256,
    };
    private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14,
    };

    // 蓝牙实例 id 形如 ...\7&29A66C89&0&F84D895352D2_C00000000...，MAC 后跟 _C00000000（大小写不定）
    private static readonly Regex _macRegex = new(@"&([0-9A-Fa-f]{12})_C00000000", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static IMMDeviceEnumerator CreateEnumerator()
    {
        var clsid = CLSID_MMDeviceEnumerator;
        var iid = IID_IMMDeviceEnumerator;
        int hr = Ole32Interop.CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_ALL, ref iid, out IntPtr ptr);
        if (hr < 0)
            throw new COMException($"CoCreateInstance(MMDeviceEnumerator) failed: 0x{hr:X8}");
        return (IMMDeviceEnumerator)Marshal.GetObjectForIUnknown(ptr);
    }

    private static string ExtractMac(string deviceIdOrInstanceId)
    {
        var m = _macRegex.Match(deviceIdOrInstanceId);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
    }

    /// <summary>
    /// 用 SetupAPI 枚举全部音频设备接口，返回 BTHENUM/BTHHFENUM 蓝牙 KS 过滤器路径。
    /// </summary>
    private static List<string> EnumerateBthAudioInterfacePaths()
    {
        var paths = new List<string>();
        var classGuid = KSCATEGORY_AUDIO;
        IntPtr devInfo = SetupApiInterop.SetupDiGetClassDevs(
            ref classGuid, null, IntPtr.Zero,
            SetupApiInterop.DIGCF_PRESENT | SetupApiInterop.DIGCF_DEVICEINTERFACE);
        if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1))
            return paths;

        try
        {
            var ifaceData = new SP_DEVICE_INTERFACE_DATA();
            ifaceData.cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>();

            for (uint index = 0; ; index++)
            {
                if (!SetupApiInterop.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref classGuid, index, ref ifaceData))
                    break; // ERROR_NO_MORE_ITEMS

                bool got = SetupApiInterop.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifaceData, IntPtr.Zero, 0, out uint reqSize, IntPtr.Zero);
                int err = got ? 0 : Marshal.GetLastWin32Error();
                if (!got && err != 122 /* ERROR_INSUFFICIENT_BUFFER */)
                    continue;
                if (reqSize == 0)
                    continue;

                IntPtr detail = Marshal.AllocHGlobal((int)reqSize);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6); // cbSize（32 位为 6，64 位为 8）
                    var detailData = Marshal.PtrToStructure<SP_DEVICE_INTERFACE_DETAIL_DATA>(detail);
                    if (SetupApiInterop.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifaceData, detail, reqSize, out _, IntPtr.Zero))
                    {
                        // DevicePath 紧跟 cbSize 字段（offset 4）之后，WCHAR* 起读
                        IntPtr pathPtr = IntPtr.Add(detail, Marshal.SizeOf<uint>());
                        string? path = Marshal.PtrToStringUni(pathPtr);
                        if (!string.IsNullOrEmpty(path) &&
                            (path.Contains(@"\\?\bthenum#", StringComparison.OrdinalIgnoreCase) ||
                             path.Contains(@"\\?\bthhfenum#", StringComparison.OrdinalIgnoreCase)))
                        {
                            paths.Add(path);
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupApiInterop.SetupDiDestroyDeviceInfoList(devInfo);
        }

        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ReadStringProperty(IMMDevice device, PROPERTYKEY key)
    {
        try
        {
            int hr = device.OpenPropertyStore(0 /* STGM_READ */, out var store);
            if (hr < 0 || store == null) return null;
            try
            {
                hr = store.GetValue(ref key, out PropVariant pv);
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

    /// <summary>蓝牙音频服务 GUID 前缀（A2DP SRC/SNK、HFP）</summary>
    private static readonly string[] AudioServiceGuidPrefixes =
    {
        "{0000110B-", // A2DP Source
        "{0000110A-", // A2DP Sink
        "{0000111E-", // Handsfree
        "{0000111F-", // Handsfree Audio Gateway
        "{0000110D-", // AVDTP
    };

    private static bool IsAudioServiceKey(string keyName)
    {
        foreach (var p in AudioServiceGuidPrefixes)
        {
            if (keyName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 注册表枚举全部已配对蓝牙音频设备（BTHENUM/BTHHFENUM），返回 MAC → 友好名。
    /// 不依赖连接状态——断开/连接都注册存在。
    /// </summary>
    private static Dictionary<string, string> EnumeratePairedBtAudioDevices()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var enumName in new[] { "BTHENUM", "BTHHFENUM" })
            {
                using var root = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{enumName}");
                if (root == null) continue;

                foreach (var svcName in root.GetSubKeyNames())
                {
                    if (!IsAudioServiceKey(svcName)) continue;
                    using var svc = root.OpenSubKey(svcName);
                    if (svc == null) continue;

                    foreach (var inst in svc.GetSubKeyNames())
                    {
                        var mac = ExtractMac(inst);
                        if (mac.Length == 0) continue;
                        string name = "";
                        try
                        {
                            using var instKey = svc.OpenSubKey(inst);
                            // FriendlyName 形如 "@...btha2dp.sys,#1;%1%0\n;(设备名)"
                            var raw = instKey?.GetValue("FriendlyName") as string ?? "";
                            var m = Regex.Match(raw, @";\(([^)]+)\)");
                            if (m.Success)
                                name = m.Groups[1].Value.Trim();
                            if (string.IsNullOrWhiteSpace(name))
                                name = instKey?.GetValue("DeviceDesc") as string ?? "";
                        }
                        catch { }

                        if (!map.ContainsKey(mac) || string.IsNullOrWhiteSpace(map[mac]))
                            map[mac] = string.IsNullOrWhiteSpace(name) ? inst : name.Trim();
                    }
                }
            }
        }
        catch
        {
            // 注册表读取失败时返回空
        }
        return map;
    }

    /// <summary>枚举所有蓝牙音频设备（按 MAC 聚合为物理设备）。</summary>
    public static List<BtAudioDevice> EnumerateDevices()
    {
        var result = new List<BtAudioDevice>();
        var byMac = new Dictionary<string, BtAudioDevice>(StringComparer.OrdinalIgnoreCase);

        // 1) 已配对设备骨架（注册表，始终存在）
        var paired = EnumeratePairedBtAudioDevices();
        foreach (var kv in paired)
        {
            var dev = new BtAudioDevice { Key = kv.Key, Name = kv.Value };
            byMac[kv.Key] = dev;
            result.Add(dev);
        }

        // 2) KS 过滤器（SetupAPI，存在即可控制）
        var ksPaths = EnumerateBthAudioInterfacePaths();
        IMMDeviceEnumerator? enumerator = null;
        try
        {
            enumerator = CreateEnumerator();
            foreach (var path in ksPaths)
            {
                var mac = ExtractMac(path);
                if (mac.Length == 0) continue;

                int hr;
                IMMDevice? ksDevice;
                try
                {
                    hr = enumerator.GetDevice(path, out ksDevice);
                }
                catch
                {
                    // 带接口索引前缀的实例 id（{2}.\\?\...）
                    try { hr = enumerator.GetDevice("{2}." + path, out ksDevice); }
                    catch { hr = -1; ksDevice = null; }
                }
                if (hr < 0 || ksDevice == null) continue;

                var ksIid = IID_IKsControl;
                int ksHr = ksDevice.Activate(ref ksIid, CLSCTX_ALL, IntPtr.Zero, out IntPtr ksPtr);
                if (ksHr < 0 || ksPtr == IntPtr.Zero)
                {
                    Marshal.ReleaseComObject(ksDevice);
                    continue;
                }

                if (!byMac.TryGetValue(mac, out var dev))
                {
                    dev = new BtAudioDevice { Key = mac, Name = path };
                    byMac[mac] = dev;
                    result.Add(dev);
                }

                dev.HasKsControl = true;
                dev.Endpoints.Add(new BtAudioEndpoint
                {
                    DeviceId = path,
                    KsControl = ksPtr,
                });
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

        // 按连接状态排序：已连接在前（IsConnected 由调用方刷新）
        return result.OrderByDescending(d => d.IsConnected).ThenBy(d => d.Name).ToList();
    }

    /// <summary>返回所有蓝牙音频设备名集合（供设备列表标记蓝牙）。</summary>
    public static HashSet<string> GetBluetoothDeviceNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dev in EnumerateDevices())
        {
            if (!string.IsNullOrWhiteSpace(dev.Name))
                names.Add(dev.Name);
        }
        return names;
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
