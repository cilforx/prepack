using MySqlConnector;

namespace PrePack.Data;

public sealed record Staff(int Id, string Name, string ShortName, bool Active);

public sealed class PrintLog
{
    public Guid ClientUid { get; set; } = Guid.NewGuid();
    public int StaffId { get; set; }
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
public sealed record WorkloadRow(int StaffId, string Name, int Items, int Pages, int Stickers,
    int Tablet, int Cream, int Liquid, int Manual, decimal Points);

public sealed record LogRow(long Id, DateTime PrintedAt, string StaffName, string Source, string? WorkingCode,
    string DrugName, string DrugType, string Unit, decimal QtyPerPack, string LotNo, DateOnly PackDate,
    DateOnly? SrcExpDate, DateOnly LabelExpDate, int Pages, int Stickers, decimal WorkFactor, decimal WorkPoints,
    string MachineName);

/// <summary>Reads and writes PrePack's own MySQL database. Never used for HOSxP / JHCIS / INVS.</summary>
public sealed class PrePackDb(string connectionString)
{
    public static PrePackDb Create(string host, int port, string user, string password, string database) =>
        new(new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            UserID = user,
            Password = password,
            Database = database,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = 10,
            DefaultCommandTimeout = 15,
        }.ConnectionString);

    private async Task<MySqlConnection> OpenAsync()
    {
        var conn = new MySqlConnection(connectionString);
        try
        {
            await conn.OpenAsync();
        }
        catch (MySqlException e)
        {
            await conn.DisposeAsync();
            throw new UserError("เชื่อมต่อ MySQL ไม่ได้: " + e.Message);
        }
        return conn;
    }

    // ── Staff ──

