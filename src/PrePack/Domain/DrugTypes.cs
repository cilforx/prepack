namespace PrePack.Domain;

public sealed class DrugTypeDef
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Unit { get; set; } = "";
    public int ShelfDays { get; set; }
    public List<decimal> QtyPresets { get; set; } = [];
}

/// <summary>
/// Decides the drug type (tablet / cream / liquid) from the INVS unit and the drug name.
/// The name wins when it contains a known keyword, because INVS units are often ambiguous
/// (e.g. a cream stored as "JAR" or a syrup as "BOT").
/// </summary>
public sealed class DrugTypeClassifier
{
    private readonly Dictionary<string, string> _unitMap;
    private readonly List<(string Type, HashSet<string> Words)> _keywords;

    public DrugTypeClassifier(IDictionary<string, string> unitMap, IDictionary<string, List<string>> nameKeywords)
    {
        _unitMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (unit, type) in unitMap)
            _unitMap[unit.Trim()] = type;

        // Order is fixed so a name like "X tab (soluble)" resolves predictably: tablet, cream, liquid.
        _keywords = [];
        foreach (var type in new[] { "tablet", "cream", "liquid" })
        {
            if (nameKeywords.TryGetValue(type, out var words))
                _keywords.Add((type, new HashSet<string>(words.Select(w => w.Trim()), StringComparer.OrdinalIgnoreCase)));
        }
    }

    /// <summary>Returns the type key, or null when neither the name nor the unit says.</summary>
    public string? Classify(string? drugName, string? invsUnit)
    {
        var byName = FromName(drugName);
        if (byName != null) return byName;
        if (!string.IsNullOrWhiteSpace(invsUnit) && _unitMap.TryGetValue(invsUnit.Trim(), out var byUnit))
            return byUnit;
        return null;
    }

    /// <summary>Matches whole words only, so "sol" does not match "Isosorbide".</summary>
    public string? FromName(string? drugName)
    {
        if (string.IsNullOrWhiteSpace(drugName)) return null;
        var tokens = Tokenize(drugName);
        foreach (var (type, words) in _keywords)
        {
            if (tokens.Any(words.Contains)) return type;
        }
        return null;
    }

    internal static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var cur = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetter(ch))
            {
                cur.Append(ch);
            }
            else if (cur.Length > 0)
            {
                tokens.Add(cur.ToString());
                cur.Clear();
            }
        }
        if (cur.Length > 0) tokens.Add(cur.ToString());
        return tokens;
    }
}
