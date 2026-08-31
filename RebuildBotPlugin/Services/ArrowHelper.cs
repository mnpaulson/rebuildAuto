using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.Sprites;
using RebuildSharedData.ClientTypes;
using RebuildSharedData.Enum;
using RebuildSharedData.Enum.EntityStats;
using UnityEngine;

namespace RebuildBotPlugin.Services
{
    public class ArrowInfo
    {
        public int ItemId { get; set; }
        public string Name { get; set; }
        public AttackElement Element { get; set; }
        public int BaseAtk { get; set; }
        public int CorrespondingQuiverId { get; set; }
    }

    public static class ArrowHelper
    {
        private static bool isInitialized = false;
        private static readonly Dictionary<int, string> MonsterElementsById = new();
        private static readonly Dictionary<string, string> MonsterElementsByName = new(StringComparer.OrdinalIgnoreCase);

        // Registry of known Arrows in Ragnarok Rebuild
        public static readonly Dictionary<int, ArrowInfo> KnownArrows = new()
        {
            [1750] = new ArrowInfo { ItemId = 1750, Name = "Arrow", Element = AttackElement.Neutral, BaseAtk = 25, CorrespondingQuiverId = 0 },
            [1751] = new ArrowInfo { ItemId = 1751, Name = "Silver Arrow", Element = AttackElement.Holy, BaseAtk = 30, CorrespondingQuiverId = 12009 },
            [1752] = new ArrowInfo { ItemId = 1752, Name = "Fire Arrow", Element = AttackElement.Fire, BaseAtk = 30, CorrespondingQuiverId = 12008 },
            [1753] = new ArrowInfo { ItemId = 1753, Name = "Steel Arrow", Element = AttackElement.Neutral, BaseAtk = 40, CorrespondingQuiverId = 12006 },
            [1754] = new ArrowInfo { ItemId = 1754, Name = "Crystal Arrow", Element = AttackElement.Water, BaseAtk = 30, CorrespondingQuiverId = 12012 },
            [1755] = new ArrowInfo { ItemId = 1755, Name = "Wind Arrow", Element = AttackElement.Wind, BaseAtk = 30, CorrespondingQuiverId = 12010 },
            [1756] = new ArrowInfo { ItemId = 1756, Name = "Stone Arrow", Element = AttackElement.Earth, BaseAtk = 30, CorrespondingQuiverId = 12011 },
            [1757] = new ArrowInfo { ItemId = 1757, Name = "Immaterial Arrow", Element = AttackElement.Ghost, BaseAtk = 30, CorrespondingQuiverId = 12014 },
            [1758] = new ArrowInfo { ItemId = 1758, Name = "Stun Arrow", Element = AttackElement.Neutral, BaseAtk = 1, CorrespondingQuiverId = 0 },
            [1759] = new ArrowInfo { ItemId = 1759, Name = "Frozen Arrow", Element = AttackElement.Water, BaseAtk = 1, CorrespondingQuiverId = 0 },
            [1762] = new ArrowInfo { ItemId = 1762, Name = "Rusty Arrow", Element = AttackElement.Poison, BaseAtk = 30, CorrespondingQuiverId = 12015 },
            [1763] = new ArrowInfo { ItemId = 1763, Name = "Poison Arrow", Element = AttackElement.Poison, BaseAtk = 1, CorrespondingQuiverId = 0 },
            [1764] = new ArrowInfo { ItemId = 1764, Name = "Sharp Arrow", Element = AttackElement.Neutral, BaseAtk = 10, CorrespondingQuiverId = 0 },
            [1765] = new ArrowInfo { ItemId = 1765, Name = "Oridecon Arrow", Element = AttackElement.Neutral, BaseAtk = 50, CorrespondingQuiverId = 12007 },
            [1767] = new ArrowInfo { ItemId = 1767, Name = "Shadow Arrow", Element = AttackElement.Dark, BaseAtk = 30, CorrespondingQuiverId = 12013 },
            [1770] = new ArrowInfo { ItemId = 1770, Name = "Iron Arrow", Element = AttackElement.Neutral, BaseAtk = 30, CorrespondingQuiverId = 12005 },
            [1772] = new ArrowInfo { ItemId = 1772, Name = "Holy Arrow", Element = AttackElement.Holy, BaseAtk = 50, CorrespondingQuiverId = 0 },
        };

        // Quiver to Arrow unpacking lookup
        public static readonly Dictionary<int, int> QuiverToArrowLookup = new()
        {
            [12005] = 1770, // Iron Arrow
            [12006] = 1753, // Steel Arrow
            [12007] = 1765, // Oridecon Arrow
            [12008] = 1752, // Fire Arrow
            [12009] = 1751, // Silver Arrow
            [12010] = 1755, // Wind Arrow
            [12011] = 1756, // Stone Arrow
            [12012] = 1754, // Crystal Arrow
            [12013] = 1767, // Shadow Arrow
            [12014] = 1757, // Immaterial Arrow
            [12015] = 1762, // Rusty Arrow
        };

