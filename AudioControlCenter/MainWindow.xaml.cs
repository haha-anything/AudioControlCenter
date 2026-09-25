using System.ComponentModel;
using System.Windows;
using BTAudioSwitcher.Services;

namespace BTAudioSwitcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Vm;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // 关闭主窗口 = 隐藏到托盘，不退出
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
