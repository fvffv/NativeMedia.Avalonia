using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.OpenGL;
using global::Avalonia.OpenGL.Controls;
using global::Avalonia.Threading;

namespace NativeMedia.Avalonia.Linux;

// Render API writes directly into Avalonia's GPU framebuffer. The child is an
// ordinary composited control: clipping, overlays and fullscreen remain Avalonia's.
internal sealed class MpvVideoSession : IDisposable
{
    private readonly object _clientGate = new();
    private readonly VideoFrameSurface _surface;
    private readonly GlVideoControl _control;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private nint _handle;
    private int _disposed, _failed;
    private int _destroyQueued;
    private double _position, _duration;
    private int _width, _height;
    private string _decoder = "pending";
    private long _rendered, _busy;
    private double _renderMs;
    private VideoPlaybackQuality _quality;
    private bool _eofNotified;
    public event EventHandler<GpuVideoFramePresentedEventArgs>? Presented;
    public event Action? Ended;
    public event Action<Exception>? Failed;
    public double Position => Volatile.Read(ref _position);
    public double Duration => Volatile.Read(ref _duration);
    public VideoPlaybackStatistics Statistics => new("libmpv / OpenGL; hwdec=" + _decoder,
        Interlocked.Read(ref _rendered), Interlocked.Read(ref _rendered), Interlocked.Read(ref _busy),
        Volatile.Read(ref _renderMs), _decoder == "no" ? "Hardware decoding unavailable; GPU presentation with software decoding." : null);

    public MpvVideoSession(VideoFrameSurface surface)
    {
        _surface = surface;
        _control = new(this);
        _handle = MpvNative.Create();
        if (_handle == 0) throw new InvalidOperationException("mpv_create failed.");
        try
        {
            foreach (var (name, value) in new[] { ("config", "no"), ("terminal", "no"), ("vo", "libmpv"),
                         ("hwdec", "auto-safe"), ("pause", "yes"), ("idle", "yes"), ("keep-open", "yes") })
                MpvNative.Check(MpvNative.Option(_handle, name, value));
            MpvNative.Check(MpvNative.Initialize(_handle));
        }
        catch { MpvNative.Destroy(_handle); _handle = 0; throw; }
        _ = Task.Run(PollAsync);
    }

