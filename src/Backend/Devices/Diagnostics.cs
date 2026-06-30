namespace Backend.Devices;

/// <summary>
/// Severity of a diagnostic entry produced by the backend.
/// </summary>
public enum DiagnosticSeverity
{
    Information,
    Waring,
    Error,
    Critical
}

/// <summary>
/// Broad area in which a diagnistic occured.
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
    public DateTimeOffset OccuredAt { get; } = Timestamp ?? DateTimeOffset.UtcNow;
}