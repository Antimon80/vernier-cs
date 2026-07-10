using System.Collections.ObjectModel;
using Backend.Util;

namespace App.Models;

public sealed record UiDiagnostics(
    string Severity,
    string Category,
    string Code,
    string Message,
    string TechnicalDetails)
{
    internal static void AddDiagnostics(ObservableCollection<UiDiagnostics> deviceDiagnostics, IEnumerable<DiagnosticEntry> diagnostics)
    {
        foreach (DiagnosticEntry diagnostic in diagnostics)
        {
            deviceDiagnostics.Add(new UiDiagnostics(
                Severity: diagnostic.Severity.ToString(),
                Category: diagnostic.Category.ToString(),
                Code: diagnostic.Code,
                Message: diagnostic.Message,
                TechnicalDetails: diagnostic.TechnicalDetails ?? string.Empty
            ));
        }
    }
}