using NativeMedia.Avalonia;
using NativeMedia.Avalonia.Linux;

var source = args.Length > 0 ? args[0] : "https://cdn.example.test/media.mp4";
var renderFrames = args.Contains("--render", StringComparer.Ordinal);
if (renderFrames) Environment.SetEnvironmentVariable("AVALONIA_NATIVE_MEDIA_RENDER_TEST", "1");
else Environment.SetEnvironmentVariable("AVALONIA_NATIVE_MEDIA_HEADLESS", "1");
using var backend = new LinuxFfmpegBackend();
MediaErrorEventArgs? error = null;
backend.Error += (_, e) => error = e;
if (!backend.IsAvailable)
{
    Console.WriteLine($"ffmpeg-unavailable: {LinuxFfmpegBackend.CheckEnvironment().Error}");
    return 2;
}
await backend.OpenAsync(source);
Console.WriteLine($"opened state={backend.State} streaming={backend.IsStreaming} duration={backend.Duration} error={error?.Message}");
if (error is not null || backend.State != MediaState.Ready) return 3;
var frames = 0;
backend.VideoFrameAvailable += (_, e) => { Interlocked.Increment(ref frames); e.Frame.Dispose(); };
await backend.PlayAsync();
await Task.Delay(1200);
await backend.PauseAsync();
Console.WriteLine($"paused position={backend.Position} duration={backend.Duration}");
if (renderFrames && frames == 0) return 5;
return backend.Position >= TimeSpan.Zero ? 0 : 4;
