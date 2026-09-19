using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AudioControlCenter.Interop;
using AudioControlCenter.Models;
using CSCore;
using CSCore.CoreAudioAPI;
using CSCore.Win32;

namespace AudioControlCenter.Services;

/// <summary>
/// 音频核心服务：枚举设备与会话、全局默认设备切换、分应用设备切换（套壳 EarTrumpet 的
/// AudioPolicyConfig + CSCore 会话枚举）。
/// </summary>
public sealed class AudioService : IDisposable
{
    /// <summary>系统默认输出设备 id</summary>
    public string? DefaultOutputId { get; private set; }
    /// <summary>系统默认输入设备 id</summary>
    public string? DefaultInputId { get; private set; }

    public List<AudioDeviceItem> RenderDevices { get; } = new();
    public List<AudioDeviceItem> CaptureDevices { get; } = new();
    public List<AppSessionItem> Sessions { get; } = new();

    /// <summary>完整刷新：设备 + 会话</summary>
    public void RefreshAll()
    {
        RefreshDevices();
        RefreshSessions();
    }

    private HashSet<string>? _bluetoothNames;

    /// <summary>刷新设备列表与默认设备</summary>
    public void RefreshDevices()
    {
        RenderDevices.Clear();
        CaptureDevices.Clear();

        try
        {
            // 蓝牙标记：按设备名匹配（MMDEVAPI 端点不含设备实例属性）
            _bluetoothNames = BtAudioKs.GetBluetoothDeviceNames();

            using var enumerator = new MMDeviceEnumerator();

            try
            {
                using var defOut = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                DefaultOutputId = defOut?.DeviceID;
            }
            catch { DefaultOutputId = null; }
            try
            {
                using var defIn = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
                DefaultInputId = defIn?.DeviceID;
            }
            catch { DefaultInputId = null; }

            FillDevices(enumerator, DataFlow.Render, RenderDevices, DefaultOutputId);
            FillDevices(enumerator, DataFlow.Capture, CaptureDevices, DefaultInputId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RefreshDevices failed: {ex}");
        }
    }

    private void FillDevices(MMDeviceEnumerator enumerator, DataFlow flow, List<AudioDeviceItem> target, string? defaultId)
    {
        using var collection = enumerator.EnumAudioEndpoints(flow, DeviceState.Active | DeviceState.UnPlugged);
        foreach (var dev in collection)
        {
            using (dev)
            {
                string id = dev.DeviceID;
                bool present = dev.DeviceState == DeviceState.Active;
                bool isBt = _bluetoothNames?.Any(n => dev.FriendlyName.Contains(n, StringComparison.OrdinalIgnoreCase)) ?? false;
                target.Add(new AudioDeviceItem
                {
                    DeviceId = id,
                    Name = dev.FriendlyName,
                    Kind = flow == DataFlow.Render ? EDataFlowKind.Render : EDataFlowKind.Capture,
                    IsBluetooth = isBt,
                    IsDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase),
                    IsPresent = present,
                    StateText = present ? "可用" : "未连接",
                });
            }
        }

        // 稳定排序：默认设备优先，其余按名称（避免系统"最后使用"乱序）
        target.Sort((a, b) =>
        {
            if (a.IsDefault != b.IsDefault) return a.IsDefault ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        });
    }

    /// <summary>刷新应用会话（按进程合并渲染/采集会话）</summary>
    public void RefreshSessions()
    {
        Sessions.Clear();

        var sessionsByPid = new Dictionary<uint,
            (string RenderDevId, string RenderDevName, AudioSessionControl2? RenderSession,
             string CaptureDevId, string CaptureDevName)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var renderCol = enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active);
            using var captureCol = enumerator.EnumAudioEndpoints(DataFlow.Capture, DeviceState.Active);

