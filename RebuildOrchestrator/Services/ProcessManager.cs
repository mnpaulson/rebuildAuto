using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RebuildOrchestrator.Models;

namespace RebuildOrchestrator.Services
{
    public class ProcessManager
    {
        public class BotProcessState
        {
            public string ProfileName { get; set; } = "";
            public string AccountId { get; set; } = "";
            public Process? Process { get; set; }
            public int ProcessId => Process != null && !Process.HasExited ? Process.Id : 0;
            public DateTime StartTime { get; set; } = DateTime.UtcNow;
            public double CpuPercent { get; set; } = 0.0;
            public double RamMb { get; set; } = 0.0;
            public bool AutoRestart { get; set; } = false;

            private TimeSpan lastTotalProcessorTime = TimeSpan.Zero;
            private DateTime lastCpuCheckTime = DateTime.UtcNow;

            public void UpdatePerformanceMetrics()
            {
                if (Process == null || Process.HasExited)
                {
                    CpuPercent = 0.0;
                    RamMb = 0.0;
                    return;
                }

                try
                {
                    Process.Refresh();
                    RamMb = Process.WorkingSet64 / (1024.0 * 1024.0);

                    var now = DateTime.UtcNow;
                    var totalTime = Process.TotalProcessorTime;

                    if (lastCpuCheckTime != DateTime.MinValue)
                    {
                        double elapsedSeconds = (now - lastCpuCheckTime).TotalSeconds;
                        double cpuTimeSeconds = (totalTime - lastTotalProcessorTime).TotalSeconds;

                        if (elapsedSeconds > 0.1)
                        {
                            CpuPercent = Math.Clamp((cpuTimeSeconds / (elapsedSeconds * Environment.ProcessorCount)) * 100.0, 0.0, 100.0);
                        }
                    }

                    lastCpuCheckTime = now;
                    lastTotalProcessorTime = totalTime;
                }
                catch
                {
                    // Process may be exiting
                }
            }
        }

        public const string DefaultGameExePath = @"C:\games\RagnarokRebuild\RebuildClient.exe";
        private readonly ConcurrentDictionary<string, BotProcessState> runningBots = new(StringComparer.OrdinalIgnoreCase);
        private readonly WindowManager windowManager;

        public event Action<FleetLogEntry>? OnLog;

        public ProcessManager(WindowManager windowManager)
        {
            this.windowManager = windowManager;
        }

        private void EmitLog(FleetLogEntry entry)
        {
            OnLog?.Invoke(entry);
        }

        public IReadOnlyDictionary<string, BotProcessState> RunningBots => runningBots;

        public bool IsBotRunning(string profileName)
        {
            if (runningBots.TryGetValue(profileName, out var state))
            {
                return state.Process != null && !state.Process.HasExited;
            }
            return false;
        }

        public BotProcessState? GetState(string profileName)
        {
            runningBots.TryGetValue(profileName, out var state);
            return state;
        }