        public static bool IsArcherClass(int jobId)
        {
            // Job 2: Archer, Job 8: Hunter, Job 14: Bard, Job 15: Dancer
            return jobId == 2 || jobId == 8 || jobId == 14 || jobId == 15;
        }

        public static void InitializeDatabase()
        {
            if (isInitialized) return;
            isInitialized = true;

            try
            {
                string json = null;
                string streamingPath = Path.Combine(Application.streamingAssetsPath, "ClientConfigGenerated/monsterdatabase.json");
                if (File.Exists(streamingPath))
                {
                    json = File.ReadAllText(streamingPath);
                }
                else
                {
                    string altPath = @"C:\games\RagnarokRebuild\RebuildClient_Data\StreamingAssets\ClientConfigGenerated\monsterdatabase.json";
                    if (File.Exists(altPath))
                    {
                        json = File.ReadAllText(altPath);
                    }
                }

                if (!string.IsNullOrEmpty(json))
                {
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in itemsElement.EnumerateArray())
                        {
                            int id = elem.GetProperty("Id").GetInt32();
                            string name = elem.GetProperty("Name").GetString();
                            string element = elem.GetProperty("Element").GetString();

                            if (id > 0 && !string.IsNullOrEmpty(element))
                            {
                                MonsterElementsById[id] = element;
                            }
                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(element))
                            {
                                MonsterElementsByName[name] = element;
                            }
                        }
                        Plugin.LogInfo($"[ArrowHelper] Successfully loaded {MonsterElementsById.Count} monster elemental definitions.");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogInfo($"[ArrowHelper] Could not load monster database: {ex.Message}");
            }
        }

        public static string GetMonsterElement(ServerControllable monster)
        {
            if (monster == null) return "Neutral1";
            if (!isInitialized) InitializeDatabase();

            if (MonsterElementsById.TryGetValue(monster.ClassId, out var el))
                return el;

            if (!string.IsNullOrEmpty(monster.Name) && MonsterElementsByName.TryGetValue(monster.Name, out var elName))
                return elName;

            return "Neutral1";
        }

