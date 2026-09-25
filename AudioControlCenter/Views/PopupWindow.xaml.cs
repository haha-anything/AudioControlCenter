using System.Windows;
using System.Windows.Input;

namespace BTAudioSwitcher.Views;

public partial class PopupWindow : Window
{
    public PopupWindow()
    {
        InitializeComponent();
    }

    /// <summary>显示在屏幕右下角（托盘上方）</summary>
    public void ShowNearTray()
    {
        var wa = SystemParameters.WorkArea;
        Show();
        UpdateLayout();
        // SizeToContent=Height 时需布局后才有实际尺寸
        Left = wa.Right - ActualWidth - 14;
        Top = wa.Bottom - ActualHeight - 14;
        Activate();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // 点击面板外部自动关闭
        if (IsLoaded && !IsActive)
            Close();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 允许拖动窗口
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
