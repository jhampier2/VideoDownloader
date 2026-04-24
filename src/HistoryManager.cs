using System.Text.Json;

namespace VideoDownloader;

public static class HistoryManager
{
    private static readonly string HistoryFile = Path.Combine(
        AppContext.BaseDirectory, "download_history.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public static List<DownloadRecord> Load()
    {
        try
        {
            if (!File.Exists(HistoryFile)) return new();
            var json = File.ReadAllText(HistoryFile);
            return JsonSerializer.Deserialize<List<DownloadRecord>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static void Save(List<DownloadRecord> records)
    {
        try
        {
            var json = JsonSerializer.Serialize(records, JsonOpts);
            File.WriteAllText(HistoryFile, json);
        }
        catch { /* No crítico */ }
    }

    public static void Add(DownloadRecord record)
    {
        var records = Load();
        records.Insert(0, record);

        // Guardar solo los últimos 100
        if (records.Count > 100) records = records.Take(100).ToList();
        Save(records);
    }

    public static void Clear()
    {
        if (File.Exists(HistoryFile)) File.Delete(HistoryFile);
    }
}
