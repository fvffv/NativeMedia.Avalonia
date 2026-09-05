using System;
using System.IO;
using System.Threading.Tasks;
using global::Avalonia.Controls;
using global::Avalonia.Platform.Storage;
using NativeMedia.Avalonia.Demo.ViewModels;
using NativeMedia.Avalonia;

namespace NativeMedia.Avalonia.Demo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closing += (_, _) =>
        {
            VideoPlayer.Dispose();
            AudioPlayer.Dispose();
        };
        VideoPlayer.Error += PlayerError;
        AudioPlayer.Error += PlayerError;
        VideoPlayer.Opened += (_, _) => SetStatus("Video is ready");
        VideoPlayer.Playing += (_, _) => SetStatus("Video is playing");
        AudioPlayer.Opened += (_, _) => SetStatus("Audio is ready");
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
