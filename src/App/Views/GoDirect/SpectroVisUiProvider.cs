using App.ViewModels;
using App.ViewModels.GoDirect;

namespace App.Views.GoDirect;

public sealed class SpectroVisUiProvider(SpectroVisMeasurementViewModel viewModel) : IDeviceUiProvider
{
    private SpectroVisMeasurementViewModel _viewModel = viewModel;

    public View CreateMeasurementView()
    {
        return new SpectroVisMeasurementView { BindingContext = _viewModel };
    }

    public async Task ShowOperatingModeDialog(INavigation navigation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        SpectroVisOperatingModeViewModel dialogViewModel = new(_viewModel);
        SpectroVisOperatingModeDialog dialog = new(dialogViewModel);

        await navigation.PushModalAsync(dialog);
    }

    public async Task ShowAcquisitionModeDialog(INavigation navigation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        SpectroVisAcquisitionModeViewModel dialogViewModel = new(_viewModel);
        SpectroVisAcquisitionModeDialog dialog = new(dialogViewModel);

        await navigation.PushModalAsync(dialog);
    }

    public async Task<CalibrationDialogResult?> ShowCalibrationDialog(INavigation navigation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        SpectroVisCalibrationViewModel dialogViewModel = new(_viewModel);
        SpectroVisCalibrationDialog dialog = new(dialogViewModel);

        await navigation.PushModalAsync(dialog);

        return await dialogViewModel.ResultTask;
    }

    public async Task ShowKeepDataPointDialog(INavigation navigation, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        SpectroVisKeepDataPointViewModel dialogViewModel = new(_viewModel);
        SpectroVisKeepDataPointDialog dialog = new(dialogViewModel);

        await navigation.PushModalAsync(dialog);
    }
}