using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RebuildBotPlugin.Services;
using UnityEngine;

namespace RebuildBotPlugin.Models
{
    [Serializable]
    public class MasterItemRule
    {
        public string ItemName { get; set; } = "";
        public string Disposition { get; set; } = "Keep"; // Keep, Store, Sell
        public int? MaxCount { get; set; } = null; // null or <= 0 means unlimited
    }

    public static class MasterItemRulesManager
    {
        private static readonly Dictionary<string, MasterItemRule> rules = new Dictionary<string, MasterItemRule>(StringComparer.OrdinalIgnoreCase);
        private static DateTime lastReadTime = DateTime.MinValue;
        private static float lastCheckTime = 0f;
        private const float CheckIntervalSeconds = 2.0f;

        public static string ResolveMasterRulesPath()
        {
            string devPath = Path.Combine(ProfileManager.DevBaseDirectory, "profiles", "master_item_rules.json");
            string gamePath = Path.Combine(ProfileManager.GameBaseDirectory, "profiles", "master_item_rules.json");

            if (File.Exists(devPath)) return devPath;
            if (File.Exists(gamePath)) return gamePath;

            if (Directory.Exists(Path.Combine(ProfileManager.DevBaseDirectory, "profiles"))) return devPath;
            return gamePath;
        }

        public static void ReloadIfNeeded()
        {
            float now = Time.time;
            if (now - lastCheckTime < CheckIntervalSeconds) return;
            lastCheckTime = now;

            try
            {
                string path = ResolveMasterRulesPath();
                if (!File.Exists(path))
                {
                    rules.Clear();
                    return;
                }

                var writeTime = File.GetLastWriteTimeUtc(path);
                if (writeTime == lastReadTime) return;

                string json = File.ReadAllText(path);
                var list = JsonSerializer.Deserialize<List<MasterItemRule>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                rules.Clear();
                if (list != null)
                {
                    foreach (var r in list)
                    {
                        if (!string.IsNullOrWhiteSpace(r.ItemName))
                        {
                            rules[r.ItemName.Trim()] = r;
                        }
                    }
                }
                lastReadTime = writeTime;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MasterItemRules] Note loading master item rules: {ex.Message}");
            }
        }

        public static bool TryGetRule(string itemName, out MasterItemRule rule)
        {
            ReloadIfNeeded();
            rule = null;
            if (string.IsNullOrWhiteSpace(itemName)) return false;
            return rules.TryGetValue(itemName.Trim(), out rule);
        }

        public static Dictionary<string, MasterItemRule> GetAllRules()
        {
            ReloadIfNeeded();
            return new Dictionary<string, MasterItemRule>(rules, StringComparer.OrdinalIgnoreCase);
        }
    }
}
