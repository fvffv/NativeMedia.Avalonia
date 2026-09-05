using System.Runtime.InteropServices;
using Avalonia;
using global::Avalonia.Media.Imaging;
using NativeMedia.Avalonia;

namespace NativeMedia.Avalonia.macOS;

/// <summary>
/// Native macOS backend based on AVPlayer and AVPlayerItemVideoOutput.
/// AVFoundation owns playback and audio synchronization; decoded BGRA frames
/// are copied from CoreVideo into Avalonia so normal Avalonia controls can be
/// composited above the video.
/// </summary>
public sealed class MacAvFoundationBackend : NativeMediaBackend
{
    private const uint PixelFormat32Bgra = 0x42475241; // 'BGRA'
    private readonly object _nativeGate = new();
    private nint _player;
    private nint _playerItem;
    private nint _videoOutput;
    private Timer? _frameTimer;
    private Timer? _durationTimer;
    private byte[] _rowBuffer = [];
    private int _readingFrame;
    private bool _disposed;

    public MacAvFoundationBackend() : base("macOS AVFoundation", OperatingSystem.IsMacOS() && CheckFramework()) { }

    public static bool CheckFramework()
        => OperatingSystem.IsMacOS()
           && CanLoadFramework(Native.AvFoundation)
           && CanLoadFramework(Native.CoreVideo)
           && CanLoadFramework(Native.QuartzCore);

    private static bool CanLoadFramework(string path)
    {
        if (!NativeLibrary.TryLoad(path, out var handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    }

    public override async Task OpenAsync(string source, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            RaiseError("AVFoundation is unavailable on this macOS runtime.");
            return;
        }
        if (!MediaSource.TryParse(source, out var uri, out var kind, out var error))
        {
            RaiseError(error!);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        lock (_nativeGate)
        {
            if (_disposed) return;
            CloseNativePlayerLocked();

            var text = Native.String(uri!.IsFile ? uri.LocalPath : source);
            var url = kind is MediaSourceKind.Http or MediaSourceKind.Https
                ? Native.Send(Native.Class("NSURL"), Native.Selector("URLWithString:"), text)
                : Native.Send(Native.Class("NSURL"), Native.Selector("fileURLWithPath:"), text);
            if (url == 0)
                throw new InvalidOperationException("AVFoundation could not create an NSURL for the media source.");

            _playerItem = Native.Send(Native.Class("AVPlayerItem"), Native.Selector("playerItemWithURL:"), url);
            if (_playerItem == 0)
                throw new InvalidOperationException("AVFoundation could not create an AVPlayerItem.");
            Native.SendVoid(_playerItem, Native.Selector("retain"));

            _videoOutput = CreateVideoOutput();
            if (_videoOutput != 0)
                Native.SendVoid(_playerItem, Native.Selector("addOutput:"), _videoOutput);

            _player = Native.Send(Native.Class("AVPlayer"), Native.Selector("playerWithPlayerItem:"), _playerItem);
            if (_player == 0)
            {
                CloseNativePlayerLocked();
                throw new InvalidOperationException("AVFoundation could not create an AVPlayer.");
            }
            Native.SendVoid(_player, Native.Selector("retain"));
            ApplyVolumeLocked();

            // Poll AVPlayerItemVideoOutput at display cadence. AVPlayer keeps
            // audio/video synchronized; this timer only transfers ready frames.
            if (_videoOutput != 0)
                _frameTimer = new Timer(_ => PullVideoFrame(), null, 0, 16);
            _durationTimer = new Timer(_ => PollDuration(), null, 100, 250);
        }

        await base.OpenAsync(source, cancellationToken);
        PollDuration();
        RaisePositionChanged();
    }

    private static nint CreateVideoOutput()
    {
        var pixelFormat = Native.SendUInt(Native.Class("NSNumber"), Native.Selector("numberWithUnsignedInt:"), PixelFormat32Bgra);
        var attributes = Native.Send(
            Native.Class("NSDictionary"),
            Native.Selector("dictionaryWithObject:forKey:"),
            pixelFormat,
            Native.PixelFormatTypeKey);
        var output = Native.Send(Native.Class("AVPlayerItemVideoOutput"), Native.Selector("alloc"));
        return output == 0
            ? 0
            : Native.Send(output, Native.Selector("initWithPixelBufferAttributes:"), attributes);
    }

    public override Task PlayAsync(CancellationToken cancellationToken = default)
    {
        lock (_nativeGate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_player == 0) { RaiseError("Open a media source before playing."); return Task.CompletedTask; }
            Native.SendVoid(_player, Native.Selector("play"));
        }
        return base.PlayAsync(cancellationToken);
    }

    public override Task PauseAsync(CancellationToken cancellationToken = default)
    {
        lock (_nativeGate)
        {
            if (_disposed) return Task.CompletedTask;
            if (_player != 0) Native.SendVoid(_player, Native.Selector("pause"));
        }
        return base.PauseAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_nativeGate)
        {
            if (_disposed) return;
            if (_player != 0)
            {
                Native.SendVoid(_player, Native.Selector("pause"));
                Native.SendTime(_player, Native.Selector("seekToTime:"), CMTime.Zero);
            }
        }
        await base.StopAsync(cancellationToken);
    }

