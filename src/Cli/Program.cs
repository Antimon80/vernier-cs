using Backend.Devices.GoDirect;
using Backend.Discovery;
using Backend.Measurements;
using HidSharp;
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
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
        });

        using DeviceManager deviceManager = new(loggerFactory);

        Console.WriteLine("Vernier Testing CLI");
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

            if (deviceManager.CurrentSpectrometer is Spectrometer spectrometer)
            {
                PrintStatus(spectrometer);
            }
            else if (deviceManager.CurrentDevice is not null)
            {
                Console.WriteLine(
                    $"Connected: {deviceManager.CurrentDevice.DeviceName} " +
                    $"VID=0x{deviceManager.CurrentDevice.Vid:X4} " +
                    $"PID=0x{deviceManager.CurrentDevice.Pid:X4}");
            }
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

            if (string.IsNullOrWhiteSpace(userInput))
            {
                continue;
            }

            string[] parts = userInput.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();

            Spectrometer? spectrometer = deviceManager.CurrentSpectrometer as Spectrometer;

            try
            {
                switch (command)
                {
                    case "help":
                        PrintHelp();
                        break;

                    case "exit":
                    case "quit":
                        if (deviceManager.CurrentDevice is not null)
                        {
                            await deviceManager.Disconnect();
                        }

                        return;

                    case "list":
                        devices = deviceManager.ListDevices();
                        PrintDevices(devices);
                        break;

                    case "select":
                        RequireArgs(parts, 2);

                        int index = int.Parse(parts[1], CultureInfo.InvariantCulture);

                        devices = deviceManager.ListDevices();

                        if (index < 0 || index >= devices.Count)
                        {
                            throw new ArgumentOutOfRangeException(
                                nameof(index),
                                $"Device index must be between 0 and {devices.Count - 1}.");
                        }

                        if (spectrometer is not null)
                        {
                            await deviceManager.Disconnect();
                        }

                        await deviceManager.Connect(index);

                        if (deviceManager.CurrentSpectrometer is Spectrometer selectedSpectrometer)
                        {
                            spectrometer = selectedSpectrometer;

                            Console.WriteLine(
                                $"OK: connected to {spectrometer.DeviceName}.");

                            PrintStatus(spectrometer);
                        }
                        else if (deviceManager.CurrentDevice is not null)
                        {
                            spectrometer = null;

                            Console.WriteLine(
                                $"OK: connected to {deviceManager.CurrentDevice.DeviceName}.");
                        }
                        break;

                    case "disconnect":
                        if (deviceManager.CurrentDevice is null)
                        {
                            Console.WriteLine("No device is connected.");
                            break;
                        }

                        await deviceManager.Disconnect();
                        spectrometer = null;

                        Console.WriteLine("OK: disconnected.");
                        break;

                    case "status":
                        spectrometer ??= EnsureSpectrometerConnected(deviceManager);
                        PrintStatus(spectrometer);
                        break;

                    case "init":
                        spectrometer ??= EnsureSpectrometerConnected(deviceManager);

                        await spectrometer.Initialize();

                        Console.WriteLine("OK: initialized.");
                        PrintWarnings(spectrometer);
                        break;

                    case "mode":
                        RequireArgs(parts, 2);
                        spectrometer ??= EnsureSpectrometerConnected(deviceManager);

                        OperatingMode mode = ParseMode(parts[1]);

                        await spectrometer.SetOperatingMode(mode);

                        Console.WriteLine(
                            $"OK: mode set to {spectrometer.Session.Mode}.");
                        break;

                    case "it":
                        RequireArgs(parts, 2);
                        spectrometer ??= EnsureSpectrometerConnected(deviceManager);

                        int requestedMs = int.Parse(parts[1], CultureInfo.InvariantCulture);

                        await spectrometer.SetIntegrationTime(requestedMs);

                        Console.WriteLine($"OK: integration time set to " + $"{spectrometer.Session.IntegrationTime} ms.");
                        break;

                    case "warmup":
                        RequireArgs(parts, 2);
                        spectrometer ??= EnsureSpectrometerConnected(deviceManager);

                        spectrometer.SkipWarmup = ParseOnOff(parts[1]);
                        Console.WriteLine($"OK: warm-up skip is " + $"{(spectrometer.SkipWarmup ? "enabled" : "disabled")}.");
                        break;

                    case "cal":
                    case "calibrate":
                        spectrometer ??= EnsureSpectrometerConnected(deviceManager);

                        await spectrometer.Calibrate();

                        Console.WriteLine("OK: calibrated.");
                        PrintWarnings(spectrometer);
                        break;

                    case "meas":
                        spectrometer ??= EnsureSpectrometerConnected(deviceManager);

                        await spectrometer.AcquireSingleSpectrum();

                        Spectrum spectrum =
                            spectrometer.Session.CurrentSpectrum ?? throw new InvalidOperationException(
                                "The measurement produced no processed spectrum.");

                        ShowSpectrum(spectrometer, spectrum);

                        Console.WriteLine(
                            $"OK: {spectrum.Mode} measurement displayed.");
                        break;

                    default:
                        Console.WriteLine(
                            "Unknown command. Type 'help'.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"ERROR: {ex.Message}");
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
        Console.WriteLine(" warmup <on|off>                     - skip white lamp warmup wait (on=normal, off=skip)");
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
        Console.WriteLine($"Session: ready={spectrometer.Session.IsInitialized}, calibrated={spectrometer.Session.IsCalibrated}, it={spectrometer.Session.IntegrationTime}ms");
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
    }

    private static Spectrometer EnsureSpectrometerConnected(DeviceManager deviceManager)
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

    private static bool ParseOnOff(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "on" or "true" => false,
            "off" or "false" => true,
            _ => throw new ArgumentOutOfRangeException(nameof(value), "Expected 'on' or 'off'.")
        };
    }

    private static void ShowSpectrum(Spectrometer spectrometer, Spectrum spectrum)
    {
        (double[] x, double[] y) = FilterFiniteValues(spectrum.WavelengthNm, spectrum.YAxis);

        using Form form = CreatePlotForm($"{spectrometer.DeviceName} - {spectrum.Mode}");

        FormsPlot plotControl = CreatePlotControl(spectrum.Mode);
        plotControl.Plot.Add.Scatter(x, y);

        form.Controls.Add(plotControl);
        form.Shown += (_, __) => plotControl.Refresh();

        Application.Run(form);
    }

    private static Form CreatePlotForm(string title)
    {
        return new Form
        {
            Text = title,
            Width = 950,
            Height = 650,
            StartPosition = FormStartPosition.CenterScreen
        };
    }

    private static FormsPlot CreatePlotControl(OperatingMode mode)
    {
        FormsPlot plotControl = new()
        {
            Dock = DockStyle.Fill
        };

        plotControl.Plot.XLabel("Wavelength [nm]");
        plotControl.Plot.YLabel(GetYAxisLabel(mode));

        return plotControl;
    }

    private static string GetYAxisLabel(OperatingMode mode)
    {
        return mode switch
        {
            OperatingMode.RawCounts => "Raw Counts",
            OperatingMode.Intensity => "Relative Intensity",
            OperatingMode.Fluorescence405 or OperatingMode.Fluorescence500 => "Relative fluorescence intensity",
            OperatingMode.Transmission => "Transmission [%]",
            OperatingMode.Absorbance => "Absorbance [a.u.]",
            _ => mode.ToString()
        };
    }

    private static (double[] x, double[] y) FilterFiniteValues(double[] x, double[] y)
    {
        if (x.Length != y.Length)
        {
            throw new ArgumentException("X and Y arrays must have equal length.");
        }

        List<double> filteredX = new(y.Length);
        List<double> filteredY = new(y.Length);

        for (int i = 0; i < y.Length; i++)
        {
            if (!double.IsFinite(y[i]) || !double.IsFinite(x[i]))
            {
                continue;
            }

            filteredX.Add(x[i]);
            filteredY.Add(y[i]);
        }

        return (filteredX.ToArray(), filteredY.ToArray());
    }
}