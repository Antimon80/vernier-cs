using System.Globalization;
using System.Resources;

namespace App.Resources.Strings;

public static class AppResources
{
    private static readonly ResourceManager ResourceManager =
        new("App.Resources.Strings.AppResources", typeof(AppResources).Assembly);

    public static CultureInfo? Culture { get; set; }

    private static string GetString(string name)
    {
        return ResourceManager.GetString(name, Culture)
            ?? $"!{name}!";
    }

    // DeviceSelectionPage
    public static string DeviceSelection_Title => GetString(nameof(DeviceSelection_Title));

    public static string DeviceSelection_Searching => GetString(nameof(DeviceSelection_Searching));

    public static string DeviceSelection_NoDevicesFound => GetString(nameof(DeviceSelection_NoDevicesFound));

    public static string DeviceSelection_DeviceFoundSingular => GetString(nameof(DeviceSelection_DeviceFoundSingular));

    public static string DeviceSelection_DeviceFoundPlural => GetString(nameof(DeviceSelection_DeviceFoundPlural));

    public static string DeviceSelection_Refreshing => GetString(nameof(DeviceSelection_Refreshing));

    public static string DeviceSelection_Connecting => GetString(nameof(DeviceSelection_Connecting));

    public static string DeviceSelection_Initializing => GetString(nameof(DeviceSelection_Initializing));
    public static string DeviceSelection_Conntected => GetString(nameof(DeviceSelection_Conntected));

    public static string DeviceSelection_ConnectedToDevice => GetString(nameof(DeviceSelection_ConnectedToDevice));

    public static string DeviceSelection_ConnectionFailed => GetString(nameof(DeviceSelection_ConnectionFailed));

    public static string DeviceSelection_DiscoveryFailed => GetString(nameof(DeviceSelection_DiscoveryFailed));

    // general dialogs
    public static string Dialog_ErrorTitle => GetString(nameof(Dialog_ErrorTitle));

    public static string Dialog_ConnectionFailedTitle => GetString(nameof(Dialog_ConnectionFailedTitle));

    public static string Dialog_Ok => GetString(nameof(Dialog_Ok));

    public static string Dialog_Cancel => GetString(nameof(Dialog_Cancel));

    // SpectrometerPage
    public static string Spectrometer_NotCalibrated => GetString(nameof(Spectrometer_NotCalibrated));

    public static string Spectrometer_IsInitialized => GetString(nameof(Spectrometer_IsInitialized));
    public static string Spectrometer_IsCalibrated => GetString(nameof(Spectrometer_IsCalibrated));

    public static string Spectrometer_StartMeasurement => GetString(nameof(Spectrometer_StartMeasurement));

    public static string Spectrometer_StopMeasurement => GetString(nameof(Spectrometer_StopMeasurement));
    public static string Spectrometer_Calibrate => GetString(nameof(Spectrometer_Calibrate));
    public static string Spectrometer_OperatingMode => GetString(nameof(Spectrometer_OperatingMode));
    public static string Spectrometer_AcquisitionMode => GetString(nameof(Spectrometer_AcquisitionMode));
}