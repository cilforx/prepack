using System.Text;
using System.Text.Json;
using PrePack.Config;

namespace PrePack.Data;

/// <summary>
/// Print logs that could not reach MySQL (server down, network). Printing never waits on the database;
/// these are re-sent on the next successful connection. InsertLogAsync ignores duplicates by ClientUid.
/// </summary>
internal static class PendingLogs
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static int Count
    {
        get
        {
            try { return File.Exists(AppPaths.PendingLogFile) ? File.ReadAllLines(AppPaths.PendingLogFile).Count(l => l.Length > 0) : 0; }
            catch (IOException) { return 0; }
        }
    }

    public static async Task AppendAsync(PrintLog log)
    {
        await Gate.WaitAsync();
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            await File.AppendAllTextAsync(AppPaths.PendingLogFile,
                JsonSerializer.Serialize(log, ConfigStore.Json).ReplaceLineEndings("") + "\n", Encoding.UTF8);
        }
        finally { Gate.Release(); }
    }

    /// <summary>Sends queued logs; keeps the ones that still fail. Returns how many are left.</summary>
    public static async Task<int> FlushAsync(PrePackDb db)
    {
        await Gate.WaitAsync();
        try
        {
            if (!File.Exists(AppPaths.PendingLogFile)) return 0;
            var remaining = new List<string>();
            var failed = false;
            foreach (var line in await File.ReadAllLinesAsync(AppPaths.PendingLogFile, Encoding.UTF8))
            {
                if (line.Length == 0) continue;
                if (failed)
                {
                    remaining.Add(line);
                    continue;
                }
                PrintLog? log;
                try { log = JsonSerializer.Deserialize<PrintLog>(line, ConfigStore.Json); }
                catch (JsonException) { continue; } // unreadable line: drop it rather than block the queue forever
                if (log == null) continue;
                try { await db.InsertLogAsync(log); }
                catch (Exception)
                {
                    failed = true; // server still unreachable: stop trying for now
                    remaining.Add(line);
                }
            }

            if (remaining.Count == 0) File.Delete(AppPaths.PendingLogFile);
            else await File.WriteAllLinesAsync(AppPaths.PendingLogFile, remaining, Encoding.UTF8);
            return remaining.Count;
        }
        finally { Gate.Release(); }
    }
}
