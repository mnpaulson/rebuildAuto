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
        public bool AutoEquipBestArrow { get; set; } = true;
        public int MinArrowCount { get; set; } = 30;
        public Dictionary<string, int> RestockTargets { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fly_Wing"] = 100,
            ["Butterfly_Wing"] = 5,
            ["Red_Potion"] = 50,
            ["Concentration_Potion"] = 3
        };
        public float SearchRadius { get; set; } = 18.0f;
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
        public bool AutoReconnect { get; set; } = true;
        public float AutoReconnectDelaySeconds { get; set; } = 4.0f;
        public int MaxReconnectAttempts { get; set; } = 10;
        public int PreferredCharacterSlot { get; set; } = -1;
        public bool LowSpecMode { get; set; } = false;
        public int TargetFrameRate { get; set; } = 10;
        public bool MuteAudioInLowSpec { get; set; } = true;
        public bool DisableRenderingInLowSpec { get; set; } = true;
        public bool AutoJobChange { get; set; } = true;
        public string TargetJob { get; set; } = "Swordman";
        public bool AutoClaimBardGifts { get; set; } = true;
        public bool AutoEquipEmptySlots { get; set; } = true;
    }

    public static class BotConfigManager
    {
        public static BotConfigData Current = new BotConfigData();
        public const string DevWorkspaceConfigPath = @"c:\dev\rebuildAuto\RebuildBotPlugin\bot_config.json";
        public static readonly string GameDirConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bot_config.json");

        public static string ConfigPath => Services.ProfileManager.GetConfigPath();

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static bool LoadConfig()
        {
            try
            {
                string targetPath = ConfigPath;

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

                        Debug.Log($"[RebuildBotPlugin] Config reloaded successfully from {targetPath} (Profile: '{(string.IsNullOrEmpty(Services.ProfileManager.ActiveProfileName) ? "Default" : Services.ProfileManager.ActiveProfileName)}')");
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
                string path = ConfigPath;
                string json = JsonSerializer.Serialize(Current, JsonOptions);

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, json);
                Debug.Log($"[RebuildBotPlugin] Config saved to {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RebuildBotPlugin] Failed to save config: {ex.Message}");
            }
        }
    }
}
