using System.IO;

using Serilog;

using WindowsOptimizer.Services;

namespace Optimizer.Helpers
{
    /// <summary>Configures Serilog file logging and routes the engine's diagnostics into it.</summary>
    public static class AppLogging
    {
        public static string LogDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Optimizer", "logs");

        public static void Initialize()
        {
            Directory.CreateDirectory(LogDirectory);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    Path.Combine(LogDirectory, "optimizer-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            // Route engine diagnostics (EngineLog) into Serilog.
            EngineLog.Configure((message, ex) =>
            {
                if (ex != null)
                {
                    Log.Error(ex, "{Message}", message);
                }
                else
                {
                    Log.Debug("{Message}", message);
                }
            });

            Log.Information("Optimizer starting (logging initialized).");
        }

        public static void Shutdown()
        {
            Log.Information("Optimizer exiting.");
            Log.CloseAndFlush();
        }
    }
}
