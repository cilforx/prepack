namespace PrePack.Data;

/// <summary>A packer. Uid (GUID) is the identity shared by the local database and MySQL.</summary>
public sealed record Staff(string Uid, string Name, string ShortName, bool Active);

/// <summary>Staff plus the bookkeeping sync needs. UpdatedUtc drives last-writer-wins.</summary>
public sealed record StaffSync(Staff Staff, DateTime UpdatedUtc, bool Dirty);

/// <summary>Short sticker name for one drug. LabelName "" = print the full name.</summary>
public sealed record DrugLabel(string Key, string DrugName, string LabelName);

public sealed record DrugLabelSync(DrugLabel Label, DateTime UpdatedUtc, bool Dirty);

public sealed class PrintLog
{
    public Guid ClientUid { get; set; } = Guid.NewGuid();
    public string StaffUid { get; set; } = "";
    public string StaffName { get; set; } = "";
    public string Source { get; set; } = "INVS"; // INVS | MANUAL
    public string? WorkingCode { get; set; }
    public string DrugName { get; set; } = "";
    public string DrugType { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal QtyPerPack { get; set; }
    public string LotNo { get; set; } = "";
    public DateOnly PackDate { get; set; }
    public DateOnly? SrcExpDate { get; set; }
    public DateOnly LabelExpDate { get; set; }
    public int Pages { get; set; }
    public int Stickers { get; set; }
    public decimal WorkFactor { get; set; } = 1;
    public decimal WorkPoints => Stickers * WorkFactor;
    public DateTime PrintedAt { get; set; }
    public string MachineName { get; set; } = "";
}

/// <summary>Per-packer totals. Tablet/Cream/Liquid are sticker counts; Points = Σ stickers × factor.</summary>
public sealed record WorkloadRow(string StaffUid, string Name, int Items, int Pages, int Stickers,
    int Tablet, int Cream, int Liquid, int Manual, decimal Points);

public sealed record LogRow(DateTime PrintedAt, string StaffName, string Source, string? WorkingCode,
    string DrugName, string DrugType, string Unit, decimal QtyPerPack, string LotNo, DateOnly PackDate,
    DateOnly? SrcExpDate, DateOnly LabelExpDate, int Pages, int Stickers, decimal WorkFactor, decimal WorkPoints,
    string MachineName, bool Synced);
