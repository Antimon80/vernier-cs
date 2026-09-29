using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.ViewModels.GoDirect;

public sealed partial class SpectroVisKeepDataPointViewModel : ObservableObject, IDisposable
{

    private readonly SpectroVisMeasurementViewModel _measurementViewModel;
    private bool _disposed;

    public SpectroVisKeepDataPointViewModel(SpectroVisMeasurementViewModel measurementViewModel)
    {
        _measurementViewModel = measurementViewModel ?? throw new ArgumentNullException(nameof(measurementViewModel));

        ColumnNameShort = _measurementViewModel.ColumnNameShort;
        Unit = _measurementViewModel.Unit;
    }

    /// <summary>
    /// Short name of the measurement column displayed in the dialog.
    /// </summary>
    public string ColumnNameShort { get; set; }

    /// <summary>
    /// Unit of the measurement value.
    /// </summary>
    public string Unit { get; set; }

    /// <summary>
    /// Measurement value to be captured as an event point.
    /// </summary>
    [ObservableProperty]
    public partial double DataPointValue { get; set; }

    /// <summary>
    /// Captures the entered value as an event point in the measurement.
    /// </summary>
    [RelayCommand]
    private async Task OnValueSet(CancellationToken ct = default)
    {
        _measurementViewModel.DataPointValue = DataPointValue;
        await _measurementViewModel.CaptureEventPoint(DataPointValue, ct);
    }

    /// <summary>
    /// Releases resources used by the view model.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }
}