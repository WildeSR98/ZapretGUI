using System.IO;
using System.Net.Http;
using System.Security.Principal;
using System.Windows;
using ZapretGUI.AI.Services;
using ZapretGUI.Core.Models;
using ZapretGUI.Core.Services;
using ZapretGUI.Services;
using ZapretGUI.Updater.Services;
using ZapretGUI.ViewModels;
using ZapretGUI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Application = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace ZapretGUI;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Log.Logger = ConfigureSerilog(new LoggerConfiguration()).CreateLogger();

        try
        {
            _host = Host.CreateDefaultBuilder(e.Args)
                .UseContentRoot(AppContext.BaseDirectory)
                .UseSerilog((_, _, loggerConfiguration) => ConfigureSerilog(loggerConfiguration))
                .ConfigureServices(ConfigureApplicationServices)
                .Build();

            await _host.StartAsync();

            Log.Information("ZapretGUI application host started. Arguments: {Arguments}", e.Args);

            if (!IsRunningAsAdmin())
            {
                Log.Warning("ZapretGUI is running without administrator privileges.");

                // Временно переключаем, чтобы закрытие диалога не завершило приложение.
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var prompt = new AdminPromptWindow();
                prompt.ShowDialog();

                if (!prompt.ContinueWithoutAdmin)
                {
                    Log.Information("User declined to continue without administrator privileges.");
                    Shutdown();
                    return;
                }
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "ZapretGUI failed to start.");

            WpfMessageBox.Show(
                $"ZapretGUI не удалось запустить.\n\n{ex.Message}",
                "Критическая ошибка запуска",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                Log.Information("Stopping ZapretGUI application host.");
                _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                _host.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ZapretGUI application host stopped with errors.");
        }
        finally
        {
            Log.Information("ZapretGUI application exited with code {ExitCode}.", e.ApplicationExitCode);
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    private static void ConfigureApplicationServices(IServiceCollection services)
    {
        // Named HttpClient для апдейтера с Polly стандартной resilience-стратегией:
        // retry (3 попытки с экспоненциальной задержкой) + circuit breaker + таймаут.
        services.AddHttpClient(ZapretGUI.Updater.Services.HttpClientNames.Updater, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.Add("User-Agent", "ZapretGUI-Updater");
        })
        ;

        // Named HttpClient для проверки связности (оркестратор).
        services.AddHttpClient(ZapretGUI.Core.Services.HttpClientNames.Connectivity, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
        })
        ;

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IUpdaterService, UpdaterService>();
        services.AddSingleton<IAppUpdaterService, AppUpdaterService>();
        services.AddSingleton<IConnectivityChecker, ConnectivityChecker>();

        services.AddSingleton<NetworkFingerprintProvider>();
        services.AddSingleton(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var dir = Path.GetDirectoryName(settingsService.SettingsPath)!;
            var registryPath = Path.Combine(dir, "ZapretGUI-ai-strategies.json");
            var registry = new AiStrategyRegistry(registryPath);
            registry.Load();
            return registry;
        });
        services.AddSingleton(sp =>
        {
            var settingsService = sp.GetRequiredService<ISettingsService>();
            var dir = Path.GetDirectoryName(settingsService.SettingsPath)!;
            return new AiHistoryStore(Path.Combine(dir, "ZapretGUI-ai-history.jsonl"));
        });
        services.AddSingleton<BatMaterializer>();
        services.AddSingleton(sp =>
            new BanditSelector(sp.GetRequiredService<AiStrategyRegistry>(), new Random()));
        services.AddSingleton(sp =>
            new StrategyEvolver(
                sp.GetRequiredService<AiStrategyRegistry>(),
                sp.GetRequiredService<AiHistoryStore>(),
                sp.GetRequiredService<BatMaterializer>(),
                () => Path.Combine(AppContext.BaseDirectory, "engine"),
                () => sp.GetRequiredService<ISettingsService>().Load().Ai));

        services.AddSingleton(sp =>
            new NetworkChangeWatcher(sp.GetRequiredService<NetworkFingerprintProvider>()));

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<MainWindow>();
    }

    private static LoggerConfiguration ConfigureSerilog(LoggerConfiguration loggerConfiguration)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZapretGUI",
            "logs");

        Directory.CreateDirectory(logDirectory);

        return loggerConfiguration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "ZapretGUI-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}");
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
