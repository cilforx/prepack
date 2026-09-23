namespace PrePack.Domain;

/// <summary>
/// One printed frame of the sticker roll: a header strip that is torn off, then Rows x Cols labels.
/// All lengths are millimetres. Keep in sync with wwwroot/js/labels.js.
/// </summary>
public sealed class LabelLayout
{
    public double FrameWidth { get; set; } = 85;
    public double FrameHeight { get; set; } = 50;
    public double HeaderHeight { get; set; } = 8;
    public int Rows { get; set; } = 3;
    public int Cols { get; set; } = 3;
    public double GapX { get; set; } = 1;
    public double GapY { get; set; } = 1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Padding { get; set; } = 0.8;
    public double FontSize { get; set; } = 5.5;
    public bool BuddhistYear { get; set; } = true;

    public int PerFrame => Rows * Cols;

    public double CellWidth => (FrameWidth - GapX * (Cols - 1)) / Cols;

    public double CellHeight => (FrameHeight - HeaderHeight - GapY * (Rows - 1)) / Rows;

    /// <summary>Returns a Thai error message, or null when the layout is usable.</summary>
    public string? Validate()
    {
        if (FrameWidth is < 10 or > 300 || FrameHeight is < 10 or > 300)
            return "ขนาดแผ่นต้องอยู่ระหว่าง 10-300 มม.";
        if (Rows is < 1 or > 10 || Cols is < 1 or > 10)
            return "จำนวนแถว/คอลัมน์ต้องอยู่ระหว่าง 1-10";
        if (HeaderHeight < 0 || HeaderHeight >= FrameHeight)
            return "ส่วนหัวต้องเล็กกว่าความสูงแผ่น";
        if (GapX < 0 || GapY < 0 || Padding < 0)
            return "ระยะห่างต้องไม่ติดลบ";
        if (Math.Abs(OffsetX) > 20 || Math.Abs(OffsetY) > 20)
            return "การเลื่อนตำแหน่งต้องไม่เกิน ±20 มม.";
        if (FontSize is < 3 or > 20)
            return "ขนาดตัวอักษรต้องอยู่ระหว่าง 3-20 pt";
        if (CellWidth <= 0 || CellHeight <= 0)
            return "ระยะห่างมากเกินไป ดวงฉลากมีขนาดติดลบ";
        return null;
    }
}
