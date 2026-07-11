using System.Collections.ObjectModel;
using System.ComponentModel;
using App.Models;
using App.Resources.Strings;
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
            option.PropertyChanged += OnOperatingModeChanged;
        }

        IntegrationTimeMs = _measurementViewModel.IntegrationTimeMs;
        CanEditIntegrationTime = _measurementViewModel.CanEditIntegrationTime;
    }

    public ObservableCollection<SpectroVisOperatingModeOption> OperatingModeOptions => _measurementViewModel.OperatingModeOptions;

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
    private async Task OnIntegrationTimeChanged()
    {
        if (_measurementViewModel.IntegrationTimeMs != IntegrationTimeMs)
        {
            await _measurementViewModel.ApplyIntegrationTimeAsync(IntegrationTimeMs);
        }
    }

    private async void OnOperatingModeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SpectroVisOperatingModeOption.IsSelected))
        {
            return;
        }

        if (sender is not SpectroVisOperatingModeOption option || !option.IsSelected)
        {
            return;
        }

        if (option.Mode == _measurementViewModel.Session.Mode)
        {
            return;
        }

        await _measurementViewModel.SelectOperatingModeAsync(option.Mode);
    }

    [RelayCommand]
    private Task OpenHelp()
    {
        return ShowNotImplementedAsync(AppResources.App_Help);
    }

    private static Task ShowNotImplementedAsync(string feature)
    {
        Page? page = Application.Current?.Windows.FirstOrDefault()?.Page;

        if (page is null)
        {
            return Task.CompletedTask;
        }

        return page.DisplayAlertAsync(feature, "not implemented yet", AppResources.Dialog_Ok);
    }
}