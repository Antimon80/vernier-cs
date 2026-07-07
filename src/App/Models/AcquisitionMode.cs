namespace App.Models;

/// <summary>
/// Describes how acquired measurement data is presented and recorded in the UI.
///
/// The backend spectrometer always delivers complete spectra. The acquisition
/// mode is therefore an application-level concept that decides how the UI
/// displays, samples and stores these spectra.
/// </summary>
public enum AcquisitionMode
{
    FullSpectrum,
    TimeResolved,
    EventTriggered
}