        /// <summary>
        /// Ragnarok Rebuild elemental damage chart matrix (AttackElement vs Defender Element Type & Level)
        /// </summary>
        public static float GetElementMultiplier(AttackElement atk, string defElementStr)
        {
            if (string.IsNullOrEmpty(defElementStr)) defElementStr = "Neutral1";

            // Parse defender element name and level
            string defName = "Neutral";
            int defLvl = 1;

            if (defElementStr.Length >= 2 && char.IsDigit(defElementStr[defElementStr.Length - 1]))
            {
                defLvl = defElementStr[defElementStr.Length - 1] - '0';
                defName = defElementStr.Substring(0, defElementStr.Length - 1);
            }
            else
            {
                defName = defElementStr;
            }

            defLvl = Mathf.Clamp(defLvl, 1, 4);

            switch (defName.ToLowerInvariant())
            {
                case "neutral":
                    if (atk == AttackElement.Ghost)
                        return defLvl switch { 1 => 0.75f, 2 => 0.50f, 3 => 0.25f, 4 => 0.00f, _ => 1.0f };
                    return 1.0f;

                case "water":
                    if (atk == AttackElement.Wind)
                        return defLvl switch { 1 => 1.50f, 2 => 1.75f, 3 => 2.00f, 4 => 2.00f, _ => 1.0f };
                    if (atk == AttackElement.Fire)
                        return defLvl switch { 1 => 0.75f, 2 => 0.50f, 3 => 0.25f, 4 => 0.00f, _ => 1.0f };
                    if (atk == AttackElement.Water)
                        return defLvl switch { 1 => 0.50f, 2 => 0.25f, 3 => 0.00f, 4 => 0.00f, _ => 1.0f };
                    return 1.0f;

                case "earth":
                    if (atk == AttackElement.Fire)
                        return defLvl switch { 1 => 1.50f, 2 => 1.75f, 3 => 2.00f, 4 => 2.00f, _ => 1.0f };
                    if (atk == AttackElement.Wind)
                        return defLvl switch { 1 => 0.75f, 2 => 0.50f, 3 => 0.25f, 4 => 0.00f, _ => 1.0f };
                    if (atk == AttackElement.Earth)
                        return defLvl switch { 1 => 0.50f, 2 => 0.25f, 3 => 0.00f, 4 => 0.00f, _ => 1.0f };
                    if (atk == AttackElement.Poison)
                        return defLvl switch { 1 => 1.25f, 2 => 1.20f, 3 => 1.10f, 4 => 1.00f, _ => 1.0f };
                    return 1.0f;

                case "fire":
                    if (atk == AttackElement.Water)
                        return defLvl switch { 1 => 1.50f, 2 => 1.75f, 3 => 2.00f, 4 => 2.00f, _ => 1.0f };
                    if (atk == AttackElement.Earth)
                        return defLvl switch { 1 => 0.75f, 2 => 0.50f, 3 => 0.25f, 4 => 0.00f, _ => 1.0f };
                    if (atk == AttackElement.Fire)
                        return defLvl switch { 1 => 0.50f, 2 => 0.25f, 3 => 0.00f, 4 => 0.00f, _ => 1.0f };
                    if (atk == AttackElement.Poison)
                        return defLvl switch { 1 => 1.25f, 2 => 1.20f, 3 => 1.10f, 4 => 1.00f, _ => 1.0f };
                    return 1.0f;

                case "wind":
                    if (atk == AttackElement.Earth)
                        return defLvl switch { 1 => 1.50f, 2 => 1.75f, 3 => 2.00f, 4 => 2.00f, _ => 1.0f };
                    if (atk == AttackElement.Water)
                        return defLvl switch { 1 => 0.75f, 2 => 0.50f, 3 => 0.25f, 4 => 0.00f, _ => 1.0f };
                    if (atk == AttackElement.Wind)
                        return defLvl switch { 1 => 0.50f, 2 => 0.25f, 3 => 0.00f, 4 => 0.00f, _ => 1.0f };
                    if (atk == AttackElement.Poison)
                        return defLvl switch { 1 => 1.25f, 2 => 1.20f, 3 => 1.10f, 4 => 1.00f, _ => 1.0f };
                    return 1.0f;

                case "poison":
                    if (atk == AttackElement.Poison) return 0.0f;
                    if (atk == AttackElement.Holy)
                        return defLvl switch { 1 => 1.10f, 2 => 1.20f, 3 => 1.30f, 4 => 1.40f, _ => 1.0f };
                    if (atk == AttackElement.Dark)
                        return defLvl switch { 1 => 0.50f, 2 => 0.25f, 3 => 0.00f, 4 => 0.00f, _ => 1.0f };
                    if (atk == AttackElement.Undead)
                        return defLvl switch { 1 => 0.75f, 2 => 0.50f, 3 => 0.25f, 4 => 0.00f, _ => 1.0f };
                    return 1.0f;

                case "undead":
                    if (atk == AttackElement.Holy)
                        return defLvl switch { 1 => 1.50f, 2 => 1.75f, 3 => 2.00f, 4 => 2.00f, _ => 1.0f };
                    if (atk == AttackElement.Fire)
                        return defLvl switch { 1 => 1.25f, 2 => 1.50f, 3 => 1.75f, 4 => 2.00f, _ => 1.0f };
                    if (atk == AttackElement.Ghost)
                        return defLvl switch { 1 => 1.10f, 2 => 1.30f, 3 => 1.50f, 4 => 1.70f, _ => 1.0f };
                    if (atk == AttackElement.Poison) return 0.25f;
                    if (atk == AttackElement.Dark || atk == AttackElement.Undead) return 0.0f;
                    return 1.0f;

                case "dark":
                    if (atk == AttackElement.Holy)
                        return defLvl switch { 1 => 1.25f, 2 => 1.50f, 3 => 1.75f, 4 => 2.00f, _ => 1.0f };
                    if (atk == AttackElement.Ghost)
                        return defLvl switch { 1 => 0.75f, 2 => 0.50f, 3 => 0.25f, 4 => 0.00f, _ => 1.0f };
                    if (atk == AttackElement.Poison)
                        return defLvl switch { 1 => 0.80f, 2 => 0.70f, 3 => 0.60f, 4 => 0.50f, _ => 1.0f };
                    if (atk == AttackElement.Dark || atk == AttackElement.Undead) return 0.0f;
                    return 1.0f;

                case "holy":
                    if (atk == AttackElement.Dark)
                        return defLvl switch { 1 => 1.25f, 2 => 1.50f, 3 => 1.75f, 4 => 2.00f, _ => 1.0f };
                    if (atk == AttackElement.Undead)
                        return defLvl switch { 1 => 1.25f, 2 => 1.50f, 3 => 1.75f, 4 => 2.00f, _ => 1.0f };
                    if (atk == AttackElement.Holy) return 0.0f;
                    if (atk == AttackElement.Ghost)
                        return defLvl switch { 1 => 0.80f, 2 => 0.60f, 3 => 0.40f, 4 => 0.20f, _ => 1.0f };
                    return 1.0f;

                case "ghost":
                    if (atk == AttackElement.Ghost)
                        return defLvl switch { 1 => 1.50f, 2 => 1.70f, 3 => 1.90f, 4 => 2.00f, _ => 1.0f };
                    if (atk == AttackElement.Neutral)
                        return defLvl switch { 1 => 0.75f, 2 => 0.50f, 3 => 0.25f, 4 => 0.00f, _ => 1.0f };
                    if (atk == AttackElement.Undead)
                        return defLvl switch { 1 => 1.10f, 2 => 1.20f, 3 => 1.30f, 4 => 1.40f, _ => 1.0f };
                    return 1.0f;

                default:
                    return 1.0f;
            }
        }

