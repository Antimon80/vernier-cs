using System.Collections.ObjectModel;
using System.ComponentModel;
using App.Models;
using App.Resources.Strings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.ViewModels.GoDirect;

/// <summary>
/// Provides the state and commands used by the SpectroVis acquisition-mode dialog.
///
/// The dialog edits acquisition settings that are stored in the long-lived
/// <see cref="SpectroVisMeasurementViewModel"/>. Acquisition-mode options are shared
/// with the measurement view model so that both view models always use the same selection state.
/// </summary>
public sealed partial class SpectroVisAcquisitionModeViewModel : ObservableObject, IDisposable
{
    private readonly SpectroVisMeasurementViewModel _measurementViewModel;
    private bool _disposed;

    /// <summary>
    /// Initializes the acquisition-mode dialog from the current measurement settings.
    /// </summary>
    /// <param name="measurementViewModel">
    /// The measurement view model whose acquisition settings are displayed and modified.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="measurementViewModel"/> is <see langword="null"/>.
    /// </exception>
    public SpectroVisAcquisitionModeViewModel(SpectroVisMeasurementViewModel measurementViewModel)
    {
        _measurementViewModel = measurementViewModel ?? throw new ArgumentNullException(nameof(measurementViewModel));

        // Display static device information in the dialog header.
        SpectrometerType = _measurementViewModel.Model.Name;
        MeasurementRangeText = $"{_measurementViewModel.Model.WavelengthMinNm:F1} - {_measurementViewModel.Model.WavelengthMaxNm:F1} nm";

        // Observe the shared radio-button options so a newly selected mode can be
        // applied immediately to the measurement view model.
        foreach (AcquisitionModeOption option in _measurementViewModel.AcquisitionModeOptions)
        {
            option.PropertyChanged += OnAcquisitionModeChanged;
        }

        // Copy the current time-resolved settings into the dialog.
        TimeResolvedDuration = _measurementViewModel.TimeResolvedDuration;
        SelectedWavelength = _measurementViewModel.SelectedWavelengthNm;
        ContinuousDataCollection = _measurementViewModel.ContinuousDataCollection;

        // Copy the current event-triggered column settings into the dialog.
        ColumnNameLong = _measurementViewModel.ColumnNameLong;
        ColumnNameShort = _measurementViewModel.ColumnNameShort;

        // Select a predefined unit when possible. Otherwise select the custom-unit
        // option and preserve the existing unit as free text.
        string currentUnit = _measurementViewModel.Unit;

        if (UnitOptions.Contains(currentUnit))
        {
            SelectedUnit = currentUnit;
        }
        else
        {
            SelectedUnit = AppResources.AcquisitionMode_OtherUnit;
            CustomUnit = currentUnit;
        }

        // Initialize the editability state for the currently selected acquisition mode.
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
    public partial int TimeResolvedDuration { get; set; }

    [ObservableProperty]
    public partial int SelectedWavelength { get; set; }

    /// <summary>
    /// Gets or sets whether time-resolved data collection continues until stopped manually.
    /// </summary>
    [ObservableProperty]
    public partial bool ContinuousDataCollection { get; set; }

    /// <summary>
    /// Common concentration units offered for event-triggered (Lambert-Beer) measurements,
    /// plus a trailing "Sonstiges"/"Other" option that reveals a free-text entry.
    /// Other device types will need a different list entirely, so this stays local to this dialog.
    /// </summary>
    public IReadOnlyList<string> UnitOptions { get; } =
    [
        "mol/l", "mmol/l", "µmol/l", "g/l", "mg/l", "µg/l", "mg/ml", "µg/ml",
        AppResources.AcquisitionMode_OtherUnit
    ];

    [ObservableProperty]
    public partial string ColumnNameLong { get; set; } = "";

    [ObservableProperty]
    public partial string ColumnNameShort { get; set; } = "";

    [ObservableProperty]
    public partial string SelectedUnit { get; set; } = "";

    [ObservableProperty]
    public partial string CustomUnit { get; set; } = "";

    /// <summary>
    /// Gets whether the free-text custom-unit entry should be shown, i.e. whether
    /// "Sonstiges"/"Other" is currently selected in the unit dropdown.
    /// </summary>
    public bool IsCustomUnitSelected => SelectedUnit == AppResources.AcquisitionMode_OtherUnit;


    [ObservableProperty]
    public partial bool CanEditTimeResolvedSettings { get; set; }

    [ObservableProperty]
    public partial bool CanEditEventTriggeredSettings { get; set; }

    [ObservableProperty]
    public partial bool CanEditWavelength { get; set; }

    /// <summary>
    /// Gets or sets whether the duration entry may be edited: only in time-resolved mode, and only
    /// while continuous data collection is not selected.
    /// </summary>
    [ObservableProperty]
    public partial bool CanEditDuration { get; set; }

    [ObservableProperty]
    public partial string SpectrometerType { get; set; } = "";

    [ObservableProperty]
    public partial string MeasurementRangeText { get; set; } = "";

    [ObservableProperty]
    public partial string DeviceTypeText { get; set; } = "";

    /// <summary>
    /// Recalculates duration-field editability when continuous collection changes.
    /// </summary>
    /// <param name="value">
    /// The new continuous-data-collection value generated by the observable property.
    /// </param>
    partial void OnContinuousDataCollectionChanged(bool value)
    {
        CanEditDuration = CanEditTimeResolvedSettings && !ContinuousDataCollection;
    }

    /// <summary>
    /// Recalculates duration-field editability when the active acquisition mode changes.
    /// </summary>
    /// <param name="value">
    /// The new time-resolved editability value generated by the observable property.
    /// </param>
    partial void OnCanEditTimeResolvedSettingsChanged(bool value)
    {
        CanEditDuration = CanEditTimeResolvedSettings && !ContinuousDataCollection;
    }

    /// <summary>
    /// Notifies the UI that custom-unit visibility may have changed.
    /// </summary>
    /// <param name="value">
    /// The newly selected unit generated by the observable property.
    /// </param>
    partial void OnSelectedUnitChanged(string value)
    {
        OnPropertyChanged(nameof(IsCustomUnitSelected));
    }

    /// <summary>
    /// Applies the current time-resolved settings to the measurement view model.
    ///
    /// Only changed values are written back.
    /// </summary>
    [RelayCommand]
    private void OnTimeResolvedSettingsChanged()
    {
        if (_measurementViewModel.TimeResolvedDuration != TimeResolvedDuration)
        {
            _measurementViewModel.TimeResolvedDuration = TimeResolvedDuration;
        }

        if (_measurementViewModel.SelectedWavelengthNm != SelectedWavelength)
        {
            _measurementViewModel.SelectedWavelengthNm = SelectedWavelength;
        }

        if (_measurementViewModel.ContinuousDataCollection != ContinuousDataCollection)
        {
            _measurementViewModel.ContinuousDataCollection = ContinuousDataCollection;
        }
    }

    /// <summary>
    /// Applies the current event-triggered settings to the measurement view model.
    ///
    /// When the custom-unit option is selected, the free-text unit is used instead
    /// of the selected predefined entry.
    /// </summary>
    /// <returns>A task representing completion of the command.</returns>
    [RelayCommand]
    private async Task OnEventTriggeredSettingsChanged()
    {
        if (_measurementViewModel.ColumnNameLong != ColumnNameLong)
        {
            _measurementViewModel.ColumnNameLong = ColumnNameLong;
        }

        if (_measurementViewModel.ColumnNameShort != ColumnNameShort)
        {
            _measurementViewModel.ColumnNameShort = ColumnNameShort;
        }

        string effectiveUnit = IsCustomUnitSelected ? CustomUnit : SelectedUnit;

        if (_measurementViewModel.Unit != effectiveUnit)
        {
            _measurementViewModel.Unit = effectiveUnit;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles selection changes raised by the shared acquisition-mode options.
    ///
    /// Only transitions to a newly selected option are processed. Deselection events
    /// and notifications for unrelated properties are ignored.
    /// </summary>
    /// <param name="sender">
    /// The acquisition-mode option that raised the property-change notification.
    /// </param>
    /// <param name="e">
    /// Information about the changed property.
    /// </param>
    private void OnAcquisitionModeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AcquisitionModeOption.IsSelected))
        {
            return;
        }

        if (sender is not AcquisitionModeOption option || !option.IsSelected)
        {
            return;
        }

        if (option.Mode == _measurementViewModel.AcquisitionMode)
        {
            return;
        }

        _measurementViewModel.SelectAcquisitionMode(option.Mode);

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
