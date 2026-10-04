using global::Avalonia;
using global::Avalonia.Rendering.Composition;

namespace NativeMedia.Avalonia;

/// <summary>Optional GPU presentation contract. CPU bitmap consumers remain supported.</summary>
public interface IGpuVideoBackend
{
    void SetPresentationSurface(VideoFrameSurface? surface);
    event EventHandler<GpuVideoFramePresentedEventArgs>? GpuFramePresented;
    VideoPlaybackStatistics PlaybackStatistics { get; }
}

/// <summary>A frame copied by the compositor, not a guarantee of physical monitor scan-out.</summary>
public sealed class GpuVideoFramePresentedEventArgs(TimeSpan position, long capturedTimestamp) : EventArgs
{
    public TimeSpan Position { get; } = position;
    public long CapturedTimestamp { get; } = capturedTimestamp;
}

/// <summary>Snapshot counters for one source. Decoder acceleration depends on codec and driver.</summary>
public sealed record VideoPlaybackStatistics(
    string RenderingPath, long SubmittedFrames, long CompositedFrames,
    long PresentationBusyCount, double LastTransferMilliseconds, string? FallbackReason);

/// <summary>Owned by the Avalonia video surface; native textures must outlive pending updates.</summary>
public sealed record GpuVideoTarget(ICompositionGpuInterop Interop, CompositionDrawingSurface Surface);
