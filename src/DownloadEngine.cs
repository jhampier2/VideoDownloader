using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VideoDownloader;

public class DownloadEngine
{
    private static readonly string YtDlpName  = OperatingSystem.IsWindows() ? "yt-dlp.exe"  : "yt-dlp";
    private static readonly string FfmpegName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    // Líneas de yt-dlp que indican que ffmpeg está trabajando
    private static readonly string[] PostProcessMarkers =
        ["[Merger]", "[ffmpeg]", "[ExtractAudio]", "[EmbedThumbnail]",
         "[Metadata]", "[FixupM3u8]", "[FixupTimestamp]"];

    // ─── Verificar dependencias ───────────────────────────────────────────────
    public static (bool ytdlp, bool ffmpeg) CheckDependencies()
    {
        bool hasYtDlp  = IsToolAvailable(YtDlpName);
        bool hasFfmpeg = IsToolAvailable(FfmpegName);
        return (hasYtDlp, hasFfmpeg);
    }

    /// <summary>
    /// Devuelve las versiones instaladas de yt-dlp y ffmpeg.
    /// Retorna null para cada herramienta que no esté disponible.
    /// </summary>
    public static async Task<(string? ytdlpVersion, string? ffmpegVersion)> GetVersionsAsync()
    {
        var ytdlp  = await GetToolVersionAsync(YtDlpName);
        var ffmpeg = await GetToolVersionAsync(FfmpegName);
        return (ytdlp, ffmpeg);
    }

    private static async Task<string?> GetToolVersionAsync(string tool)
    {
        try
        {
            var (output, _, code) = await RunProcessAsync(tool, "--version", null, CancellationToken.None);
            if (code != 0 || string.IsNullOrWhiteSpace(output)) return null;
            // ffmpeg emite varias líneas; solo nos interesa la primera
            return output.Split('\n')[0].Trim();
        }
        catch { return null; }
    }

    private static bool IsToolAvailable(string tool)
    {
        string localPath = Path.Combine(AppContext.BaseDirectory, tool);
        if (File.Exists(localPath)) return true;

        try
        {
            var psi = new ProcessStartInfo(tool, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(3000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    // ─── Detectar tipo de URL ─────────────────────────────────────────────────
    /// <summary>
    /// Consulta yt-dlp con --flat-playlist para saber si la URL es un video
    /// individual o una playlist, sin descargar nada ni pedir info completa.
    /// Es rápido porque --flat-playlist no resuelve cada entrada.
    /// </summary>
    public static async Task<(UrlType type, int? count)> DetectUrlTypeAsync(
        string url, CancellationToken ct = default)
    {
        var args = $"--flat-playlist --dump-single-json --no-warnings \"{url}\"";
        var (output, _, code) = await RunProcessAsync(YtDlpName, args, null, ct);

        if (code != 0 || string.IsNullOrWhiteSpace(output))
            return (UrlType.Unknown, null);

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            var typeStr = root.TryGetProperty("_type", out var t)
                ? t.GetString() : null;

            int? count = null;
            if (root.TryGetProperty("entries", out var entries))
                count = entries.GetArrayLength();

            return typeStr == "playlist"
                ? (UrlType.Playlist, count)
                : (UrlType.Video,    null);
        }
        catch { return (UrlType.Unknown, null); }
    }

    // ─── Obtener info del video ───────────────────────────────────────────────
    public static async Task<VideoInfo?> GetVideoInfoAsync(string url, CancellationToken ct = default)
    {
        var args = $"--dump-json --no-playlist \"{url}\"";
        var (output, _, code) = await RunProcessAsync(YtDlpName, args, null, ct);
        if (code != 0 || string.IsNullOrWhiteSpace(output)) return null;

        try   { return JsonSerializer.Deserialize<VideoInfo>(output); }
        catch { return null; }
    }

    // ─── Descargar video ──────────────────────────────────────────────────────
    /// <param name="onProgress">Progreso de descarga (porcentaje, velocidad, ETA).</param>
    /// <param name="onPostProcess">Se invoca cuando ffmpeg está procesando el archivo.</param>
    /// <param name="onLog">Líneas de log generales.</param>
    /// <param name="audioOnly">Si es true extrae audio MP3 en vez de guardar video.</param>
    public static async Task<bool> DownloadAsync(
        string url,
        string outputDir,
        string formatArg,
        Action<DownloadProgress>? onProgress,
        Action<string>? onPostProcess,          // ← NUEVO: callback para fase ffmpeg
        Action<string>? onLog,
        CancellationToken ct = default,
        bool audioOnly = false)
    {
        Directory.CreateDirectory(outputDir);
        var outputTemplate = Path.Combine(outputDir, "%(title).80s.%(ext)s");
        string ffmpegPath  = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        string modeFlags = audioOnly
            ? "--extract-audio --audio-format mp3 --audio-quality 0 "
            : "--merge-output-format mp4 --embed-thumbnail ";

        var args =
            $"--ffmpeg-location \"{ffmpegPath}\" " +
            $"--format \"{formatArg}\" "           +
            modeFlags                              +
            "--embed-metadata "                    +
            "--add-metadata "                      +
            "--no-playlist "                       +
            "--newline "                           +
            $"-o \"{outputTemplate}\" "            +
            $"\"{url}\"";

        var (_, _, code) = await RunProcessAsync(
            YtDlpName, args,
            line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;

                // ¿Es una línea de progreso de descarga?
                if (line.Contains("[download]") && line.Contains('%'))
                {
                    var p = ParseProgress(line);
                    if (p is not null) onProgress?.Invoke(p);
                    return;
                }

                // ¿Está ffmpeg post-procesando?
                if (PostProcessMarkers.Any(line.Contains))
                {
                    onPostProcess?.Invoke(line);
                    return;
                }

                onLog?.Invoke(line);
            },
            ct);

        return code == 0;
    }

    // ─── Descarga de playlist ─────────────────────────────────────────────────
    public static async Task<(int ok, int fail)> DownloadPlaylistAsync(
        string url,
        string outputDir,
        string formatArg,
        Action<string>? onProgress,
        Action<string>? onLog,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);
        var outputTemplate = Path.Combine(outputDir, "%(playlist_index)s - %(title).60s.%(ext)s");

        var args = new StringBuilder()
            .Append($"--ffmpeg-location \"{AppContext.BaseDirectory}\" ")
            .Append($"--format \"{formatArg}\" ")
            .Append("--merge-output-format mp4 ")
            .Append("--embed-metadata ")
            .Append("--newline ")
            .Append("--yes-playlist ")
            .Append($"-o \"{outputTemplate}\" ")
            .Append($"\"{url}\"")
            .ToString();

        int ok = 0, fail = 0;

        await RunProcessAsync(YtDlpName, args, line =>
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (line.Contains("[download] Finished downloading playlist")) return;

            if (line.Contains("ERROR:"))          fail++;
            if (line.Contains("[download] 100%")) ok++;

            if (line.Contains("[download]") && line.Contains('%'))
                onProgress?.Invoke(ParseProgressString(line));
            else
                onLog?.Invoke(line);
        }, ct);

        return (ok, fail);
    }

