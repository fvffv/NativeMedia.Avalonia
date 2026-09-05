namespace NativeMedia.Avalonia;

/// <summary>媒体后端注册与创建入口。<para>Entry point for registering and creating media backends.</para></summary>
public static class MediaBackendFactory
{
    private static Func<IMediaBackend> _factory = static () => new NativeMediaBackend("System Native Media", true);
    /// <summary>注册一个自定义媒体后端工厂。<para>Registers a custom media backend factory.</para></summary>
    /// <param name="factory">返回后端实例的工厂委托。<para>Factory delegate that returns a backend instance.</para></param>
    public static void Register(Func<IMediaBackend> factory) => _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    /// <summary>创建当前注册的媒体后端。<para>Creates the currently registered media backend.</para></summary>
    public static IMediaBackend Create()
        => _factory();
}
