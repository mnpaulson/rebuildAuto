using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RebuildOrchestrator.Models;

namespace RebuildOrchestrator.Services
{
    public class FleetManager
    {
        public const string DevPluginDir = @"c:\dev\rebuildAuto\RebuildBotPlugin";
        public static readonly string ProfilesDir = Path.Combine(DevPluginDir, "profiles");
        public static readonly string AccountsFilePath = Path.Combine(DevPluginDir, "accounts.json");

        private readonly ProcessManager processManager;
        private readonly WindowManager windowManager;
        private readonly ConcurrentDictionary<string, BotStatusData> cachedStatuses = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, MacroStatusData> cachedMacros = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<FleetLogEntry> eventLogs = new();
        private readonly object eventLock = new();
        private FileSystemWatcher? fileWatcher;

        public event Action? OnFleetUpdated;

        public FleetManager(ProcessManager procMgr, WindowManager winMgr)
        {
            processManager = procMgr;
            windowManager = winMgr;
            processManager.OnLog += AddLog;
            InitializeFileWatcher();
            ScanExistingProfiles();
        }

        public void AddLog(FleetLogEntry entry)
        {
            lock (eventLock)
            {
                eventLogs.Add(entry);
                if (eventLogs.Count > 200)
                {
                    eventLogs.RemoveAt(0);
                }
            }
            OnFleetUpdated?.Invoke();
        }

        public List<FleetLogEntry> GetRecentLogs(int count = 50)
        {
            lock (eventLock)
            {
                return eventLogs.TakeLast(count).Reverse().ToList();
            }
        }

        public event Action<string, string>? OnBotLogLine;
        private readonly ConcurrentDictionary<string, long> logFilePositions = new(StringComparer.OrdinalIgnoreCase);

        private void InitializeFileWatcher()
        {
            try
            {
                if (!Directory.Exists(ProfilesDir))
                {
                    Directory.CreateDirectory(ProfilesDir);
                }

                fileWatcher = new FileSystemWatcher(ProfilesDir)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                    Filter = "*.*",
                    EnableRaisingEvents = true
                };

                fileWatcher.Changed += (s, e) => HandleFileChanged(e.FullPath);
                fileWatcher.Created += (s, e) => HandleFileChanged(e.FullPath);
            }
            catch (Exception ex)
            {
                AddLog(new FleetLogEntry
                {
                    Level = "Warning",
                    Message = $"FileWatcher error: {ex.Message}"
                });
            }
        }

