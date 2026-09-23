using System.Drawing.Printing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using PrePack.Config;
using PrePack.Data;
using PrePack.Domain;

namespace PrePack.Bridge;

/// <summary>
/// JS → C# bridge (window.chrome.webview.hostObjects.bridge). COM rules: only string/int/bool parameters,
/// every method returns a JSON string {ok, data} or {ok:false, error}. Passwords never leave C#.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public class WebBridge
{
    private const int MaxPages = 200;
    private const int MaxLotLength = 40;
    private const int MaxDrugNameLength = 200;

    private const int ReportLogLimit = 500;
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(2);

    private readonly Form _owner;
    private readonly CoreWebView2 _wv;
    private AppConfig _cfg;
    private readonly LocalDb _local;
    private readonly SyncService _sync;
    private readonly System.Threading.Timer _syncTimer;

    /// <summary>Self-test only: when set, Print writes this PDF instead of sending to the printer.</summary>
    internal string? PdfSinkPath { get; set; }

    internal const int MinZoomPercent = 50;
    internal const int MaxZoomPercent = 150;

    /// <summary>Applies a zoom to the WebView2 control (set by MainForm).</summary>
    internal Action<double>? ApplyZoom { get; set; }

    internal int UiZoomPercent => Math.Clamp(_cfg.UiZoomPercent, MinZoomPercent, MaxZoomPercent);

    /// <summary>Called by MainForm when the zoom changes (settings buttons or Ctrl + mouse wheel) so it is remembered.</summary>
    internal void RememberZoom(double factor)
    {
        var percent = Math.Clamp((int)Math.Round(factor * 100), MinZoomPercent, MaxZoomPercent);
        if (percent == _cfg.UiZoomPercent) return;
        _cfg.UiZoomPercent = percent;
        ConfigStore.Save(_cfg);
    }

    public WebBridge(Form owner, CoreWebView2 wv)
    {
        _owner = owner;
        _wv = wv;
        _cfg = ConfigStore.Load();
        _local = new LocalDb(AppPaths.LocalDbFile);
        _local.SeedStaff(ConfigStore.SeedStaffNames());
        _sync = new SyncService(_local, () => _cfg.MySql);
        // Background sync; failures back off inside SyncService, so this never slows the UI.
        _syncTimer = new System.Threading.Timer(_ => SyncInBackground(), null, TimeSpan.FromSeconds(3), SyncInterval);
    }

    private void SyncInBackground() => _ = Task.Run(() => _sync.RunAsync());

    // ── Startup ──

    /// <summary>
    /// Server first: when MySQL is set up and answering, sync before loading so staff / factors are the shared ones.
    /// Otherwise (not set up, or failed in the last minute) start straight from the local database.
    /// </summary>
    public async Task<string> Init() => await Run(async () =>
    {
        var online = await _sync.TryNowAsync();
        return new
        {
            today = Today(),
            version = Version(),
            machine = Environment.MachineName,
            config = SafeConfig(),
            staff = _local.ListStaff(),
            workFactors = _local.GetWorkFactors().Table,
            online,
            sync = _sync.Status,
        };
    });

    // ── Sync / backup ──

    public string SyncStatus() => RunSync(() => _sync.Status);

    /// <summary>Local copy as it is now (no network) — the page refreshes lists after a background sync.</summary>
    public string Snapshot() => RunSync(() => new
    {
        staff = _local.ListStaff(), workFactors = _local.GetWorkFactors().Table, sync = _sync.Status,
    });

    /// <summary>"Sync ตอนนี้": runs a sync right away (ignoring backoff) and returns fresh local data.</summary>
    public async Task<string> SyncNow() => await Run(async () =>
    {
        if (!_cfg.MySql.IsConfigured) throw new UserError("ยังไม่ได้ตั้งค่า MySQL — ข้อมูลเก็บในเครื่องนี้อยู่แล้ว");
        await _sync.RunAsync(force: true, wait: true);
        var status = _sync.Status;
        if (status.LastError != null) throw new UserError(status.LastError);
        return new { sync = status, staff = _local.ListStaff(), workFactors = _local.GetWorkFactors().Table };
    });

    /// <summary>Restore: re-upload every local row (e.g. after the MySQL server was rebuilt).</summary>
    public async Task<string> ResyncAll() => await Run(async () =>
    {
        var queued = _local.ResetSynced();
        _sync.Reset();
        await _sync.RunAsync(force: true, wait: true);
        return new { queued, sync = _sync.Status };
    });

    // ── Settings: MySQL / INVS ──

    /// <summary>Saves MySQL settings, runs the schema function and a first sync. Empty password keeps the saved one.</summary>
    public async Task<string> SaveMySql(string json) => await Run(async () =>
    {
        var j = Parse(json);
        var m = new MySqlSettings
        {
            Host = Str(j, "host"),
            Port = Int(j, "port", 3306),
            User = Str(j, "user"),
            Database = Str(j, "database") is { Length: > 0 } d ? d : "prepack",
            PasswordEnc = Str(j, "password") is { Length: > 0 } p ? ConfigStore.Protect(p) : _cfg.MySql.PasswordEnc,
        };
        if (!m.IsConfigured) throw new UserError("กรุณากรอก host และ user");
        if (!Schema.IsValidDatabaseName(m.Database))
            throw new UserError("ชื่อฐานข้อมูลใช้ได้เฉพาะ A-Z, 0-9 และ _ (ห้ามขึ้นต้นด้วยตัวเลข)");

        // Test + create schema before saving, so a wrong server never replaces a working one.
        var applied = await Schema.EnsureAsync(m.Host, m.Port, m.User, ConfigStore.Unprotect(m.PasswordEnc), m.Database);
        _cfg.MySql = m;
        ConfigStore.Save(_cfg);
        _sync.Reset();
        await _sync.RunAsync(force: true, wait: true);
        var status = _sync.Status;
        if (status.LastError != null) throw new UserError("สร้างตารางแล้ว แต่ sync ไม่สำเร็จ: " + status.LastError);
        return new
        {
            applied, config = SafeConfig(), sync = status,
            staff = _local.ListStaff(), workFactors = _local.GetWorkFactors().Table,
        };
    });

    public async Task<string> SaveInvs(string json) => await Run(async () =>
    {
        var j = Parse(json);
        var s = new InvsSettings
        {
            Host = Str(j, "host"),
            Port = Int(j, "port", 1433),
            Database = Str(j, "database"),
            User = Str(j, "user"),
            PasswordEnc = Str(j, "password") is { Length: > 0 } p ? ConfigStore.Protect(p) : _cfg.Invs.PasswordEnc,
            IniPath = _cfg.Invs.IniPath,
            UnitColumn = Str(j, "unitColumn"),
        };
        if (!s.IsConfigured) throw new UserError("กรุณากรอก server และ database ของ INVS");

        var client = Invs(s);
        await client.TestAsync();
        var columns = await client.UnitColumnCandidatesAsync();
        if (s.UnitColumn.Length > 0 && !columns.Contains(s.UnitColumn, StringComparer.OrdinalIgnoreCase))
            throw new UserError($"ไม่พบคอลัมน์ {s.UnitColumn} ใน DRUG_GN");
        if (s.UnitColumn.Length == 0 && columns.Count > 0) s.UnitColumn = columns[0]; // auto-detect

        _cfg.Invs = s;
        ConfigStore.Save(_cfg);
        return new { config = SafeConfig(), unitColumns = columns };
    });

    /// <summary>Looks for invs.ini on every fixed drive (BoxBox logic) and fills the INVS form.</summary>
    public string FindInvsIni() => RunSync(() =>
    {
        var path = InvsIni.Find() ?? throw new UserError("ไม่พบ invs.ini ในทุก drive — วางไว้ที่ C:\\INVS\\invs.ini หรือกดเลือกไฟล์เอง");
        return ApplyIni(path);
    });

    public string BrowseInvsIni() => RunSync(() =>
    {
        string? chosen = null;
        _owner.Invoke(() =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "เลือกไฟล์ invs.ini",
                Filter = "INI files (*.ini)|*.ini|All files (*.*)|*.*",
                FileName = "invs.ini",
            };
            if (dlg.ShowDialog(_owner) == DialogResult.OK) chosen = dlg.FileName;
        });
        return chosen == null ? new { cancelled = true } : ApplyIni(chosen);
    });

    private object ApplyIni(string path)
    {
        var s = new InvsSettings { UnitColumn = _cfg.Invs.UnitColumn };
        InvsIni.ApplyTo(s, path);
        _cfg.Invs = s;
        ConfigStore.Save(_cfg);
        return new { config = SafeConfig() };
    }

    public async Task<string> InvsUnitColumns() => await Run(async () => await Invs(_cfg.Invs).UnitColumnCandidatesAsync());

    // ── Settings: screen zoom (this machine only, like BoxBox "ขนาดแสดงผล") ──

    public string GetUiZoom() => RunSync(() => UiZoomPercent);

    public string SetUiZoom(int percent) => RunSync(() =>
    {
        if (percent is < MinZoomPercent or > MaxZoomPercent)
            throw new UserError($"ขนาดแสดงผลต้องอยู่ระหว่าง {MinZoomPercent}-{MaxZoomPercent}%");
        _owner.Invoke(() => ApplyZoom?.Invoke(percent / 100.0)); // ZoomFactorChanged → RememberZoom saves it
        RememberZoom(percent / 100.0);
        return percent;
    });

    // ── Settings: label, printer, drug types ──

    public string GetPrinters() => RunSync(() =>
    {
        var list = new List<string>();
        foreach (string name in PrinterSettings.InstalledPrinters) list.Add(name);
        return list;
    });

    public string SaveLabelSettings(string json) => RunSync(() =>
    {
        var j = Parse(json);
        var layout = j["layout"].Deserialize<LabelLayout>(ConfigStore.Json) ?? throw new UserError("ข้อมูลไม่ถูกต้อง");
        if (layout.Validate() is { } err) throw new UserError(err);
        _cfg.Layout = layout;
        _cfg.PrinterName = Str(j, "printerName");
        ConfigStore.Save(_cfg);
        return SafeConfig();
    });

    public string SaveDrugTypes(string json) => RunSync(() =>
    {
        var j = Parse(json);
        var types = j["drugTypes"].Deserialize<List<DrugTypeDef>>(ConfigStore.Json) ?? [];
        var unitMap = j["unitMap"].Deserialize<Dictionary<string, string>>(ConfigStore.Json) ?? [];
        var keywords = j["nameKeywords"].Deserialize<Dictionary<string, List<string>>>(ConfigStore.Json) ?? [];

        var seedKeys = ConfigStore.Seed().DrugTypes.Select(t => t.Key).ToHashSet();
        if (types.Count != seedKeys.Count || types.Any(t => !seedKeys.Contains(t.Key)))
            throw new UserError("ประเภทยาไม่ครบ");
        foreach (var t in types)
        {
            if (t.ShelfDays is < 1 or > 3650) throw new UserError($"อายุยา {t.Label} ต้องอยู่ระหว่าง 1-3650 วัน");
            if (t.Unit.Trim().Length == 0) throw new UserError($"กรุณากรอกหน่วยของ {t.Label}");
            if (t.Unit.Trim().Length > 20) throw new UserError($"หน่วยของ {t.Label} ยาวเกิน 20 ตัวอักษร"); // print_logs.unit
            t.Unit = t.Unit.Trim();
            t.QtyPresets = t.QtyPresets.Where(q => q > 0).Distinct().Take(8).ToList();
        }
        if (unitMap.Values.Any(v => !seedKeys.Contains(v))) throw new UserError("ตารางหน่วยมีประเภทที่ไม่รู้จัก");

        _cfg.DrugTypes = types;
        _cfg.UnitMap = unitMap.Where(kv => kv.Key.Trim().Length > 0)
            .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        _cfg.NameKeywords = keywords.Where(kv => seedKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Select(w => w.Trim()).Where(w => w.Length > 0).Distinct().ToList());
        ConfigStore.Save(_cfg);
        return SafeConfig();
    });

    public string ResetDrugTypes() => RunSync(() =>
    {
        var seed = ConfigStore.Seed();
        _cfg.DrugTypes = seed.DrugTypes;
        _cfg.UnitMap = seed.UnitMap;
        _cfg.NameKeywords = seed.NameKeywords;
        ConfigStore.Save(_cfg);
        return SafeConfig();
    });

    // ── Staff ──

    public async Task<string> ListStaff() => await Run(async () =>
    {
        await _sync.TryNowAsync();
        return _local.ListStaff();
    });

    /// <summary>Written to the local database, then straight to the server if it answers (else on a later sync).</summary>
    public async Task<string> SaveStaff(string json) => await Run(async () =>
    {
        var j = Parse(json);
        _local.SaveStaff(new Staff(Str(j, "uid"), Str(j, "name"), Str(j, "shortName"), Bool(j, "active", true)));
        var online = await _sync.TryNowAsync();
        return new { staff = _local.ListStaff(), online, sync = _sync.Status };
    });

    // ── Workload factors (password protected) ──

    public async Task<string> GetWorkFactors() => await Run(async () =>
    {
        await _sync.TryNowAsync();
        return _local.GetWorkFactors().Table;
    });

    public string CheckAdminPassword(string password) => RunSync(() =>
        WorkFactors.CheckPassword(password) ? true : throw new UserError("รหัสไม่ถูกต้อง"));

    /// <summary>Replaces the factor table. The password is checked here too, not only in the page.</summary>
    public async Task<string> SaveWorkFactors(string json, string password) => await Run(async () =>
    {
        if (!WorkFactors.CheckPassword(password)) throw new UserError("รหัสไม่ถูกต้อง");
        var table = (JsonNode.Parse(json) as JsonArray ?? throw new UserError("ข้อมูลไม่ถูกต้อง"))
            .Select(n => n ?? throw new UserError("ข้อมูลไม่ถูกต้อง"))
            .Select(n => new WorkFactor(Str(n, "drugType"),
                n["maxQty"] is JsonValue v && v.TryGetValue<decimal>(out var m) ? m : null,
                Dec(n, "factor")))
            .ToList();
        var types = _cfg.DrugTypes.Select(t => t.Key).ToList();
        if (table.Any(f => !types.Contains(f.DrugType))) throw new UserError("ประเภทยาไม่ถูกต้อง");
        if (WorkFactors.Validate(table, types) is { } err) throw new UserError(err);

        // New version stamp → this table wins the next sync (last writer wins across machines).
        _local.SaveWorkFactors(table, LocalDb.NowUtcSeconds());
        await _sync.TryNowAsync();
        return _local.GetWorkFactors().Table;
    });

    public string SetLastStaff(string staffUid) => RunSync(() =>
    {
        _cfg.LastStaffUid = staffUid;
        ConfigStore.Save(_cfg);
        return true;
    });

    // ── Drug lookup ──

    public async Task<string> SearchDrugs(string keyword) => await Run(async () =>
    {
        if (!_cfg.Invs.IsConfigured) throw new UserError("ยังไม่ได้ตั้งค่า INVS (⚙ → ฐานข้อมูล) — กรอกยาแบบ Manual ได้");
        var classifier = Classifier();
        var drugs = await Invs(_cfg.Invs).SearchDrugsAsync(keyword);
        return drugs.Select(d => new
        {
            d.WorkingCode,
            d.DrugName,
            d.DrugNameTh,
            invsUnit = d.Unit,
            drugType = classifier.Classify(d.DrugName, d.Unit),
        });
    });

    public async Task<string> GetLots(string workingCode) => await Run(async () =>
        await Invs(_cfg.Invs).LotsAsync(workingCode, DateOnly.FromDateTime(DateTime.Today)));

    // ── Short sticker names for long drug names ──

    /// <summary>Saved sticker name for a drug ("" = use the full name). Server first, local fallback.</summary>
    public async Task<string> GetDrugLabel(string json) => await Run(async () =>
    {
        var j = Parse(json);
        var key = DrugLabels.Key(Str(j, "source"), Str(j, "workingCode"), Str(j, "drugName"));
        var label = await _sync.GetDrugLabelAsync(key);
        return new { labelName = label?.LabelName ?? "" };
    });

    /// <summary>Remember the sticker name for this drug on this machine and the server (used by every machine).</summary>
    public async Task<string> SaveDrugLabel(string json) => await Run(async () =>
    {
        var j = Parse(json);
        var drugName = Str(j, "drugName");
        if (drugName.Length == 0) throw new UserError("กรุณาเลือกยา");
        string label;
        try { label = DrugLabels.Normalize(Str(j, "labelName"), drugName); }
        catch (ArgumentException e) { throw new UserError(e.Message); }

        var key = DrugLabels.Key(Str(j, "source"), Str(j, "workingCode"), drugName);
        if ((_local.GetDrugLabel(key)?.LabelName ?? "") != label)
            _local.SaveDrugLabel(new DrugLabel(key, drugName, label));
        var online = await _sync.TryNowAsync();
        return new { labelName = label, online, sync = _sync.Status };
    });

    /// <summary>Type guess for a manually typed drug name (name keywords only).</summary>
    public string ClassifyName(string drugName) => RunSync(() => Classifier().FromName(drugName) ?? "");

    public string FrequentQty(string workingCode, string drugName) => RunSync(() =>
        _local.FrequentQty(workingCode.Length > 0 ? workingCode : null, drugName));

    // ── Print ──

    /// <summary>
    /// Silent-prints the sheets the page has already rendered into #print-area, then records the print:
    /// local database first, then the server. The sticker is already out before any network call.
    /// </summary>
    public async Task<string> Print(string json) => await Run(async () =>
    {
        var j = Parse(json);
        var test = Bool(j, "test", false);
        var layout = _cfg.Layout;
        if (layout.Validate() is { } layoutErr) throw new UserError(layoutErr);
        if (_cfg.PrinterName.Length == 0 && PdfSinkPath == null)
            throw new UserError("ยังไม่ได้เลือกเครื่องพิมพ์ (⚙ → สติกเกอร์ / เครื่องพิมพ์)");

        var pages = Int(j, "pages", 0);
        var stickers = Int(j, "stickers", 0);
        if (pages is < 1 or > MaxPages) throw new UserError($"จำนวนหน้าต้องอยู่ระหว่าง 1-{MaxPages}");
        if (stickers < 1 || stickers > pages * layout.PerFrame || stickers <= (pages - 1) * layout.PerFrame)
            throw new UserError("จำนวนดวงไม่ตรงกับจำนวนหน้า");

        PrintLog? log = null;
        if (!test) log = BuildLog(j, pages, stickers);

        await PrintRenderedAsync(layout);

        if (log == null) return new { logged = false, online = false, points = 0m, sync = _sync.Status };
        // Local first (never lost), then straight to the server if it answers; otherwise it waits for a later sync.
        _local.InsertLog(log);
        var online = await _sync.TryNowAsync();
        return new { logged = true, online, points = log.WorkPoints, sync = _sync.Status };
    });

    private PrintLog BuildLog(JsonNode j, int pages, int stickers)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (Str(j, "packDate") != Today())
            throw new UserError("วันที่เปลี่ยนแล้ว (ข้ามเที่ยงคืน) — หน้าจอปรับวันบรรจุให้ใหม่แล้ว กดพิมพ์อีกครั้ง");

        var staffUid = Str(j, "staffUid");
        if (staffUid.Length == 0) throw new UserError("กรุณาเลือกผู้บรรจุ");
        var staff = _local.ListStaff().FirstOrDefault(s => s.Uid == staffUid && s.Active)
            ?? throw new UserError("ไม่พบผู้บรรจุ หรือถูกปิดใช้งาน");

        var source = Str(j, "source") == "MANUAL" ? "MANUAL" : "INVS";
        var workingCode = source == "INVS" ? Str(j, "workingCode") : null;
        if (source == "INVS" && string.IsNullOrEmpty(workingCode)) throw new UserError("ไม่พบรหัสยา INVS");
        var drugName = Str(j, "drugName");
        if (drugName.Length == 0) throw new UserError("กรุณาเลือกยา");
        if (drugName.Length > MaxDrugNameLength) throw new UserError("ชื่อยายาวเกินไป");

        var type = _cfg.FindType(Str(j, "drugType")) ?? throw new UserError("กรุณาเลือกประเภทยา");
        var qty = Dec(j, "qty");
        if (qty is <= 0 or > 100000) throw new UserError("กรุณากรอกจำนวนต่อซอง");

        var lot = Str(j, "lotNo").ToUpperInvariant();
        if (lot.Length == 0) throw new UserError("กรุณากรอก Lot");
        if (lot.Length > MaxLotLength) throw new UserError($"Lot ยาวเกิน {MaxLotLength} ตัวอักษร");

        var srcExp = Date(j, "srcExp");
        if (srcExp is { } s && s <= today) throw new UserError("ยาในภาชนะเดิมหมดอายุแล้ว ห้ามแบ่งบรรจุ");
        var labelExp = Date(j, "labelExp") ?? throw new UserError("กรุณากรอกวันหมดอายุบนฉลาก");
        if (labelExp <= today) throw new UserError("วันหมดอายุบนฉลากต้องอยู่หลังวันนี้");
        if (srcExp is { } cap && labelExp > cap) throw new UserError("วันหมดอายุบนฉลากต้องไม่เกิน EXP ของภาชนะเดิม");

        return new PrintLog
        {
            StaffUid = staff.Uid,
            StaffName = staff.Name,
            Source = source,
            WorkingCode = workingCode,
            DrugName = drugName,
            DrugType = type.Key,
            Unit = type.Unit,
            QtyPerPack = qty,
            LotNo = lot,
            PackDate = today,
            SrcExpDate = srcExp,
            LabelExpDate = labelExp,
            Pages = pages,
            Stickers = stickers,
            WorkFactor = WorkFactors.Lookup(_local.GetWorkFactors().Table, type.Key, qty),
            PrintedAt = DateTime.Now,
            MachineName = Environment.MachineName,
        };
    }

    /// <summary>Paper = one sticker frame, no margins, no browser header/footer, 100% scale.</summary>
    internal static CoreWebView2PrintSettings CreatePrintSettings(CoreWebView2 wv, LabelLayout layout, string printerName)
    {
        const double MmPerInch = 25.4;
        var settings = wv.Environment.CreatePrintSettings();
        settings.PrinterName = printerName;
        settings.PageWidth = layout.FrameWidth / MmPerInch;
        settings.PageHeight = layout.FrameHeight / MmPerInch;
        settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0;
        settings.ScaleFactor = 1;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.ShouldPrintBackgrounds = false;
        settings.ShouldPrintSelectionOnly = false;
        settings.Orientation = CoreWebView2PrintOrientation.Portrait;
        settings.Copies = 1;
        return settings;
    }

    private async Task PrintRenderedAsync(LabelLayout layout)
    {
        if (PdfSinkPath != null)
        {
            if (!await _wv.PrintToPdfAsync(PdfSinkPath, CreatePrintSettings(_wv, layout, "")))
                throw new UserError("PrintToPdf ไม่สำเร็จ");
            return;
        }
        var status = await _wv.PrintAsync(CreatePrintSettings(_wv, layout, _cfg.PrinterName));
        switch (status)
        {
            case CoreWebView2PrintStatus.Succeeded:
                return;
            case CoreWebView2PrintStatus.PrinterUnavailable:
                throw new UserError($"ไม่พบเครื่องพิมพ์ \"{_cfg.PrinterName}\" หรือเครื่องพิมพ์ไม่พร้อม");
            default:
                throw new UserError("พิมพ์ไม่สำเร็จ — ตรวจเครื่องพิมพ์แล้วลองอีกครั้ง");
        }
    }

    // ── Reports ──

    /// <summary>
    /// All machines from MySQL when it is reachable (after a sync so this machine's prints are included);
    /// otherwise this machine's local database, flagged so the page can say so.
    /// </summary>
    public async Task<string> Workload(string json) => await Run(async () =>
    {
        var (from, to, source) = Range(Parse(json));
        return await FromRemoteOrLocal(
            db => db.WorkloadAsync(from, to, source),
            () => _local.Workload(from, to, source));
    });

    public async Task<string> Logs(string json) => await Run(async () =>
    {
        var j = Parse(json);
        var (from, to, source) = Range(j);
        var staffUid = Str(j, "staffUid");
        var lot = Str(j, "lot");
        return await FromRemoteOrLocal(
            db => db.LogsAsync(from, to, source, staffUid, lot, ReportLogLimit),
            () => _local.Logs(from, to, source, staffUid, lot, ReportLogLimit));
    });

    private async Task<object> FromRemoteOrLocal<T>(Func<PrePackDb, Task<T>> remote, Func<T> local)
    {
        string? reason = null;
        if (_sync.CanReachNow && await _sync.RunAsync(wait: true))
        {
            try
            {
                var m = _cfg.MySql;
                var db = PrePackDb.Create(m.Host, m.Port, m.User, ConfigStore.Unprotect(m.PasswordEnc), m.Database);
                return new { rows = await remote(db), origin = "mysql", sync = _sync.Status };
            }
            catch (UserError e) { reason = e.Message; }
        }
        reason ??= _cfg.MySql.IsConfigured ? _sync.Status.LastError ?? "เชื่อมต่อ MySQL ไม่ได้" : "ยังไม่ได้ตั้งค่า MySQL";
        return new { rows = local(), origin = "local", reason, sync = _sync.Status };
    }

    /// <summary>Save-as dialog for a CSV export. The content gets a UTF-8 BOM so Excel shows Thai correctly.</summary>
    public string SaveCsv(string fileName, string content) => RunSync(() =>
    {
        string? path = null;
        _owner.Invoke(() =>
        {
            using var dlg = new SaveFileDialog
            {
                Title = "บันทึกไฟล์ CSV",
                Filter = "CSV (*.csv)|*.csv",
                FileName = Path.GetFileName(fileName),
            };
            if (dlg.ShowDialog(_owner) == DialogResult.OK) path = dlg.FileName;
        });
        if (path == null) return new { saved = false };
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return new { saved = true, path };
    });

    // ── Helpers ──

    private static InvsClient Invs(InvsSettings s)
    {
        if (!s.IsConfigured) throw new UserError("ยังไม่ได้ตั้งค่า INVS (⚙ → ฐานข้อมูล)");
        return new InvsClient(s.Host, s.Port, s.Database, s.User, ConfigStore.Unprotect(s.PasswordEnc), s.UnitColumn);
    }

    private DrugTypeClassifier Classifier() => new(_cfg.UnitMap, _cfg.NameKeywords);

    /// <summary>Config for the page — connection details without passwords.</summary>
    private object SafeConfig() => new
    {
        layout = _cfg.Layout,
        drugTypes = _cfg.DrugTypes,
        unitMap = _cfg.UnitMap,
        nameKeywords = _cfg.NameKeywords,
        printerName = _cfg.PrinterName,
        uiZoomPercent = UiZoomPercent,
        lastStaffUid = _cfg.LastStaffUid,
        mysql = new { _cfg.MySql.Host, _cfg.MySql.Port, _cfg.MySql.User, _cfg.MySql.Database, hasPassword = _cfg.MySql.PasswordEnc.Length > 0 },
        invs = new
        {
            _cfg.Invs.Host, _cfg.Invs.Port, _cfg.Invs.Database, _cfg.Invs.User, _cfg.Invs.IniPath, _cfg.Invs.UnitColumn,
            hasPassword = _cfg.Invs.PasswordEnc.Length > 0,
        },
    };

    private static string Today() => DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Version() =>
        typeof(WebBridge).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    private static (DateOnly From, DateOnly To, string Source) Range(JsonNode j)
    {
        var from = Date(j, "from") ?? throw new UserError("กรุณาเลือกวันที่เริ่ม");
        var to = Date(j, "to") ?? throw new UserError("กรุณาเลือกวันที่สิ้นสุด");
        if (to < from) throw new UserError("วันที่สิ้นสุดต้องไม่ก่อนวันที่เริ่ม");
        var source = Str(j, "source") is "INVS" or "MANUAL" ? Str(j, "source") : "";
        return (from, to, source);
    }

    private static JsonNode Parse(string json)
    {
        try { return JsonNode.Parse(json) ?? throw new UserError("ข้อมูลไม่ถูกต้อง"); }
        catch (JsonException) { throw new UserError("ข้อมูลไม่ถูกต้อง"); }
    }

    private static string Str(JsonNode j, string key) =>
        j[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

    private static int Int(JsonNode j, string key, int fallback) => j[key] is JsonValue v
        ? v.TryGetValue<int>(out var i) ? i
        : v.TryGetValue<double>(out var d) && d == Math.Floor(d) && Math.Abs(d) < int.MaxValue ? (int)d
        : v.TryGetValue<string>(out var s) && int.TryParse(s, out var p) ? p : fallback
        : fallback;

    private static decimal Dec(JsonNode j, string key) => j[key] is JsonValue v
        ? v.TryGetValue<decimal>(out var m) ? m
        : v.TryGetValue<string>(out var s) && decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : 0
        : 0;

    private static bool Bool(JsonNode j, string key, bool fallback) =>
        j[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    private static DateOnly? Date(JsonNode j, string key) =>
        DateOnly.TryParseExact(Str(j, key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string Ok(object? data) =>
        JsonSerializer.Serialize(new { ok = true, data }, ConfigStore.Json);

    private static string Fail(string error) =>
        JsonSerializer.Serialize(new { ok = false, error }, ConfigStore.Json);

    private static async Task<string> Run(Func<Task<object?>> action)
    {
        try { return Ok(await action()); }
        catch (UserError e) { return Fail(e.Message); }
        catch (Exception e) { return Fail("เกิดข้อผิดพลาด: " + e.Message); }
    }

    private static string RunSync(Func<object?> action)
    {
        try { return Ok(action()); }
        catch (UserError e) { return Fail(e.Message); }
        catch (Exception e) { return Fail("เกิดข้อผิดพลาด: " + e.Message); }
    }
}
