namespace PrePack;

internal static class AppPaths
{
    /// <summary>
    /// %APPDATA%\PrePack — config, local database, WebView2 cache.
    /// PREPACK_DATA_DIR overrides it (used by the self-test so it never touches the real data).
    /// </summary>
    public static string DataDir { get; } =
        Environment.GetEnvironmentVariable("PREPACK_DATA_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrePack");

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string LocalDbFile => Path.Combine(DataDir, "prepack-local.db");
}
