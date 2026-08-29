using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RebuildBotPlugin
{
    [Serializable]
    public class BotConfigData
    {
        public bool Enabled = true;
        public bool AutoAttack = true;
        public bool AutoLoot = true;
        public bool AutoWander = true;
        public bool AutoPotion = true;
        public int HpPotionPercent = 50;
        public int SpPotionPercent = 30;
        public int HpPotionItemId = 501;
        public int SpPotionItemId = 506;
        public float SearchRadius = 18.0f;
        public float AttackRange = 2.0f;
        public float AttackCooldownSeconds = 0.4f;
        public float LootCooldownSeconds = 0.3f;
        public float WanderCooldownSeconds = 4.0f;
        public int WanderRadius = 8;
        public string TargetMap = "prt_fild08";
        public bool AutoTravel = true;
        public bool AvoidPortalsWhileWandering = true;
        public float PortalSafetyRadius = 5.0f;
        public List<string> TargetMonsterWhitelist = new List<string>();
        public List<string> TargetMonsterBlacklist = new List<string>();
        public List<string> LootItemWhitelist = new List<string>();
        public List<string> LootItemBlacklist = new List<string>();
    }

    public static class BotConfigManager
    {
        public static BotConfigData Current = new BotConfigData();
        public static string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bot_config.json");

        public static bool LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var data = JsonUtility.FromJson<BotConfigData>(json);
                    if (data != null)
                    {
                        Current = data;
                        Debug.Log($"[RebuildBotPlugin] Config reloaded successfully from {ConfigPath}");
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
                string json = JsonUtility.ToJson(Current, true);
                File.WriteAllText(ConfigPath, json);
                Debug.Log($"[RebuildBotPlugin] Config saved to {ConfigPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RebuildBotPlugin] Failed to save config: {ex.Message}");
            }
        }
    }
}
