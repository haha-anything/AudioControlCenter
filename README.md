# BT Audio Switcher（蓝牙音频切换器）

Windows 托盘应用：**蓝牙音频一键切换 + 分应用音频设备/音量管理**，一个面板搞定，不用再进系统设置翻来翻去。

## 功能

- 🎛️ **控制中心面板**（仿 iOS 控制中心，托盘单击弹出 / 双击打开主窗口）
- 📶 **蓝牙设备管理**：已配对蓝牙耳机/音箱一键连接/断开，实时显示连接状态
- 🔀 **分应用音频**：每个应用独立选择输出/输入设备、独立音量滑块、独立静音
- 🔁 **跟随默认模型**：应用默认跟随全局默认设备；单独设置后显示「已自定义」，可一键恢复默认
- 🎚️ **系统默认设备切换**：输出/输入默认设备下拉即切
- 🧠 **记忆功能**：设备设置由 Windows 系统持久化（重启自动恢复）；音量偏好本地 JSON 记忆
- 🪟 Win11 深色圆角风格

## 技术实现（套壳整合开源方案）

| 模块 | 方案 | 来源 |
|------|------|------|
| 分应用设备切换 | `Windows.Media.Internal.AudioPolicyConfig` COM（`SetPersistedDefaultAudioEndpoint`，系统级持久化） | [EarTrumpet](https://github.com/File-New-Project/EarTrumpet)（MIT） |
| 全局默认设备 | `PolicyConfigClient` COM | 同上 |
| 设备/会话枚举、音量 | CSCore（CoreAudio API） | [CSCore](https://github.com/filoe/cscore)（MIT） |
| 蓝牙连接/断开 | 音频拓扑 KS 属性（`KSPROPSETID_BtAudio` / ONESHOT_RECONNECT/DISCONNECT） | [ToothTray](https://github.com/m2jean/ToothTray)、[yasb](https://github.com/amnweb/yasb) |

## 构建

```bash
cd AudioControlCenter
dotnet build -c Release
```

产物：`AudioControlCenter/bin/Release/net9.0-windows/AudioControlCenter.exe`

## 使用

- 单击托盘图标：弹出控制中心面板（右下角）
- 双击托盘图标：打开主窗口
- 托盘右键：打开主窗口 / 立即刷新 / 退出
- 蓝牙卡片点「连接/断开」即可切换
- 应用卡片：拖音量滑块、下拉选输出/输入设备、「恢复默认」回到跟随全局默认

## 路线图

- [x] P0：托盘 + 面板 + 分应用音频 + 蓝牙连接 + 记忆 + 全局默认
- [ ] P1：扫描并配对新的蓝牙设备
- [ ] P2：蓝牙设备电量显示
- [ ] P2：面板毛玻璃特效、开机自启选项

## License

MIT（本项目代码；引用的开源组件保留各自许可）
