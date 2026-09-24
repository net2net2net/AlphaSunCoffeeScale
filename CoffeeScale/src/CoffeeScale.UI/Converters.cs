using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CoffeeScale.ViewModels;

namespace CoffeeScale.UI;

/// <summary>DateTime ↔ DateTimeOffset? 互转，供 DatePicker 双向绑定。</summary>
public sealed class DateValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt && dt != DateTime.MinValue)
            return new DateTimeOffset(dt.Date);
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTimeOffset dto) return dto.Date;
        return DateTime.MinValue;
    }
}

/// <summary>字符串为空/空串时返回 true；ConverterParameter=True 表示结果取反（为空时返回 false）。</summary>
public sealed class IsNullOrEmptyConverter : IValueConverter
{
    public static readonly IsNullOrEmptyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool empty = string.IsNullOrEmpty(value as string);
        bool invert = parameter is string s && s.Equals("True", StringComparison.OrdinalIgnoreCase);
        return invert ? !empty : empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

/// <summary>bool → 控件显隐：true 显示 / false 隐藏（直接返回 bool 绑定到 Avalonia 的 IsVisible）；ConverterParameter=True 时取反。用于杯测法静态模式切换控件显隐。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool v = value is bool b && b;
        bool invert = parameter is string s && s.Equals("True", StringComparison.OrdinalIgnoreCase);
        return invert ? !v : v;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

/// <summary>
/// bool → 透明度：true 不透明(1.0) / false 半透明(0.55)，用于按钮禁用态"明确变灰"。
/// 2026-09-13：0.4 → 0.55。深色底上 0.4 会把「停止/下一段」等按钮压到几乎看不见（等同丢失控件），
/// 0.55 仍能一眼看出"未激活"，但按钮轮廓与文字保持可辨——可用性优先。
/// </summary>
public sealed class BoolToDoubleConverter : IValueConverter
{
    public static readonly BoolToDoubleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? 1.0 : 0.55;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => null;
}

/// <summary>文本框文本 ↔ 数值双向转换，并把数值钳制到 [minPath, maxPath] 指定的 VM 边界（粉量档位范围）。</summary>
public sealed class NumericClampConverter : IValueConverter
{
    private readonly BrewViewModel _vm;
    private readonly string _minPath;
    private readonly string _maxPath;
    private readonly double _step;

    public NumericClampConverter(BrewViewModel vm, string minPath, string maxPath, double step)
    {
        _vm = vm; _minPath = minPath; _maxPath = maxPath; _step = step;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? d.ToString("0") : "0";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!double.TryParse(value as string, NumberStyles.Any, culture, out var v)) return _vm.Dose;
        double min = (double?)_vm.GetType().GetProperty(_minPath)?.GetValue(_vm) ?? 0;
        double max = (double?)_vm.GetType().GetProperty(_maxPath)?.GetValue(_vm) ?? 60;
        double clamped = Math.Clamp(Math.Round(v / _step) * _step, min, max);
        return clamped;
    }
}
