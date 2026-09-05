using System.ComponentModel;
using System.Diagnostics;
using NativeMedia.Avalonia;

namespace NativeMedia.Avalonia.Linux;

/// <summary>
/// Installs the Linux media runtime through the distribution's native package
/// manager. No shell is used: every argument is passed through
/// <see cref="ProcessStartInfo.ArgumentList"/>.
/// </summary>
public static class LinuxRuntimeInstaller
{
    private static readonly string[] AptPackages =
    [
        "gstreamer1.0-plugins-base", "gstreamer1.0-plugins-good",
        "gstreamer1.0-libav", "gstreamer1.0-x", "gstreamer1.0-gl", "gstreamer1.0-alsa"
    ];

    private static readonly PackageManagerSpec[] PackageManagers =
    [
        new(PackageManagerKind.Apt, "apt-get", ["apt-get", "apt"]),
        new(PackageManagerKind.Dnf, "dnf", ["dnf", "dnf5"]),
        new(PackageManagerKind.MicroDnf, "microdnf", ["microdnf"]),
        new(PackageManagerKind.Yum, "yum", ["yum"]),
        new(PackageManagerKind.Pacman, "pacman", ["pacman"]),
        new(PackageManagerKind.Zypper, "zypper", ["zypper"]),
        new(PackageManagerKind.Apk, "apk", ["apk"]),
        new(PackageManagerKind.Xbps, "xbps-install", ["xbps-install"]),
        new(PackageManagerKind.Emerge, "emerge", ["emerge"])
    ];

