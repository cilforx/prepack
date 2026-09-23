using Microsoft.Data.SqlClient;

namespace PrePack.Data;

public sealed record InvsDrug(string WorkingCode, string DrugName, string DrugNameTh, string Unit);

public sealed record InvsLot(string LotNo, DateOnly? ExpiryDate, decimal Qty);

/// <summary>
/// Read-only INVS (SQL Server) access. Every query is fixed here and parameterized —
/// the page can never send SQL. Only SELECT statements exist in this class.
/// </summary>
public sealed class InvsClient(string host, int port, string database, string user, string password, string unitColumn)
{
    private string ConnectionString => new SqlConnectionStringBuilder
    {
        DataSource = $"{host},{port}",
        InitialCatalog = database,
        UserID = user,
        Password = password,
        TrustServerCertificate = true, // older INVS servers fail TLS negotiation otherwise
        ConnectTimeout = 10,
        ApplicationIntent = ApplicationIntent.ReadOnly,
        ApplicationName = "PrePack",
    }.ConnectionString;

    private async Task<SqlConnection> OpenAsync()
    {
        var conn = new SqlConnection(ConnectionString);
        try
        {
            await conn.OpenAsync();
        }
        catch (SqlException e)
        {
            await conn.DisposeAsync();
            throw new UserError("เชื่อมต่อ INVS ไม่ได้: " + e.Message);
        }
        return conn;
    }

    public async Task TestAsync()
    {
        await using var conn = await OpenAsync();
    }

    /// <summary>DRUG_GN columns whose name suggests a unit / dosage form, for auto-detect and the settings dropdown.</summary>
    public async Task<List<string>> UnitColumnCandidatesAsync()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'DRUG_GN'
              AND (COLUMN_NAME LIKE '%UNIT%' OR COLUMN_NAME LIKE '%DOSAGE%' OR COLUMN_NAME LIKE '%FORM%')
            ORDER BY CASE WHEN COLUMN_NAME LIKE '%UNIT%' THEN 0 ELSE 1 END, ORDINAL_POSITION
            """, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    public async Task<List<InvsDrug>> SearchDrugsAsync(string keyword)
    {
        keyword = keyword.Trim();
        if (keyword.Length < 2) return [];

        await using var conn = await OpenAsync();
        // The unit column name cannot be a parameter, so it is only used after matching INFORMATION_SCHEMA.
        var unitExpr = "''";
        if (unitColumn.Length > 0 && (await UnitColumnsExistAsync(conn)).Contains(unitColumn, StringComparer.OrdinalIgnoreCase))
            unitExpr = $"RTRIM(ISNULL(CAST([{unitColumn.Replace("]", "]]")}] AS NVARCHAR(50)), ''))";

        await using var cmd = new SqlCommand($"""
            SELECT TOP 20 RTRIM(WORKING_CODE), RTRIM(DRUG_NAME), RTRIM(ISNULL(DRUG_NAME_TH, '')), {unitExpr}
            FROM DRUG_GN
            WHERE DRUG_NAME LIKE @kw OR DRUG_NAME_TH LIKE @kw OR WORKING_CODE = @code
            ORDER BY CASE WHEN WORKING_CODE = @code THEN 0 WHEN DRUG_NAME LIKE @prefix THEN 1 ELSE 2 END, DRUG_NAME
            """, conn) { CommandTimeout = 10 };
        var like = EscapeLike(keyword);
        cmd.Parameters.AddWithValue("@kw", "%" + like + "%");
        cmd.Parameters.AddWithValue("@prefix", like + "%");
        cmd.Parameters.AddWithValue("@code", keyword);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<InvsDrug>();
        while (await r.ReadAsync())
            list.Add(new InvsDrug(Str(r, 0), Str(r, 1), Str(r, 2), Str(r, 3)));
        return list;
    }

    /// <summary>Lots in stock that have not expired, earliest expiry first.</summary>
    public async Task<List<InvsLot>> LotsAsync(string workingCode, DateOnly today)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new SqlCommand("""
            SELECT RTRIM(LOT_NO), EXPIRED_DATE, SUM(QTY_ON_HAND)
            FROM INV_MD_C
            WHERE WORKING_CODE = @code AND QTY_ON_HAND > 0 AND EXPIRED_DATE >= @today
            GROUP BY LOT_NO, EXPIRED_DATE
            ORDER BY EXPIRED_DATE
            """, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("@code", workingCode);
        cmd.Parameters.AddWithValue("@today", Domain.Expiry.ToInvsDate(today));
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<InvsLot>();
        while (await r.ReadAsync())
        {
            var qty = r.IsDBNull(2) ? 0m : Convert.ToDecimal(r.GetValue(2));
            // Expected as text YYYYMMDD; if it turns out to be a datetime column, don't round-trip it
            // through a th-TH string (Buddhist year) — take the value directly.
            var exp = r.IsDBNull(1) ? null
                : r.GetValue(1) is DateTime dt ? DateOnly.FromDateTime(dt)
                : Domain.Expiry.ParseInvsDate(Str(r, 1));
            list.Add(new InvsLot(Str(r, 0), exp, qty));
        }
        return list;
    }

    private static async Task<List<string>> UnitColumnsExistAsync(SqlConnection conn)
    {
        await using var cmd = new SqlCommand(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DRUG_GN'", conn);
        await using var r = await cmd.ExecuteReaderAsync();
        var list = new List<string>();
        while (await r.ReadAsync()) list.Add(r.GetString(0));
        return list;
    }

    private static string Str(SqlDataReader r, int i) =>
        r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? "";

    private static string EscapeLike(string s) =>
        s.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
}
