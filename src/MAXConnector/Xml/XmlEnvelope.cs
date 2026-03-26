using System.Xml.Linq;

namespace MAXConnector.Xml;

/// <summary>
/// Builds XML documents in the eMAXExact envelope required by all MAXUpdate DLL
/// functions (MAXUpdate Rev 5.6.10 §5).
///
/// Required root structure:
///   &lt;?xml version="1.0" encoding="utf-8"?&gt;
///   &lt;eMAXExact&gt;
///     &lt;{Table}_Table&gt;
///       &lt;{Table}&gt;
///         &lt;FIELD_NAME&gt;value&lt;/FIELD_NAME&gt;
///         ...
///       &lt;/{Table}&gt;
///     &lt;/{Table}_Table&gt;
///   &lt;/eMAXExact&gt;
///
/// FIELD VALUE RULES (§5.1):
///   Strings  — omit the element or use &lt;FIELD&gt;&lt;/FIELD&gt; for blank (never use &lt;FIELD/&gt;).
///   Dates    — "yyyy-MM-dd" except TNXDTE_39 which uses "MMddyy".
///   Numbers  — base-10 string representation only.
/// </summary>
public static class XmlEnvelope
{
    /// <summary>
    /// Build a single-record eMAXExact envelope.
    /// </summary>
    /// <param name="tableName">MAX table name (e.g. "SO_Master", "Shop_Order").</param>
    /// <param name="fields">Field name → value pairs for this record.</param>
    public static string Build(string tableName, IReadOnlyDictionary<string, string> fields)
    {
        var record = new XElement(tableName,
            fields.Select(f => new XElement(f.Key, f.Value)));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("eMAXExact",
                new XElement(tableName + "_Table", record)));

        return doc.ToString(SaveOptions.None);
    }

    /// <summary>
    /// Build a nested eMAXExact envelope where an inner record is embedded inside
    /// the outer record element (used by AddPurchaseOrderLineItemXML, §5.3).
    /// </summary>
    /// <param name="outerTable">Outer table name (e.g. "Purchase_Order_Code").</param>
    /// <param name="innerTable">Inner table name (e.g. "Order_Master").</param>
    /// <param name="outerFields">Fields for the outer record.</param>
    /// <param name="innerFields">Fields for the inner (nested) record.</param>
    public static string BuildNested(
        string outerTable,
        string innerTable,
        IReadOnlyDictionary<string, string> outerFields,
        IReadOnlyDictionary<string, string> innerFields)
    {
        var innerElement = new XElement(innerTable,
            innerFields.Select(f => new XElement(f.Key, f.Value)));

        var outerContent = new List<XElement> { innerElement };
        outerContent.AddRange(outerFields.Select(f => new XElement(f.Key, f.Value)));
        var outerElement = new XElement(outerTable, outerContent);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("eMAXExact",
                new XElement(outerTable + "_Table", outerElement)));

        return doc.ToString(SaveOptions.None);
    }

    /// <summary>
    /// Format a date for standard MAX XML fields: yyyy-MM-dd (§5.1.2).
    /// Use <see cref="FormatTransactionDate"/> for TNXDTE_39.
    /// </summary>
    public static string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd");

    /// <summary>
    /// Format a date for the transaction date field TNXDTE_39: yyMMdd.
    /// NOTE: §5.1.2 docs say "MMddyy" but all §19 XML examples use yyMMdd
    /// (e.g. 130716 = July 16 2013). MMddyy would produce month 13 = invalid.
    /// </summary>
    public static string FormatTransactionDate(DateTime date) => date.ToString("yyMMdd");

    /// <summary>
    /// Format a time for the transaction time field TNXTME_39: HHmmss (24-hour).
    /// </summary>
    public static string FormatTransactionTime(DateTime time) => time.ToString("HHmmss");

    /// <summary>
    /// Format a time for labor start/end time fields (STARTTIME_39, ENDTIME_39): HHmm (4-char 24-hour).
    /// The Time_Ticket table stores START_53 / ENDTME_53 as 4-char HHMM values.
    /// </summary>
    public static string FormatLaborTime(DateTime time) => time.ToString("HHmm");

    /// <summary>
    /// Return an empty-element value string for nullable MAX fields.
    /// Prefer this over null or whitespace — the DLL treats &lt;FIELD/&gt; as null
    /// but &lt;FIELD&gt;&lt;/FIELD&gt; as a blank string (§5.1.1).
    /// </summary>
    public static string Blank => string.Empty;
}
