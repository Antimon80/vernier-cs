using Backend.Devices.GoDirect;
using Backend.Discovery;
using Backend.Measurements;
using Microsoft.Extensions.Logging;

using System.Windows.Forms;
using ScottPlot;
using ScottPlot.WinForms;
using System.Data.SqlTypes;
using System.Globalization;

internal static class Program
{
    static Program()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
    }

    [STAThread]
    private static async Task Main(string[] args)
    {
        using ILoggerFactory loggerFactory = LoggerFactory.Create(b =>
        {
            b.SetMinimumLevel(LogLevel.Information);
            b.AddConsole();
        });

        using DeviceManager deviceManager = new(loggerFactory);

        Console.WriteLine("Spectrometer Testing CLI");
        Console.WriteLine("Discovering devices ...");

        var devices = deviceManager.ListDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("No supported device found.");
            return;
        }

        if (devices.Count == 1)
        {
            Console.WriteLine($"Found 1 device: {devices[0].Name}");
            await deviceManager.Connect(0);

            PrintStatus(deviceManager);
        }
        else
        {
            Console.WriteLine($"Found {devices.Count} devices. Select one with: select <index>");
            PrintDevices(devices);
        }

        PrintHelp();

        while (true)
        {
            Console.Write("> ");
            string? userInput = Console.ReadLine();
            if (string.IsNullOrEmpty(userInput))
            {
                continue;
            }

            string[] parts = userInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();

            try
            {
                switch (command)
                {
                    case "help":
                    case "?":
                        PrintHelp();
                        break;

                    case "exit":
                    case "quit":
                        return;

                    case "list":
                        devices = deviceManager.ListDevices();
                        PrintDevices(devices);
                        break;

                    case "status":
                        PrintStatus(deviceManager);
                        break;

                    case "init":
                        EnsureConnected(deviceManager);
                        await deviceManager.CurrentSpectrometer!.Initialize();
                        Console.WriteLine("OK: initialized.");
                        PrintWarnings(deviceManager);
                        break;

                    case "mode":
                        RequireArgs(parts, 2);
                        EnsureConnected(deviceManager);
                        var mode = ParseMode(parts[1]);
                        await deviceManager.CurrentSpectrometer!.SetOperatingMode(mode);
                        Console.WriteLine($"OK: mode set to {mode}");
                        break;

                    case "cal":
                    case "calibrate":
                        EnsureConnected(deviceManager);
                        await deviceManager.CurrentSpectrometer!.Calibrate();
                        Console.WriteLine("OK: calibrated.");
                        PrintWarnings(deviceManager);
                        break;

                    case "it":
                        RequireArgs(parts, 2);
                        EnsureConnected(deviceManager);
                        int ms = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        await deviceManager.CurrentSpectrometer!.SetIntegrationTime(ms);
                        Console.WriteLine($"OK: integratin time set to {ms} ms (echoed stored in session).");
                        break;

                    case "meas":
                        EnsureConnected(deviceManager);
                        var spectrometer = deviceManager.CurrentSpectrometer!;
                        var raw = await spectrometer.AcquireSingleSpectrum();
                        var displaySpectrum = SpectrumConverter.Compute(spectrometer.Model, spectrometer.Session, raw);

                        var x = displaySpectrum.WavelengthNm;
                        var y = displaySpectrum.YAxis;

                        var (xf, yf) = FilterNaN(x, y);

                        ShowSpectrum(
                            title: $"{spectrometer.DeviceName} - {spectrometer.Mode}",
                            x: xf, y: yf,
                            xLabel: "Wavelength [nm]",
                            yLabel: displaySpectrum.Spectrum.ToString());

                        Console.WriteLine("OK: measurement displayed.");
                        break;

                    case "disconnect":
                        await deviceManager.Disconnect();
                        Console.WriteLine("OK: disconnected.");
                        break;

                    default:
                        Console.WriteLine("Unknown command. Type 'help'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine(" list                                - list connected devices");
        Console.WriteLine(" select <i>                          - connect to device by index");
        Console.WriteLine(" status                              - show current device");
        Console.WriteLine(" init                                - run Initialize()");
        Console.WriteLine(" mode<abs|trans|f405|f500|int|raw>   - select operating mode");
        Console.WriteLine(" it <ms>                             - set integration time");
        Console.WriteLine(" cal                                 - calibrate (abs/trans only, needs white lamp and blank)");
        Console.WriteLine(" meas                                - acquire single spectrum + show chart");
        Console.WriteLine(" disconnect                          - disconnect current device");
        Console.WriteLine(" help                                - show help");
        Console.WriteLine(" exit                                - quit program");
        Console.WriteLine();
    }

    private static void PrintStatus(DeviceManager deviceManager)
    {
        if (deviceManager.CurrentDevice is null)
        {
            Console.WriteLine("No device connected.");
            return;
        }

        ISpectrometer? spectrometer = deviceManager.CurrentSpectrometer;
        Console.WriteLine($"Connected: {spectrometer?.DeviceName} VID=0x{spectrometer?.Vid:X4} PID=0x{spectrometer?.Pid:X4} Mode={spectrometer?.Mode} Connected={spectrometer?.IsConnected}");
        Console.WriteLine($"Model: packets={spectrometer?.Model.PacketCount}, payloadBytes={spectrometer?.Model.PacketPayloadBytes}, white={spectrometer?.Model.HasWhiteLamp}, 405={spectrometer?.Model.HasLed405}, 500={spectrometer?.Model.HasLed500}");
        Console.WriteLine($"ROI: [{spectrometer?.Model.CCDPixelIndexMin}..{spectrometer?.Model.CCDPixelIndexMax}]  nm=[{spectrometer?.Model.WavelengthMinNm:F1}..{spectrometer?.Model.WavelengthMaxNm:F1}]");
        Console.WriteLine($"Session: ready={spectrometer?.Session.IsReady}, calibrated={spectrometer?.Session.IsCalibrated}, it={spectrometer?.Session.IntegrationTime}ms");
        PrintWarnings(deviceManager);
    }

    private static void PrintDevices(IReadOnlyList<DeviceDescriptor> devices)
    {
        for (int i = 0; i < devices.Count; i++)
        {
            DeviceDescriptor device = devices[i];
            Console.WriteLine($"[{i}] {device.Name} VID=0x{device.Vid:X4} PID=0x{device.Pid:X4}");
        }
    }

    private static void PrintWarnings(DeviceManager deviceManager)
    {
        ISpectrometer? spectrometer = deviceManager.CurrentSpectrometer;
        if (spectrometer is null)
        {
            return;
        }

        if (spectrometer.Warnings.Count > 0)
        {
            Console.WriteLine("Warnings:");
            foreach (string warning in spectrometer.Warnings)
            {
                Console.WriteLine($" - {warning}");
            }
        }
    }

    private static void EnsureConnected(DeviceManager deviceManager)
    {
        if (deviceManager.CurrentSpectrometer is null || !deviceManager.CurrentSpectrometer.IsConnected)
        {
            throw new InvalidOperationException("No connected spectrometer. Use 'select <i>' first.");
        }
    }

    private static void RequireArgs(string[] parts, int n)
    {
        if (parts.Length < n)
        {
            throw new InvalidOperationException("Missing arguments. Type 'help'.");
        }
    }

    private static OperatingMode ParseMode(string s)
    {
        return s.ToLowerInvariant() switch
        {
            "abs" or "absorbance" => OperatingMode.Absorbance,
            "trans" or "transmission" => OperatingMode.Transmission,
            "f405" or "fluorescence405" => OperatingMode.Fluorescence405,
            "f500" or "fluorescence500" => OperatingMode.Fluorescence500,
            "int" or "intensity" => OperatingMode.Intensity,
            "raw" or "rawcounts" => OperatingMode.RawCounts,
            _ => throw new ArgumentOutOfRangeException(nameof(s), $"Unknown mode '{s}.")
        };
    }

    private static void ShowSpectrum(string title, double[] x, double[] y, string xLabel, string yLabel)
    {
        using Form form = new()
        {
            Text = title,
            Width = 950,
            Height = 650,
            StartPosition = FormStartPosition.CenterScreen
        };

        FormsPlot plotControl = new() { Dock = DockStyle.Fill };
        plotControl.Plot.Add.Scatter(x, y);
        plotControl.Plot.XLabel(xLabel);
        plotControl.Plot.YLabel(yLabel);
        plotControl.Plot.Title(title);
        form.Controls.Add(plotControl);

        form.Shown += (_, __) => plotControl.Refresh();
        form.FormClosed += (_, __) => Application.ExitThread();

        Application.Run(form);
    }

    private static (double[] x2, double[] y2) FilterNaN(double[] x, double[] y)
    {
        List<double> xs = new List<double>(y.Length);
        List<double> ys = new List<double>(y.Length);

        for (int i = 0; i < y.Length; i++)
        {
            if (double.IsNaN(y[i]) || double.IsInfinity(y[i]))
            {
                continue;
            }

            xs.Add(x[i]);
            ys.Add(y[i]);
        }

        return (xs.ToArray(), ys.ToArray());
    }
}