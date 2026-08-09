using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace App.Views.Controls;

public sealed partial class MeasurementChart : SKCanvasView
{

    private const float TopMargin = 10f;
    private const int TickCount = 5;
    private const float TickLength = 6f;
    private const float TickLableGap = 8f;
    private const int MinorTicksPerMajor = 4;

    public static readonly BindableProperty XMinimumProperty = BindableProperty.Create(
        nameof(XMinimum), typeof(double), typeof(MeasurementChart), 0.0,
        propertyChanged: (bindable, _, _) => ((MeasurementChart)bindable).InvalidateSurface());

    public static readonly BindableProperty XMaximumProperty = BindableProperty.Create(
        nameof(XMaximum), typeof(double), typeof(MeasurementChart), 0.0,
        propertyChanged: (bindable, _, _) => ((MeasurementChart)bindable).InvalidateSurface());

    public static readonly BindableProperty YMinimumProperty = BindableProperty.Create(
        nameof(YMinimum), typeof(double), typeof(MeasurementChart), 0.0,
        propertyChanged: (bindable, _, _) => ((MeasurementChart)bindable).InvalidateSurface());

    public static readonly BindableProperty YMaximumProperty = BindableProperty.Create(
        nameof(YMaximum), typeof(double), typeof(MeasurementChart), 0.0,
        propertyChanged: (bindable, _, _) => ((MeasurementChart)bindable).InvalidateSurface());

    public static readonly BindableProperty XValuesProperty = BindableProperty.Create(
        nameof(XValues), typeof(IReadOnlyList<double>), typeof(MeasurementChart), Array.Empty<double>(),
        propertyChanged: (bindable, _, _) => ((MeasurementChart)bindable).InvalidateSurface());

    public static readonly BindableProperty YValuesProperty = BindableProperty.Create(
        nameof(YValues), typeof(IReadOnlyList<double>), typeof(MeasurementChart), Array.Empty<double>(),
        propertyChanged: (bindable, _, _) => ((MeasurementChart)bindable).InvalidateSurface());

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

    public double YMinimum
    {
        get => (double)GetValue(YMinimumProperty);
        set => SetValue(YMinimumProperty, value);
    }

    public double YMaximum
    {
        get => (double)GetValue(YMaximumProperty);
        set => SetValue(YMaximumProperty, value);
    }

    public IReadOnlyList<double> XValues
    {
        get => (IReadOnlyList<double>)GetValue(XValuesProperty);
        set => SetValue(XValuesProperty, value);
    }

    public IReadOnlyList<double> YValues
    {
        get => (IReadOnlyList<double>)GetValue(YValuesProperty);
        set => SetValue(YValuesProperty, value);
    }

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

        float leftMargin = TickLength + TickLableGap + maxYLabelWidth + 4f;
        float rightMargin = rightmostXLabelWidth / 2f + 4f;
        float bottomMargin = TickLength + TickLableGap + font.Size + 6f;

        float plotLeft = leftMargin;
        float plotTop = TopMargin;
        float plotRight = width - rightMargin;
        float plotBottom = height - bottomMargin;

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
            canvas.DrawText(xValue.ToString("F" + xDecimals), tickX, plotBottom + TickLength + TickLableGap + font.Size, SKTextAlign.Center, font, textPaint);
        }

        for (double yValue = firstYTick; yValue <= YMaximum + yStep * 0.001; yValue += yStep)
        {
            float tickY = ToPixelY(yValue);
            canvas.DrawLine(plotLeft - TickLength, tickY, plotLeft, tickY, tickPaint);
            canvas.DrawText(yValue.ToString("F" + yDecimals), plotLeft - TickLength - TickLableGap, tickY + font.Size / 3, SKTextAlign.Right, font, textPaint);
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

        canvas.DrawPath(path, curvePaint);

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