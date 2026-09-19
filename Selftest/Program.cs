using AudioControlCenter.Interop;
using AudioControlCenter.Services;

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
}
catch (Exception ex)
{
    Console.WriteLine($"自检异常: {ex}");
}
