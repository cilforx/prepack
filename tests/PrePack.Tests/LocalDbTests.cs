using System.Globalization;
using PrePack.Data;
using PrePack.Domain;
using Xunit;

namespace PrePack.Tests;

public sealed class LocalDbTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "prepack-test-" + Guid.NewGuid().ToString("N"));
    private readonly CultureInfo _prevCulture = CultureInfo.CurrentCulture;

    public LocalDbTests()
    {
        // Pharmacy PCs run th-TH (Buddhist calendar): every date must still round-trip as Gregorian.
        CultureInfo.CurrentCulture = new CultureInfo("th-TH");
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _prevCulture;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private LocalDb Db() => new(Path.Combine(_dir, "local.db"));

    private static PrintLog Log(string staffUid, DateOnly pack, int stickers = 9, decimal factor = 1.5m, string type = "tablet") => new()
    {
        StaffUid = staffUid, StaffName = "สมชาย", Source = "INVS", WorkingCode = "1000123", DrugName = "Paracetamol",
        DrugType = type, Unit = "เม็ด", QtyPerPack = 30, LotNo = "A1_%", PackDate = pack, SrcExpDate = pack.AddDays(400),
        LabelExpDate = pack.AddDays(365), Pages = 1, Stickers = stickers, WorkFactor = factor,
        PrintedAt = new DateTime(2026, 9, 23, 10, 15, 0), MachineName = "PC1",
    };

    [Fact]
    public void NewDatabaseHasDefaultFactorsAtEpoch()
    {
        var (table, version) = Db().GetWorkFactors();
        Assert.Equal(SyncRules.Epoch, version);
        Assert.Equal(WorkFactors.Defaults.Count, table.Count);
    }

    [Fact]
    public void StaffSaveCreatesUidAndMarksDirty()
    {
        var db = Db();
        var s = db.SaveStaff(new Staff("", "  สมชาย ใจดี ", "", true));
        Assert.True(Guid.TryParse(s.Uid, out _));
        Assert.Equal("สมชาย ใจดี", s.ShortName); // empty short name falls back to the full name
        var row = Assert.Single(db.ListStaffSync());
        Assert.True(row.Dirty);
        Assert.Equal(0, row.UpdatedUtc.Millisecond); // whole seconds, like MySQL DATETIME

        db.MarkStaffClean(s.Uid, row.UpdatedUtc);
        Assert.False(Assert.Single(db.ListStaffSync()).Dirty);
    }

    [Fact]
    public void RejectsEmptyStaffName() => Assert.Throws<UserError>(() => Db().SaveStaff(new Staff("", " ", "", true)));

    [Fact]
    public void LogRoundTripKeepsGregorianDatesAndNumbers()
    {
        var db = Db();
        var pack = new DateOnly(2026, 9, 23);
        var log = Log("u1", pack);
        db.InsertLog(log);

        var back = Assert.Single(db.UnsyncedLogs(10));
        Assert.Equal(log.ClientUid, back.ClientUid);
        Assert.Equal(pack, back.PackDate);
        Assert.Equal(new DateOnly(2027, 10, 28), back.SrcExpDate);
        Assert.Equal(new DateOnly(2027, 9, 23), back.LabelExpDate);
        Assert.Equal(new DateTime(2026, 9, 23, 10, 15, 0), back.PrintedAt);
        Assert.Equal(1.5m, back.WorkFactor);
        Assert.Equal(13.5m, back.WorkPoints);
    }

    [Fact]
    public void SyncedLogsStayAsBackupAndCanBeRequeued()
    {
        var db = Db();
        var log = Log("u1", new DateOnly(2026, 9, 23));
        db.InsertLog(log);
        db.MarkSynced(log.ClientUid);
        Assert.Equal(0, db.UnsyncedCount());
        Assert.Single(db.Logs(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "", "", "", 100)); // still there

        Assert.Equal(1, db.ResetSynced());
        Assert.Equal(1, db.UnsyncedCount());
    }

    [Fact]
    public void WorkloadSumsPerStaffInRange()
    {
        var db = Db();
        db.InsertLog(Log("u1", new DateOnly(2026, 9, 10), stickers: 9, factor: 2));
        db.InsertLog(Log("u1", new DateOnly(2026, 9, 11), stickers: 18, factor: 1, type: "cream"));
        db.InsertLog(Log("u2", new DateOnly(2026, 9, 12), stickers: 9, factor: 1));
        db.InsertLog(Log("u2", new DateOnly(2026, 10, 1), stickers: 90, factor: 1)); // outside the range

        var rows = db.Workload(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), "");
        Assert.Equal(["u1", "u2"], rows.Select(r => r.StaffUid));
        var u1 = rows[0];
        Assert.Equal(2, u1.Items);
        Assert.Equal(27, u1.Stickers);
        Assert.Equal(9, u1.Tablet);
        Assert.Equal(18, u1.Cream);
        Assert.Equal(36m, u1.Points); // 9×2 + 18×1
        Assert.Equal(9m, rows[1].Points);
    }

    [Fact]
    public void LotSearchTreatsWildcardsLiterally()
    {
        var db = Db();
        db.InsertLog(Log("u1", new DateOnly(2026, 9, 10))); // lot "A1_%"
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        Assert.Single(db.Logs(from, to, "", "", "1_%", 10));
        Assert.Empty(db.Logs(from, to, "", "", "1X", 10));
    }

    [Fact]
    public void FactorTableRoundTripsWithVersion()
    {
        var db = Db();
        var v = new DateTime(2026, 9, 23, 3, 4, 5, DateTimeKind.Utc);
        db.SaveWorkFactors([new("tablet", 30, 1), new("tablet", null, 2.5m), new("cream", null, 1)], v);
        var (table, version) = db.GetWorkFactors();
        Assert.Equal(v, version);
        Assert.Equal(2.5m, WorkFactors.Lookup(table, "tablet", 100));
    }

    [Fact]
    public void SeedStaffIsIdempotentAndSameUidOnEveryMachine()
    {
        var names = PrePack.Config.ConfigStore.SeedStaffNames();
        Assert.Equal(21, names.Count);
        Assert.Contains("วีรภัทร", names);
        Assert.Contains("ภัทราวดี", names);

        var db = Db();
        Assert.Equal(21, db.SeedStaff(names));
        Assert.Equal(0, db.SeedStaff(names)); // second start: nothing new

        // Another machine seeds the same uids → no duplicates when both sync to MySQL.
        var other = new LocalDb(Path.Combine(_dir, "other.db"));
        other.SeedStaff(names);
        Assert.Equal(db.ListStaff().Select(s => s.Uid).Order(), other.ListStaff().Select(s => s.Uid).Order());

        // Seeded rows are at the epoch, so a server edit always wins; a fresh server gets them pushed.
        var row = db.ListStaffSync().First();
        Assert.Equal(SyncRules.Epoch, row.UpdatedUtc);
        Assert.Equal(SyncAction.Push, SyncRules.Staff(row.UpdatedUtc, row.Dirty, null));
        Assert.Equal(SyncAction.Pull, SyncRules.Staff(row.UpdatedUtc, row.Dirty, new DateTime(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void SeedDoesNotUndoLocalEdits()
    {
        var db = Db();
        db.SeedStaff(["นนท์"]);
        var s = db.ListStaff().Single();
        db.SaveStaff(s with { Active = false });
        db.SeedStaff(["นนท์"]);
        Assert.False(db.ListStaff().Single().Active);
    }

    [Fact]
    public void DrugLabelRoundTripAndDirtyFlag()
    {
        var db = Db();
        var key = DrugLabels.Key("INVS", "1000456", "Amoxicillin/Clavulanate 1 g (Augmentin) tab");
        Assert.Null(db.GetDrugLabel(key));
        db.SaveDrugLabel(new DrugLabel(key, "Amoxicillin/Clavulanate 1 g (Augmentin) tab", "Augmentin 1 g"));
        Assert.Equal("Augmentin 1 g", db.GetDrugLabel(key)!.LabelName);
        var row = Assert.Single(db.ListDrugLabelsSync());
        Assert.True(row.Dirty);
        db.MarkDrugLabelClean(key, row.UpdatedUtc);
        Assert.False(Assert.Single(db.ListDrugLabelsSync()).Dirty);
    }

    [Fact]
    public void UpgradesVersion1DatabaseWithoutLosingData()
    {
        var path = Path.Combine(_dir, "v1.db");
        var db = new LocalDb(path);
        db.SaveStaff(new Staff("", "สุดา", "", true));
        // Pretend this file was made by the previous release (schema v1, no drug_labels table).
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE drug_labels; PRAGMA user_version = 1;";
            cmd.ExecuteNonQuery();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        var upgraded = new LocalDb(path);
        Assert.Single(upgraded.ListStaff());
        Assert.Null(upgraded.GetDrugLabel("INVS:1")); // table exists again
    }

    [Fact]
    public void ReopeningKeepsData()
    {
        Db().SaveStaff(new Staff("", "สุดา", "", true));
        Assert.Single(Db().ListStaff()); // second LocalDb on the same file: schema not recreated
    }
}

public class SyncRulesTests
{
    private static readonly DateTime T0 = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StaffOnlyLocalIsPushed() => Assert.Equal(SyncAction.Push, SyncRules.Staff(T0, true, null));

    [Fact]
    public void StaffOnlyRemoteIsPulled() => Assert.Equal(SyncAction.Pull, SyncRules.Staff(null, false, T0));

    [Fact]
    public void NewerLocalEditIsPushed() => Assert.Equal(SyncAction.Push, SyncRules.Staff(T0.AddMinutes(1), true, T0));

    [Fact]
    public void NewerRemoteEditWinsEvenOverDirtyLocal() => Assert.Equal(SyncAction.Pull, SyncRules.Staff(T0, true, T0.AddMinutes(1)));

    [Fact]
    public void SameSecondIsAlreadyInSync() =>
        Assert.Equal(SyncAction.None, SyncRules.Staff(T0.AddMilliseconds(400), true, T0));

    [Fact]
    public void UntouchedLocalFactorsNeverOverwriteServer() =>
        Assert.Equal(SyncAction.Pull, SyncRules.Factors(SyncRules.Epoch, T0));

    [Fact]
    public void OfflineFactorEditIsPushedToFreshServer() =>
        Assert.Equal(SyncAction.Push, SyncRules.Factors(T0, SyncRules.Epoch));

    [Fact]
    public void EqualFactorVersionsDoNothing() => Assert.Equal(SyncAction.None, SyncRules.Factors(T0, T0));
}
