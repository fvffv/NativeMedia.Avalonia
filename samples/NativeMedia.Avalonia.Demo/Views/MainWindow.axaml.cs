using System;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Input;
using global::Avalonia.Interactivity;
using global::Avalonia.Layout;
using global::Avalonia.Threading;
using global::Avalonia.Platform.Storage;
using NativeMedia.Avalonia.Demo.ViewModels;
using NativeMedia.Avalonia;

namespace NativeMedia.Avalonia.Demo.Views;

public partial class MainWindow : Window
{
    private bool _videoFullscreen;
    private Vector _normalScrollOffset;
    private readonly DispatcherTimer _diagnosticsTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    public MainWindow()
    {
        InitializeComponent();
        _diagnosticsTimer.Tick += (_, _) =>
        {
            var stats = VideoPlayer.PlaybackStatistics;
            PlaybackDiagnostics.Text = stats is null ? "" : $"{stats.RenderingPath}\nFrames submitted: {stats.SubmittedFrames}; presentation updates: {stats.CompositedFrames}; busy/coalesced: {stats.PresentationBusyCount}"
                + (string.IsNullOrEmpty(stats.FallbackReason) ? "" : $"\nFallback: {stats.FallbackReason}");
        };
        _diagnosticsTimer.Start();
        VideoPlayer.FullScreenRequested += (_, _) => UpdateVideoFullscreenLayout();
        PropertyChanged += (_, change) =>
        {
            if (change.Property == WindowStateProperty) UpdateVideoFullscreenLayout();
        };
        AddHandler(KeyDownEvent, WindowKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Closing += (_, _) =>
        {
            _diagnosticsTimer.Stop();
            VideoPlayer.Dispose();
            AudioPlayer.Dispose();
        };
        VideoPlayer.Error += PlayerError;
        AudioPlayer.Error += PlayerError;
        VideoPlayer.Opened += (_, _) => SetStatus("Video is ready");
        VideoPlayer.Playing += (_, _) => SetStatus("Video is playing");
        AudioPlayer.Opened += (_, _) => SetStatus("Audio is ready");
    }

    private void UpdateVideoFullscreenLayout()
    {
        var fullscreen = WindowState == WindowState.FullScreen;
        if (fullscreen == _videoFullscreen) return;
        _videoFullscreen = fullscreen;
        if (fullscreen) _normalScrollOffset = DemoScroll.Offset;

        // Keep the same VideoPlayer attached: reparenting it would invoke Dispose
        // in OnDetachedFromVisualTree and destroy the active decoder/GPU surfaces.
        DemoHeader.IsVisible = !fullscreen;
        DemoFooter.IsVisible = !fullscreen;
        DemoLayout.Margin = fullscreen ? new Thickness(0) : new Thickness(28);
        DemoLayout.MaxWidth = fullscreen ? double.PositiveInfinity : 920;
        DemoLayout.RowSpacing = fullscreen ? 0 : 18;
        DemoLayout.RowDefinitions[1].Height = fullscreen ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        DemoScroll.VerticalScrollBarVisibility = fullscreen ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        VideoPlayer.Width = fullscreen ? double.NaN : 720;
        VideoPlayer.Height = fullscreen ? double.NaN : 400;
        VideoPlayer.HorizontalAlignment = fullscreen ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
        if (fullscreen) DemoScroll.Offset = default;
        else Dispatcher.UIThread.Post(() =>
        {
            // Restore only after the normal scroll extent has been measured again.
            if (!_videoFullscreen) DemoScroll.Offset = _normalScrollOffset;
        }, DispatcherPriority.Loaded);
    }

    private void WindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || WindowState != WindowState.FullScreen) return;
        VideoPlayer.ExitFullscreen();
        e.Handled = true;
    }

    private void PlayerError(object? sender, MediaErrorEventArgs e) => SetStatus(e.Message);
    private void SetStatus(string message) { if (DataContext is MainViewModel vm) vm.Status = message; }

    private async void ChooseVideo(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => await ChooseAsync(true);
    private async void ChooseAudio(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => await ChooseAsync(false);
    private async void PlayVideoUrl(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => await PlayUrlAsync(true, VideoUrlBox.Text);
    private async void PlayAudioUrl(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e) => await PlayUrlAsync(false, AudioUrlBox.Text);
    private async Task PlayUrlAsync(bool video, string? url)
    {
        if (DataContext is not MainViewModel vm || string.IsNullOrWhiteSpace(url)) return;
        if (!MediaSource.TryParse(url.Trim(), out _, out var kind, out var error) || kind is not (MediaSourceKind.Http or MediaSourceKind.Https))
        { vm.Status = error ?? "Only HTTP and HTTPS URLs can be streamed."; return; }
        vm.AutoPlay = true;
        if (video) await VideoPlayer.OpenAsync(url.Trim()); else await AudioPlayer.OpenAsync(url.Trim());
        vm.Status = $"Streaming {url.Trim()}";
    }
    private async Task ChooseAsync(bool video)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = video ? "Choose a video" : "Choose an audio file",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(video ? "Video" : "Audio") { Patterns = video ? ["*.mp4", "*.mkv", "*.webm", "*.mov", "*.avi"] : ["*.mp3", "*.wav", "*.flac", "*.m4a", "*.ogg"] }]
        });
        if (files.Count == 0 || DataContext is not MainViewModel vm) return;
        var path = files[0].TryGetLocalPath() ?? files[0].Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) { vm.Status = "The selected provider did not expose a local path."; return; }
        if (video) { vm.VideoSource = path; vm.AutoPlay = true; } else { vm.AudioSource = path; vm.AutoPlay = true; }
        vm.Status = $"Loaded {Path.GetFileName(path)}";
    }
}
