# MAX DLL Integration — Technical Reference

> **Audience:** Developers and AI agents implementing or debugging MAX ERP write operations.  
> **DLL:** `MaxUpdateXML.dll` — MAXUpdate Rev 5.6.10 (ECi Macola/MAX, LLC)  
> **Last Updated:** March 2026

---

## Overview

All writes to the ExactMAX / ExactRM database go through a legacy VB6 COM DLL (`MaxUpdateXML.dll`). The DLL exposes a single class — `XMLWrapperClass` — that accepts XML documents describing manufacturing transactions and applies them to the database.

Direct SQL `INSERT`/`UPDATE` is **not used** for manufacturing transactions. The DLL enforces business rules, updates related tables atomically, and writes audit records. Always use the DLL for any write that affects shop orders, labor, inventory, or time tickets.

---

## COM Activation

The DLL is activated via **registration-free COM** using an `app.manifest` file embedded in the EXE. No `regsvr32` is required.

- **CLSID:** `{34E27738-7E0B-474F-AC7F-F767840B265B}`
- **Class:** `XMLWrapperClass`
- The `app.manifest` must contain a `<comClass>` entry for this CLSID.
- `MaxUpdateXML.dll` must be in the **same directory as the EXE** at runtime.
- The DLL has additional dependencies (`MaxOrdr2.dll`, `MaxTran2.dll`, `kwDatAcc.dll`, `EXACTRMEnc.dll`, etc.) which must be reachable at runtime. These live on the EFW network share. The `DllSearchPath` helper adds that path to the Windows DLL search path before activation.

```csharp
// ComObjectFactory.cs
var type = Type.GetTypeFromCLSID(new Guid("34E27738-7E0B-474F-AC7F-F767840B265B"));
dynamic wrapper = Activator.CreateInstance(type);
```

---

## Initialization Sequence

Call these methods on every new `XMLWrapperClass` instance, in this order:

```csharp
// 1. Suppress VB6 popup dialogs BEFORE Initialize()
wrapper.SetVisualErrorReportingXML(0);

// 2. Connect to MAX
wrapper.Initialize(
    connectionString,  // OLE DB: "Provider=sqloledb;Data Source=...;database=...;UID=...;Pwd=...;"
    companyName,       // e.g. "Powder River"
    licensePath,       // e.g. "\\server\c\EXACT\RMServer\LIC"
    logPath,           // e.g. "C:\Logs\MAXConnector"
    true);             // bErrRpt = true: write errors to xmlwrapper.log
```

**Important:** `SetVisualErrorReportingXML(0)` suppresses VB6 `MsgBox` popups that would otherwise block the web server thread waiting for someone to click OK. If this is not called, hanging HTTP requests are the symptom.

**Do not reuse instances.** Create a new `MaxTransactionClient`, call `Initialize()`, post the transaction, then discard it. The DLL holds internal state per instance.

---

## XML Envelope Format

Every transaction is submitted as an `eMAXExact` XML document:

```xml
<?xml version="1.0" encoding="utf-8"?>
<eMAXExact>
  <MAX_Transaction_Table>
    <MAX_Transaction>
      <TYPE_39>T</TYPE_39>
      <SUBTYPE_39> </SUBTYPE_39>
      <ORDNUM_39>50236847</ORDNUM_39>
      <!-- ... more fields ... -->
    </MAX_Transaction>
  </MAX_Transaction_Table>
</eMAXExact>
```

Rules:
- The table name (`MAX_Transaction`) is passed to `XmlEnvelope.Build()`.
- Use `<FIELD></FIELD>` for blank string values. **Never** use `<FIELD/>` (self-closing) — the DLL treats these differently.
- Numbers are plain base-10 strings (`"1.5"`, not `"1,5"` or scientific notation).
- Field names are always uppercase with a `_39` suffix for transaction fields.

---

## `ProcessTransXML` — The Main Method

```csharp
int result = wrapper.ProcessTransXML(ref xml, ref errMsg);
// result: 1 = success, 0 = failure
// errMsg: DLL error text on failure (may be empty even on failure)
```

- If `result != 1`, throw `MaxConnectorException` with the `errMsg` as context.
- The last XML sent is written to `C:\Logs\MAXConnector\last_transaction.xml` before calling this method. Check this file first when debugging.
- The DLL error log is at `C:\Logs\MAXConnector\xmlwrapper.log`.

---

## Date and Time Formatting — CRITICAL

This is the most common source of bugs. MAX uses **three different date formats** across different fields.

