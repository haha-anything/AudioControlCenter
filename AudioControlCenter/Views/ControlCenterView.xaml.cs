using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BTAudioSwitcher.Models;
using BTAudioSwitcher.Services;

namespace BTAudioSwitcher.Views;

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
            InitThemeRadio();
        };
    }

    private void InitThemeRadio()
    {
        var m = Vm.Settings.ThemeMode;
        ThemeAuto.IsChecked = m == 0;
        ThemeDark.IsChecked = m == 1;
        ThemeLight.IsChecked = m == 2;
    }

    private void ThemeMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        int mode = ThemeAuto.IsChecked == true ? 0 : ThemeDark.IsChecked == true ? 1 : 2;
        Vm.Settings.ThemeMode = mode;
        Vm.Settings.Save();
        App.ApplyTheme(mode);
    }

    private ControlCenterViewModel Vm => App.Vm;

    private void UpdateEmptyStates()
    {
        BtEmptyText.Visibility = Vm.BluetoothDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SessionEmptyText.Visibility = Vm.Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>会话列表变化时同步空状态（由定时刷新驱动）</summary>
    public void RefreshEmptyStates() => UpdateEmptyStates();

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

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        Vm.ToggleScan();
    }

    private void MoreBt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BluetoothDeviceItem item }) return;

        var dlg = new Window
        {
            Title = $"备注 - {item.Name}",
            Width = 360,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Application.Current.Resources["CardBrush"] as System.Windows.Media.Brush,
        };

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = "品牌备注（自定义图标匹配关键词）:", FontSize = 14, Margin = new Thickness(0,0,0,8) });
        var input = new TextBox { Text = item.UserBrand ?? "", FontSize = 15, Margin = new Thickness(0,0,0,12) };
        panel.Children.Add(input);

        var save = new Button { Content = "保存", Width = 90, Height = 34, HorizontalAlignment = HorizontalAlignment.Right };
        save.Click += (_, _) =>
        {
            item.UserBrand = string.IsNullOrWhiteSpace(input.Text) ? null : input.Text.Trim();
            dlg.DialogResult = true;
        };
        panel.Children.Add(save);
        dlg.Content = panel;
        dlg.ShowDialog();
    }

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        Vm.ShowAllBluetooth = !Vm.ShowAllBluetooth;
    }

    private async void PairScan_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ScanDeviceItem item })
        {
            await Vm.PairScanDeviceAsync(item);
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
