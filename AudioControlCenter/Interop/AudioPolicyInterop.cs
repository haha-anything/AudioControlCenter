using System;
using System.Runtime.InteropServices;

namespace BTAudioSwitcher.Interop;

/// <summary>音频数据流方向（对应 EDataFlow）</summary>
public enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2,
}

/// <summary>音频角色（对应 ERole）</summary>
public enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2,
}

/// <summary>combase.dll 辅助（WindowsCreateString / RoGetActivationFactory）</summary>
internal static class Combase
{
    [DllImport("combase.dll", PreserveSig = false)]
    public static extern void RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        [In] ref Guid iid,
        [Out, MarshalAs(UnmanagedType.IInspectable)] out object factory);

    [DllImport("combase.dll", PreserveSig = false)]
    public static extern void WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string src,
        [In] uint length,
        [Out] out IntPtr hstring);
}

/// <summary>
/// Windows.Media.Internal.AudioPolicyConfig（Win11 21H2+ 变体，EarTrumpet 同款）
/// 提供“为指定进程设置持久化默认音频端点”的能力 —— 即分应用音频设备切换。
/// </summary>
[Guid("ab3d4648-e242-459f-b02f-541c70306324")]
[InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
internal interface IAudioPolicyConfigFactory21H2
{
    int __add_CtxVolumeChange();
    int __remove_CtxVolumeChanged();
    int __add_RingerVibrateStateChanged();
    int __remove_RingerVibrateStateChange();
    int __SetVolumeGroupGainForId();
    int __GetVolumeGroupGainForId();
    int __GetActiveVolumeGroupForEndpointId();
    int __GetVolumeGroupsForEndpoint();
    int __GetCurrentVolumeContext();
    int __SetVolumeGroupMuteForId();
    int __GetVolumeGroupMuteForId();
    int __SetRingerVibrateState();
    int __GetRingerVibrateState();
    int __SetPreferredChatApplication();
    int __ResetPreferredChatApplication();
    int __GetPreferredChatApplication();
    int __GetCurrentChatApplications();
    int __add_ChatContextChanged();

    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId);
    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, [Out, MarshalAs(UnmanagedType.HString)] out string deviceId);
    [PreserveSig]
    int ClearAllPersistedApplicationDefaultEndpoints();
}

/// <summary>Win10 1809 ~ Win11 21H1 变体（比 21H2 多一个占位方法）</summary>
[Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258")]
[InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
internal interface IAudioPolicyConfigFactoryDownlevel
{
    int __add_CtxVolumeChange();
    int __remove_CtxVolumeChanged();
    int __add_RingerVibrateStateChanged();
    int __remove_RingerVibrateStateChange();
    int __SetVolumeGroupGainForId();
    int __GetVolumeGroupGainForId();
    int __GetActiveVolumeGroupForEndpointId();
    int __GetVolumeGroupsForEndpoint();
    int __GetCurrentVolumeContext();
    int __SetVolumeGroupMuteForId();
    int __GetVolumeGroupMuteForId();
    int __SetRingerVibrateState();
    int __GetRingerVibrateState();
    int __SetPreferredChatApplication();
    int __ResetPreferredChatApplication();
    int __GetPreferredChatApplication();
    int __GetCurrentChatApplications();
    int __add_ChatContextChanged();
    int __remove_ChatContextChanged();

    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, IntPtr deviceId);
    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(uint processId, EDataFlow flow, ERole role, [Out, MarshalAs(UnmanagedType.HString)] out string deviceId);
    [PreserveSig]
    int ClearAllPersistedApplicationDefaultEndpoints();
}

/// <summary>全局默认设备：PolicyConfig 客户端（EarTrumpet 同款）</summary>
[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClient
{
}

[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigWin7
{
    [PreserveSig]
    int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, IntPtr pkey, IntPtr pv);
    [PreserveSig]
    int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, IntPtr pkey, IntPtr pv);
    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, ERole eRole);
    [PreserveSig]
    int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string wszDeviceId, short isVisible);
}

/// <summary>
/// 音频策略门面：分应用设备切换（持久化，重启仍生效）+ 全局默认设备切换。
/// 完全移植自 EarTrumpet（MIT License）。
/// </summary>
public static class AudioPolicy
{
    private const string DEVINTERFACE_AUDIO_RENDER = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string DEVINTERFACE_AUDIO_CAPTURE = "#{2eef81be-33fa-4800-9670-1cd474972c3f}";
    private const string MMDEVAPI_TOKEN = @"\\?\SWD#MMDEVAPI#";

    private static readonly object _sync = new();
    private static IAudioPolicyConfigFactory21H2? _factory21H2;
    private static IAudioPolicyConfigFactoryDownlevel? _factoryDown;
    private static readonly bool _is21H2OrLater = Environment.OSVersion.Version >= new Version(10, 0, 22000);

    private static IPolicyConfigWin7 _policyClient = (IPolicyConfigWin7)new PolicyConfigClient();

