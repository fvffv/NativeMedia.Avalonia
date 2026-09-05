using NativeMedia.Avalonia;
using NativeMedia.Avalonia.Windows;

if (!OperatingSystem.IsWindows()) return 0;
using var backend = new WindowsMediaFoundationBackend();
MediaErrorEventArgs? error = null;
backend.Error += (_, e) => error = e;
var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify.wav");
await backend.OpenAsync(media);
if (error is not null || backend.State != MediaState.Ready) return 2;
await backend.PlayAsync();
await Task.Delay(100);
await backend.PauseAsync();
if (backend.Position <= TimeSpan.Zero) return 3;
await backend.StopAsync();
if (backend.State != MediaState.Stopped) return 4;
var video = @"D:\JiJiDown\Download\平均的Linux用户：_34540094956.mp4";
if (File.Exists(video))
{
    using var videoBackend = new WindowsMediaFoundationBackend();
    await videoBackend.OpenAsync(video);
    await videoBackend.PlayAsync();
    await Task.Delay(1200);
    await videoBackend.PauseAsync();
}
return 0;