        public bool AdoptRunningProcess(string profileName, int pid)
        {
            if (string.IsNullOrWhiteSpace(profileName) || pid <= 0) return false;

            if (runningBots.TryGetValue(profileName, out var existingState))
            {
                if (existingState.Process != null && !existingState.Process.HasExited && existingState.ProcessId == pid)
                {
                    return true;
                }
            }

            try
            {
                var proc = Process.GetProcessById(pid);
                if (proc == null || proc.HasExited) return false;

                var state = new BotProcessState
                {
                    ProfileName = profileName,
                    Process = proc,
                    StartTime = proc.StartTime
                };

                runningBots[profileName] = state;

                try
                {
                    proc.EnableRaisingEvents = true;
                    proc.Exited += (s, e) =>
                    {
                        EmitLog(new FleetLogEntry
                        {
                            Profile = profileName,
                            Level = "Warning",
                            Message = $"Bot process for '{profileName}' (PID {pid}) exited."
                        });
                        runningBots.TryRemove(profileName, out _);
                    };
                }
                catch { }

                EmitLog(new FleetLogEntry
                {
                    Profile = profileName,
                    Level = "Info",
                    Message = $"Connected to running bot '{profileName}' (PID {pid})."
                });

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool StartBot(LaunchBotRequest request, out string errorMessage)
        {
            errorMessage = "";
            string profile = request.ProfileName.Trim();

            if (string.IsNullOrWhiteSpace(profile))
            {
                errorMessage = "Profile name cannot be empty.";
                return false;
            }

            if (IsBotRunning(profile))
            {
                errorMessage = $"Bot profile '{profile}' is already running.";
                return false;
            }

            if (!File.Exists(DefaultGameExePath))
            {
                errorMessage = $"Game executable not found at '{DefaultGameExePath}'.";
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = DefaultGameExePath,
                    WorkingDirectory = Path.GetDirectoryName(DefaultGameExePath),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                // Build CLI flags
                var args = new List<string> { $"-profile \"{profile}\"" };

                if (!string.IsNullOrWhiteSpace(request.AccountId))
                {
                    args.Add($"-account \"{request.AccountId.Trim()}\"");
                }

                if (request.LowSpec)
                {
                    args.Add("-lowspec");
                }

                if (request.Hidden)
                {
                    args.Add("-hidden");
                }

                if (request.TargetFps.HasValue && request.TargetFps.Value > 0)
                {
                    args.Add($"-fps {request.TargetFps.Value}");
                }

                psi.Arguments = string.Join(" ", args);

                var proc = Process.Start(psi);
                if (proc == null)
                {
                    errorMessage = "Failed to launch game process.";
                    return false;
                }

                var botState = new BotProcessState
                {
                    ProfileName = profile,
                    AccountId = request.AccountId ?? "",
                    Process = proc,
                    StartTime = DateTime.UtcNow
                };

                runningBots[profile] = botState;

                proc.EnableRaisingEvents = true;
                proc.Exited += (sender, e) =>
                {
                    EmitLog(new FleetLogEntry
                    {
                        Profile = profile,
                        Level = "Warning",
                        Message = $"Bot process for '{profile}' (PID {proc.Id}) exited."
                    });
                    runningBots.TryRemove(profile, out _);
                };

                EmitLog(new FleetLogEntry
                {
                    Profile = profile,
                    Level = "Success",
                    Message = $"Launched '{profile}' (PID: {proc.Id}) with args: {psi.Arguments}"
                });

                if (request.Hidden)
                {
                    _ = Task.Run(async () =>
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            await Task.Delay(500);
                            if (proc.HasExited) break;
                            if (windowManager.HideWindow(proc.Id))
                            {
                                break;
                            }
                        }
                    });
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                EmitLog(new FleetLogEntry
                {
                    Profile = profile,
                    Level = "Error",
                    Message = $"Failed to start '{profile}': {ex.Message}"
                });
                return false;
            }
        }

        public bool StopBot(string profileName)
        {
            if (runningBots.TryGetValue(profileName, out var state) && state.Process != null && !state.Process.HasExited)
            {
                try
                {
                    try
                    {
                        state.Process.Kill(entireProcessTree: true);
                    }
                    catch { }

                    runningBots.TryRemove(profileName, out _);

                    EmitLog(new FleetLogEntry
                    {
                        Profile = profileName,
                        Level = "Info",
                        Message = $"Stopped bot process for '{profileName}'."
                    });

                    return true;
                }
                catch (Exception ex)
                {
                    EmitLog(new FleetLogEntry
                    {
                        Profile = profileName,
                        Level = "Error",
                        Message = $"Error stopping '{profileName}': {ex.Message}"
                    });
                }
            }
            return false;
        }

        public void StopAll()
        {
            foreach (var profile in runningBots.Keys.ToList())
            {
                StopBot(profile);
            }
        }

        public void UpdateMetrics()
        {
            foreach (var state in runningBots.Values)
            {
                state.UpdatePerformanceMetrics();
            }
        }

        public List<int> GetRunningPids(List<string>? filterProfiles = null)
        {
            if (filterProfiles != null && filterProfiles.Count > 0)
            {
                var set = new HashSet<string>(filterProfiles, StringComparer.OrdinalIgnoreCase);
                return runningBots.Values
                    .Where(s => set.Contains(s.ProfileName) && s.ProcessId > 0)
                    .Select(s => s.ProcessId)
                    .ToList();
            }

            return runningBots.Values
                .Where(s => s.ProcessId > 0)
                .Select(s => s.ProcessId)
                .ToList();
        }
    }
}
