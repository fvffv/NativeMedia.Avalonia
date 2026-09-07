using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Diagnostics;
using NativeMedia.Avalonia;
using Avalonia;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Threading;

namespace NativeMedia.Avalonia.Windows;

/// <summary>Media Foundation MFPlay adapter. It uses raw COM vtable calls, which are NativeAOT-safe.</summary>
[SupportedOSPlatform("windows6.1")]
public sealed class WindowsMediaFoundationBackend : NativeMediaBackend, IVideoMediaBackend, IVideoQualityMediaBackend, IVideoFrameClockBackend
{
    private nint _player;
    private nint _videoOutput;
    private nint _captureWindow;
    private Timer? _captureTimer;
    private long _captureNotBefore;
    private byte[] _captureBuffer = [];
    private int _captureWidth = 1;
    private int _captureHeight = 1;
    private VideoPlaybackQuality _playbackQuality = VideoPlaybackQuality.Original;
    private bool _externalFrameClock;
    private int _captureQueued;
    private int _captureGeneration;
    private long _lastCaptureTimestamp;
    private bool _mfStarted;
    private bool _comInitialized;
    private string? _source;
    private Timer? _durationPoller;
    private bool _videoOutputEnabled = true;
    private static readonly Guid PositionType100Ns = Guid.Empty;

    public WindowsMediaFoundationBackend() : base("Windows Media Foundation", OperatingSystem.IsWindows()) { }
    public override bool RequiresUiThreadOpen => _videoOutputEnabled;
    public void ConfigureVideoOutput(bool enabled) => _videoOutputEnabled = enabled;
    /// <inheritdoc />
    public Task SetPlaybackQualityAsync(VideoPlaybackQuality quality, CancellationToken cancellationToken = default)
    {
        _ = VideoPlaybackProfile.FromQuality(quality);
        cancellationToken.ThrowIfCancellationRequested();
        _playbackQuality = quality;
        _lastCaptureTimestamp = 0;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void SetExternalFrameClock(bool enabled)
    {
        _externalFrameClock = enabled;
        _captureTimer?.Change(enabled ? Timeout.Infinite : 0, 1);
    }

    /// <inheritdoc />
    public void RequestVideoFrame()
    {
        if (!_videoOutputEnabled || _player == 0 || Interlocked.Exchange(ref _captureQueued, 1) != 0) return;
        var generation = _captureGeneration;
        // HWND resizing/capture stays on its owning thread, outside Avalonia's
        // render pass. Only one request may be pending at a time.
        Dispatcher.UIThread.Post(() =>
        {
            try { if (generation == _captureGeneration) CaptureVideoFrame(); }
            finally { Volatile.Write(ref _captureQueued, 0); }
        }, DispatcherPriority.Background);
    }

    public override async Task OpenAsync(string source, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) { await base.OpenAsync(source, cancellationToken); return; }
        try
        {
            CloseNativePlayer();
            _source = source;
            _comInitialized = NativeMethods.CoInitializeEx(IntPtr.Zero, _videoOutputEnabled ? 2u : 0u) >= 0;
            if (NativeMethods.MFStartup(0x00020070, 0) < 0) { RaiseError("Media Foundation initialization failed."); return; }
            _mfStarted = true;
            _captureWidth = _captureHeight = 1;
            _captureBuffer = [];
            _lastCaptureTimestamp = 0;
            _captureWindow = _videoOutputEnabled ? NativeMethods.CreateCaptureWindow(1, 1) : 0;
            var hr = CreateNativePlayer(source);
            if (hr < 0 || _player == 0) { RaiseError($"Media Foundation could not open the media (HRESULT 0x{hr:X8})."); return; }
            await base.OpenAsync(source, cancellationToken);
            TryReadDuration();
            RaisePositionChanged();
            if (Duration <= TimeSpan.Zero) _durationPoller = new Timer(_ => TryReadDuration(), null, 200, 250);
            if (_videoOutputEnabled)
            {
                // Controls use the host display clock (including high-refresh
                // displays); this timer is only for standalone backend callers.
                _captureTimer = new Timer(_ => RequestVideoFrame(), null, _externalFrameClock ? Timeout.Infinite : 100, 1);
                _captureNotBefore = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3 / 4;
            }
        }
        catch (DllNotFoundException ex) { RaiseError("Media Foundation is not available.", ex); }
    }

