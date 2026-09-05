using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Threading;

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

    private readonly IMediaBackend _backend;
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
    private WindowState _previousWindowState = WindowState.Normal;
    private Window? _hostWindow;

    protected MediaPlayer() { _backend = MediaBackendFactory.Create(); Subscribe(_backend); }
    /// <summary>当前注册的平台后端名称。<para>Name of the registered platform backend.</para></summary>
    public string BackendName => _backend.Name;
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
        else if (change.Property == VolumeProperty && !_updating && Math.Abs(change.GetNewValue<double>() - _backend.Volume) > 0.001) _backend.Volume = Volume;
        else if (change.Property == MutedProperty && !_updating && change.GetNewValue<bool>() != _backend.Muted)
        {
            _backend.Muted = Muted;
            UpdateTemplateParts();
        }
        else if (change.Property == ShowControlsProperty)
        {
            RaisePropertyChanged(ControlsVisibleProperty, _controlsVisible && change.GetOldValue<bool>(), ControlsVisible);
            RaisePropertyChanged(CenterPlayVisibleProperty, change.GetOldValue<bool>() && !_isPlaying, CenterPlayVisible);
        }
    }
    private Task OpenSourceAsync(string? source) => OpenSourceAsync(source, CancellationToken.None, _openGeneration);
    private async Task OpenSourceAsync(string? source, CancellationToken cancellationToken, int generation)
    {
        if (_disposed || string.IsNullOrWhiteSpace(source)) return;
        var oldStreaming = IsStreaming;
        var oldKind = SourceKind;
        SetState(MediaState.Loading);
        await _backend.OpenAsync(source, cancellationToken);
        if (generation != _openGeneration || _disposed) return;
        RaisePropertyChanged(IsStreamingProperty, oldStreaming, IsStreaming);
        RaisePropertyChanged(SourceKindProperty, oldKind, SourceKind);
        if (State == MediaState.Ready && AutoPlay) await PlayAsync();
    }
    private void StartOpenSource(string? source)
    {
        var generation = Interlocked.Increment(ref _openGeneration);
        _ = OpenSourceAsync(source, CancellationToken.None, generation);
    }
    /// <summary>打开本地路径、file URI、HTTP URL 或 HTTPS URL。<para>Opens a local path, file URI, HTTP URL, or HTTPS URL.</para></summary>
    /// <param name="source">媒体路径或 URL。<para>Media path or URL.</para></param>
    /// <param name="cancellationToken">取消打开操作的令牌。<para>Token used to cancel the open operation.</para></param>
    public async Task OpenAsync(string source, CancellationToken cancellationToken = default)
    {
        _suppressSourceOpen = true;
        try { SetCurrentValue(SourceProperty, source); }
        finally { _suppressSourceOpen = false; }
        var generation = Interlocked.Increment(ref _openGeneration);
        await OpenSourceAsync(source, cancellationToken, generation);
    }
    /// <summary>开始或继续播放。<para>Starts or resumes playback.</para></summary>
    public Task PlayAsync() => _backend.PlayAsync();
    /// <summary>暂停播放并保留当前位置。<para>Pauses playback and keeps the current position.</para></summary>
    public Task PauseAsync() => _backend.PauseAsync();
    /// <summary>停止播放。<para>Stops playback.</para></summary>
    public Task StopAsync() => _backend.StopAsync();
    /// <summary>跳转到指定播放位置。<para>Seeks to the specified playback position.</para></summary>
    /// <param name="position">目标位置；超出有效范围时由后端进行限制。<para>Target position; the backend clamps values outside the valid range.</para></param>
    public void Seek(TimeSpan position) => _backend.Seek(position);
    /// <summary>在播放和暂停之间切换。<para>Toggles between playing and paused states.</para></summary>
    public Task TogglePlayPause() => IsPlaying ? PauseAsync() : PlayAsync();
    /// <summary>向前跳转，默认 10 秒。<para>Seeks forward by 10 seconds by default.</para></summary>
    /// <param name="amount">跳转时长；省略时为 10 秒。<para>Seek amount; defaults to 10 seconds.</para></param>
    public void SeekForward(TimeSpan? amount = null) => Seek(Position + (amount ?? TimeSpan.FromSeconds(10)));
    /// <summary>向后跳转，默认 10 秒。<para>Seeks backward by 10 seconds by default.</para></summary>
    /// <param name="amount">跳转时长；省略时为 10 秒。<para>Seek amount; defaults to 10 seconds.</para></param>
    public void SeekBackward(TimeSpan? amount = null) => Seek(Position - (amount ?? TimeSpan.FromSeconds(10)));
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
        if (_seekSlider is not null) _seekSlider.AddHandler(PointerReleasedEvent, SeekReleased, RoutingStrategies.Tunnel);
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
    private void SeekReleased(object? s, PointerReleasedEventArgs e) { if (_seekSlider is not null) Seek(TimeSpan.FromSeconds(_seekSlider.Value)); }
    private void VolumeSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;
        Volume = Math.Clamp(e.NewValue, 0, 1);
        if (e.NewValue <= 0.0001) Muted = true;
        else if (Muted) Muted = false;
    }
    private void UpdateTemplateParts()
    {
        if (_playButton is not null) _playButton.Content = IsPlaying ? "❚❚" : "▶";
        if (_centerPlayButton is not null) _centerPlayButton.IsVisible = CenterPlayVisible;
        if (_muteButton is not null) _muteButton.Content = Muted || Volume <= 0 ? "🔇" : "🔊";
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
        _controlOverlay = null;
        if (_playButton is not null) _playButton.Click -= PlayClick;
        if (_centerPlayButton is not null) _centerPlayButton.Click -= PlayClick;
        if (_stopButton is not null) _stopButton.Click -= StopClick;
        if (_muteButton is not null) _muteButton.Click -= MuteClick;
        if (_fullScreenButton is not null) _fullScreenButton.Click -= FullScreenClick;
        if (_seekSlider is not null) _seekSlider.RemoveHandler(PointerReleasedEvent, SeekReleased);
        if (_volumeSlider is not null) _volumeSlider.ValueChanged -= VolumeSliderChanged;
        _volumeSlider = null;
    }
    private void SetPosition(TimeSpan value)
    {
        if (!_updating && Math.Abs((value - _position).TotalMilliseconds) > 500) _backend.Seek(value);
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
        b.Opened += (_, e) => OnUi(() => { SetState(MediaState.Ready); ClearRuntimeStatus(); Opened?.Invoke(this, e); });
        b.Playing += (_, e) => OnUi(() => { SetPlaying(true); SetState(MediaState.Playing); Playing?.Invoke(this, e); });
        b.Paused += (_, e) => OnUi(() => { SetPlaying(false); SetControlsVisible(true); SetState(MediaState.Paused); Paused?.Invoke(this, e); });
        b.Stopped += (_, e) => OnUi(() => { SetPlaying(false); SetControlsVisible(true); SetState(MediaState.Stopped); Stopped?.Invoke(this, e); });
        b.Ended += (_, e) => OnUi(async () => { SetPlaying(false); SetControlsVisible(true); SetState(MediaState.Ended); Ended?.Invoke(this, e); if (Loop) { Seek(TimeSpan.Zero); await PlayAsync(); } });
        b.PositionChanged += (_, e) => OnUi(() =>
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
        });
        b.VolumeChanged += (_, e) => OnUi(() => { _updating = true; try { SetCurrentValue(VolumeProperty, e.Volume); } finally { _updating = false; } UpdateTemplateParts(); VolumeChanged?.Invoke(this, e); });
        b.VideoFrameAvailable += (_, e) => OnUi(() => { var old = _videoFrame; _videoFrame = e.Frame; RaisePropertyChanged(VideoFrameProperty, old, _videoFrame); old?.Dispose(); });
        b.RuntimeStatusChanged += (_, e) => OnUi(() => { var old = _runtimeStatus; _runtimeStatus = e.Message; RaisePropertyChanged(RuntimeStatusProperty, old, _runtimeStatus); RaisePropertyChanged(RuntimeStatusVisibleProperty, !string.IsNullOrWhiteSpace(old), RuntimeStatusVisible); });
        b.Error += (_, e) => OnUi(() => { SetState(MediaState.Error); var old = _runtimeStatus; _runtimeStatus = e.Message; RaisePropertyChanged(RuntimeStatusProperty, old, _runtimeStatus); RaisePropertyChanged(RuntimeStatusVisibleProperty, !string.IsNullOrWhiteSpace(old), RuntimeStatusVisible); Error?.Invoke(this, e); });
    }
    private void SetPlaying(bool value) { _updating = true; try { var old = _isPlaying; _isPlaying = value; RaisePropertyChanged(IsPlayingProperty, old, value); RaisePropertyChanged(CenterPlayVisibleProperty, !old, CenterPlayVisible); UpdateTemplateParts(); } finally { _updating = false; } }
    private void SetState(MediaState value)
    {
        var oldState = _state;
        _state = value;
        RaisePropertyChanged(StateProperty, oldState, value);
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
    public async ValueTask DisposeAsync() { Dispose(); await _backend.DisposeAsync(); }
    /// <summary>同步释放播放器及其后端资源。<para>Synchronously releases the player and backend resources.</para></summary>
    public void Dispose() { if (_disposed) return; _disposed = true; UnhookTemplateParts(); _backend.Dispose(); }
}