    public override void Seek(TimeSpan position)
    {
        lock (_nativeGate)
        {
            if (_disposed) return;
            if (_player != 0)
            {
                var target = Native.CMTimeMakeWithSeconds(Math.Max(0, position.TotalSeconds), 600);
                Native.SendThreeTimes(
                    _player,
                    Native.Selector("seekToTime:toleranceBefore:toleranceAfter:"),
                    target,
                    CMTime.Zero,
                    CMTime.Zero);
            }
        }
        base.Seek(position);
    }

    protected override TimeSpan QueryPosition()
    {
        lock (_nativeGate)
        {
            if (_disposed || _player == 0) return base.QueryPosition();
            return Native.SendTimeResult(_player, Native.Selector("currentTime")).ToTimeSpan();
        }
    }

    public override TimeSpan Duration
    {
        get
        {
            lock (_nativeGate)
            {
                if (_disposed || _playerItem == 0) return base.Duration;
                var duration = Native.SendTimeResult(_playerItem, Native.Selector("duration")).ToTimeSpan();
                return duration > TimeSpan.Zero ? duration : base.Duration;
            }
        }
        protected set => base.Duration = value;
    }

    public override double Volume
    {
        get => base.Volume;
        set
        {
            base.Volume = value;
            lock (_nativeGate) ApplyVolumeLocked();
        }
    }

    public override bool Muted
    {
        get => base.Muted;
        set
        {
            base.Muted = value;
            lock (_nativeGate) ApplyVolumeLocked();
        }
    }

    private void ApplyVolumeLocked()
    {
        if (_player != 0)
            Native.SendFloat(_player, Native.Selector("setVolume:"), Muted ? 0 : (float)Volume);
    }

    /// <summary>
    /// macOS video is composited by Avalonia through VideoFrameAvailable, so a
    /// native child handle is deliberately not used.
    /// </summary>
    public override void SetVideoOutput(nint handle) { }

    private void PollDuration()
    {
        var changed = false;
        lock (_nativeGate)
        {
            if (_disposed || _playerItem == 0) return;
            var duration = Native.SendTimeResult(_playerItem, Native.Selector("duration")).ToTimeSpan();
            if (duration <= TimeSpan.Zero) return;
            if (base.Duration != duration)
            {
                base.Duration = duration;
                changed = true;
            }
            _durationTimer?.Dispose();
            _durationTimer = null;
        }
        if (changed) RaisePositionChanged();
    }