    public override Task PlayAsync(CancellationToken cancellationToken = default) { InvokeVoid(3); return base.PlayAsync(cancellationToken); }
    public override Task PauseAsync(CancellationToken cancellationToken = default) { InvokeVoid(4); return base.PauseAsync(cancellationToken); }
    public override Task StopAsync(CancellationToken cancellationToken = default) { InvokeVoid(5); return base.StopAsync(cancellationToken); }
    public override unsafe void Seek(TimeSpan position)
    {
        if (_player != 0)
        {
            var value = new RawPropVariant { Type = 20, HValue = Math.Max(0, position.Ticks) };
            var type = PositionType100Ns;
            InvokeSetPosition(&type, &value);
        }
        base.Seek(position);
    }
    protected override unsafe TimeSpan QueryPosition()
    {
        if (_player == 0) return base.QueryPosition();
        try
        {
            var value = new RawPropVariant();
            var type = PositionType100Ns;
            var hr = InvokeGetPosition(&type, &value);
            return hr >= 0 ? TimeSpan.FromTicks(Math.Max(0, value.HValue)) : base.QueryPosition();
        }
        catch { return base.QueryPosition(); }
    }
    private unsafe void TryReadDuration()
    {
        try
        {
            var value = new RawPropVariant();
            var type = PositionType100Ns;
            InvokeGetDuration(&type, &value);
            if (value.HValue > 0)
            {
                Duration = TimeSpan.FromTicks(value.HValue);
                _durationPoller?.Dispose();
                _durationPoller = null;
                RaisePositionChanged();
            }
        }
        catch { }
    }
    public override double Volume { get => base.Volume; set { base.Volume = value; if (_player != 0) InvokeFloat(20, (float)value); } }
    public override bool Muted { get => base.Muted; set { base.Muted = value; if (_player != 0) InvokeBool(24, value); } }
    public override void SetVideoOutput(nint handle)
    {
        if (!_videoOutputEnabled) return;
        if (handle == _videoOutput) return;
        var hadOutput = _videoOutput != 0;
        _videoOutput = handle;
        // MFPCreateMediaPlayer receives the target HWND only at creation time.
        // If the Avalonia native surface appears after Source, recreate the player
        // against the real child window so video is rendered instead of audio-only.
        if (!hadOutput && handle != 0 && _player != 0 && _source is not null && _mfStarted)
        {
            try { InvokeVoid(38); InvokeVoid(2); } catch { }
            _player = 0;
            var hr = CreateNativePlayer(_source);
            if (hr >= 0 && _player != 0)
            {
                TryReadDuration();
                if (State == MediaState.Playing) InvokeVoid(3);
            }
            else RaiseError($"Media Foundation could not attach the video surface (HRESULT 0x{hr:X8}).");
        }
    }
    private int CreateNativePlayer(string source)
    {
        var uri = Uri.TryCreate(source, UriKind.Absolute, out var existing) && existing.Scheme is "http" or "https" or "file" ? source : new Uri(Path.GetFullPath(source)).AbsoluteUri;
        // Audio runs on a background MTA without a Win32 message loop. MFPlay's
        // default dispatch requires such a loop even when no callback is supplied.
        const int FreeThreadedCallback = 1;
        var result = NativeMethods.MFPCreateMediaPlayer(uri, 0,
            _videoOutputEnabled ? 0 : FreeThreadedCallback, 0, _captureWindow, out _player);
        if (_captureWindow != 0) NativeMethods.KeepCaptureWindow(_captureWindow, _captureWidth, _captureHeight);
        return result;
    }

    private unsafe bool RefreshVideoSize()
    {
        if (_player == 0 || _captureWindow == 0) return false;
        NativeSize source = default;
        var table = *(nint**)_player;
        var hr = ((delegate* unmanaged[Stdcall]<nint, NativeSize*, NativeSize*, int>)table[25])(_player, &source, null);
        if (hr < 0 || source.Width <= 0 || source.Height <= 0) return false;
        var size = VideoPlaybackProfile.FromQuality(_playbackQuality).GetOutputSize(source.Width, source.Height);
        if (_captureWidth == size.Width && _captureHeight == size.Height && _captureBuffer.Length > 0) return true;
        _captureBuffer = new byte[checked(size.Width * size.Height * 4)];
        _captureWidth = size.Width;
        _captureHeight = size.Height;
        NativeMethods.KeepCaptureWindow(_captureWindow, _captureWidth, _captureHeight);
        InvokeVoid(32); // IMFPMediaPlayer.UpdateVideo after changing the target HWND size.
        return true;
    }

