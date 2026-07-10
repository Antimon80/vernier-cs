using Backend.Devices.GoDirect;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.Models;

public sealed partial class SpectroVisOperatingModeOption(OperatingMode mode, string displayName, bool isSupported, bool isSelected) : ObservableObject
{
    public OperatingMode Mode { get; } = mode;
    public string DisplayName { get; } = displayName;
    public bool IsSupported { get; } = isSupported;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}