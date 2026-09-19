using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AudioControlCenter.Models;
using AudioControlCenter.Services;

namespace AudioControlCenter.Views;

public partial class ControlCenterView : UserControl
{
    private bool _suppressSlider;

    public ControlCenterView()
    {
        InitializeComponent();
        DataContext = App.Vm;
        Loaded += (_, _) =>
        {
            App.Vm.RefreshAll();
            UpdateEmptyStates();
        };
    }

    private ControlCenterViewModel Vm => App.Vm;

    private void UpdateEmptyStates()
    {
        BtEmptyText.Visibility = Vm.BluetoothDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionEmptyText.Visibility = Vm.Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- 头部 ----------

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Vm.RefreshAll();
        UpdateEmptyStates();
    }

    // ---------- 蓝牙 ----------

    private async void BtToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BluetoothDeviceItem item })
        {
            await Vm.ToggleBluetoothAsync(item);
            UpdateEmptyStates();
        }
    }

    // ---------- 应用会话 ----------

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: AppSessionItem s })
        {
            Vm.SessionMuteChanged(s, !s.IsMuted);
            if (sender is Button b && b.Content is TextBlock tb)
                tb.Text = s.IsMuted ? "🔇" : "🔊";
        }
    }

    private void SessionVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSlider || sender is not Slider { Tag: AppSessionItem s })
            return;
        Vm.SessionVolumeChanged(s, (float)e.NewValue);
    }

    private void SessionCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.Tag is not AppSessionItem s)
            return;

        bool isOutput = combo.Name == "OutputCombo";
        string? targetId = isOutput ? s.PersistedOutputDeviceId : s.PersistedInputDeviceId;
        SelectComboItem(combo, targetId);
    }

    private static void SelectComboItem(ComboBox combo, string? deviceId)
    {
        if (!string.IsNullOrEmpty(deviceId))
        {
            foreach (var item in combo.Items)
            {
                if (item is AudioDeviceItem dev &&
                    string.Equals(dev.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = dev;
                    return;
                }
            }
        }
        else
        {
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            combo.SelectedIndex = 0;
            return;
        }

        // 持久化的设备当前不存在（已拔出）：不选中，避免误清除系统记录
        combo.SelectedIndex = -1;
    }

    private void SessionOutput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.Tag is not AppSessionItem s || e.AddedItems.Count == 0)
            return;

        switch (e.AddedItems[0])
        {
            case ComboBoxItem:
                Vm.ApplySessionOutput(s, null);
                break;
            case AudioDeviceItem dev:
                Vm.ApplySessionOutput(s, dev.DeviceId);
                break;
        }
    }

    private void SessionInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.Tag is not AppSessionItem s || e.AddedItems.Count == 0)
            return;

        switch (e.AddedItems[0])
        {
            case ComboBoxItem:
                Vm.ApplySessionInput(s, null);
                break;
            case AudioDeviceItem dev:
                Vm.ApplySessionInput(s, dev.DeviceId);
                break;
        }
    }

    private void ResetSession_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AppSessionItem s })
            return;

        Vm.ResetSession(s);
        // 重置两个下拉为"跟随默认"
        foreach (var child in FindVisualChildren<ComboBox>(this))
        {
            if (child.Tag == s && (child.Name == "OutputCombo" || child.Name == "InputCombo"))
                SelectComboItem(child, null);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject parent)
        where T : System.Windows.DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t)
                yield return t;
            foreach (var sub in FindVisualChildren<T>(child))
                yield return sub;
        }
    }
}