    private void CaptureVideoFrame()
    {
        if (_captureWindow == 0 || _player == 0) return;
        if (State != MediaState.Playing || Position <= TimeSpan.Zero) return;
        if (Stopwatch.GetTimestamp() < _captureNotBefore) return;
        try
        {
            var now = Stopwatch.GetTimestamp();
            var maximumFrameRate = VideoPlaybackProfile.FromQuality(_playbackQuality).MaximumFrameRate;
            if (maximumFrameRate > 0 && _lastCaptureTimestamp != 0
                && Stopwatch.GetElapsedTime(_lastCaptureTimestamp, now).TotalSeconds < 1d / maximumFrameRate - 0.001)
                return;
            if (!RefreshVideoSize()) return;
            var frame = NativeMethods.CaptureWindow(_captureWindow, _captureWidth, _captureHeight, _captureBuffer);
            if (frame is not null)
            {
                var hash = ComputeFrameHash(_captureBuffer);
                if (IsBlankCapture(_captureBuffer))
                {
                    frame.Dispose();
                    return;
                }
                _lastCaptureTimestamp = now;
                RaiseVideoFrame(frame, Position, hash);
            }
        }
        catch { }
    }
    private static ulong ComputeFrameHash(ReadOnlySpan<byte> bytes)
    {
        // Sample the full BGRA buffer at a fixed stride. This is inexpensive
        // enough for capture cadence and distinguishes stale pre-seek frames.
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        var step = Math.Max(4, bytes.Length / 2048);
        for (var i = 0; i < bytes.Length; i += step) hash = (hash ^ bytes[i]) * prime;
        return hash;
    }
    private static bool IsBlankCapture(ReadOnlySpan<byte> bytes)
    {
        var step = Math.Max(4, bytes.Length / 1024);
        var first = bytes[0];
        var different = 0;
        for (var i = 0; i < bytes.Length; i += step)
        {
            if (Math.Abs(bytes[i] - first) > 3) different++;
            if (different >= 8) return false;
        }
        return true;
    }
    private unsafe void InvokeVoid(int index) { if (_player == 0) return; var table = *(nint**)_player; ((delegate* unmanaged[Stdcall]<nint, int>)table[index])(_player); }
    private unsafe void InvokeFloat(int index, float value) { var table = *(nint**)_player; ((delegate* unmanaged[Stdcall]<nint, float, int>)table[index])(_player, value); }
    private unsafe void InvokeBool(int index, bool value) { var table = *(nint**)_player; ((delegate* unmanaged[Stdcall]<nint, int, int>)table[index])(_player, value ? 1 : 0); }
    private unsafe void InvokeSetPosition(Guid* type, RawPropVariant* value) { var table = *(nint**)_player; ((delegate* unmanaged[Stdcall]<nint, Guid*, RawPropVariant*, int>)table[7])(_player, type, value); }
    private unsafe int InvokeGetPosition(Guid* type, RawPropVariant* value) { var table = *(nint**)_player; return ((delegate* unmanaged[Stdcall]<nint, Guid*, RawPropVariant*, int>)table[8])(_player, type, value); }
    private unsafe void InvokeGetDuration(Guid* type, RawPropVariant* value) { var table = *(nint**)_player; ((delegate* unmanaged[Stdcall]<nint, Guid*, RawPropVariant*, int>)table[9])(_player, type, value); }
    private void CloseNativePlayer()
    {
        _captureGeneration++;
        _durationPoller?.Dispose();
        _durationPoller = null;
        _captureTimer?.Dispose();
        _captureTimer = null;
        if (_player != 0) { try { InvokeVoid(38); InvokeVoid(2); } catch { } _player = 0; }
        if (_captureWindow != 0) { NativeMethods.DestroyWindow(_captureWindow); _captureWindow = 0; }
        _captureBuffer = [];
    }
    public override void Dispose()
    {
        CloseNativePlayer();
        if (_captureWindow != 0) { NativeMethods.DestroyWindow(_captureWindow); _captureWindow = 0; }
        base.Dispose();
        if (_mfStarted) { NativeMethods.MFShutdown(); _mfStarted = false; }
        if (_comInitialized) { NativeMethods.CoUninitialize(); _comInitialized = false; }
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct RawPropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public long HValue; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
    private static class NativeMethods
    {
        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] public static extern int MFShutdown();
        [DllImport("mfplay.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] public static extern int MFPCreateMediaPlayer(string url, int start, int options, nint callback, nint hwnd, out nint player);
        [DllImport("ole32.dll", ExactSpelling = true)] public static extern int CoInitializeEx(nint reserved, uint coInit);
        [DllImport("ole32.dll", ExactSpelling = true)] public static extern void CoUninitialize();
        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern nint CreateWindowEx(uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
        [DllImport("user32.dll", ExactSpelling = true)] public static extern bool DestroyWindow(nint handle);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern bool ShowWindow(nint handle, int command);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern bool SetWindowPos(nint handle, nint insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern nint GetDC(nint handle);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern int ReleaseDC(nint handle, nint dc);
        [DllImport("user32.dll", ExactSpelling = true)] private static extern bool PrintWindow(nint handle, nint dc, uint flags);
        [DllImport("gdi32.dll", ExactSpelling = true)] private static extern nint CreateCompatibleDC(nint dc);
        [DllImport("gdi32.dll", ExactSpelling = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
        [DllImport("gdi32.dll", ExactSpelling = true)] private static extern nint SelectObject(nint dc, nint obj);
        [DllImport("gdi32.dll", ExactSpelling = true)] private static extern bool DeleteObject(nint obj);
        [DllImport("gdi32.dll", ExactSpelling = true)] private static extern bool DeleteDC(nint dc);
        private const uint WsPopup = 0x80000000, WsVisible = 0x10000000, SsBlackRect = 0x00000004, ExToolWindow = 0x80, ExNoActivate = 0x08000000, SwpNoActivate = 0x10, SwpShowWindow = 0x40;
        private const int SwShownoactivate = 4;
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfoHeader { public uint Size; public int Width, Height; public ushort Planes, BitsPerPixel; public uint Compression, ImageSize, XPelsPerMeter, YPelsPerMeter, ColorsUsed, ColorsImportant; }
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public BitmapInfoHeader Header; }
        public static nint CreateCaptureWindow(int width, int height)
        {
            // MFPlay still needs a real HWND, but it must never become a visible
            // top-level player window. Keep it outside the virtual desktop and
            // copy its rendered frames into Avalonia's normal scene.
            var h = CreateWindowEx(ExToolWindow | ExNoActivate, "STATIC", null, WsPopup | WsVisible | SsBlackRect, -32000, -32000, width, height, 0, 0, 0, 0);
            if (h != 0) { ShowWindow(h, SwShownoactivate); SetWindowPos(h, 1, -32000, -32000, width, height, (uint)(SwpNoActivate | SwpShowWindow)); }
            return h;
        }
        public static void KeepCaptureWindow(nint handle, int width, int height)
        {
            if (handle != 0) SetWindowPos(handle, 1, -32000, -32000, width, height, (uint)(SwpNoActivate | SwpShowWindow));
        }
        public static WriteableBitmap? CaptureWindow(nint window, int width, int height, byte[] buffer)
        {
            var screen = GetDC(window); if (screen == 0) return null;
            var dc = CreateCompatibleDC(screen); if (dc == 0) { ReleaseDC(window, screen); return null; }
            var info = new BitmapInfo { Header = new BitmapInfoHeader { Size = 40, Width = width, Height = -height, Planes = 1, BitsPerPixel = 32, Compression = 0 } };
            var dib = CreateDIBSection(screen, ref info, 0, out var bits, 0, 0); if (dib == 0 || bits == 0) { DeleteDC(dc); ReleaseDC(window, screen); return null; }
            var previous = SelectObject(dc, dib);
            var ok = PrintWindow(window, dc, 2);
            var result = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Opaque);
            if (ok) { using var locked = result.Lock(); Marshal.Copy(bits, buffer, 0, width * height * 4); for (var y = 0; y < height; y++) Marshal.Copy(buffer, y * width * 4, locked.Address + y * locked.RowBytes, width * 4); }
            SelectObject(dc, previous); DeleteObject(dib); DeleteDC(dc); ReleaseDC(window, screen); return ok ? result : null;
        }
        [DllImport("gdi32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rasterOperation);
    }
}

/// <summary>启用 Windows Media Foundation 播放后端。<para>Enables the Windows Media Foundation playback backend.</para></summary>
public static class WindowsMediaFoundation
{
    [SupportedOSPlatform("windows6.1")]
    /// <summary>注册 Windows Media Foundation 后端。<para>Registers the Windows Media Foundation backend.</para></summary>
    public static void Use() => MediaBackendFactory.Register(static () => new WindowsMediaFoundationBackend());
}
internal static class WindowsMediaFoundationModule
{
    [ModuleInitializer]
    internal static void Initialize() { if (OperatingSystem.IsWindows()) WindowsMediaFoundation.Use(); }
}
