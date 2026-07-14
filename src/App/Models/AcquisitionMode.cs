namespace App.Models;

/// <summary>
/// Describes how acquired measurement data is presented and recorded in the UI.
///
/// The backend device always delivers complete data sets. The acquisition mode is therefore an 
/// application-level concept that decides how the UI displays, samples and stores these data.
/// </summary>
public enum AcquisitionMode
{
    FullSpectrum,
    TimeResolved,
    EventTriggered
}