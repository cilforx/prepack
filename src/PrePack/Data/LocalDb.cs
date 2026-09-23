using System.Globalization;
using Microsoft.Data.Sqlite;
using PrePack.Domain;

namespace PrePack.Data;

/// <summary>
/// Local SQLite database (%APPDATA%\PrePack\prepack-local.db). Every write happens here first, so the
/// app works before MySQL is set up and while it is down. After a sync the rows stay as this machine's backup.
/// Dates are stored as invariant ISO text (th-TH would otherwise write Buddhist years).
/// </summary>
public sealed class LocalDb
{
    private const int SchemaVersion = 2;
    private const string DateFmt = "yyyy-MM-dd";
    private const string TimeFmt = "yyyy-MM-ddTHH:mm:ss";
    private const string UtcFmt = "yyyy-MM-ddTHH:mm:ss.fffZ";
    private const string FactorsVersionKey = "factors_version";

    private readonly string _connectionString;

    public LocalDb(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 30,
        }.ToString();
        Migrate();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private void Migrate()
    {
        using var conn = Open();
        Exec(conn, "PRAGMA journal_mode = WAL");
        var version = Convert.ToInt32(Scalar(conn, "PRAGMA user_version"));
        if (version >= SchemaVersion) return;

        // Each step runs once, in order; never edit a step that has shipped — add the next one.
        using var tx = conn.BeginTransaction();
        if (version < 1) MigrateV1(conn, tx);
        if (version < 2)
        {
            Exec(conn, """
                CREATE TABLE IF NOT EXISTS drug_labels (
                  drug_key    TEXT PRIMARY KEY,
                  drug_name   TEXT NOT NULL,
                  label_name  TEXT NOT NULL,
                  updated_at  TEXT NOT NULL,
                  dirty       INTEGER NOT NULL DEFAULT 1
                );
                """, tx);
        }
        Exec(conn, $"PRAGMA user_version = {SchemaVersion}", tx);
        tx.Commit();
    }

    private static void MigrateV1(SqliteConnection conn, SqliteTransaction tx)
    {
        Exec(conn, """
            CREATE TABLE IF NOT EXISTS meta (k TEXT PRIMARY KEY, v TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS staff (
              uid         TEXT PRIMARY KEY,
              name        TEXT NOT NULL,
              short_name  TEXT NOT NULL,
              active      INTEGER NOT NULL DEFAULT 1,
              updated_at  TEXT NOT NULL,
              dirty       INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS print_logs (
              client_uid     TEXT PRIMARY KEY,
              staff_uid      TEXT NOT NULL,
              staff_name     TEXT NOT NULL,
              source         TEXT NOT NULL,
              working_code   TEXT NULL,
              drug_name      TEXT NOT NULL,
              drug_type      TEXT NOT NULL,
              unit           TEXT NOT NULL,
              qty_per_pack   NUMERIC NOT NULL,
              lot_no         TEXT NOT NULL,
              pack_date      TEXT NOT NULL,
              src_exp_date   TEXT NULL,
              label_exp_date TEXT NOT NULL,
              pages          INTEGER NOT NULL,
              stickers       INTEGER NOT NULL,
              work_factor    NUMERIC NOT NULL,
              work_points    NUMERIC NOT NULL,
              printed_at     TEXT NOT NULL,
              machine_name   TEXT NOT NULL,
              synced_at      TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_logs_date ON print_logs (pack_date);
            CREATE INDEX IF NOT EXISTS idx_logs_unsynced ON print_logs (synced_at, printed_at);
            CREATE TABLE IF NOT EXISTS work_factors (
              drug_type TEXT NOT NULL,
              max_qty   NUMERIC NULL,
              factor    NUMERIC NOT NULL
            );
            """, tx);
        if (Convert.ToInt64(Scalar(conn, "SELECT COUNT(*) FROM work_factors", tx)) == 0)
            WriteFactors(conn, tx, WorkFactors.Defaults, SyncRules.Epoch);
    }

    // ── Drug labels (short sticker names) ──

