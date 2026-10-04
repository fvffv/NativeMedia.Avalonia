using System.Diagnostics;
using System.Collections.Concurrent;
using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Media;
using global::Avalonia.Controls.Shapes;
using global::Avalonia.Threading;
using SvgPath = global::Avalonia.Controls.Shapes.Path;

namespace NativeMedia.Avalonia;

/// <summary>Shared MVVM-friendly media control surface for video and audio players.</summary>
public abstract class MediaPlayer : TemplatedControl, IAsyncDisposable, IDisposable
{
    /// <summary>注册媒体源属性。<para>Registered media source property.</para></summary>
    public static readonly StyledProperty<string?> SourceProperty = AvaloniaProperty.Register<MediaPlayer, string?>(nameof(Source));
    /// <summary>注册自动播放属性。<para>Registered auto-play property.</para></summary>
    public static readonly StyledProperty<bool> AutoPlayProperty = AvaloniaProperty.Register<MediaPlayer, bool>(nameof(AutoPlay));
    /// <summary>注册音量属性。<para>Registered volume property.</para></summary>
    public static readonly StyledProperty<double> VolumeProperty = AvaloniaProperty.Register<MediaPlayer, double>(nameof(Volume), 1d, coerce: (_, v) => Math.Clamp(v, 0, 1));
    /// <summary>注册静音属性。<para>Registered mute property.</para></summary>
    public static readonly StyledProperty<bool> MutedProperty = AvaloniaProperty.Register<MediaPlayer, bool>(nameof(Muted));
    /// <summary>注册默认控制栏显隐属性。<para>Registered default-controls visibility property.</para></summary>
    public static readonly StyledProperty<bool> ShowControlsProperty = AvaloniaProperty.Register<MediaPlayer, bool>(nameof(ShowControls), true);
    /// <summary>注册循环播放属性。<para>Registered loop property.</para></summary>
    public static readonly StyledProperty<bool> LoopProperty = AvaloniaProperty.Register<MediaPlayer, bool>(nameof(Loop));
    /// <summary>注册播放质量属性，默认原画。<para>Registered playback quality, defaulting to Original.</para></summary>
    public static readonly StyledProperty<VideoPlaybackQuality> PlaybackQualityProperty =
        AvaloniaProperty.Register<MediaPlayer, VideoPlaybackQuality>(nameof(PlaybackQuality), VideoPlaybackQuality.Original,
            validate: static value => Enum.IsDefined(value));
    /// <summary>注册播放位置属性。<para>Registered playback position property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, TimeSpan> PositionProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, TimeSpan>(nameof(Position), p => p.Position, (p, v) => p.SetPosition(v));
    /// <summary>注册媒体时长属性。<para>Registered media duration property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, TimeSpan> DurationProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, TimeSpan>(nameof(Duration), p => p.Duration);
    /// <summary>注册缓冲位置属性。<para>Registered buffered-position property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, TimeSpan> BufferedPositionProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, TimeSpan>(nameof(BufferedPosition), p => p.BufferedPosition);
    /// <summary>注册播放状态属性。<para>Registered playing-state property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, bool> IsPlayingProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, bool>(nameof(IsPlaying), p => p.IsPlaying, (p, v) => p.SetIsPlaying(v));
    /// <summary>注册媒体状态属性。<para>Registered media-state property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, MediaState> StateProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, MediaState>(nameof(State), p => p.State);
    /// <summary>注册秒数形式的播放位置属性。<para>Registered playback-position-in-seconds property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, double> PositionSecondsProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, double>(nameof(PositionSeconds), p => p.PositionSeconds, (p, v) => p.Seek(TimeSpan.FromSeconds(v)));
    /// <summary>注册秒数形式的媒体时长属性。<para>Registered duration-in-seconds property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, double> DurationSecondsProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, double>(nameof(DurationSeconds), p => p.DurationSeconds);
    /// <summary>注册进度条最大值属性。<para>Registered progress maximum property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, double> ProgressMaximumProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, double>(nameof(ProgressMaximum), p => p.ProgressMaximum);
    /// <summary>注册秒数形式的缓冲位置属性。<para>Registered buffered-position-in-seconds property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, double> BufferedPositionSecondsProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, double>(nameof(BufferedPositionSeconds), p => p.BufferedPositionSeconds);
    /// <summary>注册当前时间属性。<para>Registered current-time property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, TimeSpan> CurrentTimeProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, TimeSpan>(nameof(CurrentTime), p => p.CurrentTime);
    /// <summary>注册总时间属性。<para>Registered total-time property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, TimeSpan> TotalTimeProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, TimeSpan>(nameof(TotalTime), p => p.TotalTime);
    /// <summary>注册格式化当前时间文本属性。<para>Registered formatted current-time text property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, string> CurrentTimeTextProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, string>(nameof(CurrentTimeText), p => p.CurrentTimeText);
    /// <summary>注册格式化时长文本属性。<para>Registered formatted duration text property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, string> DurationTextProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, string>(nameof(DurationText), p => p.DurationText);
    /// <summary>注册控制栏当前显隐状态属性。<para>Registered current-controls-visibility property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, bool> ControlsVisibleProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, bool>(nameof(ControlsVisible), p => p.ControlsVisible);
    /// <summary>注册中央播放按钮显隐状态属性。<para>Registered center-play-button visibility property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, bool> CenterPlayVisibleProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, bool>(nameof(CenterPlayVisible), p => p.CenterPlayVisible);
    /// <summary>注册 Avalonia 合成视频状态属性。<para>Registered composited-video status property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, bool> UsesCompositedVideoProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, bool>(nameof(UsesCompositedVideo), p => p.UsesCompositedVideo);
    /// <summary>注册原生视频表面状态属性。<para>Registered native-video-surface status property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, bool> UsesNativeVideoSurfaceProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, bool>(nameof(UsesNativeVideoSurface), p => p.UsesNativeVideoSurface);
    /// <summary>注册流媒体状态属性。<para>Registered streaming status property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, bool> IsStreamingProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, bool>(nameof(IsStreaming), p => p.IsStreaming);
    /// <summary>注册媒体源类型属性。<para>Registered source-kind property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, MediaSourceKind> SourceKindProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, MediaSourceKind>(nameof(SourceKind), p => p.SourceKind);
    /// <summary>注册当前视频帧属性。<para>Registered current-video-frame property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, global::Avalonia.Media.Imaging.Bitmap?> VideoFrameProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, global::Avalonia.Media.Imaging.Bitmap?>(nameof(VideoFrame), p => p.VideoFrame);
    /// <summary>注册运行状态文本属性。<para>Registered runtime-status text property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, string> RuntimeStatusProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, string>(nameof(RuntimeStatus), p => p.RuntimeStatus);
    /// <summary>注册运行状态提示显隐属性。<para>Registered runtime-status visibility property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, bool> RuntimeStatusVisibleProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, bool>(nameof(RuntimeStatusVisible), p => p.RuntimeStatusVisible);
    /// <summary>注册缓冲/打开状态属性。<para>Registered buffering/opening state property.</para></summary>
    public static readonly DirectProperty<MediaPlayer, bool> IsBufferingProperty = AvaloniaProperty.RegisterDirect<MediaPlayer, bool>(nameof(IsBuffering), p => p.IsBuffering);

