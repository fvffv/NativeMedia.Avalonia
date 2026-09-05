using System.Diagnostics;
using Avalonia;
using global::Avalonia.Media.Imaging;
using NativeMedia.Avalonia;

namespace NativeMedia.Avalonia.Linux;

/// <summary>
/// Linux FFmpeg backend. FFmpeg is an external system executable; decoded video
/// frames are copied as BGRA into the normal Avalonia scene so overlays remain possible.
/// Audio is sent by FFmpeg to the system ALSA/Pulse/PipeWire default device.
/// </summary>
public sealed class LinuxFfmpegBackend : NativeMediaBackend
{
    private const int FrameWidth = 960;
    private const int FrameHeight = 540;
    private readonly object _processGate = new();
    private Process? _videoProcess;
    private Process? _audioProcess;
    private CancellationTokenSource? _playbackCts;
    private string? _source;
    private bool _headless;
    private bool _installAttempted;
    // This flag belongs to the FFmpeg backend (the base class intentionally
    // keeps its disposed state private).  It is guarded by _processGate so a
    // concurrent PlayAsync cannot start a new process while Dispose is closing
    // the current one.
    private bool _disposed;
    private readonly byte[] _frameBuffer = new byte[FrameWidth * FrameHeight * 4];

    public LinuxFfmpegBackend() : base("Linux FFmpeg", CheckFfmpeg()) { }
    public override bool IsAvailable => CheckFfmpeg();
    public static FfmpegAvailability CheckEnvironment()
    {
        if (!CheckFfmpeg()) return new(false, "FFmpeg was not found. Install the ffmpeg package.");
        return new(true, null);
    }

    public override async Task OpenAsync(string source, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux()) { RaiseError("The FFmpeg backend is only available on Linux."); return; }
        if (!IsAvailable)
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
        if (!MediaSource.TryParse(source, out var uri, out _, out var error)) { RaiseError(error!); return; }
        _source = uri!.IsFile ? uri.LocalPath : source;
        _headless = string.Equals(Environment.GetEnvironmentVariable("AVALONIA_NATIVE_MEDIA_HEADLESS"), "1", StringComparison.Ordinal);
        await base.OpenAsync(source, cancellationToken);
        var duration = await ProbeDurationAsync(_source, cancellationToken);
        if (duration > TimeSpan.Zero) Duration = duration;
    }

    public override async Task PlayAsync(CancellationToken cancellationToken = default)
    {
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
                video = StartVideoProcess(_source, start, _playbackCts.Token);
                _videoProcess = video;
                _ = ReadVideoFramesAsync(video, _playbackCts.Token);
                if (!_headless)
                {
                    audio = StartAudioProcess(_source, start, Volume, Muted, _playbackCts.Token);
                    _audioProcess = audio;
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
        lock (_processGate) StopProcessesLocked();
        return base.PauseAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_processGate) StopProcessesLocked();
        return base.StopAsync(cancellationToken);
    }

    public override void Seek(TimeSpan position)
    {
        base.Seek(position);
        if (State == MediaState.Playing && _source is not null) _ = PlayAsync();
    }

    public override double Volume
    {
        get => base.Volume;
        set { base.Volume = value; if (State == MediaState.Playing && _source is not null) _ = RestartAudioAsync(); }
    }
    public override bool Muted
    {
        get => base.Muted;
        set { base.Muted = value; if (State == MediaState.Playing && _source is not null) _ = RestartAudioAsync(); }
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
                _audioProcess = StartAudioProcess(_source, Position, Volume, Muted, _playbackCts.Token);
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
        var process = StartFfmpeg(source, start, ["-map", "0:v:0", "-an", "-vf", $"scale={FrameWidth}:{FrameHeight}:force_original_aspect_ratio=decrease,pad={FrameWidth}:{FrameHeight}:(ow-iw)/2:(oh-ih)/2:black", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"]);
        _ = DrainStderrAsync(process, token);
        return process;
    }
    private Process StartAudioProcess(string source, TimeSpan start, double volume, bool muted, CancellationToken token)
    {
        var filter = muted ? "volume=0" : $"volume={volume.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var output = _headless ? "null" : (Environment.GetEnvironmentVariable("AVALONIA_NATIVE_MEDIA_AUDIO_SINK")?.Trim().ToLowerInvariant() ?? "alsa");
        var args = output switch
        {
            "pulse" => new[] { "-map", "0:a:0?", "-vn", "-af", filter, "-f", "pulse", "default" },
            "null" => new[] { "-map", "0:a:0?", "-vn", "-af", filter, "-f", "null", "-" },
            _ => new[] { "-map", "0:a:0?", "-vn", "-af", filter, "-f", "alsa", "default" }
        };
        var process = StartFfmpeg(source, start, args);
        _ = DrainStderrAsync(process, token);
        return process;
    }
    private Process StartFfmpeg(string source, TimeSpan start, IEnumerable<string> outputArgs)
    {
        var psi = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
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
            while (!token.IsCancellationRequested && !process.HasExited)
            {
                var read = 0;
                while (read < _frameBuffer.Length)
                {
                    var count = await process.StandardOutput.BaseStream.ReadAsync(_frameBuffer.AsMemory(read), token);
                    if (count == 0) return;
                    read += count;
                }
                var bitmap = new WriteableBitmap(new PixelSize(FrameWidth, FrameHeight), new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Opaque);
                using (var locked = bitmap.Lock())
                { for (var y = 0; y < FrameHeight; y++) System.Runtime.InteropServices.Marshal.Copy(_frameBuffer, y * FrameWidth * 4, locked.Address + y * locked.RowBytes, FrameWidth * 4); }
                RaiseVideoFrame(bitmap);
            }
            if (!token.IsCancellationRequested) RaiseEndedFromBackend();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) RaiseError("FFmpeg video decode failed: " + ex.Message, ex); }
    }
    private static async Task DrainStderrAsync(Process process, CancellationToken token)
    { try { await process.StandardError.ReadToEndAsync(token); } catch { } }
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
    private static bool CheckFfmpeg() => OperatingSystem.IsLinux() && (FindExecutable("ffmpeg") || FindExecutable("/usr/bin/ffmpeg"));
    private static bool FindExecutable(string name) { try { using var p = Process.Start(new ProcessStartInfo(name, "-version") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }); p?.WaitForExit(1500); return p?.ExitCode == 0; } catch { return false; } }
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
