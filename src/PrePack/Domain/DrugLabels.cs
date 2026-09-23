using System.Security.Cryptography;
using System.Text;

namespace PrePack.Domain;

/// <summary>
/// Short names users type for drugs whose full name is too long for a sticker.
/// Remembered per drug and shared by all machines (local + MySQL, last writer wins).
/// </summary>
public static class DrugLabels
{
    public const int MaxLength = 100;

    /// <summary>
    /// Stable key for a drug: the INVS working code, or (manual drugs) a hash of the name so the key fits a
    /// short MySQL primary key whatever the name length. Case/space differences in manual names map together.
    /// </summary>
    public static string Key(string source, string? workingCode, string drugName)
    {
        if (source == "INVS" && !string.IsNullOrWhiteSpace(workingCode))
            return "INVS:" + workingCode.Trim();
        var norm = string.Join(' ', drugName.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
        return "NAME:" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(norm)));
    }

    /// <summary>
    /// What to store for a typed label: trimmed text, or "" when it is empty or the same as the full name
    /// (meaning "use the full name"). Throws with a Thai message when too long.
    /// </summary>
    public static string Normalize(string label, string drugName)
    {
        var t = string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (t.Length > MaxLength) throw new ArgumentException($"ชื่อบนฉลากยาวเกิน {MaxLength} ตัวอักษร");
        return t == drugName.Trim() ? "" : t;
    }
}
