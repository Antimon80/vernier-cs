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

    public string ColumnNameShort { get; set; }

    public string Unit { get; set; }

    [ObservableProperty]
    public partial double DataPointValue {get; set;}

    [RelayCommand]
    private async Task OnValueSet(CancellationToken ct = default)
    {
        _measurementViewModel.DataPointValue = DataPointValue;
        await _measurementViewModel.CaptureEventPoint(DataPointValue, ct);
    }

    /// <summary>
    /// This dialog view model doesn't subscribe to anything on the long-lived measurement
    /// view model (unlike the operating-mode/acquisition-mode dialogs), so there is nothing
    /// to unsubscribe. Kept for symmetry with the other dialog view models and as a guard
    /// against double-disposal if that ever changes.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }
}