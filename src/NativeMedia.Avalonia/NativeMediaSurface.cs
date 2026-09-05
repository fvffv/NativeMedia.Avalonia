using System.Runtime.InteropServices;
using global::Avalonia.Controls;
using global::Avalonia.Platform;

namespace NativeMedia.Avalonia;

/// <summary>Native child surface used by platform video renderers.</summary>
public sealed class NativeMediaSurface : NativeControlHost
{
    private IMediaBackend? _backend;
    private nint _nativeHandle;
    internal IMediaBackend? Backend
    {
        get => _backend;
        set
        {
            if (_backend is not null) _backend.SetVideoOutput(0);
            _backend = value;
            if (_backend is not null && _nativeHandle != IntPtr.Zero) _backend.SetVideoOutput(_nativeHandle);
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (OperatingSystem.IsWindows() && parent.Handle != IntPtr.Zero)
        {
            const uint wsChild = 0x40000000, wsVisible = 0x10000000, wsClipSiblings = 0x04000000;
            var handle = CreateWindowEx(0, "STATIC", null, wsChild | wsVisible | wsClipSiblings, 0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (handle != IntPtr.Zero) { _nativeHandle = handle; Backend?.SetVideoOutput(handle); return new PlatformHandle(handle, "HWND"); }
        }
        try
        {
            var platformHandle = base.CreateNativeControlCore(parent);
            _nativeHandle = platformHandle.Handle;
            Backend?.SetVideoOutput(platformHandle.Handle);
            return platformHandle;
        }
        catch (NotSupportedException)
        {
            // A backend may still provide audio-only playback on a headless/Wayland
            // host where Avalonia cannot create a native child surface.
            return new PlatformHandle(IntPtr.Zero, "None");
        }
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        Backend?.SetVideoOutput(0);
        _nativeHandle = IntPtr.Zero;
        if (OperatingSystem.IsWindows() && control.HandleDescriptor == "HWND" && control.Handle != IntPtr.Zero) DestroyWindow(control.Handle);
        else base.DestroyNativeControlCore(control);
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string? windowName, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyWindow(IntPtr handle);
}
