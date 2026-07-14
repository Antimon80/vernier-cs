using System.Collections.ObjectModel;
using System.ComponentModel;
using App.Models;
using App.Resources.Strings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.ViewModels.GoDirect;

public sealed partial class SpectroVisAcquisitionModeViewModel : ObservableObject, IDisposable
{
    private readonly SpectroVisMeasurementViewModel _measurementViewModel;
    private bool _disposed;

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
        ContinuousDataCollection = _measurementViewModel.ContinuousDataCollection;
        ColumnName = _measurementViewModel.ColumnName;
        Unit = _measurementViewModel.Unit;

        CanEditTimeResolvedSettings = _measurementViewModel.CanEditTimeResolvedSettings;
        CanEditEventTriggeredSettings = _measurementViewModel.CanEditEventTriggeredSettings;
        CanEditWavelength = _measurementViewModel.CanEditWavelength;
    }

    /// <summary>
    /// Gets the acquisition-mode options owned by the measurement view model, exposed here so the
    /// dialog's radio-button group can bind to them directly.
    /// </summary>
    public ObservableCollection<AcquisitionModeOption> AcquisitionModeOptions => _measurementViewModel.AcquisitionModeOptions;

    [ObservableProperty]
    public partial int TimeResolvedDuration {get; set;}

    [ObservableProperty]
    public partial int SelectedWavelength {get; set;}

    [ObservableProperty]
    public partial bool ContinuousDataCollection {get; set;}

    [ObservableProperty]
    public partial string ColumnName {get; set;} = "";

    [ObservableProperty]
    public partial string Unit {get; set;} = "";

    [ObservableProperty]
    public partial bool CanEditTimeResolvedSettings {get; set;}

    [ObservableProperty]
    public partial bool CanEditEventTriggeredSettings {get; set;}

    [ObservableProperty]
    public partial bool CanEditWavelength {get; set;}

    /// <summary>
    /// Gets or sets whether the duration entry may be edited: only in time-resolved mode, and only
    /// while continuous data collection is not selected.
    /// </summary>
    [ObservableProperty]
    public partial bool CanEditDuration {get; set;}

    partial void OnContinuousDataCollectionChanged(bool value)
    {
        RefreshCanEditDuration();
    }

    partial void OnCanEditTimeResolvedSettingsChanged(bool value)
    {
        RefreshCanEditDuration();
    }

    private void RefreshCanEditDuration()
    {
        CanEditDuration = CanEditTimeResolvedSettings && !ContinuousDataCollection;
    }

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

        if(_measurementViewModel.ContinuousDataCollection != ContinuousDataCollection)
        {
            _measurementViewModel.ContinuousDataCollection = ContinuousDataCollection;
        }
    }

    [RelayCommand]
    private async Task OnEventTriggeredSettingsChanged()
    {
        if(_measurementViewModel.ColumnName != ColumnName)
        {
            _measurementViewModel.ColumnName = ColumnName;
        }

        if(_measurementViewModel.Unit != Unit)
        {
            _measurementViewModel.Unit = Unit;
        }

        await Task.CompletedTask;
    }

    private void OnAcquisitionModeChanged(object? sender, PropertyChangedEventArgs e)
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

        // The measurement view model is the source of truth for these flags; re-read them
        // now that the acquisition mode (and therefore their computed value) has changed.
        CanEditTimeResolvedSettings = _measurementViewModel.CanEditTimeResolvedSettings;
        CanEditEventTriggeredSettings = _measurementViewModel.CanEditEventTriggeredSettings;
        CanEditWavelength = _measurementViewModel.CanEditWavelength;
    }

    [RelayCommand]
    private Task OpenHelp()
    {
        return ShowNotImplementedAsync(AppResources.App_Help);
    }

    private static Task ShowNotImplementedAsync(string feature)
    {
        Page? page = GetCurrentPage();

        if (page is null)
        {
            return Task.CompletedTask;
        }

        return page.DisplayAlertAsync(feature, "not implemented yet", AppResources.Dialog_Ok);
    }

    /// <summary>
    /// Resolves the topmost currently presented page (modal dialog if one is pushed, otherwise the
    /// root page). Application.Current.Windows[0].Page alone always returns the root page, so an
    /// alert raised while a modal dialog (like this one) is open would be requested on a page that
    /// isn't the one actually visible to the user.
    /// </summary>
    private static Page? GetCurrentPage()
    {
        Page? root = Application.Current?.Windows.FirstOrDefault()?.Page;

        if (root is null)
        {
            return null;
        }

        IReadOnlyList<Page>? modalStack = root.Navigation?.ModalStack;

        return modalStack is { Count: > 0 } ? modalStack[^1] : root;
    }

    /// <summary>
    /// Unsubscribes from the acquisition-mode options owned by the long-lived measurement view model.
    ///
    /// The options collection outlives this dialog view model, which is re-created every time the
    /// dialog is opened. Without this, every dialog open would leave behind another subscriber.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (AcquisitionModeOption option in _measurementViewModel.AcquisitionModeOptions)
        {
            option.PropertyChanged -= OnAcquisitionModeChanged;
        }
    }
}