    public DrugLabel? GetDrugLabel(string key)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, "SELECT drug_key, drug_name, label_name FROM drug_labels WHERE drug_key = @k");
        cmd.Parameters.AddWithValue("@k", key);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DrugLabel(r.GetString(0), r.GetString(1), r.GetString(2)) : null;
    }

    /// <summary>User typed a label: store it and mark it for upload.</summary>
    public void SaveDrugLabel(DrugLabel label) => UpsertDrugLabel(label, NowUtcSeconds(), dirty: true);

    public void ApplyRemoteDrugLabel(DrugLabel label, DateTime updatedUtc) => UpsertDrugLabel(label, updatedUtc, dirty: false);

    public List<DrugLabelSync> ListDrugLabelsSync()
    {
        using var conn = Open();
        using var cmd = Cmd(conn, "SELECT drug_key, drug_name, label_name, updated_at, dirty FROM drug_labels");
        using var r = cmd.ExecuteReader();
        var list = new List<DrugLabelSync>();
        while (r.Read())
        {
            list.Add(new DrugLabelSync(new DrugLabel(r.GetString(0), r.GetString(1), r.GetString(2)),
                ParseUtc(r.GetString(3)), r.GetInt64(4) != 0));
        }
        return list;
    }

    public void MarkDrugLabelClean(string key, DateTime updatedUtc)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, "UPDATE drug_labels SET dirty = 0 WHERE drug_key = @k AND updated_at = @u");
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@u", FormatUtc(updatedUtc));
        cmd.ExecuteNonQuery();
    }

    private void UpsertDrugLabel(DrugLabel l, DateTime updatedUtc, bool dirty)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, """
            INSERT INTO drug_labels (drug_key, drug_name, label_name, updated_at, dirty) VALUES (@k, @n, @l, @u, @d)
            ON CONFLICT(drug_key) DO UPDATE SET drug_name = excluded.drug_name, label_name = excluded.label_name,
              updated_at = excluded.updated_at, dirty = excluded.dirty
            """);
        cmd.Parameters.AddWithValue("@k", l.Key);
        cmd.Parameters.AddWithValue("@n", l.DrugName);
        cmd.Parameters.AddWithValue("@l", l.LabelName);
        cmd.Parameters.AddWithValue("@u", FormatUtc(updatedUtc));
        cmd.Parameters.AddWithValue("@d", dirty ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    // ── Staff ──

    public List<Staff> ListStaff() => ListStaffSync().Select(s => s.Staff).ToList();

    public List<StaffSync> ListStaffSync()
    {
        using var conn = Open();
        using var cmd = Cmd(conn, "SELECT uid, name, short_name, active, updated_at, dirty FROM staff ORDER BY active DESC, name");
        using var r = cmd.ExecuteReader();
        var list = new List<StaffSync>();
        while (r.Read())
        {
            list.Add(new StaffSync(new Staff(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3) != 0),
                ParseUtc(r.GetString(4)), r.GetInt64(5) != 0));
        }
        return list;
    }

    /// <summary>Add (empty uid) or edit a staff member locally; marks it for upload.</summary>
    public Staff SaveStaff(Staff s)
    {
        var name = s.Name.Trim();
        var shortName = s.ShortName.Trim();
        if (shortName.Length == 0) shortName = name;
        if (name.Length == 0) throw new UserError("กรุณากรอกชื่อเจ้าหน้าที่");
        if (name.Length > 100) throw new UserError("ชื่อยาวเกิน 100 ตัวอักษร");
        if (shortName.Length > 30) throw new UserError("ชื่อบนฉลากยาวเกิน 30 ตัวอักษร");

        var saved = s with
        {
            Uid = s.Uid.Length > 0 ? s.Uid : Guid.NewGuid().ToString(),
            Name = name,
            ShortName = shortName,
            Active = s.Uid.Length == 0 || s.Active,
        };
        using var conn = Open();
        UpsertStaff(conn, null, saved, NowUtcSeconds(), dirty: true);
        return saved;
    }

    /// <summary>
    /// Adds the initial staff list if missing. The uid comes from the name, so every machine seeds the SAME
    /// person (no duplicates on the server). Stamped at the sync epoch: any edit made on the server wins.
    /// Existing rows are never touched, so a deactivated or renamed seed person stays that way.
    /// </summary>
    public int SeedStaff(IEnumerable<string> names)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var added = 0;
        foreach (var name in names)
        {
            using var cmd = Cmd(conn, """
                INSERT INTO staff (uid, name, short_name, active, updated_at, dirty) VALUES (@uid, @n, @n, 1, @u, 1)
                ON CONFLICT(uid) DO NOTHING
                """, tx);
            cmd.Parameters.AddWithValue("@uid", SeedUid(name));
            cmd.Parameters.AddWithValue("@n", name);
            cmd.Parameters.AddWithValue("@u", FormatUtc(SyncRules.Epoch));
            added += cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return added;
    }

    internal static string SeedUid(string name) =>
        new Guid(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("prepack-seed-staff:" + name.Trim()))).ToString();

    /// <summary>Takes a row from MySQL as-is (not dirty).</summary>
    public void ApplyRemoteStaff(Staff s, DateTime updatedUtc)
    {
        using var conn = Open();
        UpsertStaff(conn, null, s, updatedUtc, dirty: false);
    }

    /// <summary>Clears the dirty flag only if the row was not edited again while the upload ran.</summary>
    public void MarkStaffClean(string uid, DateTime updatedUtc)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, "UPDATE staff SET dirty = 0 WHERE uid = @uid AND updated_at = @u");
        cmd.Parameters.AddWithValue("@uid", uid);
        cmd.Parameters.AddWithValue("@u", FormatUtc(updatedUtc));
        cmd.ExecuteNonQuery();
    }

    private static void UpsertStaff(SqliteConnection conn, SqliteTransaction? tx, Staff s, DateTime updatedUtc, bool dirty)
    {
        using var cmd = Cmd(conn, """
            INSERT INTO staff (uid, name, short_name, active, updated_at, dirty) VALUES (@uid, @n, @s, @a, @u, @d)
            ON CONFLICT(uid) DO UPDATE SET name = excluded.name, short_name = excluded.short_name,
              active = excluded.active, updated_at = excluded.updated_at, dirty = excluded.dirty
            """, tx);
        cmd.Parameters.AddWithValue("@uid", s.Uid);
        cmd.Parameters.AddWithValue("@n", s.Name);
        cmd.Parameters.AddWithValue("@s", s.ShortName);
        cmd.Parameters.AddWithValue("@a", s.Active ? 1 : 0);
        cmd.Parameters.AddWithValue("@u", FormatUtc(updatedUtc));
        cmd.Parameters.AddWithValue("@d", dirty ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    // ── Print logs ──

    public void InsertLog(PrintLog l)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, """
            INSERT INTO print_logs (client_uid, staff_uid, staff_name, source, working_code, drug_name, drug_type, unit,
              qty_per_pack, lot_no, pack_date, src_exp_date, label_exp_date, pages, stickers, work_factor, work_points,
              printed_at, machine_name, synced_at)
            VALUES (@uid, @suid, @sname, @src, @code, @drug, @type, @unit, @qty, @lot, @pack, @srcexp, @exp,
              @pages, @stickers, @factor, @points, @at, @machine, NULL)
            """);
        cmd.Parameters.AddWithValue("@uid", l.ClientUid.ToString());
        cmd.Parameters.AddWithValue("@suid", l.StaffUid);
        cmd.Parameters.AddWithValue("@sname", l.StaffName);
        cmd.Parameters.AddWithValue("@src", l.Source);
        cmd.Parameters.AddWithValue("@code", (object?)l.WorkingCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@drug", l.DrugName);
        cmd.Parameters.AddWithValue("@type", l.DrugType);
        cmd.Parameters.AddWithValue("@unit", l.Unit);
        cmd.Parameters.AddWithValue("@qty", Num(l.QtyPerPack));
        cmd.Parameters.AddWithValue("@lot", l.LotNo);
        cmd.Parameters.AddWithValue("@pack", FormatDate(l.PackDate));
        cmd.Parameters.AddWithValue("@srcexp", l.SrcExpDate is { } d ? FormatDate(d) : DBNull.Value);
        cmd.Parameters.AddWithValue("@exp", FormatDate(l.LabelExpDate));
        cmd.Parameters.AddWithValue("@pages", l.Pages);
        cmd.Parameters.AddWithValue("@stickers", l.Stickers);
        cmd.Parameters.AddWithValue("@factor", Num(l.WorkFactor));
        cmd.Parameters.AddWithValue("@points", Num(l.WorkPoints));
        cmd.Parameters.AddWithValue("@at", l.PrintedAt.ToString(TimeFmt, CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@machine", l.MachineName);
        cmd.ExecuteNonQuery();
    }

    public List<PrintLog> UnsyncedLogs(int limit)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, """
            SELECT client_uid, staff_uid, staff_name, source, working_code, drug_name, drug_type, unit, qty_per_pack,
                   lot_no, pack_date, src_exp_date, label_exp_date, pages, stickers, work_factor, printed_at, machine_name
            FROM print_logs WHERE synced_at IS NULL ORDER BY printed_at LIMIT @n
            """);
        cmd.Parameters.AddWithValue("@n", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<PrintLog>();
        while (r.Read())
        {
            list.Add(new PrintLog
            {
                ClientUid = Guid.Parse(r.GetString(0)),
                StaffUid = r.GetString(1),
                StaffName = r.GetString(2),
                Source = r.GetString(3),
                WorkingCode = r.IsDBNull(4) ? null : r.GetString(4),
                DrugName = r.GetString(5),
                DrugType = r.GetString(6),
                Unit = r.GetString(7),
                QtyPerPack = Dec(r, 8),
                LotNo = r.GetString(9),
                PackDate = ParseDate(r.GetString(10)),
                SrcExpDate = r.IsDBNull(11) ? null : ParseDate(r.GetString(11)),
                LabelExpDate = ParseDate(r.GetString(12)),
                Pages = r.GetInt32(13),
                Stickers = r.GetInt32(14),
                WorkFactor = Dec(r, 15),
                PrintedAt = DateTime.ParseExact(r.GetString(16), TimeFmt, CultureInfo.InvariantCulture),
                MachineName = r.GetString(17),
            });
        }
        return list;
    }

    public void MarkSynced(Guid clientUid)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, "UPDATE print_logs SET synced_at = @t WHERE client_uid = @uid");
        cmd.Parameters.AddWithValue("@t", FormatUtc(DateTime.UtcNow));
        cmd.Parameters.AddWithValue("@uid", clientUid.ToString());
        cmd.ExecuteNonQuery();
    }

    public int UnsyncedCount()
    {
        using var conn = Open();
        return Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM print_logs WHERE synced_at IS NULL"));
    }

    /// <summary>Restore: queue every local row for upload again (safe — MySQL ignores duplicate client_uid).</summary>
    public int ResetSynced()
    {
        using var conn = Open();
        using var cmd = Cmd(conn, "UPDATE print_logs SET synced_at = NULL");
        var n = cmd.ExecuteNonQuery();
        using var staff = Cmd(conn, "UPDATE staff SET dirty = 1");
        staff.ExecuteNonQuery();
        return n;
    }

    /// <summary>Quantities this drug was packed in most often on this machine, for the shortcut buttons.</summary>
    public List<decimal> FrequentQty(string? workingCode, string drugName, int limit = 5)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, workingCode != null
            ? "SELECT qty_per_pack FROM print_logs WHERE working_code = @k GROUP BY qty_per_pack ORDER BY COUNT(*) DESC, qty_per_pack LIMIT @n"
            : "SELECT qty_per_pack FROM print_logs WHERE working_code IS NULL AND drug_name = @k GROUP BY qty_per_pack ORDER BY COUNT(*) DESC, qty_per_pack LIMIT @n");
        cmd.Parameters.AddWithValue("@k", workingCode ?? drugName);
        cmd.Parameters.AddWithValue("@n", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<decimal>();
        while (r.Read()) list.Add(Dec(r, 0));
        return list;
    }

    // ── Reports (this machine only) ──

    public List<WorkloadRow> Workload(DateOnly from, DateOnly to, string source)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, """
            SELECT staff_uid, MAX(staff_name), COUNT(*), SUM(pages), SUM(stickers),
                   SUM(CASE WHEN drug_type = 'tablet' THEN stickers ELSE 0 END),
                   SUM(CASE WHEN drug_type = 'cream'  THEN stickers ELSE 0 END),
                   SUM(CASE WHEN drug_type = 'liquid' THEN stickers ELSE 0 END),
                   SUM(CASE WHEN source = 'MANUAL' THEN 1 ELSE 0 END),
                   SUM(work_points)
            FROM print_logs
            WHERE pack_date BETWEEN @from AND @to AND (@src = '' OR source = @src)
            GROUP BY staff_uid
            ORDER BY SUM(work_points) DESC, SUM(stickers) DESC
            """);
        AddRange(cmd, from, to, source);
        using var r = cmd.ExecuteReader();
        var list = new List<WorkloadRow>();
        while (r.Read())
        {
            int I(int i) => Convert.ToInt32(r.GetValue(i), CultureInfo.InvariantCulture);
            list.Add(new WorkloadRow(r.GetString(0), r.GetString(1), I(2), I(3), I(4), I(5), I(6), I(7), I(8), Dec(r, 9)));
        }
        return list;
    }

    public List<LogRow> Logs(DateOnly from, DateOnly to, string source, string staffUid, string lot, int limit)
    {
        using var conn = Open();
        using var cmd = Cmd(conn, """
            SELECT printed_at, staff_name, source, working_code, drug_name, drug_type, unit, qty_per_pack, lot_no,
                   pack_date, src_exp_date, label_exp_date, pages, stickers, work_factor, work_points, machine_name, synced_at
            FROM print_logs
            WHERE pack_date BETWEEN @from AND @to AND (@src = '' OR source = @src)
              AND (@staff = '' OR staff_uid = @staff) AND (@lot = '' OR lot_no LIKE @lotlike ESCAPE '\')
            ORDER BY printed_at DESC
            LIMIT @n
            """);
        AddRange(cmd, from, to, source);
        var lotTrim = lot.Trim().ToUpperInvariant();
        cmd.Parameters.AddWithValue("@staff", staffUid);
        cmd.Parameters.AddWithValue("@lot", lotTrim);
        cmd.Parameters.AddWithValue("@lotlike", "%" + lotTrim.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%");
        cmd.Parameters.AddWithValue("@n", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<LogRow>();
        while (r.Read())
        {
            list.Add(new LogRow(DateTime.ParseExact(r.GetString(0), TimeFmt, CultureInfo.InvariantCulture),
                r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4), r.GetString(5),
                r.GetString(6), Dec(r, 7), r.GetString(8), ParseDate(r.GetString(9)),
                r.IsDBNull(10) ? null : ParseDate(r.GetString(10)), ParseDate(r.GetString(11)),
                r.GetInt32(12), r.GetInt32(13), Dec(r, 14), Dec(r, 15), r.GetString(16), !r.IsDBNull(17)));
        }
        return list;
    }

    // ── Workload factors ──

    public (List<WorkFactor> Table, DateTime VersionUtc) GetWorkFactors()
    {
        using var conn = Open();
        using var cmd = Cmd(conn, "SELECT drug_type, max_qty, factor FROM work_factors");
        using var r = cmd.ExecuteReader();
        var list = new List<WorkFactor>();
        while (r.Read()) list.Add(new WorkFactor(r.GetString(0), r.IsDBNull(1) ? null : Dec(r, 1), Dec(r, 2)));
        r.Close();
        var v = Scalar(conn, $"SELECT v FROM meta WHERE k = '{FactorsVersionKey}'") as string;
        return (WorkFactors.Ordered(list).ToList(), v is null ? SyncRules.Epoch : ParseUtc(v));
    }

    public void SaveWorkFactors(IReadOnlyCollection<WorkFactor> table, DateTime versionUtc)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        Exec(conn, "DELETE FROM work_factors", tx);
        WriteFactors(conn, tx, table, versionUtc);
        tx.Commit();
    }

    private static void WriteFactors(SqliteConnection conn, SqliteTransaction tx, IEnumerable<WorkFactor> table, DateTime versionUtc)
    {
        foreach (var f in table)
        {
            using var ins = Cmd(conn, "INSERT INTO work_factors (drug_type, max_qty, factor) VALUES (@t, @m, @f)", tx);
            ins.Parameters.AddWithValue("@t", f.DrugType);
            ins.Parameters.AddWithValue("@m", f.MaxQty is { } m ? Num(m) : DBNull.Value);
            ins.Parameters.AddWithValue("@f", Num(f.Factor));
            ins.ExecuteNonQuery();
        }
        using var meta = Cmd(conn, "INSERT INTO meta (k, v) VALUES (@k, @v) ON CONFLICT(k) DO UPDATE SET v = excluded.v", tx);
        meta.Parameters.AddWithValue("@k", FactorsVersionKey);
        meta.Parameters.AddWithValue("@v", FormatUtc(versionUtc));
        meta.ExecuteNonQuery();
    }

    // ── Helpers ──

    private static SqliteCommand Cmd(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = tx;
        return cmd;
    }

    private static void Exec(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = Cmd(conn, sql, tx);
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = Cmd(conn, sql, tx);
        return cmd.ExecuteScalar();
    }

    private static void AddRange(SqliteCommand cmd, DateOnly from, DateOnly to, string source)
    {
        cmd.Parameters.AddWithValue("@from", FormatDate(from));
        cmd.Parameters.AddWithValue("@to", FormatDate(to));
        cmd.Parameters.AddWithValue("@src", source);
    }

    /// <summary>MySQL DATETIME keeps whole seconds (and rounds), so sync timestamps are whole seconds on both sides.</summary>
    public static DateTime NowUtcSeconds()
    {
        var t = DateTime.UtcNow;
        return new DateTime(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    // Numbers go in as double (NUMERIC affinity) so SUM works; values are small money-free quantities.
    private static double Num(decimal d) => (double)d;

    private static decimal Dec(SqliteDataReader r, int i) =>
        Math.Round(Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture), 4);

    internal static string FormatDate(DateOnly d) => d.ToString(DateFmt, CultureInfo.InvariantCulture);

    internal static DateOnly ParseDate(string s) => DateOnly.ParseExact(s, DateFmt, CultureInfo.InvariantCulture);

    internal static string FormatUtc(DateTime t) => t.ToUniversalTime().ToString(UtcFmt, CultureInfo.InvariantCulture);

    internal static DateTime ParseUtc(string s) =>
        DateTime.ParseExact(s, UtcFmt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
