using System.Threading;
using System.Windows;

namespace CodexPetLimitRings.Windows;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private MainController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "CodexPetLimitRings.Windows.SingleInstance", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        try
        {
            _controller = new MainController();
            _controller.Start();
            Services.AppLog.Write("Codex Pet HUD started.");
        }
        catch (Exception error)
        {
            Services.AppLog.Write($"Startup failed: {error.GetType().Name}: {error.Message}");
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
