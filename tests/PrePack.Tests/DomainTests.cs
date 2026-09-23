using PrePack.Config;
using PrePack.Data;
using PrePack.Domain;
using Xunit;

namespace PrePack.Tests;

public class ExpiryTests
{
    private static readonly DateOnly Today = new(2026, 9, 23);

    [Fact]
    public void AddsShelfDays()
    {
        var (date, err) = Expiry.ForLabel(Today, 365, new DateOnly(2030, 1, 1));
        Assert.Null(err);
        Assert.Equal(new DateOnly(2027, 9, 23), date);
    }

    [Fact]
    public void CappedBySourceExpiry()
    {
        var (date, _) = Expiry.ForLabel(Today, 365, new DateOnly(2027, 1, 31));
        Assert.Equal(new DateOnly(2027, 1, 31), date);
    }

    [Fact]
    public void NoSourceExpiryUsesShelfDays()
    {
        var (date, _) = Expiry.ForLabel(Today, 30, null);
        Assert.Equal(new DateOnly(2026, 10, 23), date);
    }

    [Theory]
    [InlineData(2026, 9, 23)] // expires today
    [InlineData(2026, 9, 1)]
    public void RefusesExpiredSource(int y, int m, int d)
    {
        var (date, err) = Expiry.ForLabel(Today, 365, new DateOnly(y, m, d));
        Assert.Null(date);
        Assert.NotNull(err);
    }

    [Fact]
    public void RefusesZeroShelfDays() => Assert.NotNull(Expiry.ForLabel(Today, 0, null).Error);

    [Theory]
    [InlineData("20270131", 2027, 1, 31)]
    [InlineData(" 20270131 ", 2027, 1, 31)]
    public void ParsesInvsDate(string s, int y, int m, int d) => Assert.Equal(new DateOnly(y, m, d), Expiry.ParseInvsDate(s));

