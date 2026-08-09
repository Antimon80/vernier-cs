using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace App.Views.Controls;

/// <summary>
/// Draws the axes, ticks and curve of a measurement chart on a Skia surface.
///
/// This is the low-level rendering engine used by <see cref="MeasurementChart"/>, which wraps it
/// together with the axis-range edit fields. It is purely an internal implementation detail - never
/// bound to directly from XAML - so its data properties are plain C# properties rather than MAUI
/// <see cref="BindableProperty"/> instances; <see cref="MeasurementChart"/> sets them straight from
/// code and reacts to <see cref="PlotAreaChanged"/> instead of listening for bindable-property
/// change notifications.
/// </summary>
public sealed partial class MeasurementChartCanvas : SKCanvasView
{

    private const float TopMargin = 10f;
    private const int TickCount = 5;
    private const float TickLength = 6f;
    private const float TickLableGap = 8f;
    private const int MinorTicksPerMajor = 4;

    /// <summary>
    /// Width/height (in device-independent points) reserved around the plot area so the axis-range
    /// entry fields drawn by <see cref="MeasurementChart"/> always fit, even when the axis labels
    /// themselves would need less space.
    /// </summary>
    internal const double EntryWidth = 64;
    internal const double EntryHeight = 32;

    private double _xMinimum;
    private double _xMaximum;
    private double _yMinimum;
    private double _yMaximum;
    private IReadOnlyList<double> _xValues = [];
    private IReadOnlyList<double> _yValues = [];

    /// <summary>
    /// Raised after each paint, once <see cref="PlotLeftFraction"/>, <see cref="PlotRightFraction"/>,
    /// <see cref="PlotTopFraction"/> and <see cref="PlotBottomFraction"/> have all been recomputed, so
    /// <see cref="MeasurementChart"/> can reposition its overlaid axis-range entry fields.
    /// </summary>
    public event Action? PlotAreaChanged;

    public double XMinimum
    {
        get => _xMinimum;
        set { _xMinimum = value; InvalidateSurface(); }
    }

    public double XMaximum
    {
        get => _xMaximum;
        set { _xMaximum = value; InvalidateSurface(); }
    }

    public double YMinimum
    {
        get => _yMinimum;
        set { _yMinimum = value; InvalidateSurface(); }
    }

    public double YMaximum
    {
        get => _yMaximum;
        set { _yMaximum = value; InvalidateSurface(); }
    }

    public IReadOnlyList<double> XValues
    {
        get => _xValues;
        set { _xValues = value; InvalidateSurface(); }
    }

    public IReadOnlyList<double> YValues
    {
        get => _yValues;
        set { _yValues = value; InvalidateSurface(); }
    }

    /// <summary>
    /// Left edge of the plotted data area, expressed as a fraction (0-1) of the control's width.
    /// Recomputed on every paint so other controls (e.g. the spectrum strip below the x-axis, or the
    /// axis-range entry fields) can align themselves to the same horizontal positions without
    /// duplicating the margin calculation.
    /// </summary>
    public double PlotLeftFraction { get; private set; }

    /// <summary>
    /// Right edge of the plotted data area, expressed as a fraction (0-1) of the control's width.
    /// See <see cref="PlotLeftFraction"/>.
    /// </summary>
    public double PlotRightFraction { get; private set; } = 1.0;

    /// <summary>
    /// Top edge of the plotted data area, expressed as a fraction (0-1) of the control's height.
    /// See <see cref="PlotLeftFraction"/>.
    /// </summary>
    public double PlotTopFraction { get; private set; }

    /// <summary>
    /// Bottom edge of the plotted data area, expressed as a fraction (0-1) of the control's height.
    /// See <see cref="PlotLeftFraction"/>.
    /// </summary>
    public double PlotBottomFraction { get; private set; } = 1.0;

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        SKImageInfo info = e.Info;
        float width = info.Width;
        float height = info.Height;

        canvas.Clear(SKColors.White);

        using SKFont font = new() { Size = 28 };
        using SKPaint textPaint = new() { Color = SKColors.Black, IsAntialias = true };

        double xStep = CalculateNiceStep(XMaximum - XMinimum, TickCount);
        double firstXTick = Math.Ceiling(XMinimum / xStep) * xStep;
        int xDecimals = GetDecimalPlaces(xStep);

        double yStep = CalculateNiceStep(YMaximum - YMinimum, TickCount);
        double firstYTick = Math.Ceiling(YMinimum / yStep) * yStep;
        int yDecimals = GetDecimalPlaces(yStep);

        float maxYLabelWidth = 0f;
        for (double yValue = firstYTick; yValue <= YMaximum + yStep * 0.001; yValue += yStep)
        {
            maxYLabelWidth = Math.Max(maxYLabelWidth, font.MeasureText(yValue.ToString("F" + yDecimals)));
        }

        float rightmostXLabelWidth = font.MeasureText(XMaximum.ToString("F" + xDecimals));

        // Device-pixel size of the axis-range entry fields overlaid by MeasurementChart, so the
        // margins reserve enough room for them even when the tick labels themselves are narrower.
        float pixelScaleX = Width > 0 ? width / (float)Width : 1f;
        float pixelScaleY = Height > 0 ? height / (float)Height : 1f;
        float entryWidthPixels = (float)EntryWidth * pixelScaleX;
        float entryHeightPixels = (float)EntryHeight * pixelScaleY;

        float leftMargin = TickLength + TickLableGap + Math.Max(maxYLabelWidth, entryWidthPixels) + 4f;
        float rightMargin = Math.Max(rightmostXLabelWidth / 2f + 4f, entryWidthPixels / 2f + 4f);
        float bottomMargin = TickLength + TickLableGap + Math.Max(font.Size, entryHeightPixels) + 6f;
        float topMargin = Math.Max(TopMargin, entryHeightPixels / 2f + 4f);