    public async Task OpenAsync(string source, CancellationToken token)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Volatile.Read(ref _disposed) == 0) _surface.SetGpuControl(_control);
        });
        await _control.Ready.WaitAsync(TimeSpan.FromSeconds(8), token);
        Command("loadfile", source, "replace");
        await _loaded.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
    }
    public void Play()
    {
        if (_eofNotified || (Duration > 0 && Position >= Duration)) Seek(TimeSpan.Zero);
        Command("set", "pause", "no");
    }
    public void Pause() => Command("set", "pause", "yes");
    public void Stop() { Pause(); Seek(TimeSpan.Zero); }
    public void Seek(TimeSpan position)
    {
        Volatile.Write(ref _position, Math.Max(0, position.TotalSeconds));
        Command("seek", Math.Max(0, position.TotalSeconds).ToString("R", CultureInfo.InvariantCulture), "absolute+exact");
    }
    public void SetVolume(double value) => Command("set", "volume", (Math.Clamp(value, 0, 1) * 100).ToString("R", CultureInfo.InvariantCulture));
    public void SetMuted(bool value) => Command("set", "mute", value ? "yes" : "no");
    public void SetQuality(VideoPlaybackQuality quality)
    {
        _quality = quality;
        Dispatcher.UIThread.Post(() =>
        {
            if (Volatile.Read(ref _disposed) != 0 || !ReferenceEquals(_surface.Child, _control)) return;
            var profile = VideoPlaybackProfile.FromQuality(quality);
            var width = Volatile.Read(ref _width);
            var height = Volatile.Read(ref _height);
            _surface.SetGpuControlPixelLimit(profile.MaximumLongEdge > 0 && width > 0 && height > 0
                ? profile.GetOutputSize(width, height) : default);
            _control.RequestNextFrameRendering();
        });
    }
    private void Command(params string[] args)
    {
        lock (_clientGate)
            if (_handle != 0 && Volatile.Read(ref _disposed) == 0) MpvNative.Command(_handle, args);
    }

    private async Task PollAsync()
    {
        try
        {
            while (Volatile.Read(ref _disposed) == 0)
            {
                bool loaded = false, ended = false;
                Exception? error = null;
                lock (_clientGate)
                {
                    if (_handle == 0 || Volatile.Read(ref _disposed) != 0) return;
                    for (var i = 0; i < 100; i++)
                    {
                        var ev = Marshal.PtrToStructure<MpvNative.Event>(MpvNative.WaitEvent(_handle, 0));
                        if (ev.Id == 0) break;
                        if (ev.Id == 8) loaded = true; // MPV_EVENT_FILE_LOADED
                        if (ev.Id == 7 && ev.Data != 0)
                        {
                            var end = Marshal.PtrToStructure<MpvNative.EndFile>(ev.Data);
                            if (end.Error < 0) error = new InvalidOperationException($"libmpv playback failed ({end.Error}).");
                            else if (end.Reason == 0) ended = true;
                        }
                        if (ev.Id == 5 && ev.Error < 0) error = new InvalidOperationException($"libmpv command failed ({ev.Error}).");
                    }
                    Volatile.Write(ref _position, Math.Max(0, MpvNative.Number(_handle, "time-pos")));
                    Volatile.Write(ref _duration, Math.Max(0, MpvNative.Number(_handle, "duration")));
                    Volatile.Write(ref _width, (int)MpvNative.Number(_handle, "dwidth"));
                    Volatile.Write(ref _height, (int)MpvNative.Number(_handle, "dheight"));
                    _decoder = MpvNative.Text(_handle, "hwdec-current");
                    var eof = MpvNative.Text(_handle, "eof-reached") == "yes";
                    if (eof && !_eofNotified) ended = true;
                    _eofNotified = eof;
                }
                if (error is not null) { Fail(error); return; }
                if (loaded) _loaded.TrySetResult();
                if (ended) Ended?.Invoke();
                await Task.Delay(40).ConfigureAwait(false);
            }
        }
        catch (Exception ex) { if (Volatile.Read(ref _disposed) == 0) Fail(ex); }
    }
    private void Fail(Exception error)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0 || Volatile.Read(ref _disposed) != 0) return;
        _loaded.TrySetException(error);
        _control.FailReady(error);
        Failed?.Invoke(error);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_clientGate)
            if (_handle != 0) { try { MpvNative.Command(_handle, "stop"); } catch { } }
        _loaded.TrySetCanceled();
        Dispatcher.UIThread.Post(() =>
        {
            // Detach calls OnOpenGlDeinit with the SAME context current before
            // destroying the player. Never free a live render context on a worker.
            if (ReferenceEquals(_surface.Child, _control)) _surface.SetGpuControl(null);
            _control.DestroyIfUninitialized();
        });
    }
    private void DestroyPlayer()
    {
        if (Interlocked.Exchange(ref _destroyQueued, 1) != 0) return;
        _ = Task.Run(() =>
        {
            nint handle;
            lock (_clientGate) { handle = _handle; _handle = 0; }
            if (handle != 0) MpvNative.Destroy(handle);
        });
    }

    private sealed class GlVideoControl(MpvVideoSession owner) : OpenGlControlBase
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private nint _render;
        private MpvNative.GetProc? _getProc;
        private MpvNative.Update? _update;
        private int _queued;
        private long _lastFrame;
        private nint _nativeDisplay;
        private int _displayKind;
        public Task Ready => _ready.Task;
        public void FailReady(Exception error) => _ready.TrySetException(error);

        protected override unsafe void OnOpenGlInit(GlInterface gl)
        {
            if (Volatile.Read(ref owner._disposed) != 0) { owner.DestroyPlayer(); return; }
            try
            {
                _getProc = (_, name) =>
                {
                    try { return gl.GetProcAddress(Marshal.PtrToStringUTF8(name)!); }
                    catch { return 0; } // Never unwind a managed exception through native code.
                };
                _update = _ => QueueRender();
                var init = new MpvNative.InitParams { GetProcAddress = Marshal.GetFunctionPointerForDelegate(_getProc) };
                var api = Marshal.StringToCoTaskMemUTF8("opengl");
                try
                {
                    OpenNativeDisplay();
                    var args = stackalloc MpvNative.Param[] { new(1, api), new(2, (nint)(&init)),
                        new(_nativeDisplay != 0 ? _displayKind : 0, _nativeDisplay), new(0, 0) };
                    MpvNative.Check(MpvNative.RenderCreate(out _render, owner._handle, args));
                }
                finally { Marshal.FreeCoTaskMem(api); }
                MpvNative.SetUpdateCallback(_render, _update, 0);
                _ready.TrySetResult();
            }
            catch (Exception ex) { FailReady(ex); owner.Fail(ex); }
        }
        private void QueueRender()
        {
            if (Volatile.Read(ref owner._disposed) != 0) return;
            if (Interlocked.Exchange(ref _queued, 1) != 0) { Interlocked.Increment(ref owner._busy); return; }
            _ = ScheduleRenderAsync();
        }
        private async Task ScheduleRenderAsync()
        {
            var profile = VideoPlaybackProfile.FromQuality(owner._quality);
            var previous = Interlocked.Read(ref _lastFrame);
            if (profile.MaximumFrameRate > 0 && previous > 0)
            {
                var delay = 1d / profile.MaximumFrameRate - Stopwatch.GetElapsedTime(previous).TotalSeconds;
                if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay)).ConfigureAwait(false);
            }
            Dispatcher.UIThread.Post(() =>
            {
                Volatile.Write(ref _queued, 0);
                if (Volatile.Read(ref owner._disposed) == 0) RequestNextFrameRendering();
            }, DispatcherPriority.Render);
        }
        protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
        {
            if (_render == 0 || Volatile.Read(ref owner._disposed) != 0 || Volatile.Read(ref owner._failed) != 0) return;
            try
            {
                var now = Stopwatch.GetTimestamp();
                var profile = VideoPlaybackProfile.FromQuality(owner._quality);
                var updated = (MpvNative.RenderUpdate(_render) & 1) != 0;
                var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
                var fbo = new MpvNative.Fbo { Id = fb, Width = Math.Max(1, (int)(Bounds.Width * scale)), Height = Math.Max(1, (int)(Bounds.Height * scale)) };
                int flip = 1, block = 0;
                var args = stackalloc MpvNative.Param[] { new(3, (nint)(&fbo)), new(4, (nint)(&flip)), new(12, (nint)(&block)), new(0, 0) };
                // No synchronous mpv client calls here: only the render API is
                // allowed while the GL context is current (avoids core deadlocks).
                MpvNative.Check(MpvNative.Render(_render, args));
                Volatile.Write(ref owner._renderMs, Stopwatch.GetElapsedTime(now).TotalMilliseconds);
                if (!updated) return;
                Interlocked.Exchange(ref _lastFrame, now);
                Interlocked.Increment(ref owner._rendered);
                var width = Volatile.Read(ref owner._width);
                var height = Volatile.Read(ref owner._height);
                if (width > 0 && height > 0)
                {
                    owner._surface.SetGpuFrameSize(new(width, height));
                    owner._surface.SetGpuControlPixelLimit(profile.MaximumLongEdge > 0 ? profile.GetOutputSize(width, height) : default);
                }
                owner.Presented?.Invoke(owner, new(TimeSpan.FromSeconds(owner.Position), now));
            }
            catch (Exception ex) { owner.Fail(ex); }
        }
        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            if (_render != 0)
            {
                MpvNative.SetUpdateCallback(_render, null, 0);
                MpvNative.RenderFree(_render);
                _render = 0;
            }
            CloseNativeDisplay();
            owner.DestroyPlayer();
            if (Volatile.Read(ref owner._disposed) == 0) owner.Fail(new InvalidOperationException("OpenGL video surface was detached."));
        }
        protected override void OnOpenGlLost()
        {
            // Stop issuing frames immediately; release the native session as well.
            if (_render != 0) { MpvNative.SetUpdateCallback(_render, null, 0); MpvNative.RenderFree(_render); _render = 0; }
            CloseNativeDisplay();
            owner.DestroyPlayer();
            owner.Fail(new InvalidOperationException("OpenGL video context was lost."));
        }
        public void DestroyIfUninitialized() { if (_render == 0) { CloseNativeDisplay(); owner.DestroyPlayer(); } }

        private void OpenNativeDisplay()
        {
            // A separate connection to the same display is owned by this renderer.
            // libva needs the display resource for EGL hardware-frame interop.
            var descriptor = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.HandleDescriptor;
            try
            {
                if (descriptor == "XID" || (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
                    && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))))
                { _displayKind = 8; _nativeDisplay = XOpenDisplay(0); }
                else if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                { _displayKind = 9; _nativeDisplay = wl_display_connect(0); }
            }
            catch (DllNotFoundException) { _nativeDisplay = 0; }
        }
        private void CloseNativeDisplay()
        {
            if (_nativeDisplay == 0) return;
            if (_displayKind == 8) XCloseDisplay(_nativeDisplay);
            else wl_display_disconnect(_nativeDisplay);
            _nativeDisplay = 0;
        }
        [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint name);
        [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
        [DllImport("libwayland-client.so.0")] private static extern nint wl_display_connect(nint name);
        [DllImport("libwayland-client.so.0")] private static extern void wl_display_disconnect(nint display);
    }
}
