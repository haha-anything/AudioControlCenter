using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BTAudioSwitcher.Converters;

/// <summary>exe 路径 → 应用图标（提取关联图标）</summary>
public class IconConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (values.Length > 0 && values[0] is string path && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    using var bmp = icon.ToBitmap();
                    var ms = new MemoryStream();
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    ms.Position = 0;
                    var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    return decoder.Frames[0];
                }
            }
        }
        catch { }
        return null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool 取反</summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>bool → 画刷（true=红色/强调，false=中性；parameter 可指定 "orange"）</summary>
public class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true)
        {
            var mode = parameter?.ToString() ?? "";
            return new SolidColorBrush(mode == "orange"
                ? Color.FromRgb(0xFF, 0x9F, 0x0A)
                : Color.FromRgb(0xFF, 0x45, 0x3A));
        }
        return new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x92));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool → Visibility（true=Visible，false=Collapsed）</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>导航 Tab：SelectedTab == parameter 时 Visible（parameter 为 "0"/"1"/"2"/"3"）</summary>
public class TabVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int tab && parameter is string s && int.TryParse(s, out var target))
            return tab == target ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>导航 RadioButton：SelectedTab == parameter 时 IsChecked</summary>
public class TabCheckedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int tab && parameter is string s && int.TryParse(s, out var target))
            return tab == target;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // RadioButton 点击 → 切换到对应 tab
        if (parameter is string s && int.TryParse(s, out var target)) return target;
        return 0;
    }
}