        float plotLeft = leftMargin;
        float plotTop = topMargin;
        float plotRight = width - rightMargin;
        float plotBottom = height - bottomMargin;

        if (width > 0)
        {
            PlotLeftFraction = plotLeft / width;
            PlotRightFraction = plotRight / width;
        }

        if (height > 0)
        {
            PlotTopFraction = plotTop / height;
            PlotBottomFraction = plotBottom / height;
        }

        PlotAreaChanged?.Invoke();

        float ToPixelX(double x) =>
            (float)(plotLeft + (x - XMinimum) / (XMaximum - XMinimum) * (plotRight - plotLeft));

        float ToPixelY(double y) =>
            (float)(plotBottom - (y - YMinimum) / (YMaximum - YMinimum) * (plotBottom - plotTop));

        using SKPaint framePaint = new()
        {
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            Color = SKColors.Black,
            StrokeWidth = 3,
            StrokeCap = SKStrokeCap.Square
        };

        canvas.DrawRect(new SKRect(plotLeft, plotTop, plotRight, plotBottom), framePaint);

        using SKPaint tickPaint = new()
        {
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            Color = SKColors.Black,
            StrokeWidth = 3
        };

        // Ticks whose label would fall behind one of the axis-range entry boxes have their text
        // suppressed (the tick mark itself is still drawn, so the axis stays visually complete).
        float xLabelExclusion = entryWidthPixels / 2f + TickLableGap;
        float yLabelExclusion = entryHeightPixels / 2f + TickLableGap;

        bool IsNearXEdge(float tickX) =>
            tickX <= plotLeft + xLabelExclusion || tickX >= plotRight - xLabelExclusion;

        bool IsNearYEdge(float tickY) =>
            tickY <= plotTop + yLabelExclusion || tickY >= plotBottom - yLabelExclusion;

        double xMinorStep = xStep / MinorTicksPerMajor;
        double firstXMinorTick = Math.Ceiling(XMinimum / xMinorStep) * xMinorStep;

        for (double xValue = firstXMinorTick; xValue <= XMaximum + xMinorStep * 0.001; xValue += xMinorStep)
        {
            float tickX = ToPixelX(xValue);
            canvas.DrawLine(tickX, plotBottom, tickX, plotBottom + TickLength / 2f, tickPaint);
        }

        double yMinorStep = yStep / MinorTicksPerMajor;
        double firstYMinorTick = Math.Ceiling(YMinimum / yMinorStep) * yMinorStep;

        for (double yValue = firstYMinorTick; yValue <= YMaximum + yMinorStep * 0.001; yValue += yMinorStep)
        {
            float tickY = ToPixelY(yValue);
            canvas.DrawLine(plotLeft - TickLength / 2f, tickY, plotLeft, tickY, tickPaint);
        }

        for (double xValue = firstXTick; xValue <= XMaximum + xStep * 0.001; xValue += xStep)
        {
            float tickX = ToPixelX(xValue);
            canvas.DrawLine(tickX, plotBottom, tickX, plotBottom + TickLength, tickPaint);

            if (!IsNearXEdge(tickX))
            {
                canvas.DrawText(xValue.ToString("F" + xDecimals), tickX, plotBottom + TickLength + TickLableGap + font.Size, SKTextAlign.Center, font, textPaint);
            }
        }

        for (double yValue = firstYTick; yValue <= YMaximum + yStep * 0.001; yValue += yStep)
        {
            float tickY = ToPixelY(yValue);
            canvas.DrawLine(plotLeft - TickLength, tickY, plotLeft, tickY, tickPaint);

            if (!IsNearYEdge(tickY))
            {
                canvas.DrawText(yValue.ToString("F" + yDecimals), plotLeft - TickLength - TickLableGap, tickY + font.Size / 3, SKTextAlign.Right, font, textPaint);
            }
        }

        int pointCount = Math.Min(XValues.Count, YValues.Count);

        if (pointCount == 0)
        {
            base.OnPaintSurface(e);
            return;
        }

        using SKPathBuilder pathBuilder = new();

        for (int i = 0; i < pointCount; i++)
        {
            float pixelX = ToPixelX(XValues[i]);
            float pixelY = ToPixelY(YValues[i]);

            if (i == 0)
            {
                pathBuilder.MoveTo(pixelX, pixelY);
            }
            else
            {
                pathBuilder.LineTo(pixelX, pixelY);
            }
        }

        using SKPath path = pathBuilder.Detach();
        using SKPaint curvePaint = new()
        {
            Style = SKPaintStyle.Stroke,
            IsAntialias = true,
            Color = SKColors.Red,
            StrokeWidth = 3
        };

        // Clip to the plot rectangle: once the user narrows the axis range manually, data points
        // outside [XMinimum, XMaximum] / [YMinimum, YMaximum] would otherwise be drawn past the
        // frame and over the surrounding UI.
        canvas.Save();
        canvas.ClipRect(new SKRect(plotLeft, plotTop, plotRight, plotBottom));
        canvas.DrawPath(path, curvePaint);
        canvas.Restore();

        base.OnPaintSurface(e);
    }

    private static double CalculateNiceStep(double range, int targetTickCount)
    {
        if (range <= 0 || targetTickCount <= 0)
        {
            return 1;
        }

        double roughStep = range / targetTickCount;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
        double normalized = roughStep / magnitude;

        double niceNormalized = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10
        };

        return niceNormalized * magnitude;
    }

    private static int GetDecimalPlaces(double step)
    {
        if (step <= 0)
        {
            return 0;
        }

        return Math.Max(0, (int)Math.Ceiling(-Math.Log10(step)));
    }
}
