using System.ComponentModel;
using System.Windows;
using AudioControlCenter.Services;

namespace AudioControlCenter;

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
