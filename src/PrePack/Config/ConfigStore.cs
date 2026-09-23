using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PrePack.Data;

namespace PrePack.Config;

/// <summary>Loads and saves AppConfig. First run copies the embedded seed and picks up env / invs.ini.</summary>
internal static class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static AppConfig Load()
    {
        var seed = Seed();
        if (!File.Exists(AppPaths.ConfigFile))
        {
            FirstRun(seed);
            Save(seed);
            return seed;
        }

        AppConfig cfg;
        try
        {
            cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8), Json) ?? seed;
        }
        catch (JsonException)
        {
            // Corrupt file: keep a copy for inspection and start from the seed.
            File.Copy(AppPaths.ConfigFile, AppPaths.ConfigFile + ".bad", overwrite: true);
            FirstRun(seed);
            Save(seed);
            return seed;
        }

        // Fill sections added in newer versions from the seed.
        if (cfg.DrugTypes.Count == 0) cfg.DrugTypes = seed.DrugTypes;
        if (cfg.UnitMap.Count == 0) cfg.UnitMap = seed.UnitMap;
        if (cfg.NameKeywords.Count == 0) cfg.NameKeywords = seed.NameKeywords;
        return cfg;
    }

    public static void Save(AppConfig cfg)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        var tmp = AppPaths.ConfigFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, Json), Encoding.UTF8);
        File.Move(tmp, AppPaths.ConfigFile, overwrite: true);
    }

    public static AppConfig Seed() =>
        JsonSerializer.Deserialize<AppConfig>(WebAssets.ReadText("seed-config.json"), Json)
        ?? throw new InvalidOperationException("seed-config.json is invalid");

    /// <summary>Initial staff names from seed-config.json "staff" (kept out of AppConfig/config.json).</summary>
    public static List<string> SeedStaffNames() =>
        (System.Text.Json.Nodes.JsonNode.Parse(WebAssets.ReadText("seed-config.json"))?["staff"]?.AsArray() ?? [])
            .Select(n => n?.GetValue<string>().Trim() ?? "")
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();

    private static void FirstRun(AppConfig cfg)
    {
        string? Env(string k) => Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : null;
        cfg.MySql.Host = Env("PREPACK_DB_HOST") ?? cfg.MySql.Host;
        if (int.TryParse(Env("PREPACK_DB_PORT"), out var port)) cfg.MySql.Port = port;
        cfg.MySql.User = Env("PREPACK_DB_USER") ?? cfg.MySql.User;
        cfg.MySql.Database = Env("PREPACK_DB_NAME") ?? cfg.MySql.Database;
        if (Env("PREPACK_DB_PASS") is { } pass) cfg.MySql.PasswordEnc = Protect(pass);

        var ini = InvsIni.Find();
        if (ini != null)
        {
            try { InvsIni.ApplyTo(cfg.Invs, ini); }
            catch (Exception) { /* bad ini: user can browse for another one in settings */ }
        }
    }

    public static string Protect(string plain) =>
        plain.Length == 0 ? "" : Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser));

    public static string Unprotect(string enc)
    {
        if (enc.Length == 0) return "";
        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return ""; // encrypted by another Windows user: ask for the password again
        }
    }
}
