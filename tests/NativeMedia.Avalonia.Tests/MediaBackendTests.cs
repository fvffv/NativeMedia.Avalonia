using NativeMedia.Avalonia;
using NativeMedia.Avalonia.Windows;
using NativeMedia.Avalonia.Linux;
using System.Diagnostics;

namespace NativeMedia.Avalonia.Tests;

public class MediaBackendTests
{
    [Fact]
    public async Task OpenMissingFileRaisesError()
    {
        using var backend = new NativeMediaBackend("test");
        MediaErrorEventArgs? error = null;
        backend.Error += (_, e) => error = e;
        await backend.OpenAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing"));
        Assert.Equal(MediaState.Error, backend.State);
        Assert.Contains("does not exist", error?.Message);
    }

    [Fact]
    public async Task ClockPauseAndSeekPreservePosition()
    {
        using var backend = new NativeMediaBackend("test");
        var file = Path.GetTempFileName();
        try
        {
            await backend.OpenAsync(file);
            await backend.PlayAsync();
            await Task.Delay(80);
            await backend.PauseAsync();
            var paused = backend.Position;
            Assert.InRange(paused.TotalMilliseconds, 20, 500);
            backend.Seek(TimeSpan.FromSeconds(3));
            Assert.Equal(TimeSpan.FromSeconds(3), backend.Position);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void VolumeIsClamped()
    {
        using var backend = new NativeMediaBackend("test");
        backend.Volume = 2;
        Assert.Equal(1, backend.Volume);
        backend.Volume = -1;
        Assert.Equal(0, backend.Volume);
    }

    [Theory]
    [InlineData("https://cdn.example.test/media/live.m3u8", MediaSourceKind.Https)]
    [InlineData("http://cdn.example.test/audio.mp3", MediaSourceKind.Http)]
    public void NetworkSourcesAreRecognizedWithoutLocalFileAccess(string source, MediaSourceKind expected)
    {
        Assert.True(MediaSource.TryParse(source, out var uri, out var kind, out var error), error);
        Assert.Equal(expected, kind);
        Assert.Equal(source, uri!.ToString());
        Assert.True(MediaSource.IsStreaming(source));
    }

    [Fact]
    public async Task NetworkSourceOpensWithoutDownloadingTheFile()
    {
        using var backend = new NativeMediaBackend("test");
        await backend.OpenAsync("https://cdn.example.test/video.mp4");
        Assert.Equal(MediaState.Ready, backend.State);
        Assert.True(backend.IsStreaming);
    }

    [Fact]
    public async Task WindowsMediaFoundationOpensSystemWav()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var backend = new WindowsMediaFoundationBackend();
        MediaErrorEventArgs? error = null;
        backend.Error += (_, e) => error = e;
        await backend.OpenAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify.wav"));
        Assert.Null(error);
        Assert.Equal(MediaState.Ready, backend.State);
        await backend.PlayAsync();
        await backend.PauseAsync();
        await backend.StopAsync();
    }

    [Fact]
    public async Task AudioPlayerDisablesVideoPipelineAndQueuesBlockingBackendWork()
    {
        var backend = new BlockingAudioBackend();
        MediaBackendFactory.Register(() => backend);
        var file = Path.GetTempFileName();
        try
        {
            using var player = new AudioPlayer();
            Assert.False(backend.VideoOutputEnabled);

            var callerThread = Environment.CurrentManagedThreadId;
            var stopwatch = Stopwatch.StartNew();
            var open = player.OpenAsync(file);
            var synchronousOpenTime = stopwatch.Elapsed;

            Assert.True(synchronousOpenTime < TimeSpan.FromMilliseconds(100),
                $"OpenAsync blocked the caller for {synchronousOpenTime.TotalMilliseconds:0} ms.");

            stopwatch.Restart();
            var play = player.PlayAsync();
            var synchronousPlayTime = stopwatch.Elapsed;

            Assert.True(synchronousPlayTime < TimeSpan.FromMilliseconds(100),
                $"PlayAsync blocked the caller for {synchronousPlayTime.TotalMilliseconds:0} ms.");

            await Task.WhenAll(open, play);
            Assert.NotEqual(callerThread, backend.OpenThreadId);
            Assert.NotEqual(callerThread, backend.PlayThreadId);
        }
        finally
        {
            MediaBackendFactory.Register(static () => new NativeMediaBackend("test"));
            File.Delete(file);
        }
    }

    [Fact]
    public void RepeatedFrozenVideoCaptureIsNotPlaybackProgress()
    {
        var position = TimeSpan.FromSeconds(12);
        Assert.False(MediaPlayer.HasMeaningfulVideoFrameProgress(
            lastProgressTimestamp: 1,
            previousPosition: position,
            previousContentHash: 42,
            currentPosition: position,
            currentContentHash: 42));
    }

    [Fact]
    public async Task VideoSeekStillObservesFrameThatArrivesAfterTimeout()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = MediaPlayer.WaitForSeekReadinessAsync(
            ready.Task,
            requireRenderedVideoFrame: true,
            TimeSpan.FromMilliseconds(20));

        await Task.Delay(60);
        Assert.False(waiting.IsCompleted);
        ready.SetResult();
        await waiting.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData("pulse", null, null, null, null, "pulse")]
    [InlineData("pipewire", null, null, null, null, "pulse")]
    [InlineData("alsa", "unix:/run/user/1/pulse/native", null, ":0", null, "alsa")]
    [InlineData(null, null, null, ":0", null, "pulse")]
    [InlineData("auto", null, null, null, "wayland-0", "pulse")]
    [InlineData(null, null, null, null, null, "alsa")]
    public void LinuxAudioSinkUsesDesktopServerAndHonorsOverrides(
        string? configured,
        string? pulseServer,
        string? xdgRuntimeDirectory,
        string? display,
        string? waylandDisplay,
        string expected)
    {
        var actual = LinuxFfmpegBackend.ResolveAudioSink(
            configured,
            pulseServer,
            xdgRuntimeDirectory,
            display,
            waylandDisplay,
            _ => false);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LinuxPulseServerKeepsExplicitConfiguration()
    {
        var actual = LinuxFfmpegBackend.ResolvePulseServer(
            " unix:custom-socket ",
            null,
            null,
            true,
            _ => false,
            _ => throw new InvalidOperationException("must not enumerate"));

        Assert.Equal("unix:custom-socket", actual);
    }

    [Fact]
    public void LinuxPulseServerUsesCurrentRuntimeSocketFirst()
    {
        var runtimeDirectory = Path.Combine("runtime", "current");
        var socket = Path.Combine(runtimeDirectory, "pulse", "native");
        var actual = LinuxFfmpegBackend.ResolvePulseServer(
            null,
            runtimeDirectory,
            "1000",
            true,
            path => path == socket,
            _ => throw new InvalidOperationException("must not enumerate"));

        Assert.Equal("unix:" + socket, actual);
    }

    [Fact]
    public void RootLinuxPlaybackFindsAnotherDesktopUsersPulseSocket()
    {
        var firstRuntime = Path.Combine("sessions", "1000");
        var socket = Path.Combine(firstRuntime, "pulse", "native");
        var actual = LinuxFfmpegBackend.ResolvePulseServer(
            null,
            Path.Combine("sessions", "0"),
            null,
            true,
            path => path == socket,
            _ =>
            [
                Path.Combine("sessions", "not-a-user"),
                Path.Combine("sessions", "1001"),
                firstRuntime
            ]);

        Assert.Equal("unix:" + socket, actual);
    }

    [Fact]
    public void NonRootLinuxPlaybackDoesNotSearchOtherUserSessions()
    {
        var actual = LinuxFfmpegBackend.ResolvePulseServer(
            null,
            Path.Combine("sessions", "2000"),
            null,
            false,
            _ => false,
            _ => throw new InvalidOperationException("must not enumerate"));

        Assert.Null(actual);
    }

    [Theory]
    [InlineData(12.000, 12.040, 42, 42)]
    [InlineData(12.000, 12.000, 42, 43)]
    public void AdvancingTimestampOrPictureIsPlaybackProgress(
        double previousSeconds,
        double currentSeconds,
        ulong previousHash,
        ulong currentHash)
    {
        Assert.True(MediaPlayer.HasMeaningfulVideoFrameProgress(
            lastProgressTimestamp: 1,
            previousPosition: TimeSpan.FromSeconds(previousSeconds),
            previousContentHash: previousHash,
            currentPosition: TimeSpan.FromSeconds(currentSeconds),
            currentContentHash: currentHash));
    }

    private sealed class BlockingAudioBackend : NativeMediaBackend, IVideoMediaBackend
    {
        public BlockingAudioBackend() : base("blocking-test") { }

        public bool VideoOutputEnabled { get; private set; } = true;
        public int OpenThreadId { get; private set; }
        public int PlayThreadId { get; private set; }

        public void ConfigureVideoOutput(bool enabled) => VideoOutputEnabled = enabled;

        public override async Task OpenAsync(string source, CancellationToken cancellationToken = default)
        {
            OpenThreadId = Environment.CurrentManagedThreadId;
            Thread.Sleep(180);
            await base.OpenAsync(source, cancellationToken);
        }

        public override Task PlayAsync(CancellationToken cancellationToken = default)
        {
            PlayThreadId = Environment.CurrentManagedThreadId;
            Thread.Sleep(180);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
