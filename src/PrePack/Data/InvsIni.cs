using System.Text;
using PrePack.Config;

namespace PrePack.Data;

/// <summary>
/// Finds and parses invs.ini (ported from BoxBox WebBridge.ReadInvsIni/ParseInvsIni).
/// Values in the [Pharms] section are Base64 encoded.
/// </summary>
internal static class InvsIni
{
    private static readonly string[] SubFolders =
    [
        "INVS", "Pharms", @"Program Files\INVS", @"Program Files\Pharms",
        @"Program Files (x86)\INVS", @"Program Files (x86)\Pharms", "ProgramData",
    ];

    public static string? Find()
    {
        var dirs = new List<string>
        {
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            @"C:\Windows",
            @"C:\Windows\System32",
        };
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        {
            dirs.Add(drive.RootDirectory.FullName);
            dirs.AddRange(SubFolders.Select(s => Path.Combine(drive.RootDirectory.FullName, s)));
        }

        foreach (var dir in dirs)
        {
            try
            {
                var candidate = Path.Combine(dir, "invs.ini");
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception) { /* inaccessible folder */ }
        }
        return null;
    }

    /// <summary>Reads [Pharms] from the file into settings. Throws with a Thai message on a bad file.</summary>
    public static void ApplyTo(InvsSettings s, string path)
    {
        var pharms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }
            var idx = line.IndexOf('=');
            if (idx > 0 && section.Equals("Pharms", StringComparison.OrdinalIgnoreCase))
                pharms[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        if (pharms.Count == 0)
            throw new InvalidDataException("ไม่พบ [Pharms] section ใน invs.ini");

        string Dec(string key)
        {
            if (!pharms.TryGetValue(key, out var v) || v.Length == 0) return "";
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(v)); }
            catch (FormatException) { return v; } // not Base64: use the raw value
        }

        s.Host = Dec("ServerName");
        s.Port = int.TryParse(Dec("Port"), out var p) ? p : 1433;
        s.Database = Dec("Database");
        s.User = Dec("User");
        s.PasswordEnc = ConfigStore.Protect(Dec("Password"));
        s.IniPath = path;
    }
}