    public async Task<List<Staff>> ListStaffAsync()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id, name, short_name, active FROM staff ORDER BY active DESC, name", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Staff>();
        while (await r.ReadAsync())
            list.Add(new Staff(Convert.ToInt32(r.GetValue(0)), r.GetString(1), r.GetString(2), r.GetBoolean(3)));
        return list;
    }

    public async Task<Staff> SaveStaffAsync(Staff s)
    {
        var name = s.Name.Trim();
        var shortName = s.ShortName.Trim();
        if (shortName.Length == 0) shortName = name;
        if (name.Length == 0) throw new UserError("กรุณากรอกชื่อเจ้าหน้าที่");
        if (name.Length > 100) throw new UserError("ชื่อยาวเกิน 100 ตัวอักษร");
        if (shortName.Length > 30) throw new UserError("ชื่อบนฉลากยาวเกิน 30 ตัวอักษร");

        await using var conn = await OpenAsync();
        if (s.Id == 0)
        {
            await using var ins = new MySqlCommand(
                "INSERT INTO staff (name, short_name, active) VALUES (@n, @s, 1)", conn);
            ins.Parameters.AddWithValue("@n", name);
            ins.Parameters.AddWithValue("@s", shortName);
            await ins.ExecuteNonQueryAsync();
            return new Staff((int)ins.LastInsertedId, name, shortName, true);
        }

        await using var upd = new MySqlCommand(
            "UPDATE staff SET name = @n, short_name = @s, active = @a WHERE id = @id", conn);
        upd.Parameters.AddWithValue("@n", name);
        upd.Parameters.AddWithValue("@s", shortName);
        upd.Parameters.AddWithValue("@a", s.Active);
        upd.Parameters.AddWithValue("@id", s.Id);
        await upd.ExecuteNonQueryAsync();
        return s with { Name = name, ShortName = shortName };
    }

    // ── Print log ──

    /// <summary>
    /// Inserts a print log. Re-sending the same ClientUid is a no-op, so queued logs can be retried safely.
    /// (ON DUPLICATE KEY rather than INSERT IGNORE: IGNORE would also silently swallow FK/ENUM/length errors.)
    /// </summary>
    public async Task InsertLogAsync(PrintLog l)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("""
            INSERT INTO print_logs
              (client_uid, staff_id, staff_name, source, working_code, drug_name, drug_type, unit, qty_per_pack,
               lot_no, pack_date, src_exp_date, label_exp_date, pages, stickers, work_factor, work_points,
               printed_at, machine_name)
            VALUES (@uid, @sid, @sname, @src, @code, @drug, @type, @unit, @qty,
                    @lot, @pack, @srcexp, @exp, @pages, @stickers, @factor, @points, @at, @machine)
            ON DUPLICATE KEY UPDATE client_uid = client_uid
            """, conn);
        cmd.Parameters.AddWithValue("@factor", l.WorkFactor);
        cmd.Parameters.AddWithValue("@points", l.WorkPoints);
        cmd.Parameters.AddWithValue("@uid", l.ClientUid.ToString());
        cmd.Parameters.AddWithValue("@sid", l.StaffId);
        cmd.Parameters.AddWithValue("@sname", l.StaffName);
        cmd.Parameters.AddWithValue("@src", l.Source);
        cmd.Parameters.AddWithValue("@code", (object?)l.WorkingCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@drug", l.DrugName);
        cmd.Parameters.AddWithValue("@type", l.DrugType);
        cmd.Parameters.AddWithValue("@unit", l.Unit);
        cmd.Parameters.AddWithValue("@qty", l.QtyPerPack);
        cmd.Parameters.AddWithValue("@lot", l.LotNo);
        cmd.Parameters.AddWithValue("@pack", l.PackDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@srcexp", l.SrcExpDate is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@exp", l.LabelExpDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@pages", l.Pages);
        cmd.Parameters.AddWithValue("@stickers", l.Stickers);
        cmd.Parameters.AddWithValue("@at", l.PrintedAt);
        cmd.Parameters.AddWithValue("@machine", l.MachineName);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Quantities this drug was packed in most often, for the shortcut buttons.</summary>
    public async Task<List<decimal>> FrequentQtyAsync(string? workingCode, string drugName, int limit = 5)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(workingCode != null
            ? "SELECT qty_per_pack FROM print_logs WHERE working_code = @k GROUP BY qty_per_pack ORDER BY COUNT(*) DESC, qty_per_pack LIMIT @n"
            : "SELECT qty_per_pack FROM print_logs WHERE working_code IS NULL AND drug_name = @k GROUP BY qty_per_pack ORDER BY COUNT(*) DESC, qty_per_pack LIMIT @n",
            conn);
        cmd.Parameters.AddWithValue("@k", workingCode ?? drugName);
        cmd.Parameters.AddWithValue("@n", limit);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<decimal>();
        while (await r.ReadAsync()) list.Add(r.GetDecimal(0));
        return list;
    }

    // ── Reports ──

    /// <summary>Per-staff totals between from and to (pack dates, inclusive). source: "" | INVS | MANUAL.</summary>
    public async Task<List<WorkloadRow>> WorkloadAsync(DateOnly from, DateOnly to, string source)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("""
            SELECT staff_id, MAX(staff_name), COUNT(*), SUM(pages), SUM(stickers),
                   SUM(IF(drug_type = 'tablet', stickers, 0)),
                   SUM(IF(drug_type = 'cream',  stickers, 0)),
                   SUM(IF(drug_type = 'liquid', stickers, 0)),
                   SUM(IF(source = 'MANUAL', 1, 0)),
                   SUM(work_points)
            FROM print_logs
            WHERE pack_date BETWEEN @from AND @to AND (@src = '' OR source = @src)
            GROUP BY staff_id
            ORDER BY SUM(work_points) DESC, SUM(stickers) DESC
            """, conn);
        AddRange(cmd, from, to, source);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<WorkloadRow>();
        while (await r.ReadAsync())
        {
            // COUNT/SUM come back as BIGINT/DECIMAL depending on the server; convert explicitly.
            int I(int i) => Convert.ToInt32(r.GetValue(i));
            list.Add(new WorkloadRow(I(0), r.GetString(1), I(2), I(3), I(4), I(5), I(6), I(7), I(8),
                Convert.ToDecimal(r.GetValue(9))));
        }
        return list;
    }

    /// <summary>Print log rows for the history view / drill-down. staffId 0 = everyone; lot = substring search.</summary>
    public async Task<List<LogRow>> LogsAsync(DateOnly from, DateOnly to, string source, int staffId, string lot, int limit = 500)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("""
            SELECT id, printed_at, staff_name, source, working_code, drug_name, drug_type, unit, qty_per_pack,
                   lot_no, pack_date, src_exp_date, label_exp_date, pages, stickers, work_factor, work_points, machine_name
            FROM print_logs
            WHERE pack_date BETWEEN @from AND @to AND (@src = '' OR source = @src)
              AND (@staff = 0 OR staff_id = @staff) AND (@lot = '' OR lot_no LIKE @lotlike)
            ORDER BY printed_at DESC, id DESC
            LIMIT @n
            """, conn);
        AddRange(cmd, from, to, source);
        var lotTrim = lot.Trim();
        cmd.Parameters.AddWithValue("@staff", staffId);
        cmd.Parameters.AddWithValue("@lot", lotTrim);
        cmd.Parameters.AddWithValue("@lotlike", "%" + EscapeLike(lotTrim) + "%");
        cmd.Parameters.AddWithValue("@n", limit);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<LogRow>();
        while (await r.ReadAsync())
        {
            list.Add(new LogRow(Convert.ToInt64(r.GetValue(0)), r.GetDateTime(1), r.GetString(2), r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetDecimal(8),
                r.GetString(9), DateOnly.FromDateTime(r.GetDateTime(10)),
                r.IsDBNull(11) ? null : DateOnly.FromDateTime(r.GetDateTime(11)),
                DateOnly.FromDateTime(r.GetDateTime(12)), Convert.ToInt32(r.GetValue(13)),
                Convert.ToInt32(r.GetValue(14)), r.GetDecimal(15), r.GetDecimal(16), r.GetString(17)));
        }
        return list;
    }

    // ── Workload factors ──

    public async Task<List<Domain.WorkFactor>> ListWorkFactorsAsync()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT drug_type, max_qty, factor FROM work_factors ORDER BY drug_type, max_qty IS NULL, max_qty", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<Domain.WorkFactor>();
        while (await r.ReadAsync())
            list.Add(new Domain.WorkFactor(r.GetString(0), r.IsDBNull(1) ? null : r.GetDecimal(1), r.GetDecimal(2)));
        return list;
    }

    /// <summary>Replaces the whole table in one transaction. Past print logs keep their own factor snapshot.</summary>
    public async Task SaveWorkFactorsAsync(IReadOnlyCollection<Domain.WorkFactor> table)
    {
        await using var conn = await OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var del = new MySqlCommand("DELETE FROM work_factors", conn, tx))
            await del.ExecuteNonQueryAsync();
        foreach (var f in table)
        {
            await using var ins = new MySqlCommand(
                "INSERT INTO work_factors (drug_type, max_qty, factor) VALUES (@t, @m, @f)", conn, tx);
            ins.Parameters.AddWithValue("@t", f.DrugType);
            ins.Parameters.AddWithValue("@m", (object?)f.MaxQty ?? DBNull.Value);
            ins.Parameters.AddWithValue("@f", f.Factor);
            await ins.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private static void AddRange(MySqlCommand cmd, DateOnly from, DateOnly to, string source)
    {
        cmd.Parameters.AddWithValue("@from", from.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@to", to.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@src", source);
    }

    private static string EscapeLike(string s) =>
        s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
