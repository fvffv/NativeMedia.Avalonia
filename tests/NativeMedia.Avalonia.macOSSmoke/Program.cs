using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.ApplicationLifetimes;
using NativeMedia.Avalonia;
using NativeMedia.Avalonia.macOS;

if (!OperatingSystem.IsMacOS()) return 0;
if (args.Length == 0 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Usage: NativeMedia.Avalonia.macOSSmoke <media-file>");
    return 2;
}

SmokeApplication.MediaFile = args[0];
return AppBuilder.Configure<SmokeApplication>()
    .UsePlatformDetect()
    .StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);

sealed class SmokeApplication : Application
{
    internal static string MediaFile { get; set; } = string.Empty;
    private MacAvFoundationBackend? _backend;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        var window = new Window
        {
            Width = 320,
            Height = 180,
            ShowInTaskbar = false,
            Opacity = 0.01,
            Content = new TextBlock { Text = "AVFoundation smoke test" }
        };
        desktop.MainWindow = window;
        window.Opened += async (_, _) => await RunAsync(desktop);
        base.OnFrameworkInitializationCompleted();
    }

    private async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var exitCode = 1;
        try
        {
            Console.WriteLine("stage=backend-create");
            _backend = new MacAvFoundationBackend();
            var frames = 0;
            MediaErrorEventArgs? error = null;
            _backend.Error += (_, e) => error = e;
            _backend.VideoFrameAvailable += (_, e) =>
            {
                Interlocked.Increment(ref frames);
                e.Frame.Dispose();
            };

            Console.WriteLine("stage=open");
            await _backend.OpenAsync(MediaFile);
            if (_backend.State != MediaState.Ready || error is not null)
            {
                Console.Error.WriteLine($"open-failed state={_backend.State} error={error?.Message}");
                exitCode = 3;
                return;
            }

            Console.WriteLine("stage=play");
            await _backend.PlayAsync();
            await Task.Delay(3000);
            await _backend.PauseAsync();
            var firstPosition = _backend.Position;
            var duration = _backend.Duration;
            Console.WriteLine($"played state={_backend.State} position={firstPosition.TotalSeconds:F3}s duration={duration.TotalSeconds:F3}s frames={frames} error={error?.Message ?? "none"}");
            if (firstPosition < TimeSpan.FromSeconds(1) || frames == 0 || error is not null)
            {
                exitCode = 4;
                return;
            }

            _backend.Seek(TimeSpan.FromSeconds(1));
            await _backend.PlayAsync();
            await Task.Delay(500);
            await _backend.PauseAsync();
            Console.WriteLine($"seek position={_backend.Position.TotalSeconds:F3}s frames={frames}");
            exitCode = _backend.Position >= TimeSpan.FromSeconds(1) ? 0 : 5;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            exitCode = 6;
        }
        finally
        {
            _backend?.Dispose();
            _backend = null;
            desktop.Shutdown(exitCode);
        }
    }
}
