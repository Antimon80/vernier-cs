using System.Collections.ObjectModel;
using App.Models;
using Backend.Devices.GoDirect;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.ViewModels.GoDirect;

public sealed partial class SpectroVisOperatingModeViewModel : ObservableObject
{
    private readonly SpectroVisMeasurementViewModel _measurementViewModel;

    public SpectroVisOperatingModeViewModel(SpectroVisMeasurementViewModel measurementViewModel)
    {
        _measurementViewModel = measurementViewModel ?? throw new ArgumentNullException(nameof(measurementViewModel));

        SpectrometerType = _measurementViewModel.Model.Name;
        MeasurementRangeText = $"{_measurementViewModel.Model.WavelengthMinNm:F1} - {_measurementViewModel.Model.WavelengthMaxNm:F1} nm";

        foreach (SpectroVisOperatingModeOption option in _measurementViewModel.OperatingModeOptions)
        {
            OperatingModeOptions.Add(new SpectroVisOperatingModeOption(option.Mode, option.DisplayName, option.IsSupported,
                option.Mode == _measurementViewModel.Session.Mode));
        }

        SelectedMode = _measurementViewModel.Session.Mode;
        IntegrationTimeMs = _measurementViewModel.IntegrationTimeMs;
        CanEditIntegrationTime = _measurementViewModel.CanEditIntegrationTime;
    }

    public ObservableCollection<SpectroVisOperatingModeOption> OperatingModeOptions { get; } = [];
    [ObservableProperty]
    public partial OperatingMode SelectedMode { get; set; }

    [ObservableProperty]
    public partial int IntegrationTimeMs { get; set; }

    [ObservableProperty]
    public partial bool CanEditIntegrationTime { get; set; }

    [ObservableProperty]
    public partial string SpectrometerType { get; set; } = "";

    [ObservableProperty]
    public partial string MeasurementRangeText { get; set; } = "";

    [ObservableProperty]
    public partial string DeviceTypeText { get; set; } = "";

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (_measurementViewModel.Session.Mode != SelectedMode)
        {
            await _measurementViewModel.SelectOperatingModeAsync(SelectedMode);
        }

        if (_measurementViewModel.IntegrationTimeMs != IntegrationTimeMs)
        {
            await _measurementViewModel.ApplyIntegrationTimeAsync(IntegrationTimeMs);
        }
    }
}