        public static int GetTotalArrowCount()
        {
            if (!InventoryHelper.TryGetInventoryData(out var inv) || inv == null) return 0;
            int total = 0;
            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item.ItemData != null && (item.ItemData.ItemClass == ItemClass.Ammo || KnownArrows.ContainsKey(item.Id)))
                {
                    total += item.Count;
                }
            }
            return total;
        }

        private static float lastArrowEquipTime = 0f;
        private static int lastEquippedArrowBagSlot = -1;

        public static bool EquipBestArrowForTarget(NetworkManager netManager, ServerControllable target)
        {
            if (netManager == null || target == null || !target.IsCharacterAlive) return false;
            if (!BotConfigManager.Current.AutoEquipBestArrow) return false;

            var state = PlayerState.Instance;
            if (state == null || !IsArcherClass(state.JobId)) return false;

            // Pacing delay between equip changes (1s)
            if (Time.time - lastArrowEquipTime < 1.0f) return false;

            if (!InventoryHelper.TryGetInventoryData(out var inv) || inv == null) return false;

            string monsterElement = GetMonsterElement(target);

            int bestBagSlot = -1;
            int bestArrowId = -1;
            string bestArrowName = "";
            AttackElement bestElement = AttackElement.Neutral;
            float bestScore = -1f;

            // Unpack quivers if running out of regular arrows
            CheckAndUnpackQuivers(netManager, inv);

            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item.ItemData == null || item.Count <= 0) continue;

                if (KnownArrows.TryGetValue(item.Id, out var arrowInfo))
                {
                    float mult = GetElementMultiplier(arrowInfo.Element, monsterElement);
                    float score = arrowInfo.BaseAtk * mult;

                    // Prefer higher elemental damage score; tiebreak with higher stock count
                    if (score > bestScore || (Mathf.Approximately(score, bestScore) && bestBagSlot != -1 && item.Count > inv[bestBagSlot].Count))
                    {
                        bestScore = score;
                        bestBagSlot = item.BagSlotId;
                        bestArrowId = item.Id;
                        bestArrowName = arrowInfo.Name;
                        bestElement = arrowInfo.Element;
                    }
                }
            }

            if (bestBagSlot != -1)
            {
                // Check if already equipped
                if (state.AmmoId != bestBagSlot && lastEquippedArrowBagSlot != bestBagSlot)
                {
                    netManager.SendEquipItem(bestBagSlot);
                    lastArrowEquipTime = Time.time;
                    lastEquippedArrowBagSlot = bestBagSlot;
                    BotEngine.Instance?.LogEvent($"[Arrow] Equipped '{bestArrowName}' ({bestElement}, Atk: {KnownArrows[bestArrowId].BaseAtk}) vs {target.Name} ({monsterElement}) [Multiplier: {GetElementMultiplier(bestElement, monsterElement) * 100f:F0}%].");
                    return true;
                }
            }

            return false;
        }

        private static float lastQuiverUseTime = 0f;

        private static void CheckAndUnpackQuivers(NetworkManager netManager, Il2CppSystem.Collections.Generic.SortedDictionary<int, InventoryItem> inv)
        {
            if (Time.time - lastQuiverUseTime < 5.0f) return;

            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item.ItemData == null || item.Count <= 0) continue;

                if (QuiverToArrowLookup.TryGetValue(item.Id, out int arrowId))
                {
                    // Check how many of this arrow we currently have
                    int currentArrowCount = 0;
                    foreach (var checkKvp in inv)
                    {
                        var check = checkKvp.Value;
                        if (check.Id == arrowId) currentArrowCount += check.Count;
                    }

                    // If we have fewer than 50 arrows of this type, unpack a quiver!
                    if (currentArrowCount < 50)
                    {
                        netManager.SendUseItem(item.BagSlotId);
                        lastQuiverUseTime = Time.time;
                        BotEngine.Instance?.LogEvent($"[Arrow] Unpacked quiver '{item.ProperName()}' to replenish arrows!");
                        return;
                    }
                }
            }
        }
    }
}
