using System;
using System.Collections.Generic;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using RebuildBotPlugin.Services;
using RebuildSharedData.ClientTypes;
using RebuildSharedData.Enum;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    /// <summary>
    /// Manages character equipment:
    /// 1. Auto-equipping starter weapons upon job promotion.
    /// 2. "Finders Keepers" auto-equipping of newly looted gear into empty equipment slots.
    /// </summary>
    public class EquipmentController
    {
        private float lastEquipCheckTime = 0f;
        private const float EquipCheckInterval = 2.0f; // Check every 2s

        public void ProcessAutoEquip(NetworkManager netManager, ServerControllable player, float now)
        {
            if (!BotConfigManager.Current.AutoEquipEmptySlots) return;
            if (netManager == null || player == null || !player.IsCharacterAlive) return;

            // Run on a paced interval
            if (now - lastEquipCheckTime < EquipCheckInterval) return;
            lastEquipCheckTime = now;

            var state = PlayerState.Instance;
            if (state == null || state.EquippedItems == null) return;
            if (!InventoryHelper.TryGetInventoryData(out var inv) || inv == null) return;

            // Find empty slots
            // [0] HeadTop, [1] HeadMid, [2] HeadBottom, [3] Body, [4] RightHand (Weapon),
            // [5] LeftHand (Shield), [6] Garment, [7] Footgear, [8] Accessory1, [9] Accessory2
            bool headTopEmpty = state.EquippedItems[0] == 0;
            bool headMidEmpty = state.EquippedItems[1] == 0;
            bool headBottomEmpty = state.EquippedItems[2] == 0;
            bool bodyEmpty = state.EquippedItems[3] == 0;
            bool weaponEmpty = state.EquippedItems[4] == 0;
            bool shieldEmpty = state.EquippedItems[5] == 0;
            bool garmentEmpty = state.EquippedItems[6] == 0;
            bool footgearEmpty = state.EquippedItems[7] == 0;
            bool acc1Empty = state.EquippedItems[8] == 0;
            bool acc2Empty = state.EquippedItems[9] == 0;

            bool hasAnyEmptySlot = headTopEmpty || headMidEmpty || headBottomEmpty || bodyEmpty ||
                                  weaponEmpty || shieldEmpty || garmentEmpty || footgearEmpty ||
                                  acc1Empty || acc2Empty;

            if (!hasAnyEmptySlot) return;

            // Track bag IDs that are currently equipped
            var equippedBagSlots = new HashSet<int>();
            for (int i = 0; i < state.EquippedItems.Length; i++)
            {
                if (state.EquippedItems[i] > 0)
                    equippedBagSlots.Add(state.EquippedItems[i]);
            }

            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item == null || item.ItemData == null || item.Count <= 0) continue;
                if (equippedBagSlots.Contains(item.BagSlotId)) continue;

                var data = item.ItemData;
                if (data.ItemClass != ItemClass.Equipment && data.ItemClass != ItemClass.Weapon)
                    continue;

                // Explicit blacklist: Never auto-equip the basic starter Knife into empty weapon slots
                string itemName = data.Name ?? "";
                if (itemName.Equals("Knife", StringComparison.OrdinalIgnoreCase) ||
                    itemName.Equals("Novice Knife", StringComparison.OrdinalIgnoreCase) ||
                    itemName.Equals("Novice_Knife", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var pos = data.Position;
                string slotName = null;

                if (headTopEmpty && (pos & EquipPosition.HeadUpper) != 0)
                {
                    slotName = "HeadTop";
                    headTopEmpty = false;
                }
                else if (headMidEmpty && (pos & EquipPosition.HeadMid) != 0)
                {
                    slotName = "HeadMid";
                    headMidEmpty = false;
                }
                else if (headBottomEmpty && (pos & EquipPosition.HeadLower) != 0)
                {
                    slotName = "HeadBottom";
                    headBottomEmpty = false;
                }
                else if (bodyEmpty && (pos & EquipPosition.Body) != 0)
                {
                    slotName = "Armor";
                    bodyEmpty = false;
                }
                else if (weaponEmpty && (pos & EquipPosition.MainHand) != 0)
                {
                    slotName = "Weapon";
                    weaponEmpty = false;
                }
                else if (shieldEmpty && (pos & EquipPosition.OffHand) != 0)
                {
                    slotName = "Shield";
                    shieldEmpty = false;
                }
                else if (garmentEmpty && (pos & EquipPosition.Garment) != 0)
                {
                    slotName = "Garment";
                    garmentEmpty = false;
                }
                else if (footgearEmpty && (pos & EquipPosition.Footgear) != 0)
                {
                    slotName = "Footgear";
                    footgearEmpty = false;
                }
                else if (acc1Empty && (pos & EquipPosition.Accessory) != 0)
                {
                    slotName = "Accessory 1";
                    acc1Empty = false;
                }
                else if (acc2Empty && (pos & EquipPosition.Accessory) != 0)
                {
                    slotName = "Accessory 2";
                    acc2Empty = false;
                }

                if (slotName != null)
                {
                    netManager.SendEquipItem(item.BagSlotId);
                    equippedBagSlots.Add(item.BagSlotId);
                    BotEngine.Instance?.LogEvent($"[Equipment] Auto-equipped '{data.Name}' into empty {slotName} slot (Bag Slot: {item.BagSlotId}).");
                    return; // Equip one piece per tick to avoid packet flooding
                }
            }
        }

        /// <summary>
        /// Equips the class starter weapon granted upon 1st Job promotion.
        /// </summary>
        public static bool EquipStarterWeapon(NetworkManager netManager, int jobId)
        {
            if (netManager == null || !InventoryHelper.TryGetInventoryData(out var inv) || inv == null) return false;

            // Bard promotion gives:
            // Job 1 (Swordman): Sword
            // Job 2 (Archer): Bow + Arrow
            // Job 3 (Mage): Novice_Rod / Rod
            // Job 4 (Acolyte): Mace
            // Job 5 (Thief): Cutter
            // Job 6 (Merchant): Axe

            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item == null || item.ItemData == null || item.Count <= 0) continue;
                string name = item.ItemData.Name ?? "";

                bool shouldEquip = false;
                switch (jobId)
                {
                    case 1: shouldEquip = name.Equals("Sword", StringComparison.OrdinalIgnoreCase); break;
                    case 2: shouldEquip = name.Equals("Bow", StringComparison.OrdinalIgnoreCase); break;
                    case 3: shouldEquip = name.Equals("Novice_Rod", StringComparison.OrdinalIgnoreCase) || name.Equals("Rod", StringComparison.OrdinalIgnoreCase); break;
                    case 4: shouldEquip = name.Equals("Mace", StringComparison.OrdinalIgnoreCase); break;
                    case 5: shouldEquip = name.Equals("Cutter", StringComparison.OrdinalIgnoreCase); break;
                    case 6: shouldEquip = name.Equals("Axe", StringComparison.OrdinalIgnoreCase); break;
                }

                if (shouldEquip)
                {
                    netManager.SendEquipItem(item.BagSlotId);
                    BotEngine.Instance?.LogEvent($"[Equipment] Auto-equipped starter weapon '{name}' (Bag Slot: {item.BagSlotId}).");
                    return true;
                }
            }
            return false;
        }

        public static int ResolveSlotIndex(string slotName, EquipSlot slot = EquipSlot.None)
        {
            if (slot != EquipSlot.None) return (int)slot;
            if (string.IsNullOrWhiteSpace(slotName)) return -1;

            string clean = slotName.ToLowerInvariant().Replace(" ", "").Replace("_", "");
            if (clean.Contains("headtop") || clean.Contains("upper")) return 0;
            if (clean.Contains("headmid") || clean.Contains("mid")) return 1;
            if (clean.Contains("headlow") || clean.Contains("bottom")) return 2;
            if (clean.Contains("armor") || clean.Contains("body")) return 3;
            if (clean.Contains("weapon") || clean.Contains("mainhand") || clean.Contains("right")) return 4;
            if (clean.Contains("shield") || clean.Contains("offhand") || clean.Contains("left")) return 5;
            if (clean.Contains("garment") || clean.Contains("cloak") || clean.Contains("muffler")) return 6;
            if (clean.Contains("boots") || clean.Contains("shoes") || clean.Contains("foot")) return 7;
            if (clean.Contains("acc1") || clean.Contains("accessory1")) return 8;
            if (clean.Contains("acc2") || clean.Contains("accessory2")) return 9;
            return -1;
        }

        public static string GetSlotName(int slotIndex)
        {
            switch (slotIndex)
            {
                case 0: return "HeadTop";
                case 1: return "HeadMid";
                case 2: return "HeadBottom";
                case 3: return "Armor";
                case 4: return "Weapon";
                case 5: return "Shield";
                case 6: return "Garment";
                case 7: return "Footgear";
                case 8: return "Accessory 1";
                case 9: return "Accessory 2";
                default: return $"Slot {slotIndex}";
            }
        }

        public static bool IsItemEquipped(int bagSlotId)
        {
            var state = PlayerState.Instance;
            if (state == null || state.EquippedItems == null) return false;
            for (int i = 0; i < state.EquippedItems.Length; i++)
            {
                if (state.EquippedItems[i] == bagSlotId) return true;
            }
            return false;
        }

        public static bool TryFindItemInInventory(string itemName, int bagSlotId, out InventoryItem foundItem)
        {
            foundItem = default;
            if (!InventoryHelper.TryGetInventoryData(out var inv) || inv == null) return false;

            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item == null || item.Count <= 0 || item.ItemData == null) continue;

                if (bagSlotId >= 0 && item.BagSlotId == bagSlotId)
                {
                    foundItem = item;
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(itemName) &&
                    (string.Equals(item.ItemData.Name, itemName, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(item.ItemData.Code, itemName, StringComparison.OrdinalIgnoreCase) ||
                     item.ItemData.Name.IndexOf(itemName, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    foundItem = item;
                    return true;
                }
            }
            return false;
        }

        public static int GetRefineSafeLimit(ItemData dat)
        {
            if (dat == null) return 4;
            if (dat.ItemClass == ItemClass.Equipment) return 4; // Armor: +4
            if (dat.ItemClass == ItemClass.Weapon)
            {
                switch (dat.ItemRank)
                {
                    case 1: return 7; // Level 1 weapon: +7
                    case 2: return 6; // Level 2 weapon: +6
                    case 3: return 5; // Level 3 weapon: +5
                    case 4: return 4; // Level 4 weapon: +4
                }
            }
            return 4;
        }

        public static int GetRefineOreId(ItemData dat)
        {
            if (dat == null) return 1010; // Phracon default
            if (dat.ItemClass == ItemClass.Equipment) return 985; // Elunium for armor
            if (dat.ItemClass == ItemClass.Weapon)
            {
                switch (dat.ItemRank)
                {
                    case 1: return 1010; // Phracon
                    case 2: return 1011; // Emveretarcon
                    case 3: return 984;  // Oridecon
                    case 4: return 984;  // Oridecon
                }
            }
            return 1010;
        }
    }
}
