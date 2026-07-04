using Microsoft.Extensions.Logging;

namespace Backend.Util;

/// <summary>
/// Severity of a diagnostic entry produced by the backend.
/// </summary>
public enum DiagnosticSeverity
{
    Information,
    Warning,
    Error,
    Critical
}

/// <summary>
/// Broad area in which a diagnostic occured.
/// </summary>
public enum DiagnosticCategory
{
    Discovery,
    Connection,
    Initialization,
    Calibration,
    Measurement,
    DataProcessing,
    Unexpected
}

/// <summary>
/// Structured diagnostic information that can be displayed
/// or exported by the GUI
/// </summary>
public sealed record DiagnosticEntry(
    string Code,
    DiagnosticSeverity Severity,
    DiagnosticCategory Category,
    string Message,
    string? TechnicalDetails = null,
    string? Operation = null,
    string? Source = null,
    DateTimeOffset? Timestamp = null)
{
    public DateTimeOffset OccurredAt { get; } = Timestamp ?? DateTimeOffset.UtcNow;


    internal static void AddDiagnostic(List<DiagnosticEntry> diagnostics, string code, DiagnosticSeverity severity, DiagnosticCategory category,
        string message, string? technicalDetails = null, string? operation = null, string? source = null,
        Exception? exception = null, ILogger? logger = null)
    {
        DiagnosticEntry entry = new(
            Code: code,
            Severity: severity,
            Category: category,
            Message: message,
            TechnicalDetails: technicalDetails,
            Operation: operation,
            Source: source);
        ArgumentNullException.ThrowIfNull(entry);
        diagnostics.Add(entry);

        if (exception is not null)
        {
            logger?.LogError(exception, "{Code}: {Message}. Details: {Details}",
            code, message, technicalDetails);
        }
    }

    internal static void ClearDiagnostics(List<DiagnosticEntry> diagnostics, DiagnosticCategory category)
    {
        diagnostics.RemoveAll(entry => entry.Category == category);
    }

    internal static void ClearDiagnostics(List<DiagnosticEntry> diagnostics)
    {
        diagnostics.Clear();
    }

}