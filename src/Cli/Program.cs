using Backend.Devices.GoDirect;
using Backend.Discovery;
using Backend.Measurements;
using Microsoft.Extensions.Logging;
using ScottPlot.WinForms;
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

        IReadOnlyList<DeviceDescriptor> devices = deviceManager.ListDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("No supported device found.");
            return;
        }

        if (devices.Count == 1)
        {
            Console.WriteLine($"Found 1 device: {devices[0].Name}");
            await deviceManager.Connect(0);

            Spectrometer spectrometer = EnsureConnected(deviceManager);
            PrintStatus(spectrometer);
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
            Spectrometer spectrometer = (Spectrometer)EnsureConnected(deviceManager);

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
                        PrintStatus(spectrometer);
                        break;

                    case "init":
                        await spectrometer.Initialize();
                        Console.WriteLine("OK: initialized.");
                        PrintWarnings(spectrometer);
                        break;

                    case "mode":
                        RequireArgs(parts, 2);
                        var mode = ParseMode(parts[1]);
                        await spectrometer.SetOperatingMode(mode);
                        Console.WriteLine($"OK: mode set to {mode}");
                        break;

                    case "cal":
                    case "calibrate":
                        await spectrometer.Calibrate();
                        Console.WriteLine("OK: calibrated.");
                        PrintWarnings(spectrometer);
                        break;

                    case "warmup":
                        RequireArgs(parts, 2);
                        Console.WriteLine($"OK: SkipWarmup set to {spectrometer.SkipWarmup} (true=skip warmup wait).");
                        break;

                    case "it":
                        RequireArgs(parts, 2);
                        int ms = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        await spectrometer.SetIntegrationTime(ms);
                        Console.WriteLine($"OK: integratin time set to {ms} ms (echoed stored in session).");
                        break;

                    case "meas":
                        ushort[] raw = await spectrometer.AcquireSingleSpectrum();
                        Spectrum displaySpectrum = SpectrumConverter.Compute(spectrometer.Model, spectrometer.Session, raw);

                        double[] x = displaySpectrum.WavelengthNm;
                        double[] y = displaySpectrum.YAxis;

                        (double[] xf, double[] yf) = FilterNaN(x, y);

                        ShowSpectrum(
                            title: $"{spectrometer.DeviceName} - {spectrometer.Mode}",
                            x: xf, y: yf,
                            xLabel: "Wavelength [nm]",
                            yLabel: displaySpectrum.Mode.ToString());

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
        Console.WriteLine(" warmup <on|off>                     - skip white lamp warmup wait (on=skip, off=normal)");
        Console.WriteLine(" meas                                - acquire single spectrum + show chart");
        Console.WriteLine(" disconnect                          - disconnect current device");
        Console.WriteLine(" help                                - show help");
        Console.WriteLine(" exit                                - quit program");
        Console.WriteLine();
    }

    private static void PrintStatus(Spectrometer spectrometer)
    {
        Console.WriteLine($"Connected: {spectrometer.DeviceName} VID=0x{spectrometer.Vid:X4} PID=0x{spectrometer.Pid:X4} Mode={spectrometer.Mode} Connected={spectrometer.IsConnected}");
        Console.WriteLine($"Model: packets={spectrometer.Model.PacketCount}, payloadBytes={spectrometer.Model.PacketPayloadBytes}, white={spectrometer.Model.HasWhiteLamp}, 405={spectrometer.Model.HasLed405}, 500={spectrometer.Model.HasLed500}");
        Console.WriteLine($"ROI: [{spectrometer.Model.CCDPixelIndexMin}..{spectrometer.Model.CCDPixelIndexMax}]  nm=[{spectrometer.Model.WavelengthMinNm:F1}..{spectrometer.Model.WavelengthMaxNm:F1}]");
        Console.WriteLine($"Session: ready={spectrometer.Session.IsReady}, calibrated={spectrometer.Session.IsCalibrated}, it={spectrometer.Session.IntegrationTime}ms");
        PrintWarnings(spectrometer);
    }

    private static void PrintDevices(IReadOnlyList<DeviceDescriptor> devices)
    {
        for (int i = 0; i < devices.Count; i++)
        {
            DeviceDescriptor device = devices[i];
            Console.WriteLine($"[{i}] {device.Name} VID=0x{device.Vid:X4} PID=0x{device.Pid:X4}");
        }
    }

    private static void PrintWarnings(Spectrometer spectrometer)
    {
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

    private static Spectrometer EnsureConnected(DeviceManager deviceManager)
    {
        if (deviceManager.CurrentSpectrometer is not Spectrometer spec || !spec.IsConnected)
        {
            throw new InvalidOperationException("No connected GoDirect spectrometer. Use 'select <i>' first.");
        }

        return spec;
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
        List<double> xs = new(y.Length);
        List<double> ys = new(y.Length);

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