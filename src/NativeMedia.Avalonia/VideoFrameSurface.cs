using global::Avalonia.Controls;
using global::Avalonia.Media.Imaging;

namespace NativeMedia.Avalonia;

/// <summary>用于显示合成视频帧的 Avalonia 图像表面，其他 Avalonia 控件可以覆盖在其上方。<para>Regular Avalonia surface for composited video frames; other Avalonia controls can render above it.</para></summary>
public sealed class VideoFrameSurface : Image { }
