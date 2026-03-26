using MAXConnector;
using MAXConnector.Services;
using MAXConnector.WebApp.Data;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Load configuration
var maxConfig = new MaxConnectorConfig
{
    ConnectionString  = builder.Configuration["MAX:ConnectionString"] ?? "",
    CompanyName       = builder.Configuration["MAX:CompanyName"] ?? "",
    LicensePath       = builder.Configuration["MAX:LicensePath"] ?? "",
    LogPath           = builder.Configuration["MAX:LogPath"] ?? @"C:\Logs\MAXConnector",
    EnableErrorReport = false,
    EfwPath           = builder.Configuration["MAX:EfwPath"] ?? "",
};

var sqlConnectionString = builder.Configuration["MAX:SqlConnectionString"] ?? "";

// Register services
builder.Services.AddSingleton(maxConfig);
builder.Services.AddSingleton(new ShopOrderRepository(sqlConnectionString));

var app = builder.Build();

// Ensure log directory exists
Directory.CreateDirectory(maxConfig.LogPath);

app.UseStaticFiles();

// ── API: Employees ───────────────────────────────────────────────────────

app.MapGet("/api/employees/{id}", async (ShopOrderRepository repo, string id) =>
{
    var emp = await repo.GetEmployeeAsync(id);
    if (emp is null)
        return Results.NotFound(new { success = false, message = $"Employee '{id}' not found in MAX." });
    if (!emp.IsActive)
        return Results.Json(new { success = false, message = $"Employee '{id}' ({emp.FullName}) is not active in MAX. Only active employees (Account Type A) can log labor." }, statusCode: 403);
    return Results.Ok(emp);
});

// ── API: Shop Orders ──────────────────────────────────────────────────────

app.MapGet("/api/orders", async (ShopOrderRepository repo, [FromQuery] string? search) =>
{
    var orders = await repo.GetOpenShopOrdersAsync(search);
    return Results.Ok(orders);
});

app.MapGet("/api/orders/{orderNum}", async (ShopOrderRepository repo, string orderNum) =>
{
    var order = await repo.GetShopOrderAsync(orderNum);
    return order is null ? Results.NotFound() : Results.Ok(order);
});

app.MapGet("/api/orders/{orderNum}/operations", async (ShopOrderRepository repo, string orderNum) =>
{
    var ops = await repo.GetOperationsAsync(orderNum);
    return Results.Ok(ops);
});

// ── API: Time Entry (Manual Input) ───────────────────────────────────────

app.MapPost("/api/time-entry", async (ShopOrderRepository repo, [FromBody] TimeEntryRequest req) =>
{
    var entry = new TimeEntry
    {
        OrderNum     = req.OrderNum,
        OperationSeq = req.OperationSeq,
        EmployeeId   = req.EmployeeId,
        WorkDate     = req.WorkDate ?? DateTime.Today,
        RunHours     = req.RunHours,
        SetupHours   = req.SetupHours,
        QtyCompleted = req.QtyCompleted,
        QtyScrap     = req.QtyScrap,
        Notes        = req.Notes,
    };
    var id = await repo.InsertTimeEntryAsync(entry);
    return Results.Ok(new { success = true, entryId = id, message = "Time entry saved." });
});

app.MapGet("/api/time-entries/{orderNum}", async (ShopOrderRepository repo, string orderNum, [FromQuery] string? op) =>
{
    var entries = await repo.GetTimeEntriesAsync(orderNum, op);
    return Results.Ok(entries);
});

// ── API: Post a single time entry to MAX ─────────────────────────────────

app.MapPost("/api/time-entries/{id:int}/post", async (ShopOrderRepository repo, MaxConnectorConfig maxConfig, int id) =>
{
    var entry = await repo.GetTimeEntryAsync(id);
    if (entry is null) return Results.NotFound(new { success = false, message = "Entry not found." });
    if (entry.PostedToMax) return Results.Ok(new { success = true, message = "Already posted to MAX." });

    var client = new MaxTransactionClient(maxConfig);
    try
    {
        Console.WriteLine($"[POST] EntryId={id} Order={entry.OrderNum} Op={entry.OperationSeq} Emp={entry.EmployeeId}");
        client.Initialize();

        // P/C (§6.7.21, Type P/C) — post operation complete, advance MRP.
        var workDate = entry.WorkDate.Date <= DateTime.Today ? entry.WorkDate.Date : DateTime.Today;
        Console.WriteLine($"[POST] P/C workDate={workDate:d} run={entry.RunHours} setup={entry.SetupHours} qty={entry.QtyCompleted}");

        client.PostOperationCompletion(
            entry.OrderNum,
            entry.OperationSeq,
            transactionTime: workDate,
            quantity:        entry.QtyCompleted > 0 ? entry.QtyCompleted : 1,
            actualRunTime:   entry.RunHours,
            actualSetupTime: entry.SetupHours);

        await repo.MarkEntryPostedAsync(id);
        Console.WriteLine($"[POST] Success EntryId={id}");
        return Results.Ok(new { success = true, message = "Posted to MAX." });
    }
    catch (MaxConnectorException ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.MaxErrorMessage) ? ex.Message : $"{ex.Message} — {ex.MaxErrorMessage}";
        Console.WriteLine($"[POST] MaxConnectorException EntryId={id}: {detail}");
        return Results.Json(new { success = false, message = detail }, statusCode: 400);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[POST] Exception EntryId={id}: {ex}");
        return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
    }
});

