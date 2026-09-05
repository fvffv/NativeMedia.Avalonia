# NativeMedia.Avalonia

使用平台原生媒体引擎，提供统一的 Avalonia 播放体验。  
Native media backends. One unified Avalonia playback experience.

`NativeMedia.Avalonia` 是面向 Avalonia 12 / .NET 10 的轻量级跨平台音视频播放组件库。它不在 C# 中重新实现解码器，而是根据运行平台调用系统媒体能力，再把视频帧合成到 Avalonia 的渲染树中。

`NativeMedia.Avalonia` is a lightweight cross-platform audio/video playback library for Avalonia 12 and .NET 10. It uses the media stack provided by each operating system and composites decoded video frames into the Avalonia visual tree.

## 特性 / Features

- 统一的 `VideoPlayer` 和 `AudioPlayer` 控件 / Unified `VideoPlayer` and `AudioPlayer` controls
- 本地文件、`file://`、HTTP 和 HTTPS 网络流 / Local files, `file://`, HTTP, and HTTPS streams
- 播放、暂停、停止、Seek、循环、音量和静音 / Play, pause, stop, seeking, looping, volume, and mute
- MVVM 友好的 Avalonia 属性、TwoWay 绑定和事件 / MVVM-friendly Avalonia properties, TwoWay binding, and events
- 默认 HTML5 风格悬浮控制栏，可替换 `ControlTheme` / HTML5-style overlay controls with replaceable `ControlTheme`
- 视频画面和控制栏位于同一个 Avalonia 渲染树，任意原生 Avalonia 控件都可以覆盖在视频上方 / The video and controls share the Avalonia render tree, so any native Avalonia control can overlay the video
- 兼容 NativeAOT；公共 API、属性、方法和事件带有中英双语 XML 注释 / NativeAOT-friendly, with bilingual XML documentation on the public API
- Linux 缺少 FFmpeg 时可以通过系统包管理器请求安装 / Can request installation of FFmpeg through the Linux package manager when it is missing

## 安装 / Installation

```bash
dotnet add package NativeMedia.Avalonia --version 1.0.1
```

包名是 `NativeMedia.Avalonia`，程序集和命名空间仍然是 `NativeMedia.Avalonia`。  
The package is named `NativeMedia.Avalonia`; the assembly and namespace remain `NativeMedia.Avalonia`.

项目引用 Avalonia `12.1.1` 和 .NET `10.0`。  
The package targets .NET `10.0` and references Avalonia `12.1.1`.

## 快速开始 / Quick start

在应用启动时注册当前平台适配器。Windows 和 macOS 也会通过模块初始化器自动注册，显式调用可以让启动代码更直观；Linux 建议显式启用 FFmpeg。

Register the adapter for the current platform at application startup. Windows and macOS also register through module initializers; explicit registration keeps startup code clear. On Linux, explicitly enable FFmpeg.

```csharp
using NativeMedia.Avalonia;
using NativeMedia.Avalonia.Linux;
using NativeMedia.Avalonia.macOS;
using NativeMedia.Avalonia.Windows;

if (OperatingSystem.IsWindows())
    WindowsMediaFoundation.Use();
else if (OperatingSystem.IsLinux())
    LinuxFfmpeg.Use();
else if (OperatingSystem.IsMacOS())
    MacAvFoundation.Use();
```

在 Avalonia XAML 中使用控件：  
Use the controls from Avalonia XAML:

```xml
<Window xmlns:media="using:NativeMedia.Avalonia">
  <Grid RowDefinitions="*,Auto">
    <media:VideoPlayer Grid.Row="0"
                       Source="{Binding VideoSource}"
                       AutoPlay="True"
                       Position="{Binding Position, Mode=TwoWay}"
                       Volume="{Binding Volume, Mode=TwoWay}"
                       IsPlaying="{Binding IsPlaying, Mode=TwoWay}" />

    <media:AudioPlayer Grid.Row="1"
                       Source="{Binding AudioSource}"
                       AutoPlay="False"
                       ShowControls="True" />
  </Grid>
</Window>
```

