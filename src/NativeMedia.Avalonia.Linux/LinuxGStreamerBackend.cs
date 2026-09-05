using System.Runtime.InteropServices;
using NativeMedia.Avalonia;

namespace NativeMedia.Avalonia.Linux;

/// <summary>GStreamer adapter using the distribution's installed GStreamer runtime.</summary>
public sealed class LinuxGStreamerBackend : NativeMediaBackend
{
    private nint _pipeline;
    private nint _videoOutput;
    private nint _bus;
    private bool _installAttempted;
    public LinuxGStreamerBackend() : base("Linux GStreamer", CheckGStreamer()) { }
    public override bool IsAvailable => CheckEnvironment().IsAvailable;
    public static GStreamerAvailability CheckEnvironment()
    {
        if (!CheckGStreamer()) return new(false, "GStreamer 1.x (libgstreamer-1.0) was not found. Install GStreamer and its plugins.");
        try
        {
            NativeMethods.gst_init(IntPtr.Zero, IntPtr.Zero);
            foreach (var element in new[] { "playbin", "decodebin", "typefind" })
            {
                var factory = NativeMethods.gst_element_factory_find(element);
                if (factory == 0)
                    return new(false, $"GStreamer is installed but missing element: {element}.");
                NativeMethods.gst_object_unref(factory);
            }
            return new(true, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        { return new(false, "GStreamer runtime is incomplete: " + ex.Message); }
    }
    public override async Task OpenAsync(string source, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            if (MediaRuntimeOptions.AutoInstallLinuxRuntime && !_installAttempted)
            {
                _installAttempted = true;
                RaiseRuntimeStatus("正在请求系统权限安装 GStreamer 播放环境…", true);
                var install = await LinuxRuntimeInstaller.EnsureAsync(cancellationToken);
                if (install.Success)
                {
                    RaiseRuntimeStatus(install.Message);
                    await OpenAsync(source, cancellationToken);
                    return;
                }
                RaiseRuntimeStatus(install.Message);
            }
            RaiseError(CheckEnvironment().Error ?? "GStreamer playback environment is unavailable.");
            return;
        }
        try
        {
            DisposePipeline();
            NativeMethods.gst_init(IntPtr.Zero, IntPtr.Zero);
            string uri;
            if (Uri.TryCreate(source, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") uri = source;
            else if (u is not null && u.Scheme == "file") uri = u.AbsoluteUri;
            else uri = $"file://{Uri.EscapeDataString(Path.GetFullPath(source)).Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)}";
            var headless = string.Equals(Environment.GetEnvironmentVariable("AVALONIA_NATIVE_MEDIA_HEADLESS"), "1", StringComparison.Ordinal);
            var sinkOptions = headless ? " audio-sink=fakesink video-sink=fakesink" : string.Empty;
            _pipeline = NativeMethods.gst_parse_launch($"playbin uri=\"{uri}\"{sinkOptions}", IntPtr.Zero);
            if (_pipeline == 0) { RaiseError("GStreamer could not create a playback pipeline."); return; }
            _bus = NativeMethods.gst_element_get_bus(_pipeline);
            if (_videoOutput != 0) { try { NativeMethods.gst_video_overlay_set_window_handle(_pipeline, (nuint)_videoOutput); } catch (DllNotFoundException) { } }
            if (NativeMethods.gst_element_set_state(_pipeline, 3) == 0) { RaiseError("GStreamer could not preroll this media. Check installed codecs and sinks."); return; } // preroll; actual playback starts in PlayAsync
            await base.OpenAsync(source, cancellationToken);
            await Task.Delay(100, cancellationToken);
            if (TryReadBusError(out var error))
            {
                DisposePipeline();
                if (MediaRuntimeOptions.AutoInstallLinuxRuntime && !_installAttempted)
                {
                    _installAttempted = true;
                    RaiseRuntimeStatus("当前媒体需要额外的 GStreamer 解码插件，正在请求系统权限安装…", true);
                    var install = await LinuxRuntimeInstaller.EnsureAsync(cancellationToken);
                    if (install.Success)
                    {
                        RaiseRuntimeStatus(install.Message);
                        await OpenAsync(source, cancellationToken);
                        return;
                    }
                    RaiseRuntimeStatus(install.Message);
                }
                RaiseError(error ?? "GStreamer could not open this media. Check installed codec plugins.");
                return;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { RaiseError("GStreamer runtime is incomplete.", ex); }
    }
    public override Task PlayAsync(CancellationToken cancellationToken = default)
    {
        if (_pipeline != 0) NativeMethods.gst_element_set_state(_pipeline, 4); // GST_STATE_PLAYING
        return base.PlayAsync(cancellationToken);
    }
    public override Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_pipeline != 0) NativeMethods.gst_element_set_state(_pipeline, 3); // GST_STATE_PAUSED
        return base.PauseAsync(cancellationToken);
    }
    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_pipeline != 0) NativeMethods.gst_element_set_state(_pipeline, 1); // GST_STATE_NULL
        return base.StopAsync(cancellationToken);
    }
    public override void Seek(TimeSpan position)
    {
        if (_pipeline != 0) NativeMethods.gst_element_seek_simple(_pipeline, 3, 1 | 2, Math.Max(0, position.Ticks) * 100);
        base.Seek(position);
    }
    protected override TimeSpan QueryPosition()
    {
        if (_pipeline != 0 && NativeMethods.gst_element_query_position(_pipeline, 3, out var value) != 0 && value > 0)
            return TimeSpan.FromTicks(value / 100);
        return base.QueryPosition();
    }
    public override TimeSpan Duration
    {
        get
        {
            if (_pipeline != 0 && NativeMethods.gst_element_query_duration(_pipeline, 3, out var value) != 0 && value > 0)
            {
                var duration = TimeSpan.FromTicks(value / 100);
                BufferedPosition = duration;
                return duration;
            }
            return base.Duration;
        }
        protected set => base.Duration = value;
    }
    public override void SetVideoOutput(nint handle)
    {
        _videoOutput = handle;
        if (_pipeline != 0 && handle != 0) { try { NativeMethods.gst_video_overlay_set_window_handle(_pipeline, (nuint)handle); } catch (DllNotFoundException) { } }
    }
    private bool TryReadBusError(out string? message)
    {
        message = null;
        if (_bus == 0) return false;
        var msg = NativeMethods.gst_bus_timed_pop_filtered(_bus, 0, 2); // GST_MESSAGE_ERROR
        if (msg == 0) return false;
        try
        {
            NativeMethods.gst_message_parse_error(msg, out var error, out _);
            if (error != 0)
            {
                message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, IntPtr.Size == 8 ? 8 : 4));
                NativeMethods.g_error_free(error);
            }
            return true;
        }
        finally { NativeMethods.gst_message_unref(msg); }
    }
    private void DisposePipeline()
    {
        if (_bus != 0) { NativeMethods.gst_object_unref(_bus); _bus = 0; }
        if (_pipeline != 0) { NativeMethods.gst_element_set_state(_pipeline, 1); NativeMethods.gst_object_unref(_pipeline); _pipeline = 0; }
    }
    public override void Dispose() { DisposePipeline(); base.Dispose(); }
    private static bool CheckGStreamer()
    {
        if (!OperatingSystem.IsLinux() || !NativeLibrary.TryLoad("libgstreamer-1.0.so.0", out var handle)) return false;
        NativeLibrary.Free(handle); return true;
    }
    public readonly record struct GStreamerAvailability(bool IsAvailable, string? Error);
    private static class NativeMethods
    {
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern void gst_init(IntPtr argc, IntPtr argv);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern nint gst_parse_launch([MarshalAs(UnmanagedType.LPUTF8Str)] string pipeline, IntPtr error);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern void gst_object_unref(nint obj);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern nint gst_element_factory_find([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern nint gst_element_get_bus(nint element);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern nint gst_bus_timed_pop_filtered(nint bus, ulong timeout, uint types);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern void gst_message_parse_error(nint message, out nint error, out nint debug);
        [DllImport("libglib-2.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern void g_error_free(nint error);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern void gst_message_unref(nint message);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern int gst_element_set_state(nint element, int state);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern int gst_element_seek_simple(nint element, int format, int flags, long position);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern int gst_element_query_position(nint element, int format, out long position);
        [DllImport("libgstreamer-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern int gst_element_query_duration(nint element, int format, out long duration);
        [DllImport("libgstvideo-1.0.so.0", CallingConvention = CallingConvention.Cdecl)] public static extern void gst_video_overlay_set_window_handle(nint overlay, nuint handle);
    }
}

public static class LinuxGStreamer
{
    public static void Use() => MediaBackendFactory.Register(static () => new LinuxGStreamerBackend());
}

internal static class LinuxGStreamerModule
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Initialize()
    {
        if (OperatingSystem.IsLinux()) LinuxFfmpeg.Use();
    }
}
