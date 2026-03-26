using MAXConnector.Interop;
using MAXConnector.Xml;

namespace MAXConnector.Services;

/// <summary>
/// Wraps XMLWrapperClass (MaxUpdateXML.dll) for all MAX inventory and
/// manufacturing transactions (MAXUpdate Rev 5.6.10 §4.4 / §6).
///
/// Supported transaction types (§6.4 / §19):
///   Receipt:    Purchase, Shop, RMA, Subcontract, Non-Inventory, Unplanned, Backflush
///   Issue:      Shop, Subcontract, Unplanned
///   Shipment:   Sales Order, Subcontract BOM
///   Transfer, Adjustment, Cycle-Count, Repetitive
///   Labor:      Login/Logout (Direct and Indirect)
///
/// Usage:
///   var client = new MaxTransactionClient(config);
///   client.Initialize();
///   client.ProcessTransaction(fields);
/// </summary>
public sealed class MaxTransactionClient
{
    private readonly MaxConnectorConfig _config;
    private dynamic? _wrapper;

    public MaxTransactionClient(MaxConnectorConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Creates the XMLWrapperClass COM object and calls Initialize.
    /// Must be called before ProcessTransaction.
    /// </summary>
    public void Initialize()
    {
        _wrapper = ComObjectFactory.CreateXmlWrapper(_config);
        // Suppress VB6 popup dialogs BEFORE Initialize() runs.
        _wrapper.SetVisualErrorReportingXML(0);
        _wrapper.Initialize(
            _config.ConnectionString,
            _config.CompanyName,
            _config.LicensePath,
            _config.LogPath,
            true); // always enable DLL error logging to xmlwrapper.log for diagnostics
    }

    /// <summary>
    /// Process a MAX inventory or manufacturing transaction.
    ///
    /// <paramref name="transactionFields"/> must contain all required fields for
    /// the transaction type. Required fields by type: §6.7.
    /// Working XML examples for each type: §19.
    ///
    /// Key fields in all transactions:
    ///   TRNTYP_39  — Transaction type code ("RCP", "ISS", "SHP", "TRN", "ADJ", ...)
    ///   TNXDTE_39  — Transaction date in MMddyy format (XmlEnvelope.FormatTransactionDate)
    ///   PRTNUM_39  — Part number
    ///   WARCOD_39  — Warehouse code
    /// </summary>
    public void ProcessTransaction(IReadOnlyDictionary<string, string> transactionFields)
    {
        EnsureInitialized();

        // Inject the system username so every resulting record is stamped in the audit columns.
        var fieldsWithUser = new Dictionary<string, string>(transactionFields)
        {
            ["USERNAME_39"] = _config.Username
        };

        var xml = XmlEnvelope.Build("MAX_Transaction", fieldsWithUser);

        // Dump sent XML to a debug file for diagnostics.
        try
        {
            var debugDir = _config.LogPath;
            if (!string.IsNullOrEmpty(debugDir))
            {
                var debugFile = System.IO.Path.Combine(debugDir, "last_transaction.xml");
                System.IO.File.WriteAllText(debugFile, xml);
            }
        }
        catch { /* never let debug logging break the transaction */ }

        var errMsg = string.Empty;
        int result = _wrapper!.ProcessTransXML(ref xml, ref errMsg);

        if (result != 1)
            throw new MaxConnectorException(
                $"{nameof(MaxTransactionClient)}.{nameof(ProcessTransaction)} failed (COM returned {result}).",
                errMsg);
    }

    // ── Labor (Job Progress / Time Card) ──────────────────────────────────

    /// <summary>
    /// Login an employee to a shop order operation (§19.18).
    /// Starts the labor clock for time-card tracking.
    /// </summary>
    public void LaborLoginOrder(
        string orderNum,
        string operationSeq,
        string employeeId,
        DateTime loginTime,
        string shift = "1",
        string lineNum = "00",
        string deliveryNum = "00")
    {
        var fields = new Dictionary<string, string>
        {
            ["TYPE_39"]    = "L",
            ["SUBTYPE_39"] = "L",
            ["ORDNUM_39"]  = orderNum,
            ["LINNUM_39"]  = lineNum,
            ["DELNUM_39"]  = deliveryNum,
            ["OPRSEQ_39"]  = operationSeq,
            ["TNXQTY_39"]  = "1",
            ["TNXDTE_39"]  = XmlEnvelope.FormatTransactionDate(loginTime),
            ["TNXTME_39"]  = XmlEnvelope.FormatTransactionTime(loginTime),
            ["SHIFT_39"]   = shift,
            ["EMPID_39"]   = employeeId,
        };
        ProcessTransaction(fields);
    }

    /// <summary>
    /// Logout an employee from a shop order operation (§19.20).
    /// Completes the time-card entry with run time, setup time, and quantity.
    /// </summary>
    /// <param name="orderNum">Shop order number.</param>
    /// <param name="operationSeq">Operation sequence.</param>
    /// <param name="employeeId">Employee ID.</param>
    /// <param name="logoutTime">Date/time of logout.</param>
    /// <param name="quantity">Pieces completed during this labor session.</param>
    /// <param name="actualRunTime">Actual run time in hours (e.g. 1.5 = 1h30m).</param>
    /// <param name="loginTime">Original login date/time (EXPDATE_39 = login date, TICKET_39 = login time per §6.7.15).</param>
    /// <param name="shift">Shift number ("1", "2", or "3"). Defaults to "1".</param>
    /// <param name="actualSetupTime">Actual setup time in hours. Defaults to 0.</param>
    /// <param name="scrapQty">Scrap quantity. Defaults to 0.</param>
    /// <param name="lineNum">Line number. Defaults to "00".</param>
    /// <param name="deliveryNum">Delivery number. Defaults to "00".</param>
    public void LaborLogoutOrder(
        string orderNum,
        string operationSeq,
        string employeeId,
        DateTime logoutTime,
        double quantity,
        double actualRunTime,
        DateTime loginTime,
        string shift = "1",
        double actualSetupTime = 0,
        double scrapQty = 0,
        string lineNum = "00",
        string deliveryNum = "00")
    {
        var fields = new Dictionary<string, string>
        {
            ["TYPE_39"]      = "L",
            ["SUBTYPE_39"]   = "O",
            ["ORDNUM_39"]    = orderNum,
            ["LINNUM_39"]    = lineNum,
            ["DELNUM_39"]    = deliveryNum,
            ["OPRSEQ_39"]    = operationSeq,
            ["TNXDTE_39"]    = XmlEnvelope.FormatTransactionDate(logoutTime),
            ["TNXTME_39"]    = XmlEnvelope.FormatTransactionTime(logoutTime),
            ["TNXQTY_39"]    = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SETUPTIME_39"] = "0",
            ["SHIFT_39"]     = shift,
            ["EMPID_39"]     = employeeId,
            ["ASCRAP_39"]    = scrapQty.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RUNACT_39"]    = actualRunTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SETACT_39"]    = actualSetupTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["EXPDATE_39"]   = XmlEnvelope.FormatTransactionDate(loginTime),
            ["TICKET_39"]    = XmlEnvelope.FormatTransactionTime(loginTime),
        };
        ProcessTransaction(fields);
    }

