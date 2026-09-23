using System.Security.Cryptography;
using System.Text;

namespace PrePack.Domain;

/// <summary>
/// One band of the workload table: packs of this drug type with #qty up to MaxQty (null = no limit)
/// count Factor points per sticker. Stored in MySQL so every machine scores the same way.
/// </summary>
public sealed record WorkFactor(string DrugType, decimal? MaxQty, decimal Factor);

public static class WorkFactors
{
    public const decimal MaxFactor = 100;

    /// <summary>Default bands. All factors start at 1 (every sticker counts the same) until a pharmacist sets them.</summary>
    public static readonly IReadOnlyList<WorkFactor> Defaults =
    [
        new("tablet", 5, 1), new("tablet", 30, 1), new("tablet", 60, 1), new("tablet", 100, 1), new("tablet", null, 1),
        new("cream", null, 1),
        new("liquid", null, 1),
    ];

    /// <summary>
    /// Factor for a pack: the first band of the type (ordered by MaxQty, open-ended last) whose MaxQty ≥ qty.
    /// Returns 1 when the type has no bands, so a missing table never blocks printing.
    /// </summary>
    public static decimal Lookup(IEnumerable<WorkFactor> table, string drugType, decimal qty)
    {
        foreach (var band in Ordered(table.Where(b => b.DrugType == drugType)))
        {
            if (band.MaxQty is null || qty <= band.MaxQty) return band.Factor;
        }
        return 1;
    }

    public static IEnumerable<WorkFactor> Ordered(IEnumerable<WorkFactor> bands) =>
        bands.OrderBy(b => b.MaxQty is null).ThenBy(b => b.MaxQty);

    /// <summary>Returns a Thai error, or null when every type has distinct bands ending in an open-ended one.</summary>
    public static string? Validate(IReadOnlyCollection<WorkFactor> table, IEnumerable<string> types)
    {
        foreach (var type in types)
        {
            var bands = table.Where(b => b.DrugType == type).ToList();
            if (bands.Count(b => b.MaxQty is null) != 1)
                return "แต่ละประเภทต้องมีช่วงสุดท้าย \"ขึ้นไป\" หนึ่งช่วงพอดี";
            if (bands.Any(b => b.MaxQty is <= 0))
                return "จำนวนเม็ดสูงสุดของช่วงต้องมากกว่า 0";
            if (bands.Where(b => b.MaxQty != null).GroupBy(b => b.MaxQty).Any(g => g.Count() > 1))
                return "มีช่วงจำนวนซ้ำกัน";
        }
        if (table.Any(b => b.Factor is < 0 or > MaxFactor))
            return $"factor ต้องอยู่ระหว่าง 0-{MaxFactor}";
        return null;
    }

    // SHA-256 of "prepack-admin:" + password. The password guards the factor table from casual edits;
    // it is not a security boundary (anyone with the exe could patch it).
    private const string AdminHash = "9f96da77a36f56790d483f4fb64912d2b1804f71a702498034ce3b1554352822";

    public static bool CheckPassword(string? password)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("prepack-admin:" + (password ?? ""))));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(hash.ToLowerInvariant()), Encoding.ASCII.GetBytes(AdminHash));
    }
}
