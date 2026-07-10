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

    // device selection
    public static string DeviceSelection_Title => GetString(nameof(DeviceSelection_Title));
    public static string DeviceSelection_Searching => GetString(nameof(DeviceSelection_Searching));
    public static string DeviceSelection_NoDevicesFound => GetString(nameof(DeviceSelection_NoDevicesFound));
    public static string DeviceSelection_DeviceFoundSingular => GetString(nameof(DeviceSelection_DeviceFoundSingular));
    public static string DeviceSelection_DeviceFoundPlural => GetString(nameof(DeviceSelection_DeviceFoundPlural));
    public static string DeviceSelection_Refreshing => GetString(nameof(DeviceSelection_Refreshing));
    public static string DeviceSelection_Connecting => GetString(nameof(DeviceSelection_Connecting));
    public static string DeviceSelection_Initializing => GetString(nameof(DeviceSelection_Initializing));
    public static string DeviceSelection_Connected => GetString(nameof(DeviceSelection_Connected));
    public static string DeviceSelection_ConnectedToDevice => GetString(nameof(DeviceSelection_ConnectedToDevice));
    public static string DeviceSelection_ConnectionFailed => GetString(nameof(DeviceSelection_ConnectionFailed));
    public static string DeviceSelection_ConnectionFailedWithDetails => GetString(nameof(DeviceSelection_ConnectionFailedWithDetails));
    public static string DeviceSelection_DiscoveryFailed => GetString(nameof(DeviceSelection_DiscoveryFailed));
    public static string DeviceSelection_DiscoveryFailedWithDetails => GetString(nameof(DeviceSelection_DiscoveryFailedWithDetails));
    public static string DeviceSelection_InitializationFailed => GetString(nameof(DeviceSelection_InitializationFailed));

    // common dialogs
    public static string Dialog_ErrorTitle => GetString(nameof(Dialog_ErrorTitle));
    public static string Dialog_Ok => GetString(nameof(Dialog_Ok));
    public static string Dialog_Cancel => GetString(nameof(Dialog_Cancel));
    public static string Dialog_Close => GetString(nameof(Dialog_Close));
    public static string Dialog_CannotCalibrate => GetString(nameof(Dialog_CannotCalibrate));

    // status
    public static string Spectrometer_NotCalibrated => GetString(nameof(Spectrometer_NotCalibrated));
    public static string Device_IsInitialized => GetString(nameof(Device_IsInitialized));
    public static string Spectrometer_IsCalibrated => GetString(nameof(Spectrometer_IsCalibrated));
    public static string Spectrometer_WhiteLamp => GetString(nameof(Spectrometer_WhiteLamp));
    public static string Spectrometer_CurrentOperatingMode => GetString(nameof(Spectrometer_CurrentOperatingMode));

    // tooltips
    public static string App_OpenFile => GetString(nameof(App_OpenFile));
    public static string App_SaveFile => GetString(nameof(App_SaveFile));
    public static string App_SaveFileAs => GetString(nameof(App_SaveFileAs));
    public static string App_Print => GetString(nameof(App_Print));
    public static string App_ExportData => GetString(nameof(App_ExportData));
    public static string App_ImportData => GetString(nameof(App_ImportData));
    public static string Device_ToggleMeasurement => GetString(nameof(Device_ToggleMeasurement));
    public static string Device_KeepDataPoint => GetString(nameof(Device_KeepDataPoint));
    public static string Device_Calibrate => GetString(nameof(Device_Calibrate));
    public static string Spectrometer_OperatingMode => GetString(nameof(Spectrometer_OperatingMode));
    public static string Device_AcquisitionMode => GetString(nameof(Device_AcquisitionMode));
    public static string App_CrossHairs => GetString(nameof(App_CrossHairs));
    public static string App_DataManagement => GetString(nameof(App_DataManagement));
    public static string App_DataAnalysis => GetString(nameof(App_DataAnalysis));
    public static string Device_Diagnostics => GetString(nameof(Device_Diagnostics));
    public static string App_Settings => GetString(nameof(App_Settings));
    public static string App_Help => GetString(nameof(App_Help));

    // chart and table
    public static string Spectrometer_FullSpectrum => GetString(nameof(Spectrometer_FullSpectrum));
    public static string Device_TimeResolved => GetString(nameof(Device_TimeResolved));
    public static string Device_EventTriggered => GetString(nameof(Device_EventTriggered));
    public static string Spectrometer_Wavelength => GetString(nameof(Spectrometer_Wavelength));
    public static string App_TimeAxis => GetString(nameof(App_TimeAxis));
    public static string Spectrometer_ConcentrationAxis => GetString(nameof(Spectrometer_ConcentrationAxis));
    public static string Spectrometer_RawCounts => GetString(nameof(Spectrometer_RawCounts));
    public static string Spectrometer_Intensity => GetString(nameof(Spectrometer_Intensity));
    public static string Spectrometer_Transmittance => GetString(nameof(Spectrometer_Transmittance));
    public static string Spectrometer_Absorbance => GetString(nameof(Spectrometer_Absorbance));
    public static string Device_NoDataYet => GetString(nameof(Device_NoDataYet));

    // spectrometer operating mode
    public static string OperatingMode_Absorbance => GetString(nameof(OperatingMode_Absorbance));
    public static string OperatingMode_Transmittance => GetString(nameof(OperatingMode_Transmittance));
    public static string OperatingMode_Fluorescence405 => GetString(nameof(OperatingMode_Fluorescence405));
    public static string OperatingMode_Fluorescence500 => GetString(nameof(OperatingMode_Fluorescence500));
    public static string OperatingMode_Emission => GetString(nameof(OperatingMode_Emission));
    public static string OperatingMode_RawCounts => GetString(nameof(OperatingMode_RawCounts));
    public static string OperatingMode_DeviceType => GetString(nameof(OperatingMode_DeviceType));
    public static string OperatingMode_MeasurementRange => GetString(nameof(OperatingMode_MeasurementRange));
    public static string OperatingMode_IntegrationTime => GetString(nameof(OperatingMode_IntegrationTime));
    public static string OperatingMode_DialogTitle => GetString(nameof(OperatingMode_DialogTitle));

    // diagnostics dialog
    public static string Diagnostics_DialogTitle => GetString(nameof(Diagnostics_DialogTitle));
    public static string Diagnostics_NoEntries => GetString(nameof(Diagnostics_NoEntries));

    // miscellaneous
    public static string App_AppName => GetString(nameof(App_AppName));
}