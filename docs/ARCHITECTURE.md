# MAXConnector — Architecture Overview

> **Audience:** Developers, operations staff, and AI agents working with this codebase.  
> **Last Updated:** March 2026

---

## Purpose

MAXConnector is a shop-floor time entry system that bridges a web UI with the ExactMAX / ExactRM manufacturing ERP. Workers log labor time through a mobile-friendly web app; the system stages those entries and posts them to MAX as **Time Tickets**, which decrement the quantity remaining on shop orders and accumulate actual labor hours against job operations.

---

## System Diagram

```
[Browser / Shop Floor Tablet]
         |
         | HTTP (port 5000)
         v
[MAXConnector.WebApp]  ─── SQL (ADO.NET direct) ──▶ [ExactMAX SQL Server]
         |                                            Database: ExactMAXPowderRiver
         | COM interop (in-process)                   Host: 10.0.2.131
         v
[MaxUpdateXML.dll]  ──── Network ────────────────▶ [MAX EFW Share]
  (XMLWrapperClass)                                  \\100.80.129.14\c\EXACT\RMCLIENT\EFW
                                                     License: \\100.80.129.14\c\EXACT\RMServer\LIC
```

---

## Solution Structure

```
MAXConnector.sln
├── src/MAXConnector/              # Class library — DLL wrapper and XML building
│   ├── MaxConnectorConfig.cs      # Configuration POCO (paths, connection strings)
│   ├── MaxConnectorException.cs   # Exception type for DLL failures
│   ├── Interop/
│   │   ├── ComObjectFactory.cs    # COM activation via registration-free manifest
│   │   ├── DllSearchPath.cs       # Adds EFW network path to Windows DLL search path
│   │   ├── MaxOrder2Interop.cs    # MaxOrdr2.dll wrapper (read-only order queries)
│   │   └── MaxTran2Interop.cs     # MaxTran2.dll wrapper (transaction processing)
│   ├── Services/
│   │   ├── MaxOrderClient.cs      # Read-side: query shop orders / parts from MAX
│   │   └── MaxTransactionClient.cs  # Write-side: post transactions via ProcessTransXML
│   └── Xml/
│       └── XmlEnvelope.cs         # Build eMAXExact XML envelopes + date formatting
│
├── src/MAXConnector.WebApp/       # ASP.NET Core 8 Minimal API + static SPA
│   ├── Program.cs                 # All API routes (employees, orders, time entries)
│   ├── appsettings.json           # Production config (DB connection, paths)
│   ├── Data/
│   │   └── ShopOrderRepository.cs # SQL queries + staging table CRUD
│   └── wwwroot/
│       ├── index.html             # Single-page UI (vanilla JS, no framework)
│       ├── css/app.css
│       └── js/app.js              # All client-side logic
│
└── src/MAXConnector.Sample/       # Console test harness
```

---

## Technology Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 8, Windows (required for COM interop) |
| Web framework | ASP.NET Core Minimal API |
| Frontend | Vanilla HTML/CSS/JS, no build step required |
| Database reads | ADO.NET (`Microsoft.Data.SqlClient`), direct SQL queries |
| Database writes | MAX COM DLL (`MaxUpdateXML.dll`) via `ProcessTransXML` |
| COM activation | Registration-free (app.manifest), no `regsvr32` required |
| Platform | Windows only (COM, 32-bit DLL constraint) |

---

## Configuration (`appsettings.json`)

```json
"MAX": {
  "ConnectionString": "Provider=sqloledb;Data Source=10.0.2.131;database=ExactMAXPowderRiver;UID=sa;Pwd=...;",
  "SqlConnectionString": "Server=10.0.2.131;Database=ExactMAXPowderRiver;...",
  "CompanyName": "Powder River",
  "LicensePath": "\\\\100.80.129.14\\c\\EXACT\\RMServer\\LIC",
  "LogPath": "C:\\Logs\\MAXConnector",
  "EfwPath": "\\\\100.80.129.14\\c\\EXACT\\RMCLIENT\\EFW"
}
```

Two connection strings are needed:
- **`ConnectionString`** — OLE DB format used by the MAX DLL's `Initialize()` method.
- **`SqlConnectionString`** — ADO.NET format used for direct read queries from the app.

---

## Key Database Tables (ExactMAX SQL Server)

| Table | Purpose | Notes |
|---|---|---|
| `Order_Master` | Shop orders | `TYPE_10='MF'`, `STATUS_10`: 3=Released, 4=In Process |
| `Job_Progress` | Operations per order | `ORDNUM_14` contains order + suffix (e.g. `502368470000`) |
| `Part_Master` | Parts catalog | Joined to get `PMDES1_01` (description) |
| `Employee_Master` | Employee records | Valid employees: `ACCTYP_40='A'` |
| `Time_Ticket` | Posted labor records | Written by the DLL. `PSTCDE_53='Y'` = fully posted |
| `Employee_Work` | Open labor clock entries | Written/consumed by Login/Logout transactions |
| `ShopFloor_TimeEntry` | **Staging table (custom)** | Created by this project; not a MAX table |

