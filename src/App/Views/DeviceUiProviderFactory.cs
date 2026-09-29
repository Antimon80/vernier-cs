using App.ViewModels;
using App.ViewModels.GoDirect;
using App.Views.GoDirect;

namespace App.Views;

public static class DeviceUiProviderFactory
{
    /// <summary>
    /// Creates the UI provider for the specified device-specific measurement view model.
    /// </summary>
    public static IDeviceUiProvider Create(IDeviceMeasurementViewModel viewModel)
    {
        switch (viewModel)
        {
            case SpectroVisMeasurementViewModel spectroVisViewModel:
                return new SpectroVisUiProvider(spectroVisViewModel);
            default:
                throw new InvalidOperationException(
                    $"No measurement view is registered for view model type '{viewModel.GetType().Name}'."
                );
        }

    }
}