| Field | Format | Example | Used For |
|---|---|---|---|
| `TNXDTE_39` | `yyMMdd` | `260325` = March 25, 2026 | Transaction date |
| `EXPDATE_39` | `yyMMdd` | `260325` = March 25, 2026 | Work/ticket date (Type T) |
| Most other date fields | `yyyy-MM-dd` | `2026-03-25` | Standard dates |
| `TNXTME_39` | `HHmmss` | `060000` = 6:00:00 AM | Transaction time |
| `STARTTIME_39`, `ENDTIME_39` | `HHmm` | `0600`, `0735` | Labor session times |

### The `yyMMdd` Rule

> **The MAXUpdate documentation (§5.1.2) says the format is `MMddyy` but this is WRONG.**  
> All working XML examples in §19 use `yyMMdd` (e.g., `"130716"` = July 16, 2013).  
> Using `MMddyy` produces dates like `"032526"` which parses as month=03, day=25, year=26 — but the DLL interprets this as month=25 which is invalid, returning "Transaction must have a valid date".

**Always use `yyMMdd` for `TNXDTE_39` and `EXPDATE_39`.**

```csharp
// XmlEnvelope.cs
public static string FormatTransactionDate(DateTime date) => date.ToString("yyMMdd");
public static string FormatTransactionTime(DateTime time) => time.ToString("HHmmss");
public static string FormatLaborTime(DateTime time) => time.ToString("HHmm");
public static string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd");
```

---

## Time Ticket Entry (Type T — §6.7.27)

This is the **working transaction type** for shop floor labor posting. It creates a time ticket directly — no open Login record in `Employee_Work` is required.

### Why Type T (not Login/Logout)

Three transaction approaches were investigated:

| Approach | Type | Result |
|---|---|---|
| Login + Logout | `L/L` + `L/O` | Requires open login state in `Employee_Work`; session management complexity |
| Post Time Ticket | `P/T` | Attempted; validation errors with field set |
| **Time Ticket (direct)** | **`T/ `** | **Working. No prior state needed. Use this.** |

Type T is the correct choice for web-based manual time entry where no real-time clock-in/clock-out is tracked.

### Required XML Fields

```xml
<TYPE_39>T</TYPE_39>
<SUBTYPE_39> </SUBTYPE_39>        <!-- MUST be a space " ", not empty "" — §3.0 -->
<ORDNUM_39>50236847</ORDNUM_39>   <!-- Shop order number, no suffix -->
<OPRSEQ_39>0010</OPRSEQ_39>       <!-- Operation sequence -->
<TNXDTE_39>260325</TNXDTE_39>     <!-- Transaction date: yyMMdd (today) -->
<TNXTME_39>060432</TNXTME_39>     <!-- Transaction time: HHmmss -->
<TNXQTY_39>1</TNXQTY_39>         <!-- Quantity completed (must be > 0) -->
<SHIFT_39>1</SHIFT_39>            <!-- Shift number: 1, 2, or 3 -->
<EMPID_39>1686</EMPID_39>         <!-- Employee ID (must have ACCTYP_40='A') -->
<TICKET_39></TICKET_39>           <!-- Blank — DB stores 6 spaces -->
<STARTTIME_39>0600</STARTTIME_39> <!-- Work session start: HHmm -->
<ENDTIME_39>0735</ENDTIME_39>     <!-- Work session end: HHmm -->
<RUNACT_39>1.25</RUNACT_39>       <!-- Actual run time in hours -->
<SETACT_39>0.0</SETACT_39>        <!-- Actual setup time in hours -->
<ASCRAP_39>0</ASCRAP_39>          <!-- Scrap quantity -->
<EXPDATE_39>260325</EXPDATE_39>   <!-- Actual WORK date (time ticket date): yyMMdd -->
```

### Fields NOT Used for Type T

Per the WTL (Working Transaction Logic) field matrix:
- `LINNUM_39` — NOT included (blank / not applicable for Type T)
- `DELNUM_39` — NOT included (blank / not applicable for Type T)

Including these fields causes validation failures with some transaction types. Omit them entirely for Type T.

### SUBTYPE Must Be a Space

`SUBTYPE_39` for Type T must be the string `" "` (a single space character), not an empty string `""`. Per §3.0 of the MAXUpdate documentation:

> "If SubType is shown as blank, a blank space should be passed into the SubType ' ', not an empty string ''."

### How EXPDATE_39 vs TNXDTE_39 Work

| Field | Meaning | Value |
|---|---|---|
| `TNXDTE_39` | **When the transaction was posted** (today) | `DateTime.Now` in `yyMMdd` |
| `EXPDATE_39` | **When the work actually happened** (the time ticket date) | `entry.WorkDate` in `yyMMdd` |

