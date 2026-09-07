using Avalonia;

namespace NativeMedia.Avalonia;

/// <summary>视频播放质量预设；仅影响播放输出，不修改媒体文件。<para>Video output quality presets; the media file is never modified.</para></summary>
public enum VideoPlaybackQuality
{
    /// <summary>原画：不缩小源分辨率、不主动限制源帧率。<para>Original dimensions with no intentional frame-rate reduction.</para></summary>
    Original,
    /// <summary>高清：最长边 1920、最短边 1080，最高 60 fps。<para>Up to 1920 by 1080, portrait-aware, at up to 60 fps.</para></summary>
    High,
    /// <summary>均衡：最长边 1280、最短边 720，最高 30 fps。<para>Up to 1280 by 720, portrait-aware, at up to 30 fps.</para></summary>
    Balanced,
    /// <summary>流畅：最长边 960、最短边 540，最高 24 fps。<para>Up to 960 by 540, portrait-aware, at up to 24 fps.</para></summary>
    Smooth
}

/// <summary>平台后端共用的播放输出限制。<para>Shared output limits for platform backends.</para></summary>
public readonly record struct VideoPlaybackProfile(int MaximumLongEdge, int MaximumShortEdge, int MaximumFrameRate)
{
    /// <summary>获取预设；零表示不限制。<para>Gets preset limits; zero means unrestricted.</para></summary>
    public static VideoPlaybackProfile FromQuality(VideoPlaybackQuality quality) => quality switch
    {
        VideoPlaybackQuality.Original => new(0, 0, 0),
        VideoPlaybackQuality.High => new(1920, 1080, 60),
        VideoPlaybackQuality.Balanced => new(1280, 720, 30),
        VideoPlaybackQuality.Smooth => new(960, 540, 24),
        _ => throw new ArgumentOutOfRangeException(nameof(quality))
    };

    /// <summary>按比例缩小，兼容竖屏，不放大小视频。<para>Fits the source without upscaling or changing its aspect ratio.</para></summary>
    public PixelSize GetOutputSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (MaximumLongEdge == 0 || MaximumShortEdge == 0) return new(width, height);
        var scale = Math.Min(1d, Math.Min(
            (double)MaximumLongEdge / Math.Max(width, height),
            (double)MaximumShortEdge / Math.Min(width, height)));
        return new(Math.Max(1, (int)Math.Floor(width * scale)), Math.Max(1, (int)Math.Floor(height * scale)));
    }
}

/// <summary>可选的视频质量后端契约。<para>Optional backend contract for configurable video output quality.</para></summary>
public interface IVideoQualityMediaBackend
{
    /// <summary>应用视频播放质量；播放中的媒体可在当前位置重新建立视频输出。<para>Applies video quality; active video output may restart at the current position.</para></summary>
    Task SetPlaybackQualityAsync(VideoPlaybackQuality quality, CancellationToken cancellationToken = default);
}

/// <summary>由宿主显示器的渲染节奏驱动原生视频取帧。<para>Optional display-driven frame requests for native video backends.</para></summary>
public interface IVideoFrameClockBackend
{
    /// <summary>启用时停止后端的备用定时器。<para>Disables the fallback timer while the host supplies frame requests.</para></summary>
    void SetExternalFrameClock(bool enabled);
    /// <summary>请求当前视频帧，不在渲染回调内更改控件。<para>Requests a frame without mutating controls inside a render callback.</para></summary>
    void RequestVideoFrame();
}
