namespace MAXConnector;

/// <summary>
/// Thrown when a MAXUpdate DLL call returns a failure code or when
/// the connector is misconfigured.
/// </summary>
public sealed class MaxConnectorException : Exception
{
    /// <summary>The raw error text returned by the DLL (may be empty).</summary>
    public string MaxErrorMessage { get; }

    public MaxConnectorException(string message, string maxErrorMessage = "")
        : base(message)
    {
        MaxErrorMessage = maxErrorMessage;
    }

    public MaxConnectorException(string message, Exception inner)
        : base(message, inner)
    {
        MaxErrorMessage = string.Empty;
    }
}
