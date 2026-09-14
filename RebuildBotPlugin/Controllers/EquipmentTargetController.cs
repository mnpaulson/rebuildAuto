using System;
using System.Collections.Generic;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using RebuildBotPlugin.Models;
using RebuildBotPlugin.Services;
using RebuildSharedData.ClientTypes;
using RebuildSharedData.Enum;
using RebuildSharedData.Networking;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public enum EquipmentPurchasePhase
    {
        Idle,
        TravelingToVendor,
        InteractingWithVendor,
        EquippingNewItem,
        SellingRetiredGear,
        PostPurchaseRefining,
        Completed
    }

    public class EquipmentTargetController
    {
        public EquipmentPurchasePhase CurrentPhase { get; private set; } = EquipmentPurchasePhase.Idle;
        public bool IsActive => CurrentPhase != EquipmentPurchasePhase.Idle;

        private EquipmentTarget activeTarget = null;
        private ShopItemEntry activeVendor = null;
        private readonly NpcInteractionHelper npcHelper = new();
        private readonly BlacksmithHelper blacksmithHelper = new();
        private float lastStepTime = 0f;
        private int internalStep = 0;
        private float lastCheckTime = 0f;
        private const float CheckInterval = 3.0f; // Check every 3 seconds

        public bool CheckTripInterrupt(NetworkManager netManager, ServerControllable player, float now)
        {
            if (IsActive) return true;
            if (now - lastCheckTime < CheckInterval) return false;
            lastCheckTime = now;

            if (netManager == null || player == null || !player.IsCharacterAlive) return false;

            var targets = BotConfigManager.Current.EquipmentTargets;
            if (targets == null || targets.Count == 0) return false;

            var state = PlayerState.Instance;
            if (state == null) return false;

            int playerLevel = player.Level;
            int playerZeny = state.GetData(RebuildSharedData.Enum.EntityStats.PlayerStat.Zeny);

            // Group targets by equipment slot preserving config progression order
            var targetsBySlot = new Dictionary<string, List<(int originalIndex, EquipmentTarget target)>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || string.IsNullOrWhiteSpace(t.ItemName)) continue;
                string slot = GetTargetSlot(t);
                if (!targetsBySlot.TryGetValue(slot, out var list))
                {
                    list = new List<(int, EquipmentTarget)>();
                    targetsBySlot[slot] = list;
                }
                list.Add((i, t));
            }

            bool holdingTwoHanded = IsHoldingTwoHandedWeapon();

            EquipmentTarget bestCandidate = null;
            ShopItemEntry bestVendor = null;

            foreach (var kvp in targetsBySlot)
            {
                string slotName = kvp.Key;
                var slotTargets = kvp.Value;

                // If wielding a 2-handed weapon, suppress shield slot targets completely
                if (holdingTwoHanded && (string.Equals(slotName, "Shield", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(slotName, "LeftHand", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // 1. Determine the highest-tier target in this slot that the player ALREADY owns (equipped, bag, or storage)
                int maxOwnedSlotIndex = -1;
                for (int i = 0; i < slotTargets.Count; i++)
                {
                    var t = slotTargets[i].target;
                    if (IsItemOwned(t.ItemName))
                    {
                        maxOwnedSlotIndex = Math.Max(maxOwnedSlotIndex, i);
                    }
                }

                // 2. If the player owns a target for this slot, ensure the highest owned target is equipped if usable
                if (maxOwnedSlotIndex >= 0)
                {
                    var ownedTarget = slotTargets[maxOwnedSlotIndex].target;
                    if (playerLevel >= ownedTarget.MinLevel && !IsItemEquipped(ownedTarget.ItemName))
                    {
                        if (EquipmentController.TryFindBestItemInInventory(ownedTarget.ItemName, out var invItem))
                        {
                            netManager.SendEquipItem(invItem.BagSlotId);
                            BotEngine.Instance?.LogEvent($"[Equipment Target] Equipping already owned '{ownedTarget.ItemName}' (Bag Slot: {invItem.BagSlotId}) from inventory.");
                        }
                    }
                }

                // 3. For purchasing, ONLY consider targets strictly higher than maxOwnedSlotIndex!
                // Any target <= maxOwnedSlotIndex is already owned or is a downgrade. Never downgrade!
                for (int i = slotTargets.Count - 1; i > maxOwnedSlotIndex; i--)
                {
                    var target = slotTargets[i].target;
                    if (playerLevel < target.MinLevel) continue;
                    if (playerZeny < target.MinZeny) continue;

                    if (ShopRegistry.TryGetEntry(target.ItemName, out var vendorEntry))
                    {
                        if (bestCandidate == null || target.MinZeny > bestCandidate.MinZeny)
                        {
                            bestCandidate = target;
                            bestVendor = vendorEntry;
                        }
                        break; // Found highest affordable upgrade for this slot
                    }
                }
            }

            if (bestCandidate != null && bestVendor != null)
            {
                // Trigger hunting trip interrupt!
                StartPurchase(bestCandidate, bestVendor, now);
                return true;
            }

            return false;
        }

        public void StartPurchase(EquipmentTarget target, ShopItemEntry vendor, float now)
        {
            activeTarget = target;
            activeVendor = vendor;
            CurrentPhase = EquipmentPurchasePhase.TravelingToVendor;
            internalStep = 0;
            lastStepTime = now;
            npcHelper.Reset();

            BotEngine.Instance?.LogEvent($"[Equipment Target] Trip Interrupt: Purchasing '{target.ItemName}' from {vendor.NpcName} in {vendor.VendorMap} (Target Refine: +{target.TargetRefineLevel}).");
        }

        public bool Process(BotEngine bot, float now)
        {
            var netManager = NetworkManager.Instance;
            var player = bot.Player;
            if (netManager == null || player == null || activeTarget == null || activeVendor == null)
            {
                Reset();
                return false;
            }

            switch (CurrentPhase)
            {
                case EquipmentPurchasePhase.TravelingToVendor:
                    if (string.Equals(netManager.CurrentMap, activeVendor.VendorMap, StringComparison.OrdinalIgnoreCase))
                    {
                        // Arrived on vendor map, interact with vendor NPC
                        CurrentPhase = EquipmentPurchasePhase.InteractingWithVendor;
                        npcHelper.Reset();
                        npcHelper.Begin(activeVendor.NpcName, activeVendor.NpcPosition);
                        internalStep = 0;
                        lastStepTime = now;
                        BotEngine.Instance?.LogEvent($"[Equipment Target] Arrived in {activeVendor.VendorMap}. Approaching {activeVendor.NpcName} at ({activeVendor.NpcPosition.x}, {activeVendor.NpcPosition.y}).");
                    }
                    else
                    {
                        // Use Butterfly Wing if in non-town map
                        if (internalStep == 0)
                        {
                            internalStep = 1;
                            if (!TownRoutineController.IsTownMap(netManager.CurrentMap))
                            {
                                int bwing = InventoryHelper.FindFirstItemId(602, 12324);
                                if (bwing > 0)
                                {
                                    netManager.SendUseItem(bwing);
                                    lastStepTime = now + 1.2f;
                                    return true;
                                }
                            }
                        }

                        var travelState = BotState.TravelingToTargetMap;
                        bot.Navigation.ProcessTravel(
                            netManager,
                            player,
                            now,
                            ref travelState,
                            destinationMapOverride: activeVendor.VendorMap,
                            targetCellPos: activeVendor.NpcPosition);
                    }
                    return true;

                case EquipmentPurchasePhase.InteractingWithVendor:
                    bool busy = npcHelper.Process(
                        netManager,
                        player,
                        bot.Navigation,
                        now,
                        onOptionMenu: (options) =>
                        {
                            if (options != null && options.Length > 0)
                            {
                                options[0].OnClick(); // First option is usually "Buy" or shop open
                                internalStep = 1;
                                lastStepTime = now;
                            }
                            return true;
                        },
                        onDialogOpen: null,
                        onNoUiVisible: () =>
                        {
                            if (internalStep == 1 && now - lastStepTime >= 0.5f)
                            {
                                // Look up ItemData by canonical name
                                int targetItemId = GetItemIdByName(activeTarget.ItemName);
                                if (targetItemId > 0)
                                {
                                    var msg = netManager.StartMessage(PacketType.ShopBuySell);
                                    msg.Write(1); // 1 item type
                                    msg.Write(targetItemId);
                                    msg.Write(1); // buy 1 count
                                    netManager.SendMessage(msg);
                                    BotEngine.Instance?.LogEvent($"[Equipment Target] Dispatched purchase packet for '{activeTarget.ItemName}' (ID: {targetItemId}).");
                                }

                                NpcInteractionHelper.CleanupNpcUi();
                                CurrentPhase = EquipmentPurchasePhase.EquippingNewItem;
                                internalStep = 0;
                                lastStepTime = now + 0.8f;
                                return true;
                            }
                            return false;
                        }
                    );
                    return true;

                case EquipmentPurchasePhase.EquippingNewItem:
                    if (now - lastStepTime >= 0.5f)
                    {
                        // Find newly purchased item in inventory
                        if (EquipmentController.TryFindBestItemInInventory(activeTarget.ItemName, out var newItem))
                        {
                            netManager.SendEquipItem(newItem.BagSlotId);
                            BotEngine.Instance?.LogEvent($"[Equipment Target] Equipped '{activeTarget.ItemName}' (Bag Slot: {newItem.BagSlotId}).");

                            CheckAndAdvanceToRefine(bot, now);
                        }
                        else if (internalStep++ > 10)
                        {
                            BotEngine.Instance?.LogEvent($"[Equipment Target] Purchase verification timed out for '{activeTarget.ItemName}'.");
                            Reset();
                            return false;
                        }
                    }
                    return true;

                case EquipmentPurchasePhase.SellingRetiredGear:
                    // Obsolete phase: replaced gear is safely sold during standard town routine
                    CheckAndAdvanceToRefine(bot, now);
                    return true;

                case EquipmentPurchasePhase.PostPurchaseRefining:
                    if (blacksmithHelper.Process(bot, now))
                    {
                        return true;
                    }

                    BotEngine.Instance?.LogEvent($"[Equipment Target] Refine process finished ({blacksmithHelper.StatusMessage}). Resuming hunt.");
                    CurrentPhase = EquipmentPurchasePhase.Completed;
                    Reset();
                    return false;

                case EquipmentPurchasePhase.Completed:
                    Reset();
                    return false;
            }

            return false;
        }

        private void CheckAndAdvanceToRefine(BotEngine bot, float now)
        {
            if (activeTarget != null && activeTarget.TargetRefineLevel > 0)
            {
                // Attempt to refine immediately if affordable
                if (blacksmithHelper.Begin(activeTarget.ItemName, activeTarget.TargetRefineLevel, safeLimitOnly: false, out string error, maxAttemptsCount: 5))
                {
                    CurrentPhase = EquipmentPurchasePhase.PostPurchaseRefining;
                    internalStep = 0;
                    lastStepTime = now;
                    BotEngine.Instance?.LogEvent($"[Equipment Target] Starting immediate post-purchase refine on '{activeTarget.ItemName}' to +{activeTarget.TargetRefineLevel}.");
                    return;
                }
                else
                {
                    BotEngine.Instance?.LogEvent($"[Equipment Target] Post-purchase refine deferred: {error}. Resuming hunt.");
                }
            }

            CurrentPhase = EquipmentPurchasePhase.Completed;
            Reset();
        }

        public static void ProcessKafraOreWithdrawal(NetworkManager netManager)
        {
            if (netManager == null) return;
            var targets = BotConfigManager.Current.EquipmentTargets;
            if (targets == null || targets.Count == 0) return;

            // Check if any equipped target has pending refines
            foreach (var target in targets)
            {
                if (target == null || target.TargetRefineLevel <= 0) continue;
                if (!EquipmentController.TryFindItemInInventory(target.ItemName, -1, out var item) || item.ItemData == null) continue;
                if (!EquipmentController.IsItemEquipped(item.BagSlotId)) continue;

                int currentRefine = item.Type == ItemType.UniqueItem ? (int)item.UniqueItem.Refine : 0;
                if (currentRefine >= target.TargetRefineLevel) continue;

                int oreId = EquipmentController.GetRefineOreId(item.ItemData);
                int roughId = (oreId == 984) ? 756 : (oreId == 985 ? 757 : 0);

                if (oreId == 984 || oreId == 985)
                {
                    InventoryHelper.CountTotalEffectiveOres(oreId, roughId, out int invP, out int invR, out int storP, out int storR, out int totalEff);
                    int attemptsNeeded = Math.Min(5, target.TargetRefineLevel - currentRefine);

                    if (totalEff >= attemptsNeeded)
                    {
                        // Withdraw pure ores first
                        if (invP < attemptsNeeded && storP > 0)
                        {
                            int withdrawPure = Math.Min(attemptsNeeded - invP, storP);
                            int storSlot = FindStorageSlotForId(oreId);
                            if (storSlot >= 0)
                            {
                                netManager.SendMoveStorageItem(storSlot, withdrawPure, false);
                                BotEngine.Instance?.LogEvent($"[Equipment Target] Withdrew {withdrawPure}x pure ore (ID: {oreId}) from Kafra storage for refine.");
                                invP += withdrawPure;
                            }
                        }

                        // Withdraw rough ores if still needed
                        int remainingAttempts = attemptsNeeded - invP;
                        if (remainingAttempts > 0 && storR >= 5)
                        {
                            int neededRough = remainingAttempts * 5;
                            int withdrawRough = Math.Min(neededRough, (storR / 5) * 5);
                            int storSlot = FindStorageSlotForId(roughId);
                            if (storSlot >= 0 && withdrawRough > 0)
                            {
                                netManager.SendMoveStorageItem(storSlot, withdrawRough, false);
                                BotEngine.Instance?.LogEvent($"[Equipment Target] Withdrew {withdrawRough}x rough ore (ID: {roughId}) from Kafra storage for purify.");
                            }
                        }
                    }
                }
            }
        }

        public static bool HasPendingBlacksmithTrip(out string itemName, out int targetRefine, out int maxAttempts)
        {
            itemName = null;
            targetRefine = 0;
            maxAttempts = 5;

            var targets = BotConfigManager.Current.EquipmentTargets;
            if (targets == null || targets.Count == 0) return false;

            var state = PlayerState.Instance;
            int zeny = state != null ? state.GetData(RebuildSharedData.Enum.EntityStats.PlayerStat.Zeny) : 0;

            foreach (var target in targets)
            {
                if (target == null || target.TargetRefineLevel <= 0) continue;
                if (!EquipmentController.TryFindItemInInventory(target.ItemName, -1, out var item) || item.ItemData == null) continue;
                if (!EquipmentController.IsItemEquipped(item.BagSlotId)) continue;

                int currentRefine = item.Type == ItemType.UniqueItem ? (int)item.UniqueItem.Refine : 0;
                if (currentRefine >= target.TargetRefineLevel) continue;

                int oreId = EquipmentController.GetRefineOreId(item.ItemData);
                int roughId = (oreId == 984) ? 756 : (oreId == 985 ? 757 : 0);
                int fee = BlacksmithHelper.GetRefineFee(item.ItemData);

                if (oreId == 984 || oreId == 985)
                {
                    int pureCount = InventoryHelper.GetItemCount(oreId);
                    int roughCount = roughId > 0 ? InventoryHelper.GetItemCount(roughId) : 0;
                    int totalAttempts = pureCount + (roughCount / 5);
                    int attemptsNeeded = Math.Min(5, target.TargetRefineLevel - currentRefine);

                    if (attemptsNeeded > 0 && totalAttempts >= attemptsNeeded && zeny >= fee * attemptsNeeded)
                    {
                        itemName = target.ItemName;
                        targetRefine = target.TargetRefineLevel;
                        maxAttempts = attemptsNeeded;
                        return true;
                    }
                }
                else
                {
                    // Basic ores (Phracon/Emveretarcon) can be bought from Vurewell
                    int oreCost = BlacksmithHelper.GetOreBuyPrice(oreId);
                    int attempts = Math.Min(target.TargetRefineLevel - currentRefine, 7);
                    int costPerAttempt = fee + oreCost;
                    if (zeny >= costPerAttempt * attempts)
                    {
                        itemName = target.ItemName;
                        targetRefine = target.TargetRefineLevel;
                        maxAttempts = attempts;
                        return true;
                    }
                }
            }

            return false;
        }

        private static int FindStorageSlotForId(int itemId)
        {
            if (!InventoryHelper.TryGetStorageData(out var storageData)) return -1;
            foreach (var kvp in storageData)
            {
                if (kvp.Value != null && kvp.Value.ItemData != null && kvp.Value.ItemData.Id == itemId && kvp.Value.Count > 0)
                {
                    return kvp.Key;
                }
            }
            return -1;
        }

        public static string GetTargetSlot(EquipmentTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.ItemName))
                return "Unknown";

            if (ShopRegistry.TryGetEntry(target.ItemName, out var entry) && !string.IsNullOrEmpty(entry.EquipSlot))
            {
                return entry.EquipSlot;
            }

            var loader = Assets.Scripts.Sprites.ClientDataLoader.Instance;
            if (loader != null)
            {
                if (loader.TryGetItemByName(target.ItemName, out var dat) && dat != null)
                {
                    int slotIdx = EquipmentController.ResolveSlotIndex(dat.Position.ToString());
                    if (slotIdx >= 0) return EquipmentController.GetSlotName(slotIdx);
                }
                string norm = target.ItemName.Trim().Replace(' ', '_');
                if (loader.TryGetItemByName(norm, out var datNorm) && datNorm != null)
                {
                    int slotIdx = EquipmentController.ResolveSlotIndex(datNorm.Position.ToString());
                    if (slotIdx >= 0) return EquipmentController.GetSlotName(slotIdx);
                }
            }

            return "Weapon";
        }

        public static bool IsHoldingTwoHandedWeapon()
        {
            var state = PlayerState.Instance;
            if (state == null || state.EquippedItems == null || state.EquippedItems.Length <= 4) return false;
            int weaponBagId = state.EquippedItems[4];
            if (weaponBagId <= 0) return false;
            if (InventoryHelper.TryGetInventoryItem(weaponBagId, out var itm) && itm.ItemData != null)
            {
                var wData = itm.ItemData;
                return wData.ItemClass == ItemClass.Weapon && (wData.Position == EquipPosition.BothHands || (wData.Position & EquipPosition.OffHand) != 0);
            }
            return false;
        }

        public static bool IsItemMatch(ItemData data, string targetName)
        {
            if (data == null || string.IsNullOrWhiteSpace(targetName)) return false;
            string norm = targetName.Trim().Replace('_', ' ');
            return string.Equals(data.Name, targetName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(data.Code, targetName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(data.Name?.Replace('_', ' '), norm, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsItemEquipped(string itemName)
        {
            var state = PlayerState.Instance;
            if (state == null || state.EquippedItems == null) return false;

            for (int i = 0; i < state.EquippedItems.Length; i++)
            {
                int bagSlotId = state.EquippedItems[i];
                if (bagSlotId > 0 && InventoryHelper.TryGetInventoryItem(bagSlotId, out var item) && item.ItemData != null)
                {
                    if (IsItemMatch(item.ItemData, itemName))
                        return true;
                }
            }
            return false;
        }

        public static bool IsItemOwned(string itemName)
        {
            if (IsItemEquipped(itemName)) return true;
            if (EquipmentController.TryFindItemInInventory(itemName, -1, out _)) return true;
            if (IsItemInStorage(itemName)) return true;
            return false;
        }

        public static bool IsItemEquippedWithRefine(string itemName, int targetRefine)
        {
            var state = PlayerState.Instance;
            if (state == null || state.EquippedItems == null) return false;

            for (int i = 0; i < state.EquippedItems.Length; i++)
            {
                int bagSlotId = state.EquippedItems[i];
                if (bagSlotId > 0 && InventoryHelper.TryGetInventoryItem(bagSlotId, out var item) && item.ItemData != null)
                {
                    if (IsItemMatch(item.ItemData, itemName))
                    {
                        int refine = item.Type == ItemType.UniqueItem ? (int)item.UniqueItem.Refine : 0;
                        return refine >= targetRefine;
                    }
                }
            }
            return false;
        }

        public static bool IsItemInStorage(string itemName)
        {
            if (!InventoryHelper.TryGetStorageData(out var storageData)) return false;
            foreach (var kvp in storageData)
            {
                var item = kvp.Value;
                if (item?.ItemData == null) continue;
                if (IsItemMatch(item.ItemData, itemName))
                {
                    return true;
                }
            }
            return false;
        }

        private static int GetEquippedSlotBagId(string equipSlot)
        {
            int slotIdx = EquipmentController.ResolveSlotIndex(equipSlot);
            var state = PlayerState.Instance;
            if (state != null && state.EquippedItems != null && slotIdx >= 0 && slotIdx < state.EquippedItems.Length)
            {
                return state.EquippedItems[slotIdx];
            }
            return -1;
        }

        private static int GetItemIdByName(string itemName)
        {
            // Search in inventory or try to find in client data
            if (EquipmentController.TryFindItemInInventory(itemName, -1, out var item) && item.ItemData != null)
            {
                return item.ItemData.Id;
            }

            // Fallback: check ClientDataLoader
            var loader = Assets.Scripts.Sprites.ClientDataLoader.Instance;
            if (loader != null)
            {
                if (loader.TryGetItemByName(itemName, out var itemDat) && itemDat != null)
                {
                    return itemDat.Id;
                }
                string norm = itemName.Trim().Replace(' ', '_');
                if (loader.TryGetItemByName(norm, out var itemDatNorm) && itemDatNorm != null)
                {
                    return itemDatNorm.Id;
                }
            }

            return -1;
        }

        public void Reset()
        {
            CurrentPhase = EquipmentPurchasePhase.Idle;
            activeTarget = null;
            activeVendor = null;
            internalStep = 0;
            npcHelper.Reset();
            blacksmithHelper.Reset();
        }
    }
}
