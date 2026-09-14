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

                bool partyEnabled = false;
                string partyName = "";
                bool isPartyLeader = false;
                bool isPartySupport = false;
                bool isPartyLooter = false;

                bool isDistributor = false;
                string distributorMap = "prt_fild08";
                int distributorX = 150;
                int distributorY = 360;
                string vendingShopTitle = "Fleet Depot";

                if (status != null && !string.IsNullOrEmpty(status.PartyName))
                {
                    partyEnabled = status.PartyEnabled;
                    partyName = status.PartyName;
                    isPartyLeader = status.IsPartyLeader;
                    isPartySupport = status.IsPartySupport;
                    isPartyLooter = status.IsPartyLooter;
                }
                else
                {
                    try
                    {
                        string rawConfig = GetProfileConfigRaw(name);
                        if (!string.IsNullOrWhiteSpace(rawConfig) && rawConfig != "{}")
                        {
                            using var doc = JsonDocument.Parse(rawConfig);
                            if (doc.RootElement.TryGetProperty("PartyEnabled", out var pe)) partyEnabled = pe.GetBoolean();
                            if (doc.RootElement.TryGetProperty("PartyName", out var pn)) partyName = pn.GetString() ?? "";
                            if (doc.RootElement.TryGetProperty("IsPartyLeader", out var pl)) isPartyLeader = pl.GetBoolean();
                            if (doc.RootElement.TryGetProperty("IsPartySupport", out var ps)) isPartySupport = ps.GetBoolean();
                            if (doc.RootElement.TryGetProperty("IsPartyLooter", out var plt)) isPartyLooter = plt.GetBoolean();
                            if (doc.RootElement.TryGetProperty("IsDistributor", out var idProp)) isDistributor = idProp.GetBoolean();
                            if (doc.RootElement.TryGetProperty("DistributorMap", out var dmProp)) distributorMap = dmProp.GetString() ?? "prt_fild08";
                            if (doc.RootElement.TryGetProperty("DistributorX", out var dxProp)) distributorX = dxProp.GetInt32();
                            if (doc.RootElement.TryGetProperty("DistributorY", out var dyProp)) distributorY = dyProp.GetInt32();
                            if (doc.RootElement.TryGetProperty("VendingShopTitle", out var vstProp)) vendingShopTitle = vstProp.GetString() ?? "Fleet Depot";
                        }
                    }
                    catch { }
                }

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
                    PartyEnabled = partyEnabled,
                    PartyName = partyName,
                    IsPartyLeader = isPartyLeader,
                    IsPartySupport = isPartySupport,
                    IsPartyLooter = isPartyLooter,
                    IsInGameParty = status?.IsInGameParty ?? false,
                    InGamePartyName = status?.InGamePartyName ?? "",
                    IsInGamePartyLeader = status?.IsInGamePartyLeader ?? false,
                    InGamePartyLeaderName = status?.InGamePartyLeaderName ?? "",
                    IsDistributor = status?.IsDistributor ?? isDistributor,
                    DistributorMap = !string.IsNullOrEmpty(status?.DistributorMap) ? status.DistributorMap : distributorMap,
                    DistributorX = status?.DistributorX != 0 ? (status?.DistributorX ?? distributorX) : distributorX,
                    DistributorY = status?.DistributorY != 0 ? (status?.DistributorY ?? distributorY) : distributorY,
                    IsVendingOpen = status?.IsVendingOpen ?? false,
                    IsReadyForDonations = status?.IsReadyForDonations ?? false,
                    VendingShopTitle = !string.IsNullOrEmpty(status?.VendingShopTitle) ? status.VendingShopTitle : vendingShopTitle,
                    Status = status,
                    MacroStatus = macro
                });
            }

            // Consensus resolution for In-Game Party Leader:
            // If multiple bots claim IsInGamePartyLeader = true within the same party (e.g., due to stale offline profiles or split party states),
            // identify the true leader by consensus so only ONE bot displays the '★ In-Game Leader' badge.
            var partyGroups = profileList
                .Where(p => p.PartyEnabled && !string.IsNullOrWhiteSpace(p.PartyName))
                .GroupBy(p => p.PartyName.Trim(), StringComparer.OrdinalIgnoreCase);

            foreach (var group in partyGroups)
            {
                var candidateLeaders = group.Where(p => p.IsInGamePartyLeader).ToList();
                if (candidateLeaders.Count > 1)
                {
                    // Tally votes based on InGamePartyLeaderName reported by members
                    var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    foreach (var member in group)
                    {
                        string named = (member.InGamePartyLeaderName ?? "").Trim();
                        if (!string.IsNullOrEmpty(named))
                        {
                            votes[named] = votes.GetValueOrDefault(named, 0) + (member.IsRunning ? 2 : 1);
                        }
                    }

                    // Score candidates:
                    // 1) Votes matching ProfileName or CharacterName
                    // 2) IsRunning status (running bot beats offline bot)
                    // 3) Orchestrator designated leader (IsPartyLeader) as tiebreak
                    BotProfileInfo? trueLeader = candidateLeaders
                        .OrderByDescending(p => votes.GetValueOrDefault(p.ProfileName, 0) + votes.GetValueOrDefault(p.Status?.CharacterName ?? "", 0))
                        .ThenByDescending(p => p.IsRunning)
                        .ThenByDescending(p => p.IsPartyLeader)
                        .FirstOrDefault();

                    if (trueLeader != null)
                    {
                        foreach (var cand in candidateLeaders)
                        {
                            if (cand != trueLeader)
                            {
                                cand.IsInGamePartyLeader = false;
                                if (cand.Status != null)
                                {
                                    cand.Status.IsInGamePartyLeader = false;
                                }
                            }
                        }
                    }
                }
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

        public bool UpdatePartySettings(PartyUpdateRequest req, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(req.ProfileName))
            {
                error = "Profile name is required.";
                return false;
            }

            try
            {
                string dir = Path.Combine(ProfilesDir, req.ProfileName);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string profileConfig = Path.Combine(dir, "bot_config.json");
                string raw = File.Exists(profileConfig) ? File.ReadAllText(profileConfig) : GetProfileConfigRaw(req.ProfileName);

                Dictionary<string, object?> dict;
                try
                {
                    dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }
                catch
                {
                    dict = new();
                }

                dict["PartyEnabled"] = req.PartyEnabled;
                dict["PartyName"] = req.PartyName ?? "";
                dict["IsPartyLeader"] = req.IsPartyLeader;
                dict["IsPartySupport"] = req.IsPartySupport;
                dict["IsPartyLooter"] = req.IsPartyLooter;

                if (processManager.IsBotRunning(req.ProfileName))
                {
                    dict["Enabled"] = true;
                }

                string updatedJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(profileConfig, updatedJson);

                // If this profile was designated as Leader, uncheck leader on any other profile in the same party
                if (req.PartyEnabled && req.IsPartyLeader && !string.IsNullOrWhiteSpace(req.PartyName))
                {
                    if (Directory.Exists(ProfilesDir))
                    {
                        foreach (var otherDir in Directory.GetDirectories(ProfilesDir))
                        {
                            string otherProfile = Path.GetFileName(otherDir);
                            if (string.Equals(otherProfile, req.ProfileName, StringComparison.OrdinalIgnoreCase)) continue;

                            string otherConfigPath = Path.Combine(otherDir, "bot_config.json");
                            if (File.Exists(otherConfigPath))
                            {
                                try
                                {
                                    string otherRaw = File.ReadAllText(otherConfigPath);
                                    var otherDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(otherRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                    if (otherDict != null &&
                                        otherDict.TryGetValue("PartyName", out var otherPNameObj) &&
                                        string.Equals(otherPNameObj?.ToString(), req.PartyName, StringComparison.OrdinalIgnoreCase) &&
                                        otherDict.TryGetValue("IsPartyLeader", out var otherLeaderObj) &&
                                        (otherLeaderObj is bool b && b || otherLeaderObj?.ToString() == "True"))
                                    {
                                        otherDict["IsPartyLeader"] = false;
                                        File.WriteAllText(otherConfigPath, JsonSerializer.Serialize(otherDict, new JsonSerializerOptions { WriteIndented = true }));
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }

                AddLog(new FleetLogEntry
                {
                    Profile = req.ProfileName,
                    Level = "Info",
                    Message = $"Updated party settings: Enabled={req.PartyEnabled}, Party='{req.PartyName}', Leader={req.IsPartyLeader}, Support={req.IsPartySupport}, Looter={req.IsPartyLooter}"
                });

                OnFleetUpdated?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public List<AccountSummaryItem> GetAccountsSummary()
        {
            var registry = LoadAccountsRegistry();
            var result = new List<AccountSummaryItem>();
            if (registry?.Accounts == null) return result;

            foreach (var acc in registry.Accounts)
            {
                var chars = new List<CharacterSummaryItem>();
                var usedSlots = new HashSet<int>();
                if (acc.Characters != null)
                {
                    foreach (var c in acc.Characters)
                    {
                        chars.Add(new CharacterSummaryItem
                        {
                            Name = c.Name,
                            Slot = c.Slot,
                            Gender = c.Gender ?? "Male"
                        });
                        usedSlots.Add(c.Slot);
                    }
                }

                int nextSlot = -1;
                for (int s = 0; s < 3; s++)
                {
                    if (!usedSlots.Contains(s))
                    {
                        nextSlot = s;
                        break;
                    }
                }

                result.Add(new AccountSummaryItem
                {
                    AccountId = acc.AccountId,
                    Username = string.IsNullOrWhiteSpace(acc.Username) ? acc.AccountId : acc.Username,
                    Characters = chars,
                    NextAvailableSlot = nextSlot
                });
            }

            return result;
        }

        public bool AddBot(AddBotRequest req, out string error)
        {
            error = "";
            if (req == null)
            {
                error = "Invalid request.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(req.AccountId))
            {
                error = "Account ID is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(req.CharacterName))
            {
                error = "Character name is required.";
                return false;
            }

            string charName = req.CharacterName.Trim();
            if (charName.Length < 2 || charName.Length > 30)
            {
                error = "Character name must be between 2 and 30 characters.";
                return false;
            }

            // Validate stats: 6 values, 1-9 each, total = 33
            if (req.StartingStats == null || req.StartingStats.Count != 6)
            {
                error = "Starting stats must contain exactly 6 attributes (STR, AGI, VIT, INT, DEX, LUK).";
                return false;
            }

            int sum = 0;
            foreach (var stat in req.StartingStats)
            {
                if (stat < 1 || stat > 9)
                {
                    error = "Each starting stat value must be between 1 and 9.";
                    return false;
                }
                sum += stat;
            }

            if (sum != 33)
            {
                error = $"Total starting stat points must equal 33 (current sum is {sum}).";
                return false;
            }

            try
            {
                // 1. Update or create Account in accounts.json
                var registry = LoadAccountsRegistry() ?? new AccountsRegistry();
                var account = registry.Accounts.FirstOrDefault(a => string.Equals(a.AccountId, req.AccountId.Trim(), StringComparison.OrdinalIgnoreCase));

                if (account == null)
                {
                    account = new AccountEntry
                    {
                        AccountId = req.AccountId.Trim(),
                        Username = req.AccountId.Trim(),
                        Password = req.Password ?? "",
                        IsNewAccount = req.IsNewAccount,
                        Characters = new List<CharacterEntry>()
                    };
                    registry.Accounts.Add(account);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(req.Password))
                    {
                        account.Password = req.Password;
                    }
                    if (req.IsNewAccount)
                    {
                        account.IsNewAccount = true;
                    }
                }

                // Check character slot assignment
                var existingChar = account.Characters.FirstOrDefault(c => string.Equals(c.Name, charName, StringComparison.OrdinalIgnoreCase));
                int slot = req.CharacterSlot;
                if (slot < 0 || slot > 2)
                {
                    var usedSlots = new HashSet<int>(account.Characters.Select(c => c.Slot));
                    slot = -1;
                    for (int s = 0; s < 3; s++)
                    {
                        if (!usedSlots.Contains(s))
                        {
                            slot = s;
                            break;
                        }
                    }
                    if (slot < 0)
                    {
                        error = $"Account '{account.AccountId}' already has 3 characters (max slots filled).";
                        return false;
                    }
                }

                if (existingChar != null)
                {
                    existingChar.Slot = slot;
                    existingChar.Gender = req.Gender;
                    existingChar.StartingStats = req.StartingStats;
                }
                else
                {
                    account.Characters.Add(new CharacterEntry
                    {
                        Name = charName,
                        Slot = slot,
                        Gender = req.Gender,
                        StartingStats = req.StartingStats
                    });
                }

                // Save accounts.json
                string updatedAccountsJson = JsonSerializer.Serialize(registry, new JsonSerializerOptions { WriteIndented = true });
                string primaryAccountsPath = ResolveAccountsFilePath();
                try
                {
                    string? dir = Path.GetDirectoryName(primaryAccountsPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(primaryAccountsPath, updatedAccountsJson);
                }
                catch { }

                // Mirror to game root if present
                string gameRootAccounts = @"C:\Games\RagnarokRebuild\accounts.json";
                try
                {
                    if (!string.Equals(primaryAccountsPath, gameRootAccounts, StringComparison.OrdinalIgnoreCase))
                    {
                        File.WriteAllText(gameRootAccounts, updatedAccountsJson);
                    }
                }
                catch { }

                // Mirror to DevPluginDir if present
                try
                {
                    if (!string.Equals(primaryAccountsPath, AccountsFilePath, StringComparison.OrdinalIgnoreCase) && Directory.Exists(DevPluginDir))
                    {
                        File.WriteAllText(AccountsFilePath, updatedAccountsJson);
                    }
                }
                catch { }

                // 2. Create bot profile directory & bot_config.json
                string profileDir = Path.Combine(ProfilesDir, charName);
                if (!Directory.Exists(profileDir)) Directory.CreateDirectory(profileDir);

                string profileConfigPath = Path.Combine(profileDir, "bot_config.json");
                Dictionary<string, object?> configDict = new(StringComparer.OrdinalIgnoreCase);

                // Start from root default template if available
                string rootConfig = Path.Combine(DevPluginDir, "bot_config.json");
                if (File.Exists(rootConfig))
                {
                    try
                    {
                        var rootParsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(rootConfig), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (rootParsed != null)
                        {
                            foreach (var kvp in rootParsed) configDict[kvp.Key] = kvp.Value;
                        }
                    }
                    catch { }
                }

                // If profile config already exists, keep existing non-overridden properties
                if (File.Exists(profileConfigPath))
                {
                    try
                    {
                        var profParsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(File.ReadAllText(profileConfigPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        if (profParsed != null)
                        {
                            foreach (var kvp in profParsed) configDict[kvp.Key] = kvp.Value;
                        }
                    }
                    catch { }
                }

                // Apply new bot settings
                configDict["TargetJob"] = req.TargetJob ?? "Novice";
                configDict["AutoJobChange"] = true;
                configDict["AutoCreateCharacter"] = true;
                configDict["AutoCreateAccount"] = req.IsNewAccount;
                configDict["CharacterGender"] = req.Gender ?? "Male";
                configDict["StartingStats"] = req.StartingStats;
                configDict["PreferredCharacterSlot"] = slot;
                configDict["AutoStatAllocation"] = true;
                configDict["StatBuildPlan"] = req.StatBuildPlan;
                configDict["AutoSkillAllocation"] = true;
                configDict["SkillBuildPlan"] = req.SkillBuildPlan;

                string configJson = JsonSerializer.Serialize(configDict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(profileConfigPath, configJson);

                // 3. Scan profiles & notify UI
                ScanExistingProfiles();
                OnFleetUpdated?.Invoke();

                AddLog(new FleetLogEntry
                {
                    Profile = charName,
                    Level = "Success",
                    Message = $"Added bot profile '{charName}' (Account: '{account.AccountId}', Slot {slot}, Job: '{req.TargetJob}')."
                });

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static string ResolveAccountsFilePath()
        {
            if (File.Exists(AccountsFilePath)) return AccountsFilePath;
            string gameRootAccounts = @"C:\Games\RagnarokRebuild\accounts.json";
            if (File.Exists(gameRootAccounts)) return gameRootAccounts;
            string pluginRootAccounts = @"C:\Games\RagnarokRebuild\rebuildAuto\RebuildBotPlugin\accounts.json";
            if (File.Exists(pluginRootAccounts)) return pluginRootAccounts;
            string appBaseAccounts = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.json");
            if (File.Exists(appBaseAccounts)) return appBaseAccounts;
            return AccountsFilePath;
        }

        private AccountsRegistry? LoadAccountsRegistry()
        {
            try
            {
                string path = ResolveAccountsFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
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
            public bool IsNewAccount { get; set; } = false;
            public List<CharacterEntry> Characters { get; set; } = new();
        }

        private class CharacterEntry
        {
            public string Name { get; set; } = "";
            public int Slot { get; set; } = 0;
            public string Gender { get; set; } = "Male";
            public List<int>? StartingStats { get; set; }
        }

        public static string ResolveMasterItemRulesFilePath()
        {
            if (Directory.Exists(ProfilesDir))
                return Path.Combine(ProfilesDir, "master_item_rules.json");

            string altDir = @"C:\Games\RagnarokRebuild\rebuildAuto\RebuildBotPlugin\profiles";
            if (Directory.Exists(altDir))
                return Path.Combine(altDir, "master_item_rules.json");

            return Path.Combine(ProfilesDir, "master_item_rules.json");
        }

        public List<MasterItemRule> GetMasterItemRules()
        {
            try
            {
                string path = ResolveMasterItemRulesFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<List<MasterItemRule>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<MasterItemRule>();
                }
            }
            catch { }
            return new List<MasterItemRule>();
        }

        public bool SaveMasterItemRules(List<MasterItemRule> rules, out string error)
        {
            error = "";
            try
            {
                string path = ResolveMasterItemRulesFilePath();
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