也可以直接打开网络媒体地址：  
Network media can be opened directly:

```xml
<media:VideoPlayer Source="https://cdn.example.com/video.mp4"
                   AutoPlay="True" />
```

```csharp
await player.OpenAsync("https://cdn.example.com/live.m3u8");
await player.PlayAsync();
player.Seek(TimeSpan.FromSeconds(30));
await player.PauseAsync();
```

## 平台后端 / Platform backends

| 平台 / Platform | 默认后端 / Backend | 运行时要求 / Runtime requirement |
| --- | --- | --- |
| Windows | Media Foundation / MFPlay | Windows 自带 `mfplat.dll` 等 Media Foundation 组件 / Windows Media Foundation (`mfplat.dll`, etc.) |
| macOS | AVFoundation / AVPlayer | 系统 `AVFoundation.framework`、CoreVideo、QuartzCore / System frameworks |
| Linux | 外部 FFmpeg 进程 | `ffmpeg` 和 `ffprobe` 位于 `PATH`；音频使用 ALSA/Pulse/PipeWire 默认设备 / `ffmpeg` and `ffprobe` on `PATH`; audio uses the default ALSA/Pulse/PipeWire device |

当前 NuGet 包包含核心程序集和三个平台适配程序集。运行时只会使用当前操作系统对应的适配器。平台适配器不会把 FFmpeg、Media Foundation 或 Apple 系统框架打包进 NuGet 包。

The NuGet package contains the core assembly and the three platform adapter assemblies. At runtime only the adapter for the current operating system is used. The package does not bundle FFmpeg binaries, Windows Media Foundation, or Apple system frameworks.

### Linux FFmpeg / Linux FFmpeg

Ubuntu/Debian：

```bash
sudo apt update
sudo apt install ffmpeg
```

Fedora/RHEL：

```bash
sudo dnf install ffmpeg
```

Arch Linux：

```bash
sudo pacman -S ffmpeg
```

也可以让组件在首次打开媒体时请求 `sudo` 或 `pkexec` 安装：  
The component can also request `sudo` or `pkexec` to install FFmpeg when the first source is opened:

```csharp
MediaRuntimeOptions.AutoInstallLinuxRuntime = true; // 默认值 / default: true
```

如果应用不允许自动安装，请关闭该选项，并在部署文档中明确要求用户预装 FFmpeg：  
Disable it when applications must not invoke a package manager:

```csharp
MediaRuntimeOptions.AutoInstallLinuxRuntime = false;
```

Linux 后端也提供 `LinuxRuntimeInstaller.EnsureFfmpegAsync()`，支持 apt、dnf/yum、pacman、zypper、apk、xbps 和 emerge。安装操作需要系统权限和网络，包本身不会下载或携带 `.so` 文件。

The Linux adapter also exposes `LinuxRuntimeInstaller.EnsureFfmpegAsync()`. It recognizes apt, dnf/yum, pacman, zypper, apk, xbps, and emerge. Installation requires system privileges and network access; the NuGet package does not download or bundle `.so` files.

Linux 程序集中还保留了可选的 `LinuxGStreamerBackend`。如需使用 GStreamer，可以显式调用 `LinuxGStreamer.Use()`；它要求系统安装 GStreamer 1.x 及对应插件。默认注册仍然是 FFmpeg，建议使用 FFmpeg 以获得当前的 Avalonia 合成视频和控件覆盖行为。  
The Linux assembly also contains an optional `LinuxGStreamerBackend`. Call `LinuxGStreamer.Use()` explicitly after installing GStreamer 1.x and its plugins. FFmpeg remains the default and is recommended for the current Avalonia-composited video and overlay behavior.

## 视频覆盖和默认控制栏 / Video overlays and controls

视频帧通过 `VideoFrameSurface` 进入 Avalonia 合成渲染，默认模板使用底部 Overlay 控制栏。因此可以直接在同一个 `Grid` 中叠加任意 Avalonia 控件：

Video frames are presented through `VideoFrameSurface` and the default template uses a bottom overlay control bar. Any Avalonia control can be placed in the same `Grid` above the player:

