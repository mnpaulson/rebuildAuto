using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace RebuildBotPlugin
{
    [Serializable]
    public class BotConfigData
    {
        public bool Enabled { get; set; } = true;
        public bool AutoAttack { get; set; } = true;
        public bool AutoLoot { get; set; } = true;
        public bool AutoWander { get; set; } = true;
        public bool AutoPotion { get; set; } = true;
        public bool AutoRespawn { get; set; } = true;
        public bool AutoAvoidMonsters { get; set; } = true;
        public bool EmergencyFlyWingOnLowHp { get; set; } = true;
        public int EmergencyFlyWingHpPercent { get; set; } = 20;
        public bool AutoSitToRecover { get; set; } = true;
        public int SitHpPercent { get; set; } = 30;
        public int StandHpPercent { get; set; } = 90;
        public float FlyWingCooldownSeconds { get; set; } = 1.5f;
        public int FlyWingItemId { get; set; } = 601;
        public bool VerboseLogging { get; set; } = false;
        public int HpPotionPercent { get; set; } = 50;
        public int HpPotionItemId { get; set; } = 501;
        public List<int> HpPotionItemIds { get; set; } = new List<int> { 501, 502, 503, 507 };
        public bool AutoAspdPotion { get; set; } = true;
        public string AspdPotionPreference { get; set; } = "Auto";
        public Dictionary<string, string> ItemRules { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int ReturnToBaseWeightPercent { get; set; } = 90;
        public bool AutoReturnToBaseOnWeight { get; set; } = true;
        public bool AutoReturnOnOutOfHpItems { get; set; } = true;
        public bool AutoRestock { get; set; } = true;
        public bool AutoRestockOnLowSupplies { get; set; } = true;
        public Dictionary<string, int> RestockTargets { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fly_Wing"] = 100,
            ["Butterfly_Wing"] = 5,
            ["Red_Potion"] = 50,
            ["Concentration_Potion"] = 3
        };
        public float SearchRadius { get; set; } = 18.0f;
        public float AttackRange { get; set; } = 2.0f;
        public float AttackCooldownSeconds { get; set; } = 0.4f;
        public float LootCooldownSeconds { get; set; } = 0.3f;
        public float WanderCooldownSeconds { get; set; } = 4.0f;
        public int WanderRadius { get; set; } = 8;
        public string TargetMap { get; set; } = "prt_fild08";
        public bool AutoTravel { get; set; } = true;
        public bool AvoidPortalsWhileWandering { get; set; } = true;
        public float PortalSafetyRadius { get; set; } = 5.0f;
        public bool PrioritizeAggressiveMonsters { get; set; } = true;
        public List<string> PriorityMonsterList { get; set; } = new List<string>();
        public List<string> TargetMonsterWhitelist { get; set; } = new List<string>();
        public List<string> TargetMonsterBlacklist { get; set; } = new List<string>();
        public List<string> MonsterAvoidanceList { get; set; } = new List<string>();
        public List<string> LootItemWhitelist { get; set; } = new List<string>();
        public List<string> LootItemBlacklist { get; set; } = new List<string>();
        public List<RebuildBotPlugin.Models.SkillRule> SkillRules { get; set; } = new List<RebuildBotPlugin.Models.SkillRule>();
        public bool AutoStatAllocation { get; set; } = true;
        public List<RebuildBotPlugin.Models.StatBuildGoal> StatBuildPlan { get; set; } = new List<RebuildBotPlugin.Models.StatBuildGoal>();
        public bool AutoSkillAllocation { get; set; } = true;
        public List<RebuildBotPlugin.Models.SkillBuildGoal> SkillBuildPlan { get; set; } = new List<RebuildBotPlugin.Models.SkillBuildGoal>();
    }

    public static class BotConfigManager
    {
        public static BotConfigData Current = new BotConfigData();
        public const string DevWorkspaceConfigPath = @"c:\dev\rebuildAuto\RebuildBotPlugin\bot_config.json";
        public static readonly string GameDirConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bot_config.json");

        public static string ConfigPath
        {
            get
            {
                if (File.Exists(DevWorkspaceConfigPath))
                    return DevWorkspaceConfigPath;
                return GameDirConfigPath;
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static bool LoadConfig()
        {
            try
            {
                // If both exist, pick the file that was modified most recently
                string targetPath = ConfigPath;
                if (File.Exists(DevWorkspaceConfigPath) && File.Exists(GameDirConfigPath))
                {
                    DateTime devTime = File.GetLastWriteTimeUtc(DevWorkspaceConfigPath);
                    DateTime gameTime = File.GetLastWriteTimeUtc(GameDirConfigPath);
                    targetPath = (devTime >= gameTime) ? DevWorkspaceConfigPath : GameDirConfigPath;
                }

                if (File.Exists(targetPath))
                {
                    string json = File.ReadAllText(targetPath);
                    var data = JsonSerializer.Deserialize<BotConfigData>(json, JsonOptions);
                    if (data != null)
                    {
                        Current = data;
                        if (Current.HpPotionItemIds == null)
                            Current.HpPotionItemIds = new List<int>();
                        if (Current.HpPotionItemId > 0 && !Current.HpPotionItemIds.Contains(Current.HpPotionItemId))
                            Current.HpPotionItemIds.Add(Current.HpPotionItemId);
                        if (Current.ItemRules == null)
                            Current.ItemRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        Debug.Log($"[RebuildBotPlugin] Config reloaded successfully from {targetPath}");

                        // Bidirectional sync: keep both files identical
                        try
                        {
                            if (File.Exists(DevWorkspaceConfigPath) && File.Exists(GameDirConfigPath))
                            {
                                string otherPath = (targetPath == DevWorkspaceConfigPath) ? GameDirConfigPath : DevWorkspaceConfigPath;
                                File.WriteAllText(otherPath, json);
                            }
                        }
                        catch { }

                        return true;
                    }
                }
                else
                {
                    SaveConfig();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RebuildBotPlugin] Failed to load config: {ex.Message}");
            }
            return false;
        }

        public static void SaveConfig()
        {
            try
            {
                string json = JsonSerializer.Serialize(Current, JsonOptions);
                File.WriteAllText(ConfigPath, json);

                // Mirror save to game directory if saving to workspace
                if (ConfigPath != GameDirConfigPath && Directory.Exists(AppDomain.CurrentDomain.BaseDirectory))
                {
                    try { File.WriteAllText(GameDirConfigPath, json); } catch { }
                }

                Debug.Log($"[RebuildBotPlugin] Config saved to {ConfigPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RebuildBotPlugin] Failed to save config: {ex.Message}");
            }
        }
    }
}
