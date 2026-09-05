using Avalonia;
using global::Avalonia.Controls;

namespace NativeMedia.Avalonia;

/// <summary>将 TimeSpan 格式化显示为 mm:ss 或 hh:mm:ss 的文本控件。<para>Text block that formats a TimeSpan as mm:ss or hh:mm:ss.</para></summary>
public sealed class TimeTextBlock : TextBlock
{
    /// <summary>注册时间值属性。<para>Registered time-value property.</para></summary>
    public static readonly StyledProperty<TimeSpan> ValueProperty = AvaloniaProperty.Register<TimeTextBlock, TimeSpan>(nameof(Value));
    /// <summary>要显示的时间值。<para>Time value to display.</para></summary>
    public TimeSpan Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty) Text = Value.TotalHours >= 1 ? Value.ToString(@"hh\:mm\:ss") : Value.ToString(@"mm\:ss");
    }
}