            // 1) 渲染设备的会话
            foreach (var dev in renderCol)
            {
                string devId, devName;
                using (dev)
                {
                    devId = dev.DeviceID;
                    devName = dev.FriendlyName;
                    using var mgr = GetSessionManager(dev);
                    if (mgr == null) continue;
                    using var sessions = mgr.GetSessionEnumerator();
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        var s2 = session.QueryInterface<AudioSessionControl2>();
                        if (s2 == null) continue;
                        try
                        {
                            uint pid = (uint)s2.ProcessID;
                            if (pid == 0) continue;
                            if (!sessionsByPid.TryGetValue(pid, out var entry))
                                entry = (devId, devName, s2, "", "");
                            else
                                entry.RenderSession?.Dispose();
                            sessionsByPid[pid] = (entry.RenderDevId, entry.RenderDevName, s2, entry.CaptureDevId, entry.CaptureDevName);
                        }
                        catch { }
                    }
                }
            }

            // 2) 采集设备的会话（用于匹配输入设备）
            foreach (var dev in captureCol)
            {
                string devId, devName;
                using (dev)
                {
                    devId = dev.DeviceID;
                    devName = dev.FriendlyName;
                    using var mgr = GetSessionManager(dev);
                    if (mgr == null) continue;
                    using var sessions = mgr.GetSessionEnumerator();
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        var s2 = session.QueryInterface<AudioSessionControl2>();
                        if (s2 == null) continue;
                        try
                        {
                            uint pid = (uint)s2.ProcessID;
                            if (pid == 0) continue;
                            if (sessionsByPid.TryGetValue(pid, out var entry))
                                sessionsByPid[pid] = (entry.RenderDevId, entry.RenderDevName, entry.RenderSession, devId, devName);
                        }
                        catch { }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RefreshSessions failed: {ex}");
        }

        // 3) 构建 UI 模型
        foreach (var kv in sessionsByPid.OrderBy(k => GetProcessName(k.Key)))
        {
            var (renderDevId, renderDevName, renderSession, captureDevId, captureDevName) = kv.Value;
            if (renderSession == null && captureDevId.Length == 0) continue;

            var item = new AppSessionItem
            {
                ProcessId = kv.Key,
                ProcessName = GetProcessName(kv.Key),
                DisplayName = GetDisplayName(renderSession, kv.Key),
                IconPath = GetIconPath(kv.Key),
            };

            if (renderSession != null)
            {
                using var volume = renderSession.QueryInterface<SimpleAudioVolume>();
                if (volume != null)
                {
                    item.Volume = volume.MasterVolume;
                    item.IsMuted = volume.IsMuted;
                }
                item.CurrentOutputDeviceId = renderDevId;
                item.CurrentOutputDeviceName = renderDevName;
            }
            else
            {
                item.CurrentOutputDeviceId = "";
                item.CurrentOutputDeviceName = "（无输出）";
            }

            if (captureDevId.Length > 0)
            {
                item.CurrentInputDeviceId = captureDevId;
                item.CurrentInputDeviceName = captureDevName;
            }
            else
            {
                item.CurrentInputDeviceId = "";
                item.CurrentInputDeviceName = "（无输入）";
            }

            item.PersistedOutputDeviceId = AudioPolicy.GetProcessDefaultEndpoint(kv.Key, EDataFlow.eRender);
            item.PersistedInputDeviceId = AudioPolicy.GetProcessDefaultEndpoint(kv.Key, EDataFlow.eCapture);

            Sessions.Add(item);
        }

        // 释放持有中的会话对象
        foreach (var kv in sessionsByPid.Values)
            kv.RenderSession?.Dispose();
    }

    private static AudioSessionManager2? GetSessionManager(MMDevice dev)
    {
        try { return AudioSessionManager2.FromMMDevice(dev); } catch { return null; }
    }

    private static string GetProcessName(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch { return pid.ToString(); }
    }

    private static string? GetDisplayName(AudioSessionControl2? session, uint pid)
    {
        if (session != null)
        {
            try
            {
                var n = session.DisplayName;
                if (!string.IsNullOrWhiteSpace(n))
                    return ResolveDisplayName(n);
            }
            catch { }
        }
        return GetProcessName(pid);
    }

    /// <summary>解析资源字符串显示名（如 "@%SystemRoot%\...AudioSrv.Dll,-202" → "系统声音"）</summary>
    private static string ResolveDisplayName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith("@", StringComparison.Ordinal))
            return raw;
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            int hr = Interop.Shell32Interop.SHLoadIndirectString(raw, sb, sb.Capacity, IntPtr.Zero);
            if (hr == 0 && sb.Length > 0)
                return sb.ToString();
        }
        catch { }
        return raw;
    }

    private static string? GetIconPath(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            if (p == null || p.MainModule == null) return null;
            return p.MainModule.FileName;
        }
        catch { return null; }
    }

    // ---------- 操作 ----------

    /// <summary>设置全局默认输出设备</summary>
    public bool SetGlobalDefaultOutput(string deviceId)
        => AudioPolicy.SetGlobalDefaultEndpoint(deviceId, EDataFlow.eRender);

    /// <summary>设置全局默认输入设备</summary>
    public bool SetGlobalDefaultInput(string deviceId)
        => AudioPolicy.SetGlobalDefaultEndpoint(deviceId, EDataFlow.eCapture);

    /// <summary>为应用设置输出设备（系统持久化）</summary>
    public bool SetSessionOutput(AppSessionItem session, string deviceId)
    {
        if (!AudioPolicy.SetProcessDefaultEndpoint(session.ProcessId, EDataFlow.eRender, deviceId))
            return false;
        session.PersistedOutputDeviceId = deviceId;
        session.RefreshFollowState();
        return true;
    }

    /// <summary>为应用设置输入设备（系统持久化）</summary>
    public bool SetSessionInput(AppSessionItem session, string deviceId)
    {
        if (!AudioPolicy.SetProcessDefaultEndpoint(session.ProcessId, EDataFlow.eCapture, deviceId))
            return false;
        session.PersistedInputDeviceId = deviceId;
        session.RefreshFollowState();
        return true;
    }

    /// <summary>静默清除进程默认端点（供 UI 直接改模型场景）</summary>
    public void ClearProcessDefaultEndpointQuiet(uint processId, Models.EDataFlowKind kind)
    {
        AudioPolicy.ClearProcessDefaultEndpoint(
            processId,
            kind == Models.EDataFlowKind.Render ? EDataFlow.eRender : EDataFlow.eCapture);
    }

    /// <summary>应用恢复为跟随全局默认（清除输出+输入覆盖）</summary>
    public void ResetSessionToDefault(AppSessionItem session)
    {
        AudioPolicy.ClearProcessDefaultEndpoint(session.ProcessId, EDataFlow.eRender);
        AudioPolicy.ClearProcessDefaultEndpoint(session.ProcessId, EDataFlow.eCapture);
        session.PersistedOutputDeviceId = null;
        session.PersistedInputDeviceId = null;
        session.RefreshFollowState();
    }

    /// <summary>设置应用会话音量（0-1）</summary>
    public void SetSessionVolume(AppSessionItem session, float volume)
    {
        try
        {
            SetVolumeForPid(session.ProcessId, volume, null);
            session.Volume = volume;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetSessionVolume failed: {ex}");
        }
    }

    /// <summary>切换应用静音</summary>
    public void SetSessionMute(AppSessionItem session, bool muted)
    {
        try
        {
            SetVolumeForPid(session.ProcessId, null, muted);
            session.IsMuted = muted;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SetSessionMute failed: {ex}");
        }
    }

    private static void SetVolumeForPid(uint pid, float? volume, bool? muted)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var col = enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active);
        foreach (var dev in col)
        {
            using (dev)
            {
                using var mgr = GetSessionManager(dev);
                if (mgr == null) continue;
                using var sessions = mgr.GetSessionEnumerator();
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    var s2 = session.QueryInterface<AudioSessionControl2>();
                    if (s2 == null) continue;
                    if ((uint)s2.ProcessID != pid) continue;
                    using var vol = session.QueryInterface<SimpleAudioVolume>();
                    if (vol == null) continue;
                    if (volume is float v) vol.MasterVolume = v;
                    if (muted is bool m) vol.IsMuted = m;
                }
            }
        }
    }

    public void Dispose()
    {
    }
}
