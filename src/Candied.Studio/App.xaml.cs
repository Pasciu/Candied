using System;
using System.Threading.Tasks;
using System.Windows;
using Candied.Decompiler;
using Candied.Studio.Services;
using Candied.Studio.ViewModels;
using Candied.Studio.Views;
using Candied.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Candied.Studio;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            StudioLogger.Fatal($"DispatcherUnhandledException: {e.Exception.Message}", e.Exception);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                StudioLogger.Fatal($"AppDomain.CurrentDomain.UnhandledException: {ex.Message}", ex);
            }
            else
            {
                StudioLogger.Fatal($"AppDomain.CurrentDomain.UnhandledException: {e.ExceptionObject}");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            StudioLogger.Error($"TaskScheduler.UnobservedTaskException: {e.Exception.Message}", e.Exception);
        };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        StudioLogger.Info($"Candied Studio starting up. CommandLine: '{Environment.CommandLine}'");

        try
        {
            var services = new ServiceCollection();

            // Infrastructure adapters
            services.AddDecompilerInfrastructure();
            services.AddWorkspaceInfrastructure();

            // Studio services
            services.AddSingleton<IWindowsJobObject, WindowsJobObject>();
            services.AddSingleton<IUnityWindowLocator, UnityWindowLocator>();
            services.AddSingleton<IWindowEmbedder, Win32WindowEmbedder>();
            services.AddSingleton<IUnityGameHostController, UnityGameHostController>();
            services.AddSingleton<StudioHttpServer>();

            // UI components
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider();

            var httpServer = _serviceProvider.GetRequiredService<StudioHttpServer>();
            httpServer.Start();

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.EnsureVisibleAndActive();
        }
        catch (Exception ex)
        {
            StudioLogger.Fatal($"Failed during Studio OnStartup: {ex.Message}", ex);
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StudioLogger.Info($"Candied Studio exiting. ExitCode: {e.ApplicationExitCode}");
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
