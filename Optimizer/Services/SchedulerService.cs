using System.Diagnostics;

namespace Optimizer.Services
{
    public interface ISchedulerService
    {
        bool ScheduleOnLogon(string profileId, string profileName);
        bool ScheduleDaily(string profileId, string profileName, string time24h);
        bool RemoveSchedule(string profileName);
        IReadOnlyList<string> ListSchedules();
    }

    /// <summary>
    /// Creates Windows Task Scheduler jobs that relaunch Optimizer in headless mode to apply a
    /// profile (on logon or at a daily time). Tasks run with highest privileges so admin-level
    /// tweaks apply without a UAC prompt.
    /// </summary>
    public class SchedulerService : ISchedulerService
    {
        private const string Prefix = "Optimizer-";

        public bool ScheduleOnLogon(string profileId, string profileName)
            => CreateTask(profileName, profileId, "/sc onlogon");

        public bool ScheduleDaily(string profileId, string profileName, string time24h)
            => CreateTask(profileName, profileId, $"/sc daily /st {time24h}");

        private static bool CreateTask(string profileName, string profileId, string scheduleArgs)
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exe)) return false;

            var taskName = Prefix + Sanitize(profileName);
            var run = $"\\\"{exe}\\\" --apply-profile={profileId}";
            // /rl highest = run elevated; /f = overwrite if exists.
            var args = $"/create /tn \"{taskName}\" /tr \"{run}\" {scheduleArgs} /rl highest /f";
            return RunSchTasks(args);
        }

        public bool RemoveSchedule(string profileName)
            => RunSchTasks($"/delete /tn \"{Prefix + Sanitize(profileName)}\" /f");

        public IReadOnlyList<string> ListSchedules()
        {
            var result = new List<string>();
            try
            {
                var output = RunSchTasksOutput("/query /fo csv /nh");
                foreach (var line in output.Split('\n'))
                {
                    var name = line.Split(',').FirstOrDefault()?.Trim('"', ' ', '\r');
                    if (!string.IsNullOrEmpty(name) && name.Contains(Prefix))
                    {
                        result.Add(name.TrimStart('\\'));
                    }
                }
            }
            catch { /* ignore */ }
            return result;
        }

        private static string Sanitize(string name)
        {
            foreach (var c in new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' })
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private static bool RunSchTasks(string arguments)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("schtasks", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                if (p == null) return false;
                p.WaitForExit(10000);
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "schtasks failed: {Args}", arguments);
                return false;
            }
        }

        private static string RunSchTasksOutput(string arguments)
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            });
            if (p == null) return string.Empty;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return output;
        }
    }
}
