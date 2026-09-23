using System.Text.Json.Serialization;
using PrePack.Domain;

namespace PrePack.Config;

/// <summary>Per-machine settings in %APPDATA%\PrePack\config.json (seeded from the embedded seed-config.json).</summary>
public sealed class AppConfig
{
    public LabelLayout Layout { get; set; } = new();
    public List<DrugTypeDef> DrugTypes { get; set; } = [];
    public Dictionary<string, string> UnitMap { get; set; } = [];
    public Dictionary<string, List<string>> NameKeywords { get; set; } = [];

    public string PrinterName { get; set; } = "";

    /// <summary>Screen zoom for this machine, percent (WebView2 ZoomFactor × 100). Does not affect printing.</summary>
    public int UiZoomPercent { get; set; } = 100;
    public string LastStaffUid { get; set; } = "";

    public MySqlSettings MySql { get; set; } = new();
    public InvsSettings Invs { get; set; } = new();

    public DrugTypeDef? FindType(string? key) => DrugTypes.FirstOrDefault(t => t.Key == key);
}

public sealed class MySqlSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3306;
    public string User { get; set; } = "";
    public string Database { get; set; } = "prepack";

    /// <summary>DPAPI (CurrentUser) encrypted, Base64. Never sent to the page.</summary>
    public string PasswordEnc { get; set; } = "";

    [JsonIgnore] public bool IsConfigured => Host.Length > 0 && User.Length > 0;
}

public sealed class InvsSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 1433;
    public string Database { get; set; } = "";
    public string User { get; set; } = "";
    public string PasswordEnc { get; set; } = "";

    /// <summary>invs.ini path the settings came from (display only).</summary>
    public string IniPath { get; set; } = "";

    /// <summary>DRUG_GN column holding the dosage unit; validated against INFORMATION_SCHEMA before use.</summary>
    public string UnitColumn { get; set; } = "";

    [JsonIgnore] public bool IsConfigured => Host.Length > 0 && Database.Length > 0;
}