    /// <summary>Installs the legacy GStreamer runtime used by the optional GStreamer backend.</summary>
    public static async Task<RuntimeInstallResult> EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux()) return new(false, "Linux runtime installation is only available on Linux.");
        var manager = DetectPackageManager();
        if (manager is null) return UnsupportedManager("GStreamer");

        try
        {
            var result = await RunElevatedAsync(manager.Value, GetGStreamerArguments(manager.Value.Kind), cancellationToken);
            return result.ExitCode == 0
                ? new(true, "Linux media runtime installation completed. Try playback again.")
                : InstallationFailed(manager.Value, result, "GStreamer");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { return new(false, $"Could not start the Linux package manager: {ex.Message}"); }
    }

    /// <summary>Installs FFmpeg and ffprobe for the Linux FFmpeg backend.</summary>
    public static async Task<RuntimeInstallResult> EnsureFfmpegAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux()) return new(false, "Linux runtime installation is only available on Linux.");
        var manager = DetectPackageManager();
        if (manager is null) return UnsupportedManager("FFmpeg");

        try
        {
            var result = await RunElevatedAsync(manager.Value, GetFfmpegArguments(manager.Value.Kind), cancellationToken);
            return result.ExitCode == 0
                ? new(true, "FFmpeg installation completed. Try playback again.")
                : InstallationFailed(manager.Value, result, "FFmpeg");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { return new(false, "Could not start the package manager: " + ex.Message); }
    }

    private static string[] GetFfmpegArguments(PackageManagerKind kind) => kind switch
    {
        // --no-install-recommends is an APT-only option. Passing it to dnf
        // was the reason Fedora installation failed in the previous version.
        PackageManagerKind.Apt => ["install", "-y", "--no-install-recommends", "ffmpeg"],
        PackageManagerKind.Dnf or PackageManagerKind.MicroDnf or PackageManagerKind.Yum => ["install", "-y", "ffmpeg"],
        PackageManagerKind.Pacman => ["-Sy", "--noconfirm", "ffmpeg"],
        PackageManagerKind.Zypper => ["--non-interactive", "install", "ffmpeg"],
        PackageManagerKind.Apk => ["add", "--no-cache", "ffmpeg"],
        PackageManagerKind.Xbps => ["-S", "-y", "ffmpeg"],
        PackageManagerKind.Emerge => ["--oneshot", "media-video/ffmpeg"],
        _ => ["install", "-y", "ffmpeg"]
    };

    private static string[] GetGStreamerArguments(PackageManagerKind kind) => kind switch
    {
        PackageManagerKind.Apt => ["install", "-y", "--no-install-recommends", .. AptPackages],
        PackageManagerKind.Dnf or PackageManagerKind.MicroDnf or PackageManagerKind.Yum =>
            ["install", "-y", "gstreamer1", "gstreamer1-plugins-base", "gstreamer1-plugins-good", "gstreamer1-libav", "gstreamer1-plugins-bad-free"],
        PackageManagerKind.Pacman => ["-Sy", "--noconfirm", "gstreamer", "gst-plugins-base", "gst-plugins-good", "gst-libav"],
        PackageManagerKind.Zypper => ["--non-interactive", "install", "gstreamer", "gstreamer-plugins-base", "gstreamer-plugins-good", "gstreamer-plugins-libav"],
        PackageManagerKind.Apk => ["add", "--no-cache", "gstreamer", "gst-plugins-base", "gst-plugins-good", "gst-libav"],
        PackageManagerKind.Xbps => ["-S", "-y", "gstreamer", "gst-plugins-base", "gst-plugins-good", "gst-libav"],
        PackageManagerKind.Emerge => ["--oneshot", "media-libs/gstreamer", "media-plugins/gst-plugins-base", "media-plugins/gst-plugins-good", "media-plugins/gst-plugins-libav"],
        _ => ["install", "-y", "gstreamer"]
    };

    private static PackageManager? DetectPackageManager()
    {
        foreach (var spec in PackageManagers)
        {
            var executable = FindExecutable(spec.ExecutableNames);
            if (executable is not null) return new(spec.Kind, spec.Name, executable);
        }
        return null;
    }

    private static string? FindExecutable(params string[] names)
    {
        foreach (var name in names)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (path is null) continue;
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    private static async Task<InstallCommandResult> RunElevatedAsync(
        PackageManager manager,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var launcher = FindExecutable("pkexec") ?? FindExecutable("sudo");
        if (launcher is null)
            throw new InvalidOperationException("Neither pkexec nor sudo was found.");

        var psi = new ProcessStartInfo
        {
            FileName = launcher,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(manager.Executable);
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        await Task.WhenAll(stdout, stderr);
        var diagnostic = (await stderr).Trim();
        if (diagnostic.Length == 0) diagnostic = (await stdout).Trim();
        return new(process.ExitCode, diagnostic);
    }

    private static RuntimeInstallResult UnsupportedManager(string runtime)
        => new(false, $"No supported Linux package manager was found for {runtime}. Install it manually. Supported managers: apt, dnf/yum, pacman, zypper, apk, xbps, emerge.");

    private static RuntimeInstallResult InstallationFailed(PackageManager manager, InstallCommandResult result, string runtime)
    {
        var detail = string.IsNullOrWhiteSpace(result.Diagnostic)
            ? string.Empty
            : " " + TrimDiagnostic(result.Diagnostic);
        return new(false, $"The {manager.Name} package manager exited with code {result.ExitCode} while installing {runtime}.{detail}");
    }

    private static string TrimDiagnostic(string diagnostic)
        => diagnostic.Length <= 600 ? diagnostic : diagnostic[..600] + "…";

    private enum PackageManagerKind
    {
        Apt,
        Dnf,
        MicroDnf,
        Yum,
        Pacman,
        Zypper,
        Apk,
        Xbps,
        Emerge
    }

    private readonly record struct PackageManager(PackageManagerKind Kind, string Name, string Executable);
    private readonly record struct PackageManagerSpec(PackageManagerKind Kind, string Name, string[] ExecutableNames);
    private readonly record struct InstallCommandResult(int ExitCode, string Diagnostic);

    public readonly record struct RuntimeInstallResult(bool Success, string Message);
}