    [Fact]
    public void InvsDatesIgnoreThaiCulture()
    {
        // Pharmacy PCs run th-TH (Buddhist calendar); INVS dates are Gregorian text.
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
        try
        {
            Assert.Equal(new DateOnly(2027, 1, 31), Expiry.ParseInvsDate("20270131"));
            Assert.Equal("20260923", Expiry.ToInvsDate(new DateOnly(2026, 9, 23)));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2027-01-31")]
    [InlineData("20271345")]
    public void RejectsBadInvsDate(string? s) => Assert.Null(Expiry.ParseInvsDate(s));
}

public class DrugTypeClassifierTests
{
    // Uses the shipped seed so a bad edit to seed-config.json fails here.
    private static readonly DrugTypeClassifier C = FromSeed();

    private static DrugTypeClassifier FromSeed()
    {
        var seed = ConfigStore.Seed();
        return new DrugTypeClassifier(seed.UnitMap, seed.NameKeywords);
    }

    [Theory]
    [InlineData("Paracetamol 500 mg tab", "tablet")]
    [InlineData("Amoxicillin 500 mg CAP", "tablet")]
    [InlineData("Triamcinolone 0.1% cream", "cream")]
    [InlineData("Clotrimazole cream 1%", "cream")]
    [InlineData("Diclofenac gel", "cream")]
    [InlineData("Calamine lotion", "cream")]
    [InlineData("Ferrous fumarate syrup", "liquid")]
    [InlineData("Paracetamol syr 120mg/5ml", "liquid")]
    [InlineData("Amoxicillin susp", "liquid")]
    public void ClassifiesByName(string name, string expected) => Assert.Equal(expected, C.Classify(name, null));

    [Fact]
    public void NameWinsOverUnit() => Assert.Equal("cream", C.Classify("Urea cream 10%", "BOT"));

    [Theory]
    [InlineData("Isosorbide dinitrate 10 mg", "sol")]     // "sol" inside a word
    [InlineData("Captopril 25 mg", "cap")]                // "cap" inside a word
    [InlineData("Gemfibrozil 300 mg", "gel")]             // "gel" is not a prefix match either
    public void DoesNotMatchInsideWords(string name, string _) => Assert.Null(C.FromName(name));

    [Theory]
    [InlineData("TAB", "tablet")]
    [InlineData(" tab ", "tablet")]
    [InlineData("หลอด", "cream")]
    [InlineData("ขวด", "liquid")]
    public void FallsBackToUnit(string unit, string expected) => Assert.Equal(expected, C.Classify("Isosorbide dinitrate", unit));

    [Fact]
    public void UnknownStaysUnknown() => Assert.Null(C.Classify("Isosorbide dinitrate", "AMP"));
}

public class LabelLayoutTests
{
    [Fact]
    public void DefaultIsValid()
    {
        var l = ConfigStore.Seed().Layout;
        Assert.Null(l.Validate());
        Assert.InRange(l.CellWidth, 27.66, 27.67);   // (85 - 2) / 3
        Assert.InRange(l.CellHeight, 13.33, 13.34);  // (50 - 8 - 2) / 3
        Assert.Equal(9, l.PerFrame);
    }

    [Fact]
    public void RejectsHeaderAsTallAsFrame() => Assert.NotNull(new LabelLayout { HeaderHeight = 50 }.Validate());

    [Fact]
    public void FooterTakesRoomFromRows()
    {
        var l = new LabelLayout { FooterHeight = 5 }; // 50 - 8 - 5 - 2 gaps = 35 → 3 rows of 11.67
        Assert.Null(l.Validate());
        Assert.InRange(l.CellHeight, 11.66, 11.67);
        Assert.NotNull(new LabelLayout { HeaderHeight = 25, FooterHeight = 25 }.Validate());
    }

    [Fact]
    public void PerRowAndPerColumnSizesSetPositions()
    {
        var l = new LabelLayout { RowHeights = [12, 13, 15], ColWidths = [30, 25, 28] };
        Assert.Null(l.Validate()); // rows 40 ≤ 40, cols 83 ≤ 83
        Assert.Equal(8, l.RowTop(0));
        Assert.Equal(8 + 12 + 1, l.RowTop(1));
        Assert.Equal(8 + 12 + 1 + 13 + 1, l.RowTop(2));
        Assert.Equal(15, l.RowHeight(2));
        Assert.Equal(0, l.ColLeft(0));
        Assert.Equal(30 + 1 + 25 + 1, l.ColLeft(2));
        Assert.Equal(28, l.ColWidth(2));
    }

    [Fact]
    public void EmptyListsMeanEvenSplit()
    {
        var l = new LabelLayout();
        Assert.Equal(l.CellHeight, l.RowHeight(1));
        Assert.Equal(l.CellWidth, l.ColWidth(2));
    }

    [Theory]
    [InlineData(new[] { 13.0, 13.0 }, null)]              // 2 values for 3 rows
    [InlineData(new[] { 14.0, 14.0, 14.0 }, null)]        // 42 > 40 mm of row space
    [InlineData(new[] { 13.0, 0.0, 13.0 }, null)]         // zero height
    [InlineData(null, new[] { 30.0, 30.0, 30.0 })]        // 90 > 83 mm of column space
    public void RejectsBadPerRowOrColumnSizes(double[]? rows, double[]? cols) =>
        Assert.NotNull(new LabelLayout { RowHeights = rows?.ToList() ?? [], ColWidths = cols?.ToList() ?? [] }.Validate());

    [Fact]
    public void RejectsGapsThatLeaveNoRoom() => Assert.NotNull(new LabelLayout { GapX = 43 }.Validate()); // 85 - 2×43 < 0
}

public class WorkFactorTests
{
    private static readonly List<WorkFactor> Table =
    [
        new("tablet", 30, 1), new("tablet", null, 3), new("tablet", 5, 2), // deliberately unordered
        new("cream", null, 1.5m),
    ];

    [Theory]
    [InlineData("tablet", 1, 2)]
    [InlineData("tablet", 5, 2)]      // band upper bound is inclusive
    [InlineData("tablet", 5.5, 1)]
    [InlineData("tablet", 30, 1)]
    [InlineData("tablet", 100, 3)]    // open-ended band
    [InlineData("cream", 5, 1.5)]
    [InlineData("liquid", 60, 1)]     // no bands for the type → 1, never blocks printing
    public void LooksUpBand(string type, double qty, double factor) =>
        Assert.Equal((decimal)factor, WorkFactors.Lookup(Table, type, (decimal)qty));

    [Fact]
    public void DefaultsAreValidAndNeutral()
    {
        var types = ConfigStore.Seed().DrugTypes.Select(t => t.Key).ToList();
        Assert.Null(WorkFactors.Validate(WorkFactors.Defaults.ToList(), types));
        Assert.All(WorkFactors.Defaults, f => Assert.Equal(1, f.Factor)); // every sticker counts the same until set
        Assert.All(types, t => Assert.Contains(WorkFactors.Defaults, f => f.DrugType == t));
    }

    [Fact]
    public void ValidateNeedsExactlyOneOpenEndedBand()
    {
        Assert.NotNull(WorkFactors.Validate([new("tablet", 30, 1)], ["tablet"]));
        Assert.NotNull(WorkFactors.Validate([new("tablet", null, 1), new("tablet", null, 2)], ["tablet"]));
    }

    [Fact]
    public void ValidateRejectsDuplicateBandsAndBadFactors()
    {
        Assert.NotNull(WorkFactors.Validate([new("tablet", 30, 1), new("tablet", 30, 2), new("tablet", null, 1)], ["tablet"]));
        Assert.NotNull(WorkFactors.Validate([new("tablet", null, -1)], ["tablet"]));
        Assert.NotNull(WorkFactors.Validate([new("tablet", null, 101)], ["tablet"]));
        Assert.NotNull(WorkFactors.Validate([new("tablet", 0, 1), new("tablet", null, 1)], ["tablet"]));
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("admin")]
    [InlineData("")]
    [InlineData(null)]
    public void RejectsWrongPassword(string? pw) => Assert.False(WorkFactors.CheckPassword(pw));

    // The real password is not stored in the repo. Set PREPACK_ADMIN_PASSWORD locally to check it.
    [Fact]
    public void AcceptsRealPasswordWhenProvided()
    {
        var pw = Environment.GetEnvironmentVariable("PREPACK_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(pw)) return;
        Assert.True(WorkFactors.CheckPassword(pw));
        Assert.True(WorkFactors.CheckPassword(" " + pw + " ")); // copy-paste spaces are ignored
        Assert.False(WorkFactors.CheckPassword(pw.ToUpperInvariant() == pw ? pw.ToLowerInvariant() : pw.ToUpperInvariant()));
        Assert.False(WorkFactors.CheckPassword(pw + "4"));
    }

    [Fact]
    public void LogPointsAreStickersTimesFactor() =>
        Assert.Equal(27m, new PrintLog { Stickers = 18, WorkFactor = 1.5m }.WorkPoints);
}

public class DrugLabelTests
{
    [Fact]
    public void InvsDrugsAreKeyedByWorkingCode() =>
        Assert.Equal("INVS:1000456", DrugLabels.Key("INVS", " 1000456 ", "anything"));

    [Fact]
    public void ManualDrugsAreKeyedByNormalizedName()
    {
        var a = DrugLabels.Key("MANUAL", null, "Urea  cream 10%");
        Assert.Equal(a, DrugLabels.Key("MANUAL", "", " urea cream 10% "));
        Assert.StartsWith("NAME:", a);
        Assert.True(a.Length <= 64); // fits drug_labels.drug_key VARCHAR(64)
        Assert.NotEqual(a, DrugLabels.Key("MANUAL", null, "Urea cream 20%"));
    }

    [Theory]
    [InlineData("  Augmentin   1 g ", "Augmentin 1 g")]
    [InlineData("", "")]                                  // empty = use full name
    [InlineData("Amoxicillin 500 mg cap", "")]            // same as full name = use full name
    public void NormalizesLabel(string typed, string stored) =>
        Assert.Equal(stored, DrugLabels.Normalize(typed, "Amoxicillin 500 mg cap"));

    [Fact]
    public void RejectsTooLongLabel() =>
        Assert.Throws<ArgumentException>(() => DrugLabels.Normalize(new string('ก', 101), "x"));
}

public class SchemaTests
{
    [Theory]
    [InlineData("prepack", true)]
    [InlineData("prepack_2569", true)]
    [InlineData("_x", true)]
    [InlineData("1prepack", false)]
    [InlineData("pre-pack", false)]
    [InlineData("prepack`; DROP DATABASE hos; --", false)]
    [InlineData("", false)]
    public void ValidatesDatabaseName(string name, bool ok) => Assert.Equal(ok, Schema.IsValidDatabaseName(name));

    [Fact]
    public void MigrationsAreEmbeddedInOrder()
    {
        var versions = Schema.EmbeddedMigrations().Select(m => m.Version).ToList();
        Assert.Contains("001_init", versions);
        Assert.Equal(versions.Order(StringComparer.Ordinal), versions);
    }

    [Fact]
    public void InitMigrationCreatesTablesOnly()
    {
        var body = Schema.EmbeddedMigrations().Single(m => m.Version == "001_init").Body;
        var stmts = Schema.SplitStatements(body);
        Assert.Equal(3, stmts.Count); // staff, print_logs, work_factors
        Assert.All(stmts, s => Assert.StartsWith("CREATE TABLE IF NOT EXISTS", s));
        Assert.DoesNotContain(stmts, s => s.Contains("JSON", StringComparison.OrdinalIgnoreCase)); // MySQL 5.7 / MariaDB 10.3
    }

    [Fact]
    public void StaffUidMigrationAddsBackfillsThenIndexes()
    {
        var stmts = Schema.SplitStatements(Schema.EmbeddedMigrations().Single(m => m.Version == "002_staff_uid").Body);
        Assert.Equal(3, stmts.Count);
        Assert.StartsWith("ALTER TABLE staff ADD COLUMN uid", stmts[0]);
        Assert.StartsWith("UPDATE staff SET uid = UUID()", stmts[1]);
        Assert.Contains("UNIQUE KEY", stmts[2]);
    }

    [Fact]
    public void SplitDropsCommentsAndBlankLines()
    {
        var s = Schema.SplitStatements("-- c\nCREATE TABLE a (x INT);\n\n-- d\nCREATE TABLE b (\n y INT\n);\n");
        Assert.Equal(["CREATE TABLE a (x INT)", "CREATE TABLE b (\n y INT\n)"], s);
    }
}

public class InvsIniTests
{
    [Fact]
    public void DecodesBase64Pharms()
    {
        static string B(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"[Other]\nServerName=ignored\n[Pharms]\nServerName={B("10.0.0.5")}\nPort={B("1444")}\n" +
                                    $"Database={B("INVS")}\nUser={B("reader")}\nPassword={B("secret")}\n");
            var s = new InvsSettings();
            InvsIni.ApplyTo(s, path);
            Assert.Equal("10.0.0.5", s.Host);
            Assert.Equal(1444, s.Port);
            Assert.Equal("INVS", s.Database);
            Assert.Equal("reader", s.User);
            Assert.Equal("secret", ConfigStore.Unprotect(s.PasswordEnc)); // stored encrypted, never plain
            Assert.NotEqual("secret", s.PasswordEnc);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingPharmsSectionFails()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[Other]\nA=1\n");
            Assert.Throws<InvalidDataException>(() => InvsIni.ApplyTo(new InvsSettings(), path));
        }
        finally { File.Delete(path); }
    }
}