app.MapDelete("/api/time-entries/{id:int}", async (ShopOrderRepository repo, int id) =>
{
    var entry = await repo.GetTimeEntryAsync(id);
    if (entry is null) return Results.NotFound(new { success = false, message = "Entry not found." });
    if (entry.PostedToMax) return Results.BadRequest(new { success = false, message = "Cannot delete a posted entry." });
    await repo.DeleteTimeEntryAsync(id);
    return Results.Ok(new { success = true });
});

// ── API: Workstation (clock-in / clock-out) ───────────────────────────────

// Validate employee and return any active session.
app.MapGet("/api/workstation/employee/{id}", async (ShopOrderRepository repo, string id) =>
{
    var emp = await repo.GetEmployeeAsync(id);
    if (emp is null)
        return Results.NotFound(new { success = false, message = $"Employee '{id}' not found." });
    if (!emp.IsActive)
        return Results.Json(new { success = false, message = $"Employee '{id}' is not active in MAX." }, statusCode: 403);

    var session = await repo.GetActiveSessionAsync(id);
    return Results.Ok(new { success = true, employee = emp, activeSession = session });
});

// ── API: Sessions ──────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

// Start a session (no MAX call). Inserts into ShopFloor_ActiveSession.
app.MapPost("/api/session/start", async (ShopOrderRepository repo, [FromBody] SessionStartRequest req) =>
{
    var emp = await repo.GetEmployeeAsync(req.EmployeeId);
    if (emp is null || !emp.IsActive)
        return Results.BadRequest(new { success = false, message = "Invalid or inactive employee." });

    var existing = await repo.GetActiveSessionAsync(req.EmployeeId);
    if (existing is not null)
        return Results.BadRequest(new { success = false, message = $"Already has an active session on order {existing.OrderNum} Op {existing.OperationSeq}." });

    var sessionId = await repo.StartSessionAsync(req.EmployeeId, req.OrderNum, req.OperationSeq, req.Shift ?? "1");
    var session   = await repo.GetActiveSessionByIdAsync(sessionId);
    Console.WriteLine($"[SESSION-START] Emp={req.EmployeeId} Order={req.OrderNum} Op={req.OperationSeq} SessionId={sessionId}");
    return Results.Ok(new { success = true, message = $"Session started on order {req.OrderNum} Op {req.OperationSeq}.", session });
});

