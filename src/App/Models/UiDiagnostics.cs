namespace App.Models;

public sealed record UiDiagnostics(
    string Severity,
    string Category,
    string Code,
    string Message,
    string TechnicalDetails);