    // ── Labor (Post Time Ticket – direct, no Login/Logout) ──────────────────

    /// <summary>
    /// Post a completed time ticket directly to MAX (§6.7.17 / Type P, Sub T).
    /// Single-transaction approach for manual/retroactive time entry — does NOT
    /// require an open Login record in Employee_Work.
    /// </summary>
    /// <param name="orderNum">Shop order number (e.g. "50236847").</param>
    /// <param name="operationSeq">Operation sequence (e.g. "0010").</param>
    /// <param name="employeeId">Employee ID (must have ACCTYP_40='A').</param>
    /// <param name="transactionTime">Date/time of the work (login/start time).</param>
    /// <param name="quantity">Pieces completed (must be &gt; 0).</param>
    /// <param name="actualRunTime">Run time in hours.</param>
    /// <param name="actualSetupTime">Setup time in hours. Defaults to 0.</param>
    /// <param name="scrapQty">Scrap quantity. Defaults to 0.</param>
    /// <param name="shift">Shift number ("1", "2", or "3"). Defaults to "1".</param>
    /// <param name="lineNum">Line number. Defaults to "00".</param>
    /// <param name="deliveryNum">Delivery number. Defaults to "00".</param>
    public void PostTimeTicket(
        string orderNum,
        string operationSeq,
        string employeeId,
        DateTime transactionTime,
        double quantity,
        double actualRunTime,
        double actualSetupTime = 0,
        double scrapQty = 0,
        string shift = "1",
        string lineNum = "00",
        string deliveryNum = "00")
    {
        var startTime = transactionTime;
        var endTime   = startTime.AddHours(actualRunTime + actualSetupTime);

        var fields = new Dictionary<string, string>
        {
            ["TYPE_39"]      = "P",
            ["SUBTYPE_39"]   = "T",
            ["ORDNUM_39"]    = orderNum,
            ["LINNUM_39"]    = lineNum,
            ["DELNUM_39"]    = deliveryNum,
            ["OPRSEQ_39"]    = operationSeq,
            ["TNXDTE_39"]    = XmlEnvelope.FormatTransactionDate(transactionTime),
            ["TNXTME_39"]    = XmlEnvelope.FormatTransactionTime(transactionTime),
            ["TNXQTY_39"]    = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["EMPID_39"]     = employeeId,
            ["TICKET_39"]    = "0",
            ["STARTTIME_39"] = XmlEnvelope.FormatLaborTime(startTime),
            ["ENDTIME_39"]   = XmlEnvelope.FormatLaborTime(endTime),
            ["RUNACT_39"]    = actualRunTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SETACT_39"]    = actualSetupTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ASCRAP_39"]    = scrapQty.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SHIFT_39"]     = shift,
        };
        ProcessTransaction(fields);
    }

    // ── Time Ticket (direct entry, §6.7.27) ──────────────────────────────

    /// <summary>
    /// Creates a time ticket directly without requiring an open Login record (§6.7.27 / Type T).
    /// Use this for manual or retroactive time entry.
    /// EXPDATE_39 holds the actual work date; TNXDTE_39 is today's transaction date.
    /// SUBTYPE_39 must be a blank space ' ' per §3.0 ("If SubType is shown as blank,
    /// a blank space should be passed into the SubType ' ', not an empty string ''").
    /// </summary>
    /// <param name="orderNum">Shop order number (e.g. "50236847").</param>
    /// <param name="operationSeq">Operation sequence (e.g. "0010").</param>
    /// <param name="employeeId">Employee ID (must have ACCTYP_40='A').</param>
    /// <param name="workDate">The actual date of the work (used as the time ticket date).</param>
    /// <param name="startTime">Start time of the work session (HHmm format).</param>
    /// <param name="endTime">End time of the work session (HHmm format).</param>
    /// <param name="quantity">Pieces completed (must be &gt; 0).</param>
    /// <param name="actualRunTime">Actual run time in hours.</param>
    /// <param name="actualSetupTime">Actual setup time in hours. Defaults to 0.</param>
    /// <param name="scrapQty">Scrap quantity. Defaults to 0.</param>
    /// <param name="shift">Shift number ("1", "2", or "3"). Defaults to "1".</param>
    public void TimeTicketEntry(
        string orderNum,
        string operationSeq,
        string employeeId,
        DateTime workDate,
        DateTime startTime,
        DateTime endTime,
        double quantity,
        double actualRunTime,
        double actualSetupTime = 0,
        double scrapQty = 0,
        string shift = "1")
    {
        var now = DateTime.Now;
        var fields = new Dictionary<string, string>
        {
            ["TYPE_39"]      = "T",
            ["SUBTYPE_39"]   = " ",   // must be a space, not empty (§3.0)
            ["ORDNUM_39"]    = orderNum,
            // LINE and DELIVERY numbers are NOT USED for Type T (WTL matrix §1.5)
            ["OPRSEQ_39"]    = operationSeq,
            ["TNXDTE_39"]    = XmlEnvelope.FormatTransactionDate(now),
            ["TNXTME_39"]    = XmlEnvelope.FormatTransactionTime(now),
            ["TNXQTY_39"]    = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SHIFT_39"]     = shift,
            ["EMPID_39"]     = employeeId,
            ["TICKET_39"]    = "",    // blank — DB stores 6 spaces
            ["STARTTIME_39"] = XmlEnvelope.FormatLaborTime(startTime),
            ["ENDTIME_39"]   = XmlEnvelope.FormatLaborTime(endTime),
            ["RUNACT_39"]    = actualRunTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SETACT_39"]    = actualSetupTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ASCRAP_39"]    = scrapQty.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["EXPDATE_39"]   = XmlEnvelope.FormatTransactionDate(workDate), // yyMMdd — holds time ticket date (§6.7.27)
        };
        ProcessTransaction(fields);
    }

    // ── Post Operation Completion ─────────────────────────────────────────

    /// <summary>
    /// Post an operation completion to MRP (§6.7.16 / Type P, Sub C).
    /// This is a SEPARATE transaction from LaborLogoutOrder. The L/O records the
    /// time ticket; this one updates Job_Progress (QTYCOM_14, RUNACT_14, SETACT_14,
    /// QUECDE_14) and advances the shop order routing in MRP.
    /// Must be called AFTER LaborLogoutOrder for the same session.
    /// </summary>
    /// <param name="orderNum">Shop order number.</param>
    /// <param name="operationSeq">Operation sequence being completed.</param>
    /// <param name="transactionTime">Date/time of completion.</param>
    /// <param name="quantity">Pieces completed this session (must be &gt; 0).</param>
    /// <param name="actualRunTime">Actual run time in hours.</param>
    /// <param name="actualSetupTime">Actual setup time in hours. Defaults to 0.</param>
    /// <param name="shift">Shift number ("1", "2", or "3"). Defaults to "1".</param>
    /// <param name="nextOperationSeq">Optional next operation sequence. When omitted MAX uses its routing default.</param>
    public void PostOperationCompletion(
        string orderNum,
        string operationSeq,
        DateTime transactionTime,
        double quantity,
        double actualRunTime,
        double actualSetupTime = 0,
        string shift = "1",
        string? nextOperationSeq = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["TYPE_39"]    = "P",
            ["SUBTYPE_39"] = "C",
            ["ORDNUM_39"]  = orderNum,
            ["OPRSEQ_39"]  = operationSeq,
            ["TNXDTE_39"]  = XmlEnvelope.FormatTransactionDate(transactionTime),
            ["TNXTME_39"]  = XmlEnvelope.FormatTransactionTime(transactionTime),
            ["TNXQTY_39"]  = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SHIFT_39"]   = shift,
            ["RUNACT_39"]  = actualRunTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["SETACT_39"]  = actualSetupTime.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrEmpty(nextOperationSeq))
            fields["NXTOPR_39"] = nextOperationSeq;

        ProcessTransaction(fields);
    }

    // ── Receipt – Shop Order ──────────────────────────────────────────────

    /// <summary>
    /// Receive completed goods from a shop order into stock (§19.4 / §5.5.4).
    /// </summary>
    /// <param name="orderNum">Shop order number (e.g. "50007654").</param>
    /// <param name="quantity">Quantity to receive.</param>
    /// <param name="transactionTime">Date/time of the receipt.</param>
    /// <param name="receiveToStock">Receive-to stockroom. If blank, uses the part's default.</param>
    /// <param name="referenceDesc">Optional reference description.</param>
    /// <param name="lotNumber">Optional lot number (for lot-controlled parts).</param>
    /// <param name="serialNumber">Optional serial number (for serial-controlled parts).</param>
    /// <param name="serialAssignCode">Serial assignment code: "R"=range, "I"=individual, "A"=auto.</param>
    public void ReceiveShopOrder(
        string orderNum,
        double quantity,
        DateTime transactionTime,
        string? receiveToStock = null,
        string? referenceDesc = null,
        string? lotNumber = null,
        string? serialNumber = null,
        string? serialAssignCode = null)
    {
        // USERNAME_39 is injected by ProcessTransaction from _config.Username.
        var fields = new Dictionary<string, string>
        {
            ["TYPE_39"]    = "R",
            ["SUBTYPE_39"] = "S",
            ["ORDNUM_39"]  = orderNum,
            ["TNXQTY_39"]  = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TNXDTE_39"]  = XmlEnvelope.FormatTransactionDate(transactionTime),
            ["TNXTME_39"]  = XmlEnvelope.FormatTransactionTime(transactionTime),
        };

        if (!string.IsNullOrEmpty(receiveToStock))  fields["RCVSTK_39"]  = receiveToStock;
        if (!string.IsNullOrEmpty(referenceDesc))    fields["UDFREF_39"]  = referenceDesc;
        if (!string.IsNullOrEmpty(lotNumber))        fields["LOT_39"]     = lotNumber;
        if (!string.IsNullOrEmpty(serialNumber))     fields["SERIAL_39"]  = serialNumber;
        if (!string.IsNullOrEmpty(serialAssignCode)) fields["ASSCODE_39"] = serialAssignCode;

        ProcessTransaction(fields);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_wrapper is null)
            throw new InvalidOperationException(
                $"Call {nameof(Initialize)}() before using {nameof(MaxTransactionClient)}.");
    }
}
