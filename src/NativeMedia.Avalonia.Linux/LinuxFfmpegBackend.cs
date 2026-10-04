using System.Diagnostics;
using System.Buffers.Binary;
using Avalonia;
using global::Avalonia.Media.Imaging;
using NativeMedia.Avalonia;

namespace NativeMedia.Avalonia.Linux;

/// <summary>
/// Linux FFmpeg backend. FFmpeg is an external system executable; decoded video
/// frames are copied as BGRA into the normal Avalonia scene so overlays remain possible.
/// Audio is sent by FFmpeg to the system ALSA/Pulse/PipeWire default device.
/// </summary>
public sealed class LinuxFfmpegBackend : NativeMediaBackend, IVideoMediaBackend, IVideoQualityMediaBackend, IGpuVideoBackend
{
    private VideoFrameSurface? _presentationSurface;
    private MpvVideoSession? _gpu;
    private string? _gpuFallback;
    private int _openGeneration;
    private bool _openingGpu;
    public event EventHandler<GpuVideoFramePresentedEventArgs>? GpuFramePresented;
    public VideoPlaybackStatistics PlaybackStatistics => _gpu?.Statistics
        ?? new("FFmpeg / CPU bitmap", 0, 0, 0, 0, _gpuFallback);
    public void SetPresentationSurface(VideoFrameSurface? surface) => _presentationSurface = surface;
    protected override TimeSpan QueryPosition() => _gpu is { } gpu
        ? TimeSpan.FromSeconds(gpu.Position) : base.QueryPosition();
    private VideoPlaybackQuality _playbackQuality = VideoPlaybackQuality.Original;
    private readonly object _processGate = new();
    private Process? _videoProcess;
    private Process? _audioProcess;
    private CancellationTokenSource? _playbackCts;
    private string? _source;
    private bool _headless;
    private bool _installAttempted;
    private bool _videoOutputEnabled = true;
    // This flag belongs to the FFmpeg backend (the base class intentionally
    // keeps its disposed state private).  It is guarded by _processGate so a
    // concurrent PlayAsync cannot start a new process while Dispose is closing
    // the current one.
    private bool _disposed;

