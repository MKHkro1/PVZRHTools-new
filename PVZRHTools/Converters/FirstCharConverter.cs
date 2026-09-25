using System;
using System.Globalization;
using System.Windows.Data;

namespace PVZRHTools.Converters;

/// <summary>侧栏收起时 TabItem 只显示首字（5.3.1 #5「左侧栏收起」半边）。</summary>
public sealed class FirstCharConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        return string.IsNullOrEmpty(text) ? string.Empty : text.Substring(0, 1);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
