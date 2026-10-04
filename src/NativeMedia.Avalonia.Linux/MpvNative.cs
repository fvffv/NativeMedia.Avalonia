using System.Runtime.InteropServices;

namespace NativeMedia.Avalonia.Linux;

// libmpv is an optional system dependency. No bundled binaries or auto-install.
internal static unsafe class MpvNative
{
    private const string Library = "NativeMedia.LibMpv";
    private static readonly nint Handle;
    static MpvNative()
    {
        if (!OperatingSystem.IsLinux()) return;
        foreach (var name in new[] { "libmpv.so.2", "libmpv.so.1" })
            if (NativeLibrary.TryLoad(name, out Handle)) break;
        if (Handle != 0)
            NativeLibrary.SetDllImportResolver(typeof(MpvNative).Assembly,
                (name, _, _) => name == Library ? Handle : 0);
    }
    internal static bool IsAvailable => Handle != 0;
    [StructLayout(LayoutKind.Sequential)] internal struct Param(int type, nint data) { public int Type = type; public nint Data = data; }
    [StructLayout(LayoutKind.Sequential)] internal struct InitParams { public nint GetProcAddress, Context; }
    [StructLayout(LayoutKind.Sequential)] internal struct Fbo { public int Id, Width, Height, Format; }
    [StructLayout(LayoutKind.Sequential)] internal struct Event { public int Id, Error; public ulong UserData; public nint Data; }
    [StructLayout(LayoutKind.Sequential)] internal struct EndFile { public int Reason, Error; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate nint GetProc(nint context, nint name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Update(nint context);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_create")] internal static extern nint Create();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_initialize")] internal static extern int Initialize(nint handle);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_terminate_destroy")] internal static extern void Destroy(nint handle);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_set_option_string")]
    internal static extern int Option(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_command_async")] private static extern int CommandAsync(nint handle, ulong id, nint* args);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property")]
    private static extern int GetDouble(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out double value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_get_property_string")]
    private static extern nint GetString(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_free")] private static extern void Free(nint value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_wait_event")] internal static extern nint WaitEvent(nint handle, double timeout);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_render_context_create")] internal static extern int RenderCreate(out nint context, nint handle, Param* parameters);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_render_context_set_update_callback")]
    internal static extern void SetUpdateCallback(nint context, Update? callback, nint data);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_render_context_update")] internal static extern ulong RenderUpdate(nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_render_context_render")] internal static extern int Render(nint context, Param* parameters);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_render_context_free")] internal static extern void RenderFree(nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, EntryPoint = "mpv_error_string")] private static extern nint ErrorString(int error);

    internal static void Check(int result)
    {
        if (result < 0) throw new InvalidOperationException("libmpv: " + Marshal.PtrToStringUTF8(ErrorString(result)));
    }
    internal static void Command(nint handle, params string[] args)
    {
        var pointers = stackalloc nint[args.Length + 1];
        for (var i = 0; i <= args.Length; i++) pointers[i] = 0;
        try
        {
            for (var i = 0; i < args.Length; i++) pointers[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            Check(CommandAsync(handle, 0, pointers));
        }
        finally { for (var i = 0; i < args.Length; i++) Marshal.FreeCoTaskMem(pointers[i]); }
    }
    internal static double Number(nint handle, string name)
        => GetDouble(handle, name, 5, out var value) >= 0 && double.IsFinite(value) ? value : 0;
    internal static string Text(nint handle, string name)
    {
        var value = GetString(handle, name);
        try { return Marshal.PtrToStringUTF8(value) ?? "unknown"; }
        finally { if (value != 0) Free(value); }
    }
}
