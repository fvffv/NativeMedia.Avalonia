using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using global::Windows.Win32;
using global::Windows.Win32.Foundation;
using global::Windows.Win32.Graphics.Direct3D;
using global::Windows.Win32.Graphics.Direct3D10;
using global::Windows.Win32.Graphics.Direct3D11;
using global::Windows.Win32.Graphics.Dxgi;
using global::Windows.Win32.Graphics.Dxgi.Common;
using global::Windows.Win32.Media.MediaFoundation;
using global::Windows.Win32.System.Com;

namespace NativeMedia.Avalonia.Windows;

// All COM calls use generated unmanaged bindings: no RCWs or runtime COM marshalling.
// The caller serializes control/frame operations; MF shares the device using multithread protection.
internal sealed unsafe class MediaEngineNative : IDisposable
{
    private ID3D11Device* _device;
    private ID3D11DeviceContext* _context;
    private IMFDXGIDeviceManager* _manager;
    private IMFMediaEngine* _engine;
    private nint _notify;
    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _error;
    public Task Loaded => _loaded.Task;
    public int ErrorCode => Volatile.Read(ref _error);
    public double Position => _engine == null ? 0 : _engine->GetCurrentTime();
    public double Duration => _engine == null ? 0 : _engine->GetDuration();
    public bool Ended => _engine != null && _engine->IsEnded();
    public event Action? FrameNeeded;

    public MediaEngineNative(byte[] adapterLuid, string source)
    {
        try
        {
            CreateDevice(adapterLuid);
            IMFDXGIDeviceManager* manager;
            uint token;
            PInvoke.MFCreateDXGIDeviceManager(&token, &manager).ThrowOnFailure();
            _manager = manager;
            manager->ResetDevice((IUnknown*)_device, token);
            IMFAttributes* attributes;
            PInvoke.MFCreateAttributes(&attributes, 3).ThrowOnFailure();
            IMFMediaEngineClassFactory* factory = null;
            try
            {
                _notify = MediaEngineNotify.Create(OnEvent);
                var key = PInvoke.MF_MEDIA_ENGINE_DXGI_MANAGER;
                attributes->SetUnknown(&key, (IUnknown*)manager);
                key = PInvoke.MF_MEDIA_ENGINE_CALLBACK;
                attributes->SetUnknown(&key, (IUnknown*)_notify);
                key = PInvoke.MF_MEDIA_ENGINE_VIDEO_OUTPUT_FORMAT;
                attributes->SetUINT32(&key, (uint)DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM);
                PInvoke.CoCreateInstance(PInvoke.CLSID_MFMediaEngineClassFactory, null,
                    CLSCTX.CLSCTX_INPROC_SERVER, out factory).ThrowOnFailure();
                IMFMediaEngine* engine;
                // Default frame-server mode, with audio/video synchronization owned by Media Engine.
                factory->CreateInstance(0, attributes, &engine);
                _engine = engine;
                var uri = Uri.TryCreate(source, UriKind.Absolute, out var parsed)
                    && parsed.Scheme is "http" or "https" or "file"
                    ? source : new Uri(Path.GetFullPath(source)).AbsoluteUri;
                var bstr = Marshal.StringToBSTR(uri);
                try { engine->SetSource(new BSTR((char*)bstr)); }
                finally { Marshal.FreeBSTR(bstr); }
                engine->Load();
            }
            finally { if (factory != null) factory->Release(); attributes->Release(); }
        }
        catch { Dispose(); throw; }
    }