```xml
<Grid>
  <media:VideoPlayer Source="{Binding Source}" />
  <Border HorizontalAlignment="Left"
          VerticalAlignment="Top"
          Margin="16"
          Background="#99000000"
          Padding="8">
    <TextBlock Foreground="White" Text="直播中 / LIVE" />
  </Border>
</Grid>
```

`ShowControls="False"` 可以隐藏默认控制栏，只保留视频或音频区域。应用可以为 `VideoPlayer` 或 `AudioPlayer` 替换 `ControlTheme`，保留现有绑定和事件。

Set `ShowControls="False"` to hide the default controls. Applications can replace the `ControlTheme` for either player while keeping the existing bindings and events.

## 常用 API / Common API

可写属性 / Writable properties:

- `Source`：本地路径或 HTTP(S) URL / local path or HTTP(S) URL
- `AutoPlay`：打开后自动播放 / start after opening
- `Volume`：`0.0` 到 `1.0` / from `0.0` to `1.0`
- `Muted`：静音 / mute
- `ShowControls`：显示默认控制栏 / show default controls
- `Loop`：播放结束后循环 / loop at end
- `Position`、`PositionSeconds`：TwoWay 播放位置 / TwoWay playback position
- `IsPlaying`：TwoWay 播放状态 / TwoWay playing state

只读属性 / Read-only properties:

- `Duration`、`DurationSeconds`、`TotalTime`、`DurationText`
- `CurrentTime`、`CurrentTimeText`
- `BufferedPosition`、`BufferedPositionSeconds`
- `State`、`BackendName`、`BackendAvailable`
- `IsStreaming`、`SourceKind`
- `ControlsVisible`、`CenterPlayVisible`
- `UsesCompositedVideo`、`UsesNativeVideoSurface`
- `RuntimeStatus`、`RuntimeStatusVisible`

方法 / Methods:

```csharp
await player.OpenAsync(source);
await player.PlayAsync();
await player.PauseAsync();
await player.StopAsync();
player.Seek(TimeSpan.FromSeconds(10));
player.SeekForward();
player.SeekBackward();
player.TogglePlayPause();
player.ToggleFullscreen();
await player.DisposeAsync();
```

事件 / Events:

`Opened`、`Playing`、`Paused`、`Stopped`、`Ended`、`PositionChanged`、`VolumeChanged`、`Error` 和 `FullScreenRequested`。

All public properties, methods, events, and event arguments include bilingual XML documentation. When using the NuGet package, keep the generated `NativeMedia.Avalonia.xml` beside the DLL so Visual Studio/Rider can show IntelliSense comments.

## 构建源码 / Build from source

```powershell
dotnet restore NativeMedia.Avalonia.sln
dotnet build NativeMedia.Avalonia.sln -c Release
dotnet test tests/NativeMedia.Avalonia.Tests/NativeMedia.Avalonia.Tests.csproj -c Release
dotnet pack src/NativeMedia.Avalonia/NativeMedia.Avalonia.csproj -c Release --no-restore
```

NuGet 包输出到：

```text
artifacts/packages/NativeMedia.Avalonia.1.0.1.nupkg
```

要在本地测试包：

```powershell
dotnet new avalonia.app -n NativeMediaPackageSample
dotnet add NativeMediaPackageSample package NativeMedia.Avalonia --version 1.0.1 --source .\artifacts\packages
```

## 目录结构 / Repository layout

```text
src/
  NativeMedia.Avalonia/          # 核心控件、属性、事件和主题 / core controls, API, and theme
  NativeMedia.Avalonia.Windows/  # Windows Media Foundation / MFPlay
  NativeMedia.Avalonia.Linux/    # Linux FFmpeg and runtime installer
  NativeMedia.Avalonia.macOS/    # macOS AVFoundation / AVPlayer
samples/                         # Avalonia demo
tests/                           # 单元测试和平台 smoke test
```

## 许可证 / License

MIT License。详见 [LICENSE](LICENSE)。  
MIT License. See [LICENSE](LICENSE).
