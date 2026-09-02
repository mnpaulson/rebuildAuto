using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RebuildBotPlugin.Models;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    /// <summary>
    /// Manages the discrete Macro Action queue for the bot.
    /// Supports dynamic command execution from LLM Orchestrator, CLI, or bot_macro.json.
    /// </summary>
    public class MacroController
    {
        public Queue<MacroAction> ActionQueue { get; } = new();
        public MacroAction CurrentAction { get; private set; }
        public List<MacroAction> ExecutionHistory { get; } = new();

        public bool HasActiveMacro => CurrentAction != null || ActionQueue.Count > 0;

        private float lastFileCheckTime = 0f;
        private const float FileCheckInterval = 1.0f; // Check for bot_macro.json every 1s
        private bool isFirstCheck = true;
        
        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions { WriteIndented = true };

        public const string DevMacroPath = @"c:\dev\rebuildAuto\RebuildBotPlugin\bot_macro.json";
        public const string DevStatusPath = @"c:\dev\rebuildAuto\RebuildBotPlugin\macro_status.json";
        public static readonly string GameDirMacroPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bot_macro.json");
        public static readonly string GameDirStatusPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "macro_status.json");

        public void Enqueue(MacroAction action)
        {
            if (action == null) return;
            ActionQueue.Enqueue(action);
            BotEngine.Instance?.LogEvent($"[Macro] Enqueued action: '{action.Description}' (Queue size: {ActionQueue.Count}).");
            SaveStatus();
        }

        public void ClearQueue()
        {
            ActionQueue.Clear();
            if (CurrentAction != null)
            {
                CurrentAction.Cleanup(BotEngine.Instance);
                CurrentAction = null;
            }
            SaveStatus();
        }

        public bool ProcessMacro(BotEngine bot, float now)
        {
            if (isFirstCheck)
            {
                isFirstCheck = false;
                lastFileCheckTime = now;
                CheckMacroFile();
                SaveStatus();
            }
            else if (now - lastFileCheckTime >= FileCheckInterval)
            {
                lastFileCheckTime = now;
                CheckMacroFile();
            }

            // If no active action, dequeue next
            if (CurrentAction == null && ActionQueue.Count > 0)
            {
                CurrentAction = ActionQueue.Dequeue();
                CurrentAction.Begin(bot);
                SaveStatus();
            }

            // Process currently executing action
            if (CurrentAction != null)
            {
                bool running = CurrentAction.Process(bot, now);

                if (!running || CurrentAction.Status != MacroStatus.Running)
                {
                    if (CurrentAction.Status == MacroStatus.Running)
                    {
                        // Default to success if Process returned false without changing status
                        CurrentAction.Status = MacroStatus.Success;
                        CurrentAction.CompletedAt = DateTime.UtcNow;
                    }

                    ExecutionHistory.Add(CurrentAction);
                    SaveStatus();
                    CurrentAction = null;
                }

                return true; // Blocks routine bot logic while macro is active
            }

            return false;
        }

        private void CheckMacroFile()
        {
            try
            {
                string targetPath = Services.ProfileManager.GetMacroPath();
                if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath)) return;

                string json = File.ReadAllText(targetPath).Trim();
                if (string.IsNullOrWhiteSpace(json) || json == "{}" || json == "[]")
                {
                    return;
                }

                var batch = JsonSerializer.Deserialize<MacroCommandBatch>(json, ReadOptions);

                if (batch != null && batch.Commands != null && batch.Commands.Count > 0)
                {
                    BotEngine.Instance?.LogEvent($"[Macro] Received {batch.Commands.Count} command(s) from '{Path.GetFileName(targetPath)}' (Profile: '{(string.IsNullOrEmpty(Services.ProfileManager.ActiveProfileName) ? "Default" : Services.ProfileManager.ActiveProfileName)}')!");

                    foreach (var cmd in batch.Commands)
                    {
                        var action = ConvertCommandToMacroAction(cmd);
                        if (action != null)
                        {
                            Enqueue(action);
                        }
                    }

                    // Clear the file after reading to avoid executing repeatedly
                    File.WriteAllText(targetPath, "{\n  \"Commands\": []\n}");
                }
            }
            catch (Exception ex)
            {
                BotEngine.Instance?.LogEvent($"[Macro Error] Failed to read macro file: {ex.Message}");
            }
        }

        private MacroAction ConvertCommandToMacroAction(MacroCommandEntry cmd)
        {
            if (cmd == null || string.IsNullOrWhiteSpace(cmd.ActionType)) return null;

            string type = cmd.ActionType.Trim().ToLowerInvariant();

            switch (type)
            {
                case "equipitem":
                case "equip":
                    return new EquipItemMacroAction
                    {
                        ItemName = cmd.ItemName ?? cmd.TargetItemName ?? ""
                    };

                case "unequipitem":
                case "unequip":
                    return new UnequipItemMacroAction
                    {
                        SlotName = cmd.SlotName ?? ""
                    };

                case "slotcard":
                case "socket":
                case "card":
                    return new SlotCardMacroAction
                    {
                        TargetGearName = cmd.TargetItemName ?? cmd.ItemName ?? "",
                        CardName = cmd.CardName ?? ""
                    };

                case "useitem":
                case "use":
                case "consume":
                    return new UseItemMacroAction
                    {
                        ItemName = cmd.ItemName ?? "",
                        Quantity = cmd.Quantity > 0 ? cmd.Quantity : 1
                    };

                case "traveltomap":
                case "travel":
                case "map":
                    return new TravelToMapMacroAction
                    {
                        TargetMap = cmd.TargetMap ?? cmd.ItemName ?? ""
                    };

                case "buyitem":
                case "buy":
                    var buy = new BuyItemMacroAction
                    {
                        ItemName = cmd.ItemName ?? "",
                        Quantity = cmd.Quantity > 0 ? cmd.Quantity : 1
                    };
                    if (!string.IsNullOrWhiteSpace(cmd.VendorName)) buy.VendorName = cmd.VendorName;
                    if (cmd.VendorX > 0 && cmd.VendorY > 0) buy.VendorPosition = new Vector2Int(cmd.VendorX, cmd.VendorY);
                    return buy;

                case "upgradeitem":
                case "upgrade":
                case "refine":
                    return new UpgradeItemMacroAction
                    {
                        ItemName = cmd.ItemName ?? cmd.TargetItemName ?? "",
                        TargetRefineLevel = cmd.TargetRefineLevel > 0 ? cmd.TargetRefineLevel : 4,
                        StopAtSafeLimit = cmd.StopAtSafeLimit
                    };

                default:
                    BotEngine.Instance?.LogEvent($"[Macro Warning] Unknown ActionType '{cmd.ActionType}'.");
                    return null;
            }
        }

        private void SaveStatus()
        {
            try
            {
                var statusObj = new
                {
                    Profile = string.IsNullOrEmpty(Services.ProfileManager.ActiveProfileName) ? "Default" : Services.ProfileManager.ActiveProfileName,
                    HasActiveMacro = HasActiveMacro,
                    QueueCount = ActionQueue.Count,
                    CurrentAction = CurrentAction,
                    RecentHistory = ExecutionHistory.Count > 10
                        ? ExecutionHistory.GetRange(ExecutionHistory.Count - 10, 10)
                        : ExecutionHistory,
                    Timestamp = DateTime.UtcNow
                };

                string json = JsonSerializer.Serialize(statusObj, WriteOptions);

                string statusPath = Services.ProfileManager.GetStatusPath();
                string dir = Path.GetDirectoryName(statusPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(statusPath, json);
            }
            catch (Exception ex)
            {
                BotEngine.Instance?.LogDebug($"[Macro Status] Note writing status: {ex.Message}");
            }
        }
    }
}
