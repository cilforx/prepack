using System.Globalization;

namespace PrePack.Domain;

public static class Expiry
{
    /// <summary>
    /// Label expiry after repackaging: packDate + shelfDays, never later than the source expiry.
    /// Returns null with a Thai error when the source is already expired.
    /// </summary>
    public static (DateOnly? Date, string? Error) ForLabel(DateOnly packDate, int shelfDays, DateOnly? sourceExpiry)
    {
        if (shelfDays < 1)
            return (null, "อายุยาหลังแบ่งต้องมากกว่า 0 วัน");
        if (sourceExpiry is { } src && src <= packDate)
            return (null, "ยาในภาชนะเดิมหมดอายุแล้ว ห้ามแบ่งบรรจุ");

        var exp = packDate.AddDays(shelfDays);
        if (sourceExpiry is { } cap && cap < exp)
            exp = cap;
        return (exp, null);
    }

    /// <summary>INVS stores EXPIRED_DATE as text "YYYYMMDD" (Gregorian year).</summary>
    public static DateOnly? ParseInvsDate(string? s)
    {
        s = s?.Trim();
        if (s is not { Length: 8 }) return null;
        // Invariant culture: pharmacy PCs run th-TH, whose Buddhist calendar would read 2027 as B.E.
        return DateOnly.TryParseExact(s, InvsDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    public static string ToInvsDate(DateOnly d) => d.ToString(InvsDateFormat, CultureInfo.InvariantCulture);

    private const string InvsDateFormat = "yyyyMMdd";
}
