using System.Windows;
using System.Windows.Input;
using BTAudioSwitcher.Services;

namespace BTAudioSwitcher.Views;

/// <summary>迷你控制面板（托盘单击弹出）：最近设备一键连接 + 默认设备切换 + 全局音量</summary>
public partial class QuickPanelWindow : Window
{
    private bool _volumeLoading = true;

    public QuickPanelWindow()
    {
        InitializeComponent();
        DataContext = App.Vm;
        Loaded += (_, _) => LoadVolume();
    }

    /// <summary>显示在屏幕右下角（托盘上方）</summary>
    public void ShowNearTray()
    {
        var wa = SystemParameters.WorkArea;
        Show();
        UpdateLayout();
        Left = wa.Right - ActualWidth - 14;
        Top = wa.Bottom - ActualHeight - 14;
        Activate();
    }

    private void LoadVolume()
    {
        try
        {
            var v = App.Vm.Audio.GetDefaultOutputVolume();
            if (v is float f)
            {
                _volumeLoading = true;
                VolumeSlider.Value = f;
                VolumeText.Text = $"{Math.Round(f * 100)}%";
                _volumeLoading = false;
            }
            else
            {
                VolumeText.Text = "--";
                _volumeLoading = false;
            }
        }
        catch { _volumeLoading = false; }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volumeLoading) return;
        try
        {
            App.Vm.Audio.SetDefaultOutputVolume((float)e.NewValue);
            VolumeText.Text = $"{Math.Round(e.NewValue * 100)}%";
        }
        catch { }
    }

    private void QuickDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Models.BluetoothDeviceItem item)
        {
            _ = App.Vm.ToggleBluetoothAsync(item);
        }
    }

    private void OpenFull_Click(object sender, RoutedEventArgs e)
    {
        Close();
        App.ShowFullPanel();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsLoaded && !IsActive)
            Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
