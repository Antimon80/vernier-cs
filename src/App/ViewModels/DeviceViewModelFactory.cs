using App.Models;
using App.ViewModels.GoDirect;
using Backend.Discovery;

namespace App.ViewModels;

/// <summary>
/// Creates the measurement view model for the currently connected device.
/// </summary>
public static class DeviceViewModelFactory
{
    public static IDeviceMeasurementViewModel Create(DeviceManager deviceManager, WideMeasurementTable table, ChartModel chart, MeasurementDataSet data)
    {
        if (deviceManager.CurrentSpectrometer is not null)
        {
            return new SpectroVisMeasurementViewModel(deviceManager.CurrentSpectrometer, table, chart, data);
        }

        throw new InvalidOperationException($"The selected device type '{deviceManager.CurrentDevice?.DeviceName}' is not supported by the measurement UI yet.");
    }
}