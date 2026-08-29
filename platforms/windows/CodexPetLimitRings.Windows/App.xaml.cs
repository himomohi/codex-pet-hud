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
        var inputSelfTestIndex = Array.FindIndex(
            e.Args,
            argument => string.Equals(
                argument,
                "--input-relay-self-test",
                StringComparison.OrdinalIgnoreCase));
        if (inputSelfTestIndex >= 0)
        {
            var outputPath = inputSelfTestIndex + 1 < e.Args.Length
                ? Path.GetFullPath(e.Args[inputSelfTestIndex + 1])
                : Path.Combine(Path.GetTempPath(), "codex-pet-input-relay-self-test.json");
            Dispatcher.BeginInvoke(async () =>
            {
                var exitCode = await InputRelaySelfTest.RunAsync(outputPath);
                Shutdown(exitCode);
            });
            return;
        }

        _singleInstance = new Mutex(true, "CodexPetLimitRings.Windows.SingleInstance", out var created);
        if (!created)
        {
            Shutdown();
            return;
        }

        try
        {
            _controller = new MainController();
            var captureIndex = Array.FindIndex(
                e.Args,
                argument => string.Equals(argument, "--verify-capture", StringComparison.OrdinalIgnoreCase));
            var verificationCapturePath = captureIndex >= 0 && captureIndex + 1 < e.Args.Length
                ? Path.GetFullPath(e.Args[captureIndex + 1])
                : null;
            _controller.Start(
                e.Args.Any(argument => string.Equals(argument, "--settings", StringComparison.OrdinalIgnoreCase)),
                verificationCapturePath);
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
