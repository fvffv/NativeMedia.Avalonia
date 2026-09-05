using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NativeMedia.Avalonia.Demo.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] private string? videoSource;
    [ObservableProperty] private string? audioSource;
    [ObservableProperty] private TimeSpan videoPosition;
    [ObservableProperty] private TimeSpan audioPosition;
    [ObservableProperty] private double videoVolume = 1;
    [ObservableProperty] private double audioVolume = 1;
    [ObservableProperty] private bool videoIsPlaying;
    [ObservableProperty] private bool audioIsPlaying;
    [ObservableProperty] private bool autoPlay;
    [ObservableProperty] private bool loop;
    [ObservableProperty] private bool showControls = true;
    [ObservableProperty] private string status = "Ready — set VideoSource or AudioSource to a local media file.";
}
