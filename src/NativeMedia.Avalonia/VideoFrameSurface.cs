using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Media;
using global::Avalonia.Rendering.Composition;
using global::Avalonia.Threading;

namespace NativeMedia.Avalonia;

/// <summary>用于显示合成视频帧的 Avalonia 图像表面，其他 Avalonia 控件可以覆盖在其上方。<para>Regular Avalonia surface for composited video frames; other Avalonia controls can render above it.</para></summary>
public sealed class VideoFrameSurface : Decorator
{
    private GpuVideoTarget? _gpuTarget;
    private CompositionSurfaceVisual? _gpuVisual;
    private Size _gpuSize;
    private int _attachmentGeneration;
    private PixelSize _gpuControlPixelLimit;

    public VideoFrameSurface() => ClipToBounds = true;

    /// <summary>Hosts a composited GPU control (not a native child window).</summary>
    public void SetGpuControl(Control? control)
    {
        Dispatcher.UIThread.VerifyAccess();
        HideGpuFrame();
        Child = control;
    }

    /// <summary>Limits a child GPU render target; an empty limit preserves full output resolution.</summary>
    public void SetGpuControlPixelLimit(PixelSize limit)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_gpuControlPixelLimit == limit) return;
        _gpuControlPixelLimit = limit;
        InvalidateArrange();
    }

    /// <summary>Creates an ordinary Avalonia composition surface, never a child HWND.</summary>
    public async Task<GpuVideoTarget?> GetGpuTargetAsync(CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_gpuTarget is not null && !_gpuTarget.Interop.IsLost) return _gpuTarget;
        if (_gpuTarget is not null)
        {
            ElementComposition.SetElementChildVisual(this, null);
            _gpuTarget.Surface.Dispose();
            _gpuTarget = null;
            _gpuVisual = null;
        }
        var visual = ElementComposition.GetElementVisual(this);
        if (visual is null) return null;
        var generation = _attachmentGeneration;
        var interop = await visual.Compositor.TryGetCompositionGpuInterop();
        cancellationToken.ThrowIfCancellationRequested();
        if (generation != _attachmentGeneration || interop is null || interop.IsLost) return null;
        if (_gpuTarget is not null) return _gpuTarget;
        var drawingSurface = visual.Compositor.CreateDrawingSurface();
        _gpuVisual = visual.Compositor.CreateSurfaceVisual();
        _gpuVisual.Surface = drawingSurface;
        _gpuVisual.Visible = false;
        ElementComposition.SetElementChildVisual(this, _gpuVisual);
        return _gpuTarget = new(interop, drawingSurface);
    }

    /// <summary>Updates GPU frame layout without transferring pixels through managed memory.</summary>
    public void SetGpuFrameSize(PixelSize size)
    {
        Dispatcher.UIThread.VerifyAccess();
        var next = new Size(size.Width, size.Height);
        if (_gpuSize != next) { _gpuSize = next; InvalidateMeasure(); }
        if (_gpuVisual is not null) _gpuVisual.Visible = true;
        UpdateGpuLayout();
    }

    /// <summary>Switches back to bitmap presentation, retaining outstanding compositor resources.</summary>
    public void HideGpuFrame()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_gpuVisual is not null) _gpuVisual.Visible = false;
        _gpuSize = default;
        InvalidateMeasure();
    }

    private void UpdateGpuLayout()
    {
        if (_gpuVisual is null || _gpuSize.Width <= 0) return;
        var size = Stretch.CalculateSize(Bounds.Size, _gpuSize);
        _gpuVisual.Size = new(size.Width, size.Height);
        _gpuVisual.Offset = new((Bounds.Width - size.Width) / 2, (Bounds.Height - size.Height) / 2, 0);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty || change.Property == StretchProperty) UpdateGpuLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ++_attachmentGeneration;
        ElementComposition.SetElementChildVisual(this, null);
        _gpuTarget?.Surface.Dispose();
        _gpuTarget = null;
        _gpuVisual = null;
        base.OnDetachedFromVisualTree(e);
    }
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
    {
        Child?.Measure(availableSize);
        return _gpuSize.Width > 0 ? Stretch.CalculateSize(availableSize, _gpuSize)
            : Child is not null ? Child.DesiredSize
            : Source is { } source
            ? Stretch.CalculateSize(availableSize, source.Size)
            : default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not null)
        {
            var size = _gpuSize.Width > 0 ? Stretch.CalculateSize(finalSize, _gpuSize) : finalSize;
            var rendered = size;
            if (_gpuControlPixelLimit.Width > 0 && size.Width > 0 && size.Height > 0)
            {
                var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
                var ratio = Math.Min(1, Math.Min(_gpuControlPixelLimit.Width / (size.Width * scaling),
                    _gpuControlPixelLimit.Height / (size.Height * scaling)));
                rendered = new Size(size.Width * ratio, size.Height * ratio);
            }
            Child.RenderTransformOrigin = RelativePoint.TopLeft;
            Child.RenderTransform = rendered.Width > 0 && rendered.Height > 0 && rendered != size
                ? new ScaleTransform(size.Width / rendered.Width, size.Height / rendered.Height) : null;
            Child.Arrange(new Rect((finalSize.Width - size.Width) / 2,
                (finalSize.Height - size.Height) / 2, rendered.Width, rendered.Height));
        }
        return finalSize;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var source = Source;
        if (source is null || Child is not null) return;

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
