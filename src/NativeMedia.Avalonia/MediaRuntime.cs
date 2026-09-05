namespace NativeMedia.Avalonia;

/// <summary>播放运行环境状态事件参数。<para>Event data describing playback runtime status.</para></summary>
public sealed class MediaRuntimeStatusEventArgs(string message, bool isBusy = false) : EventArgs
{
    /// <summary>状态或提示文本。<para>Status or informational message.</para></summary>
    public string Message { get; } = message;
    /// <summary>是否正在执行安装或准备操作。<para>Whether an installation or preparation operation is in progress.</para></summary>
    public bool IsBusy { get; } = isBusy;
}

/// <summary>媒体运行环境选项。<para>Options for the media runtime environment.</para></summary>
public static class MediaRuntimeOptions
{
    /// <summary>
    /// Linux 缺少 FFmpeg 时是否允许组件请求系统包管理器自动安装。
    /// <para>Whether the component may ask the Linux package manager to install missing FFmpeg components.</para>
    /// </summary>
    public static bool AutoInstallLinuxRuntime { get; set; } = true;
}
