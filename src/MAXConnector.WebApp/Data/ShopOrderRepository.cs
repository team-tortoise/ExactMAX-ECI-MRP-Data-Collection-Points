using Microsoft.Data.SqlClient;

namespace MAXConnector.WebApp.Data;

/// <summary>
/// Read-only queries against the ExactMAX database for shop orders and operations.
/// The MAXUpdate DLL is write-only, so we query SQL directly for display data.
/// </summary>
public sealed class ShopOrderRepository
{
    private readonly string _connectionString;

    public ShopOrderRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Get open shop orders (status 3 = Released, 4 = In Process).
    /// </summary>
    public async Task<List<ShopOrderSummary>> GetOpenShopOrdersAsync(string? searchTerm = null)
    {
        const string sql = """
            SELECT TOP 500
                   om.ORDNUM_10, om.PRTNUM_10, om.CURQTY_10, om.CURDUE_10,
                   om.STATUS_10, om.ORDREF_10, om.STK_10,
                   pm.PMDES1_01 AS PartDescription
            FROM Order_Master om
            LEFT JOIN Part_Master pm ON pm.PRTNUM_01 = om.PRTNUM_10
            WHERE om.TYPE_10 = 'MF'
              AND (
                    om.STATUS_10 = 3
                    OR (om.STATUS_10 = 4 AND om.CURDUE_10 >= DATEADD(month, -6, GETDATE()))
                  )
              AND (@Search IS NULL
                   OR om.ORDNUM_10 LIKE '%' + @Search + '%'
                   OR om.PRTNUM_10 LIKE '%' + @Search + '%'
                   OR om.ORDREF_10 LIKE '%' + @Search + '%')
            ORDER BY om.CURDUE_10 DESC
            """;

        var orders = new List<ShopOrderSummary>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Search", (object?)searchTerm ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            orders.Add(new ShopOrderSummary
            {
                OrderNum        = reader.GetString(reader.GetOrdinal("ORDNUM_10")).Trim(),
                PartNum         = reader.GetString(reader.GetOrdinal("PRTNUM_10")).Trim(),
                Quantity        = Convert.ToDouble(reader["CURQTY_10"]),
                DueDate         = reader.IsDBNull(reader.GetOrdinal("CURDUE_10")) ? null : reader.GetDateTime(reader.GetOrdinal("CURDUE_10")),
                Status          = Convert.ToInt32(reader["STATUS_10"]),
                OrderRef        = reader.IsDBNull(reader.GetOrdinal("ORDREF_10")) ? "" : reader.GetString(reader.GetOrdinal("ORDREF_10")).Trim(),
                Stockroom       = reader.IsDBNull(reader.GetOrdinal("STK_10")) ? "" : reader.GetString(reader.GetOrdinal("STK_10")).Trim(),
                PartDescription = reader.IsDBNull(reader.GetOrdinal("PartDescription")) ? "" : reader.GetString(reader.GetOrdinal("PartDescription")).Trim(),
            });
        }
        return orders;
    }

    /// <summary>
    /// Get operations (routing steps) for a given shop order.
    /// </summary>
    public async Task<List<ShopOrderOperation>> GetOperationsAsync(string orderNum)
    {
        const string sql = """
            SELECT jp.ORDNUM_14, jp.OPRSEQ_14, jp.OPRDES_14,
                   jp.WRKCTR_14, jp.QTYCOM_14, jp.ASCRAP_14,
                   jp.RUNTIM_14, jp.SETTIM_14,
                   jp.RUNACT_14, jp.SETACT_14,
                   jp.HLDCDE_14, jp.QTYREM_14,
                   jp.QUECDE_14
            FROM Job_Progress jp
            WHERE jp.ORDNUM_14 LIKE @OrderNum + '%'
            ORDER BY jp.OPRSEQ_14
            """;

        var ops = new List<ShopOrderOperation>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@OrderNum", orderNum);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ops.Add(new ShopOrderOperation
            {
                OrderNum       = reader.GetString(reader.GetOrdinal("ORDNUM_14")).Trim(),
                OperationSeq   = reader.GetString(reader.GetOrdinal("OPRSEQ_14")).Trim(),
                Description    = reader.IsDBNull(reader.GetOrdinal("OPRDES_14")) ? "" : reader.GetString(reader.GetOrdinal("OPRDES_14")).Trim(),
                WorkCenter     = reader.IsDBNull(reader.GetOrdinal("WRKCTR_14")) ? "" : reader.GetString(reader.GetOrdinal("WRKCTR_14")).Trim(),
                QtyCompleted   = Convert.ToDouble(reader["QTYCOM_14"]),
                QtyScrapped    = Convert.ToDouble(reader["ASCRAP_14"]),
                RunStandard    = Convert.ToDouble(reader["RUNTIM_14"]),
                SetupStandard  = Convert.ToDouble(reader["SETTIM_14"]),
                RunActual      = Convert.ToDouble(reader["RUNACT_14"]),
                SetupActual    = Convert.ToDouble(reader["SETACT_14"]),
                QueueCode      = reader.IsDBNull(reader.GetOrdinal("QUECDE_14")) ? "" : reader.GetString(reader.GetOrdinal("QUECDE_14")).Trim(),
            });
        }
        return ops;
    }

    /// <summary>
    /// Get a single shop order with its order quantity details.
    /// </summary>
    public async Task<ShopOrderSummary?> GetShopOrderAsync(string orderNum)
    {
        const string sql = """
            SELECT om.ORDNUM_10, om.PRTNUM_10, om.CURQTY_10, om.CURDUE_10,
                   om.STATUS_10, om.ORDREF_10, om.STK_10,
                   pm.PMDES1_01 AS PartDescription
            FROM Order_Master om
            LEFT JOIN Part_Master pm ON pm.PRTNUM_01 = om.PRTNUM_10
            WHERE om.TYPE_10 = 'MF' AND om.ORDNUM_10 = @OrderNum
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@OrderNum", orderNum);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new ShopOrderSummary
        {
            OrderNum        = reader.GetString(reader.GetOrdinal("ORDNUM_10")).Trim(),
            PartNum         = reader.GetString(reader.GetOrdinal("PRTNUM_10")).Trim(),
            Quantity        = Convert.ToDouble(reader["CURQTY_10"]),
            DueDate         = reader.IsDBNull(reader.GetOrdinal("CURDUE_10")) ? null : reader.GetDateTime(reader.GetOrdinal("CURDUE_10")),
            Status          = Convert.ToInt32(reader["STATUS_10"]),
            OrderRef        = reader.IsDBNull(reader.GetOrdinal("ORDREF_10")) ? "" : reader.GetString(reader.GetOrdinal("ORDREF_10")).Trim(),
            Stockroom       = reader.IsDBNull(reader.GetOrdinal("STK_10")) ? "" : reader.GetString(reader.GetOrdinal("STK_10")).Trim(),
            PartDescription = reader.IsDBNull(reader.GetOrdinal("PartDescription")) ? "" : reader.GetString(reader.GetOrdinal("PartDescription")).Trim(),
        };
    }

    // ── Time Entry Methods ──────────────────────────────────────────────

    public async Task<int> InsertTimeEntryAsync(TimeEntry entry)
    {
        const string sql = """
            INSERT INTO ShopFloor_TimeEntry
                (OrderNum, OperationSeq, EmployeeId, WorkDate, RunHours, SetupHours, QtyCompleted, QtyScrap, Notes)
            VALUES
                (@OrderNum, @OperationSeq, @EmployeeId, @WorkDate, @RunHours, @SetupHours, @QtyCompleted, @QtyScrap, @Notes);
            SELECT SCOPE_IDENTITY();
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@OrderNum", entry.OrderNum);
        cmd.Parameters.AddWithValue("@OperationSeq", entry.OperationSeq);
        cmd.Parameters.AddWithValue("@EmployeeId", entry.EmployeeId);
        cmd.Parameters.AddWithValue("@WorkDate", entry.WorkDate);
        cmd.Parameters.AddWithValue("@RunHours", entry.RunHours);
        cmd.Parameters.AddWithValue("@SetupHours", entry.SetupHours);
        cmd.Parameters.AddWithValue("@QtyCompleted", entry.QtyCompleted);
        cmd.Parameters.AddWithValue("@QtyScrap", entry.QtyScrap);
        cmd.Parameters.AddWithValue("@Notes", (object?)entry.Notes ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<List<TimeEntry>> GetTimeEntriesAsync(string orderNum, string? operationSeq = null)
    {
        var sql = """
            SELECT EntryId, OrderNum, OperationSeq, EmployeeId, WorkDate,
                   RunHours, SetupHours, QtyCompleted, QtyScrap, Notes, CreatedAt, PostedToMax
            FROM ShopFloor_TimeEntry
            WHERE OrderNum = @OrderNum
            """;
        if (operationSeq != null)
            sql += " AND OperationSeq = @OperationSeq";
        sql += " ORDER BY CreatedAt DESC";
        var entries = new List<TimeEntry>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@OrderNum", orderNum);
        if (operationSeq != null)
            cmd.Parameters.AddWithValue("@OperationSeq", operationSeq);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(new TimeEntry
            {
                EntryId      = reader.GetInt32(reader.GetOrdinal("EntryId")),
                OrderNum     = reader.GetString(reader.GetOrdinal("OrderNum")).Trim(),
                OperationSeq = reader.GetString(reader.GetOrdinal("OperationSeq")).Trim(),
                EmployeeId   = reader.GetString(reader.GetOrdinal("EmployeeId")).Trim(),
                WorkDate     = reader.GetDateTime(reader.GetOrdinal("WorkDate")),
                RunHours     = Convert.ToDouble(reader["RunHours"]),
                SetupHours   = Convert.ToDouble(reader["SetupHours"]),
                QtyCompleted = Convert.ToDouble(reader["QtyCompleted"]),
                QtyScrap     = Convert.ToDouble(reader["QtyScrap"]),
                Notes        = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
                CreatedAt    = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                PostedToMax  = reader.GetBoolean(reader.GetOrdinal("PostedToMax")),
            });
        }
        return entries;
    }

    public async Task<EmployeeInfo?> GetEmployeeAsync(string employeeId)
    {
        const string sql = """
            SELECT EMPID_40, LASTNM_40, FRSTNM_40, RTRIM(ACCTYP_40) AS ACCTYP_40
            FROM Employee_Master
            WHERE RTRIM(EMPID_40) = @EmpId
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EmpId", employeeId.Trim());

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new EmployeeInfo
        {
            EmployeeId = reader.GetString(reader.GetOrdinal("EMPID_40")).Trim(),
            LastName   = reader.IsDBNull(reader.GetOrdinal("LASTNM_40")) ? "" : reader.GetString(reader.GetOrdinal("LASTNM_40")).Trim(),
            FirstName  = reader.IsDBNull(reader.GetOrdinal("FRSTNM_40")) ? "" : reader.GetString(reader.GetOrdinal("FRSTNM_40")).Trim(),
            IsActive   = reader.GetString(reader.GetOrdinal("ACCTYP_40")) == "A",
        };
    }

    public async Task<TimeEntry?> GetTimeEntryAsync(int entryId)
    {
        const string sql = """
            SELECT EntryId, OrderNum, OperationSeq, EmployeeId, WorkDate,
                   RunHours, SetupHours, QtyCompleted, QtyScrap, Notes, CreatedAt, PostedToMax
            FROM ShopFloor_TimeEntry
            WHERE EntryId = @EntryId
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EntryId", entryId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;

        return new TimeEntry
        {
            EntryId      = reader.GetInt32(reader.GetOrdinal("EntryId")),
            OrderNum     = reader.GetString(reader.GetOrdinal("OrderNum")).Trim(),
            OperationSeq = reader.GetString(reader.GetOrdinal("OperationSeq")).Trim(),
            EmployeeId   = reader.GetString(reader.GetOrdinal("EmployeeId")).Trim(),
            WorkDate     = reader.GetDateTime(reader.GetOrdinal("WorkDate")),
            RunHours     = Convert.ToDouble(reader["RunHours"]),
            SetupHours   = Convert.ToDouble(reader["SetupHours"]),
            QtyCompleted = Convert.ToDouble(reader["QtyCompleted"]),
            QtyScrap     = Convert.ToDouble(reader["QtyScrap"]),
            Notes        = reader.IsDBNull(reader.GetOrdinal("Notes")) ? null : reader.GetString(reader.GetOrdinal("Notes")),
            CreatedAt    = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            PostedToMax  = reader.GetBoolean(reader.GetOrdinal("PostedToMax")),
        };
    }

    public async Task MarkEntryPostedAsync(int entryId)
    {
        const string sql = "UPDATE ShopFloor_TimeEntry SET PostedToMax = 1 WHERE EntryId = @EntryId";
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EntryId", entryId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteTimeEntryAsync(int entryId)
    {
        const string sql = "DELETE FROM ShopFloor_TimeEntry WHERE EntryId = @EntryId AND PostedToMax = 0";
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EntryId", entryId);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Session Methods ──────────────────────────────────────────────────────

    public async Task<int> StartSessionAsync(string employeeId, string orderNum, string operationSeq, string shift)
    {
        const string sql = """
            INSERT INTO ShopFloor_ActiveSession (EmployeeId, OrderNum, OperationSeq, Shift)
            VALUES (@EmployeeId, @OrderNum, @OperationSeq, @Shift);
            SELECT SCOPE_IDENTITY();
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EmployeeId",  employeeId);
        cmd.Parameters.AddWithValue("@OrderNum",    orderNum);
        cmd.Parameters.AddWithValue("@OperationSeq", operationSeq);
        cmd.Parameters.AddWithValue("@Shift",       shift);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    public async Task<ActiveSession?> GetActiveSessionAsync(string employeeId)
    {
        const string sql = """
            SELECT s.SessionId, s.EmployeeId, s.OrderNum, s.OperationSeq, s.Shift, s.StartTime,
                   s.OverrideRunHours, s.OverrideSetupHours,
                   om.PRTNUM_10 AS PartNum, pm.PMDES1_01 AS PartDescription,
                   jp.WRKCTR_14 AS WorkCenter,
                   em.FRSTNM_40 AS FirstName, em.LASTNM_40 AS LastName
            FROM ShopFloor_ActiveSession s
            LEFT JOIN Order_Master om ON om.ORDNUM_10 = s.OrderNum AND om.TYPE_10 = 'MF'
            LEFT JOIN Part_Master pm ON pm.PRTNUM_01 = om.PRTNUM_10
            LEFT JOIN Job_Progress jp ON jp.ORDNUM_14 LIKE s.OrderNum + '%'
                                     AND RTRIM(jp.OPRSEQ_14) = s.OperationSeq
            LEFT JOIN Employee_Master em ON RTRIM(em.EMPID_40) = s.EmployeeId
            WHERE RTRIM(s.EmployeeId) = @EmployeeId
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@EmployeeId", employeeId.Trim());
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapSession(r) : null;
    }

    public async Task<ActiveSession?> GetActiveSessionByIdAsync(int sessionId)
    {
        const string sql = """
            SELECT s.SessionId, s.EmployeeId, s.OrderNum, s.OperationSeq, s.Shift, s.StartTime,
                   s.OverrideRunHours, s.OverrideSetupHours,
                   om.PRTNUM_10 AS PartNum, pm.PMDES1_01 AS PartDescription,
                   jp.WRKCTR_14 AS WorkCenter,
                   em.FRSTNM_40 AS FirstName, em.LASTNM_40 AS LastName
            FROM ShopFloor_ActiveSession s
            LEFT JOIN Order_Master om ON om.ORDNUM_10 = s.OrderNum AND om.TYPE_10 = 'MF'
            LEFT JOIN Part_Master pm ON pm.PRTNUM_01 = om.PRTNUM_10
            LEFT JOIN Job_Progress jp ON jp.ORDNUM_14 LIKE s.OrderNum + '%'
                                     AND RTRIM(jp.OPRSEQ_14) = s.OperationSeq
            LEFT JOIN Employee_Master em ON RTRIM(em.EMPID_40) = s.EmployeeId
            WHERE s.SessionId = @SessionId
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? MapSession(r) : null;
    }

    public async Task<List<ActiveSession>> GetAllActiveSessionsAsync()
    {
        const string sql = """
            SELECT s.SessionId, s.EmployeeId, s.OrderNum, s.OperationSeq, s.Shift, s.StartTime,
                   s.OverrideRunHours, s.OverrideSetupHours,
                   om.PRTNUM_10 AS PartNum, pm.PMDES1_01 AS PartDescription,
                   jp.WRKCTR_14 AS WorkCenter,
                   em.FRSTNM_40 AS FirstName, em.LASTNM_40 AS LastName
            FROM ShopFloor_ActiveSession s
            LEFT JOIN Order_Master om ON om.ORDNUM_10 = s.OrderNum AND om.TYPE_10 = 'MF'
            LEFT JOIN Part_Master pm ON pm.PRTNUM_01 = om.PRTNUM_10
            LEFT JOIN Job_Progress jp ON jp.ORDNUM_14 LIKE s.OrderNum + '%'
                                     AND RTRIM(jp.OPRSEQ_14) = s.OperationSeq
            LEFT JOIN Employee_Master em ON RTRIM(em.EMPID_40) = s.EmployeeId
            ORDER BY s.StartTime
            """;

        var sessions = new List<ActiveSession>();
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            sessions.Add(MapSession(r));
        return sessions;
    }

    public async Task MoveSessionToHistoryAsync(
        int sessionId, string employeeId, string orderNum, string operationSeq, string shift,
        DateTime startTime, double runHours, double setupHours,
        double qtyCompleted, double qtyScrap, bool abandoned)
    {
        const string insertSql = """
            INSERT INTO ShopFloor_SessionHistory
                (SessionId, EmployeeId, OrderNum, OperationSeq, Shift, StartTime, EndTime,
                 RunHours, SetupHours, QtyCompleted, QtyScrap, Abandoned)
            VALUES
                (@SessionId, @EmployeeId, @OrderNum, @OperationSeq, @Shift, @StartTime, GETDATE(),
                 @RunHours, @SetupHours, @QtyCompleted, @QtyScrap, @Abandoned);
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();
        try
        {
            await using var cmd1 = new SqlCommand(insertSql, conn, tx);
            cmd1.Parameters.AddWithValue("@SessionId",    sessionId);
            cmd1.Parameters.AddWithValue("@EmployeeId",   employeeId);
            cmd1.Parameters.AddWithValue("@OrderNum",     orderNum);
            cmd1.Parameters.AddWithValue("@OperationSeq", operationSeq);
            cmd1.Parameters.AddWithValue("@Shift",        shift);
            cmd1.Parameters.AddWithValue("@StartTime",    startTime);
            cmd1.Parameters.AddWithValue("@RunHours",     runHours);
            cmd1.Parameters.AddWithValue("@SetupHours",   setupHours);
            cmd1.Parameters.AddWithValue("@QtyCompleted", qtyCompleted);
            cmd1.Parameters.AddWithValue("@QtyScrap",     qtyScrap);
            cmd1.Parameters.AddWithValue("@Abandoned",    abandoned);
            await cmd1.ExecuteNonQueryAsync();

            await using var cmd2 = new SqlCommand(
                "DELETE FROM ShopFloor_ActiveSession WHERE SessionId = @SessionId", conn, tx);
            cmd2.Parameters.AddWithValue("@SessionId", sessionId);
            await cmd2.ExecuteNonQueryAsync();

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateSessionOverrideAsync(int sessionId, double? overrideRunHours, double? overrideSetupHours)
    {
        const string sql = """
            UPDATE ShopFloor_ActiveSession
            SET OverrideRunHours = @OverrideRunHours, OverrideSetupHours = @OverrideSetupHours
            WHERE SessionId = @SessionId
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SessionId",          sessionId);
        cmd.Parameters.AddWithValue("@OverrideRunHours",   (object?)overrideRunHours   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@OverrideSetupHours", (object?)overrideSetupHours ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static ActiveSession MapSession(SqlDataReader r)
    {
        return new ActiveSession
        {
            SessionId          = r.GetInt32(r.GetOrdinal("SessionId")),
            EmployeeId         = r.GetString(r.GetOrdinal("EmployeeId")).Trim(),
            OrderNum           = r.GetString(r.GetOrdinal("OrderNum")).Trim(),
            OperationSeq       = r.GetString(r.GetOrdinal("OperationSeq")).Trim(),
            Shift              = r.GetString(r.GetOrdinal("Shift")).Trim(),
            StartTime          = r.GetDateTime(r.GetOrdinal("StartTime")),
            OverrideRunHours   = r.IsDBNull(r.GetOrdinal("OverrideRunHours"))   ? null : r.GetDouble(r.GetOrdinal("OverrideRunHours")),
            OverrideSetupHours = r.IsDBNull(r.GetOrdinal("OverrideSetupHours")) ? null : r.GetDouble(r.GetOrdinal("OverrideSetupHours")),
            PartNum            = r.IsDBNull(r.GetOrdinal("PartNum"))            ? "" : r.GetString(r.GetOrdinal("PartNum")).Trim(),
            PartDescription    = r.IsDBNull(r.GetOrdinal("PartDescription"))    ? "" : r.GetString(r.GetOrdinal("PartDescription")).Trim(),
            WorkCenter         = r.IsDBNull(r.GetOrdinal("WorkCenter"))         ? "" : r.GetString(r.GetOrdinal("WorkCenter")).Trim(),
            FullName           = BuildFullName(
                r.IsDBNull(r.GetOrdinal("FirstName")) ? "" : r.GetString(r.GetOrdinal("FirstName")).Trim(),
                r.IsDBNull(r.GetOrdinal("LastName"))  ? "" : r.GetString(r.GetOrdinal("LastName")).Trim()),
        };
    }

    private static string BuildFullName(string first, string last)
        => $"{first} {last}".Trim();
}

public class ShopOrderSummary
{
    public string OrderNum { get; set; } = "";
    public string PartNum { get; set; } = "";
    public double Quantity { get; set; }
    public DateTime? DueDate { get; set; }
    public int Status { get; set; }
    public string OrderRef { get; set; } = "";
    public string Stockroom { get; set; } = "";
    public string PartDescription { get; set; } = "";

    public string StatusText => Status switch
    {
        1 => "Planned",
        2 => "Quoted",
        3 => "Released",
        4 => "In Process",
        5 => "Complete",
        6 => "Closed",
        _ => $"Unknown ({Status})"
    };
}

public class ShopOrderOperation
{
    public string OrderNum { get; set; } = "";
    public string OperationSeq { get; set; } = "";
    public string Description { get; set; } = "";
    public string WorkCenter { get; set; } = "";
    public double QtyCompleted { get; set; }
    public double QtyScrapped { get; set; }
    public double RunStandard { get; set; }
    public double SetupStandard { get; set; }
    public double RunActual { get; set; }
    public double SetupActual { get; set; }
    /// <summary>Queue code from QUECDE_14: "Y" = active, "C" = complete, blank = queued.</summary>
    public string QueueCode { get; set; } = "";
}

public class EmployeeInfo
{
    public string EmployeeId { get; set; } = "";
    public string FirstName  { get; set; } = "";
    public string LastName   { get; set; } = "";
    public string FullName   => $"{FirstName} {LastName}".Trim();
    public bool   IsActive   { get; set; }
}

public class TimeEntry
{
    public int EntryId { get; set; }
    public string OrderNum { get; set; } = "";
    public string OperationSeq { get; set; } = "";
    public string EmployeeId { get; set; } = "";
    public DateTime WorkDate { get; set; }
    public double RunHours { get; set; }
    public double SetupHours { get; set; }
    public double QtyCompleted { get; set; }
    public double QtyScrap { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool PostedToMax { get; set; }
}

public class ActiveSession
{
    public int     SessionId          { get; set; }
    public string  EmployeeId         { get; set; } = "";
    public string  OrderNum           { get; set; } = "";
    public string  OperationSeq       { get; set; } = "";
    public string  Shift              { get; set; } = "1";
    public DateTime StartTime         { get; set; }
    public double? OverrideRunHours   { get; set; }
    public double? OverrideSetupHours { get; set; }
    // Enriched (from JOINs at query time)
    public string PartNum             { get; set; } = "";
    public string PartDescription     { get; set; } = "";
    public string WorkCenter          { get; set; } = "";
    public string FullName            { get; set; } = "";

    /// <summary>Seconds elapsed since session start, computed at serialization time.</summary>
    public int ElapsedSeconds => (int)Math.Max(0, (DateTime.Now - StartTime).TotalSeconds);
}
