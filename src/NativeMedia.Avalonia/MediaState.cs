namespace NativeMedia.Avalonia;

/// <summary>
/// 媒体播放器的生命周期状态。
/// <para>Lifecycle state of a media player.</para>
/// </summary>
public enum MediaState { None, Loading, Ready, Playing, Paused, Stopped, Ended, Error }

/// <summary>
/// 媒体播放错误事件参数。
/// <para>Event data for a media playback error.</para>
/// </summary>
public sealed class MediaErrorEventArgs(string message, Exception? exception = null) : EventArgs
{
    /// <summary>错误信息。<para>Error message.</para></summary>
    public string Message { get; } = message;
    /// <summary>导致错误的异常；如果后端没有提供异常则为 <see langword="null" />。<para>Underlying exception, or <see langword="null" /> when the backend did not provide one.</para></summary>
    public Exception? Exception { get; } = exception;
}

/// <summary>媒体状态事件参数。<para>Event data for a media state change.</para></summary>
public sealed class MediaEventArgs(MediaState state) : EventArgs
{
    /// <summary>变更后的媒体状态。<para>New media state.</para></summary>
    public MediaState State { get; } = state;
}

/// <summary>播放位置变更事件参数。<para>Event data for a playback position change.</para></summary>
public sealed class PositionChangedEventArgs(TimeSpan position) : EventArgs
{
    /// <summary>新的播放位置。<para>New playback position.</para></summary>
    public TimeSpan Position { get; } = position;
}

/// <summary>音量变更事件参数。<para>Event data for a volume change.</para></summary>
public sealed class VolumeChangedEventArgs(double volume) : EventArgs
{
    /// <summary>新的音量，范围为 0 到 1。<para>New volume in the range 0 to 1.</para></summary>
    public double Volume { get; } = volume;
}

/// <summary>新视频帧事件参数。<para>Event data for a newly decoded video frame.</para></summary>
public sealed class VideoFrameEventArgs(global::Avalonia.Media.Imaging.Bitmap frame, TimeSpan? position = null, long capturedTimestamp = 0, ulong contentHash = 0) : EventArgs
{
    /// <summary>解码后的视频帧。<para>Decoded video frame.</para></summary>
    public global::Avalonia.Media.Imaging.Bitmap Frame { get; } = frame;
    /// <summary>该帧对应的媒体时间；后端无法提供时为 null。<para>Media timestamp of the frame, or null when unavailable.</para></summary>
    public TimeSpan? Position { get; } = position;
    /// <summary>帧被后端发布时的单调时钟时间戳。<para>Monotonic timestamp captured when the backend published the frame.</para></summary>
    public long CapturedTimestamp { get; } = capturedTimestamp;
    /// <summary>帧内容指纹，用于确认 Seek 后确实收到新画面。<para>Frame content fingerprint used to confirm a new picture after seeking.</para></summary>
    public ulong ContentHash { get; } = contentHash;
}