    private static void EnsureFactory()
    {
        if (_is21H2OrLater)
        {
            if (_factory21H2 == null)
            {
                var iid = typeof(IAudioPolicyConfigFactory21H2).GUID;
                Combase.RoGetActivationFactory("Windows.Media.Internal.AudioPolicyConfig", ref iid, out object factory);
                _factory21H2 = (IAudioPolicyConfigFactory21H2)factory;
            }
        }
        else
        {
            if (_factoryDown == null)
            {
                var iid = typeof(IAudioPolicyConfigFactoryDownlevel).GUID;
                Combase.RoGetActivationFactory("Windows.Media.Internal.AudioPolicyConfig", ref iid, out object factory);
                _factoryDown = (IAudioPolicyConfigFactoryDownlevel)factory;
            }
        }
    }

    private static string GenerateDeviceId(string deviceId, EDataFlow flow)
        => $"{MMDEVAPI_TOKEN}{deviceId}{(flow == EDataFlow.eRender ? DEVINTERFACE_AUDIO_RENDER : DEVINTERFACE_AUDIO_CAPTURE)}";

    private static string UnpackDeviceId(string deviceId)
    {
        if (deviceId.StartsWith(MMDEVAPI_TOKEN))
            deviceId = deviceId.Remove(0, MMDEVAPI_TOKEN.Length);
        if (deviceId.EndsWith(DEVINTERFACE_AUDIO_RENDER))
            deviceId = deviceId.Remove(deviceId.Length - DEVINTERFACE_AUDIO_RENDER.Length);
        if (deviceId.EndsWith(DEVINTERFACE_AUDIO_CAPTURE))
            deviceId = deviceId.Remove(deviceId.Length - DEVINTERFACE_AUDIO_CAPTURE.Length);
        return deviceId;
    }

    /// <summary>为指定进程设置持久化默认输出/输入设备。deviceId 为 MMDevice id（形如 {0.0.0.00000000}.{guid}）。</summary>
    public static bool SetProcessDefaultEndpoint(uint processId, EDataFlow flow, string deviceId)
    {
        lock (_sync)
        {
            try
            {
                EnsureFactory();
                IntPtr hstring = IntPtr.Zero;
                if (!string.IsNullOrWhiteSpace(deviceId))
                {
                    var str = GenerateDeviceId(deviceId, flow);
                    Combase.WindowsCreateString(str, (uint)str.Length, out hstring);
                }

                int hr;
                if (_is21H2OrLater)
                {
                    hr = _factory21H2!.SetPersistedDefaultAudioEndpoint(processId, flow, ERole.eMultimedia, hstring);
                    if (hr >= 0) hr = _factory21H2.SetPersistedDefaultAudioEndpoint(processId, flow, ERole.eConsole, hstring);
                }
                else
                {
                    hr = _factoryDown!.SetPersistedDefaultAudioEndpoint(processId, flow, ERole.eMultimedia, hstring);
                    if (hr >= 0) hr = _factoryDown.SetPersistedDefaultAudioEndpoint(processId, flow, ERole.eConsole, hstring);
                }
                return hr >= 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetProcessDefaultEndpoint failed: {ex}");
                return false;
            }
        }
    }

    /// <summary>查询指定进程当前持久化的默认设备（系统记忆），未设置时返回 null。</summary>
    public static string? GetProcessDefaultEndpoint(uint processId, EDataFlow flow)
    {
        lock (_sync)
        {
            try
            {
                EnsureFactory();
                string deviceId;
                int hr;
                if (_is21H2OrLater)
                {
                    hr = _factory21H2!.GetPersistedDefaultAudioEndpoint(processId, flow, ERole.eMultimedia, out deviceId);
                }
                else
                {
                    hr = _factoryDown!.GetPersistedDefaultAudioEndpoint(processId, flow, ERole.eMultimedia, out deviceId);
                }
                if (hr < 0 || string.IsNullOrWhiteSpace(deviceId))
                    return null;
                var unpacked = UnpackDeviceId(deviceId);
                return string.IsNullOrWhiteSpace(unpacked) ? null : unpacked;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>清除指定进程的持久化设备设置（恢复为跟随默认）。</summary>
    public static bool ClearProcessDefaultEndpoint(uint processId, EDataFlow flow)
        => SetProcessDefaultEndpoint(processId, flow, "");

    /// <summary>设置全局默认设备（系统级）。</summary>
    public static bool SetGlobalDefaultEndpoint(string deviceId, EDataFlow flow)
    {
        try
        {
            // 渲染与采集设备 ID 空间不同，但 PolicyConfig 的 SetDefaultEndpoint 直接收 MMDevice id
            int hr1 = _policyClient.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            int hr2 = _policyClient.SetDefaultEndpoint(deviceId, ERole.eConsole);
            bool ok = hr1 >= 0 && hr2 >= 0;
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diag.log"), $"[{DateTime.Now:HH:mm:ss}] SetGlobalDefaultEndpoint flow={flow} hr={hr1}/{hr2} ok={ok} dev={deviceId}" + Environment.NewLine); } catch { }
            return ok;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SetGlobalDefaultEndpoint failed: {ex}");
            return false;
        }
    }
}
