namespace MAXConnector;

/// <summary>
/// Configuration passed to MaxOrderClient and MaxTransactionClient.
/// See MAXUpdate Rev 5.6.10 §4.1 for parameter descriptions.
/// </summary>
public sealed class MaxConnectorConfig
{
    /// <summary>
    /// OLE DB connection string pointing to the ExactMAX / ExactRM SQL database.
    /// Example: "Provider=sqloledb;Data Source=SQLSERVER;database=ExactRM;UID=sa;Pwd=pass;"
    /// Leave empty to use DLL registry defaults.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>MAX company name as it appears in the company list.</summary>
    public string CompanyName { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the ExactRM.lic license file.
    /// Default MAX path: C:\EXACT\RMCLIENT\EFW\ExactRM.lic
    /// Leave empty to use DLL registry defaults.
    /// </summary>
    public string LicensePath { get; set; } = string.Empty;

    /// <summary>Directory path where the connector log file will be written.</summary>
    public string LogPath { get; set; } = string.Empty;

    /// <summary>
    /// MAX user name stamped as USERNAME_39 on every transaction.
    /// Appears in the CreatedBy / ModifiedBy audit columns on resulting records.
    /// </summary>
    public string Username { get; set; } = "MINION2";

    /// <summary>When true, the DLL writes an error report file on failures.</summary>
    public bool EnableErrorReport { get; set; } = false;

    /// <summary>
    /// Full path to MaxDataSchema.xsd — required for ETL extract/load operations only.
    /// Included in MAX installation at C:\EXACT\RMCLIENT\EFW\MaxDataSchema.xsd.
    /// </summary>
    public string MaxDataSchemaPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to the MAX EFW directory containing all dependent DLLs
    /// (kwDatAcc.dll, EXACTRMEnc.dll, ERMRemCl.dll, etc.).
    /// This is added to the Windows DLL search path so those dependencies
    /// are found without having to copy them locally.
    /// Default MAX path: \\Maxdev\c\EXACT\RMCLIENT\EFW
    /// </summary>
    public string EfwPath { get; set; } = string.Empty;
}