    private readonly IMediaBackend _backend;
    private readonly BackendWorker? _backendWorker;
    private bool _updating;
    private TimeSpan _position, _duration, _buffered;
    private bool _isPlaying;
    private MediaState _state;
    private bool _disposed;
    private Button? _playButton, _centerPlayButton, _stopButton, _muteButton, _fullScreenButton;
    private Slider? _seekSlider, _volumeSlider;
    private NativeMediaSurface? _videoSurface;
    private Control? _controlOverlay;
    private DispatcherTimer? _controlsTimer;
    private bool _controlsVisible = true;
    private global::Avalonia.Media.Imaging.Bitmap? _videoFrame;
    private string _runtimeStatus = string.Empty;
    private bool _suppressSourceOpen;
    private int _openGeneration;
    private readonly object _seekBufferGate = new();
    private TaskCompletionSource? _seekReady;
    private bool _seekBuffering;
    private long _seekStartedTimestamp;
    private TimeSpan _seekTarget;
    private TimeSpan _seekOrigin;
    private ulong _seekOriginFrameContentHash;
    private int _seekGeneration;
    private const int SeekIndicatorMinimumMilliseconds = 140;
    private const int SeekIndicatorTimeoutMilliseconds = 2500;
    private bool _awaitingFirstVideoFrame;
    private bool _networkBuffering;
    private bool _videoFrameNeedsRender;
    private long _firstVideoFrameWaitStartedTimestamp;
    private long _lastRenderedFrameTimestamp;
    private long _lastPlaybackProgressTimestamp;
    private TimeSpan? _lastRenderedProgressPosition;
    private TimeSpan _lastObservedAudioPosition;
    private ulong _lastRenderedProgressContentHash;
    private TimeSpan? _lastVideoFramePosition;
    private long _lastVideoFrameCapturedTimestamp;
    private ulong _lastVideoFrameContentHash;
    private VideoFrameSurface? _videoFrameSurface;
    private TopLevel? _frameClockHost;
    private bool _frameClockScheduled;
    private readonly object _pendingFrameGate = new();
    private VideoFrameEventArgs? _pendingVideoFrame;
    private bool _frameDeliveryScheduled;
    private DispatcherTimer? _bufferingMonitor;
    private const int NetworkBufferingThresholdMilliseconds = 700;
    private bool _playbackOperationBuffering;
    private WindowState _previousWindowState = WindowState.Normal;
    private Window? _hostWindow;