    private void PullVideoFrame()
    {
        if (Interlocked.Exchange(ref _readingFrame, 1) != 0) return;
        nint pixelBuffer = 0;
        try
        {
            lock (_nativeGate)
            {
                if (_disposed || _videoOutput == 0 || _player == 0) return;
                var itemTime = Native.SendTimeForDouble(
                    _videoOutput,
                    Native.Selector("itemTimeForHostTime:"),
                    Native.CACurrentMediaTime());
                if (!itemTime.IsValid
                    || !Native.SendBoolForTime(_videoOutput, Native.Selector("hasNewPixelBufferForItemTime:"), itemTime))
                    return;
                pixelBuffer = Native.SendPixelBuffer(
                    _videoOutput,
                    Native.Selector("copyPixelBufferForItemTime:itemTimeForDisplay:"),
                    itemTime,
                    0);
            }

            if (pixelBuffer != 0) PublishPixelBuffer(pixelBuffer);
        }
        catch (Exception ex)
        {
            if (!_disposed) RaiseError("AVFoundation video output failed: " + ex.Message, ex);
        }
        finally
        {
            if (pixelBuffer != 0) Native.CFRelease(pixelBuffer);
            Volatile.Write(ref _readingFrame, 0);
        }
    }

    private void PublishPixelBuffer(nint pixelBuffer)
    {
        if (Native.CVPixelBufferGetPixelFormatType(pixelBuffer) != PixelFormat32Bgra) return;
        if (Native.CVPixelBufferLockBaseAddress(pixelBuffer, 1) != 0) return;
        try
        {
            var widthValue = Native.CVPixelBufferGetWidth(pixelBuffer);
            var heightValue = Native.CVPixelBufferGetHeight(pixelBuffer);
            var rowBytesValue = Native.CVPixelBufferGetBytesPerRow(pixelBuffer);
            if (widthValue == 0 || heightValue == 0 || widthValue > int.MaxValue || heightValue > int.MaxValue || rowBytesValue > int.MaxValue) return;

            var width = (int)widthValue;
            var height = (int)heightValue;
            var sourceRowBytes = (int)rowBytesValue;
            var copyRowBytes = checked(width * 4);
            var source = Native.CVPixelBufferGetBaseAddress(pixelBuffer);
            if (source == 0 || sourceRowBytes < copyRowBytes) return;
            if (_rowBuffer.Length < copyRowBytes) _rowBuffer = new byte[copyRowBytes];

            var bitmap = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                global::Avalonia.Platform.PixelFormat.Bgra8888,
                global::Avalonia.Platform.AlphaFormat.Opaque);
            using (var locked = bitmap.Lock())
            {
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(source + y * sourceRowBytes, _rowBuffer, 0, copyRowBytes);
                    Marshal.Copy(_rowBuffer, 0, locked.Address + y * locked.RowBytes, copyRowBytes);
                }
            }
            RaiseVideoFrame(bitmap);
        }
        finally
        {
            Native.CVPixelBufferUnlockBaseAddress(pixelBuffer, 1);
        }
    }

    public override void Dispose()
    {
        lock (_nativeGate)
        {
            if (_disposed) return;
            _disposed = true;
            CloseNativePlayerLocked();
        }
        base.Dispose();
    }

    private void CloseNativePlayerLocked()
    {
        _frameTimer?.Dispose();
        _frameTimer = null;
        _durationTimer?.Dispose();
        _durationTimer = null;
        if (_player != 0)
        {
            try { Native.SendVoid(_player, Native.Selector("pause")); } catch { }
        }
        if (_playerItem != 0 && _videoOutput != 0)
        {
            try { Native.SendVoid(_playerItem, Native.Selector("removeOutput:"), _videoOutput); } catch { }
        }
        if (_videoOutput != 0) Native.SendVoid(_videoOutput, Native.Selector("release"));
        if (_player != 0) Native.SendVoid(_player, Native.Selector("release"));
        if (_playerItem != 0) Native.SendVoid(_playerItem, Native.Selector("release"));
        _videoOutput = 0;
        _player = 0;
        _playerItem = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CMTime(long Value, int Timescale, uint Flags, long Epoch)
    {
        public static CMTime Zero => new(0, 1, 1, 0);
        public bool IsValid => Timescale > 0 && (Flags & 1) != 0 && (Flags & 12) == 0;
        public static CMTime From(TimeSpan value) => new(Math.Max(0, value.Ticks), (int)TimeSpan.TicksPerSecond, 1, 0);
        public TimeSpan ToTimeSpan()
        {
            if (!IsValid) return TimeSpan.Zero;
            var seconds = (double)Value / Timescale;
            return double.IsFinite(seconds) && seconds >= 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
        }
    }

    private static class Native
    {
        internal const string AvFoundation = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";
        internal const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";
        internal const string QuartzCore = "/System/Library/Frameworks/QuartzCore.framework/QuartzCore";
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string CoreMedia = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
        private static readonly nint CoreVideoHandle = NativeLibrary.Load(CoreVideo);

        internal static nint PixelFormatTypeKey { get; } = ReadExportedObject("kCVPixelBufferPixelFormatTypeKey");

        private static nint ReadExportedObject(string name)
        {
            var symbol = NativeLibrary.GetExport(CoreVideoHandle, name);
            return Marshal.ReadIntPtr(symbol);
        }

        [DllImport(ObjC)] internal static extern nint objc_getClass([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(ObjC)] private static extern nint sel_registerName([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern nint Send(nint target, nint selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern nint Send(nint target, nint selector, nint value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern nint Send(nint target, nint selector, nint value1, nint value2);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendUtf8(nint target, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern nint SendUInt(nint target, nint selector, uint value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern void SendVoid(nint target, nint selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern void SendVoid(nint target, nint selector, nint value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern void SendFloat(nint target, nint selector, float value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern void SendTime(nint target, nint selector, CMTime value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern void SendThreeTimes(nint target, nint selector, CMTime value, CMTime toleranceBefore, CMTime toleranceAfter);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern CMTime SendTimeResult(nint target, nint selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] internal static extern CMTime SendTimeForDouble(nint target, nint selector, double value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        internal static extern bool SendBoolForTime(nint target, nint selector, CMTime value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        internal static extern nint SendPixelBuffer(nint target, nint selector, CMTime value, nint displayTime);

        [DllImport(QuartzCore)] internal static extern double CACurrentMediaTime();
        [DllImport(CoreMedia)] internal static extern CMTime CMTimeMakeWithSeconds(double seconds, int preferredTimescale);
        [DllImport(CoreVideo)] internal static extern int CVPixelBufferLockBaseAddress(nint pixelBuffer, ulong lockFlags);
        [DllImport(CoreVideo)] internal static extern int CVPixelBufferUnlockBaseAddress(nint pixelBuffer, ulong lockFlags);
        [DllImport(CoreVideo)] internal static extern nuint CVPixelBufferGetWidth(nint pixelBuffer);
        [DllImport(CoreVideo)] internal static extern nuint CVPixelBufferGetHeight(nint pixelBuffer);
        [DllImport(CoreVideo)] internal static extern nuint CVPixelBufferGetBytesPerRow(nint pixelBuffer);
        [DllImport(CoreVideo)] internal static extern nint CVPixelBufferGetBaseAddress(nint pixelBuffer);
        [DllImport(CoreVideo)] internal static extern uint CVPixelBufferGetPixelFormatType(nint pixelBuffer);
        [DllImport(CoreFoundation)] internal static extern void CFRelease(nint value);

        internal static nint Class(string value) => objc_getClass(value);
        internal static nint Selector(string value) => sel_registerName(value);
        internal static nint String(string value) => SendUtf8(Class("NSString"), Selector("stringWithUTF8String:"), value);
    }
}

/// <summary>启用 macOS AVFoundation 播放后端。<para>Enables the macOS AVFoundation playback backend.</para></summary>
public static class MacAvFoundation
{
    /// <summary>注册 macOS AVFoundation 后端。<para>Registers the macOS AVFoundation backend.</para></summary>
    public static void Use() => MediaBackendFactory.Register(static () => new MacAvFoundationBackend());
}

internal static class MacAvFoundationModule
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        if (OperatingSystem.IsMacOS()) MacAvFoundation.Use();
    }
}
