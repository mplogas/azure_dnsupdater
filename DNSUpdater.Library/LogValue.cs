namespace DNSUpdater.Library;

/// <summary>
/// Makes a value from the request safe to put into a log message. Control characters such as line breaks
/// are percent-encoded, so a client can't forge log entries. Valid hostnames and IPv4 addresses stay unchanged.
/// </summary>
public static class LogValue
{
    public static string Escape(string? value) => Uri.EscapeDataString(value ?? string.Empty);
}
