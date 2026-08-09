using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace App.Views.Controls;

/// <summary>
/// Draws a horizontal strip that fills the plotted x-axis range of a full-spectrum chart with
/// the approximate visible-spectrum color of each wavelength.
///
/// The strip does not know the chart's pixel margins directly; instead it consumes
/// <see cref="PlotLeftFraction"/>/<see cref="PlotRightFraction"/> (0-1 fractions of the control's
/// own width) published by <see cref="MeasurementChart"/>, so the colored band lines up exactly
/// with the chart's plotted data area and tick marks, regardless of how wide the y-axis labels are.
/// </summary>
public sealed partial class SpectrumStripView : SKCanvasView
{
    public static readonly BindableProperty XMinimumProperty = BindableProperty.Create(
        nameof(XMinimum), typeof(double), typeof(SpectrumStripView), 0.0,
        propertyChanged: (bindable, _, _) => ((SpectrumStripView)bindable).InvalidateSurface());

    public static readonly BindableProperty XMaximumProperty = BindableProperty.Create(
        nameof(XMaximum), typeof(double), typeof(SpectrumStripView), 0.0,
        propertyChanged: (bindable, _, _) => ((SpectrumStripView)bindable).InvalidateSurface());

    public static readonly BindableProperty PlotLeftFractionProperty = BindableProperty.Create(
        nameof(PlotLeftFraction), typeof(double), typeof(SpectrumStripView), 0.0,
        propertyChanged: (bindable, _, _) => ((SpectrumStripView)bindable).InvalidateSurface());

    public static readonly BindableProperty PlotRightFractionProperty = BindableProperty.Create(
        nameof(PlotRightFraction), typeof(double), typeof(SpectrumStripView), 1.0,
        propertyChanged: (bindable, _, _) => ((SpectrumStripView)bindable).InvalidateSurface());

    public double XMinimum
    {
        get => (double)GetValue(XMinimumProperty);
        set => SetValue(XMinimumProperty, value);
    }

    public double XMaximum
    {
        get => (double)GetValue(XMaximumProperty);
        set => SetValue(XMaximumProperty, value);
    }

    /// <summary>
    /// Left edge of the chart's plotted data area, as a fraction (0-1) of this control's width.
    /// Bind this to the chart's <c>PlotLeftFraction</c> so the two controls stay aligned.
    /// </summary>
    public double PlotLeftFraction
    {
        get => (double)GetValue(PlotLeftFractionProperty);
        set => SetValue(PlotLeftFractionProperty, value);
    }

    /// <summary>
    /// Right edge of the chart's plotted data area, as a fraction (0-1) of this control's width.
    /// Bind this to the chart's <c>PlotRightFraction</c> so the two controls stay aligned.
    /// </summary>
    public double PlotRightFraction
    {
        get => (double)GetValue(PlotRightFractionProperty);
        set => SetValue(PlotRightFractionProperty, value);
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        SKImageInfo info = e.Info;
        float width = info.Width;
        float height = info.Height;

        canvas.Clear(SKColors.Transparent);

        float plotLeft = (float)(PlotLeftFraction * width);
        float plotRight = (float)(PlotRightFraction * width);

        if (plotRight <= plotLeft || XMaximum <= XMinimum)
        {
            base.OnPaintSurface(e);
            return;
        }

        using SKPaint paint = new() { IsAntialias = false };

        // One vertical line per device pixel column, colored by the wavelength at that x position.
        for (float pixelX = plotLeft; pixelX < plotRight; pixelX += 1f)
        {
            double t = (pixelX - plotLeft) / (plotRight - plotLeft);
            double wavelengthNm = XMinimum + t * (XMaximum - XMinimum);

            paint.Color = WavelengthToColor(wavelengthNm);
            canvas.DrawLine(pixelX, 0, pixelX, height, paint);
        }

        base.OnPaintSurface(e);
    }

    /// <summary>
    /// Approximates the perceived RGB color of visible light at the given wavelength, following the
    /// well-known piecewise-linear model (Dan Bruton). Wavelengths outside the roughly 380-700 nm
    /// visible range (i.e. UV or IR) have no perceptible spectral color and are rendered as black.
    /// </summary>
    internal static SKColor WavelengthToColor(double wavelengthNm)
    {
        double r, g, b;

        switch (wavelengthNm)
        {
            case >= 380 and < 440:
                r = -(wavelengthNm - 440) / (440 - 380);
                g = 0;
                b = 1;
                break;
            case >= 440 and < 490:
                r = 0;
                g = (wavelengthNm - 440) / (490 - 440);
                b = 1;
                break;
            case >= 490 and < 510:
                r = 0;
                g = 1;
                b = -(wavelengthNm - 510) / (510 - 490);
                break;
            case >= 510 and < 580:
                r = (wavelengthNm - 510) / (580 - 510);
                g = 1;
                b = 0;
                break;
            case >= 580 and < 645:
                r = 1;
                g = -(wavelengthNm - 645) / (645 - 580);
                b = 0;
                break;
            case >= 645 and <= 700:
                r = 1;
                g = 0;
                b = 0;
                break;
            default:
                // Outside the visible range (UV below 380 nm, IR above 700 nm): no perceptible color.
                return SKColors.Black;
        }

        // The eye's sensitivity tapers off near the edges of the visible range, so fade intensity there.
        double intensity = wavelengthNm switch
        {
            >= 380 and < 420 => 0.3 + 0.7 * (wavelengthNm - 380) / (420 - 380),
            >= 420 and <= 645 => 1.0,
            > 645 and <= 700 => 0.3 + 0.7 * (700 - wavelengthNm) / (700 - 645),
            _ => 1.0
        };

        const double gamma = 0.8;
        byte ToByte(double c) => (byte)Math.Clamp(Math.Round(255 * Math.Pow(c * intensity, gamma)), 0, 255);

        return new SKColor(ToByte(r), ToByte(g), ToByte(b));
    }
}
