namespace NativeMedia.Avalonia;

/// <summary>
/// 视频播放器控件，提供视频画面、播放控制栏和全屏操作。
/// <para>Video player control that provides video rendering, playback controls, and fullscreen support.</para>
/// </summary>
public sealed class VideoPlayer : MediaPlayer
{
    protected override Type StyleKeyOverride => typeof(VideoPlayer);
}

/// <summary>
/// 音频播放器控件，提供音频播放、进度、音量和静音控制。
/// <para>Audio player control that provides playback, progress, volume, and mute controls.</para>
/// </summary>
public sealed class AudioPlayer : MediaPlayer
{
    protected override Type StyleKeyOverride => typeof(AudioPlayer);
}
