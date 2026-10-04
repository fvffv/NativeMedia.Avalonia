using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using global::Avalonia.Platform;
using global::Avalonia.Rendering.Composition;
using global::Avalonia.Threading;

namespace NativeMedia.Avalonia.macOS;

// Retaining the CVPixelBuffer keeps AVFoundation's decoder pool from recycling
// its IOSurface until Avalonia has finished both the update and import disposal.
internal sealed class IosurfaceVideoPresenter(VideoFrameSurface surface, GpuVideoTarget target) : IDisposable
{
    private int _pending, _generation, _disposed;
    private long _submitted, _composited, _busy;
    private string? _failure;
    public event EventHandler<GpuVideoFramePresentedEventArgs>? Presented;
    public event Action<string>? Failed;
    public VideoPlaybackStatistics Statistics => new("AVFoundation / IOSurface shared image",
        Interlocked.Read(ref _submitted), Interlocked.Read(ref _composited), Interlocked.Read(ref _busy), 0, _failure);

    public static bool IsSupported(GpuVideoTarget target)
        => target.Interop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef)
           && target.Interop.GetSynchronizationCapabilities(KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef)
               .HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.Automatic);

    // true also means backpressure: do not switch to costly CPU copies when busy.
    public bool TryPresent(nint pixelBuffer, PixelSize size, TimeSpan position)
    {
        if (Volatile.Read(ref _disposed) != 0 || _failure is not null) return false;
        var iosurface = CVPixelBufferGetIOSurface(pixelBuffer);
        if (iosurface == 0) { Fail("AVFoundation did not supply an IOSurface-backed pixel buffer."); return false; }
        if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
        {
            Interlocked.Increment(ref _busy);
            return true;
        }
        CFRetain(pixelBuffer);
        var generation = Volatile.Read(ref _generation);
        var captured = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref _submitted);
        Dispatcher.UIThread.Post(() => Present(pixelBuffer, iosurface, size, position, captured, generation), DispatcherPriority.Render);
        return true;
    }

    private async void Present(nint pixelBuffer, nint iosurface, PixelSize size, TimeSpan position, long captured, int generation)
    {
        ICompositionImportedGpuImage? imported = null;
        try
        {
            if (Volatile.Read(ref _disposed) != 0 || generation != Volatile.Read(ref _generation)) return;
            if (target.Interop.IsLost) throw new InvalidOperationException("Avalonia GPU device was lost.");
            imported = target.Interop.ImportImage(new PlatformHandle(iosurface, KnownPlatformGraphicsExternalImageHandleTypes.IOSurfaceRef),
                new PlatformGraphicsExternalImageProperties
                {
                    Width = size.Width, Height = size.Height,
                    Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm, TopLeftOrigin = true
                });
            await target.Surface.UpdateAsync(imported);
            if (Volatile.Read(ref _disposed) != 0 || generation != Volatile.Read(ref _generation)) return;
            Interlocked.Increment(ref _composited);
            surface.SetGpuFrameSize(size);
            Presented?.Invoke(this, new(position, captured));
        }
        catch (Exception ex) { Fail(ex.Message); }
        finally
        {
            try { if (imported is not null) await imported.DisposeAsync(); }
            catch (Exception ex) { Fail(ex.Message); }
            finally { CFRelease(pixelBuffer); Volatile.Write(ref _pending, 0); }
        }
    }

    private void Fail(string message)
    {
        if (Interlocked.CompareExchange(ref _failure, message, null) is not null) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            surface.HideGpuFrame();
            Failed?.Invoke(message);
        });
    }
    public void Invalidate() => Interlocked.Increment(ref _generation);
    public void Dispose() { Interlocked.Exchange(ref _disposed, 1); Invalidate(); }

    [DllImport("/System/Library/Frameworks/CoreVideo.framework/CoreVideo")]
    private static extern nint CVPixelBufferGetIOSurface(nint pixelBuffer);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFRetain(nint value);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(nint value);
}
