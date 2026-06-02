using System.IO;

namespace Optimizer.Helpers
{
    /// <summary>
    /// Resolves the Syncfusion license key from outside the source tree so it is never
    /// committed. Lookup order:
    ///   1. Environment variable SYNCFUSION_LICENSE_KEY (preferred for CI / secrets)
    ///   2. A "license.key" file next to the executable, then in the working directory
    /// Returns null when no key is configured (the Syncfusion trial dialog will then appear).
    /// </summary>
    public static class LicenseConfig
    {
        public const string EnvVarName = "SYNCFUSION_LICENSE_KEY";
        public const string FileName = "license.key";

        public static string? GetSyncfusionKey()
        {
            var fromEnv = Environment.GetEnvironmentVariable(EnvVarName);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return fromEnv.Trim();
            }

            foreach (var dir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                try
                {
                    var path = Path.Combine(dir, FileName);
                    if (File.Exists(path))
                    {
                        var text = File.ReadAllText(path).Trim();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
                catch
                {
                    // Ignore unreadable locations and fall through.
                }
            }

            return null;
        }
    }
}
