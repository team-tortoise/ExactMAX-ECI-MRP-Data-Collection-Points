using MAXConnector.Interop;
using MAXConnector.Xml;

namespace MAXConnector.Services;

/// <summary>
/// High-level wrapper around XMLWrapperClass (MaxUpdateXML.dll) for Sales Orders,
/// Purchase Orders, Shop Orders, and RMAs (MAXUpdate Rev 5.6.10 §4.3).
///
/// Usage:
///   var client = new MaxOrderClient(config);
///   client.Initialize();          // call once before any order operation
///   client.AddSalesOrder(fields);
/// </summary>
public sealed class MaxOrderClient
{
    private readonly MaxConnectorConfig _config;
    private dynamic? _wrapper;

    public MaxOrderClient(MaxConnectorConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Creates the XMLWrapperClass COM object and calls Initialize.
    /// Must be called before any other method.
    /// Visual error reporting (pop-up dialogs) is disabled automatically.
    /// </summary>
    public void Initialize()
    {
        _wrapper = ComObjectFactory.CreateXmlWrapper(_config);
        // Suppress VB6 popup dialogs BEFORE Initialize() runs — the DLL
        // can show error dialogs during license/connection setup.
        _wrapper.SetVisualErrorReportingXML(0);
        _wrapper.Initialize(
            _config.ConnectionString,
            _config.CompanyName,
            _config.LicensePath,
            _config.LogPath,
            _config.EnableErrorReport);
    }

    // ── Sales Orders ──────────────────────────────────────────────────

    /// <summary>Add a Sales Order header. Fields: SO_Master table (§5.2).</summary>
    public void AddSalesOrder(IReadOnlyDictionary<string, string> fields)
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("SO_Master", fields);
        string result = _wrapper!.AddSalesOrderXML(ref xml);
        ThrowIfStringError(result, nameof(AddSalesOrder));
    }

