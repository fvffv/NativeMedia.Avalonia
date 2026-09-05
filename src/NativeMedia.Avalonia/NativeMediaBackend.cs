using System.Diagnostics;

namespace NativeMedia.Avalonia;

/// <summary>
/// Shared clock and lifecycle implementation. Platform adapters provide native
/// availability checks and can replace this class with a decoder-backed implementation.
/// Position is derived from a monotonic clock, never incremented by a timer.
/// </summary>
public class NativeMediaBackend : IMediaBackend
{
    private readonly Stopwatch _clock = new();
    private readonly object _gate = new();
    private bool _disposed;
    private string? _source;
    private TimeSpan _position;
    private TimeSpan _clockOffset;
    private TimeSpan _duration;
    private TimeSpan _bufferedPosition;
    private bool _isStreaming;
    private MediaState _state;
    private double _volume = 1;
    private bool _muted;
    private Timer? _positionPoller;

    public NativeMediaBackend(string name, bool available = true) { Name = name; IsAvailable = available; }
    public string Name { get; }
    public virtual bool IsAvailable { get; protected init; }
    public MediaState State { get { lock (_gate) { UpdateClock(); return _state; } } }
    public virtual TimeSpan Position { get { lock (_gate) { UpdateClock(); return _position; } } }
    public virtual TimeSpan Duration { get { lock (_gate) return _duration; } protected set { lock (_gate) { _duration = value; if (!_isStreaming) _bufferedPosition = value; } } }
    public virtual TimeSpan BufferedPosition { get { lock (_gate) return _bufferedPosition > TimeSpan.Zero ? _bufferedPosition : _duration; } protected set { lock (_gate) _bufferedPosition = value; } }
    public bool IsStreaming { get { lock (_gate) return _isStreaming; } private set { lock (_gate) _isStreaming = value; } }
    public virtual double Volume { get { lock (_gate) return _volume; } set { var v = Math.Clamp(value, 0, 1); lock (_gate) _volume = v; VolumeChanged?.Invoke(this, new(v)); } }
    public virtual bool Muted { get { lock (_gate) return _muted; } set { lock (_gate) _muted = value; } }

    public event EventHandler? Opened, Playing, Paused, Stopped, Ended;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
    public event EventHandler<VideoFrameEventArgs>? VideoFrameAvailable;
    public event EventHandler<MediaRuntimeStatusEventArgs>? RuntimeStatusChanged;
    public event EventHandler<MediaErrorEventArgs>? Error;
    public virtual void SetVideoOutput(nint handle) { }

    public virtual async Task OpenAsync(string source, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!IsAvailable) { Fail($"{Name} media backend is unavailable on this system."); return; }
        if (string.IsNullOrWhiteSpace(source)) { Fail("Media source is empty."); return; }
        if (!MediaSource.TryParse(source, out var uri, out var kind, out var sourceError)) { Fail(sourceError!); return; }
        var isNetwork = kind is MediaSourceKind.Http or MediaSourceKind.Https;
        var path = kind == MediaSourceKind.FileUri ? uri!.LocalPath : source;
        await Task.Yield(); cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) { _source = source; _isStreaming = isNetwork; _position = TimeSpan.Zero; _clockOffset = TimeSpan.Zero; _duration = TimeSpan.Zero; _bufferedPosition = TimeSpan.Zero; _state = MediaState.Ready; }
        if (!isNetwork) BufferedPosition = Duration;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    public virtual Task PlayAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed(); cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) { if (_source is null) { Fail("Open a media source before playing."); return Task.CompletedTask; } UpdateClock(); _clockOffset = _position; _state = MediaState.Playing; _clock.Restart(); _positionPoller ??= new Timer(_ => PollPosition(), null, 200, 200); }
        Playing?.Invoke(this, EventArgs.Empty); return Task.CompletedTask;
    }
    public virtual Task PauseAsync(CancellationToken cancellationToken = default)
    { ThrowIfDisposed(); lock (_gate) { UpdateClock(); _clockOffset = _position; _clock.Stop(); if (_state == MediaState.Playing) _state = MediaState.Paused; } Paused?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public virtual Task StopAsync(CancellationToken cancellationToken = default)
    { ThrowIfDisposed(); lock (_gate) { _clock.Stop(); _clockOffset = TimeSpan.Zero; _position = TimeSpan.Zero; _state = MediaState.Stopped; } PositionChanged?.Invoke(this, new(TimeSpan.Zero)); Stopped?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
    public virtual void Seek(TimeSpan position)
    { ThrowIfDisposed(); var p = position < TimeSpan.Zero ? TimeSpan.Zero : (Duration > TimeSpan.Zero && position > Duration ? Duration : position); lock (_gate) { _position = p; _clockOffset = p; _clock.Restart(); if (_state != MediaState.Playing) _clock.Stop(); } PositionChanged?.Invoke(this, new(p)); }

    private void PollPosition() { lock (_gate) { if (_disposed || _state != MediaState.Playing) return; UpdateClock(); var p = _position; PositionChanged?.Invoke(this, new(p)); } }
    protected void RaisePositionChanged() => PositionChanged?.Invoke(this, new(Position));
    protected void RaiseVideoFrame(global::Avalonia.Media.Imaging.Bitmap frame) => VideoFrameAvailable?.Invoke(this, new(frame));
    protected void RaiseEndedFromBackend()
    {
        lock (_gate) { _position = _duration; _state = MediaState.Ended; _clock.Stop(); }
        Ended?.Invoke(this, EventArgs.Empty);
    }
    protected void RaiseRuntimeStatus(string message, bool isBusy = false) => RuntimeStatusChanged?.Invoke(this, new(message, isBusy));
    private void UpdateClock()
    { if (_state == MediaState.Playing) { _position = QueryPosition(); if (_duration > TimeSpan.Zero && _position >= _duration) { _position = _duration; _state = MediaState.Ended; _clock.Stop(); Ended?.Invoke(this, EventArgs.Empty); } } }
    protected virtual TimeSpan QueryPosition() => _clockOffset + _clock.Elapsed;
    protected void Fail(string message) => RaiseError(message);
    protected void RaiseError(string message, Exception? exception = null) { lock (_gate) _state = MediaState.Error; Error?.Invoke(this, new(message, exception)); }
    private void ThrowIfDisposed() { ObjectDisposedException.ThrowIf(_disposed, this); }
    public virtual ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    public virtual void Dispose() { if (_disposed) return; _disposed = true; _positionPoller?.Dispose(); _positionPoller = null; _clock.Stop(); }
}