These can be the same if work is posted same-day, or different for retroactive entries.

### The WebApp Post Logic

```csharp
var workDate  = entry.WorkDate.Date <= DateTime.Today ? entry.WorkDate.Date : DateTime.Today;
var startTime = workDate.AddHours(6);   // assumes 6 AM start
var endTime   = startTime.AddHours(entry.RunHours + entry.SetupHours);

client.TimeTicketEntry(
    entry.OrderNum,
    entry.OperationSeq,
    entry.EmployeeId,
    workDate,
    startTime,
    endTime,
    quantity: entry.QtyCompleted > 0 ? entry.QtyCompleted : 1,  // qty must be > 0
    actualRunTime: entry.RunHours,
    actualSetupTime: entry.SetupHours,
    scrapQty: entry.QtyScrap);
```

Start time defaults to 6:00 AM on the work date. End time is computed from total hours. If no quantity was completed, `1` is used (a quantity of `0` fails DLL validation).

---

## Employee Validation

Employees must have `ACCTYP_40 = 'A'` (Account Type = Active) in the `Employee_Master` table. Inactive employees will cause the DLL to reject the transaction.

The `/api/employees/{id}` endpoint validates this before allowing sign-in.

---

## Database Effects of a Successful Time Ticket

When Type T succeeds, the DLL writes to:

| Table | Effect |
|---|---|
| `Time_Ticket` | New row created: labor hours, start/end times, quantities, `PSTCDE_53` status |
| `Job_Progress` | `RUNACT_14` (actual run hours) incremented; `QTYCOM_14` (qty completed) incremented; `QTYREM_14` decremented |
| `Order_Master` | Quantity remaining updated; order status may advance |

The staging table `ShopFloor_TimeEntry.PostedToMax` is set to `1` by the application after the DLL returns success.

---

## Troubleshooting

### "Transaction must have a valid date"
- **Cause:** `TNXDTE_39` or `EXPDATE_39` in `MMddyy` format instead of `yyMMdd`.
- **Fix:** Verify `XmlEnvelope.FormatTransactionDate()` returns `yyMMdd`. Check `last_transaction.xml` — `TNXDTE_39=260325` is correct; `TNXDTE_39=032526` is wrong.

### COM object fails to activate
- **Cause:** `MaxUpdateXML.dll` is not in the EXE output directory, or the `app.manifest` is missing/incorrect.
- **Fix:** Check that the DLL and manifest are in `bin/Debug/net8.0-windows/`. The Sample project has a working manifest that can be copied.

### DLL call hangs
- **Cause:** VB6 popup dialog waiting for user interaction. `SetVisualErrorReportingXML(0)` was not called before `Initialize()`.
- **Fix:** Always call `SetVisualErrorReportingXML(0)` first.

### "Employee not found" / DLL rejects employee
- **Cause:** Employee `ACCTYP_40 != 'A'` or employee ID has trailing spaces.
- **Fix:** Query `SELECT RTRIM(EMPID_40), ACCTYP_40 FROM Employee_Master WHERE RTRIM(EMPID_40) = @Id`.

### No rows in Time_Ticket after success
- **Cause:** The app returned success but was running on an old build (the DLL wasn't actually called with the corrected payload). Always check `last_transaction.xml` timestamp to confirm the current build sent the transaction.
- **Fix:** Restart the app (`dotnet run`) and repost.

### Network / license errors
- **Cause:** EFW share (`\\100.80.129.14\c\EXACT\RMCLIENT\EFW`) or license path not reachable.
- **Fix:** Verify network connectivity from the server. The EFW path is set in `appsettings.json → MAX:EfwPath`.

---

## Other Transaction Types Available

`MaxTransactionClient` also has methods for these (not used by the web app currently):

| Method | Type/Sub | Description |
|---|---|---|
| `LaborLoginOrder` | `L/L` | Login employee to a shop order operation |
| `LaborLogoutOrder` | `L/O` | Logout employee with actual times and qty |
| `PostTimeTicket` | `P/T` | Retroactive time ticket (alternative to Type T) |
| `ReceiveShopOrder` | `R/S` | Receive completed goods from a shop order |

---

## Bolt-On Software Tables

Tables such as `BPT_*` and `MDCM_*` in the MAX database are used by a separate bolt-on software product that shares the same database. These tables are **not written by `MaxUpdateXML.dll`** and are not relevant to labor time entry. Do not attempt to write to them via the DLL.
