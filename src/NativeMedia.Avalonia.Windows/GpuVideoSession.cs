using System.Diagnostics;
using Avalonia;
using global::Avalonia.Platform;
using global::Avalonia.Rendering.Composition;
using global::Avalonia.Threading;

namespace NativeMedia.Avalonia.Windows;

internal sealed class GpuVideoSession : IDisposable
{
    private readonly object _gate = new();
    private readonly MediaEngineNative _native;
    private readonly VideoFrameSurface _surface;
    private readonly GpuVideoTarget _target;
    private readonly List<Slot> _slots = new(3);
    private bool _disposed;
    private bool _playing;
    private bool _failed;
    private int _workerQueued;
    private int _forceFrame;
    private long _submitted, _composited, _busy;
    private long _lastPts = long.MinValue;
    private long _lastOutput;
    private int _generation;
    private double _transferMs;
    private VideoPlaybackQuality _quality;
    public event EventHandler<GpuVideoFramePresentedEventArgs>? Presented;
    public event Action<Exception>? Failed;

    public GpuVideoSession(VideoFrameSurface surface, GpuVideoTarget target, string source)
    {
        _surface = surface; _target = target;
        if (!target.Interop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle)
            || !target.Interop.GetSynchronizationCapabilities(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle)
                .HasFlag(CompositionGpuImportedImageSynchronizationCapabilities.KeyedMutex))
            throw new NotSupportedException("The Avalonia GPU backend does not support keyed-mutex D3D11 texture import.");
        _native = new MediaEngineNative(target.Interop.DeviceLuid ?? [], source);
        _native.FrameNeeded += () => RequestFrame(force: true);
    }

    public Task Loaded => _native.Loaded;
    public double Position { get { lock (_gate) return _disposed ? 0 : _native.Position; } }
    public double Duration { get { lock (_gate) return _disposed ? 0 : _native.Duration; } }
    public bool HasEnded { get { lock (_gate) return !_disposed && _native.Ended; } }
    public VideoPlaybackStatistics Statistics
    {
        get { lock (_gate) return new("D3D11 shared texture / Media Engine", _submitted, _composited, _busy, _transferMs, null); }
    }
    public void Play() { lock (_gate) { if (_disposed) return; _native.Play(); _playing = true; } }
    public void Pause() { lock (_gate) { if (_disposed) return; _native.Pause(); _playing = false; } }
    public void Stop() { lock (_gate) { if (_disposed) return; _native.Pause(); _native.Seek(0); _playing = false; ++_generation; _lastPts = long.MinValue; } }
    public void Seek(TimeSpan position) { lock (_gate) { if (_disposed) return; ++_generation; _lastPts = long.MinValue; _lastOutput = 0; _native.Seek(Math.Max(0, position.TotalSeconds)); } }
    public void SetVolume(double value) { lock (_gate) { if (!_disposed) _native.SetVolume(value); } }
    public void SetMuted(bool value) { lock (_gate) { if (!_disposed) _native.SetMuted(value); } }
    public void SetQuality(VideoPlaybackQuality quality) { lock (_gate) { _quality = quality; _lastOutput = 0; } }

    public void RequestFrame(bool force = false)
    {
        if (force) Volatile.Write(ref _forceFrame, 1);
        if (Interlocked.Exchange(ref _workerQueued, 1) != 0) return;
        // Neither decoding nor GPU mutex waits run in an Avalonia UI/render callback.
        _ = Task.Run(() =>
        {
            try { Pump(Interlocked.Exchange(ref _forceFrame, 0) != 0); }
            catch (Exception ex) { Fail(ex); }
            finally
            {
                Volatile.Write(ref _workerQueued, 0);
                // A seek-complete notification must not be lost behind an already queued display tick.
                if (Volatile.Read(ref _forceFrame) != 0) RequestFrame();
            }
        });
    }

    private void Pump(bool force)
    {
        lock (_gate)
        {
            if (_disposed || _failed || (!_playing && !force)) return;
            if (_target.Interop.IsLost) throw new InvalidOperationException("The Avalonia GPU device was lost.");
            if (_native.ErrorCode != 0) throw new InvalidOperationException($"Media Engine failed: 0x{_native.ErrorCode:X8}.");
            var profile = VideoPlaybackProfile.FromQuality(_quality);
            var now = Stopwatch.GetTimestamp();
            if (profile.MaximumFrameRate > 0 && _lastOutput != 0
                && Stopwatch.GetElapsedTime(_lastOutput, now).TotalSeconds < 1d / profile.MaximumFrameRate) return;
            var (width, height) = _native.GetVideoSize();
            if (width <= 0 || height <= 0) return;
            var size = profile.GetOutputSize(width, height);
            if (_slots.Count > 0 && (_slots[0].Texture.Width != size.Width || _slots[0].Texture.Height != size.Height))
            {
                foreach (var previous in _slots) Retire(previous);
                _slots.Clear();
            }
            var slot = _slots.FirstOrDefault(s => s.Completion.Task.IsCompletedSuccessfully);
            if (slot is null && _slots.Count < 3)
            {
                slot = new Slot(_native.CreateTexture(size.Width, size.Height));
                _slots.Add(slot);
            }
            // Never overwrite a texture still being read by the compositor, or grow an unbounded queue.
            if (slot is null || !slot.Texture.TryAcquire())
            {
                ++_busy;
                if (_lastOutput != 0 && Stopwatch.GetElapsedTime(_lastOutput, now) > TimeSpan.FromSeconds(5))
                    throw new TimeoutException("The compositor stopped consuming shared video textures.");
                return;
            }
            bool released = false;
            try
            {
                if (!_native.TryGetFrame(out var pts) || pts == _lastPts) return;
                var started = Stopwatch.GetTimestamp();
                _native.Transfer(slot.Texture);
                _transferMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                slot.Texture.Release(1);
                released = true;
                _lastPts = pts; _lastOutput = now; ++_submitted;
                slot.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                var generation = _generation;
                var captured = now;
                Dispatcher.UIThread.Post(() => Present(slot, size, pts, captured, generation), DispatcherPriority.Render);
            }
            finally { if (!released) slot.Texture.Release(0); }
        }
    }

    private async void Present(Slot slot, PixelSize size, long pts, long captured, int generation)
    {
        try
        {
            // Even an old seek generation must finish its mutex handoff before reusing this slot.
            lock (_gate) { if (_disposed) return; }
            slot.Imported ??= _target.Interop.ImportImage(
                new PlatformHandle(slot.Texture.Handle, KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),
                new PlatformGraphicsExternalImageProperties
                {
                    Width = size.Width, Height = size.Height,
                    Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,
                    TopLeftOrigin = true
                });
            var update = _target.Surface.UpdateWithKeyedMutexAsync(slot.Imported, 1, 0);
            await update;
            lock (_gate)
            {
                if (_disposed || generation != _generation) return;
                ++_composited;
            }
            _surface.SetGpuFrameSize(size);
            Presented?.Invoke(this, new(TimeSpan.FromTicks(Math.Max(0, pts)), captured));
        }
        catch (Exception ex) { Fail(ex); }
        finally { slot.Completion.TrySetResult(); }
    }

    private void Fail(Exception error)
    {
        lock (_gate) { if (_disposed || _failed) return; _failed = true; }
        Dispatcher.UIThread.Post(() => Failed?.Invoke(error));
    }
    private static async void Retire(Slot slot)
    {
        // Do not block UI shutdown waiting for the compositor. Its update task owns the resource lifetime.
        await slot.Completion.Task.ConfigureAwait(false);
        try { if (slot.Imported is not null) await slot.Imported.DisposeAsync().ConfigureAwait(false); }
        catch { /* A lost compositor can reject cleanup; the local COM references still need releasing. */ }
        finally { slot.Texture.Dispose(); }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; ++_generation;
            _native.Dispose();
            foreach (var slot in _slots) Retire(slot);
            _slots.Clear();
        }
    }
    private sealed class Slot(SharedVideoTexture texture)
    {
        public SharedVideoTexture Texture { get; } = texture;
        public ICompositionImportedGpuImage? Imported;
        public TaskCompletionSource Completion = Completed();
        private static TaskCompletionSource Completed() { var task = new TaskCompletionSource(); task.SetResult(); return task; }
    }
}
