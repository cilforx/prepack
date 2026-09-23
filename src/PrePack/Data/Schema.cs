using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace PrePack.Data;

/// <summary>
/// Creates the PrePack database and applies embedded migrations (Data/Migrations/NNN_name.sql).
/// Only ever writes to PrePack's own database: it refuses a target that looks like HOSxP or JHCIS.
/// </summary>
public static partial class Schema
{
    /// <summary>Tables that only exist in hospital HIS databases. Finding any of them means "wrong database".</summary>
    private static readonly string[] HisTables = ["patient", "ovst", "opitemrece", "person", "visit", "drugitems"];

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,63}$")]
    private static partial Regex DatabaseNameRegex();

    public static bool IsValidDatabaseName(string name) => DatabaseNameRegex().IsMatch(name);

    /// <summary>
    /// Ensures the database exists and is up to date. Returns the migrations that were applied.
    /// </summary>
    public static async Task<List<string>> EnsureAsync(string host, int port, string user, string password,
        string database, CancellationToken ct = default)
    {
        if (!IsValidDatabaseName(database))
            throw new UserError("ชื่อฐานข้อมูลใช้ได้เฉพาะ A-Z, 0-9 และ _ (ห้ามขึ้นต้นด้วยตัวเลข)");

        var server = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            UserID = user,
            Password = password,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = 10,
        };

        await using (var conn = new MySqlConnection(server.ConnectionString))
        {
            await OpenAsync(conn, ct);
            if (!await DatabaseExistsAsync(conn, database, ct))
            {
                try
                {
                    await ExecAsync(conn,
                        $"CREATE DATABASE IF NOT EXISTS `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci", ct);
                }
                catch (MySqlException e) when (e.ErrorCode is MySqlErrorCode.DatabaseAccessDenied or MySqlErrorCode.AccessDenied)
                {
                    throw new UserError(
                        $"user '{user}' ไม่มีสิทธิ์สร้างฐานข้อมูล — ให้ผู้ดูแลระบบรัน:\n" +
                        $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;\n" +
                        $"GRANT ALL PRIVILEGES ON `{database}`.* TO '{user}'@'%';");
                }
            }
            await GuardNotHisAsync(conn, database, ct);
        }

        server.Database = database;
        await using var db = new MySqlConnection(server.ConnectionString);
        await OpenAsync(db, ct);
        var applied = await MigrateAsync(db, ct);
        await SeedWorkFactorsAsync(db, ct);
        return applied;
    }

    /// <summary>First run only: default workload bands (factor 1 = every sticker counts the same).</summary>
    private static async Task SeedWorkFactorsAsync(MySqlConnection conn, CancellationToken ct)
    {
        await using (var count = new MySqlCommand("SELECT COUNT(*) FROM work_factors", conn))
        {
            if (Convert.ToInt64(await count.ExecuteScalarAsync(ct)) > 0) return;
        }
        foreach (var f in Domain.WorkFactors.Defaults)
        {
            await using var ins = new MySqlCommand(
                "INSERT INTO work_factors (drug_type, max_qty, factor) VALUES (@t, @m, @f)", conn);
            ins.Parameters.AddWithValue("@t", f.DrugType);
            ins.Parameters.AddWithValue("@m", (object?)f.MaxQty ?? DBNull.Value);
            ins.Parameters.AddWithValue("@f", f.Factor);
            await ins.ExecuteNonQueryAsync(ct);
        }
    }

    private static async Task<List<string>> MigrateAsync(MySqlConnection conn, CancellationToken ct)
    {
        await ExecAsync(conn, """
            CREATE TABLE IF NOT EXISTS schema_migrations (
              version    VARCHAR(100) PRIMARY KEY,
              applied_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """, ct);

        var applied = new List<string>();
        foreach (var (version, body) in EmbeddedMigrations())
        {
            await using (var check = new MySqlCommand("SELECT COUNT(*) FROM schema_migrations WHERE version = @v", conn))
            {
                check.Parameters.AddWithValue("@v", version);
                if (Convert.ToInt64(await check.ExecuteScalarAsync(ct)) > 0) continue;
            }

            // MySQL DDL auto-commits, so statements run one by one rather than in a transaction.
            foreach (var stmt in SplitStatements(body))
            {
                try { await ExecAsync(conn, stmt, ct); }
                catch (MySqlException e) { throw new InvalidOperationException($"migration {version}: {e.Message}", e); }
            }

            await using var mark = new MySqlCommand("INSERT INTO schema_migrations (version) VALUES (@v)", conn);
            mark.Parameters.AddWithValue("@v", version);
            await mark.ExecuteNonQueryAsync(ct);
            applied.Add(version);
        }
        return applied;
    }

    internal static IEnumerable<(string Version, string Body)> EmbeddedMigrations()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames()
                     .Where(n => n.StartsWith("migrations/", StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
                     .Order(StringComparer.Ordinal))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s, Encoding.UTF8);
            yield return (name["migrations/".Length..^".sql".Length], r.ReadToEnd());
        }
    }

    /// <summary>Splits on ";" at line ends and drops "--" comment lines (same rules as v0.1).</summary>
    internal static List<string> SplitStatements(string sql)
    {
        var output = new List<string>();
        var cur = new StringBuilder();
        foreach (var line in sql.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal)) continue;
            cur.Append(line.TrimEnd('\r')).Append('\n');
            if (trimmed.EndsWith(';'))
            {
                output.Add(cur.ToString().Trim().TrimEnd(';'));
                cur.Clear();
            }
        }
        if (cur.ToString().Trim() is { Length: > 0 } rest) output.Add(rest);
        return output;
    }

    private static async Task<bool> DatabaseExistsAsync(MySqlConnection conn, string database, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = @db", conn);
        cmd.Parameters.AddWithValue("@db", database);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task GuardNotHisAsync(MySqlConnection conn, string database, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(
            $"SELECT TABLE_NAME FROM information_schema.TABLES WHERE TABLE_SCHEMA = @db AND TABLE_NAME IN ({string.Join(",", HisTables.Select((_, i) => "@t" + i))}) LIMIT 1",
            conn);
        cmd.Parameters.AddWithValue("@db", database);
        for (var i = 0; i < HisTables.Length; i++) cmd.Parameters.AddWithValue("@t" + i, HisTables[i]);
        if (await cmd.ExecuteScalarAsync(ct) is string table)
            throw new UserError(
                $"ฐานข้อมูล '{database}' มีตาราง '{table}' ซึ่งเป็นของ HOSxP/JHCIS — PrePack จะไม่เขียนลงฐานนี้ " +
                "ให้ตั้งชื่อฐานข้อมูลแยก เช่น prepack");
    }

    private static async Task OpenAsync(MySqlConnection conn, CancellationToken ct)
    {
        try { await conn.OpenAsync(ct); }
        catch (MySqlException e) { throw new UserError("เชื่อมต่อ MySQL ไม่ได้: " + e.Message); }
    }

    private static async Task ExecAsync(MySqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
