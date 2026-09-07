using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;

namespace NativeMedia.Avalonia;

/// <summary>用于显示合成视频帧的 Avalonia 图像表面，其他 Avalonia 控件可以覆盖在其上方。<para>Regular Avalonia surface for composited video frames; other Avalonia controls can render above it.</para></summary>
public sealed class VideoFrameSurface : Control
{
    /// <summary>注册当前帧属性。<para>Registered current-frame property.</para></summary>
    public static readonly StyledProperty<IImage?> SourceProperty =
        Image.SourceProperty.AddOwner<VideoFrameSurface>();

    /// <summary>注册图像缩放模式。<para>Registered image stretch mode.</para></summary>
    public static readonly StyledProperty<Stretch> StretchProperty =
        Image.StretchProperty.AddOwner<VideoFrameSurface>();

    static VideoFrameSurface()
    {
        AffectsMeasure<VideoFrameSurface>(SourceProperty, StretchProperty);
        AffectsRender<VideoFrameSurface>(SourceProperty, StretchProperty);
    }

    /// <summary>当前显示的帧。<para>Frame currently being displayed.</para></summary>
    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>帧在可用区域内的缩放方式。<para>How the frame is scaled within the available area.</para></summary>
    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// 在当前帧真正进入 Avalonia 渲染过程后触发。
    /// <para>Raised after the current frame has actually passed through Avalonia's render path.</para>
    /// </summary>
    internal event EventHandler? FrameRendered;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
        => Source is { } source
            ? Stretch.CalculateSize(availableSize, source.Size)
            : default;

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var source = Source;
        if (source is null) return;

        var sourceRect = new Rect(source.Size);
        var destinationSize = Stretch.CalculateSize(Bounds.Size, source.Size);
        var destinationRect = new Rect(
            (Bounds.Width - destinationSize.Width) / 2,
            (Bounds.Height - destinationSize.Height) / 2,
            destinationSize.Width,
            destinationSize.Height);
        source.Draw(context, sourceRect, destinationRect);
        FrameRendered?.Invoke(this, EventArgs.Empty);
    }
}