    /// <summary>Modify an existing Sales Order header.</summary>
    public void ChangeSalesOrder(IReadOnlyDictionary<string, string> fields)
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("SO_Master", fields);
        short result = _wrapper!.ChangeSalesOrderXML(ref xml);
        ThrowIfZero(result, nameof(ChangeSalesOrder));
    }

    /// <summary>Delete a Sales Order and all its line items.</summary>
    public void DeleteSalesOrder(string orderNum)
    {
        EnsureInitialized();
        short result = _wrapper!.DeleteSalesOrderXML(orderNum);
        ThrowIfZero(result, nameof(DeleteSalesOrder));
    }

    /// <summary>Add a Sales Order line item. Fields: SO_Detail table.</summary>
    public void AddSalesOrderLineItem(IReadOnlyDictionary<string, string> fields)
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("SO_Detail", fields);
        short result = _wrapper!.AddSalesOrderLineItemXML(ref xml);
        ThrowIfZero(result, nameof(AddSalesOrderLineItem));
    }

    /// <summary>Modify an existing Sales Order line item.</summary>
    public void ChangeSalesOrderLineItem(IReadOnlyDictionary<string, string> fields, string oldXml = "")
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("SO_Detail", fields);
        short result = _wrapper!.ChangeSalesOrderLineItemXML(ref xml, ref oldXml);
        ThrowIfZero(result, nameof(ChangeSalesOrderLineItem));
    }

    /// <summary>Delete a Sales Order line item.</summary>
    public void DeleteSalesOrderLineItem(string orderNum)
    {
        EnsureInitialized();
        short result = _wrapper!.DeleteSalesOrderLineItemXML(orderNum);
        ThrowIfZero(result, nameof(DeleteSalesOrderLineItem));
    }

    /// <summary>Convert a Quote-type Sales Order to an active order.</summary>
    public void ConvertQuote(string orderNum)
    {
        EnsureInitialized();
        bool suppressMsg = true;
        int result = _wrapper!.ConvertQuoteXML(orderNum, ref suppressMsg);
        ThrowIfZero((short)result, nameof(ConvertQuote));
    }

    // ── Purchase Orders ──────────────────────────────────────────────────

    /// <summary>
    /// Add a Purchase Order (header + line) in one call (§5.3).
    /// Returns the assigned PO number (empty string = failure).
    /// </summary>
    public string AddPurchaseOrder(IReadOnlyDictionary<string, string> fields,
        bool includeOrderRevision = false, bool createHeader = true)
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("Purchase_Order_Code", fields);
        string result = _wrapper!.AddPOXML(ref xml, includeOrderRevision, createHeader);
        if (string.IsNullOrEmpty(result))
            throw new MaxConnectorException($"{nameof(AddPurchaseOrder)} failed.");
        return result;
    }

    /// <summary>Change the heading of an existing Purchase Order.</summary>
    public void ChangePurchaseOrderHeading(IReadOnlyDictionary<string, string> fields)
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("Purchase_Order_Code", fields);
        short result = _wrapper!.ChangePOHeadingXML(ref xml);
        ThrowIfZero(result, nameof(ChangePurchaseOrderHeading));
    }

    /// <summary>Add a Purchase Order line item. Fields: Order_Master table.</summary>
    public void AddPurchaseOrderLineItem(IReadOnlyDictionary<string, string> fields,
        bool includeOrderRevision = false, bool createHeader = false)
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("Order_Master", fields);
        short result = _wrapper!.AddPurchaseOrderLineItemXML(ref xml, includeOrderRevision, createHeader);
        ThrowIfZero(result, nameof(AddPurchaseOrderLineItem));
    }

    /// <summary>Modify an existing Purchase Order line item.</summary>
    public void ChangePurchaseOrderLineItem(IReadOnlyDictionary<string, string> fields,
        bool includeOrderRevision = false)
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("Order_Master", fields);
        short result = _wrapper!.ChangePurchaseOrderLineItemXML(ref xml, includeOrderRevision);
        ThrowIfZero(result, nameof(ChangePurchaseOrderLineItem));
    }

    /// <summary>Delete a Purchase Order.</summary>
    public void DeletePurchaseOrder(string orderNum)
    {
        EnsureInitialized();
        short result = _wrapper!.DeletePurchaseOrderXML(orderNum);
        ThrowIfZero(result, nameof(DeletePurchaseOrder));
    }

    // ── Shop Orders ──────────────────────────────────────────────────────

    /// <summary>
    /// Add or update a Shop Order (§5.4).
    /// Returns the assigned order number. Fields: Shop_Order table.
    /// </summary>
    public string AddUpdateShopOrder(IReadOnlyDictionary<string, string> fields)
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("Shop_Order", fields);
        var ordNumOut = string.Empty;
        short result = _wrapper!.AddUpdateShopOrderXML(ref xml, ref ordNumOut);
        ThrowIfZero(result, nameof(AddUpdateShopOrder));
        return ordNumOut;
    }

    // ── RMA ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Add a Return Material Authorization.
    /// Returns the assigned RMA number. Fields: RMA_Master table.
    /// </summary>
    public string AddRma(IReadOnlyDictionary<string, string> fields)
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("RMA_Master", fields);
        var rmaNumOut = string.Empty;
        short result = _wrapper!.AddRMAXML(ref xml, ref rmaNumOut);
        ThrowIfZero(result, nameof(AddRma));
        return rmaNumOut;
    }

    /// <summary>Modify an existing RMA.</summary>
    public void ChangeRma(IReadOnlyDictionary<string, string> fields)
    {
        EnsureInitialized();
        var xml = XmlEnvelope.Build("RMA_Master", fields);
        short result = _wrapper!.ChangeRMAXML(ref xml);
        ThrowIfZero(result, nameof(ChangeRma));
    }

    /// <summary>Delete an RMA (pass RMANUM, RMALIN, RMADEL per §4.3).</summary>
    public void DeleteRma(string rmaNum, string rmaLin, string rmaDel)
    {
        EnsureInitialized();
        short result = _wrapper!.DeleteRMAXML(rmaNum, rmaLin, rmaDel);
        ThrowIfZero(result, nameof(DeleteRma));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureInitialized()
    {
        if (_wrapper is null)
            throw new InvalidOperationException(
                $"Call {nameof(Initialize)}() before using {nameof(MaxOrderClient)}.");
    }

    private static void ThrowIfZero(short result, string op)
    {
        if (result == 0)
            throw new MaxConnectorException(
                $"{nameof(MaxOrderClient)}.{op} failed (COM returned 0).");
    }

    private static void ThrowIfStringError(string result, string op)
    {
        if (!string.IsNullOrEmpty(result))
            throw new MaxConnectorException(
                $"{nameof(MaxOrderClient)}.{op} failed.", result);
    }
}