// Post a session: send P/C to MAX and move to history.
app.MapPost("/api/session/post", async (ShopOrderRepository repo, MaxConnectorConfig maxConfig, [FromBody] SessionPostRequest req) =>
{
    if (req.QtyCompleted <= 0)
        return Results.BadRequest(new { success = false, message = "Pieces completed must be greater than zero." });

    var session = await repo.GetActiveSessionByIdAsync(req.SessionId);
    if (session is null)
        return Results.BadRequest(new { success = false, message = "Session not found." });

    var now          = DateTime.Now;
    var elapsedHours = Math.Floor((now - session.StartTime).TotalHours * 10000) / 10000;
    var setupHours   = session.OverrideSetupHours
                       ?? Math.Min(req.SetupHours, Math.Floor(elapsedHours * 0.5 * 10000) / 10000);
    var runHours     = session.OverrideRunHours
                       ?? Math.Floor((elapsedHours - setupHours) * 10000) / 10000;

    var client = new MaxTransactionClient(maxConfig);
    try
    {
        client.Initialize();
        client.PostOperationCompletion(
            session.OrderNum,
            session.OperationSeq,
            transactionTime: now,
            quantity:        req.QtyCompleted,
            actualRunTime:   runHours,
            actualSetupTime: setupHours,
            shift:           session.Shift);

        Console.WriteLine($"[SESSION-POST] SessionId={req.SessionId} Emp={session.EmployeeId} Order={session.OrderNum} Op={session.OperationSeq} elapsed={elapsedHours:F2}h run={runHours:F2}h setup={setupHours:F2}h qty={req.QtyCompleted} scrap={req.QtyScrap}");

        await repo.MoveSessionToHistoryAsync(
            req.SessionId, session.EmployeeId, session.OrderNum, session.OperationSeq, session.Shift,
            session.StartTime, runHours, setupHours, req.QtyCompleted, req.QtyScrap, abandoned: false);

        return Results.Ok(new { success = true, message = "Operation completed and posted to MRP.", elapsedHours = Math.Round(elapsedHours, 2) });
    }
    catch (MaxConnectorException ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.MaxErrorMessage) ? ex.Message : $"{ex.Message} — {ex.MaxErrorMessage}";
        Console.WriteLine($"[SESSION-POST] MaxConnectorException SessionId={req.SessionId}: {detail}");
        return Results.Json(new { success = false, message = detail }, statusCode: 400);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SESSION-POST] Exception SessionId={req.SessionId}: {ex}");
        return Results.Json(new { success = false, message = ex.Message }, statusCode: 500);
    }
});

// Abandon a session: move to history with Abandoned=1, no MAX call.
app.MapPost("/api/session/abandon", async (ShopOrderRepository repo, [FromBody] SessionAbandonRequest req) =>
{
    var session = await repo.GetActiveSessionByIdAsync(req.SessionId);
    if (session is null)
        return Results.BadRequest(new { success = false, message = "Session not found." });

    var elapsedHours = Math.Floor((DateTime.Now - session.StartTime).TotalHours * 10000) / 10000;
    await repo.MoveSessionToHistoryAsync(
        req.SessionId, session.EmployeeId, session.OrderNum, session.OperationSeq, session.Shift,
        session.StartTime, runHours: elapsedHours, setupHours: 0, qtyCompleted: 0, qtyScrap: 0, abandoned: true);

    Console.WriteLine($"[SESSION-ABANDON] SessionId={req.SessionId} Emp={session.EmployeeId} Order={session.OrderNum} Op={session.OperationSeq}");
    return Results.Ok(new { success = true, message = "Session abandoned." });
});

// Return all active sessions.
app.MapGet("/api/sessions/active", async (ShopOrderRepository repo) =>
{
    var sessions = await repo.GetAllActiveSessionsAsync();
    return Results.Ok(sessions);
});

// ── API: Material Handler (R/S receive) ───────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────────

app.MapPost("/api/material/receive", async (ShopOrderRepository repo, MaxConnectorConfig maxConfig, [FromBody] MaterialReceiveRequest req) =>
{
    if (req.QtyCompleted <= 0)
        return Results.BadRequest(new { success = false, message = "Quantity must be greater than zero." });

    var emp = await repo.GetEmployeeAsync(req.EmployeeId);
    if (emp is null || !emp.IsActive)
        return Results.BadRequest(new { success = false, message = "Invalid or inactive employee." });

    var client = new MaxTransactionClient(maxConfig);
    try
    {
        client.Initialize();
        var stockroom = string.IsNullOrEmpty(req.ReceiveToStock) ? null : req.ReceiveToStock;
        client.ReceiveShopOrder(req.OrderNum, req.QtyCompleted, DateTime.Now, receiveToStock: stockroom);
        var receivedToStock = stockroom ?? "default";
        Console.WriteLine($"[MATERIAL R/S] Emp={req.EmployeeId} Order={req.OrderNum} qty={req.QtyCompleted} stockroom={receivedToStock}");
        return Results.Ok(new { success = true, message = $"Received {req.QtyCompleted} pcs into stock.", receivedToStock });
    }
    catch (MaxConnectorException ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.MaxErrorMessage) ? ex.Message : $"{ex.Message} — {ex.MaxErrorMessage}";
        Console.WriteLine($"[MATERIAL R/S] Failed Emp={req.EmployeeId} Order={req.OrderNum}: {detail}");
        return Results.Json(new { success = false, message = detail }, statusCode: 400);
    }
});

// ── API: Team Lead (manual post time + P/C + R/S, no clock-in required) ──────

