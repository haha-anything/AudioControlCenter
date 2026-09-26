using BTAudioSwitcher.Interop;
using BTAudioSwitcher.Services;
using CSCore.CoreAudioAPI;

// 服务层自检：验证设备/会话/蓝牙枚举是否真实可用
Console.OutputEncoding = System.Text.Encoding.UTF8;

try
{
    var audio = new AudioService();
    audio.RefreshDevices();

    Console.WriteLine($"===== 输出设备（{audio.RenderDevices.Count}）=====");
    foreach (var d in audio.RenderDevices)
        Console.WriteLine($"  [{(d.IsDefault ? "默认" : "    ")}] {d.Name} (bt={d.IsBluetooth}, {d.StateText})");

    Console.WriteLine($"===== 输入设备（{audio.CaptureDevices.Count}）=====");
    foreach (var d in audio.CaptureDevices)
        Console.WriteLine($"  [{(d.IsDefault ? "默认" : "    ")}] {d.Name} (bt={d.IsBluetooth}, {d.StateText})");

    audio.RefreshSessions();
    Console.WriteLine($"===== 应用会话（{audio.Sessions.Count}）=====");
    foreach (var s in audio.Sessions)
        Console.WriteLine($"  {s.DisplayName} 输出={s.CurrentOutputDeviceName} 输入={s.CurrentInputDeviceName} 音量={s.Volume:P0} 覆盖=[{s.PersistedOutputDeviceId ?? "无"}|{s.PersistedInputDeviceId ?? "无"}]");

    var bt = new BluetoothService();
    bt.Refresh(audio);
    Console.WriteLine($"===== 蓝牙音频设备（{bt.Devices.Count}）=====");
    foreach (var b in bt.Devices)
        Console.WriteLine($"  {b.Name} 已连接={b.IsConnected} 可控={b.HasKsControl}");

    // 会话诊断：每个渲染端点的会话枚举情况
    Console.WriteLine("===== 会话枚举诊断 =====");
    foreach (var d in audio.RenderDevices.Where(x => x.IsPresent))
    {
        try
        {
            using var enumerator = new CSCore.CoreAudioAPI.MMDeviceEnumerator();
            using var dev = enumerator.GetDevice(d.DeviceId);
            using var mgr = AudioSessionManager2.FromMMDevice(dev);
            using var sessions = mgr.GetSessionEnumerator();
            Console.WriteLine($"  {d.Name}: {sessions.Count} 个会话");
            for (int i = 0; i < sessions.Count; i++)
            {
                using var s = sessions[i];
                var s2 = s.QueryInterface<AudioSessionControl2>();
                if (s2 == null) { Console.WriteLine($"    [{i}] 非 s2 类型"); continue; }
                string dn = ""; uint pid = 0;
                try { dn = s2.DisplayName ?? ""; } catch { }
                try { pid = (uint)s2.ProcessID; } catch { }
                Console.WriteLine($"    [{i}] pid={pid} name='{dn}'");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {d.Name}: 枚举失败 {ex.GetType().Name}");
        }
    }

    // 无副作用指令验证：对可控制的未连接设备发 DISCONNECT（已断开状态下指令应成功返回）
    if (args.Contains("--test-bt"))
    {
        Console.WriteLine("===== KS 指令验证（发 DISCONNECT，不改变状态）=====");
        foreach (var b in bt.Devices)
        {
            if (!b.HasKsControl) continue;
            bool ok = BtAudioKs.SetConnected(b.Source, false);
            Console.WriteLine($"  {b.Name}: SetConnected(disconnect) => {ok}");
        }
    }

    // 全局默认设备切换验证（切到非默认再切回，验证 HRESULT）
    Console.WriteLine("===== 全局默认设备切换 ====");
    foreach (var flow in new[] { BTAudioSwitcher.Interop.EDataFlow.eRender, BTAudioSwitcher.Interop.EDataFlow.eCapture })
    {
        var list = flow == BTAudioSwitcher.Interop.EDataFlow.eRender ? audio.RenderDevices : audio.CaptureDevices;
        var current = list.FirstOrDefault(x => x.IsDefault);
        var other = list.FirstOrDefault(x => !x.IsDefault && x.IsPresent);
        if (current == null || other == null) { Console.WriteLine($"  {flow}: 设备不足，跳过"); continue; }
        Console.WriteLine($"  {flow}: 当前默认=[{current.Name}] 切到=[{other.Name}]");
        bool ok1 = BTAudioSwitcher.Interop.AudioPolicy.SetGlobalDefaultEndpoint(other.DeviceId, flow);
        System.Threading.Thread.Sleep(500);
        bool ok2 = BTAudioSwitcher.Interop.AudioPolicy.SetGlobalDefaultEndpoint(current.DeviceId, flow);
        Console.WriteLine($"  {flow}: 切走={ok1} 切回={ok2} (应均为 True)");
    }

    // 电量读取验证（对每个设备试 GATT Battery）
    Console.WriteLine("===== 电量读取（GATT Battery）=====");
    foreach (var b in bt.Devices)
    {
        int? pct = BTAudioSwitcher.Interop.BtWinrt.ReadBatteryPercentAsync(
            BTAudioSwitcher.Interop.BtWinrt.MacToUlong(b.Key)).GetAwaiter().GetResult();
        Console.WriteLine($"  {b.Name} ({b.Key}): {(pct.HasValue ? pct + "%" : "读不到")}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"自检异常: {ex}");
}
