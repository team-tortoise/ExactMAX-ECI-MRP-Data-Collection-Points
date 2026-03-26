namespace MAXConnector.Interop;

/// <summary>
/// Creates instances of XMLWrapperClass from MaxUpdateXML.dll via COM activation.
///
/// CLSID {34E27738-7E0B-474F-AC7F-F767840B265B} is resolved through the
/// application manifest (app.manifest) using registration-free COM activation —
/// no regsvr32 required. MaxUpdateXML.dll must be in the same folder as the EXE.
///
/// If EfwPath is configured the EFW network share is added to the Windows DLL
/// search path so MaxUpdateXML.dll can find its own dependencies at runtime
/// (MaxOrdr2.dll, MaxTran2.dll, kwDatAcc.dll, EXACTRMEnc.dll, etc.).
///
/// RETURN VALUE CONVENTIONS (from Interop.MaxUpdateXML.dll metadata):
///   "Add*" methods that create records  → string: empty = success, non-empty = error
///   "Change*" / "Delete*" methods       → short: 1 = success, 0 = failure
///   ProcessTransXML                     → int:   1 = success, 0 = failure
/// </summary>
internal static class ComObjectFactory
{
    private static readonly Guid XmlWrapperClsid = new("34E27738-7E0B-474F-AC7F-F767840B265B");

    /// <summary>
    /// Instantiates XMLWrapperClass and returns it as <c>dynamic</c> so methods
    /// are dispatched via IDispatch (no vtable layout required).
    /// </summary>
    internal static dynamic CreateXmlWrapper(MaxConnectorConfig config)
    {
        DllSearchPath.AddSearchPaths(config.EfwPath ?? string.Empty, config.LicensePath ?? string.Empty);

        var type = Type.GetTypeFromCLSID(XmlWrapperClsid)
            ?? throw new MaxConnectorException(
                "XMLWrapperClass (MaxUpdateXML.dll) could not be located. " +
                "Ensure MaxUpdateXML.dll is in the same folder as the EXE and " +
                "the app.manifest contains the comClass entry for " +
                "{34E27738-7E0B-474F-AC7F-F767840B265B}.");

        return Activator.CreateInstance(type)
            ?? throw new MaxConnectorException("Activator.CreateInstance returned null for XMLWrapperClass.");
    }
}