    private void CreateDevice(byte[] luid)
    {
        if (luid.Length != 8) throw new NotSupportedException("The compositor did not expose its DXGI adapter LUID.");
        IDXGIFactory1* factory;
        var iid = typeof(IDXGIFactory1).GUID;
        PInvoke.CreateDXGIFactory1(&iid, (void**)&factory).ThrowOnFailure();
        IDXGIAdapter* selected = null;
        try
        {
            // Do not assume adapter zero: the UI can run on either GPU on hybrid laptops.
            for (uint i = 0; ; ++i)
            {
                IDXGIAdapter* adapter;
                var table = *(nint**)factory;
                var hr = ((delegate* unmanaged[Stdcall]<IDXGIFactory1*, uint, IDXGIAdapter**, int>)table[7])(factory, i, &adapter);
                if (hr == unchecked((int)0x887A0002)) break; // DXGI_ERROR_NOT_FOUND
                Marshal.ThrowExceptionForHR(hr);
                var desc = adapter->GetDesc();
                var adapterId = desc.AdapterLuid;
                if (new ReadOnlySpan<byte>(&adapterId, 8).SequenceEqual(luid)) { selected = adapter; break; }
                adapter->Release();
            }
            if (selected == null) throw new NotSupportedException("The compositor's DXGI adapter was not found.");
            ID3D11Device* device;
            ID3D11DeviceContext* context;
            var levels = stackalloc D3D_FEATURE_LEVEL[] { D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0 };
            PInvoke.D3D11CreateDevice(selected, D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN, default,
                D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
                levels, 2, 7, &device, null, &context).ThrowOnFailure();
            _device = device;
            _context = context;
            _context->QueryInterface(out ID3D10Multithread* multithread).ThrowOnFailure();
            try { multithread->SetMultithreadProtected(true); }
            finally { multithread->Release(); }
        }
        finally { if (selected != null) selected->Release(); factory->Release(); }
    }

    private void OnEvent(uint value, nuint parameter, uint detail)
    {
        // MF invokes this on its own threads. Never call back into the engine here.
        if (value == (uint)MF_MEDIA_ENGINE_EVENT.MF_MEDIA_ENGINE_EVENT_ERROR)
        {
            var hr = detail == 0 ? unchecked((int)0x80004005) : unchecked((int)detail);
            Volatile.Write(ref _error, hr);
            _loaded.TrySetException(new COMException($"Media Engine error {parameter} (0x{hr:X8}).", hr));
        }
        else if (value == (uint)MF_MEDIA_ENGINE_EVENT.MF_MEDIA_ENGINE_EVENT_LOADEDDATA)
            _loaded.TrySetResult();
        else if (value == (uint)MF_MEDIA_ENGINE_EVENT.MF_MEDIA_ENGINE_EVENT_SEEKED)
            FrameNeeded?.Invoke();
    }

    public void Play() => _engine->Play();
    public void Pause() => _engine->Pause();
    public void Seek(double seconds) => _engine->SetCurrentTime(seconds);
    public void SetVolume(double value) => _engine->SetVolume(value);
    public void SetMuted(bool value) => _engine->SetMuted(value);
    public (int Width, int Height) GetVideoSize()
    {
        uint width, height;
        _engine->GetNativeVideoSize(&width, &height);
        return ((int)width, (int)height);
    }
    public bool TryGetFrame(out long timestamp)
    {
        long pts;
        // Preserve S_FALSE (no new frame); the generated throwing wrapper discards it.
        var hr = ((delegate* unmanaged[Stdcall]<IMFMediaEngine*, long*, int>)(*(nint**)_engine)[44])(_engine, &pts);
        Marshal.ThrowExceptionForHR(hr);
        timestamp = pts;
        return hr == 0;
    }
    public void Transfer(SharedVideoTexture texture)
    {
        var source = new MFVideoNormalizedRect { left = 0, top = 0, right = 1, bottom = 1 };
        var destination = new RECT { right = texture.Width, bottom = texture.Height };
        var border = new MFARGB { rgbAlpha = 255 };
        _engine->TransferVideoFrame((IUnknown*)texture.Texture, &source, &destination, &border);
        _context->Flush();
    }
    public SharedVideoTexture CreateTexture(int width, int height) => new(_device, width, height);

    public void Dispose()
    {
        if (_engine != null)
        {
            try { _engine->Shutdown(); } catch { }
            _engine->Release(); _engine = null;
        }
        if (_notify != 0) { ((IUnknown*)_notify)->Release(); _notify = 0; }
        if (_manager != null) { _manager->Release(); _manager = null; }
        if (_context != null) { _context->Release(); _context = null; }
        if (_device != null) { _device->Release(); _device = null; }
        _loaded.TrySetCanceled();
    }
}

