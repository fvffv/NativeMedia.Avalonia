namespace NativeMedia.Avalonia;

/// <summary>
/// 媒体源的类型。
/// <para>Kind of a media source.</para>
/// </summary>
public enum MediaSourceKind { None, LocalFile, FileUri, Http, Https }

/// <summary>媒体源解析辅助方法。<para>Helpers for parsing media sources.</para></summary>
public static class MediaSource
{
    /// <summary>
    /// 解析本地路径、文件 URI 或 HTTP(S) 地址，并返回规范化 URI 和类型。
    /// <para>Parses a local path, file URI, or HTTP(S) URL and returns a normalized URI and source kind.</para>
    /// </summary>
    /// <param name="source">媒体路径或 URL。<para>Media path or URL.</para></param>
    /// <param name="uri">解析后的 URI；解析失败时为 <see langword="null" />。<para>Parsed URI, or <see langword="null" /> when parsing fails.</para></param>
    /// <param name="kind">媒体源类型。<para>Detected source kind.</para></param>
    /// <param name="error">失败原因；成功时为 <see langword="null" />。<para>Error message, or <see langword="null" /> on success.</para></param>
    /// <returns>解析成功返回 <see langword="true" />。<para><see langword="true" /> when parsing succeeds.</para></returns>
    public static bool TryParse(string? source, out Uri? uri, out MediaSourceKind kind, out string? error)
    {
        uri = null;
        kind = MediaSourceKind.None;
        error = null;
        if (string.IsNullOrWhiteSpace(source)) { error = "Media source is empty."; return false; }
        if (Uri.TryCreate(source, UriKind.Absolute, out var parsed))
        {
            kind = parsed.Scheme.ToLowerInvariant() switch
            {
                "http" => MediaSourceKind.Http,
                "https" => MediaSourceKind.Https,
                "file" => MediaSourceKind.FileUri,
                _ => MediaSourceKind.None
            };
            if (kind is MediaSourceKind.Http or MediaSourceKind.Https or MediaSourceKind.FileUri)
            {
                uri = parsed;
                if (kind is MediaSourceKind.Http or MediaSourceKind.Https && string.IsNullOrWhiteSpace(parsed.Host))
                { error = "The network media URL has no host."; return false; }
                if (kind == MediaSourceKind.FileUri)
                {
                    if (string.IsNullOrWhiteSpace(parsed.LocalPath)) { error = "The file URI has no local path."; return false; }
                    if (!File.Exists(parsed.LocalPath)) { error = $"Media file does not exist: {parsed.LocalPath}"; return false; }
                }
                return true;
            }
        }
        kind = MediaSourceKind.LocalFile;
        var fullPath = Path.GetFullPath(source);
        if (!File.Exists(fullPath)) { error = $"Media file does not exist: {fullPath}"; return false; }
        uri = new Uri(fullPath);
        return true;
    }

    /// <summary>判断媒体源是否为 HTTP 或 HTTPS 流媒体。<para>Determines whether a source is an HTTP or HTTPS stream.</para></summary>
    /// <param name="source">媒体路径或 URL。<para>Media path or URL.</para></param>
    /// <returns>网络媒体返回 <see langword="true" />。<para><see langword="true" /> for network media.</para></returns>
    public static bool IsStreaming(string? source)
        => TryParse(source, out _, out var kind, out _) && kind is MediaSourceKind.Http or MediaSourceKind.Https;
}
