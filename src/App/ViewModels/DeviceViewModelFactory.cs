using App.Models;
using App.ViewModels.GoDirect;
using Backend.Discovery;

namespace App.ViewModels;

/// <summary>
/// Creates the measurement view model for the currently connected device.
/// </summary>
public static class DeviceModelFactory
{
    public static IDeviceMeasurementViewModel Create(DeviceManager deviceManager, Func<bool> isMeasurementRunningProvider, WideMeasurementTable table)
    {
        if (deviceManager.CurrentSpectrometer is not null)
        {
            return new SpectroVisMeasurementViewModel(deviceManager.CurrentSpectrometer, isMeasurementRunningProvider, table);
        }

        throw new InvalidOperationException($"The selected device type '{deviceManager.CurrentDevice?.DeviceName}' is not supported by the measurement UI yet.");
    }
}