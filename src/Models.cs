using System.Text.Json.Serialization;

namespace VideoDownloader;

public class VideoInfo
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "Sin título";

    [JsonPropertyName("uploader")]
    public string Uploader { get; set; } = "Desconocido";

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }

    [JsonPropertyName("view_count")]
    public long? ViewCount { get; set; }

    [JsonPropertyName("like_count")]
    public long? LikeCount { get; set; }

    [JsonPropertyName("upload_date")]
    public string? UploadDate { get; set; }   // formato yyyyMMdd de yt-dlp

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("formats")]
    public List<VideoFormat>? Formats { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("webpage_url")]
    public string? Url { get; set; }

    // ─── Propiedades calculadas ────────────────────────────────────────────────

    public string DurationFormatted => Duration.HasValue
        ? TimeSpan.FromSeconds(Duration.Value).ToString(@"hh\:mm\:ss").TrimStart('0', ':')
        : "Desconocida";

    public string ViewsFormatted => FormatCount(ViewCount);

    public string LikesFormatted => LikeCount.HasValue ? FormatCount(LikeCount) : "N/A";

    /// <summary>Convierte "20240315" → "15/03/2024".</summary>
    public string UploadDateFormatted
    {
        get
        {
            if (UploadDate is null || UploadDate.Length != 8) return "Desconocida";
            if (DateTime.TryParseExact(UploadDate, "yyyyMMdd",
                    null, System.Globalization.DateTimeStyles.None, out var d))
                return d.ToString("dd/MM/yyyy");
            return UploadDate;
        }
    }

    /// <summary>Primeras 120 caracteres de la descripción, sin saltos de línea.</summary>
    public string DescriptionSnippet
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Description)) return "";
            var flat = Description.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return flat.Length > 120 ? flat[..117] + "…" : flat;
        }
    }

    /// <summary>Resolución máxima de video disponible entre los formatos.</summary>
    public string MaxResolution
    {
        get
        {
            if (Formats is null) return "N/A";
            var best = Formats
                .Where(f => f.HasVideo && f.Height.HasValue)
                .OrderByDescending(f => f.Height)
                .FirstOrDefault();
            return best?.Resolution ?? "N/A";
        }
    }

    private static string FormatCount(long? n) => n.HasValue
        ? n.Value >= 1_000_000 ? $"{n.Value / 1_000_000.0:F1}M"
        : n.Value >= 1_000     ? $"{n.Value / 1_000.0:F1}K"
        : n.Value.ToString()
        : "N/A";
}

public class VideoFormat
{
    [JsonPropertyName("format_id")]
    public string FormatId { get; set; } = "";

    [JsonPropertyName("ext")]
    public string Ext { get; set; } = "";

    [JsonPropertyName("resolution")]
    public string? Resolution { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("filesize")]
    public long? Filesize { get; set; }

    [JsonPropertyName("filesize_approx")]
    public long? FilesizeApprox { get; set; }

    [JsonPropertyName("fps")]
    public double? Fps { get; set; }

    [JsonPropertyName("vcodec")]
    public string? Vcodec { get; set; }

    [JsonPropertyName("acodec")]
    public string? Acodec { get; set; }

    [JsonPropertyName("format_note")]
    public string? Note { get; set; }

    [JsonPropertyName("tbr")]
    public double? Tbr { get; set; }    // total bitrate kbps

    public bool HasVideo => Vcodec != null && Vcodec != "none";
    public bool HasAudio => Acodec != null && Acodec != "none";

    /// <summary>Tamaño real o aproximado, el que esté disponible.</summary>
    public long? BestFilesize => Filesize ?? FilesizeApprox;

    public string FilesizeFormatted
    {
        get
        {
            var size = BestFilesize;
            if (!size.HasValue) return "~desconocido";
            var approx = Filesize is null ? "~" : "";
            return size.Value >= 1_073_741_824 ? $"{approx}{size.Value / 1_073_741_824.0:F2} GB"
                 : size.Value >= 1_048_576     ? $"{approx}{size.Value / 1_048_576.0:F1} MB"
                 : $"{approx}{size.Value / 1024.0:F0} KB";
        }
    }

    /// <summary>Codec de video abreviado para mostrar en tabla.</summary>
    public string VcodecShort => Vcodec switch
    {
        null or "none"                      => "—",
        var v when v.StartsWith("avc")      => "H.264",
        var v when v.StartsWith("hvc")
                || v.StartsWith("hevc")     => "H.265",
        var v when v.StartsWith("vp9")      => "VP9",
        var v when v.StartsWith("av01")     => "AV1",
        var v                               => v[..Math.Min(v.Length, 8)]
    };

    /// <summary>Codec de audio abreviado.</summary>
    public string AcodecShort => Acodec switch
    {
        null or "none"                      => "—",
        var a when a.StartsWith("mp4a")     => "AAC",
        var a when a.StartsWith("opus")     => "Opus",
        var a when a.StartsWith("mp3")      => "MP3",
        var a when a.StartsWith("vorbis")   => "Vorbis",
        var a                               => a[..Math.Min(a.Length, 6)]
    };
}

public class DownloadRecord
{
    public string   Title      { get; set; } = "";
    public string   Url        { get; set; } = "";
    public string   Format     { get; set; } = "";
    public string   OutputPath { get; set; } = "";
    public DateTime Date       { get; set; } = DateTime.Now;
    public bool     Success    { get; set; }
}

/// <summary>Progreso de descarga parseado desde la salida de yt-dlp.</summary>
public record DownloadProgress(
    float  Percent,
    string Size,
    string Speed,
    string Eta
);

public enum QualityPreset
{
    Best,
    HD1080,
    HD720,
    SD480,
    AudioOnly,
    Custom
}

public enum UrlType
{
    Video,      // URL de video individual
    Playlist,   // URL de playlist o canal
    Unknown     // No se pudo determinar (error de red, URL inválida, etc.)
}