    public LinuxFfmpegBackend() : base("Linux FFmpeg", CheckFfmpeg()) { }
    public override bool IsAvailable => CheckFfmpeg() || (_videoOutputEnabled && MpvNative.IsAvailable);
    public void ConfigureVideoOutput(bool enabled) => _videoOutputEnabled = enabled;
    /// <inheritdoc />
    public Task SetPlaybackQualityAsync(VideoPlaybackQuality quality, CancellationToken cancellationToken = default)
    {
        _ = VideoPlaybackProfile.FromQuality(quality);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_processGate)
        {
            if (_disposed || _playbackQuality == quality) return Task.CompletedTask;
            _playbackQuality = quality;
            if (_gpu is { } gpu) { gpu.SetQuality(quality); return Task.CompletedTask; }
            if (_videoOutputEnabled && State == MediaState.Playing) return PlayAsync(cancellationToken);
        }
        return Task.CompletedTask;
    }
    public static FfmpegAvailability CheckEnvironment()
    {
        if (!CheckFfmpeg()) return new(false, "FFmpeg was not found. Install the ffmpeg package.");
        return new(true, null);
    }

    public override async Task OpenAsync(string source, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux()) { RaiseError("The FFmpeg backend is only available on Linux."); return; }
        if (!MediaSource.TryParse(source, out var uri, out _, out var error)) { RaiseError(error!); return; }
        var generation = Interlocked.Increment(ref _openGeneration);
        _gpu?.Dispose();
        _gpu = null;
        lock (_processGate) StopProcessesLocked();
        _source = uri!.IsFile ? uri.LocalPath : source;
        _headless = string.Equals(Environment.GetEnvironmentVariable("AVALONIA_NATIVE_MEDIA_HEADLESS"), "1", StringComparison.Ordinal);
        _gpuFallback = null;
        if (_videoOutputEnabled && !_headless && _presentationSurface is { } surface && MpvNative.IsAvailable
            && Environment.GetEnvironmentVariable("AVALONIA_NATIVE_MEDIA_DISABLE_MPV") != "1")
        {
            MpvVideoSession? session = null;
            try
            {
                _openingGpu = true;
                session = new(surface);
                _gpu = session;
                session.SetQuality(_playbackQuality);
                session.SetVolume(Volume);
                session.SetMuted(Muted);
                session.Presented += (_, frame) =>
                {
                    if (ReferenceEquals(_gpu, session) && !_disposed)
                    {
                        if (session.Duration > 0) Duration = TimeSpan.FromSeconds(session.Duration);
                        GpuFramePresented?.Invoke(this, frame);
                    }
                };
                session.Ended += () => { if (ReferenceEquals(_gpu, session) && !_disposed && State != MediaState.Ended) RaiseEndedFromBackend(); };
                session.Failed += ex =>
                {
                    if (!_openingGpu && ReferenceEquals(_gpu, session) && !_disposed)
                        _ = RecoverGpuAsync(session, generation, ex);
                };
                await session.OpenAsync(_source, cancellationToken);
                if (_disposed || generation != _openGeneration) { session.Dispose(); return; }
                await base.OpenAsync(source, cancellationToken);
                if (session.Duration > 0) Duration = TimeSpan.FromSeconds(session.Duration);
                RaiseRuntimeStatus("libmpv OpenGL presentation enabled. Hardware decoder selection is reported in PlaybackStatistics.");
                return;
            }
            catch (OperationCanceledException) { session?.Dispose(); _gpu = null; throw; }
            catch (Exception ex) { session?.Dispose(); _gpu = null; _gpuFallback = ex.Message; }
            finally { _openingGpu = false; }
        }
        else if (_videoOutputEnabled) _gpuFallback = "libmpv unavailable/disabled, headless mode, or no Avalonia GPU surface.";
        if (!CheckFfmpeg())
        {
            if (MediaRuntimeOptions.AutoInstallLinuxRuntime && !_installAttempted)
            {
                _installAttempted = true;
                RaiseRuntimeStatus("正在请求系统权限安装 FFmpeg 播放环境…", true);
                var install = await LinuxRuntimeInstaller.EnsureFfmpegAsync(cancellationToken);
                if (install.Success) { RaiseRuntimeStatus(install.Message); await OpenAsync(source, cancellationToken); return; }
                RaiseRuntimeStatus(install.Message);
            }
            RaiseError(CheckEnvironment().Error!); return;
        }
        if (_gpuFallback is not null) RaiseRuntimeStatus("Using FFmpeg CPU fallback: " + _gpuFallback);
        await base.OpenAsync(source, cancellationToken);
        var duration = await ProbeDurationAsync(_source, cancellationToken);
        if (duration > TimeSpan.Zero) Duration = duration;
    }

    public override async Task PlayAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_gpu is { } gpu) { gpu.Play(); await base.PlayAsync(cancellationToken); return; }
        Process? video = null;
        Process? audio = null;
        lock (_processGate)
        {
            if (_disposed) return;
            if (_source is null) { RaiseError("Open a media source before playing."); return; }

            // PlayAsync can be triggered by both the Source/AutoPlay binding
            // and a button click.  Stop the previous generation while holding
            // the same gate used by Dispose, so no old operation can race a
            // newly-created audio process.
            StopProcessesLocked();
            if (_disposed) return;

            _playbackCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var start = Position;
            try
            {
                if (_videoOutputEnabled)
                {
                    video = StartVideoProcess(_source, start, _playbackCts.Token);
                    _videoProcess = video;
                    _ = ReadVideoFramesAsync(video, _playbackCts.Token);
                }
                if (!_headless)
                {
                    var launch = StartAudioProcess(_source, start, Volume, Muted, _playbackCts.Token);
                    audio = launch.Process;
                    _audioProcess = audio;
                    _ = MonitorAudioProcessAsync(audio, launch.Sink, launch.AllowAlsaFallback, _playbackCts.Token);
                }
            }
            catch
            {
                TryKill(video);
                TryKill(audio);
                _videoProcess = null;
                _audioProcess = null;
                _playbackCts?.Dispose();
                _playbackCts = null;
                throw;
            }
        }

        // Dispose may have run immediately after process creation.  In that
        // case it has already killed the processes; do not transition the
        // base backend to Playing or leave a process from a failed call alive.
        lock (_processGate)
        {
            if (_disposed)
            {
                StopProcessesLocked();
                return;
            }
        }

        try
        {
            await base.PlayAsync(cancellationToken);
        }
        catch
        {
            lock (_processGate) StopProcessesLocked();
            throw;
        }
    }

    public override Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_gpu is { } gpu) { gpu.Pause(); return base.PauseAsync(cancellationToken); }
        lock (_processGate) StopProcessesLocked();
        return base.PauseAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_gpu is { } gpu) { gpu.Stop(); return base.StopAsync(cancellationToken); }
        lock (_processGate) StopProcessesLocked();
        return base.StopAsync(cancellationToken);
    }

    public override void Seek(TimeSpan position)
    {
        base.Seek(position);
        if (_gpu is { } gpu) { gpu.Seek(position); return; }
        if (State == MediaState.Playing && _source is not null) _ = PlayAsync();
    }

    public override double Volume
    {
        get => base.Volume;
        set { base.Volume = value; if (_gpu is { } gpu) gpu.SetVolume(value); else if (State == MediaState.Playing && _source is not null) _ = Task.Run(RestartAudioAsync); }
    }
    public override bool Muted
    {
        get => base.Muted;
        set { base.Muted = value; if (_gpu is { } gpu) gpu.SetMuted(value); else if (State == MediaState.Playing && _source is not null) _ = Task.Run(RestartAudioAsync); }
    }

    private async Task RecoverGpuAsync(MpvVideoSession session, int generation, Exception error)
    {
        // Serialize recovery on the UI dispatcher to avoid detaching a newer source.
        await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_disposed || generation != _openGeneration || !ReferenceEquals(_gpu, session)) return;
            var position = TimeSpan.FromSeconds(session.Position);
            var playing = State == MediaState.Playing;
            _gpu = null;
            session.Dispose();
            _gpuFallback = error.Message;
            if (!CheckFfmpeg()) { RaiseError("libmpv failed and FFmpeg fallback is unavailable: " + error.Message, error); return; }
            RaiseRuntimeStatus("GPU playback failed; using FFmpeg CPU fallback: " + error.Message);
            base.Seek(position);
            if (playing)
            {
                try { await PlayAsync(); }
                catch (Exception ex) { if (!_disposed) RaiseError("FFmpeg fallback failed: " + ex.Message, ex); }
            }
        });
    }

    private Task RestartAudioAsync()
    {
        lock (_processGate)
        {
            if (_disposed || _source is null || _headless || _playbackCts is null) return Task.CompletedTask;
            var old = _audioProcess;
            _audioProcess = null;
            TryKill(old);
            if (_disposed || _playbackCts.IsCancellationRequested) return Task.CompletedTask;
            try
            {
                var launch = StartAudioProcess(_source, Position, Volume, Muted, _playbackCts.Token);
                _audioProcess = launch.Process;
                _ = MonitorAudioProcessAsync(launch.Process, launch.Sink, launch.AllowAlsaFallback, _playbackCts.Token);
            }
            catch (Exception ex)
            {
                RaiseError("FFmpeg audio playback failed: " + ex.Message, ex);
            }
        }
        return Task.CompletedTask;
    }

    private Process StartVideoProcess(string source, TimeSpan start, CancellationToken token)
    {
        var process = StartFfmpeg(source, start, CreateVideoOutputArguments(_playbackQuality));
        _ = DrainStderrAsync(process, token);
        return process;
    }

    internal static string[] CreateVideoOutputArguments(VideoPlaybackQuality quality)
    {
        var profile = VideoPlaybackProfile.FromQuality(quality);
        var args = new List<string> { "-map", "0:v:0", "-an" };
        if (profile.MaximumLongEdge > 0)
        {
            // Fit both landscape and portrait sources, without upscaling.
            var scale = $"min(1,min({profile.MaximumLongEdge}/max(iw,ih),{profile.MaximumShortEdge}/min(iw,ih)))";
            args.Add("-vf");
            args.Add($"select='isnan(prev_selected_t)+gte(t-prev_selected_t,1/{profile.MaximumFrameRate}-0.000001)',scale=w='max(1,floor(iw*{scale}))':h='max(1,floor(ih*{scale}))':flags=lanczos");
        }
        // Each uncompressed BMP carries its own dimensions, including after a
        // resolution change. Original uses neither a scaling nor an fps filter.
        args.AddRange(["-fps_mode", "passthrough", "-c:v", "bmp", "-pix_fmt", "bgra", "-f", "image2pipe", "pipe:1"]);
        return args.ToArray();
    }
    private AudioProcessLaunch StartAudioProcess(string source, TimeSpan start, double volume, bool muted, CancellationToken token)
    {
        var filter = muted ? "volume=0" : $"volume={volume.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var configured = Environment.GetEnvironmentVariable("AVALONIA_NATIVE_MEDIA_AUDIO_SINK");
        var sink = _headless ? "null" : ResolveAudioSink(
            configured,
            Environment.GetEnvironmentVariable("PULSE_SERVER"),
            Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
            Environment.GetEnvironmentVariable("DISPLAY"),
            Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"),
            File.Exists);
        var pulseServer = sink == "pulse"
            ? ResolvePulseServer(
                Environment.GetEnvironmentVariable("PULSE_SERVER"),
                Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
                Environment.GetEnvironmentVariable("SUDO_UID"),
                string.Equals(Environment.UserName, "root", StringComparison.Ordinal),
                File.Exists,
                Directory.EnumerateDirectories)
            : null;
        var automatic = string.IsNullOrWhiteSpace(configured)
            || string.Equals(configured.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
        return StartAudioProcessForSink(
            source,
            start,
            filter,
            sink,
            pulseServer,
            automatic && sink == "pulse",
            token);
    }

    private AudioProcessLaunch StartAudioProcessForSink(
        string source,
        TimeSpan start,
        string filter,
        string sink,
        string? pulseServer,
        bool allowAlsaFallback,
        CancellationToken token)
    {
        var args = sink switch
        {
            "pulse" => new[] { "-map", "0:a:0?", "-vn", "-af", filter, "-f", "pulse", "default" },
            "null" => new[] { "-map", "0:a:0?", "-vn", "-af", filter, "-f", "null", "-" },
            _ => new[] { "-map", "0:a:0?", "-vn", "-af", filter, "-f", "alsa", "default" }
        };
        var process = StartFfmpeg(source, start, args, pulseServer);
        return new(process, sink, allowAlsaFallback);
    }

    internal static string ResolveAudioSink(
        string? configured,
        string? pulseServer,
        string? xdgRuntimeDirectory,
        string? display,
        string? waylandDisplay,
        Func<string, bool> fileExists)
    {
        var requested = configured?.Trim().ToLowerInvariant();
        if (requested is "pulse" or "pipewire") return "pulse";
        if (requested is "alsa" or "null") return requested;
        if (!string.IsNullOrEmpty(requested) && requested != "auto")
            throw new InvalidOperationException(
                $"Unsupported AVALONIA_NATIVE_MEDIA_AUDIO_SINK value '{configured}'. Use auto, pulse, pipewire, or alsa.");

        var pulseSocket = string.IsNullOrWhiteSpace(xdgRuntimeDirectory)
            ? null
            : Path.Combine(xdgRuntimeDirectory, "pulse", "native");
        var desktopSession = !string.IsNullOrWhiteSpace(display) || !string.IsNullOrWhiteSpace(waylandDisplay);
        return !string.IsNullOrWhiteSpace(pulseServer)
               || (pulseSocket is not null && fileExists(pulseSocket))
               || desktopSession
            ? "pulse"
            : "alsa";
    }

    internal static string? ResolvePulseServer(
        string? configuredPulseServer,
        string? xdgRuntimeDirectory,
        string? sudoUid,
        bool searchOtherUserSessions,
        Func<string, bool> fileExists,
        Func<string, IEnumerable<string>> enumerateDirectories)
    {
        if (!string.IsNullOrWhiteSpace(configuredPulseServer)) return configuredPulseServer.Trim();

        var checkedRuntimeDirectories = new HashSet<string>(StringComparer.Ordinal);
        string? TryRuntimeDirectory(string? runtimeDirectory)
        {
            if (string.IsNullOrWhiteSpace(runtimeDirectory)
                || !checkedRuntimeDirectories.Add(runtimeDirectory)) return null;

            var socket = Path.Combine(runtimeDirectory, "pulse", "native");
            return fileExists(socket) ? "unix:" + socket : null;
        }

        var server = TryRuntimeDirectory(xdgRuntimeDirectory);
        if (server is not null) return server;

        if (!string.IsNullOrWhiteSpace(sudoUid)
            && uint.TryParse(sudoUid, out _))
        {
            server = TryRuntimeDirectory(Path.Combine("/run/user", sudoUid));
            if (server is not null) return server;
        }

        if (!searchOtherUserSessions) return null;

        string[] runtimeDirectories;
        try
        {
            runtimeDirectories = enumerateDirectories("/run/user")
                .Where(static directory => uint.TryParse(Path.GetFileName(directory), out _))
                .OrderBy(static directory => uint.Parse(Path.GetFileName(directory)))
                .ToArray();
        }
        catch
        {
            return null;
        }

        foreach (var runtimeDirectory in runtimeDirectories)
        {
            server = TryRuntimeDirectory(runtimeDirectory);
            if (server is not null) return server;
        }

        return null;
    }

    private async Task MonitorAudioProcessAsync(
        Process process,
        string sink,
        bool allowAlsaFallback,
        CancellationToken token)
    {
        string diagnostic;
        int exitCode;
        try
        {
            diagnostic = await process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception) when (token.IsCancellationRequested) { return; }
        catch (ObjectDisposedException) { return; }

        Process? fallback = null;
        lock (_processGate)
        {
            if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_audioProcess, process)) return;
            _audioProcess = null;
            if (exitCode != 0 && allowAlsaFallback)
            {
                try
                {
                    var filter = Muted
                        ? "volume=0"
                        : $"volume={Volume.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                    var launch = StartAudioProcessForSink(_source!, Position, filter, "alsa", null, false, token);
                    fallback = launch.Process;
                    _audioProcess = fallback;
                }
                catch (Exception ex)
                {
                    diagnostic = string.IsNullOrWhiteSpace(diagnostic)
                        ? ex.Message
                        : diagnostic.Trim() + " " + ex.Message;
                }
            }
        }

        try { process.Dispose(); } catch { }
        if (fallback is not null)
        {
            Debug.WriteLine("FFmpeg PulseAudio/PipeWire output failed; falling back to ALSA. " + diagnostic.Trim());
            _ = MonitorAudioProcessAsync(fallback, "alsa", false, token);
            return;
        }

        if (exitCode != 0 && !IsMissingAudioStreamDiagnostic(diagnostic))
        {
            var detail = string.IsNullOrWhiteSpace(diagnostic)
                ? $"FFmpeg {sink} output exited with code {exitCode}."
                : diagnostic.Trim();
            RaiseRuntimeStatus("FFmpeg audio output failed: " + TrimDiagnostic(detail));
        }
    }

    private static bool IsMissingAudioStreamDiagnostic(string diagnostic)
        => diagnostic.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase)
           || diagnostic.Contains("matches no streams", StringComparison.OrdinalIgnoreCase);

    private static string TrimDiagnostic(string value)
        => value.Length <= 500 ? value : value[..500] + "…";

    private readonly record struct AudioProcessLaunch(Process Process, string Sink, bool AllowAlsaFallback);
    private Process StartFfmpeg(
        string source,
        TimeSpan start,
        IEnumerable<string> outputArgs,
        string? pulseServer = null)
    {
        var psi = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (!string.IsNullOrWhiteSpace(pulseServer)) psi.Environment["PULSE_SERVER"] = pulseServer;
        psi.ArgumentList.Add("-hide_banner"); psi.ArgumentList.Add("-loglevel"); psi.ArgumentList.Add("error"); psi.ArgumentList.Add("-nostdin"); psi.ArgumentList.Add("-re");
        if (start > TimeSpan.Zero) { psi.ArgumentList.Add("-ss"); psi.ArgumentList.Add(start.TotalSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); }
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(source);
        foreach (var arg in outputArgs) psi.ArgumentList.Add(arg);
        return Process.Start(psi) ?? throw new InvalidOperationException("Could not start ffmpeg.");
    }
    private async Task ReadVideoFramesAsync(Process process, CancellationToken token)
    {
        try
        {
            var stream = process.StandardOutput.BaseStream;
            var header = new byte[14];
            byte[] frameBuffer = [];
            while (!token.IsCancellationRequested)
            {
                if (await stream.ReadAsync(header.AsMemory(0, 1), token) == 0) break;
                await stream.ReadExactlyAsync(header.AsMemory(1), token);
                var frameLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(2, 4));
                if (header[0] != 'B' || header[1] != 'M' || frameLength < 54 || frameLength > 512 * 1024 * 1024)
                    throw new InvalidDataException("Invalid FFmpeg BMP frame header or unsupported frame size.");
                var length = checked((int)frameLength);
                if (frameBuffer.Length != length) frameBuffer = new byte[length];
                header.CopyTo(frameBuffer, 0);
                await stream.ReadExactlyAsync(frameBuffer.AsMemory(header.Length), token);
                token.ThrowIfCancellationRequested();
                using var imageStream = new MemoryStream(frameBuffer, 0, length, writable: false);
                var bitmap = new Bitmap(imageStream);
                if (token.IsCancellationRequested) { bitmap.Dispose(); return; }
                RaiseVideoFrame(bitmap, Position, ComputeFrameHash(frameBuffer));
            }
            if (!token.IsCancellationRequested)
            {
                await process.WaitForExitAsync(token);
                if (process.ExitCode == 0) RaiseEndedFromBackend();
                else RaiseError($"FFmpeg video decode exited with code {process.ExitCode}.");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) RaiseError("FFmpeg video decode failed: " + ex.Message, ex); }
    }
    private static async Task DrainStderrAsync(Process process, CancellationToken token)
    { try { await process.StandardError.ReadToEndAsync(token); } catch { } }
    private static ulong ComputeFrameHash(ReadOnlySpan<byte> bytes)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        var step = Math.Max(4, bytes.Length / 2048);
        for (var i = 0; i < bytes.Length; i += step) hash = (hash ^ bytes[i]) * prime;
        return hash;
    }
    private void StopProcessesLocked()
    {
        _playbackCts?.Cancel();
        TryKill(_videoProcess); TryKill(_audioProcess);
        try { _videoProcess?.WaitForExit(3000); } catch { }
        try { _audioProcess?.WaitForExit(3000); } catch { }
        try { _videoProcess?.Dispose(); } catch { }
        try { _audioProcess?.Dispose(); } catch { }
        _videoProcess = null; _audioProcess = null; _playbackCts?.Dispose(); _playbackCts = null;
    }
    public override void Dispose()
    {
        Interlocked.Increment(ref _openGeneration);
        _gpu?.Dispose();
        _gpu = null;
        lock (_processGate)
        {
            if (_disposed) return;
            // Set this before killing anything.  A PlayAsync that is resuming
            // on another thread will observe the flag and cannot create audio
            // after the window has started closing.
            _disposed = true;
            StopProcessesLocked();
        }
        base.Dispose();
    }
    private static void TryKill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                // Wait synchronously so the ALSA/Pulse device is released
                // before the owning window is allowed to finish closing.
                process.WaitForExit(3000);
            }
        }
        catch { }
    }
    private static bool CheckFfmpeg()
    {
        if (!OperatingSystem.IsLinux()) return false;
        if (File.Exists("/usr/bin/ffmpeg") || File.Exists("/usr/local/bin/ffmpeg")) return true;
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path)) return false;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                if (File.Exists(Path.Combine(directory, "ffmpeg"))) return true;
            }
            catch (ArgumentException) { }
        }
        return false;
    }
    private static async Task<TimeSpan> ProbeDurationAsync(string source, CancellationToken token)
    {
        try
        {
            var psi = new ProcessStartInfo("ffprobe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            psi.ArgumentList.Add("-v"); psi.ArgumentList.Add("error"); psi.ArgumentList.Add("-show_entries"); psi.ArgumentList.Add("format=duration"); psi.ArgumentList.Add("-of"); psi.ArgumentList.Add("default=noprint_wrappers=1:nokey=1"); psi.ArgumentList.Add(source);
            using var p = Process.Start(psi); if (p is null) return TimeSpan.Zero;
            var text = await p.StandardOutput.ReadToEndAsync(token); await p.WaitForExitAsync(token);
            return double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? TimeSpan.FromSeconds(Math.Max(0, seconds)) : TimeSpan.Zero;
        }
        catch { return TimeSpan.Zero; }
    }
    public readonly record struct FfmpegAvailability(bool IsAvailable, string? Error);
}

/// <summary>启用 Linux FFmpeg 播放后端。<para>Enables the Linux FFmpeg playback backend.</para></summary>
public static class LinuxFfmpeg
{
    /// <summary>注册 Linux FFmpeg 后端。<para>Registers the Linux FFmpeg backend.</para></summary>
    public static void Use() => MediaBackendFactory.Register(static () => new LinuxFfmpegBackend());
}
