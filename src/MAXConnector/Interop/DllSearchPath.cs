namespace MAXConnector.Interop;

/// <summary>
/// Prepends the MAX EFW network directory to the process PATH so that
/// MaxUpdateXML.dll (a VB6 COM server) can resolve its dependencies
/// (MAXORDR2.DLL, MAXTRAN2.DLL, kwDatAcc.dll, EXACTRMEnc.dll, etc.)
/// via the standard Windows LoadLibrary search order.
///
/// VB6 DLLs call LoadLibrary with bare filenames — they do NOT use
/// the AddDllDirectory / SetDefaultDllDirectories mechanism. The only
/// reliable way to ensure their lazy-loaded dependencies are found is
/// to add the EFW path to the process PATH environment variable.
///
/// Call <see cref="AddSearchPath"/> once before COM activation.
/// </summary>
internal static class DllSearchPath
{
    /// <summary>
    /// Prepends each path in <paramref name="paths"/> to the process PATH
    /// if not already present. Safe to call multiple times.
    /// </summary>
    internal static void AddSearchPaths(params string[] paths)
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var prefix = string.Empty;
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (current.Contains(p, StringComparison.OrdinalIgnoreCase)) continue;
            if (prefix.Contains(p, StringComparison.OrdinalIgnoreCase)) continue;
            prefix = string.IsNullOrEmpty(prefix) ? p : p + ";" + prefix;
        }
        if (!string.IsNullOrEmpty(prefix))
            Environment.SetEnvironmentVariable("PATH", prefix + ";" + current);
    }
}

