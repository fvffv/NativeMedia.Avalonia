using NativeMedia.Avalonia;
using NativeMedia.Avalonia.Windows;

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
}