    // ─── Actualizar yt-dlp ───────────────────────────────────────────────────
    public static async Task<string> UpdateYtDlpAsync()
    {
        var (output, err, code) = await RunProcessAsync(
            YtDlpName, "-U", null, CancellationToken.None);
        return code == 0 ? output : err;
    }

    // ─── Parse de progreso → DownloadProgress ────────────────────────────────
    private static DownloadProgress? ParseProgress(string line)
    {
        var match = Regex.Match(line,
            @"(\d+(?:\.\d+)?)%\s+of\s+([\d.]+\s*\S+)\s+at\s+([\S]+)\s+ETA\s+([\S]+)");

        if (match.Success &&
            float.TryParse(match.Groups[1].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out float pct))
        {
            return new DownloadProgress(
                pct,
                match.Groups[2].Value,
                match.Groups[3].Value,
                match.Groups[4].Value);
        }
        return null;
    }

    // ─── Parse de progreso → string (para playlist) ───────────────────────────
    private static string ParseProgressString(string line)
    {
        var match = Regex.Match(line,
            @"(\d+(?:\.\d+)?)%\s+of\s+([\d.]+\s*\w+)\s+at\s+([\S]+)\s+ETA\s+([\S]+)");

        return match.Success
            ? $"[{match.Groups[1].Value,5}%]  {match.Groups[2].Value}  @ {match.Groups[3].Value}  ETA {match.Groups[4].Value}"
            : line.Replace("[download]", "").Trim();
    }

    // ─── Helper: correr proceso ───────────────────────────────────────────────
    private static async Task<(string stdout, string stderr, int exitCode)> RunProcessAsync(
        string executable,
        string arguments,
        Action<string>? onLineOutput,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stdoutBuilder.AppendLine(e.Data);
            onLineOutput?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            stderrBuilder.AppendLine(e.Data);
            onLineOutput?.Invoke(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return ("", "Cancelado por el usuario.", -1);
        }

        return (stdoutBuilder.ToString().Trim(), stderrBuilder.ToString().Trim(), process.ExitCode);
    }

    // ─── Construir argumento de formato ──────────────────────────────────────
    public static string BuildFormatArg(QualityPreset preset, string? customFormat = null) =>
        preset switch
        {
            QualityPreset.Best      => "bestvideo+bestaudio/best",
            QualityPreset.HD1080    => "bestvideo[height<=1080]+bestaudio/best[height<=1080]",
            QualityPreset.HD720     => "bestvideo[height<=720]+bestaudio/best[height<=720]",
            QualityPreset.SD480     => "bestvideo[height<=480]+bestaudio/best[height<=480]",
            QualityPreset.AudioOnly => "bestaudio/best",
            QualityPreset.Custom    => customFormat ?? "bestvideo+bestaudio/best",
            _                       => "bestvideo+bestaudio/best"
        };
}