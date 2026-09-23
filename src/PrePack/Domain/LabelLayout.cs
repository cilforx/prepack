namespace PrePack.Domain;

/// <summary>
/// One printed frame of the sticker roll: a header strip (torn off), Rows x Cols labels, a footer strip (torn off).
/// All lengths are millimetres. Keep in sync with wwwroot/js/labels.js.
/// Rows/columns are equal unless RowHeights / ColWidths give one size per row / column.
/// </summary>
public sealed class LabelLayout
{
    private const double Tolerance = 0.05; // mm — rounding slack when sizes are typed by hand

    public double FrameWidth { get; set; } = 85;
    public double FrameHeight { get; set; } = 50;
    public double HeaderHeight { get; set; } = 8;
    public double FooterHeight { get; set; }
    public int Rows { get; set; } = 3;
    public int Cols { get; set; } = 3;

    /// <summary>Height of each row, top to bottom. Empty = split the space evenly.</summary>
    public List<double> RowHeights { get; set; } = [];

    /// <summary>Width of each column, left to right. Empty = split the width evenly.</summary>
    public List<double> ColWidths { get; set; } = [];

    public double GapX { get; set; } = 1;
    public double GapY { get; set; } = 1;
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Padding { get; set; } = 0.8;
    public double FontSize { get; set; } = 5.5;
    public bool BuddhistYear { get; set; } = true;

    public int PerFrame => Rows * Cols;

    /// <summary>Height available for label rows (frame minus header, footer and row gaps).</summary>
    public double RowSpace => FrameHeight - HeaderHeight - FooterHeight - GapY * (Rows - 1);

    public double ColSpace => FrameWidth - GapX * (Cols - 1);

    /// <summary>Even column width (used when ColWidths is empty).</summary>
    public double CellWidth => ColSpace / Cols;

    /// <summary>Even row height (used when RowHeights is empty).</summary>
    public double CellHeight => RowSpace / Rows;

    public double RowHeight(int row) => RowHeights.Count == Rows ? RowHeights[row] : CellHeight;

    public double ColWidth(int col) => ColWidths.Count == Cols ? ColWidths[col] : CellWidth;

    /// <summary>Top edge of a row, measured from the top of the frame.</summary>
    public double RowTop(int row)
    {
        var y = HeaderHeight;
        for (var i = 0; i < row; i++) y += RowHeight(i) + GapY;
        return y;
    }

    public double ColLeft(int col)
    {
        var x = 0.0;
        for (var i = 0; i < col; i++) x += ColWidth(i) + GapX;
        return x;
    }

    /// <summary>Returns a Thai error message, or null when the layout is usable.</summary>
    public string? Validate()
    {
        if (FrameWidth is < 10 or > 300 || FrameHeight is < 10 or > 300)
            return "ขนาดแผ่นต้องอยู่ระหว่าง 10-300 มม.";
        if (Rows is < 1 or > 10 || Cols is < 1 or > 10)
            return "จำนวนแถว/คอลัมน์ต้องอยู่ระหว่าง 1-10";
        if (HeaderHeight < 0 || FooterHeight < 0)
            return "ส่วนหัว/ส่วนท้ายต้องไม่ติดลบ";
        if (HeaderHeight + FooterHeight >= FrameHeight)
            return "ส่วนหัว + ส่วนท้ายต้องเล็กกว่าความสูงแผ่น";
        if (GapX < 0 || GapY < 0 || Padding < 0)
            return "ระยะห่างต้องไม่ติดลบ";
        if (Math.Abs(OffsetX) > 20 || Math.Abs(OffsetY) > 20)
            return "การเลื่อนตำแหน่งต้องไม่เกิน ±20 มม.";
        if (FontSize is < 3 or > 20)
            return "ขนาดตัวอักษรต้องอยู่ระหว่าง 3-20 pt";

        if (RowHeights.Count > 0)
        {
            if (RowHeights.Count != Rows) return $"ใส่ความสูงให้ครบ {Rows} แถว (หรือเว้นว่างเพื่อแบ่งเท่ากัน)";
            if (RowHeights.Any(h => h <= 0)) return "ความสูงแต่ละแถวต้องมากกว่า 0";
            if (RowHeights.Sum() > RowSpace + Tolerance)
                return $"ความสูงแถวรวม {RowHeights.Sum():0.##} มม. เกินที่ว่าง {RowSpace:0.##} มม. (แผ่น − หัว − ท้าย − ช่องว่าง)";
        }
        else if (CellHeight <= 0)
        {
            return "ส่วนหัว/ส่วนท้าย/ช่องว่างมากเกินไป ดวงฉลากมีความสูงติดลบ";
        }

        if (ColWidths.Count > 0)
        {
            if (ColWidths.Count != Cols) return $"ใส่ความกว้างให้ครบ {Cols} คอลัมน์ (หรือเว้นว่างเพื่อแบ่งเท่ากัน)";
            if (ColWidths.Any(w => w <= 0)) return "ความกว้างแต่ละคอลัมน์ต้องมากกว่า 0";
            if (ColWidths.Sum() > ColSpace + Tolerance)
                return $"ความกว้างคอลัมน์รวม {ColWidths.Sum():0.##} มม. เกินที่ว่าง {ColSpace:0.##} มม. (แผ่น − ช่องว่าง)";
        }
        else if (CellWidth <= 0)
        {
            return "ระยะห่างมากเกินไป ดวงฉลากมีความกว้างติดลบ";
        }
        return null;
    }
}
