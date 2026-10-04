# NativeMedia.Avalonia Demo

## English

Choose a local video or enter a video URL. The Demo references the library projects directly: Windows D3D11, Linux libmpv/OpenGL (optional system libmpv runtime), and macOS IOSurface. Original quality is the default. Diagnostics below the video show the actual presentation path and any fallback reason, not measured display FPS. Linux/macOS still need target-machine playback validation.

Use the player's fullscreen button or double-click the video to fill the screen with the video player. The Demo's file selectors, audio player and settings are hidden. Press **Esc**, double-click again, or use the fullscreen button to restore the window and Demo layout. The same player remains attached throughout, so fullscreen does not reopen the media or dispose the GPU session. Aspect ratio is preserved; black bars may remain.

```powershell
dotnet run --project samples/NativeMedia.Avalonia.Demo -c Release
```

## 中文

选择本地视频或输入视频网址即可播放。Demo 直接引用源码项目：Windows D3D11、Linux libmpv／OpenGL（需可选的系统 libmpv 运行库）以及 macOS IOSurface。默认使用原画档。视频下方显示实际渲染路径和回退原因，不是屏幕实测 FPS。Linux／macOS 仍需在目标机器实测播放。

点击播放器全屏按钮或双击视频后，由视频播放控件铺满屏幕，同时隐藏 Demo 的文件选择、音频播放器和设置区域。按 **Esc**、再次双击视频或点击全屏按钮，可恢复原窗口和 Demo 布局。切换期间不移动或重建播放器，不重新打开媒体或释放 GPU 会话。视频保持原始宽高比，必要时仍会有黑边。

上面的命令可单独启动 Demo。