### Staging Table: `ShopFloor_TimeEntry`

```sql
CREATE TABLE ShopFloor_TimeEntry (
    EntryId       INT IDENTITY PRIMARY KEY,
    OrderNum      NVARCHAR(20)   NOT NULL,
    OperationSeq  NVARCHAR(10)   NOT NULL,
    EmployeeId    NVARCHAR(20)   NOT NULL,
    WorkDate      DATE           NOT NULL,
    RunHours      FLOAT          NOT NULL DEFAULT 0,
    SetupHours    FLOAT          NOT NULL DEFAULT 0,
    QtyCompleted  FLOAT          NOT NULL DEFAULT 0,
    QtyScrap      FLOAT          NOT NULL DEFAULT 0,
    Notes         NVARCHAR(500)  NULL,
    CreatedAt     DATETIME       NOT NULL DEFAULT GETDATE(),
    PostedToMax   BIT            NOT NULL DEFAULT 0
);
```

This table acts as a buffer. Entries are created by the web UI, then submitted to MAX via the DLL. `PostedToMax` is set to `1` only after the DLL returns a success code.

---

## API Endpoints

| Method | Route | Description |
|---|---|---|
| `GET` | `/api/employees/{id}` | Validate employee ID; returns name and active status |
| `GET` | `/api/orders` | List open shop orders (optionally search by order/part/ref) |
| `GET` | `/api/orders/{orderNum}` | Get a single shop order |
| `GET` | `/api/orders/{orderNum}/operations` | Get routing operations for an order |
| `POST` | `/api/time-entry` | Save a time entry to staging (`ShopFloor_TimeEntry`) |
| `GET` | `/api/time-entries/{orderNum}` | Get all time entries for an order |
| `POST` | `/api/time-entries/{id}/post` | Post a staging entry to MAX via the DLL |
| `DELETE` | `/api/time-entries/{id}` | Delete an unposted staging entry |

All responses use `{ success: bool, message: string, ... }` JSON.

---

## UI Flow

```
Sign In (Employee ID)
    │
    ▼
Order List  ──[search]──▶  filtered results
    │
    ▼ (tap order)
Order Detail
  ├─ Order info (part, qty, due date)
  ├─ Operations list (each with progress bar and "Log Work" button)
  │      │
  │      ▼ (tap "Log Work")
  │   Log Work Modal
  │     [Cancel]  [Save]  [Save & Post]
  │      │                     │
  │      │                     ▼
  │      │              saves staging entry
  │      │              then immediately posts to MAX
  │      ▼
  │   saves staging entry only
  │
  └─ Time Entries section
       ├─ pending entries: [Post to MAX] [Delete] buttons
       └─ [Post All to MAX] header button
```

**Save vs Save & Post:**  
- **Save** — writes to `ShopFloor_TimeEntry` only. Entry appears as "Pending" and must be manually posted.  
- **Save & Post** — saves to staging, then immediately calls the MAX DLL. One tap, no second step.

---

## The MAX DLL Write Path

All writes to MAX go through a single call:

```
XMLWrapperClass.ProcessTransXML(ref string xml, ref string errMsg) → int
```

- Returns `1` = success, `0` = failure (check `errMsg`).
- The XML must follow the `eMAXExact` envelope format (see [MAX-DLL-Integration.md](MAX-DLL-Integration.md)).
- The DLL is invoked **in-process** via COM; it connects to the MAX SQL Server and EFW network share independently.
- `Initialize()` must be called on every new instance before any transaction.

---

## Logging and Diagnostics

| File | Purpose |
|---|---|
| `C:\Logs\MAXConnector\xmlwrapper.log` | DLL error log (written when `bErrRpt=true`) |
| `C:\Logs\MAXConnector\last_transaction.xml` | The last XML payload sent to the DLL — overwritten each call |

When debugging a failed transaction, always check `last_transaction.xml` first to confirm the exact XML that was sent, then cross-reference `xmlwrapper.log` for the DLL's internal error message.

---

## Running the Application

```powershell
# From workspace root
dotnet run --project src/MAXConnector.WebApp/MAXConnector.WebApp.csproj

# App listens on http://localhost:5000
```

Requirements:
- Windows OS (COM/32-bit DLL constraint)
- `MaxUpdateXML.dll` and `app.manifest` present in the output folder
- Network access to the EFW share (`\\100.80.129.14\c\EXACT\RMCLIENT\EFW`)
- Network access to the SQL Server (`10.0.2.131`)