        private void HandleFileChanged(string fullPath)
        {
            string fileName = Path.GetFileName(fullPath).ToLowerInvariant();
            string profileName = Path.GetFileName(Path.GetDirectoryName(fullPath) ?? "");

            if (string.IsNullOrWhiteSpace(profileName) || profileName.Equals("profiles", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                if (fileName == "bot_status.json")
                {
                    string json = ReadFileSafe(fullPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var status = JsonSerializer.Deserialize<BotStatusData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (status != null)
                        {
                            status.Profile = profileName;
                            cachedStatuses[profileName] = status;
                            OnFleetUpdated?.Invoke();
                        }
                    }
                }
                else if (fileName == "macro_status.json")
                {
                    string json = ReadFileSafe(fullPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var macro = JsonSerializer.Deserialize<MacroStatusData>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (macro != null)
                        {
                            macro.Profile = profileName;
                            cachedMacros[profileName] = macro;
                            OnFleetUpdated?.Invoke();
                        }
                    }
                }
                else if (fileName == "bot.log")
                {
                    TailLogFile(profileName, fullPath);
                }
            }
            catch { }
        }

        private readonly object tailLock = new();

        private void TailLogFile(string profileName, string fullPath)
        {
            lock (tailLock)
            {
                try
                {
                    var fi = new FileInfo(fullPath);
                    if (!fi.Exists) return;

                    long lastPos = logFilePositions.GetOrAdd(profileName, 0);

                    // If file was truncated or recreated, reset
                    if (fi.Length < lastPos)
                    {
                        lastPos = 0;
                    }

                    // If no new content was added, skip
                    if (fi.Length == lastPos)
                    {
                        return;
                    }

                    using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (lastPos > 0 && lastPos < stream.Length)
                    {
                        stream.Seek(lastPos, SeekOrigin.Begin);
                    }

                    using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            OnBotLogLine?.Invoke(profileName, line.Trim());
                        }
                    }

                    // Record the exact physical file length on disk
                    logFilePositions[profileName] = stream.Length;
                }
                catch { }
            }
        }

        public List<string> GetRecentBotLogs(string profileName, int count = 200)
        {
            var lines = new List<string>();
            try
            {
                if (string.Equals(profileName, "all", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(profileName))
                {
                    if (Directory.Exists(ProfilesDir))
                    {
                        foreach (var dir in Directory.GetDirectories(ProfilesDir))
                        {
                            string p = Path.GetFileName(dir);
                            string logPath = Path.Combine(dir, "bot.log");
                            if (File.Exists(logPath))
                            {
                                lines.AddRange(GetLastLinesOfFile(logPath, 50).Select(l => $"[{p}] {l}"));
                            }
                        }
                    }
                }
                else
                {
                    string logPath = Path.Combine(ProfilesDir, profileName, "bot.log");
                    if (File.Exists(logPath))
                    {
                        lines = GetLastLinesOfFile(logPath, count);
                    }
                }
            }
            catch { }
            return lines;
        }

        private static List<string> GetLastLinesOfFile(string path, int count)
        {
            var list = new List<string>();
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        list.Add(line.Trim());
                    }
                }
            }
            catch { }
            return list.TakeLast(count).ToList();
        }

        private static string ReadFileSafe(string path)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    return reader.ReadToEnd();
                }
                catch
                {
                    System.Threading.Thread.Sleep(30);
                }
            }
            return "";
        }

        private void ScanExistingProfiles()
        {
            if (Directory.Exists(ProfilesDir))
            {
                foreach (var dir in Directory.GetDirectories(ProfilesDir))
                {
                    string profileName = Path.GetFileName(dir);
                    string statusFile = Path.Combine(dir, "bot_status.json");
                    string macroFile = Path.Combine(dir, "macro_status.json");
                    string logFile = Path.Combine(dir, "bot.log");

                    if (File.Exists(statusFile)) HandleFileChanged(statusFile);
                    if (File.Exists(macroFile)) HandleFileChanged(macroFile);
                    if (File.Exists(logFile))
                    {
                        var fi = new FileInfo(logFile);
                        logFilePositions[profileName] = fi.Length;
                    }
                }
            }
        }

        public FleetOverviewResponse GetFleetOverview()
        {
            processManager.UpdateMetrics();

            var accountsRegistry = LoadAccountsRegistry();
            var discoveredProfiles = new Dictionary<string, (string accountId, string username, int slot)>(StringComparer.OrdinalIgnoreCase);

            // 1. Ingest Accounts Registry
            if (accountsRegistry != null && accountsRegistry.Accounts != null)
            {
                foreach (var acc in accountsRegistry.Accounts)
                {
                    if (acc.Characters != null)
                    {
                        foreach (var c in acc.Characters)
                        {
                            if (!string.IsNullOrWhiteSpace(c.Name))
                            {
                                discoveredProfiles[c.Name] = (acc.AccountId, acc.Username, c.Slot);
                            }
                        }
                    }
                }
            }

            // 2. Ingest Directory Profiles
            if (Directory.Exists(ProfilesDir))
            {
                foreach (var dir in Directory.GetDirectories(ProfilesDir))
                {
                    string p = Path.GetFileName(dir);
                    if (!discoveredProfiles.ContainsKey(p))
                    {
                        discoveredProfiles[p] = ("", "", 0);
                    }
                }
            }

            var profileList = new List<BotProfileInfo>();
            long totalZeny = 0;
            double totalExp = 0.0;
            int totalKills = 0;

            foreach (var kvp in discoveredProfiles)
            {
                string name = kvp.Key;
                var (accId, username, slot) = kvp.Value;

                cachedStatuses.TryGetValue(name, out var status);
                cachedMacros.TryGetValue(name, out var macro);

                if (status != null && status.ProcessId.HasValue && status.ProcessId.Value > 0)
                {
                    if (!processManager.IsBotRunning(name))
                    {
                        processManager.AdoptRunningProcess(name, status.ProcessId.Value);
                    }
                }

                bool isRunning = processManager.IsBotRunning(name);
                var procState = processManager.GetState(name);

                if (isRunning && (status == null || (procState != null && status.Timestamp < procState.StartTime.AddSeconds(-10))))
                {
                    status = new BotStatusData
                    {
                        Profile = name,
                        CharacterName = name,
                        JobName = "Starting Up...",
                        BotState = "Launching",
                        Hp = 1,
                        MaxHp = 1,
                        Sp = 1,
                        MaxSp = 1,
                        ProcessId = procState?.ProcessId,
                        Timestamp = DateTime.UtcNow
                    };
                }

                if (status != null)
                {
                    totalZeny += status.Zeny;
                    totalExp += status.BaseExpPerHour;
                    totalKills += status.MonstersKilled;
                }

                bool isVisible = isRunning && procState != null && procState.ProcessId > 0 && windowManager.IsWindowVisibleForPid(procState.ProcessId);

                profileList.Add(new BotProfileInfo
                {
                    ProfileName = name,
                    AccountId = accId,
                    Username = username,
                    CharacterSlot = slot,
                    IsRunning = isRunning,
                    IsWindowVisible = isVisible,
                    ProcessId = procState?.ProcessId,
                    CpuPercent = procState?.CpuPercent ?? 0.0,
                    RamMegabytes = procState?.RamMb ?? 0.0,
                    ProcessStartTime = procState?.StartTime,
                    Status = status,
                    MacroStatus = macro
                });
            }

            return new FleetOverviewResponse
            {
                TotalBots = profileList.Count,
                RunningBots = profileList.Count(p => p.IsRunning),
                TotalZeny = totalZeny,
                TotalBaseExpPerHour = totalExp,
                TotalKills = totalKills,
                Profiles = profileList.OrderByDescending(p => p.IsRunning).ThenBy(p => p.ProfileName).ToList(),
                Monitors = windowManager.GetMonitors(),
                Timestamp = DateTime.UtcNow
            };
        }

        public bool EnqueueMacro(string profileName, MacroEnqueueRequest request, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                string dir = Path.Combine(ProfilesDir, profileName);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string macroPath = Path.Combine(dir, "bot_macro.json");

                var entry = new Dictionary<string, object?>
                {
                    ["ActionType"] = request.ActionType,
                    ["ItemName"] = request.ItemName,
                    ["TargetItemName"] = request.TargetItemName,
                    ["CardName"] = request.CardName,
                    ["Quantity"] = request.Quantity,
                    ["TargetRefineLevel"] = request.TargetRefineLevel,
                    ["StopAtSafeLimit"] = request.StopAtSafeLimit,
                    ["SlotName"] = request.SlotName,
                    ["TargetMap"] = request.TargetMap,
                    ["VendorName"] = request.VendorName,
                    ["VendorX"] = request.VendorX,
                    ["VendorY"] = request.VendorY
                };

                var batch = new { Commands = new List<object> { entry } };
                string json = JsonSerializer.Serialize(batch, new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(macroPath, json);

                AddLog(new FleetLogEntry
                {
                    Profile = profileName,
                    Level = "Info",
                    Message = $"Dispatched Macro: {request.ActionType} (Item: {request.ItemName ?? request.TargetMap ?? "N/A"})"
                });

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        public string GetProfileConfigRaw(string profileName)
        {
            string profileConfig = Path.Combine(ProfilesDir, profileName, "bot_config.json");
            string rootConfig = Path.Combine(DevPluginDir, "bot_config.json");

            if (File.Exists(profileConfig)) return File.ReadAllText(profileConfig);
            if (File.Exists(rootConfig)) return File.ReadAllText(rootConfig);
            return "{}";
        }

        public bool SaveProfileConfigRaw(string profileName, string jsonContent, out string error)
        {
            error = "";
            try
            {
                // Validate JSON syntax
                using var doc = JsonDocument.Parse(jsonContent);

                string dir = Path.Combine(ProfilesDir, profileName);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string profileConfig = Path.Combine(dir, "bot_config.json");
                File.WriteAllText(profileConfig, jsonContent);

                AddLog(new FleetLogEntry
                {
                    Profile = profileName,
                    Level = "Success",
                    Message = $"Saved configuration for profile '{profileName}'."
                });

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private AccountsRegistry? LoadAccountsRegistry()
        {
            try
            {
                if (File.Exists(AccountsFilePath))
                {
                    string json = File.ReadAllText(AccountsFilePath);
                    return JsonSerializer.Deserialize<AccountsRegistry>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch { }
            return null;
        }

        private class AccountsRegistry
        {
            public List<AccountEntry> Accounts { get; set; } = new();
        }

        private class AccountEntry
        {
            public string AccountId { get; set; } = "";
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public List<CharacterEntry> Characters { get; set; } = new();
        }

        private class CharacterEntry
        {
            public string Name { get; set; } = "";
            public int Slot { get; set; } = 0;
        }
    }
}
