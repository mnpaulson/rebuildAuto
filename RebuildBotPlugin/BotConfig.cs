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
        public Dictionary<string, int> ItemRuleLimits { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Distributor Configuration (Merchant/Blacksmith only)
        public bool IsDistributor { get; set; } = false;
        public string DistributorMap { get; set; } = "prt_fild08";
        public int DistributorX { get; set; } = 150;
        public int DistributorY { get; set; } = 360;
        public bool DistributorOverrideNpcEnabled { get; set; } = false;
        public string DistributorOverrideNpcMap { get; set; } = "";
        public int DistributorOverrideNpcX { get; set; } = 0;
        public int DistributorOverrideNpcY { get; set; } = 0;
        public string VendingShopTitle { get; set; } = "Fleet Depot";
        public List<string> VendConsumables { get; set; } = new List<string>
        {
            "Red_Potion",
            "Concentration_Potion",
            "Awakening_Potion",
            "Butterfly_Wing",
            "Fly_Wing"
        };
        public Dictionary<string, int> VendConsumableTargets { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Red_Potion"] = 100,
            ["Concentration_Potion"] = 20,
            ["Awakening_Potion"] = 20,
            ["Butterfly_Wing"] = 100,
            ["Fly_Wing"] = 500
        };
        public Dictionary<string, int> VendConsumableMinStock { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Red_Potion"] = 10,
            ["Concentration_Potion"] = 5,
            ["Awakening_Potion"] = 5,
            ["Butterfly_Wing"] = 10,
            ["Fly_Wing"] = 50,
            ["Silver_Arrow"] = 2000
        };
        public int VendingTargetCartStock { get; set; } = 100;
        public int VendingRestockThreshold { get; set; } = 10;

        public int GetTargetVendStock(string consumableName)
        {
            if (string.IsNullOrWhiteSpace(consumableName)) return VendingTargetCartStock > 0 ? VendingTargetCartStock : 100;
            string norm = consumableName.Replace(' ', '_');
            if (VendConsumableTargets != null)
            {
                if (VendConsumableTargets.TryGetValue(consumableName, out int count) && count > 0) return count;
                if (VendConsumableTargets.TryGetValue(norm, out int normCount) && normCount > 0) return normCount;
            }
            return VendingTargetCartStock > 0 ? VendingTargetCartStock : 100;
        }

        public int GetMinVendStock(string consumableName)
        {
            if (string.IsNullOrWhiteSpace(consumableName)) return VendingRestockThreshold > 0 ? VendingRestockThreshold : 10;
            string norm = consumableName.Replace(' ', '_');
            if (VendConsumableMinStock != null)
            {
                if (VendConsumableMinStock.TryGetValue(consumableName, out int min) && min > 0) return min;
                if (VendConsumableMinStock.TryGetValue(norm, out int normMin) && normMin > 0) return normMin;
            }
            if (string.Equals(norm, "Silver_Arrow", StringComparison.OrdinalIgnoreCase))
                return 2000;
            int target = GetTargetVendStock(consumableName);
            int defThreshold = VendingRestockThreshold > 0 ? VendingRestockThreshold : 10;
            return Mathf.Min(defThreshold, target);
        }
        public int ReturnToBaseWeightPercent { get; set; } = 90;
        public bool AutoReturnToBaseOnWeight { get; set; } = true;
        public bool AutoReturnOnOutOfHpItems { get; set; } = false; // Deprecated: Replaced by per-item EssentialSupplies
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
        public List<string> EssentialSupplies { get; set; } = new List<string> { "Fly_Wing", "Butterfly_Wing" };

        public bool IsSupplyEssential(string itemName)
        {
            if (EssentialSupplies == null || string.IsNullOrWhiteSpace(itemName)) return false;
            string norm = itemName.Trim().Replace(' ', '_');
            return EssentialSupplies.Exists(s => string.Equals(s.Trim().Replace(' ', '_'), norm, StringComparison.OrdinalIgnoreCase));
        }
        public float SearchRadius { get; set; } = 18.0f;
        public float AttackCooldownSeconds { get; set; } = 0.4f;
        public float LootCooldownSeconds { get; set; } = 0.3f;
        public float WanderCooldownSeconds { get; set; } = 4.0f;
        public int WanderRadius { get; set; } = 8;
        public string TargetMap { get; set; } = "prt_fild08";
        public bool AutoTravel { get; set; } = true;
        public bool AvoidPortalsWhileWandering { get; set; } = true;
        public float PortalSafetyRadius { get; set; } = 5.0f;
        public bool AvoidTrackedBosses { get; set; } = true;
        public float BossAvoidanceRadius { get; set; } = 25.0f;
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
        public List<RebuildBotPlugin.Models.EquipmentTarget> EquipmentTargets { get; set; } = new List<RebuildBotPlugin.Models.EquipmentTarget>();

        // Character Creation & Identity
        public bool AutoCreateAccount { get; set; } = false;
        public bool AutoCreateCharacter { get; set; } = true;
        public string CharacterGender { get; set; } = "Male";
        public List<int> StartingStats { get; set; } = new List<int> { 5, 5, 5, 5, 5, 8 };
        public int CharacterHairStyle { get; set; } = 0;
        public int CharacterHairColor { get; set; } = 0;

        // Partying & Fleet Coordination
        public bool SuppressFleeWhilePartied { get; set; } = true;
        public bool PartyEnabled { get; set; } = false;
        public string PartyName { get; set; } = "";
        public bool IsPartyLeader { get; set; } = false;
        public bool IsPartySupport { get; set; } = false;
        public bool IsPartyLooter { get; set; } = false;
        public float PartyFollowDistance { get; set; } = 3.0f;
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
                        if (Current.ItemRuleLimits == null)
                            Current.ItemRuleLimits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        if (Current.VendConsumables == null)
                            Current.VendConsumables = new List<string> { "Red_Potion", "Concentration_Potion", "Awakening_Potion", "Butterfly_Wing", "Fly_Wing" };

                        if (Current.VendConsumableTargets == null || Current.VendConsumableTargets.Count == 0)
                        {
                            Current.VendConsumableTargets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                            foreach (var item in Current.VendConsumables)
                            {
                                Current.VendConsumableTargets[item] = Current.VendingTargetCartStock > 0 ? Current.VendingTargetCartStock : 100;
                            }
                        }
                        else
                        {
                            var allKeys = new HashSet<string>(Current.VendConsumables, StringComparer.OrdinalIgnoreCase);
                            foreach (var k in Current.VendConsumableTargets.Keys)
                            {
                                if (allKeys.Add(k))
                                    Current.VendConsumables.Add(k);
                            }
                        }

                        if (Current.VendConsumableMinStock == null)
                        {
                            Current.VendConsumableMinStock = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        }
                        foreach (var item in Current.VendConsumables)
                        {
                            if (!Current.VendConsumableMinStock.ContainsKey(item))
                            {
                                Current.VendConsumableMinStock[item] = Current.GetMinVendStock(item);
                            }
                        }

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
                if (Current != null && Current.VendConsumableTargets != null && Current.VendConsumableTargets.Count > 0)
                {
                    Current.VendConsumables = new List<string>(Current.VendConsumableTargets.Keys);
                }

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