    protected MediaPlayer()
    {
        _backend = MediaBackendFactory.Create();
        if (_backend is IVideoMediaBackend configurable)
            configurable.ConfigureVideoOutput(this is VideoPlayer);
        if (!_backend.RequiresUiThreadOpen)
            _backendWorker = new BackendWorker(GetType().Name + ".MediaBackend");
        Subscribe(_backend);
    }
    /// <summary>当前注册的平台后端名称。<para>Name of the registered platform backend.</para></summary>
    public string BackendName => _backend.Name;
    /// <summary>GPU submission/composition counters; not a physical display FPS measurement.</summary>
    public VideoPlaybackStatistics? PlaybackStatistics => (_backend as IGpuVideoBackend)?.PlaybackStatistics;
    /// <summary>当前平台播放运行时是否可用。<para>Whether the selected platform runtime is available.</para></summary>
    public bool BackendAvailable => _backend.IsAvailable;
    /// <summary>媒体源路径或网络 URL。支持本地路径、file URI、HTTP 和 HTTPS。<para>Media path or network URL. Supports local paths, file URIs, HTTP, and HTTPS.</para></summary>
    public string? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    /// <summary>打开媒体后是否自动播放。<para>Whether to start playback automatically after opening.</para></summary>
    public bool AutoPlay { get => GetValue(AutoPlayProperty); set => SetValue(AutoPlayProperty, value); }
    /// <summary>播放音量，范围为 0.0 到 1.0。<para>Playback volume from 0.0 to 1.0.</para></summary>
    public double Volume { get => GetValue(VolumeProperty); set => SetValue(VolumeProperty, value); }
    /// <summary>是否静音。<para>Whether playback is muted.</para></summary>
    public bool Muted { get => GetValue(MutedProperty); set => SetValue(MutedProperty, value); }
    /// <summary>是否显示默认控制栏。<para>Whether the default control bar is shown.</para></summary>
    public bool ShowControls { get => GetValue(ShowControlsProperty); set => SetValue(ShowControlsProperty, value); }
    /// <summary>播放结束后是否从头循环。<para>Whether to restart playback when the media ends.</para></summary>
    public bool Loop { get => GetValue(LoopProperty); set => SetValue(LoopProperty, value); }
    /// <summary>视频输出质量，支持绑定和播放中切换；原画不主动降低分辨率或帧率。<para>Bindable video quality; Original applies no resolution or frame-rate reduction.</para></summary>
    public VideoPlaybackQuality PlaybackQuality { get => GetValue(PlaybackQualityProperty); set => SetValue(PlaybackQualityProperty, value); }
    /// <summary>当前播放位置；通过 Avalonia TwoWay Binding 可写入。<para>Current playback position; writable through Avalonia TwoWay Binding.</para></summary>
    public TimeSpan Position => _position;
    /// <summary>媒体总时长。<para>Total media duration.</para></summary>
    public TimeSpan Duration => _duration;
    /// <summary>已缓冲到的播放位置。<para>Playback position that has been buffered.</para></summary>
    public TimeSpan BufferedPosition => _buffered;
    /// <summary>当前是否正在播放；通过 Avalonia TwoWay Binding 可控制播放/暂停。<para>Whether playback is active; can control play/pause through Avalonia TwoWay Binding.</para></summary>
    public bool IsPlaying => _isPlaying;
    /// <summary>当前媒体状态。<para>Current media state.</para></summary>
    public MediaState State => _state;
    /// <summary>当前播放位置（秒）；通过 Avalonia TwoWay Binding 可写入。<para>Current playback position in seconds; writable through Avalonia TwoWay Binding.</para></summary>
    public double PositionSeconds => _position.TotalSeconds;
    /// <summary>媒体总时长（秒）。<para>Total media duration in seconds.</para></summary>
    public double DurationSeconds => Math.Max(0, _duration.TotalSeconds);
    /// <summary>默认进度条的最大值，通常等于总时长（秒）。<para>Maximum value for the default progress bar, normally the duration in seconds.</para></summary>
    public double ProgressMaximum => Math.Max(1, DurationSeconds);
    /// <summary>已缓冲位置（秒）。<para>Buffered playback position in seconds.</para></summary>
    public double BufferedPositionSeconds => Math.Max(0, _buffered.TotalSeconds);
    /// <summary>当前播放时间，等同于 <see cref="Position" />。<para>Current playback time; equivalent to <see cref="Position" />.</para></summary>
    public TimeSpan CurrentTime => Position;
    /// <summary>媒体总时间，等同于 <see cref="Duration" />。<para>Total media time; equivalent to <see cref="Duration" />.</para></summary>
    public TimeSpan TotalTime => Duration;
    /// <summary>格式化后的当前时间文本，例如 01:23。<para>Formatted current-time text, for example 01:23.</para></summary>
    public string CurrentTimeText => FormatTime(CurrentTime);
    /// <summary>格式化后的总时长文本，例如 18:24。<para>Formatted duration text, for example 18:24.</para></summary>
    public string DurationText => FormatTime(TotalTime);
    /// <summary>默认控制栏当前是否可见；播放时鼠标停止移动约 2.6 秒后会变为 false。<para>Whether the default control bar is currently visible; while playing it becomes false about 2.6 seconds after pointer movement stops.</para></summary>
    public bool ControlsVisible => _controlsVisible && ShowControls;
    /// <summary>暂停时中央播放按钮是否可见。<para>Whether the center play button is visible while paused.</para></summary>
    public bool CenterPlayVisible => ShowControls && !_isPlaying;
    /// <summary>是否使用 Avalonia 合成视频帧；Windows、Linux 和 macOS 当前为 true。<para>Whether video frames are composited by Avalonia; currently true on Windows, Linux, and macOS.</para></summary>
    public bool UsesCompositedVideo => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    /// <summary>是否使用原生视频表面。<para>Whether a native video surface is used.</para></summary>
    public bool UsesNativeVideoSurface => !UsesCompositedVideo;
    /// <summary>当前媒体源是否为 HTTP(S) 流媒体。<para>Whether the current source is an HTTP(S) stream.</para></summary>
    public bool IsStreaming => _backend.IsStreaming;
    /// <summary>当前媒体源类型。<para>Kind of the current media source.</para></summary>
    public MediaSourceKind SourceKind => MediaSource.TryParse(Source, out _, out var kind, out _) ? kind : MediaSourceKind.None;
    /// <summary>当前视频帧；仅 VideoPlayer 对该属性有实际意义。<para>Current video frame; meaningful primarily for VideoPlayer.</para></summary>
    public global::Avalonia.Media.Imaging.Bitmap? VideoFrame => _videoFrame;
    /// <summary>后端运行状态或安装提示文本。<para>Backend runtime status or installation message.</para></summary>
    public string RuntimeStatus => _runtimeStatus;
    /// <summary>是否有运行状态提示需要显示。<para>Whether a runtime status message should be displayed.</para></summary>
    public bool RuntimeStatusVisible => !string.IsNullOrWhiteSpace(_runtimeStatus);
    /// <summary>当前是否正在打开媒体或等待首批数据；可用于显示缓冲动画。<para>Whether the media is opening or waiting for initial data; useful for showing a buffering indicator.</para></summary>
    public bool IsBuffering => _state == MediaState.Loading || _seekBuffering || _awaitingFirstVideoFrame || _networkBuffering || _playbackOperationBuffering;
    /// <summary>用户请求进入或退出全屏时触发。<para>Raised when the user requests entering or exiting fullscreen.</para></summary>
    public event EventHandler? FullScreenRequested;
    /// <summary>媒体成功打开后触发。<para>Raised after the media is opened successfully.</para></summary>
    public event EventHandler? Opened;
    /// <summary>开始播放时触发。<para>Raised when playback starts.</para></summary>
    public event EventHandler? Playing;
    /// <summary>暂停时触发。<para>Raised when playback is paused.</para></summary>
    public event EventHandler? Paused;
    /// <summary>停止时触发。<para>Raised when playback is stopped.</para></summary>
    public event EventHandler? Stopped;
    /// <summary>播放到媒体末尾时触发。<para>Raised when playback reaches the end of the media.</para></summary>
    public event EventHandler? Ended;
    /// <summary>播放位置变化时触发。<para>Raised when the playback position changes.</para></summary>
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    /// <summary>音量变化时触发。<para>Raised when the volume changes.</para></summary>
    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;
    /// <summary>打开、解码或播放发生错误时触发。<para>Raised when opening, decoding, or playback fails.</para></summary>
    public event EventHandler<MediaErrorEventArgs>? Error;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty && !_suppressSourceOpen) StartOpenSource(change.NewValue as string);
        else if (change.Property == PlaybackQualityProperty && this is VideoPlayer)
        {
            var quality = change.GetNewValue<VideoPlaybackQuality>();
            _ = ApplyPlaybackQualityAsync(quality);
        }
        else if (change.Property == VolumeProperty && !_updating && Math.Abs(change.GetNewValue<double>() - _backend.Volume) > 0.001)
            _ = RunBackendActionAsync(backend => backend.Volume = Volume);
        else if (change.Property == MutedProperty && !_updating && change.GetNewValue<bool>() != _backend.Muted)
        {
            _ = RunBackendActionAsync(backend => backend.Muted = Muted);
            UpdateTemplateParts();
        }
        else if (change.Property == ShowControlsProperty)
        {
            RaisePropertyChanged(ControlsVisibleProperty, _controlsVisible && change.GetOldValue<bool>(), ControlsVisible);
            RaisePropertyChanged(CenterPlayVisibleProperty, change.GetOldValue<bool>() && !_isPlaying, CenterPlayVisible);
        }
    }
    private Task OpenSourceAsync(string? source) => OpenSourceAsync(source, CancellationToken.None, _openGeneration);
    private async Task ApplyPlaybackQualityAsync(VideoPlaybackQuality quality)
    {
        try
        {
            await RunBackendOperationAsync(backend => backend is IVideoQualityMediaBackend configurable
                ? configurable.SetPlaybackQualityAsync(quality)
                : Task.CompletedTask);
        }
        catch (Exception ex)
        {
            if (!_disposed) OnUi(() => Error?.Invoke(this, new MediaErrorEventArgs("Could not change playback quality: " + ex.Message, ex)));
        }
    }
    private async Task OpenSourceAsync(string? source, CancellationToken cancellationToken, int generation)
    {
        if (_disposed || string.IsNullOrWhiteSpace(source)) return;
        var oldStreaming = IsStreaming;
        var oldKind = SourceKind;
        try
        {
            if (this is VideoPlayer) BeginFirstVideoFrameWait();
            else SetPlaybackOperationBuffering(true);
            SetState(MediaState.Loading);

            // Most backends can perform synchronous startup away from the UI
            // thread (AVPlayer, ffprobe, FFmpeg process setup). Windows MFPlay
            // is the exception: its hidden HWND/message pump must stay on the
            // native UI thread, so Source changes are deferred until the host
            // window has had a chance to render before that short setup runs.
            var quality = PlaybackQuality;
            // Linux/macOS startup runs on the backend worker, but template and
            // visual-tree access must always happen on the Avalonia UI thread.
            if (this is VideoPlayer && _backend is IGpuVideoBackend gpu)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ApplyTemplate();
                    gpu.SetPresentationSurface(_videoFrameSurface);
                });
            }
            await RunBackendOperationAsync(async backend =>
            {
                if (this is VideoPlayer && backend is IVideoQualityMediaBackend configurable)
                    await configurable.SetPlaybackQualityAsync(quality, cancellationToken);
                await backend.OpenAsync(source, cancellationToken);
            });

            if (generation != _openGeneration || _disposed) return;
            RaisePropertyChanged(IsStreamingProperty, oldStreaming, IsStreaming);
            RaisePropertyChanged(SourceKindProperty, oldKind, SourceKind);
            if (this is AudioPlayer) SetPlaybackOperationBuffering(false);
            // Opened is posted from the backend worker; its UI notification may
            // still be queued when this await completes. Use the completed
            // backend operation's state, not the potentially stale UI mirror.
            if (_backend.State == MediaState.Ready && AutoPlay) await PlayAsync();
            else if (_backend.State == MediaState.Ready && this is VideoPlayer) StopBufferingMonitor();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (this is AudioPlayer) SetPlaybackOperationBuffering(false);
            // A newer Source value or an explicit cancellation superseded this
            // open request. The newer request owns the player state.
        }
        catch (Exception ex)
        {
            if (this is AudioPlayer) SetPlaybackOperationBuffering(false);
            if (generation != _openGeneration || _disposed) return;
            OnUi(() =>
            {
                StopBufferingMonitor();
                SetState(MediaState.Error);
                var oldStatus = _runtimeStatus;
                _runtimeStatus = ex.Message;
                RaisePropertyChanged(RuntimeStatusProperty, oldStatus, _runtimeStatus);
                RaisePropertyChanged(RuntimeStatusVisibleProperty, !string.IsNullOrWhiteSpace(oldStatus), RuntimeStatusVisible);
                Error?.Invoke(this, new MediaErrorEventArgs(ex.Message, ex));
            });
        }
    }
    private void BeginFirstVideoFrameWait()
    {
        if (this is not VideoPlayer) return;
        lock (_seekBufferGate)
        {
            _awaitingFirstVideoFrame = true;
            _firstVideoFrameWaitStartedTimestamp = Stopwatch.GetTimestamp();
            _lastRenderedFrameTimestamp = 0;
            _lastRenderedProgressPosition = null;
            _lastRenderedProgressContentHash = 0;
            _lastVideoFramePosition = null;
            _lastVideoFrameCapturedTimestamp = 0;
            _lastVideoFrameContentHash = 0;
        }
        OnUi(() => RaisePropertyChanged(IsBufferingProperty, false, IsBuffering));
        StartBufferingMonitor();
    }
    private void StartOpenSource(string? source)
    {
        CancelSeekBuffering();
        var generation = Interlocked.Increment(ref _openGeneration);
        Dispatcher.UIThread.Post(
            () =>
            {
                if (generation == _openGeneration && !_disposed)
                    _ = OpenSourceAsync(source, CancellationToken.None, generation);
            },
            DispatcherPriority.Background);
    }
    /// <summary>打开本地路径、file URI、HTTP URL 或 HTTPS URL。<para>Opens a local path, file URI, HTTP URL, or HTTPS URL.</para></summary>
    /// <param name="source">媒体路径或 URL。<para>Media path or URL.</para></param>
    /// <param name="cancellationToken">取消打开操作的令牌。<para>Token used to cancel the open operation.</para></param>
    public async Task OpenAsync(string source, CancellationToken cancellationToken = default)
    {
        CancelSeekBuffering();
        _suppressSourceOpen = true;
        try { SetCurrentValue(SourceProperty, source); }
        finally { _suppressSourceOpen = false; }
        var generation = Interlocked.Increment(ref _openGeneration);
        await OpenSourceAsync(source, cancellationToken, generation);
    }
    /// <summary>开始或继续播放；不会阻塞 Avalonia UI 线程。<para>Starts or resumes playback without blocking the Avalonia UI thread.</para></summary>
    public async Task PlayAsync()
    {
        BeginFirstVideoFrameWaitIfNeeded();
        BeginNetworkPlaybackWait();
        StartBufferingMonitor();
        var isAudio = this is AudioPlayer;
        if (isAudio) SetPlaybackOperationBuffering(true);
        try
        {
            await RunBackendOperationAsync(static backend => backend.PlayAsync());
        }
        catch
        {
            if (isAudio) SetPlaybackOperationBuffering(false);
            throw;
        }
        if (isAudio) SetPlaybackOperationBuffering(false);
    }
    /// <summary>暂停播放并保留当前位置；不会阻塞 Avalonia UI 线程。<para>Pauses playback and keeps the current position without blocking the Avalonia UI thread.</para></summary>
    public Task PauseAsync()
    {
        StopBufferingMonitor();
        CancelSeekBuffering();
        return RunBackendOperationAsync(static backend => backend.PauseAsync());
    }
    /// <summary>停止播放；不会阻塞 Avalonia UI 线程。<para>Stops playback without blocking the Avalonia UI thread.</para></summary>
    public Task StopAsync()
    {
        StopBufferingMonitor();
        CancelSeekBuffering();
        return RunBackendOperationAsync(static backend => backend.StopAsync());
    }

    private Task RunBackendOperationAsync(Func<IMediaBackend, Task> operation)
    {
        if (_backend.RequiresUiThreadOpen)
        {
            // MFPlay requires its HWND/COM calls on the Avalonia native thread,
            // but posting the operation makes the public API non-blocking for
            // button clicks, bindings, and AudioPlayer playback.
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await operation(_backend);
                    completion.TrySetResult();
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }, DispatcherPriority.Background);
            return completion.Task;
        }

        return _backendWorker?.InvokeAsync(() => operation(_backend))
            ?? Task.Run(() => operation(_backend));
    }

    private Task RunBackendActionAsync(Action<IMediaBackend> action)
    {
        if (_backend.RequiresUiThreadOpen)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try { action(_backend); }
                catch (Exception ex) { OnUi(() => Error?.Invoke(this, new MediaErrorEventArgs(ex.Message, ex))); }
            }, DispatcherPriority.Background);
            return Task.CompletedTask;
        }
        return _backendWorker?.InvokeAsync(() => action(_backend))
            ?? Task.Run(() => action(_backend));
    }
    /// <summary>跳转到指定播放位置。<para>Seeks to the specified playback position.</para></summary>
    /// <param name="position">目标位置；超出有效范围时由后端进行限制。<para>Target position; the backend clamps values outside the valid range.</para></param>
    public void Seek(TimeSpan position)
    {
        var pending = BeginSeekBuffering(position);
        _ = RunSeekAsync(position, pending);
        _ = FinishSeekBufferingAsync(pending.Generation, pending.Waiter, pending.StartedTimestamp);
    }

    private async Task RunSeekAsync(TimeSpan position, SeekBufferingRequest pending)
    {
        try
        {
            await RunBackendActionAsync(backend => backend.Seek(position));
        }
        catch (Exception ex)
        {
            EndSeekBuffering(pending.Generation, pending.Waiter);
            OnUi(() => Error?.Invoke(this, new MediaErrorEventArgs(ex.Message, ex)));
        }
    }
    /// <summary>在播放和暂停之间切换。<para>Toggles between playing and paused states.</para></summary>
    public Task TogglePlayPause() => IsPlaying ? PauseAsync() : PlayAsync();
    /// <summary>向前跳转，默认 10 秒。<para>Seeks forward by 10 seconds by default.</para></summary>
    /// <param name="amount">跳转时长；省略时为 10 秒。<para>Seek amount; defaults to 10 seconds.</para></param>
    public void SeekForward(TimeSpan? amount = null) => Seek(Position + (amount ?? TimeSpan.FromSeconds(10)));
    /// <summary>向后跳转，默认 10 秒。<para>Seeks backward by 10 seconds by default.</para></summary>
    /// <param name="amount">跳转时长；省略时为 10 秒。<para>Seek amount; defaults to 10 seconds.</para></param>
    public void SeekBackward(TimeSpan? amount = null) => Seek(Position - (amount ?? TimeSpan.FromSeconds(10)));

    private SeekBufferingRequest BeginSeekBuffering(TimeSpan target)
    {
        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource? previous;
        bool oldValue;
        int generation;
        long started;
        lock (_seekBufferGate)
        {
            previous = _seekReady;
            oldValue = IsBuffering;
            generation = ++_seekGeneration;
            started = Stopwatch.GetTimestamp();
            _seekTarget = target < TimeSpan.Zero ? TimeSpan.Zero : target;
            _seekOrigin = _position;
            _seekOriginFrameContentHash = _lastVideoFrameContentHash;
            _seekReady = waiter;
            _seekStartedTimestamp = started;
            _seekBuffering = true;
        }
        previous?.TrySetCanceled();
        if (!oldValue) OnUi(() => RaisePropertyChanged(IsBufferingProperty, false, true));
        return new SeekBufferingRequest(generation, waiter, started);
    }

    private void MarkSeekReady(TimeSpan? framePosition = null, long capturedTimestamp = 0, ulong contentHash = 0)
    {
        TaskCompletionSource? waiter;
        long started;
        lock (_seekBufferGate)
        {
            if (!_seekBuffering) return;
            waiter = _seekReady;
            started = _seekStartedTimestamp;
        }
        if (waiter is null) return;
        if (framePosition is not null)
        {
            // Video seeking is considered ready only after a frame published
            // after the seek request reaches the UI and is close to the target
            // timestamp. A pre-seek frame or a stale position tick cannot hide
            // the indicator anymore.
            if (capturedTimestamp < started) return;
            var target = _seekTarget;
            var origin = _seekOrigin;
            var frame = framePosition.Value;
            var nearTarget = target >= origin
                ? frame >= target - TimeSpan.FromMilliseconds(350)
                : frame <= target + TimeSpan.FromMilliseconds(350);
            if (!nearTarget) return;
            if (_seekOriginFrameContentHash != 0
                && contentHash != 0
                && contentHash == _seekOriginFrameContentHash)
                return;
        }
        var elapsed = Stopwatch.GetElapsedTime(started);
        if (elapsed >= TimeSpan.FromMilliseconds(SeekIndicatorMinimumMilliseconds))
            waiter.TrySetResult();
    }

    private async Task FinishSeekBufferingAsync(int generation, TaskCompletionSource waiter, long started)
    {
        try
        {
            await WaitForSeekReadinessAsync(
                waiter.Task,
                this is VideoPlayer,
                TimeSpan.FromMilliseconds(SeekIndicatorTimeoutMilliseconds));
        }
        catch (TimeoutException)
        {
            // Audio has no rendered frame with which to confirm readiness, so
            // its indicator uses the timeout as a final fallback.
        }
        catch (OperationCanceledException) { return; }

        var remaining = TimeSpan.FromMilliseconds(SeekIndicatorMinimumMilliseconds) - Stopwatch.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            try { await Task.Delay(remaining); }
            catch (OperationCanceledException) { return; }
        }
        EndSeekBuffering(generation, waiter);
    }

    internal static async Task WaitForSeekReadinessAsync(
        Task ready,
        bool requireRenderedVideoFrame,
        TimeSpan timeout)
    {
        try
        {
            await ready.WaitAsync(timeout);
        }
        catch (TimeoutException) when (requireRenderedVideoFrame)
        {
            // WaitAsync does not cancel the underlying task. Keep observing it:
            // otherwise a frame arriving after the timeout can signal readiness
            // but nobody remains to clear the seek indicator.
            await ready;
        }
    }

    private void EndSeekBuffering(int generation, TaskCompletionSource waiter)
    {
        bool changed;
        lock (_seekBufferGate)
        {
            if (generation != _seekGeneration || !ReferenceEquals(waiter, _seekReady)) return;
            changed = _seekBuffering;
            _seekBuffering = false;
            _seekReady = null;
        }
        if (changed && _state != MediaState.Loading)
            OnUi(() => RaisePropertyChanged(IsBufferingProperty, true, IsBuffering));
    }

    private void CancelSeekBuffering()
    {
        TaskCompletionSource? waiter;
        bool changed;
        lock (_seekBufferGate)
        {
            ++_seekGeneration;
            waiter = _seekReady;
            _seekReady = null;
            changed = _seekBuffering;
            _seekBuffering = false;
        }
        waiter?.TrySetCanceled();
        if (changed && _state != MediaState.Loading)
            OnUi(() => RaisePropertyChanged(IsBufferingProperty, true, IsBuffering));
    }

    private readonly record struct SeekBufferingRequest(int Generation, TaskCompletionSource Waiter, long StartedTimestamp);

    private void BeginFirstVideoFrameWaitIfNeeded()
    {
        if (this is not VideoPlayer) return;

        bool changed;
        lock (_seekBufferGate)
        {
            changed = !_awaitingFirstVideoFrame;
            if (changed)
            {
                _awaitingFirstVideoFrame = true;
                _firstVideoFrameWaitStartedTimestamp = Stopwatch.GetTimestamp();
                _lastRenderedFrameTimestamp = 0;
                _lastRenderedProgressPosition = null;
                _lastRenderedProgressContentHash = 0;
            }
        }
        if (changed) OnUi(() => RaisePropertyChanged(IsBufferingProperty, false, IsBuffering));
        StartBufferingMonitor();
    }

    private void BeginNetworkPlaybackWait()
    {
        var old = IsBuffering;
        _lastPlaybackProgressTimestamp = Stopwatch.GetTimestamp();
        _lastObservedAudioPosition = _position;
        _networkBuffering = MediaSource.IsStreaming(Source);
        if (old != IsBuffering)
            OnUi(() => RaisePropertyChanged(IsBufferingProperty, old, IsBuffering));
    }

    private void SetPlaybackOperationBuffering(bool value)
    {
        var old = IsBuffering;
        _playbackOperationBuffering = value;
        if (old != IsBuffering)
            OnUi(() => RaisePropertyChanged(IsBufferingProperty, old, IsBuffering));
    }

    private void StartBufferingMonitor()
    {
        OnUi(() =>
        {
            if (_bufferingMonitor is not null) return;
            _bufferingMonitor = new DispatcherTimer(
                TimeSpan.FromMilliseconds(200),
                DispatcherPriority.Background,
                (_, _) => UpdateBufferingFromRenderedFrame());
            _bufferingMonitor.Start();
        });
    }

    private void StopBufferingMonitor()
    {
        OnUi(() =>
        {
            _bufferingMonitor?.Stop();
            _bufferingMonitor = null;
            var old = IsBuffering;
            _networkBuffering = false;
            _awaitingFirstVideoFrame = false;
            _playbackOperationBuffering = false;
            if (old != IsBuffering)
            {
                RaisePropertyChanged(IsBufferingProperty, old, IsBuffering);
            }
        });
    }

    private void UpdateBufferingFromRenderedFrame()
    {
        if (_disposed || !_isPlaying) return;

        var hasRenderedFrame = _lastRenderedFrameTimestamp != 0;
        if (this is VideoPlayer && _awaitingFirstVideoFrame && hasRenderedFrame)
        {
            var old = IsBuffering;
            _awaitingFirstVideoFrame = false;
            if (old != IsBuffering)
                RaisePropertyChanged(IsBufferingProperty, old, IsBuffering);
        }

        if (!MediaSource.IsStreaming(Source)) return;
        var progressTimestamp = this is VideoPlayer
            ? _lastRenderedFrameTimestamp
            : _lastPlaybackProgressTimestamp;
        var elapsed = progressTimestamp == 0
            ? TimeSpan.MaxValue
            : Stopwatch.GetElapsedTime(progressTimestamp);
        var stalled = elapsed >= TimeSpan.FromMilliseconds(NetworkBufferingThresholdMilliseconds);
        if (stalled == _networkBuffering) return;
        var wasBuffering = IsBuffering;
        _networkBuffering = stalled;
        if (wasBuffering != IsBuffering)
            RaisePropertyChanged(IsBufferingProperty, wasBuffering, IsBuffering);
    }

    private void VideoFrameSurfaceRendered(object? sender, EventArgs e)
    {
        if (!_videoFrameNeedsRender) return;
        _videoFrameNeedsRender = false;
        if (_lastVideoFrameCapturedTimestamp < _firstVideoFrameWaitStartedTimestamp) return;

        // Count only a genuinely advancing frame as playback progress. Windows
        // capture can keep returning the same old bitmap while an online source
        // is buffering; treating every capture callback as progress made the
        // spinner disappear even though the picture was still frozen.
        if (HasMeaningfulVideoFrameProgress(
                _lastRenderedFrameTimestamp,
                _lastRenderedProgressPosition,
                _lastRenderedProgressContentHash,
                _lastVideoFramePosition,
                _lastVideoFrameContentHash))
        {
            _lastRenderedFrameTimestamp = Stopwatch.GetTimestamp();
            _lastPlaybackProgressTimestamp = _lastRenderedFrameTimestamp;
            _lastRenderedProgressPosition = _lastVideoFramePosition;
            _lastRenderedProgressContentHash = _lastVideoFrameContentHash;
        }
        // Render may record progress, but must never change a bound property:
        // the loading overlay's visibility invalidates layout during the pass.
        // Capture this frame's identity so a queued notification cannot certify
        // a newer frame (or a replacement Source) which has not rendered yet.
        var generation = _openGeneration;
        var position = _lastVideoFramePosition;
        var capturedTimestamp = _lastVideoFrameCapturedTimestamp;
        var contentHash = _lastVideoFrameContentHash;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || generation != _openGeneration) return;
            MarkSeekReady(position, capturedTimestamp, contentHash);
            UpdateBufferingFromRenderedFrame();
        }, DispatcherPriority.Background);
    }

    internal static bool HasMeaningfulVideoFrameProgress(
        long lastProgressTimestamp,
        TimeSpan? previousPosition,
        ulong previousContentHash,
        TimeSpan? currentPosition,
        ulong currentContentHash)
    {
        if (lastProgressTimestamp == 0) return true;
        if (currentPosition is { } position
            && (previousPosition is null
                || position > previousPosition.Value + TimeSpan.FromMilliseconds(15)
                || position < previousPosition.Value - TimeSpan.FromMilliseconds(100)))
            return true;
        return currentContentHash != 0 && currentContentHash != previousContentHash;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        UnhookTemplateParts();
        base.OnApplyTemplate(e);
        _playButton = e.NameScope.Find<Button>("PART_PlayPauseButton");
        _centerPlayButton = e.NameScope.Find<Button>("PART_CenterPlayPauseButton");
        _stopButton = e.NameScope.Find<Button>("PART_StopButton");
        _muteButton = e.NameScope.Find<Button>("PART_MuteButton");
        _fullScreenButton = e.NameScope.Find<Button>("PART_FullScreenButton");
        _seekSlider = e.NameScope.Find<Slider>("PART_SeekSlider");
        _volumeSlider = e.NameScope.Find<Slider>("PART_VolumeSlider");
        _videoSurface = e.NameScope.Find<NativeMediaSurface>("PART_VideoSurface");
        _videoFrameSurface = e.NameScope.Find<VideoFrameSurface>("PART_VideoFrameSurface");
        if (_backend is IGpuVideoBackend gpuBackend) gpuBackend.SetPresentationSurface(_videoFrameSurface);
        if (_videoFrameSurface is not null) _videoFrameSurface.FrameRendered += VideoFrameSurfaceRendered;
        _controlOverlay = e.NameScope.Find<Control>("PART_ControlOverlay");
        if (_videoSurface is not null) _videoSurface.Backend = _backend;
        if (_videoSurface is not null)
        {
            _videoSurface.PointerMoved += PlayerPointerMoved;
            _videoSurface.PointerEntered += PlayerPointerEntered;
        }
        if (_playButton is not null) _playButton.Click += PlayClick;
        if (_centerPlayButton is not null) _centerPlayButton.Click += PlayClick;
        if (_stopButton is not null) _stopButton.Click += StopClick;
        if (_muteButton is not null) _muteButton.Click += MuteClick;
        if (_fullScreenButton is not null) _fullScreenButton.Click += FullScreenClick;
        if (_seekSlider is not null)
        {
            _seekSlider.AddHandler(PointerPressedEvent, SeekPressed, RoutingStrategies.Tunnel);
            _seekSlider.AddHandler(PointerReleasedEvent, SeekReleased, RoutingStrategies.Tunnel);
        }
        if (_volumeSlider is not null) _volumeSlider.ValueChanged += VolumeSliderChanged;
        AddHandler(PointerMovedEvent, PlayerPointerMoved, RoutingStrategies.Bubble);
        AddHandler(PointerEnteredEvent, PlayerPointerEntered, RoutingStrategies.Bubble);
        AddHandler(DoubleTappedEvent, PlayerDoubleTapped, RoutingStrategies.Bubble);
        _controlsTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(2600), DispatcherPriority.Background, (_, _) =>
        {
            // Browser-style behavior: keep controls available while paused or ended.
            if (IsPlaying && (_controlOverlay is null || !_controlOverlay.IsPointerOver)) SetControlsVisible(false);
        });
        UpdateTemplateParts();
    }
    private void PlayClick(object? s, global::Avalonia.Interactivity.RoutedEventArgs e) => _ = TogglePlayPause();
    private void StopClick(object? s, global::Avalonia.Interactivity.RoutedEventArgs e) => _ = StopAsync();
    private void MuteClick(object? s, global::Avalonia.Interactivity.RoutedEventArgs e) => Muted = !Muted;
    private void FullScreenClick(object? s, global::Avalonia.Interactivity.RoutedEventArgs e) => ToggleFullscreen();
    private void SeekPressed(object? s, PointerPressedEventArgs e)
    {
        if (_seekSlider is null || _seekSlider.Bounds.Width <= 0) return;
        var point = e.GetPosition(_seekSlider);
        var ratio = Math.Clamp(point.X / _seekSlider.Bounds.Width, 0, 1);
        var value = _seekSlider.Minimum + (_seekSlider.Maximum - _seekSlider.Minimum) * ratio;
        _seekSlider.Value = value;
        e.Handled = true;
    }
    private void SeekReleased(object? s, PointerReleasedEventArgs e) { if (_seekSlider is not null) Seek(TimeSpan.FromSeconds(_seekSlider.Value)); }
    private void VolumeSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        Volume = Math.Clamp(e.NewValue, 0, 1);
        if (e.NewValue <= 0.0001) Muted = true;
        else if (Muted) Muted = false;
    }

    // SVG-compatible path data keeps every default button icon vector-based
    // and independent from emoji/font availability on the host platform.
    private const string SvgPlay = "M 5 3 L 19 12 L 5 21 Z";
    private const string SvgPause = "M 4 3 H 9 V 21 H 4 Z M 15 3 H 20 V 21 H 15 Z";
    private const string SvgStop = "M 5 5 H 19 V 19 H 5 Z";
    private const string SvgVolume = "M 3 9 H 7 L 12 4 V 20 L 7 15 H 3 Z M 15 9 H 17 V 15 H 15 Z M 19 6 H 21 V 18 H 19 Z";
    private const string SvgMute = "M 3 9 H 7 L 12 4 V 20 L 7 15 H 3 Z M 14 9 L 20 15 L 18 17 L 12 11 Z M 20 9 L 14 15 L 12 13 L 18 7 Z";
    private const string SvgFullscreen = "M 3 3 H 10 V 5 H 5 V 10 H 3 Z M 14 3 H 21 V 10 H 19 V 5 H 14 Z M 3 14 H 5 V 19 H 10 V 21 H 3 Z M 19 14 H 21 V 21 H 14 V 19 H 19 Z";

    private static SvgPath CreateSvgIcon(string pathData, double size)
        => new()
        {
            Data = StreamGeometry.Parse(pathData),
            Fill = Brushes.White,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            IsHitTestVisible = false
        };

    private void UpdateTemplateParts()
    {
        if (_playButton is not null) _playButton.Content = CreateSvgIcon(IsPlaying ? SvgPause : SvgPlay, 18);
        if (_centerPlayButton is not null) _centerPlayButton.IsVisible = CenterPlayVisible;
        if (_muteButton is not null) _muteButton.Content = CreateSvgIcon(Muted || Volume <= 0 ? SvgMute : SvgVolume, 18);
        if (_volumeSlider is not null)
        {
            _updating = true;
            try { _volumeSlider.Value = Math.Clamp(Volume, 0, 1); }
            finally { _updating = false; }
        }
        if (_seekSlider is not null && !_seekSlider.IsPointerOver) { _seekSlider.Maximum = Math.Max(1, DurationSeconds); _seekSlider.Value = PositionSeconds; }
    }
    private void UnhookTemplateParts()
    {
        RemoveHandler(PointerMovedEvent, PlayerPointerMoved);
        RemoveHandler(PointerEnteredEvent, PlayerPointerEntered);
        RemoveHandler(DoubleTappedEvent, PlayerDoubleTapped);
        _controlsTimer?.Stop();
        _controlsTimer = null;
        if (_videoSurface is not null)
        {
            _videoSurface.PointerMoved -= PlayerPointerMoved;
            _videoSurface.PointerEntered -= PlayerPointerEntered;
            _videoSurface.Backend = null;
        }
        _videoSurface = null;
        if (_videoFrameSurface is not null) _videoFrameSurface.FrameRendered -= VideoFrameSurfaceRendered;
        _videoFrameSurface = null;
        if (_backend is IGpuVideoBackend gpuBackend) gpuBackend.SetPresentationSurface(null);
        _controlOverlay = null;
        if (_playButton is not null) _playButton.Click -= PlayClick;
        if (_centerPlayButton is not null) _centerPlayButton.Click -= PlayClick;
        if (_stopButton is not null) _stopButton.Click -= StopClick;
        if (_muteButton is not null) _muteButton.Click -= MuteClick;
        if (_fullScreenButton is not null) _fullScreenButton.Click -= FullScreenClick;
        if (_seekSlider is not null)
        {
            _seekSlider.RemoveHandler(PointerPressedEvent, SeekPressed);
            _seekSlider.RemoveHandler(PointerReleasedEvent, SeekReleased);
        }
        if (_volumeSlider is not null) _volumeSlider.ValueChanged -= VolumeSliderChanged;
        _volumeSlider = null;
    }
    private void SetPosition(TimeSpan value)
    {
        if (!_updating && Math.Abs((value - _position).TotalMilliseconds) > 500) Seek(value);
    }
    private void SetIsPlaying(bool value) { if (!_updating && value != _isPlaying) _ = (value ? PlayAsync() : PauseAsync()); }
    private void PlayerPointerMoved(object? sender, PointerEventArgs e) { SetControlsVisible(true); _controlsTimer?.Stop(); _controlsTimer?.Start(); }
    private void PlayerPointerEntered(object? sender, PointerEventArgs e) { SetControlsVisible(true); _controlsTimer?.Stop(); _controlsTimer?.Start(); }
    private void PlayerDoubleTapped(object? sender, TappedEventArgs e) => ToggleFullscreen();
    private void SetControlsVisible(bool value) { if (_controlsVisible == value) return; var old = ControlsVisible; _controlsVisible = value; RaisePropertyChanged(ControlsVisibleProperty, old, ControlsVisible); }
    /// <summary>将宿主窗口切换到全屏。<para>Switches the host window to fullscreen.</para></summary>
    public void EnterFullscreen()
    {
        if (TopLevel.GetTopLevel(this) is Window window && window.WindowState != WindowState.FullScreen)
        {
            _previousWindowState = window.WindowState;
            window.WindowState = WindowState.FullScreen;
        }
        FullScreenRequested?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>退出全屏并恢复之前的窗口状态。<para>Exits fullscreen and restores the previous window state.</para></summary>
    public void ExitFullscreen()
    {
        if (TopLevel.GetTopLevel(this) is Window window && window.WindowState == WindowState.FullScreen)
            window.WindowState = _previousWindowState;
        FullScreenRequested?.Invoke(this, EventArgs.Empty);
    }
    /// <summary>切换全屏和普通窗口状态。<para>Toggles between fullscreen and the normal window state.</para></summary>
    public void ToggleFullscreen()
    {
        if (TopLevel.GetTopLevel(this) is Window window && window.WindowState == WindowState.FullScreen) ExitFullscreen();
        else EnterFullscreen();
    }
    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? value.ToString(@"hh\:mm\:ss") : value.ToString(@"mm\:ss");
    private void Subscribe(IMediaBackend b)
    {
        if (b is IGpuVideoBackend gpu)
            gpu.GpuFramePresented += (_, frame) => OnUi(() =>
            {
                if (_disposed) return;
                if (_videoFrame is { } cpuFrame)
                {
                    _videoFrame = null;
                    RaisePropertyChanged(VideoFrameProperty, cpuFrame, null);
                    cpuFrame.Dispose();
                }
                _lastVideoFramePosition = frame.Position;
                _lastVideoFrameCapturedTimestamp = frame.CapturedTimestamp;
                _lastVideoFrameContentHash = 0; // Media Engine provides the actual frame PTS.
                _videoFrameNeedsRender = true;
                VideoFrameSurfaceRendered(this, EventArgs.Empty);
            });
        b.Opened += (_, e) => OnUi(() => { SetState(MediaState.Ready); ClearRuntimeStatus(); Opened?.Invoke(this, e); });
        b.Playing += (_, e) => OnUi(() => { SetPlaying(true); SetState(MediaState.Playing); Playing?.Invoke(this, e); });
        b.Paused += (_, e) => OnUi(() => { StopBufferingMonitor(); SetPlaying(false); SetControlsVisible(true); SetState(MediaState.Paused); Paused?.Invoke(this, e); });
        b.Stopped += (_, e) => OnUi(() => { StopBufferingMonitor(); SetPlaying(false); SetControlsVisible(true); SetState(MediaState.Stopped); Stopped?.Invoke(this, e); });
        b.Ended += (_, e) => OnUi(async () => { StopBufferingMonitor(); SetPlaying(false); SetControlsVisible(true); SetState(MediaState.Ended); Ended?.Invoke(this, e); if (Loop) { Seek(TimeSpan.Zero); await PlayAsync(); } });
        b.PositionChanged += (_, e) =>
        {
            OnUi(() =>
            {
            var old = _position;
            _position = e.Position;
            RaisePropertyChanged(PositionProperty, old, _position);
            RaisePropertyChanged(PositionSecondsProperty, old.TotalSeconds, _position.TotalSeconds);
            RaisePropertyChanged(CurrentTimeProperty, old, _position);
            RaisePropertyChanged(CurrentTimeTextProperty, FormatTime(old), CurrentTimeText);
            var oldDuration = _duration;
            _duration = _backend.Duration;
            if (oldDuration != _duration)
            {
                RaisePropertyChanged(DurationProperty, oldDuration, _duration);
                RaisePropertyChanged(DurationSecondsProperty, oldDuration.TotalSeconds, _duration.TotalSeconds);
                RaisePropertyChanged(ProgressMaximumProperty, Math.Max(1, oldDuration.TotalSeconds), ProgressMaximum);
                RaisePropertyChanged(TotalTimeProperty, oldDuration, _duration);
                RaisePropertyChanged(DurationTextProperty, FormatTime(oldDuration), DurationText);
            }
            var oldBuffered = _buffered;
            _buffered = _backend.BufferedPosition;
            RaisePropertyChanged(BufferedPositionSecondsProperty, oldBuffered.TotalSeconds, _buffered.TotalSeconds);
            UpdateTemplateParts();
            PositionChanged?.Invoke(this, e);
            if (this is AudioPlayer && _isPlaying
                && e.Position > _lastObservedAudioPosition + TimeSpan.FromMilliseconds(15))
            {
                _lastPlaybackProgressTimestamp = Stopwatch.GetTimestamp();
                if (_networkBuffering)
                {
                    var wasBuffering = IsBuffering;
                    _networkBuffering = false;
                    RaisePropertyChanged(IsBufferingProperty, wasBuffering, IsBuffering);
                }
            }
            _lastObservedAudioPosition = e.Position;
            if (this is not VideoPlayer) MarkSeekReady();
            });
        };
        b.VolumeChanged += (_, e) => OnUi(() => { _updating = true; try { SetCurrentValue(VolumeProperty, e.Volume); } finally { _updating = false; } UpdateTemplateParts(); VolumeChanged?.Invoke(this, e); });
        b.VideoFrameAvailable += (_, e) =>
        {
            lock (_pendingFrameGate)
            {
                if (_disposed) { e.Frame.Dispose(); return; }
                _pendingVideoFrame?.Frame.Dispose();
                _pendingVideoFrame = e;
                if (_frameDeliveryScheduled) return;
                _frameDeliveryScheduled = true;
            }
            // Keep only the newest pending frame if the display cannot keep up.
            // Never enqueue an unbounded number of full-resolution bitmaps.
            Dispatcher.UIThread.Post(DeliverVideoFrame, DispatcherPriority.Background);
        };
        b.RuntimeStatusChanged += (_, e) => OnUi(() => { var old = _runtimeStatus; _runtimeStatus = e.Message; RaisePropertyChanged(RuntimeStatusProperty, old, _runtimeStatus); RaisePropertyChanged(RuntimeStatusVisibleProperty, !string.IsNullOrWhiteSpace(old), RuntimeStatusVisible); });
        b.Error += (_, e) => OnUi(() => { StopBufferingMonitor(); CancelSeekBuffering(); SetState(MediaState.Error); var old = _runtimeStatus; _runtimeStatus = e.Message; RaisePropertyChanged(RuntimeStatusProperty, old, _runtimeStatus); RaisePropertyChanged(RuntimeStatusVisibleProperty, !string.IsNullOrWhiteSpace(old), RuntimeStatusVisible); Error?.Invoke(this, e); });
    }
    private void DeliverVideoFrame()
    {
        VideoFrameEventArgs? frame;
        lock (_pendingFrameGate)
        {
            frame = _pendingVideoFrame;
            _pendingVideoFrame = null;
            _frameDeliveryScheduled = false;
        }
        if (frame is null) return;
        if (_disposed) { frame.Frame.Dispose(); return; }
        var old = _videoFrame;
        _videoFrame = frame.Frame;
        _lastVideoFramePosition = frame.Position;
        _lastVideoFrameCapturedTimestamp = frame.CapturedTimestamp;
        _lastVideoFrameContentHash = frame.ContentHash;
        _videoFrameNeedsRender = true;
        RaisePropertyChanged(VideoFrameProperty, old, _videoFrame);
        old?.Dispose();
    }

    private void SetPlaying(bool value) { _updating = true; try { var old = _isPlaying; _isPlaying = value; RaisePropertyChanged(IsPlayingProperty, old, value); RaisePropertyChanged(CenterPlayVisibleProperty, !old, CenterPlayVisible); UpdateTemplateParts(); if (value) ScheduleVideoFrameClock(); } finally { _updating = false; } }

    private void ScheduleVideoFrameClock()
    {
        if (_disposed || !IsPlaying || _frameClockScheduled || _frameClockHost is null) return;
        _frameClockScheduled = true;
        _frameClockHost.RequestAnimationFrame(_ =>
        {
            _frameClockScheduled = false;
            if (_disposed || !IsPlaying || _frameClockHost is null) return;
            if (_backend is IVideoFrameClockBackend clock) clock.RequestVideoFrame();
            ScheduleVideoFrameClock();
        });
    }
    private void SetState(MediaState value)
    {
        var oldState = _state;
        var wasBuffering = IsBuffering;
        _state = value;
        RaisePropertyChanged(StateProperty, oldState, value);
        RaisePropertyChanged(IsBufferingProperty, wasBuffering, IsBuffering);
        var oldDuration = _duration;
        _duration = _backend.Duration;
        RaisePropertyChanged(DurationProperty, oldDuration, _duration);
        RaisePropertyChanged(DurationSecondsProperty, oldDuration.TotalSeconds, _duration.TotalSeconds);
        RaisePropertyChanged(ProgressMaximumProperty, Math.Max(1, oldDuration.TotalSeconds), ProgressMaximum);
        RaisePropertyChanged(TotalTimeProperty, oldDuration, _duration);
        RaisePropertyChanged(DurationTextProperty, FormatTime(oldDuration), DurationText);
        var oldBuffered = _buffered;
        _buffered = _backend.BufferedPosition;
        RaisePropertyChanged(BufferedPositionProperty, oldBuffered, _buffered);
        RaisePropertyChanged(BufferedPositionSecondsProperty, oldBuffered.TotalSeconds, _buffered.TotalSeconds);
        UpdateTemplateParts();
    }
    private void ClearRuntimeStatus()
    {
        if (string.IsNullOrEmpty(_runtimeStatus)) return;
        var old = _runtimeStatus;
        _runtimeStatus = string.Empty;
        RaisePropertyChanged(RuntimeStatusProperty, old, _runtimeStatus);
        RaisePropertyChanged(RuntimeStatusVisibleProperty, true, false);
    }
    private void OnUi(Action action) { if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(action); }
    protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this is VideoPlayer && _backend is IVideoFrameClockBackend clock)
        {
            _frameClockHost = TopLevel.GetTopLevel(this);
            clock.SetExternalFrameClock(_frameClockHost is not null);
            ScheduleVideoFrameClock();
        }
        // OnDetachedFromVisualTree normally runs during window shutdown, but
        // some desktop lifetime/platform combinations close the native window
        // before Avalonia detaches every child.  Observe Closed as a second,
        // host-level safety net so audio/video is always released.
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            _hostWindow = window;
            window.Closed += HostWindowClosed;
        }
    }
    private void HostWindowClosed(object? sender, EventArgs e) => Dispose();
    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_hostWindow is not null)
        {
            _hostWindow.Closed -= HostWindowClosed;
            _hostWindow = null;
        }
        // A media control may own native decoder/audio processes. Detaching from
        // the window must synchronously release them so closing a window cannot
        // leave audio playing in the background.
        Dispose();
    }
    /// <summary>异步释放播放器及其后端资源。<para>Asynchronously releases the player and backend resources.</para></summary>
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    /// <summary>同步释放播放器及其后端资源。<para>Synchronously releases the player and backend resources.</para></summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _frameClockHost = null;
        lock (_pendingFrameGate)
        {
            _pendingVideoFrame?.Frame.Dispose();
            _pendingVideoFrame = null;
        }
        CancelSeekBuffering();
        StopBufferingMonitor();
        UnhookTemplateParts();
        if (_backendWorker is not null) _backendWorker.DisposeBackend(_backend);
        else _backend.Dispose();
    }

    private sealed class BackendWorker : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _thread;
        private int _disposed;

        public BackendWorker(string name)
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = name
            };
            if (OperatingSystem.IsWindows()) _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        public Task InvokeAsync(Func<Task> operation)
            => Enqueue(() => operation().GetAwaiter().GetResult());

        public Task InvokeAsync(Action operation)
            => Enqueue(operation);

        private Task Enqueue(Action operation)
        {
            if (Volatile.Read(ref _disposed) != 0)
                return Task.FromException(new ObjectDisposedException(nameof(BackendWorker)));

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _queue.Add(() =>
                {
                    try
                    {
                        operation();
                        completion.TrySetResult();
                    }
                    catch (Exception ex)
                    {
                        completion.TrySetException(ex);
                    }
                });
            }
            catch (InvalidOperationException)
            {
                completion.TrySetException(new ObjectDisposedException(nameof(BackendWorker)));
            }
            return completion.Task;
        }

        public void DisposeBackend(IMediaBackend backend)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var completion = new ManualResetEventSlim();
            try
            {
                _queue.Add(() =>
                {
                    try { backend.Dispose(); }
                    finally { completion.Set(); }
                });
                completion.Wait(TimeSpan.FromSeconds(5));
            }
            catch (InvalidOperationException) { }
            finally
            {
                _queue.CompleteAdding();
                if (Thread.CurrentThread != _thread) _thread.Join(1000);
                completion.Dispose();
            }
        }

        private void Run()
        {
            foreach (var operation in _queue.GetConsumingEnumerable()) operation();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _queue.CompleteAdding();
            if (Thread.CurrentThread != _thread) _thread.Join(1000);
            _queue.Dispose();
        }
    }
}
