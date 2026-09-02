using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace RebuildBotPlugin.Services
{
    public class MonsterDatabase
    {
        private static MonsterDatabase instance;
        public static MonsterDatabase Instance => instance ??= new MonsterDatabase();

        private static readonly HashSet<string> knownAggressiveMonsters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Zombie", "Familiar", "Soldier Skeleton", "Archer Skeleton",
            "Orc Zombie", "Orc Skeleton", "Munak", "Bon Gun", "Gargoyle", "Mummy",
            "Drainliar", "Hydra", "Thief Bug Male", "Thief Bug Female", "Ghouls", "Ghoul",
            "Khalitzburg", "Raydric", "Abysmal Knight", "Wanderer", "Evil Druid", "Wraith",
            "Mimic", "Hunter Fly", "Bathory", "Joker", "Clock", "High Orc", "Alarm",
            "Anubis", "Pasana", "Minorous", "Sohee", "Whisper", "Marionette"
        };

        private readonly HashSet<string> parsedAggressiveMonsters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool monsterDbLoaded = false;

        public void EnsureLoaded()
        {
            if (monsterDbLoaded) return;
            monsterDbLoaded = true;

            try
            {
                string filePath = Path.Combine(Application.streamingAssetsPath, "ClientConfigGenerated", "monsterdatabase.json");
                if (!File.Exists(filePath))
                {
                    filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RebuildClient_Data", "StreamingAssets", "ClientConfigGenerated", "monsterdatabase.json");
                }

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Items", out var items))
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.TryGetProperty("Name", out var nameProp) && item.TryGetProperty("Ai", out var aiProp))
                            {
                                string name = nameProp.GetString();
                                string ai = aiProp.GetString();
                                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(ai))
                                {
                                    if (ai.IndexOf("Aggressive", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        ai.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        ai.IndexOf("Angry", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        parsedAggressiveMonsters.Add(name);
                                    }
                                }
                            }
                        }
                    }
                    BotLog.Info($"[MonsterDatabase] Loaded monster database: identified {parsedAggressiveMonsters.Count} aggressive species.");
                }
            }
            catch (Exception ex)
            {
                BotLog.Warn($"[MonsterDatabase] Monster database notice: {ex.Message}");
            }
        }

        public bool IsAggressive(string monsterName)
        {
            if (string.IsNullOrEmpty(monsterName)) return false;
            EnsureLoaded();

            if (BotConfigManager.Current.PriorityMonsterList != null &&
                BotConfigManager.Current.PriorityMonsterList.Contains(monsterName))
                return true;

            if (parsedAggressiveMonsters.Contains(monsterName))
                return true;

            return knownAggressiveMonsters.Contains(monsterName);
        }
    }
}
