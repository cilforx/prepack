namespace PrePack;

internal static class AppPaths
{
    /// <summary>%APPDATA%\PrePack — config ของเครื่องนี้, WebView2 cache, log ที่ค้างส่ง</summary>
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrePack");

    public static string ConfigFile => Path.Combine(DataDir, "config.json");
    public static string PendingLogFile => Path.Combine(DataDir, "pending-logs.jsonl");
}
