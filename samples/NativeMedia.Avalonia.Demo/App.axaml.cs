using System;
using Avalonia;
using global::Avalonia.Controls.ApplicationLifetimes;
using global::Avalonia.Markup.Xaml;
using NativeMedia.Avalonia.Demo.ViewModels;
using NativeMedia.Avalonia.Demo.Views;

namespace NativeMedia.Avalonia.Demo;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel
            {
                VideoSource = Environment.GetEnvironmentVariable("AVALONIA_NATIVE_MEDIA_VIDEO"),
                AutoPlay = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AVALONIA_NATIVE_MEDIA_VIDEO"))
            };
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