app.MapPost("/api/teamlead/post", async (ShopOrderRepository repo, MaxConnectorConfig maxConfig, [FromBody] TeamleadPostRequest req) =>
{
    if (req.QtyCompleted <= 0)
        return Results.BadRequest(new { success = false, message = "Pieces completed must be greater than zero." });
    if (req.RunHours <= 0)
        return Results.BadRequest(new { success = false, message = "Run hours must be greater than zero." });
    if (req.SetupHours >= req.RunHours)
        return Results.BadRequest(new { success = false, message = "Setup hours must be less than run hours." });

    var emp = await repo.GetEmployeeAsync(req.EmployeeId);
    if (emp is null || !emp.IsActive)
        return Results.BadRequest(new { success = false, message = $"Employee '{req.EmployeeId}' not found or inactive." });

    var workDate  = (req.WorkDate ?? DateTime.Today).Date;
    var startTime = workDate.AddHours(6);   // consistent start anchor for STARTTIME_39/ENDTIME_39
    var endTime   = startTime.AddHours(req.RunHours + req.SetupHours);
    var now       = DateTime.Now;

    var client = new MaxTransactionClient(maxConfig);
    var results = new List<string>();
    try
    {
        client.Initialize();

        // 1. Time Ticket (Type T — no active Login required)
        client.TimeTicketEntry(
            req.OrderNum, req.OperationSeq, req.EmployeeId,
            workDate, startTime, endTime,
            req.QtyCompleted, req.RunHours,
            actualSetupTime: req.SetupHours,
            scrapQty: req.QtyScrap);
        results.Add("time ticket posted");
        Console.WriteLine($"[TL T/] Emp={req.EmployeeId} Order={req.OrderNum} Op={req.OperationSeq} qty={req.QtyCompleted} run={req.RunHours}h setup={req.SetupHours}h date={workDate:d}");

        // 2. P/C — mark operation complete in MRP
        if (req.MarkComplete)
        {
            client.PostOperationCompletion(
                req.OrderNum, req.OperationSeq,
                transactionTime: now,
                quantity:        req.QtyCompleted,
                actualRunTime:   req.RunHours,
                actualSetupTime: req.SetupHours);
            results.Add("operation marked complete");
            Console.WriteLine($"[TL P/C] Order={req.OrderNum} Op={req.OperationSeq}");
        }

        // 3. R/S — receive finished parts into stock
        string? receivedToStock = null;
        if (req.MarkComplete && req.ReceiveToStock is not null)
        {
            var stockroom = string.IsNullOrEmpty(req.ReceiveToStock) ? null : req.ReceiveToStock;
            client.ReceiveShopOrder(req.OrderNum, req.QtyCompleted, now, receiveToStock: stockroom);
            receivedToStock = stockroom ?? "default";
            results.Add($"received to stock ({receivedToStock})");
            Console.WriteLine($"[TL R/S] Order={req.OrderNum} qty={req.QtyCompleted} stockroom={receivedToStock}");
        }

        var msg = string.Join(", ", results).ToUpperFirstChar() + ".";
        return Results.Ok(new { success = true, message = msg, receivedToStock });
    }
    catch (MaxConnectorException ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.MaxErrorMessage) ? ex.Message : $"{ex.Message} — {ex.MaxErrorMessage}";
        Console.WriteLine($"[TL POST] Failed Emp={req.EmployeeId} Order={req.OrderNum}: {detail}");
        return Results.Json(new { success = false, message = detail }, statusCode: 400);
    }
});

// Serve index.html for the root path
app.MapFallbackToFile("index.html");

app.Run();

// ── Request DTOs ─────────────────────────────────────────────────────────

record TimeEntryRequest(
    string OrderNum, string OperationSeq, string EmployeeId,
    double RunHours, double SetupHours, double QtyCompleted, double QtyScrap,
    DateTime? WorkDate, string? Notes);

record SessionStartRequest(string EmployeeId, string OrderNum, string OperationSeq, string? Shift);

record SessionPostRequest(int SessionId, double QtyCompleted, double SetupHours = 0, double QtyScrap = 0);

record SessionAbandonRequest(int SessionId);

record MaterialReceiveRequest(string EmployeeId, string OrderNum, double QtyCompleted, string? ReceiveToStock = null);

record TeamleadPostRequest(
    string EmployeeId, string OrderNum, string OperationSeq,
    double QtyCompleted, double RunHours, double SetupHours = 0, double QtyScrap = 0,
    DateTime? WorkDate = null, bool MarkComplete = false, string? ReceiveToStock = null);

// ── Helpers ──────────────────────────────────────────────────────────────────

static class StringExtensions
{
    public static string ToUpperFirstChar(this string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