internal sealed unsafe class SharedVideoTexture : IDisposable
{
    public readonly int Width, Height;
    public ID3D11Texture2D* Texture { get; private set; }
    private IDXGIKeyedMutex* _mutex;
    public nint Handle { get; private set; }
    public SharedVideoTexture(ID3D11Device* device, int width, int height)
    {
        Width = width; Height = height;
        try
        {
            var desc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1 },
                Usage = D3D11_USAGE.D3D11_USAGE_DEFAULT,
                BindFlags = D3D11_BIND_FLAG.D3D11_BIND_RENDER_TARGET | D3D11_BIND_FLAG.D3D11_BIND_SHADER_RESOURCE,
                MiscFlags = D3D11_RESOURCE_MISC_FLAG.D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX
            };
            ID3D11Texture2D* texture;
            device->CreateTexture2D(&desc, null, &texture);
            Texture = texture;
            texture->QueryInterface(out IDXGIKeyedMutex* mutex).ThrowOnFailure();
            _mutex = mutex;
            texture->QueryInterface(out IDXGIResource* resource).ThrowOnFailure();
            try { HANDLE handle; resource->GetSharedHandle(&handle); Handle = (nint)handle.Value; }
            finally { resource->Release(); }
        }
        catch { Dispose(); throw; }
    }
    public bool TryAcquire()
    {
        var hr = ((delegate* unmanaged[Stdcall]<IDXGIKeyedMutex*, ulong, uint, int>)(*(nint**)_mutex)[8])(_mutex, 0, 0);
        if (hr == 258) return false; // WAIT_TIMEOUT is positive, not a failing HRESULT.
        if (hr == 128) throw new InvalidOperationException("The shared video texture mutex was abandoned.");
        Marshal.ThrowExceptionForHR(hr);
        return true;
    }
    public void Release(ulong key) => _mutex->ReleaseSync(key);
    public void Dispose()
    {
        if (_mutex != null) { _mutex->Release(); _mutex = null; }
        if (Texture != null) { Texture->Release(); Texture = null; }
        // GetSharedHandle returns a non-owning legacy handle: never CloseHandle it.
        Handle = 0;
    }
}

// A minimal reference-counted, agile COM callback. Its GCHandle is freed only after
// Media Engine and the creator have both released their references.
internal static unsafe class MediaEngineNotify
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Instance { public nint Vtable; public nint Target; public int References; }
    private static readonly nint Table = CreateTable();
    private static nint CreateTable()
    {
        var table = (nint*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(MediaEngineNotify), 4 * sizeof(nint));
        table[0] = (nint)(delegate* unmanaged[Stdcall]<Instance*, Guid*, void**, int>)&Query;
        table[1] = (nint)(delegate* unmanaged[Stdcall]<Instance*, uint>)&AddRef;
        table[2] = (nint)(delegate* unmanaged[Stdcall]<Instance*, uint>)&Release;
        table[3] = (nint)(delegate* unmanaged[Stdcall]<Instance*, uint, nuint, uint, int>)&Notify;
        return (nint)table;
    }
    public static nint Create(Action<uint, nuint, uint> callback)
    {
        var self = (Instance*)NativeMemory.AllocZeroed((nuint)sizeof(Instance));
        self->Vtable = Table;
        self->Target = GCHandle.ToIntPtr(GCHandle.Alloc(callback));
        self->References = 1;
        return (nint)self;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Query(Instance* self, Guid* iid, void** result)
    {
        if (result == null || iid == null) return unchecked((int)0x80004003);
        *result = null;
        if (*iid != typeof(IUnknown).GUID && *iid != typeof(IMFMediaEngineNotify).GUID
            && *iid != new Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90")) return unchecked((int)0x80004002);
        Interlocked.Increment(ref self->References); *result = self; return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(Instance* self) => (uint)Interlocked.Increment(ref self->References);
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(Instance* self)
    {
        var count = Interlocked.Decrement(ref self->References);
        if (count == 0) { GCHandle.FromIntPtr(self->Target).Free(); NativeMemory.Free(self); }
        return (uint)count;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Notify(Instance* self, uint value, nuint parameter, uint detail)
    {
        try { ((Action<uint, nuint, uint>)GCHandle.FromIntPtr(self->Target).Target!)(value, parameter, detail); return 0; }
        catch (Exception ex) { return ex.HResult; }
    }
}
