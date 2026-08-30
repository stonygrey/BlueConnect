// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

using System.Reflection;

using MetroLog.Maui;
using MetroLog.MicrosoftExtensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;

using BarcodeScanning;

using MudBlazor;
using MudBlazor.Services;

using ReactiveUI.Reactive.Builder;

using StonyGrey.BlueConnect.Abstractions;

using Zaffre.Services;
using Zaffre.Services.Abstractions;
using Zaffre.ViewModels;

namespace Zaffre;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // ReactiveUI 24 requires explicit initialization before any of its
        // types are touched (WhenAnyValue/ToProperty throw otherwise).
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithBlazor()
            .BuildApp();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseBarcodeScanning()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Embedded appsettings.json; the ConfigurationManager reads the
        // stream eagerly, so disposing right after the add is safe.
        using (var appSettings = Assembly.GetExecutingAssembly().GetManifestResourceStream("Zaffre.appsettings.json"))
        {
            if (appSettings is not null)
            {
                builder.Configuration.AddJsonStream(appSettings);
            }
        }

        // MetroLog: in-memory + rolling file. Services depend only on ILogger<T>.
        // Unsandboxed Catalyst maps AppDataDirectory to ~/Library and
        // LocalApplicationData to ~/Documents (TCC). Pin the Mac folder.
#if MACCATALYST
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "Zaffre", "logs");
#else
        var logDir = Path.Combine(FileSystem.AppDataDirectory, "logs");
#endif
        Directory.CreateDirectory(logDir);
        builder.Logging.AddDebug();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddInMemoryLogger(options =>
        {
            options.MinLevel = LogLevel.Trace;
        });
        builder.Logging.AddStreamingFileLogger(options =>
        {
            options.MinLevel = LogLevel.Trace;
            options.RetainDays = 7;
            options.FolderPath = logDir;
        });
        // Floor: Information. appsettings.json may raise a category (or
        // lower StonyGrey.BlueConnect / Zaffre to Debug for more detail).
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices(config =>
        {
            // Toasts sit at the bottom, clear of the fixed app bar and the notch.
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
            config.SnackbarConfiguration.VisibleStateDuration = 4000;
            config.SnackbarConfiguration.ShowCloseIcon = true;
        });
        builder.Services.AddSingleton(MetroLog.Operators.LogOperatorRetriever.Instance);

        builder.Services.Configure<BlueConnectOptions>(builder.Configuration.GetSection("BlueConnect"));
        RegisterScanner(builder.Services);
        builder.Services.AddSingleton<INoiseCredentialService, NoiseCredentialService>();
        builder.Services.AddSingleton<IToasts, ToastService>();
        builder.Services.AddSingleton<IPakeCredentialService, PakeCredentialService>();
        builder.Services.AddSingleton<IEdhocCredentialService, EdhocCredentialService>();
#if WINDOWS
        // The package has a Windows TFM but no scanner backend; the PAKE
        // tab's manual entry carries the flow there.
        builder.Services.AddSingleton<IQrScanService, UnavailableQrScanService>();
#else
        builder.Services.AddSingleton<IQrScanService, QrScanService>();
#endif
        builder.Services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        builder.Services.AddSingleton<ICommandService, CommandService>();
        builder.Services.AddSingleton<DeviceSelection>();
        builder.Services.AddSingleton<ScanViewModel>();
        builder.Services.AddSingleton<TestViewModel>();
        builder.Services.AddSingleton<LowPrivilegeCommandsViewModel>();
        builder.Services.AddSingleton<HighPrivilegeCommandsViewModel>();
        builder.Services.AddSingleton<PakeCredentialsViewModel>();
        builder.Services.AddSingleton<EdhocCredentialsViewModel>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        var app = builder.Build();
        Console.WriteLine($"ZAFFRE-LOG-DIR: {logDir}");
#if DEBUG
        // Console diagnostics for headless verification: visible when the app
        // is launched from a terminal (Catalyst/Windows).
        app.Services.GetRequiredService<IScanner>()
            .Subscribe(s => Console.WriteLine(
                $"ZAFFRE-SCAN: {s.Type} '{s.Advertisement.LocalName}' {s.Advertisement.Id} {s.Advertisement.Rssi.ValueDbm}dBm"));
        app.Services.GetRequiredService<IDiagnosticsService>()
            .Subscribe(s => Console.WriteLine(
                $"ZAFFRE-TEST: running={s.IsRunning} tests={s.TestCount} connFail={s.ConnectionFailures} cmdFail={s.CommandFailures} " +
                $"min={s.MinConnectTime.TotalMilliseconds:F0}ms avg={s.AverageConnectTime.TotalMilliseconds:F0}ms max={s.MaxConnectTime.TotalMilliseconds:F0}ms"));
#endif
        return app;
    }

    private static void RegisterScanner(IServiceCollection services)
    {
#if ANDROID
        services.AddSingleton<IScanner, StonyGrey.BlueConnect.Droid.Scanner>();
#elif WINDOWS
        services.AddSingleton<IScanner, StonyGrey.BlueConnect.WinRT.Scanner>();
#else
#if DEBUG
        // The iOS simulator has no Bluetooth; fake the BLE layer there so the
        // full scan/test flow can be exercised in the UI.
        if (DeviceInfo.Current.DeviceType == DeviceType.Virtual)
        {
            services.AddSingleton<IScanner, Services.Simulated.SimulatedScanner>();
            return;
        }
#endif
        services.AddSingleton<IScanner, StonyGrey.BlueConnect.CoreBluetooth.Scanner>();
#endif
    }
}
