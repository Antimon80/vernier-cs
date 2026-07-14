using System.ComponentModel;
using App.Models;
using App.Resources.Strings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.ViewModels.GoDirect;

public sealed partial class SpectroVisAcquisitionModeViewModel : ObservableObject
{
    private readonly SpectroVisMeasurementViewModel _measurementViewModel;

    public SpectroVisAcquisitionModeViewModel(SpectroVisMeasurementViewModel measurementViewModel)
    {
        _measurementViewModel = measurementViewModel ?? throw new ArgumentNullException(nameof(measurementViewModel));

        SpectrometerType = _measurementViewModel.Model.Name;
        MeasurementRangeText = $"{_measurementViewModel.Model.WavelengthMinNm:F1} - {_measurementViewModel.Model.WavelengthMaxNm:F1} nm";

        foreach(AcquisitionModeOption option in _measurementViewModel.AcquisitionModeOptions)
        {
            option.PropertyChanged += OnAcquisitionModeChanged;
        }

        TimeResolvedDuration = _measurementViewModel.TimeResolvedDuration;
        SelectedWavelength = _measurementViewModel.SelectedWavelengthNm;

        CanEditTimeResolvedSettings = _measurementViewModel.CanEditTimeResolvedSettings;
        CanEditEventTriggeredSettings = _measurementViewModel.CanEditEventTriggeredSettings;
    }

    [ObservableProperty]
    public partial int TimeResolvedDuration {get; set;}

    [ObservableProperty]
    public partial int SelectedWavelength {get; set;}

    [ObservableProperty]
    public partial bool CanEditTimeResolvedSettings {get; set;}

    [ObservableProperty]
    public partial bool CanEditEventTriggeredSettings {get; set;}

    [ObservableProperty]
    public partial string SpectrometerType {get; set;} = "";

    [ObservableProperty]
    public partial string MeasurementRangeText {get; set;} = "";

    [ObservableProperty]
    public partial string DeviceTypeText {get; set;} = "";

    [RelayCommand]
    private void OnTimeResolvedSettingsChanged()
    {
        if(_measurementViewModel.TimeResolvedDuration != TimeResolvedDuration)
        {
            _measurementViewModel.TimeResolvedDuration = TimeResolvedDuration;
        }

        if(_measurementViewModel.SelectedWavelengthNm != SelectedWavelength)
        {
            _measurementViewModel.SelectedWavelengthNm = SelectedWavelength;
        }
    }

    [RelayCommand]
    private async void OnEventTriggeredSettingsChanged()
    {
        
    }

    private async void OnAcquisitionModeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if(e.PropertyName != nameof(AcquisitionModeOption.IsSelected))
        {
            return;
        }

        if(sender is not AcquisitionModeOption option || !option.IsSelected)
        {
            return;
        }

        if(option.Mode == _measurementViewModel.AcquisitionMode)
        {
            return;
        }

        _measurementViewModel.SelectAcquisitionMode(option.Mode);
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
