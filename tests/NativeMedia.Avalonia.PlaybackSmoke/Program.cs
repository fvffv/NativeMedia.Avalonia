using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;
using NativeMedia.Avalonia;
using NativeMedia.Avalonia.Windows;
using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    internal static string[] Arguments = [];
    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = args;
        WindowsMediaFoundation.Use();
        return AppBuilder.Configure<SmokeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}

internal sealed class SmokeApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Resources.MergedDictionaries.Add((ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri("avares://NativeMedia.Avalonia/Themes/Generic.axaml")));
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var lifetime = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        var video = Program.Arguments.Contains("--video");
        MediaPlayer player = video ? new VideoPlayer() : new AudioPlayer { IsVisible = false };
        player.AutoPlay = true;
        var window = new Window { Width = 640, Height = 420, Content = player, Title = "NativeMedia playback regression" };
        lifetime.MainWindow = window;
        window.Opened += async (_, _) =>
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await Verify(player, video).WaitAsync(deadline.Token);
                Console.WriteLine("PASS");
                lifetime.Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL: " + ex);
                lifetime.Shutdown(1);
            }
        };
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task Verify(MediaPlayer player, bool video)
    {
        var source = Program.Arguments.FirstOrDefault(a => !a.StartsWith("--"))
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
        string? error = null;
        player.Error += (_, e) => { error = e.Message; Console.WriteLine("MEDIA ERROR: " + error); };
        var bufferingChanges = 0;
        player.PropertyChanged += (_, e) =>
        {
            if (e.Property == MediaPlayer.IsBufferingProperty)
            {
                bufferingChanges++;
                Console.WriteLine($"BUFFERING={player.IsBuffering}, position={player.Position.TotalSeconds:F3}");
            }
        };
        var start = Stopwatch.StartNew();
        var opening = player.OpenAsync(source);
        Console.WriteLine($"OpenAsync synchronous time={start.Elapsed.TotalMilliseconds:F1}ms");
        await opening;
        Console.WriteLine($"OPEN state={player.State}, duration={player.Duration.TotalSeconds:F3}");
        float peak = 0;
        var rendered = false;
        var advanced = false;
        for (var i = 0; i < (video ? 180 : 60); i++)
        {
            await Task.Delay(100);
            peak = Math.Max(peak, ProcessAudioMeter.ReadPeak());
            advanced |= player.Position > TimeSpan.FromMilliseconds(250);
            rendered |= video && player.VideoFrame is not null && !player.IsBuffering;
            if (i % 20 == 0) Console.WriteLine($"SAMPLE state={player.State}, position={player.Position.TotalSeconds:F3}, peak={peak:F6}, rendered={rendered}");
            if (error is not null) throw new InvalidOperationException(error);
            if (video && rendered && advanced && i > 40) break;
        }
        Console.WriteLine($"RESULT peak={peak:F6}, advanced={advanced}, rendered={rendered}, bufferingChanges={bufferingChanges}");
        if (!advanced) throw new InvalidOperationException("Native playback position never advanced.");
        if (!video && peak <= 0.00001f) throw new InvalidOperationException("No audio signal in this process's WASAPI session.");
        if (video && !rendered) throw new InvalidOperationException("No rendered video frame cleared buffering.");
        if (video)
        {
            player.Seek(TimeSpan.FromSeconds(5));
            await Task.Delay(1800);
            await player.PauseAsync();
            await Task.Delay(200);
            await player.PlayAsync();
            await Task.Delay(1800);
            if (error is not null) throw new InvalidOperationException(error);
            Console.WriteLine($"AFTER SEEK/RESUME position={player.Position.TotalSeconds:F3}, buffering={player.IsBuffering}");
            if (player.Position <= TimeSpan.FromSeconds(5) || player.IsBuffering)
                throw new InvalidOperationException("Video did not resume after seeking.");
        }
        await player.StopAsync();
    }
}

// Read only this process's audio session, so unrelated desktop sounds cannot
// make a silent player pass. Raw COM calls also work in a NativeAOT smoke build.
internal static unsafe class ProcessAudioMeter
{
    private static readonly Guid EnumeratorClass = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid EnumeratorInterface = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid ManagerInterface = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    private static readonly Guid Control2Interface = new("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
    private static readonly Guid MeterInterface = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
    public static float ReadPeak()
    {
        nint enumerator = 0, device = 0, manager = 0, sessions = 0;
        try
        {
            var cls = EnumeratorClass; var iid = EnumeratorInterface;
            Marshal.ThrowExceptionForHR(CoCreateInstance(&cls, 0, 1, &iid, &enumerator));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int, int, nint*, int>)Table(enumerator)[4])(enumerator, 0, 1, &device));
            iid = ManagerInterface;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, Guid*, uint, nint, nint*, int>)Table(device)[3])(device, &iid, 23, 0, &manager));
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(manager)[5])(manager, &sessions));
            int count = 0;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int*, int>)Table(sessions)[3])(sessions, &count));
            float peak = 0;
            for (var i = 0; i < count; i++)
            {
                nint control = 0, control2 = 0, meter = 0;
                try
                {
                    Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, int, nint*, int>)Table(sessions)[4])(sessions, i, &control));
                    iid = Control2Interface;
                    if (Query(control, &iid, &control2) < 0) continue;
                    uint pid = 0;
                    if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)Table(control2)[14])(control2, &pid) < 0 || pid != Environment.ProcessId) continue;
                    iid = MeterInterface;
                    if (Query(control, &iid, &meter) < 0) continue;
                    float value = 0;
                    Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<nint, float*, int>)Table(meter)[3])(meter, &value));
                    peak = Math.Max(peak, value);
                }
                finally { Release(meter); Release(control2); Release(control); }
            }
            return peak;
        }
        finally { Release(sessions); Release(manager); Release(device); Release(enumerator); }
    }
    private static nint* Table(nint value) => *(nint**)value;
    private static int Query(nint value, Guid* iid, nint* result)
        => ((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(value)[0])(value, iid, result);
    private static void Release(nint value) { if (value != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Table(value)[2])(value); }
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(Guid* cls, nint outer, uint context, Guid* iid, nint* result);
}
