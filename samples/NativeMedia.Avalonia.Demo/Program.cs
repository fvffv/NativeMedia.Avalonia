using Avalonia;
using System;

namespace NativeMedia.Avalonia.Demo;

using NativeMedia.Avalonia.Windows;
using NativeMedia.Avalonia.Linux;
using NativeMedia.Avalonia.macOS;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsWindows()) WindowsMediaFoundation.Use();
        else if (OperatingSystem.IsLinux()) LinuxFfmpeg.Use();
        else if (OperatingSystem.IsMacOS()) MacAvFoundation.Use();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
