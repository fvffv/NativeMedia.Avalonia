namespace NativeMedia.Avalonia;

/// <summary>Platform-neutral contract implemented by the native media adapters.</summary>
public interface IMediaBackend : IAsyncDisposable, IDisposable
{
    string Name { get; }
    bool IsAvailable { get; }
    /// <summary>
    /// Indicates that opening must stay on the Avalonia/native UI thread.
    /// <para>Indicates whether opening must remain on the Avalonia/native UI thread.</para>
    /// </summary>
    bool RequiresUiThreadOpen { get; }
    MediaState State { get; }
    TimeSpan Position { get; }
    TimeSpan Duration { get; }
    TimeSpan BufferedPosition { get; }
    bool IsStreaming { get; }
    event EventHandler<MediaRuntimeStatusEventArgs>? RuntimeStatusChanged;
    double Volume { get; set; }
    bool Muted { get; set; }
    void SetVideoOutput(nint handle);
    event EventHandler? Opened;
    event EventHandler? Playing;
    event EventHandler? Paused;
    event EventHandler? Stopped;
    event EventHandler? Ended;
    event EventHandler<PositionChangedEventArgs>? PositionChanged;
    event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
    event EventHandler<VideoFrameEventArgs>? VideoFrameAvailable;
    event EventHandler<MediaErrorEventArgs>? Error;
    Task OpenAsync(string source, CancellationToken cancellationToken = default);
    Task PlayAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    void Seek(TimeSpan position);
}

/// <summary>Optional backend switch used to disable video-only work for AudioPlayer. <para>Optional backend switch for disabling video-only work when used by AudioPlayer.</para></summary>
public interface IVideoMediaBackend
{
    /// <summary>Configures whether a video output is required. <para>Configures whether video output is required.</para></summary>
    void ConfigureVideoOutput(bool enabled);
}
