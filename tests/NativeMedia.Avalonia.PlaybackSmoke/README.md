# Windows playback regression

Run on a Windows desktop with an audio output device:

```powershell
dotnet run --project tests/NativeMedia.Avalonia.PlaybackSmoke -c Release
dotnet run --project tests/NativeMedia.Avalonia.PlaybackSmoke -c Release -- --video "<video URL or path>"
```

The audio case uses an invisible AudioPlayer with AutoPlay, as in drive-desktop.
It checks the process's own WASAPI audio-session peak, not just the software clock.
The video case loads the real default template and runs Avalonia's render loop,
checks the loading indicator clears after a frame, then seeks, pauses and resumes.
Both modes exit nonzero on failure and shut down their test window automatically.
The source is optional for audio and defaults to Windows/Media/Alarm01.wav.
