using MySqlConnector;
using PrePack.Domain;

namespace PrePack.Data;

/// <summary>
/// PrePack's shared MySQL database (the sync target). Never used for HOSxP / JHCIS / INVS.
/// Every machine writes to its local SQLite first; SyncService pushes here.
/// </summary>
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
            ConnectionTimeout = 5,
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

    // ── Staff (sync) ──

    /// <summary>All staff with their remote id, for merging and for mapping uid → staff_id on log upload.</summary>
    public async Task<List<(int Id, StaffSync Row)>> ListStaffAsync()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id, uid, name, short_name, active, updated_at FROM staff WHERE uid IS NOT NULL", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<(int, StaffSync)>();
        while (await r.ReadAsync())
        {
            var staff = new Staff(r.GetString(1), r.GetString(2), r.GetString(3), r.GetBoolean(4));
            // updated_at holds UTC written by PrePack (see UpsertStaffAsync).
            list.Add((Convert.ToInt32(r.GetValue(0)),
                new StaffSync(staff, DateTime.SpecifyKind(r.GetDateTime(5), DateTimeKind.Utc), false)));
        }
        return list;
    }

    public async Task UpsertStaffAsync(Staff s, DateTime updatedUtc)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("""
            INSERT INTO staff (uid, name, short_name, active, updated_at) VALUES (@uid, @n, @s, @a, @u)
            ON DUPLICATE KEY UPDATE name = VALUES(name), short_name = VALUES(short_name),
                                    active = VALUES(active), updated_at = VALUES(updated_at)
            """, conn);
        cmd.Parameters.AddWithValue("@uid", s.Uid);
        cmd.Parameters.AddWithValue("@n", s.Name);
        cmd.Parameters.AddWithValue("@s", s.ShortName);
        cmd.Parameters.AddWithValue("@a", s.Active);
        cmd.Parameters.AddWithValue("@u", DateTime.SpecifyKind(updatedUtc, DateTimeKind.Unspecified));
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Print log (sync) ──

    /// <summary>
    /// Inserts a print log. Re-sending the same ClientUid is a no-op, so upload can be retried and a
    /// full re-push from the local backup is safe. (ON DUPLICATE KEY rather than INSERT IGNORE:
    /// IGNORE would also silently swallow FK / ENUM / length errors.)
    /// </summary>
    public async Task InsertLogAsync(PrintLog l, int staffId)
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
        cmd.Parameters.AddWithValue("@uid", l.ClientUid.ToString());
        cmd.Parameters.AddWithValue("@sid", staffId);
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
        cmd.Parameters.AddWithValue("@factor", l.WorkFactor);
        cmd.Parameters.AddWithValue("@points", l.WorkPoints);
        cmd.Parameters.AddWithValue("@at", l.PrintedAt);
        cmd.Parameters.AddWithValue("@machine", l.MachineName);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Drug labels (sync + direct lookup) ──

    public async Task<List<DrugLabelSync>> ListDrugLabelsAsync()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("SELECT drug_key, drug_name, label_name, updated_at FROM drug_labels", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<DrugLabelSync>();
        while (await r.ReadAsync())
        {
            list.Add(new DrugLabelSync(new DrugLabel(r.GetString(0), r.GetString(1), r.GetString(2)),
                DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc), false));
        }
        return list;
    }

    /// <summary>One label straight from the server (used when a drug is picked, so other machines' edits show at once).</summary>
    public async Task<DrugLabelSync?> GetDrugLabelAsync(string key)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT drug_key, drug_name, label_name, updated_at FROM drug_labels WHERE drug_key = @k", conn);
        cmd.Parameters.AddWithValue("@k", key);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync()
            ? new DrugLabelSync(new DrugLabel(r.GetString(0), r.GetString(1), r.GetString(2)),
                DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc), false)
            : null;
    }

    public async Task UpsertDrugLabelAsync(DrugLabel l, DateTime updatedUtc, string machine)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("""
            INSERT INTO drug_labels (drug_key, drug_name, label_name, updated_at, machine_name) VALUES (@k, @n, @l, @u, @m)
            ON DUPLICATE KEY UPDATE drug_name = VALUES(drug_name), label_name = VALUES(label_name),
                                    updated_at = VALUES(updated_at), machine_name = VALUES(machine_name)
            """, conn);
        cmd.Parameters.AddWithValue("@k", l.Key);
        cmd.Parameters.AddWithValue("@n", l.DrugName.Length > 200 ? l.DrugName[..200] : l.DrugName);
        cmd.Parameters.AddWithValue("@l", l.LabelName);
        cmd.Parameters.AddWithValue("@u", DateTime.SpecifyKind(updatedUtc, DateTimeKind.Unspecified));
        cmd.Parameters.AddWithValue("@m", machine);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Workload factors (sync) ──

    /// <summary>The whole table and its version (latest updated_at, UTC).</summary>
    public async Task<(List<WorkFactor> Table, DateTime VersionUtc)> ListWorkFactorsAsync()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT drug_type, max_qty, factor, updated_at FROM work_factors", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<WorkFactor>();
        var version = SyncRules.Epoch;
        while (await r.ReadAsync())
        {
            list.Add(new WorkFactor(r.GetString(0), r.IsDBNull(1) ? null : r.GetDecimal(1), r.GetDecimal(2)));
            var u = DateTime.SpecifyKind(r.GetDateTime(3), DateTimeKind.Utc);
            if (u > version) version = u;
        }
        return (list, version);
    }

    /// <summary>Replaces the whole table in one transaction, stamping every row with the table version.</summary>
    public async Task SaveWorkFactorsAsync(IReadOnlyCollection<WorkFactor> table, DateTime versionUtc)
    {
        await using var conn = await OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var del = new MySqlCommand("DELETE FROM work_factors", conn, tx))
            await del.ExecuteNonQueryAsync();
        foreach (var f in table)
        {
            await using var ins = new MySqlCommand(
                "INSERT INTO work_factors (drug_type, max_qty, factor, updated_at) VALUES (@t, @m, @f, @u)", conn, tx);
            ins.Parameters.AddWithValue("@t", f.DrugType);
            ins.Parameters.AddWithValue("@m", (object?)f.MaxQty ?? DBNull.Value);
            ins.Parameters.AddWithValue("@f", f.Factor);
            ins.Parameters.AddWithValue("@u", DateTime.SpecifyKind(versionUtc, DateTimeKind.Unspecified));
            await ins.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    // ── Reports (all machines) ──

    /// <summary>Per-staff totals between from and to (pack dates, inclusive). source: "" | INVS | MANUAL.</summary>
    public async Task<List<WorkloadRow>> WorkloadAsync(DateOnly from, DateOnly to, string source)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("""
            SELECT s.uid, MAX(p.staff_name), COUNT(*), SUM(p.pages), SUM(p.stickers),
                   SUM(IF(p.drug_type = 'tablet', p.stickers, 0)),
                   SUM(IF(p.drug_type = 'cream',  p.stickers, 0)),
                   SUM(IF(p.drug_type = 'liquid', p.stickers, 0)),
                   SUM(IF(p.source = 'MANUAL', 1, 0)),
                   SUM(p.work_points)
            FROM print_logs p JOIN staff s ON s.id = p.staff_id
            WHERE p.pack_date BETWEEN @from AND @to AND (@src = '' OR p.source = @src)
            GROUP BY s.uid
            ORDER BY SUM(p.work_points) DESC, SUM(p.stickers) DESC
            """, conn);
        AddRange(cmd, from, to, source);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<WorkloadRow>();
        while (await r.ReadAsync())
        {
            // COUNT/SUM come back as BIGINT/DECIMAL depending on the server; convert explicitly.
            int I(int i) => Convert.ToInt32(r.GetValue(i));
            list.Add(new WorkloadRow(r.GetString(0), r.GetString(1), I(2), I(3), I(4), I(5), I(6), I(7), I(8),
                Convert.ToDecimal(r.GetValue(9))));
        }
        return list;
    }

    /// <summary>Print log rows for history / drill-down. staffUid "" = everyone; lot = substring search.</summary>
    public async Task<List<LogRow>> LogsAsync(DateOnly from, DateOnly to, string source, string staffUid, string lot, int limit)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new MySqlCommand("""
            SELECT p.printed_at, p.staff_name, p.source, p.working_code, p.drug_name, p.drug_type, p.unit, p.qty_per_pack,
                   p.lot_no, p.pack_date, p.src_exp_date, p.label_exp_date, p.pages, p.stickers, p.work_factor,
                   p.work_points, p.machine_name
            FROM print_logs p JOIN staff s ON s.id = p.staff_id
            WHERE p.pack_date BETWEEN @from AND @to AND (@src = '' OR p.source = @src)
              AND (@staff = '' OR s.uid = @staff) AND (@lot = '' OR p.lot_no LIKE @lotlike)
            ORDER BY p.printed_at DESC, p.id DESC
            LIMIT @n
            """, conn);
        AddRange(cmd, from, to, source);
        var lotTrim = lot.Trim();
        cmd.Parameters.AddWithValue("@staff", staffUid);
        cmd.Parameters.AddWithValue("@lot", lotTrim);
        cmd.Parameters.AddWithValue("@lotlike", "%" + EscapeLike(lotTrim) + "%");
        cmd.Parameters.AddWithValue("@n", limit);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<LogRow>();
        while (await r.ReadAsync())
        {
            list.Add(new LogRow(r.GetDateTime(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetDecimal(7),
                r.GetString(8), DateOnly.FromDateTime(r.GetDateTime(9)),
                r.IsDBNull(10) ? null : DateOnly.FromDateTime(r.GetDateTime(10)),
                DateOnly.FromDateTime(r.GetDateTime(11)), Convert.ToInt32(r.GetValue(12)),
                Convert.ToInt32(r.GetValue(13)), r.GetDecimal(14), r.GetDecimal(15), r.GetString(16), true));
        }
        return list;
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
