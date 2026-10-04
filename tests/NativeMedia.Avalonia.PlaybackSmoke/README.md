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

## NuGet backend registration (.NET 10 / .NET 11)

Add `artifacts/packages` as a NuGet source before using a locally packed version.
Package mode deliberately does not call `WindowsMediaFoundation.Use()` manually:
it must exercise the package's generated module initializer.

```powershell
dotnet run --project tests/NativeMedia.Avalonia.PlaybackSmoke -c Release -p:MediaPackageVersion=1.0.14 -p:SmokeTargetFramework=net11.0 -p:SmokeAvaloniaVersion=12.1.2 -- --registration-only
```

Use `net10.0`, `net10.0-windows`, or `net11.0-windows` to check another compatible target.
`--registration-only` verifies backend selection without opening a window or playing media.
Omit that flag to run the playback checks against the package. Package 1.0.13 on
`net11.0` reproduces the registration failure; 1.0.14 removes the exact-framework restriction.

## Windows GPU path (1.0.16)

```powershell
dotnet run --project tests/NativeMedia.Avalonia.PlaybackSmoke -c Release -- --video --require-gpu "D:\JiJiDown\Download\asd.mp4"
```

`--require-gpu` fails if no GPU frame was composited, instead of accepting a successful CPU fallback.
The result prints rendering-path counters. This short functional check is not proof of sustained
4K120 or zero dropped frames. Use an actual 4K120 source and a 120 Hz+ display, compare CPU and GPU
Video Decode usage, and inspect physical presentation with a GPU/display profiler for performance acceptance.
No actual playback/performance run was performed for the 1.0.16 change.

## 中文

音频检查使用不可见的 `AudioPlayer` 自动播放，通过当前进程的 WASAPI 音频峰值确认实际输出。
视频检查使用默认模板及 Avalonia 渲染循环，检查首帧、加载状态、定位、暂停和恢复。
音频未指定路径时使用 Windows 自带的 `Media/Alarm01.wav`。

使用本地 NuGet 包前，请将 `artifacts/packages` 加入包源。上面的命令用于检查 .NET 11
是否通过包内模块初始化器自动注册 Windows 后端，不会手动调用注册函数掩盖问题。
`--registration-only` 仅检查后端选择，不打开窗口或播放媒体；删除该参数可执行实际播放检查。
可替换 `SmokeTargetFramework` 检查 .NET 10 或带 `-windows` 后缀的目标框架。
1.0.13 在 `net11.0` 下可以复现注册失败，1.0.14 已移除目标框架完全匹配限制。

1.0.16 可使用上面的 `--video --require-gpu` 命令，若没有合成 GPU 帧则失败，避免把 CPU 回退误认为 GPU 成功。
输出包含显示路径及帧计数。这个短功能检查不等于持续 4K120 零丢帧验证；性能验收需要真正的 4K120 视频、
120 Hz 以上屏幕，并检查 CPU／GPU Video Decode 占用及实际显示时间线。本次未执行实际播放或性能测试。
