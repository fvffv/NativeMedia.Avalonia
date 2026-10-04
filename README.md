# NativeMedia.Avalonia

[English](#english) · [Chinese](#中文)

## English

A lightweight audio and video playback library for **Avalonia 12.1.1 / .NET 10**. It provides `VideoPlayer` and `AudioPlayer` controls backed by Windows Media Foundation, macOS AVFoundation, or Linux FFmpeg.

This guide describes the source and API used by package **1.0.17**.

Version 1.0.17 extends the Windows Media Engine/D3D11 path with optional Linux libmpv/OpenGL presentation and macOS AVFoundation/IOSurface import. All three paths remain in Avalonia's composition tree. Hardware decoding still depends on the codec, native player, GPU and driver. CPU fallback remains available.

Version 1.0.14 fixes automatic backend registration for .NET 11 and platform-specific target frameworks such as `net10.0-windows` and `net11.0-windows`. The library still targets .NET 10; compatible newer applications do not need to register the backend manually. Earlier packages restricted registration to exactly `net10.0`, which could leave audio and video without native output.

### Features

- Local files, file URIs, HTTP, and HTTPS media sources.
- Playback, pause, stop, seeking, looping, volume, and mute.
- Bindable playback quality: Original, High, Balanced, and Smooth.
- MVVM bindings, playback events, time displays, and buffering status.
- Overlay controls, fullscreen actions, and ordinary Avalonia controls over video.
- NativeAOT-compatible backend registration through the NuGet package.
- Optional installation of missing Linux FFmpeg through the system package manager.

### Installation and setup

Install the package from your configured NuGet sources:

```bash
dotnet add package NativeMedia.Avalonia --version 1.0.17
```

When using a locally built package, add this repository's `artifacts/packages` directory as a NuGet source first. The package contains the core library and all three platform adapters; it does not bundle FFmpeg or operating-system media frameworks.

Merge the player resources into the existing `App.axaml` resource dictionary:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://NativeMedia.Avalonia/Themes/Generic.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

Keep your application's existing styles and resource dictionaries. The sample application uses `FluentTheme`.

The NuGet package automatically registers the backend for the current operating system. No manual platform switch is required when consuming the package.

### Quick start

Add `xmlns:media="using:NativeMedia.Avalonia"` to your window or user control, then use:

```xml
<media:VideoPlayer x:Name="Player"
                   Source="{Binding VideoSource}"
                   PlaybackQuality="Original"
                   AutoPlay="True"
                   ShowControls="True"
                   Volume="0.8" />
```

For audio:

```xml
<media:AudioPlayer Source="{Binding AudioSource}"
                   AutoPlay="True"
                   ShowControls="True" />
```

`VideoSource` and `AudioSource` are strings containing a local path or a media URL. Codec and streaming-format support depend on the selected native backend. `AudioPlayer` disables video decoding/capture; `PlaybackQuality` has no effect on audio.

Alternatively, open and control the named player from an async UI event handler:

```csharp
using NativeMedia.Avalonia;

Player.AutoPlay = false;
Player.PlaybackQuality = VideoPlaybackQuality.Original;
await Player.OpenAsync("https://example.com/video.mp4");
await Player.PlayAsync();
Player.Seek(TimeSpan.FromSeconds(30));
await Player.PauseAsync();
```

Replace the example URL with an accessible media source. Setting `Source` starts opening asynchronously; `AutoPlay` determines whether successful opening also starts playback. Use `OpenAsync` when you need to await the open operation, and inspect `State` or handle `Error` for failures.

### Common writable properties

These properties support XAML values, bindings, and C# assignment.

| Property | Type | Default | Purpose |
| --- | --- | --- | --- |
| `Source` | `string?` | `null` | Local path, file URI, or HTTP(S) URL. Changing it opens the new source. |
| `AutoPlay` | `bool` | `false` | Start playback after opening a source successfully. |
| `PlaybackQuality` | `VideoPlaybackQuality` | `Original` | Video output quality; supports changes during playback. |
| `Volume` | `double` | `1.0` | Volume from 0 to 1; out-of-range values are clamped. |
| `Muted` | `bool` | `false` | Mute audio without changing `Volume`. |
| `ShowControls` | `bool` | `true` | Show or hide the default player controls. |
| `Loop` | `bool` | `false` | Restart from the beginning when playback ends. |

### Playback quality

| Value | Resolution limit | Frame-rate limit |
| --- | --- | --- |
| `Original` | Source dimensions; no intentional downscaling | No intentional frame-rate reduction |
| `High` | Long edge 1920, short edge 1080 | Up to 60 fps |
| `Balanced` | Long edge 1280, short edge 720 | Up to 30 fps |
| `Smooth` | Long edge 960, short edge 540 | Up to 24 fps |

Presets preserve aspect ratio, support portrait video, and do not enlarge smaller sources. They change local playback output, not the media file or network download bitrate.

```csharp
Player.PlaybackQuality = VideoPlaybackQuality.High;
Player.PlaybackQuality = VideoPlaybackQuality.Balanced;
Player.PlaybackQuality = VideoPlaybackQuality.Smooth;
Player.PlaybackQuality = VideoPlaybackQuality.Original;
```

For MVVM, bind `PlaybackQuality="{Binding SelectedPlaybackQuality}"` to a `VideoPlaybackQuality` property.

`Original` is the original-quality/lossless-playback preset in the sense that it adds no resolution reduction or intentional frame-rate reduction. The current BGRA composition path does not promise bit-exact HDR/10-bit preservation. Actual display frame rate depends on decoding, copying, UI rendering, and display refresh rate. If rendering cannot keep up, the control retains only the newest pending frame instead of accumulating full-resolution images.

Windows uses native-sized D3D11 textures in `Original` mode and performs preset scaling on the GPU, using the compositor's DXGI adapter and a bounded three-texture pool. Windows and macOS request frames at the host's rendering cadence. Linux libmpv renders directly into an Avalonia OpenGL framebuffer, with preset resolution/frame-rate limits on presentation (not the source decoder). Its FFmpeg fallback transfers uncompressed BMP frames; `Original` adds no scaling or frame-selection filter. Changing quality on that fallback restarts FFmpeg at the current position and can briefly buffer. On macOS, a preset that requires downscaling uses CPU bitmap scaling; `Original` and sources already within the preset dimensions can use IOSurface import.

### GPU playback and diagnostics

Use the normal `VideoPlayer` template, attach it to a window, and keep `PlaybackQuality="Original"` for 4K/high-frame-rate sources. No extra video HWND is placed above Avalonia: overlays, controls, clipping and transforms stay in the Avalonia visual/composition tree. Windows 8+ Media Engine, a hardware D3D11 video device, and an Avalonia renderer supporting D3D11 shared textures/keyed mutexes are required for this path. Remote desktops, software renderers, unavailable GPU interop, or device failures can select CPU capture instead; `RuntimeStatus` and `PlaybackStatistics.FallbackReason` explain why.

```csharp
var stats = Player.PlaybackStatistics;
// RenderingPath: "D3D11 shared texture / Media Engine" when the GPU path is active.
// SubmittedFrames / CompositedFrames: source-session counters, not monitor scan-out FPS.
// PresentationBusyCount: frame requests skipped while all textures are busy; NOT decoder drops.
// LastTransferMilliseconds: CPU time submitting the last GPU transfer, not GPU execution time.
```

`PlaybackStatistics` is a C# snapshot getter (poll when needed), not a bindable live FPS property. `VideoFrame` stays null on the GPU path: CPU readback is deliberately omitted. GPU-to-GPU transfer/color conversion/composition still occurs; this is **no CPU readback**, not literally zero GPU copies. Output is currently BGRA8 SDR, not bit-exact 10-bit/HDR passthrough. A 120 Hz or faster display/compositor and hardware codec support are necessary to display 4K120; zero dropped frames cannot be promised on arbitrary hardware. This release has been compiled, but 4K120 playback and overlay performance require validation on the target machine.

#### Linux: libmpv / OpenGL

The default `LinuxFfmpegBackend` now first tries the system `libmpv.so.2` or `libmpv.so.1` for an attached video control. Install the **libmpv runtime** provided by your distribution, plus an appropriate GPU/VA-API/NVIDIA driver. Installing only the `mpv` executable is not sufficient if it does not provide the shared library. The library does not download or install libmpv. An Avalonia renderer capable of OpenGL composition/texture sharing is required; Intel VA-API interop generally requires EGL. X11/Wayland display connections are supplied to the render API and released with the renderer.

libmpv owns both audio and video timing. `hwdec=auto-safe` requests a supported hardware decoder; software decoding remains possible. Read `PlaybackStatistics.RenderingPath`: `hwdec=vaapi`, `nvdec`, etc. identify the selected decoder; `hwdec=no` means software decoding despite GPU display. A `-copy` decoder may read decoded frames back to CPU memory. Only the active non-copy hardware path avoids that decoder readback. OpenGL initialization failure falls back to FFmpeg; later renderer/player failures attempt to continue from the current position using FFmpeg. `AVALONIA_NATIVE_MEDIA_DISABLE_MPV=1` forces the legacy path for troubleshooting. Headless and audio-only playback still use FFmpeg.

On this path, frame counters count Render API updates, not confirmed compositor completions or physical display scanout. The busy counter records coalesced update notifications; it is not a dropped-frame counter. `LastTransferMilliseconds` measures CPU render-submission time. The Demo displays these diagnostics below the video.

#### macOS: AVFoundation / IOSurface

The player requests IOSurface-backed, Metal-compatible BGRA CoreVideo buffers and imports them through Avalonia's supported IOSurface interface. AVPlayer continues to handle playback/audio synchronization; no Metal window is placed over the UI. Buffers are retained until compositor import/update/disposal completes, with one pending import to bound memory. Unsupported interop, missing IOSurfaces or device loss falls back to CPU bitmaps. Switching source/seek invalidates queued older frames. Both Apple Silicon and Intel CMTime calling conventions are handled.

Use `PlaybackQuality="Original"` for the high-frame-rate GPU path. Presets requiring downscaling currently use the CPU scaling fallback on macOS; the reason is exposed in `PlaybackStatistics.FallbackReason`. AVFoundation chooses the decoder; IOSurface presentation alone does not prove hardware decoding. BGRA8 output is not HDR passthrough. Linux/macOS code has been cross-compiled on Windows, **not playback-tested on either target OS**. Validate local playback, audio sync, seek, pause, looping, overlays, fullscreen and shutdown on the target machine before release.

### Playback bindings and time values

The following properties expose C# getters, but their registered Avalonia properties also accept updates through bindings:

| Property | Type | Binding behavior | C# alternative |
| --- | --- | --- | --- |
| `Position` | `TimeSpan` | Seek when the bound position changes. | `Seek(position)` |
| `PositionSeconds` | `double` | Seek using seconds. | `Seek(TimeSpan.FromSeconds(seconds))` |
| `IsPlaying` | `bool` | Play or pause when the bound value changes. | `PlayAsync()` / `PauseAsync()` |

Do not write `Player.Position = ...` or `Player.IsPlaying = ...` in C#; these CLR properties have no setter. Use the methods above. In XAML, explicitly specify `Mode=TwoWay` when both the player and view model update the value:

```xml
<media:VideoPlayer Source="{Binding VideoSource}"
                   PlaybackQuality="{Binding SelectedPlaybackQuality}"
                   AutoPlay="True"
                   Position="{Binding PlaybackPosition, Mode=TwoWay}"
                   IsPlaying="{Binding IsPlaying, Mode=TwoWay}"
                   Volume="{Binding Volume, Mode=TwoWay}"
                   Muted="{Binding IsMuted, Mode=TwoWay}" />
```

Here `PlaybackPosition` is a `TimeSpan`, `IsPlaying` and `IsMuted` are `bool`, and `Volume` is a `double`. Writable view-model properties should notify changes.

| Read-only property | Type | Meaning |
| --- | --- | --- |
| `Duration` / `TotalTime` | `TimeSpan` | Total media duration; zero while unknown. |
| `DurationSeconds` | `double` | Total duration in seconds. |
| `CurrentTime` | `TimeSpan` | Alias of `Position`. |
| `CurrentTimeText` / `DurationText` | `string` | Formatted `mm:ss` or `hh:mm:ss` text. |
| `BufferedPosition` / `BufferedPositionSeconds` | `TimeSpan` / `double` | Backend-reported or estimated buffered position. |
| `ProgressMaximum` | `double` | `Math.Max(1, DurationSeconds)`, suitable for a progress slider. |

```csharp
double elapsedSeconds = Player.PositionSeconds;
double totalSeconds = Player.DurationSeconds;
TimeSpan elapsed = Player.CurrentTime;
TimeSpan total = Player.TotalTime;
string timeLabel = $"{Player.CurrentTimeText} / {Player.DurationText}";
```

`BufferedPosition` is not a byte-accurate network buffer meter; a backend can report the full duration when detailed buffer information is unavailable.

### Status and diagnostics

| Property | Type | Meaning |
| --- | --- | --- |
| `State` | `MediaState` | `None`, `Loading`, `Ready`, `Playing`, `Paused`, `Stopped`, `Ended`, or `Error`. |
| `IsBuffering` | `bool` | Opening, waiting for a video frame, seeking, or waiting for playback progress. |
| `RuntimeStatus` / `RuntimeStatusVisible` | `string` / `bool` | Runtime preparation, installation, or diagnostic message and its visibility. |
| `BackendName` / `BackendAvailable` | `string` / `bool` | Selected backend name and runtime availability. |
| `IsStreaming` | `bool` | Whether the source is HTTP or HTTPS. |
| `SourceKind` | `MediaSourceKind` | `None`, `LocalFile`, `FileUri`, `Http`, or `Https`. |
| `ControlsVisible` / `CenterPlayVisible` | `bool` | Current visibility of the default control bar and center play button. |
| `VideoFrame` | `Bitmap?` | CPU fallback frame, owned by the player; null during GPU presentation. Do not dispose it yourself. |
| `PlaybackStatistics` | `VideoPlaybackStatistics?` | Windows rendering path and submission/composition counters; C# snapshot getter. |
| `UsesCompositedVideo` / `UsesNativeVideoSurface` | `bool` | Video presentation path; the default desktop backends use composited video. |

For video, buffering ends after a new frame actually passes through the Avalonia render path. After seeking, the indicator waits for the target picture; it does not simply disappear on a fixed timeout. During network playback, a gap of roughly 700 ms without meaningful progress can show the indicator again.

### Methods

| Method | Behavior |
| --- | --- |
| `OpenAsync(string source, CancellationToken cancellationToken = default)` | Open a source; obeys `AutoPlay`. |
| `PlayAsync()` | Start or resume playback. |
| `PauseAsync()` | Pause and retain the position. |
| `StopAsync()` | Stop and reset the position to zero. |
| `Seek(TimeSpan position)` | Request a seek; the backend clamps the target to the valid range. |
| `SeekForward(TimeSpan? amount = null)` | Seek forward; default amount is 10 seconds. |
| `SeekBackward(TimeSpan? amount = null)` | Seek backward; default amount is 10 seconds. |
| `TogglePlayPause()` | Return a `Task` that plays or pauses. |
| `EnterFullscreen()` / `ExitFullscreen()` / `ToggleFullscreen()` | Change the host window's fullscreen state. |
| `Dispose()` / `DisposeAsync()` | Release the backend and native playback resources. |

```csharp
await Player.TogglePlayPause();
Player.SeekForward();
Player.SeekBackward(TimeSpan.FromSeconds(5));
Player.EnterFullscreen();
Player.ExitFullscreen();
await Player.StopAsync();
```

Call control APIs and update control properties from the Avalonia UI thread. The control dispatches backend operations as needed; Windows video initialization and HWND work remain on the native UI thread. `Seek` requests work asynchronously and does not await the rendered target frame.

Detaching a control from its visual tree or closing its host window disposes playback resources. A disposed instance cannot be reused. For a control you manage outside the visual tree, call `Dispose` or `DisposeAsync` explicitly.

### Events

| Event | Event data | Meaning |
| --- | --- | --- |
| `Opened` | `EventArgs` | The backend reports that the media has opened; this is not a rendered-first-frame notification. |
| `Playing` / `Paused` / `Stopped` / `Ended` | `EventArgs` | Playback lifecycle notifications. |
| `PositionChanged` | `PositionChangedEventArgs.Position` | Updated playback position. |
| `VolumeChanged` | `VolumeChangedEventArgs.Volume` | Updated volume. |
| `Error` | `MediaErrorEventArgs.Message`, `Exception` | Playback or opening failure; `Exception` may be null. |
| `FullScreenRequested` | `EventArgs` | A fullscreen action was requested. |

```csharp
Player.Opened += (_, _) => System.Diagnostics.Debug.WriteLine("Media opened");
Player.PositionChanged += (_, e) => System.Diagnostics.Debug.WriteLine(e.Position);
Player.Error += (_, e) => System.Diagnostics.Debug.WriteLine(e.Message);
```

Some backend diagnostics are exposed through `RuntimeStatus` without changing `State` to `Error`. Monitor both when troubleshooting.

### Overlays and default controls

Video frames and default controls share the Avalonia render tree. Add normal controls above the player in the same `Grid`:

```xml
<Grid>
  <media:VideoPlayer Source="{Binding VideoSource}" AutoPlay="True" />
  <Border HorizontalAlignment="Left"
          VerticalAlignment="Top"
          Margin="16"
          Padding="8"
          Background="#99000000"
          IsHitTestVisible="False">
    <TextBlock Text="LIVE" Foreground="White" />
  </Border>
</Grid>
```

The default video controls overlay the bottom of the picture. They include seeking, time text, volume, mute, fullscreen, and a central play button. During playback, the controls hide after roughly 2.6 seconds without pointer activity and remain available while paused. Double-clicking the video toggles fullscreen.

Use `ShowControls="False"` to hide the default controls, or replace the `ControlTheme`. The built-in template is [Generic.axaml](src/NativeMedia.Avalonia/Themes/Generic.axaml).

### Platform requirements and Linux audio

| Platform | Default backend | Runtime |
| --- | --- | --- |
| Windows | `WindowsMediaFoundationBackend` | Media Engine + D3D11 video; MFPlay for audio/CPU fallback, including `mfplat.dll`. |
| macOS | `MacAvFoundationBackend` | System AVFoundation, CoreVideo, and QuartzCore frameworks. |
| Linux | `LinuxFfmpegBackend` | Optional system libmpv + GPU/OpenGL drivers for video; `ffmpeg`/`ffprobe` on `PATH` for fallback/audio-only playback, plus an audio service/device. |

On Ubuntu or Debian, install FFmpeg if needed:

```bash
sudo apt update
sudo apt install ffmpeg
```

By default, the component can request permission to install missing FFmpeg through a supported package manager. Control this before opening a source:

```csharp
MediaRuntimeOptions.AutoInstallLinuxRuntime = false;
```

The default is `true`. `LinuxRuntimeInstaller.EnsureFfmpegAsync()` is also available in `NativeMedia.Avalonia.Linux`. The installer recognizes apt, dnf/yum, pacman, zypper, apk, xbps, and emerge; installation depends on configured repositories, privileges, and network access.

The Linux FFmpeg fallback automatically prefers PulseAudio; PipeWire desktops can provide the same output through `pipewire-pulse`. Automatic selection can fall back to ALSA if Pulse output fails. These FFmpeg sink settings do not control libmpv, which selects its own native audio output.

| Environment variable | Purpose |
| --- | --- |
| `AVALONIA_NATIVE_MEDIA_AUDIO_SINK` | `auto` (default), `pulse`, `pipewire` (uses Pulse), or `alsa`. |
| `PULSE_SERVER` | Explicit Pulse server; takes precedence over automatic discovery. |
| `XDG_RUNTIME_DIR` | Current session's runtime directory, checked for `pulse/native`. |
| `SUDO_UID` | Helps locate the original user's runtime directory after elevation. |

When a root-launched desktop app has no session audio socket, the backend can discover a socket under `/run/user/<uid>/pulse/native` and set `PULSE_SERVER` for the FFmpeg audio child process. Explicit sink selection does not use the automatic ALSA fallback.

### Source references and custom backends

When referencing source projects directly, reference the core and the relevant platform project(s), then explicitly register the platform backend before creating a player, as in the demo:

```csharp
using NativeMedia.Avalonia.Windows;
using NativeMedia.Avalonia.Linux;
using NativeMedia.Avalonia.macOS;

if (OperatingSystem.IsWindows()) WindowsMediaFoundation.Use();
else if (OperatingSystem.IsLinux()) LinuxFfmpeg.Use();
else if (OperatingSystem.IsMacOS()) MacAvFoundation.Use();
```

For a custom backend, implement `IMediaBackend` and register a factory with `MediaBackendFactory.Register` before creating controls. Optional contracts are:

- `IVideoMediaBackend`: disable video work for audio-only playback.
- `IVideoQualityMediaBackend`: apply playback quality.
- `IVideoFrameClockBackend`: receive display-driven frame requests.

The Linux assembly also retains an optional GStreamer backend. Register it explicitly with `LinuxGStreamer.Use()` if your application is designed for that path; the default quality and composited-video behavior documented here describes the FFmpeg backend.

### Build and package

Run from the repository root:

```bash
dotnet restore NativeMedia.Avalonia.sln
dotnet build NativeMedia.Avalonia.sln -c Release
dotnet pack src/NativeMedia.Avalonia/NativeMedia.Avalonia.csproj -c Release --no-restore -p:Version=1.0.17
```

The explicit `Version` property sets the package version. Output:

```text
artifacts/packages/NativeMedia.Avalonia.1.0.17.nupkg
```

Run the unit tests separately when needed:

```bash
dotnet test tests/NativeMedia.Avalonia.Tests/NativeMedia.Avalonia.Tests.csproj -c Release
```

Run the demo:

```bash
dotnet run --project samples/NativeMedia.Avalonia.Demo/NativeMedia.Avalonia.Demo.csproj
```

A Windows x64 NativeAOT publishing example:

```bash
dotnet publish samples/NativeMedia.Avalonia.Demo/NativeMedia.Avalonia.Demo.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:StripSymbols=true -o artifacts/aot-win-x64
```

NativeAOT still requires the platform runtime components listed above. Platform-specific publishing needs the corresponding toolchain.

### Repository layout and license

```text
src/
  NativeMedia.Avalonia/          Core controls, properties, events, and themes
  NativeMedia.Avalonia.Windows/  Windows Media Foundation backend
  NativeMedia.Avalonia.Linux/    Linux FFmpeg backend and runtime installer
  NativeMedia.Avalonia.macOS/    macOS AVFoundation backend
samples/                         Demo application
tests/                           Unit tests and playback smoke applications
artifacts/packages/              Locally generated NuGet packages
```

Public control APIs include XML documentation for IDE IntelliSense. Keep the generated XML documentation beside the assemblies when distributing DLLs directly.

MIT License. See [LICENSE](LICENSE).

---

## 中文

面向 **Avalonia 12.1.1 / .NET 10** 的轻量级音视频播放组件库。提供 `VideoPlayer` 和 `AudioPlayer` 控件，分别调用 Windows Media Foundation、macOS AVFoundation 或 Linux FFmpeg 完成播放。

本文对应 **1.0.17** 版本的源码和 API。

1.0.17 在 Windows Media Engine／D3D11 路径基础上，新增可选的 Linux libmpv／OpenGL 显示与 macOS AVFoundation／IOSurface 导入。三端都保留在 Avalonia 合成树内。实际硬解能力仍取决于编码格式、原生播放器、显卡及驱动，并保留 CPU 回退。

1.0.14 修复了 .NET 11 以及 `net10.0-windows`、`net11.0-windows` 等平台专用目标框架的后端自动注册。库本身仍以 .NET 10 为目标，兼容的更高版本应用无需手动注册后端。旧包将自动注册限制为完全匹配 `net10.0`，其他兼容框架可能因此没有实际音视频输出。

### 功能

- 支持本地文件、文件 URI、HTTP 和 HTTPS 媒体地址。
- 支持播放、暂停、停止、跳转、循环、音量和静音。
- 提供可绑定的原画、高清、均衡、流畅四档播放质量。
- 提供 MVVM 绑定、播放事件、时间显示和缓冲状态。
- 默认悬浮控制栏、全屏操作，以及普通 Avalonia 控件覆盖视频。
- NuGet 包自动注册当前平台后端，兼容 NativeAOT。
- Linux 缺少 FFmpeg 时，可通过系统包管理器请求安装。

### 安装与初始化

从已配置的 NuGet 包源安装：

```bash
dotnet add package NativeMedia.Avalonia --version 1.0.17
```

使用本地生成的包时，请先把本仓库的 `artifacts/packages` 目录添加为 NuGet 包源。包内包含核心库和三个平台适配程序集，不附带 FFmpeg 或操作系统媒体框架。

将播放器资源合并到现有 `App.axaml` 的资源字典中：

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://NativeMedia.Avalonia/Themes/Generic.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

保留应用已有的样式和资源字典；示例程序使用 `FluentTheme`。

通过 NuGet 引用时，包会自动注册当前操作系统对应的播放后端，不需要手写平台判断。

### 快速开始

在窗口或用户控件上添加 `xmlns:media="using:NativeMedia.Avalonia"`，然后使用：

```xml
<media:VideoPlayer x:Name="Player"
                   Source="{Binding VideoSource}"
                   PlaybackQuality="Original"
                   AutoPlay="True"
                   ShowControls="True"
                   Volume="0.8" />
```

音频播放器：

```xml
<media:AudioPlayer Source="{Binding AudioSource}"
                   AutoPlay="True"
                   ShowControls="True" />
```

`VideoSource`、`AudioSource` 为本地路径或网络媒体 URL 字符串，具体编码和流媒体格式的支持由当前原生后端决定。`AudioPlayer` 会关闭视频解码和捕获，`PlaybackQuality` 不影响音频播放。

也可以在异步 UI 事件处理方法中操作上面命名为 `Player` 的控件：

```csharp
using NativeMedia.Avalonia;

Player.AutoPlay = false;
Player.PlaybackQuality = VideoPlaybackQuality.Original;
await Player.OpenAsync("https://example.com/video.mp4");
await Player.PlayAsync();
Player.Seek(TimeSpan.FromSeconds(30));
await Player.PauseAsync();
```

请将示例 URL 替换为实际可访问的媒体地址。设置 `Source` 会异步打开媒体；`AutoPlay` 决定打开成功后是否开始播放。需要等待打开操作时使用 `OpenAsync`，并通过 `State` 或 `Error` 事件判断失败情况。

### 常用可写属性

这些属性可以直接在 XAML 中设置、绑定，也可以在 C# 中赋值。

| 属性 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `Source` | `string?` | `null` | 本地路径、文件 URI 或 HTTP(S) URL，修改后打开新媒体。 |
| `AutoPlay` | `bool` | `false` | 媒体打开成功后是否自动播放。 |
| `PlaybackQuality` | `VideoPlaybackQuality` | `Original` | 视频输出质量，支持播放中切换。 |
| `Volume` | `double` | `1.0` | 音量范围 0 到 1，超出范围会被限制。 |
| `Muted` | `bool` | `false` | 静音，不改变 `Volume` 的值。 |
| `ShowControls` | `bool` | `true` | 是否显示默认播放器控制栏。 |
| `Loop` | `bool` | `false` | 播放结束后是否从头循环。 |

### 播放画质等级

| 值 | 分辨率上限 | 帧率上限 |
| --- | --- | --- |
| `Original` | 保留源分辨率，不主动缩小 | 不主动降帧 |
| `High` | 最长边 1920、最短边 1080 | 最高 60 fps |
| `Balanced` | 最长边 1280、最短边 720 | 最高 30 fps |
| `Smooth` | 最长边 960、最短边 540 | 最高 24 fps |

预设保持宽高比，兼容竖屏视频，不放大小于限制的源。等级只改变本地播放输出，不修改媒体文件，也不降低网络下载码率。

```csharp
Player.PlaybackQuality = VideoPlaybackQuality.High;
Player.PlaybackQuality = VideoPlaybackQuality.Balanced;
Player.PlaybackQuality = VideoPlaybackQuality.Smooth;
Player.PlaybackQuality = VideoPlaybackQuality.Original;
```

在 MVVM 中，可使用 `PlaybackQuality="{Binding SelectedPlaybackQuality}"`，绑定到类型为 `VideoPlaybackQuality` 的属性。

`Original` 是这里的“原画／无损播放”档，含义是不额外降低分辨率、不主动压低源帧率。当前使用 BGRA 图像合成，不承诺 HDR／10-bit 的逐位无损。实际显示帧率受解码、像素拷贝、UI 渲染和屏幕刷新率限制；当显示跟不上时，控件只保留最新待显示帧，避免原画帧在内存中无限堆积。

Windows 在 `Original` 档按原生尺寸创建 D3D11 纹理，其他质量档在 GPU 上缩放，解码设备匹配 Avalonia 显卡，并使用最多三张纹理。Windows 和 macOS 的取帧跟随宿主渲染节奏。Linux libmpv 直接渲染到 Avalonia OpenGL 帧缓冲，预设限制显示分辨率／帧率，而非源解码器。FFmpeg 回退仍使用未压缩 BMP 帧，原画档不添加缩放或抽帧滤镜；回退路径切换质量会在当前位置重启 FFmpeg，可能短暂缓冲。macOS 中需要缩小分辨率的预设使用 CPU 位图缩放；`Original` 及尺寸已在预设范围内的源可使用 IOSurface。

### GPU 播放与诊断

使用普通 `VideoPlayer` 模板并将控件挂到窗口中，播放 4K／高帧率视频时保留 `PlaybackQuality="Original"`。没有额外的视频 HWND 盖在 Avalonia 上面，覆盖层、按钮、裁剪和变换仍在 Avalonia 视觉／合成树中。GPU 路径需要 Windows 8+ Media Engine、支持视频的硬件 D3D11 设备，以及支持 D3D11 共享纹理／keyed mutex 的 Avalonia 渲染器。远程桌面、软件渲染、互操作不可用或设备故障时可能回退 CPU 截图，原因见 `RuntimeStatus` 和 `PlaybackStatistics.FallbackReason`。

```csharp
var stats = Player.PlaybackStatistics;
// RenderingPath 为 "D3D11 shared texture / Media Engine" 时正在使用 GPU 路径。
// SubmittedFrames / CompositedFrames：当前媒体的提交／合成计数，不代表屏幕实际刷新帧率。
// PresentationBusyCount：纹理全忙而跳过的取帧请求数，不是解码器丢帧数。
// LastTransferMilliseconds：CPU 提交上一次 GPU 传输的耗时，不是 GPU 执行耗时。
```

`PlaybackStatistics` 是按需读取的 C# 快照属性，并非可绑定的实时 FPS 属性。GPU 路径下 `VideoFrame` 为 null，避免为了暴露位图而回读像素。仍有 GPU 内部传输、色彩转换和合成，因此准确说法是**不回读 CPU**，不是 GPU 内部完全零复制。当前输出为 BGRA8 SDR，不是逐位无损的 10-bit／HDR 直通。显示 4K120 需要至少 120 Hz 的屏幕／合成器和相应硬解能力，不能承诺任意硬件零丢帧。本版本已编译，4K120 实际播放及覆盖层性能仍需在目标机器验证。

#### Linux：libmpv／OpenGL

默认 `LinuxFfmpegBackend` 现在优先为已挂载的视频控件尝试系统 `libmpv.so.2` 或 `libmpv.so.1`。请安装发行版提供的 **libmpv 运行库**及适配显卡的 GPU／VA-API／NVIDIA 驱动。仅安装 `mpv` 命令行程序不一定包含共享库。本组件不会下载或安装 libmpv。Avalonia 渲染器必须支持 OpenGL 合成／纹理共享；Intel VA-API 互操作通常需要 EGL。实现向渲染接口提供 X11／Wayland 显示连接，并随渲染器释放。

libmpv 同时负责音视频时钟，使用 `hwdec=auto-safe` 请求受支持的硬件解码，但仍可能选择软解。查看 `PlaybackStatistics.RenderingPath`：`hwdec=vaapi`、`nvdec` 等表示所选解码器，`hwdec=no` 表示“软解 + GPU 显示”；带 `-copy` 的解码器可能将解码帧回读 CPU。只有实际启用非 copy 硬解路径，才能避免这一步回读。OpenGL 初始化失败时回退 FFmpeg；播放中渲染器／播放器故障会尝试从当前位置继续使用 FFmpeg。设置 `AVALONIA_NATIVE_MEDIA_DISABLE_MPV=1` 可强制旧路径排查问题。无界面模式和纯音频播放仍使用 FFmpeg。

该路径的帧计数是 Render API 更新次数，不代表已完成合成或屏幕实际扫描输出；busy 计数表示合并的更新通知，不是丢帧数。`LastTransferMilliseconds` 是 CPU 提交渲染的耗时。Demo 在视频下方显示这些诊断信息。

#### macOS：AVFoundation／IOSurface

播放器请求 IOSurface 支持、兼容 Metal 的 BGRA CoreVideo 缓冲区，通过 Avalonia 的 IOSurface 接口导入。播放与音频同步继续由 AVPlayer 负责，不在 UI 上方叠加 Metal 原生窗口。缓冲区会持有到合成器完成导入、更新和释放；最多一个待处理导入，避免内存堆积。互操作不可用、没有 IOSurface 或设备丢失时回退 CPU 位图。切换来源／拖动进度会使已排队的旧帧失效，同时兼容 Apple Silicon 和 Intel 的 CMTime 调用约定。

高帧率 GPU 播放请用 `PlaybackQuality="Original"`。macOS 需要缩小分辨率的预设暂时走 CPU 缩放回退，原因可从 `PlaybackStatistics.FallbackReason` 查看。解码器由 AVFoundation 选择，IOSurface 显示本身不等于确认硬解；BGRA8 输出不是 HDR 直通。Linux／macOS 代码目前仅在 Windows 上完成交叉编译，**尚未在对应系统实测播放**。发布前请在目标机器验证本地播放、音画同步、拖动、暂停、循环、覆盖层、全屏和关闭释放。

### 播放绑定与时间属性

下面三个属性在 C# 中只有 getter，但注册的 Avalonia 属性允许通过绑定写入：

| 属性 | 类型 | 绑定写入的效果 | C# 中的对应方法 |
| --- | --- | --- | --- |
| `Position` | `TimeSpan` | 跳转到绑定的新位置。 | `Seek(position)` |
| `PositionSeconds` | `double` | 按秒跳转。 | `Seek(TimeSpan.FromSeconds(seconds))` |
| `IsPlaying` | `bool` | 根据绑定值播放或暂停。 | `PlayAsync()` / `PauseAsync()` |

不要在 C# 中写 `Player.Position = ...` 或 `Player.IsPlaying = ...`，因为这些 CLR 属性没有 setter；应使用对应方法。在 XAML 中，需要播放器和 ViewModel 相互更新时明确写出 `Mode=TwoWay`：

```xml
<media:VideoPlayer Source="{Binding VideoSource}"
                   PlaybackQuality="{Binding SelectedPlaybackQuality}"
                   AutoPlay="True"
                   Position="{Binding PlaybackPosition, Mode=TwoWay}"
                   IsPlaying="{Binding IsPlaying, Mode=TwoWay}"
                   Volume="{Binding Volume, Mode=TwoWay}"
                   Muted="{Binding IsMuted, Mode=TwoWay}" />
```

其中 `PlaybackPosition` 为 `TimeSpan`，`IsPlaying` 和 `IsMuted` 为 `bool`，`Volume` 为 `double`。ViewModel 中的可写属性应具备属性变更通知。

| 只读属性 | 类型 | 说明 |
| --- | --- | --- |
| `Duration` / `TotalTime` | `TimeSpan` | 媒体总时长，未知时为零。 |
| `DurationSeconds` | `double` | 总时长，单位秒。 |
| `CurrentTime` | `TimeSpan` | 等同于 `Position`。 |
| `CurrentTimeText` / `DurationText` | `string` | 格式化的 `mm:ss` 或 `hh:mm:ss` 文本。 |
| `BufferedPosition` / `BufferedPositionSeconds` | `TimeSpan` / `double` | 后端报告或估算的缓冲位置。 |
| `ProgressMaximum` | `double` | `Math.Max(1, DurationSeconds)`，可用作进度条最大值。 |

```csharp
double elapsedSeconds = Player.PositionSeconds;
double totalSeconds = Player.DurationSeconds;
TimeSpan elapsed = Player.CurrentTime;
TimeSpan total = Player.TotalTime;
string timeLabel = $"{Player.CurrentTimeText} / {Player.DurationText}";
```

`BufferedPosition` 不是精确到字节的网络缓存计量值；后端无法提供细节时，可能使用总时长作为缓冲位置。

### 状态与诊断属性

| 属性 | 类型 | 说明 |
| --- | --- | --- |
| `State` | `MediaState` | `None`、`Loading`、`Ready`、`Playing`、`Paused`、`Stopped`、`Ended` 或 `Error`。 |
| `IsBuffering` | `bool` | 正在打开媒体、等待视频帧、跳转，或等待播放进度恢复。 |
| `RuntimeStatus` / `RuntimeStatusVisible` | `string` / `bool` | 环境准备、安装或诊断文本，以及是否显示提示。 |
| `BackendName` / `BackendAvailable` | `string` / `bool` | 当前后端名称和运行环境可用性。 |
| `IsStreaming` | `bool` | 媒体源是否为 HTTP 或 HTTPS。 |
| `SourceKind` | `MediaSourceKind` | `None`、`LocalFile`、`FileUri`、`Http` 或 `Https`。 |
| `ControlsVisible` / `CenterPlayVisible` | `bool` | 当前默认控制栏和中央播放按钮的可见状态。 |
| `VideoFrame` | `Bitmap?` | CPU 回退帧，由播放器持有；GPU 显示时为 null，请勿自行释放。 |
| `PlaybackStatistics` | `VideoPlaybackStatistics?` | Windows 显示路径及提交／合成计数；C# 快照属性。 |
| `UsesCompositedVideo` / `UsesNativeVideoSurface` | `bool` | 视频显示方式；默认桌面后端使用合成视频。 |

视频缓冲状态会等待新画面真正进入 Avalonia 渲染流程后结束。跳转后也会等待目标位置的画面，不会仅依赖固定超时隐藏加载动画。网络播放期间约 700 毫秒没有有效进度时，可能再次显示缓冲状态。

### 常用方法

| 方法 | 说明 |
| --- | --- |
| `OpenAsync(string source, CancellationToken cancellationToken = default)` | 打开媒体，并遵循 `AutoPlay` 设置。 |
| `PlayAsync()` | 开始或继续播放。 |
| `PauseAsync()` | 暂停并保留当前进度。 |
| `StopAsync()` | 停止并将位置归零。 |
| `Seek(TimeSpan position)` | 请求跳转，后端会将目标限制在有效范围内。 |
| `SeekForward(TimeSpan? amount = null)` | 向前跳转，默认 10 秒。 |
| `SeekBackward(TimeSpan? amount = null)` | 向后跳转，默认 10 秒。 |
| `TogglePlayPause()` | 切换播放／暂停，返回 `Task`。 |
| `EnterFullscreen()` / `ExitFullscreen()` / `ToggleFullscreen()` | 调整宿主窗口的全屏状态。 |
| `Dispose()` / `DisposeAsync()` | 释放后端和原生播放资源。 |

```csharp
await Player.TogglePlayPause();
Player.SeekForward();
Player.SeekBackward(TimeSpan.FromSeconds(5));
Player.EnterFullscreen();
Player.ExitFullscreen();
await Player.StopAsync();
```

请在 Avalonia UI 线程调用控件 API 和修改控件属性。控件会按需调度后端操作；Windows 视频初始化和 HWND 操作仍保留在原生 UI 线程。`Seek` 只提交异步跳转请求，不会等待目标画面完成渲染。

控件从可视树移除或宿主窗口关闭时会释放播放资源，已释放的实例不能复用。如果在可视树之外自行管理播放器，请显式调用 `Dispose` 或 `DisposeAsync`。

### 事件

| 事件 | 事件参数 | 说明 |
| --- | --- | --- |
| `Opened` | `EventArgs` | 后端报告媒体已打开，不代表首帧已经渲染。 |
| `Playing` / `Paused` / `Stopped` / `Ended` | `EventArgs` | 播放生命周期通知。 |
| `PositionChanged` | `PositionChangedEventArgs.Position` | 当前播放位置更新。 |
| `VolumeChanged` | `VolumeChangedEventArgs.Volume` | 音量更新。 |
| `Error` | `MediaErrorEventArgs.Message`、`Exception` | 打开或播放失败；`Exception` 可能为空。 |
| `FullScreenRequested` | `EventArgs` | 请求执行全屏操作。 |

```csharp
Player.Opened += (_, _) => System.Diagnostics.Debug.WriteLine("媒体已打开");
Player.PositionChanged += (_, e) => System.Diagnostics.Debug.WriteLine(e.Position);
Player.Error += (_, e) => System.Diagnostics.Debug.WriteLine(e.Message);
```

部分后端诊断通过 `RuntimeStatus` 展示，不一定把 `State` 改成 `Error`。排查问题时请同时查看这两类信息。

### 视频覆盖与默认控制栏

视频帧和默认控制栏处于同一个 Avalonia 渲染树，在同一个 `Grid` 中添加普通控件即可覆盖视频：

```xml
<Grid>
  <media:VideoPlayer Source="{Binding VideoSource}" AutoPlay="True" />
  <Border HorizontalAlignment="Left"
          VerticalAlignment="Top"
          Margin="16"
          Padding="8"
          Background="#99000000"
          IsHitTestVisible="False">
    <TextBlock Text="直播中" Foreground="White" />
  </Border>
</Grid>
```

默认视频控制栏悬浮在画面底部，包含进度、时间、音量、静音、全屏和中央播放按钮。播放时鼠标停止活动约 2.6 秒后隐藏控制栏，暂停时保持可用；双击视频可以切换全屏。

使用 `ShowControls="False"` 隐藏默认控制栏，也可以替换 `ControlTheme`。内置模板见 [Generic.axaml](src/NativeMedia.Avalonia/Themes/Generic.axaml)。

### 平台要求与 Linux 音频

| 平台 | 默认后端 | 运行时要求 |
| --- | --- | --- |
| Windows | `WindowsMediaFoundationBackend` | Media Engine + D3D11 视频；MFPlay 音频／CPU 回退，包括 `mfplat.dll`。 |
| macOS | `MacAvFoundationBackend` | 系统 AVFoundation、CoreVideo 和 QuartzCore 框架。 |
| Linux | `LinuxFfmpegBackend` | 视频 GPU 路径可选安装系统 libmpv 和 GPU／OpenGL 驱动；回退／纯音频需要 `PATH` 中的 `ffmpeg`、`ffprobe`，另需音频服务或输出设备。 |

Ubuntu、Debian 可以按需安装 FFmpeg：

```bash
sudo apt update
sudo apt install ffmpeg
```

默认情况下，组件可以请求系统权限，通过受支持的包管理器安装缺失的 FFmpeg。在打开媒体之前设置：

```csharp
MediaRuntimeOptions.AutoInstallLinuxRuntime = false;
```

默认值是 `true`。`NativeMedia.Avalonia.Linux` 命名空间还提供 `LinuxRuntimeInstaller.EnsureFfmpegAsync()`。安装器识别 apt、dnf/yum、pacman、zypper、apk、xbps 和 emerge，能否安装取决于已配置的软件仓库、权限和网络。

Linux 的 FFmpeg 回退自动优先选择 PulseAudio；PipeWire 桌面可通过 `pipewire-pulse` 使用同一输出路径。自动模式下，Pulse 输出失败可回退 ALSA。这些 FFmpeg 音频输出设置不控制 libmpv，后者自行选择原生音频输出。

| 环境变量 | 说明 |
| --- | --- |
| `AVALONIA_NATIVE_MEDIA_AUDIO_SINK` | `auto`（默认）、`pulse`、`pipewire`（使用 Pulse）或 `alsa`。 |
| `PULSE_SERVER` | 显式指定 Pulse 服务，优先于自动发现。 |
| `XDG_RUNTIME_DIR` | 当前会话运行目录，用于检查 `pulse/native`。 |
| `SUDO_UID` | 提权后用于寻找原用户的运行目录。 |

以 root 启动的桌面程序没有当前会话音频 socket 时，后端可以查找 `/run/user/<uid>/pulse/native`，并仅给 FFmpeg 音频子进程设置 `PULSE_SERVER`。显式指定音频输出类型时，不使用自动 ALSA 回退。

### 源码引用与自定义后端

直接引用源码项目时，应引用核心库以及需要的平台项目，然后像示例程序一样，在创建播放器之前明确注册后端：

```csharp
using NativeMedia.Avalonia.Windows;
using NativeMedia.Avalonia.Linux;
using NativeMedia.Avalonia.macOS;

if (OperatingSystem.IsWindows()) WindowsMediaFoundation.Use();
else if (OperatingSystem.IsLinux()) LinuxFfmpeg.Use();
else if (OperatingSystem.IsMacOS()) MacAvFoundation.Use();
```

自定义后端需要实现 `IMediaBackend`，并在创建控件之前通过 `MediaBackendFactory.Register` 注册工厂。可选接口包括：

- `IVideoMediaBackend`：为纯音频播放关闭视频处理。
- `IVideoQualityMediaBackend`：应用播放画质。
- `IVideoFrameClockBackend`：接收显示刷新驱动的取帧请求。

Linux 程序集中还保留了可选的 GStreamer 后端。如果应用使用该路径，可以显式调用 `LinuxGStreamer.Use()`；本文描述的默认画质和合成视频行为对应 FFmpeg 后端。

### 源码构建与打包

在仓库根目录执行：

```bash
dotnet restore NativeMedia.Avalonia.sln
dotnet build NativeMedia.Avalonia.sln -c Release
dotnet pack src/NativeMedia.Avalonia/NativeMedia.Avalonia.csproj -c Release --no-restore -p:Version=1.0.17
```

命令中的 `Version` 参数明确指定包版本，输出位置为：

```text
artifacts/packages/NativeMedia.Avalonia.1.0.17.nupkg
```

需要时单独执行单元测试：

```bash
dotnet test tests/NativeMedia.Avalonia.Tests/NativeMedia.Avalonia.Tests.csproj -c Release
```

运行示例程序：

```bash
dotnet run --project samples/NativeMedia.Avalonia.Demo/NativeMedia.Avalonia.Demo.csproj
```

Windows x64 NativeAOT 发布示例：

```bash
dotnet publish samples/NativeMedia.Avalonia.Demo/NativeMedia.Avalonia.Demo.csproj -c Release -r win-x64 --self-contained true -p:PublishAot=true -p:StripSymbols=true -o artifacts/aot-win-x64
```

NativeAOT 仍需要上面列出的系统播放组件；面向不同平台发布时，需要相应平台的工具链。

### 目录结构与许可证

```text
src/
  NativeMedia.Avalonia/          核心控件、属性、事件和主题
  NativeMedia.Avalonia.Windows/  Windows Media Foundation 后端
  NativeMedia.Avalonia.Linux/    Linux FFmpeg 后端与运行时安装器
  NativeMedia.Avalonia.macOS/    macOS AVFoundation 后端
samples/                         示例程序
tests/                           单元测试与播放验证程序
artifacts/packages/              本地生成的 NuGet 包
```

公共控件 API 提供 XML 文档注释，供 IDE 显示 IntelliSense 提示。直接分发 DLL 时，请把生成的 XML 文档与对应程序集放在一起。

采用 MIT 许可证，详见 [LICENSE](LICENSE)。
