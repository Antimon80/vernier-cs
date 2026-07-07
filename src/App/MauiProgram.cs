using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Backend.Discovery;
using App.ViewModels;
using App.Views;
using App.Views.GoDirect;
using App.Services;

namespace App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            IDeviceManager? deviceManager = null;

            var builder = MauiApp.CreateBuilder();

            builder.UseMauiApp<App>().ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            }).ConfigureLifecycleEvents(events =>
{
#if WINDOWS
    events.AddWindows(windows =>
    {
        windows.OnClosed((window, args) =>
        {
            try
            {
                deviceManager?.Dispose();
            }
            catch
            {
                // App is closing. Do not throw from lifecycle cleanup.
            }
        });

        windows.OnPlatformMessage((window, args) =>
        {
            const uint WM_DEVICECHANGE = 0x0219;

            const long DBT_DEVNODES_CHANGED = 0x0007;
            const long DBT_DEVICEARRIVAL = 0x8000;
            const long DBT_DEVICEREMOVECOMPLETE = 0x8004;

            if (args.MessageId != WM_DEVICECHANGE)
            {
                return;
            }

            long eventCode = (long)args.WParam;

            if (eventCode == DBT_DEVNODES_CHANGED ||
                eventCode == DBT_DEVICEARRIVAL ||
                eventCode == DBT_DEVICEREMOVECOMPLETE)
            {
                deviceManager?.NotifyDeviceTopologyChanged();
            }
        });
    });
#endif
});

#if DEBUG
            builder.Logging.SetMinimumLevel(LogLevel.Trace);
            builder.Logging.AddDebug();
            builder.Logging.AddConsole();
#endif

            builder.Services.AddSingleton<IDeviceManager>(sp => new DeviceManager(sp.GetService<ILoggerFactory>()));
            builder.Services.AddTransient<DeviceSelectionViewModel>();
            builder.Services.AddTransient<DeviceSelectionPage>();

            builder.Services.AddTransient<MeasurementPage>();
            builder.Services.AddTransient<MeasurementViewModel>();

            builder.Services.AddSingleton<LocalizationService>();

            var app = builder.Build();

            deviceManager = app.Services.GetRequiredService<IDeviceManager>();

            return app;
        }
    }
}
