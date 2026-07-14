using System.Collections.ObjectModel;
using System.ComponentModel;
using App.Models;
using App.Resources.Strings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.ViewModels.GoDirect;

public sealed partial class SpectroVisOperatingModeViewModel : ObservableObject, IDisposable
{
    private readonly SpectroVisMeasurementViewModel _measurementViewModel;
    private bool _disposed;

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

    /// <summary>
    /// Gets the operating-mode options owned by the measurement view model, exposed here so the
    /// dialog's radio-button group can bind to them directly.
    /// </summary>
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

        try
        {
            await _measurementViewModel.SelectOperatingModeAsync(option.Mode);

            IntegrationTimeMs = _measurementViewModel.IntegrationTimeMs;
            CanEditIntegrationTime = _measurementViewModel.CanEditIntegrationTime;
        }
        catch (Exception ex)
        {
            _measurementViewModel.RefreshOperatingModeSelection();

            await ShowErrorAsync(ex);
        }
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

    private static Task ShowErrorAsync(Exception ex)
    {
        Page? page = GetCurrentPage();

        if (page is null)
        {
            return Task.CompletedTask;
        }

        return page.DisplayAlertAsync(AppResources.Dialog_ErrorTitle, ex.Message, AppResources.Dialog_Ok);
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
    /// Unsubscribes from the operating-mode options owned by the long-lived measurement view model.
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

        foreach (SpectroVisOperatingModeOption option in _measurementViewModel.OperatingModeOptions)
        {
            option.PropertyChanged -= OnOperatingModeChanged;
        }
    }
}