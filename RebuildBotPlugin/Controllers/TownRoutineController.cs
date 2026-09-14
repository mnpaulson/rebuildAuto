using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI;
using Assets.Scripts.UI.Inventory;
using RebuildBotPlugin.Models;
using RebuildBotPlugin.Services;
using RebuildSharedData.Data;
using RebuildSharedData.Enum;
using RebuildSharedData.Networking;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public enum TownRoutineState
    {
        Idle,
        ReturningToBase,
        NavigatingToDistributor,
        DonatingToDistributor,
        WaitingForDistributorVend,
        BuyingAtDistributor,
        NavigatingToGeneralVendorSell,
        InteractingWithGeneralVendorSell,
        BuyingAtGeneralVendor,
        NavigatingToRanchVendor,
        BuyingAtRanchVendor,
        NavigatingToAlchemist,
        BuyingAtAlchemist,
        NavigatingToKafra,
        InteractingWithKafra,
        ExecutingBlacksmithUpgrade,
        Completed
    }

    public enum RestockVendorType
    {
        GeneralVendor,
        RanchVendor,
        AlchemistVendor
    }

    public class RestockItemDefinition
    {
        public string CanonicalName { get; set; }
        public int ItemId { get; set; }
        public RestockVendorType Vendor { get; set; }
        public List<int> EquivalentItemIds { get; set; }
    }

    public class TownRoutineController
    {
        public static readonly Vector2Int GeneralVendorPosition = new Vector2Int(151, 347);
        public static readonly Vector2Int RanchVendorPosition = new Vector2Int(150, 350);
        public static readonly Vector2Int AlchemistPosition = new Vector2Int(145, 346);
        public static readonly Vector2Int KafraPosition = new Vector2Int(158, 362);
        public const string BaseMap = "prt_fild08";

        // Normalized canonical dictionary - space and underscore aliases are resolved via TryGetRestockItem
        public static readonly Dictionary<string, RestockItemDefinition> KnownRestockItems = new(StringComparer.OrdinalIgnoreCase)
        {
            // General Vendor (151, 347)
            ["Fly_Wing"] = new RestockItemDefinition { CanonicalName = "Fly_Wing", ItemId = 601, Vendor = RestockVendorType.GeneralVendor, EquivalentItemIds = new List<int> { 601, 12323 } },
            ["Butterfly_Wing"] = new RestockItemDefinition { CanonicalName = "Butterfly_Wing", ItemId = 602, Vendor = RestockVendorType.GeneralVendor, EquivalentItemIds = new List<int> { 602, 12324 } },
            ["Arrow"] = new RestockItemDefinition { CanonicalName = "Arrow", ItemId = 1750, Vendor = RestockVendorType.GeneralVendor },
            ["Silver_Arrow"] = new RestockItemDefinition { CanonicalName = "Silver_Arrow", ItemId = 1751, Vendor = RestockVendorType.GeneralVendor },
            ["Fire_Arrow"] = new RestockItemDefinition { CanonicalName = "Fire_Arrow", ItemId = 1752, Vendor = RestockVendorType.GeneralVendor },
            ["Trap"] = new RestockItemDefinition { CanonicalName = "Trap", ItemId = 1065, Vendor = RestockVendorType.GeneralVendor },

            // Ranch Vendor (150, 350)
            ["Milk"] = new RestockItemDefinition { CanonicalName = "Milk", ItemId = 519, Vendor = RestockVendorType.RanchVendor },
            ["Meat"] = new RestockItemDefinition { CanonicalName = "Meat", ItemId = 517, Vendor = RestockVendorType.RanchVendor },
            ["Apple"] = new RestockItemDefinition { CanonicalName = "Apple", ItemId = 512, Vendor = RestockVendorType.RanchVendor },
            ["Banana"] = new RestockItemDefinition { CanonicalName = "Banana", ItemId = 513, Vendor = RestockVendorType.RanchVendor },
            ["Carrot"] = new RestockItemDefinition { CanonicalName = "Carrot", ItemId = 514, Vendor = RestockVendorType.RanchVendor },
            ["Potato"] = new RestockItemDefinition { CanonicalName = "Potato", ItemId = 516, Vendor = RestockVendorType.RanchVendor },
            ["Pumpkin"] = new RestockItemDefinition { CanonicalName = "Pumpkin", ItemId = 535, Vendor = RestockVendorType.RanchVendor },

            // Diligent Alchemist (145, 346)
            ["Red_Potion"] = new RestockItemDefinition { CanonicalName = "Red_Potion", ItemId = 501, Vendor = RestockVendorType.AlchemistVendor },
            ["Orange_Potion"] = new RestockItemDefinition { CanonicalName = "Orange_Potion", ItemId = 502, Vendor = RestockVendorType.AlchemistVendor },
            ["Yellow_Potion"] = new RestockItemDefinition { CanonicalName = "Yellow_Potion", ItemId = 503, Vendor = RestockVendorType.AlchemistVendor },
            ["White_Potion"] = new RestockItemDefinition { CanonicalName = "White_Potion", ItemId = 504, Vendor = RestockVendorType.AlchemistVendor },
            ["Green_Potion"] = new RestockItemDefinition { CanonicalName = "Green_Potion", ItemId = 506, Vendor = RestockVendorType.AlchemistVendor },
            ["Concentration_Potion"] = new RestockItemDefinition { CanonicalName = "Concentration_Potion", ItemId = 645, Vendor = RestockVendorType.AlchemistVendor },
            ["Awakening_Potion"] = new RestockItemDefinition { CanonicalName = "Awakening_Potion", ItemId = 656, Vendor = RestockVendorType.AlchemistVendor },
            ["Berserk_Potion"] = new RestockItemDefinition { CanonicalName = "Berserk_Potion", ItemId = 657, Vendor = RestockVendorType.AlchemistVendor },
        };

        public static bool TryGetRestockItem(string key, out RestockItemDefinition def)
        {
            def = null;
            if (string.IsNullOrWhiteSpace(key)) return false;
            string normalized = key.Trim().Replace(" ", "_");
            return KnownRestockItems.TryGetValue(normalized, out def);
        }

        private TownRoutineState currentState = TownRoutineState.Idle;
        private float stateStartTime = 0f;
        private float lastActionTime = 0f;
        private int stepPhase = 0;
        private float targetStopDistance = 0f;
        private bool usedWingThisRoutine = false;
        private float lastCompletedTime = 0f;
        private float lastDistributorMissingTime = 0f;
        private readonly NpcInteractionHelper npcHelper = new();
        private readonly BlacksmithHelper blacksmithHelper = new();

        public TownRoutineState CurrentState => currentState;
        public bool IsActive => currentState != TownRoutineState.Idle;
        public float LastCompletedTime => lastCompletedTime;

        public void StartRoutine(string reason = "Overweight/Base routine")
        {
            currentState = TownRoutineState.ReturningToBase;
            stateStartTime = Time.time;
            lastActionTime = 0f;
            stepPhase = 0;
            usedWingThisRoutine = false;
            lastDistributorMissingTime = 0f;
            targetStopDistance = 0f;
            npcHelper.Reset();
            blacksmithHelper.Reset();
            BotEngine.Instance?.LogEvent($"[Town Routine] {reason} triggered. Initiating return to prt_fild08.");
        }

        public void CancelRoutine()
        {
            if (currentState != TownRoutineState.Idle)
            {
                currentState = TownRoutineState.Idle;
                stepPhase = 0;
                usedWingThisRoutine = false;
                npcHelper.Reset();
                blacksmithHelper.Reset();
                CloseOpenShopUI();
                CloseOpenStorageUI();
                BotEngine.Instance?.LogEvent("[Town Routine] Routine cancelled.");
            }
        }

        public static (int keep, int store, int sell) GetItemStackBreakdown(InventoryItem item)
        {
            if (item == null || item.ItemData == null || item.Count <= 0) return (0, 0, 0);

            var state = PlayerState.Instance;
            if (state?.EquippedItems != null)
            {
                for (int i = 0; i < state.EquippedItems.Length; i++)
                {
                    if (state.EquippedItems[i] == item.BagSlotId)
                        return (item.Count, 0, 0); // Equipped gear is always protected
                }
            }

            string name = item.ItemData.Name ?? "";
            string disp = null;
            int? maxLimit = null;

            // 1. Profile rule (highest priority)
            if (!string.IsNullOrEmpty(name) && BotConfigManager.Current.ItemRules.TryGetValue(name, out var profileRule))
            {
                disp = profileRule;
                if (BotConfigManager.Current.ItemRuleLimits.TryGetValue(name, out var lim) && lim > 0)
                {
                    maxLimit = lim;
                }
            }

            // 2. Master item rule (fallback if not in profile rules)
            if (string.IsNullOrEmpty(disp) && MasterItemRulesManager.TryGetRule(name, out var masterRule))
            {
                disp = masterRule.Disposition;
                if (!maxLimit.HasValue && masterRule.MaxCount.HasValue && masterRule.MaxCount.Value > 0)
                {
                    maxLimit = masterRule.MaxCount.Value;
                }
            }

            // 3. Fallback category rules
            if (string.IsNullOrEmpty(disp))
            {
                switch (item.ItemData.ItemClass)
                {
                    case ItemClass.Card:
                    case ItemClass.Weapon:
                    case ItemClass.Equipment:
                        disp = "Store";
                        break;
                    case ItemClass.Etc:
                        disp = "Sell";
                        break;
                    case ItemClass.Useable:
                    case ItemClass.Ammo:
                    default:
                        disp = "Keep";
                        break;
                }
            }

            // Evaluate limits
            if (string.Equals(disp, "Sell", StringComparison.OrdinalIgnoreCase))
            {
                return (0, 0, item.Count);
            }

            if (string.Equals(disp, "Keep", StringComparison.OrdinalIgnoreCase))
            {
                if (maxLimit.HasValue && maxLimit.Value > 0)
                {
                    int totalInBag = InventoryHelper.GetItemCount(item.Id);
                    if (totalInBag > maxLimit.Value)
                    {
                        int excess = totalInBag - maxLimit.Value;
                        int sellFromThisStack = Mathf.Min(excess, item.Count);
                        int keepFromThisStack = item.Count - sellFromThisStack;
                        return (keepFromThisStack, 0, sellFromThisStack);
                    }
                }
                return (item.Count, 0, 0);
            }

            if (string.Equals(disp, "Store", StringComparison.OrdinalIgnoreCase))
            {
                if (maxLimit.HasValue && maxLimit.Value > 0)
                {
                    int storedCount = InventoryHelper.GetStorageItemCount(item.Id);
                    if (storedCount >= maxLimit.Value)
                    {
                        // Storage cap already reached; sell entire stack
                        return (0, 0, item.Count);
                    }
                    int remainingStorageCap = maxLimit.Value - storedCount;
                    int storeFromThisStack = Mathf.Min(remainingStorageCap, item.Count);
                    int sellFromThisStack = item.Count - storeFromThisStack;
                    return (0, storeFromThisStack, sellFromThisStack);
                }
                return (0, item.Count, 0);
            }

            return (item.Count, 0, 0);
        }

        public static string GetItemDisposition(InventoryItem item)
        {
            var (keep, store, sell) = GetItemStackBreakdown(item);
            if (sell > 0 && keep == 0 && store == 0) return "Sell";
            if (store > 0 && keep == 0 && sell == 0) return "Store";
            if (sell > 0) return "Sell";
            if (store > 0) return "Store";
            return "Keep";
        }

        public static bool HasItemsToSell()
        {
            if (!InventoryHelper.TryGetInventoryData(out var inv)) return false;
            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item?.ItemData != null && item.Count > 0)
                {
                    var (_, _, sell) = GetItemStackBreakdown(item);
                    if (sell > 0) return true;
                }
            }
            return false;
        }

        public static bool HasItemsToStore()
        {
            if (!InventoryHelper.TryGetInventoryData(out var inv)) return false;
            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item?.ItemData != null && item.Count > 0)
                {
                    var (_, store, _) = GetItemStackBreakdown(item);
                    if (store > 0) return true;
                }
            }
            return false;
        }

        public static List<(int itemId, int count)> GetItemsToPurchaseFromVendor(RestockVendorType vendor)
        {
            var result = new List<(int itemId, int count)>();
            if (!BotConfigManager.Current.AutoRestock || BotConfigManager.Current.RestockTargets == null)
                return result;

            InventoryHelper.TryGetInventoryData(out var inv);
            var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in BotConfigManager.Current.RestockTargets)
            {
                string key = kvp.Key;
                int targetCount = kvp.Value;
                if (targetCount <= 0) continue;

                if (!TryGetRestockItem(key, out var def))
                    continue;

                if (def.Vendor != vendor)
                    continue;

                if (!processedKeys.Add(def.CanonicalName))
                    continue;

                int currentCount = 0;
                if (inv != null)
                {
                    foreach (var invKvp in inv)
                    {
                        var item = invKvp.Value;
                        if (item.ItemData == null) continue;
                        if (def.EquivalentItemIds != null && def.EquivalentItemIds.Contains(item.Id))
                        {
                            currentCount += item.Count;
                        }
                        else if (item.Id == def.ItemId)
                        {
                            currentCount += item.Count;
                        }
                    }
                }

                int needed = targetCount - currentCount;
                if (needed > 0)
                {
                    result.Add((def.ItemId, needed));
                }
            }

            // Archer automatic arrow fallback if no arrow restock targets are explicitly configured
            if (vendor == RestockVendorType.GeneralVendor)
            {
                var state = PlayerState.Instance;
                if (state != null && ArrowHelper.IsArcherClass(state.JobId))
                {
                    bool hasExplicitArrowTarget = processedKeys.Contains("Arrow") ||
                                                  processedKeys.Contains("Silver_Arrow") ||
                                                  processedKeys.Contains("Fire_Arrow");
                    if (!hasExplicitArrowTarget)
                    {
                        int currentArrows = ArrowHelper.GetTotalArrowCount();
                        int targetArrows = 500;
                        if (currentArrows < targetArrows)
                        {
                            result.Add((1750, targetArrows - currentArrows)); // Buy standard Arrows (ID: 1750)
                        }
                    }
                }
            }

            return result;
        }

        public static bool HasSuppliesNeeded()
        {
            return GetItemsToPurchaseFromVendor(RestockVendorType.GeneralVendor).Count > 0 ||
                   GetItemsToPurchaseFromVendor(RestockVendorType.RanchVendor).Count > 0 ||
                   GetItemsToPurchaseFromVendor(RestockVendorType.AlchemistVendor).Count > 0;
        }

        public const float RoutineCooldownSeconds = 45.0f;

        public static bool IsOverweight()
        {
            var cfg = BotConfigManager.Current;
            if (cfg == null || !cfg.AutoReturnToBaseOnWeight) return false;

            var netManager = NetworkManager.Instance;
            if (netManager != null && IsTownOrBaseMap(netManager.CurrentMap))
                return false;

            var state = PlayerState.Instance;
            if (state != null && state.MaxWeight > 0)
            {
                float weightPercent = (float)state.CurrentWeight / state.MaxWeight * 100f;
                return weightPercent >= cfg.ReturnToBaseWeightPercent;
            }
            return false;
        }

        public static bool HasDepletedEssentialSupplies(out string reason)
        {
            reason = null;
            var cfg = BotConfigManager.Current;
            if (cfg == null || !cfg.AutoRestock || !cfg.AutoRestockOnLowSupplies || cfg.RestockTargets == null || cfg.RestockTargets.Count == 0)
                return false;

            var netManager = NetworkManager.Instance;
            if (netManager != null && IsTownOrBaseMap(netManager.CurrentMap))
                return false;

            var player = CameraFollower.Instance?.TargetControllable;
            if (player != null && ArrowHelper.IsBowUserOutOfAmmo(player))
            {
                reason = "Out of arrows";
                return true;
            }

            // Don't trigger if broke and no loot to sell
            var state = PlayerState.Instance;
            if ((state == null || state.Zeny < 50) && !HasItemsToSell())
                return false;

            // If hunting on base map and out of supplies, don't trigger unless we can afford and need them
            if (netManager != null && string.Equals(netManager.CurrentMap, BaseMap, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(cfg.TargetMap, BaseMap, StringComparison.OrdinalIgnoreCase))
            {
                if (!HasSuppliesNeeded()) return false;
            }

            if (!InventoryHelper.TryGetInventoryData(out var inv) || inv == null)
                return false;

            foreach (var kvp in cfg.RestockTargets)
            {
                string itemName = kvp.Key;
                int targetCount = kvp.Value;
                if (targetCount <= 0) continue;

                // Only evaluate items marked as Essential!
                if (!cfg.IsSupplyEssential(itemName))
                    continue;

                int currentCount = GetCurrentSupplyCount(itemName, inv);

                // For Archer arrows, trigger before arrows completely reach 0 if MinArrowCount configured
                if (IsArrowItem(itemName) && state != null && ArrowHelper.IsArcherClass(state.JobId))
                {
                    int minArrows = cfg.MinArrowCount > 0 ? cfg.MinArrowCount : 30;
                    if (currentCount <= minArrows)
                    {
                        reason = $"Essential arrow '{itemName}' low ({currentCount} <= {minArrows})";
                        return true;
                    }
                }
                else if (currentCount == 0)
                {
                    reason = $"Essential supply '{itemName}' depleted (0/{targetCount})";
                    return true;
                }
            }

            return false;
        }

        public static bool HasDepletedEssentialSupplies()
        {
            return HasDepletedEssentialSupplies(out _);
        }

        public static int GetCurrentSupplyCount(string itemName, Il2CppSystem.Collections.Generic.SortedDictionary<int, InventoryItem> inv)
        {
            if (inv == null || string.IsNullOrWhiteSpace(itemName)) return 0;

            if (TryGetRestockItem(itemName, out var def))
            {
                int total = 0;
                foreach (var kvp in inv)
                {
                    var item = kvp.Value;
                    if (item == null || item.ItemData == null) continue;
                    if (def.EquivalentItemIds != null && def.EquivalentItemIds.Contains(item.Id))
                    {
                        total += item.Count;
                    }
                    else if (item.Id == def.ItemId)
                    {
                        total += item.Count;
                    }
                }
                return total;
            }

            // Fallback match by name
            int count = 0;
            string norm = itemName.Trim().Replace('_', ' ');
            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item?.ItemData == null) continue;
                if (string.Equals(item.ItemData.Name, norm, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.ItemData.Name, itemName, StringComparison.OrdinalIgnoreCase))
                {
                    count += item.Count;
                }
            }
            return count;
        }

        private static bool IsArrowItem(string itemName)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return false;
            return itemName.IndexOf("Arrow", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryGetRestockCount(string key, out int targetCount)
        {
            targetCount = 0;
            if (BotConfigManager.Current.RestockTargets == null) return false;

            if (BotConfigManager.Current.RestockTargets.TryGetValue(key, out targetCount)) return true;
            string spaceKey = key.Replace('_', ' ');
            if (BotConfigManager.Current.RestockTargets.TryGetValue(spaceKey, out targetCount)) return true;
            return false;
        }

        public static int GetAvailableButterflyWingId()
        {
            return InventoryHelper.FindFirstItemId(602, 12324);
        }

        private static readonly HashSet<string> TownMaps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "prt_fild08",
            "prontera",
            "morocc",
            "geffen",
            "payon",
            "alberta",
            "izlude",
            "aldebaran",
            "comodo",
            "yuno",
            "pay_arche"
        };

        public static bool IsTownMap(string mapName)
        {
            if (string.IsNullOrEmpty(mapName)) return false;
            return TownMaps.Contains(mapName);
        }

        public static bool IsTownOrBaseMap(string mapName)
        {
            if (string.IsNullOrEmpty(mapName)) return false;
            return IsTownMap(mapName) || string.Equals(mapName, BaseMap, StringComparison.OrdinalIgnoreCase);
        }

        public bool ProcessTownRoutine(NetworkManager netManager, ServerControllable player, NavigationController navigation, float now)
        {
            if (currentState == TownRoutineState.Idle)
            {
                // Mandatory cooldown after completing a routine to prevent rapid re-trigger loops
                if (now - lastCompletedTime < RoutineCooldownSeconds)
                    return false;

                // Defer starting town routine if currently looting
                if (BotEngine.Instance != null && BotEngine.Instance.Loot != null)
                {
                    if (BotEngine.Instance.Loot.PendingLootItemId != -1 || BotEngine.Instance.Loot.FindNearestGroundItem(player.CellPosition) != null)
                        return false;
                }

                // Check overweight trigger
                if (IsOverweight())
                {
                    StartRoutine("Overweight threshold reached");
                    return true;
                }

                // Check depleted essential supplies trigger (triggers if any essential supply is 0 or low arrows)
                if (BotConfigManager.Current.AutoRestockOnLowSupplies && HasDepletedEssentialSupplies(out string reason))
                {
                    StartRoutine(reason);
                    return true;
                }

                return false;
            }

            bool inBaseMap = string.Equals(netManager.CurrentMap, BaseMap, StringComparison.OrdinalIgnoreCase);

            switch (currentState)
            {
                case TownRoutineState.ReturningToBase:
                    // Check if an active distributor is configured in the fleet
                    if (DistributorController.TryGetActiveDistributor(out var dist))
                    {
                        if (HasItemsToSell() || HasItemsToStore())
                        {
                            currentState = TownRoutineState.NavigatingToDistributor;
                            stepPhase = 0;
                            BotEngine.Instance?.LogEvent($"[Town Routine] Active Distributor found ({dist.CharacterName} at {dist.Map} ({dist.Position.x}, {dist.Position.y})). Routing to Distributor to donate items.");
                            return true;
                        }
                        else if (HasSuppliesNeeded())
                        {
                            currentState = TownRoutineState.WaitingForDistributorVend;
                            stepPhase = 0;
                            BotEngine.Instance?.LogEvent($"[Town Routine] Active Distributor found. Routing to Distributor {dist.CharacterName} to purchase supplies.");
                            return true;
                        }
                    }

                    if (inBaseMap)
                    {
                        // Once in base, decide where to navigate first
                        if (HasItemsToSell())
                        {
                            currentState = TownRoutineState.NavigatingToGeneralVendorSell;
                            stepPhase = 0;
                            targetStopDistance = UnityEngine.Random.Range(BotConstants.MinStopDistance, BotConstants.MaxStopDistance);
                            BotEngine.Instance?.LogEvent($"[Town Routine] Arrived in prt_fild08. Moving to General Vendor to sell items (target distance: {targetStopDistance:F1} tiles).");
                        }
                        else
                        {
                            AdvanceFromGeneralVendor(now);
                        }
                        return true;
                    }

                    // Only use Butterfly Wing if NOT already in a town
                    if (!usedWingThisRoutine)
                    {
                        usedWingThisRoutine = true;
                        if (!IsTownMap(netManager.CurrentMap))
                        {
                            int bwingId = GetAvailableButterflyWingId();
                            if (bwingId > 0)
                            {
                                netManager.SendUseItem(bwingId);
                                lastActionTime = now + 1.2f;
                                BotEngine.Instance?.LogEvent($"[Town Routine] In field '{netManager.CurrentMap}'. Used Butterfly Wing (ID: {bwingId}) to return towards save point.");
                                return true;
                            }
                        }
                        else
                        {
                            BotEngine.Instance?.LogEvent($"[Town Routine] Already in town '{netManager.CurrentMap}'. Skipping Butterfly Wing and traveling to base.");
                        }
                    }

                    // Route to prt_fild08 using navigation.ProcessTravel
                    var travelBotState = BotState.TravelingToTargetMap;
                    navigation.ProcessTravel(netManager, player, now, ref travelBotState);
                    return true;

                case TownRoutineState.NavigatingToDistributor:
                    return ProcessNavigatingToDistributor(player, navigation, now);

                case TownRoutineState.DonatingToDistributor:
                    return ProcessDonatingToDistributor(netManager, player, now);

                case TownRoutineState.WaitingForDistributorVend:
                    return ProcessWaitingForDistributorVend(netManager, player, navigation, now);

                case TownRoutineState.BuyingAtDistributor:
                    return ProcessBuyingAtDistributor(netManager, now);

                case TownRoutineState.NavigatingToGeneralVendorSell:
                    if (!inBaseMap)
                    {
                        currentState = TownRoutineState.ReturningToBase;
                        return true;
                    }
                    return ProcessNavigationToTarget(player, navigation, GeneralVendorPosition, TownRoutineState.InteractingWithGeneralVendorSell, "Vendor", now);

                case TownRoutineState.InteractingWithGeneralVendorSell:
                    return ProcessVendorSell(netManager, now);

                case TownRoutineState.BuyingAtGeneralVendor:
                    return ProcessVendorBuy(netManager, RestockVendorType.GeneralVendor, "Vendor", GeneralVendorPosition, () => AdvanceFromRanchVendor(now), now);

                case TownRoutineState.NavigatingToRanchVendor:
                    return ProcessNavigationToTarget(player, navigation, RanchVendorPosition, TownRoutineState.BuyingAtRanchVendor, "Ranch", now);

                case TownRoutineState.BuyingAtRanchVendor:
                    return ProcessVendorBuy(netManager, RestockVendorType.RanchVendor, "Ranch", RanchVendorPosition, () => AdvanceFromAlchemist(now), now);

                case TownRoutineState.NavigatingToAlchemist:
                    return ProcessNavigationToTarget(player, navigation, AlchemistPosition, TownRoutineState.BuyingAtAlchemist, "Alchemist", now);

                case TownRoutineState.BuyingAtAlchemist:
                    return ProcessVendorBuy(netManager, RestockVendorType.AlchemistVendor, "Alchemist", AlchemistPosition, () =>
                    {
                        if (HasItemsToStore() || EquipmentTargetController.HasPendingBlacksmithTrip(out _, out _, out _))
                        {
                            currentState = TownRoutineState.NavigatingToKafra;
                            stepPhase = 0;
                            lastActionTime = now + 0.4f;
                        }
                        else
                        {
                            currentState = TownRoutineState.Completed;
                            stepPhase = 0;
                            lastActionTime = now;
                        }
                    }, now);

                case TownRoutineState.NavigatingToKafra:
                    return ProcessNavigationToTarget(player, navigation, KafraPosition, TownRoutineState.InteractingWithKafra, "Kafra", now);

                case TownRoutineState.InteractingWithKafra:
                    return ProcessKafraStorage(netManager, now);

                case TownRoutineState.ExecutingBlacksmithUpgrade:
                    if (blacksmithHelper.Process(BotEngine.Instance, now))
                    {
                        return true;
                    }
                    BotEngine.Instance?.LogEvent($"[Town Routine] Blacksmith upgrade completed: {blacksmithHelper.StatusMessage}. Finishing town routine.");
                    currentState = TownRoutineState.Completed;
                    stepPhase = 0;
                    lastActionTime = now;
                    return true;

                case TownRoutineState.Completed:
                    NpcInteractionHelper.CleanupNpcUi();
                    lastCompletedTime = now;
                    BotEngine.Instance?.LogEvent("[Town Routine] Base operations completed successfully. Resuming hunting.");
                    currentState = TownRoutineState.Idle;
                    navigation?.ResetWander();

                    if (player != null && ArrowHelper.IsBowUser(player))
                    {
                        ArrowHelper.EquipAnyAvailableArrow(netManager);
                    }
                    return false;
            }

            return false;
        }

        private bool ProcessNavigationToTarget(ServerControllable player, NavigationController navigation, Vector2Int targetPos, TownRoutineState nextState, string targetName, float now)
        {
            var netManager = NetworkManager.Instance;

            // Opportunistic visual range interaction: If on base town map and NPC is visible in entity list, interact immediately!
            if (netManager != null && string.Equals(netManager.CurrentMap, BaseMap, StringComparison.OrdinalIgnoreCase))
            {
                var npc = NpcInteractionHelper.FindNearbyNpc(netManager, targetName, targetPos, player.CellPosition);
                if (npc != null)
                {
                    float visualDist = Vector2.Distance(player.CellPosition, npc.CellPosition);
                    currentState = nextState;
                    stepPhase = 0;
                    lastActionTime = now;
                    BotEngine.Instance?.LogEvent($"[Town Routine] Spotted {targetName} from {visualDist:F1} tiles away. Opening interaction.");
                    return true;
                }
            }

            if (!MapNavMesh.Instance.IsReachable(player.CellPosition, targetPos))
            {
                var travelState = BotState.TravelingToTargetMap;
                navigation.ProcessTravel(NetworkManager.Instance, player, now, ref travelState, targetPos);
                return true;
            }

            if (!player.IsMoving && now - lastActionTime >= 0.3f)
            {
                navigation.NavigateTowards(player.CellPosition, targetPos, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                lastActionTime = now;
            }
            return true;
        }

        private bool ProcessVendorSell(NetworkManager netManager, float now)
        {
            if (stepPhase == 0)
            {
                NpcInteractionHelper.CleanupNpcUi();
                var vendorNpc = NpcInteractionHelper.FindNearbyNpc(netManager, "Vendor", GeneralVendorPosition);
                if (vendorNpc != null)
                {
                    netManager.SendNpcClick(vendorNpc.Id);
                    stepPhase = 1;
                    lastActionTime = now;
                }
                else if (now - lastActionTime > 3.0f)
                {
                    AdvanceFromGeneralVendor(now);
                }
            }
            else if (stepPhase == 1)
            {
                var cam = CameraFollower.Instance;
                bool dialogOpen = cam != null && cam.DialogPanel != null && cam.DialogPanel.activeSelf;
                bool optionOpen = cam != null && cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;

                if (optionOpen)
                {
                    if (now - lastActionTime < 0.4f) return true;

                    var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(false);
                    NpcOptionButton sellBtn = null;
                    if (buttons != null)
                    {
                        foreach (var btn in buttons)
                        {
                            if (btn == null) continue;
                            string text = btn.TextBox != null ? btn.TextBox.text : "";
                            if (text.IndexOf("Sell", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                sellBtn = btn;
                                break;
                            }
                        }
                    }
                    if (sellBtn != null)
                    {
                        sellBtn.OnClick();
                    }
                    else
                    {
                        cam.NpcOptionPanel.SetActive(false);
                        netManager.SendNpcSelectOption(1); // Option 1 is "Sell"
                    }
                    stepPhase = 2;
                    lastActionTime = now;
                }
                else if (dialogOpen)
                {
                    if (now - lastActionTime >= 0.5f)
                    {
                        netManager.SendNpcAdvance();
                        lastActionTime = now;
                    }
                }
                else if (now - lastActionTime >= 3.0f)
                {
                    stepPhase = 0;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 2)
            {
                if (ShopUI.Instance != null || now - lastActionTime >= 0.6f)
                {
                    if (InventoryHelper.TryGetInventoryData(out var inv))
                    {
                        var itemsToSell = new List<(int bagId, int count)>();
                        foreach (var kvp in inv)
                        {
                            var item = kvp.Value;
                            if (item.ItemData != null && item.Count > 0 && GetItemDisposition(item) == "Sell")
                            {
                                itemsToSell.Add((item.BagSlotId, item.Count));
                            }
                        }

                        if (itemsToSell.Count > 0)
                        {
                            var msg = netManager.StartMessage(PacketType.ShopBuySell);
                            msg.Write(itemsToSell.Count);
                            foreach (var (bagId, count) in itemsToSell)
                            {
                                msg.Write(bagId);
                                msg.Write(count);
                            }
                            netManager.SendMessage(msg);
                            BotEngine.Instance?.LogEvent($"[Town Routine] Submitting {itemsToSell.Count} item stack(s) to General Vendor for sale.");
                        }
                        else
                        {
                            netManager.SubmitShopPurchase(null);
                            BotEngine.Instance?.LogEvent("[Town Routine] No items marked for sale.");
                        }
                    }

                    stepPhase = 3;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 3 && now - lastActionTime >= 0.8f)
            {
                CloseOpenShopUI();
                NpcInteractionHelper.CleanupNpcUi();
                BotEngine.Instance?.LogEvent("[Town Routine] Sale completed.");
                AdvanceFromGeneralVendor(now);
            }
            return true;
        }

        private bool ProcessVendorBuy(NetworkManager netManager, RestockVendorType vendorType, string npcNameSubstr, Vector2Int npcPos, Action onFinished, float now)
        {
            if (stepPhase == 0)
            {
                NpcInteractionHelper.CleanupNpcUi();
                var npc = NpcInteractionHelper.FindNearbyNpc(netManager, npcNameSubstr, npcPos);
                if (npc != null)
                {
                    netManager.SendNpcClick(npc.Id);
                    stepPhase = 1;
                    lastActionTime = now;
                }
                else if (now - lastActionTime > 3.0f)
                {
                    onFinished?.Invoke();
                }
            }
            else if (stepPhase == 1)
            {
                var cam = CameraFollower.Instance;
                bool dialogOpen = cam != null && cam.DialogPanel != null && cam.DialogPanel.activeSelf;
                bool optionOpen = cam != null && cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;

                if (optionOpen)
                {
                    if (now - lastActionTime < 0.4f) return true;

                    var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(false);
                    NpcOptionButton buyBtn = null;
                    if (buttons != null)
                    {
                        foreach (var btn in buttons)
                        {
                            if (btn == null) continue;
                            string text = btn.TextBox != null ? btn.TextBox.text : "";
                            if (text.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                buyBtn = btn;
                                break;
                            }
                        }
                    }
                    if (buyBtn != null)
                    {
                        buyBtn.OnClick();
                    }
                    else
                    {
                        cam.NpcOptionPanel.SetActive(false);
                        netManager.SendNpcSelectOption(0); // Option 0 is "Buy"
                    }
                    stepPhase = 2;
                    lastActionTime = now;
                }
                else if (dialogOpen)
                {
                    if (now - lastActionTime >= 0.5f)
                    {
                        netManager.SendNpcAdvance();
                        lastActionTime = now;
                    }
                }
                else if (now - lastActionTime >= 3.0f)
                {
                    stepPhase = 0;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 2)
            {
                if (ShopUI.Instance != null || now - lastActionTime >= 0.6f)
                {
                    var buys = GetItemsToPurchaseFromVendor(vendorType);
                    if (buys.Count > 0)
                    {
                        var msg = netManager.StartMessage(PacketType.ShopBuySell);
                        msg.Write(buys.Count);
                        foreach (var (itemId, count) in buys)
                        {
                            msg.Write(itemId);
                            msg.Write(count);
                        }
                        netManager.SendMessage(msg);
                        BotEngine.Instance?.LogEvent($"[Town Routine] Purchased {buys.Count} item type(s) from {vendorType}.");
                    }
                    else
                    {
                        netManager.SubmitShopPurchase(null);
                    }

                    stepPhase = 3;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 3 && now - lastActionTime >= 0.8f)
            {
                CloseOpenShopUI();
                NpcInteractionHelper.CleanupNpcUi();
                onFinished?.Invoke();
            }
            return true;
        }

        private bool ProcessKafraStorage(NetworkManager netManager, float now)
        {
            var cam = CameraFollower.Instance;
            bool dialogOpen = cam != null && cam.DialogPanel != null && cam.DialogPanel.activeSelf;
            bool optionOpen = cam != null && cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;

            if (stepPhase == 0)
            {
                NpcInteractionHelper.CleanupNpcUi();
                var kafraNpc = NpcInteractionHelper.FindNearbyNpc(netManager, "Kafra", KafraPosition);
                if (kafraNpc != null)
                {
                    netManager.SendNpcClick(kafraNpc.Id);
                    stepPhase = 1;
                    lastActionTime = now + UnityEngine.Random.Range(0.6f, 0.9f);
                }
                else if (now - lastActionTime > 3.0f)
                {
                    currentState = TownRoutineState.Completed;
                    lastCompletedTime = now;
                }
            }
            else if (stepPhase == 1)
            {
                if (optionOpen)
                {
                    if (now - lastActionTime < 0.6f) return true;

                    var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(false);
                    if (buttons != null && buttons.Length > 0)
                    {
                        foreach (var btn in buttons)
                        {
                            if (btn == null) continue;
                            string text = btn.TextBox != null ? btn.TextBox.text : "";
                            if (text.IndexOf("Storage", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                btn.OnClick();
                                stepPhase = 2;
                                lastActionTime = now + UnityEngine.Random.Range(0.8f, 1.2f);
                                BotEngine.Instance?.LogEvent($"[Town Routine] Selected 'Use Storage' (ID: {btn.Id}).");
                                return true;
                            }
                        }
                    }
                }
                else if (dialogOpen)
                {
                    if (now - lastActionTime >= 0.75f)
                    {
                        netManager.SendNpcAdvance();
                        lastActionTime = now;
                        BotEngine.Instance?.LogEvent("[Town Routine] Advanced Kafra welcome dialog.");
                    }
                    return true;
                }
                else if (now - lastActionTime >= 3.5f)
                {
                    stepPhase = 0;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 2 && now - lastActionTime >= 0f)
            {
                if (InventoryHelper.TryGetInventoryData(out var inv))
                {
                    int storedCount = 0;
                    foreach (var kvp in inv)
                    {
                        var item = kvp.Value;
                        if (item.ItemData != null && item.Count > 0 && GetItemDisposition(item) == "Store")
                        {
                            netManager.SendMoveStorageItem(item.BagSlotId, item.Count, true);
                            storedCount++;
                        }
                    }
                    BotEngine.Instance?.LogEvent($"[Town Routine] Deposited {storedCount} item stack(s) into Kafra Storage.");

                    // Check and withdraw ores or gear needed for equipment targets
                    EquipmentTargetController.ProcessKafraOreWithdrawal(netManager);
                }

                stepPhase = 3;
                lastActionTime = now + UnityEngine.Random.Range(0.7f, 1.0f);
            }
            else if (stepPhase == 3 && now - lastActionTime >= 0f)
            {
                netManager.SendEndStorage();
                CloseOpenStorageUI();
                stepPhase = 4;
                lastActionTime = now + 2.0f;
                BotEngine.Instance?.LogEvent("[Town Routine] Closed Kafra Storage. Waiting for server sync...");
            }
            else if (stepPhase == 4 && now - lastActionTime >= 0f)
            {
                CloseOpenStorageUI();

                // Check if an equipment refine trip to prt_in should be executed before returning to hunt
                if (EquipmentTargetController.HasPendingBlacksmithTrip(out string itemName, out int targetRefine, out int maxAttempts))
                {
                    if (blacksmithHelper.Begin(itemName, targetRefine, safeLimitOnly: false, out string err, maxAttemptsCount: maxAttempts))
                    {
                        currentState = TownRoutineState.ExecutingBlacksmithUpgrade;
                        stepPhase = 0;
                        lastActionTime = now;
                        BotEngine.Instance?.LogEvent($"[Town Routine] Transitioning to prt_in Blacksmith for upgrade on '{itemName}' (+{targetRefine}, {maxAttempts} attempts).");
                        return true;
                    }
                }

                currentState = TownRoutineState.Completed;
                lastCompletedTime = now;
                stepPhase = 0;
                lastActionTime = now;
            }
            return true;
        }

        private void AdvanceFromGeneralVendor(float now)
        {
            var generalBuys = GetItemsToPurchaseFromVendor(RestockVendorType.GeneralVendor);
            if (generalBuys.Count > 0)
            {
                currentState = TownRoutineState.BuyingAtGeneralVendor;
                stepPhase = 0;
                lastActionTime = now + 0.4f;
                BotEngine.Instance?.LogEvent("[Town Routine] Preparing to buy supplies from General Vendor.");
                return;
            }

            AdvanceFromRanchVendor(now);
        }

        private void AdvanceFromRanchVendor(float now)
        {
            var ranchBuys = GetItemsToPurchaseFromVendor(RestockVendorType.RanchVendor);
            if (ranchBuys.Count > 0)
            {
                currentState = TownRoutineState.NavigatingToRanchVendor;
                stepPhase = 0;
                targetStopDistance = UnityEngine.Random.Range(BotConstants.MinStopDistance, BotConstants.MaxStopDistance);
                lastActionTime = now + 0.4f;
                BotEngine.Instance?.LogEvent($"[Town Routine] Moving to Ranch Vendor to buy supplies (target distance: {targetStopDistance:F1} tiles).");
                return;
            }

            AdvanceFromAlchemist(now);
        }

        private void AdvanceFromAlchemist(float now)
        {
            var alchemistBuys = GetItemsToPurchaseFromVendor(RestockVendorType.AlchemistVendor);
            if (alchemistBuys.Count > 0)
            {
                currentState = TownRoutineState.NavigatingToAlchemist;
                stepPhase = 0;
                targetStopDistance = UnityEngine.Random.Range(BotConstants.MinStopDistance, BotConstants.MaxStopDistance);
                lastActionTime = now + 0.4f;
                BotEngine.Instance?.LogEvent($"[Town Routine] Moving to Diligent Alchemist to buy supplies (target distance: {targetStopDistance:F1} tiles).");
                return;
            }

            if (HasItemsToStore())
            {
                currentState = TownRoutineState.NavigatingToKafra;
                stepPhase = 0;
                targetStopDistance = UnityEngine.Random.Range(BotConstants.MinStopDistance, BotConstants.MaxStopDistance);
                lastActionTime = now + 0.4f;
                BotEngine.Instance?.LogEvent($"[Town Routine] Moving to Kafra Staff to deposit items (target distance: {targetStopDistance:F1} tiles).");
                return;
            }

            currentState = TownRoutineState.Completed;
            stepPhase = 0;
            lastActionTime = now;
        }

        public static void CloseOpenShopUI()
        {
            try
            {
                if (ShopUI.Instance != null)
                {
                    var shop = ShopUI.Instance;
                    if (UiManager.Instance != null)
                    {
                        UiManager.Instance.ForceHideTooltip();
                        if (UiManager.Instance.ItemDescriptionWindow != null)
                            UiManager.Instance.ItemDescriptionWindow.HideWindow();
                    }
                    if (shop.RightWindow != null)
                        UnityEngine.Object.Destroy(shop.RightWindow.gameObject);
                    if (shop.LeftWindow != null)
                        UnityEngine.Object.Destroy(shop.LeftWindow.gameObject);
                    UnityEngine.Object.Destroy(shop.gameObject);
                    ShopUI.Instance = null;
                }
            }
            catch (Exception ex)
            {
                BotEngine.Instance?.LogEvent($"[Town Routine] Note closing shop UI: {ex.Message}");
            }
        }

        private bool ProcessNavigatingToDistributor(ServerControllable player, NavigationController navigation, float now)
        {
            if (!DistributorController.TryGetActiveDistributor(out var dist))
            {
                currentState = TownRoutineState.NavigatingToGeneralVendorSell;
                stepPhase = 0;
                lastActionTime = now;
                return true;
            }

            var netManager = NetworkManager.Instance;
            if (netManager == null) return false;

            if (!string.Equals(netManager.CurrentMap, dist.Map, StringComparison.OrdinalIgnoreCase))
            {
                var tState = BotState.TravelingToTargetMap;
                navigation.ProcessTravel(netManager, player, now, ref tState, targetCellPos: dist.Position, destinationMapOverride: dist.Map);
                return true;
            }

            float distToDist = Vector2.Distance(player.CellPosition, dist.Position);
            if (distToDist > 2.5f)
            {
                if (!player.IsMoving && now - lastActionTime >= 0.25f)
                {
                    navigation.NavigateTowards(player.CellPosition, dist.Position, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                    lastActionTime = now;
                }
                return true;
            }

            currentState = TownRoutineState.DonatingToDistributor;
            stepPhase = 0;
            lastActionTime = now;
            BotEngine.Instance?.LogEvent($"[Town Routine] Reached Distributor post for {dist.CharacterName} at {dist.Position}. Awaiting readiness for donation.");
            return true;
        }

        private bool ProcessDonatingToDistributor(NetworkManager netManager, ServerControllable player, float now)
        {
            if (BotEngine.Instance != null)
            {
                BotEngine.Instance.CurrentState = BotState.DonatingToDistributor;
            }

            if (!DistributorController.TryGetActiveDistributor(out var dist))
            {
                currentState = TownRoutineState.NavigatingToGeneralVendorSell;
                stepPhase = 0;
                lastActionTime = now;
                return true;
            }

            // Check if distributor character is physically present nearby
            bool isDistributorNearby = false;
            if (netManager.EntityList != null && player != null)
            {
                foreach (var kvp in netManager.EntityList)
                {
                    var ent = kvp.Value;
                    if (ent == null || ent.Id == netManager.PlayerId) continue;
                    if (ent.CharacterType != CharacterType.Player) continue;

                    if (string.Equals(ent.Name, dist.CharacterName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(ent.Name, dist.ProfileName, StringComparison.OrdinalIgnoreCase) ||
                        (ent.CellPosition == dist.Position && Vector2.Distance(player.CellPosition, ent.CellPosition) <= 4.0f))
                    {
                        if (Vector2.Distance(player.CellPosition, ent.CellPosition) <= 4.0f)
                        {
                            isDistributorNearby = true;
                            break;
                        }
                    }
                }
            }

            // SAFEGUARD: Do NOT drop items if distributor is not nearby, or is currently vending, or not ready for donations!
            if (!isDistributorNearby || !dist.IsReadyForDonations || dist.IsVendingOpen)
            {
                if (now - lastActionTime >= 3.0f)
                {
                    string reason = !isDistributorNearby
                        ? $"Waiting for distributor '{dist.CharacterName}' to physically arrive at post..."
                        : (dist.IsVendingOpen
                            ? $"Distributor '{dist.CharacterName}' is vending. Waiting for shop to close and open pickup..."
                            : $"Distributor '{dist.CharacterName}' is present but not yet ready for donations...");

                    BotEngine.Instance?.LogEvent($"[Town Routine] {reason}");
                    lastActionTime = now;
                }
                return true; // Wait indefinitely near post without dropping anything
            }

            if (now - lastActionTime < 0.25f) return true;

            if (InventoryHelper.TryGetInventoryData(out var inv))
            {
                foreach (var kvp in inv)
                {
                    var item = kvp.Value;
                    if (item?.ItemData == null || item.Count <= 0) continue;

                    var (_, store, sell) = GetItemStackBreakdown(item);
                    int dropCount = store + sell;
                    if (dropCount > 0)
                    {
                        netManager.SendDropItem(item.BagSlotId, dropCount);
                        lastActionTime = now;
                        BotEngine.Instance?.LogEvent($"[Town Routine] Donated {dropCount}x {item.ItemData.Name} to Distributor.");
                        return true;
                    }
                }
            }

            BotEngine.Instance?.LogEvent("[Town Routine] Item donation complete.");
            if (HasSuppliesNeeded())
            {
                currentState = TownRoutineState.WaitingForDistributorVend;
                stepPhase = 0;
                lastActionTime = now;
            }
            else
            {
                currentState = TownRoutineState.Completed;
                stepPhase = 0;
                lastActionTime = now;
            }
            return true;
        }

        private bool ProcessWaitingForDistributorVend(NetworkManager netManager, ServerControllable player, NavigationController navigation, float now)
        {
            if (BotEngine.Instance != null)
            {
                BotEngine.Instance.CurrentState = BotState.BuyingFromDistributor;
            }

            if (!DistributorController.TryGetActiveDistributor(out var dist))
            {
                if (lastDistributorMissingTime == 0f)
                {
                    lastDistributorMissingTime = now;
                }

                if (now - lastDistributorMissingTime > 4.0f)
                {
                    BotEngine.Instance?.LogEvent("[Town Routine] Distributor no longer active. Falling back to General Vendor.");
                    lastDistributorMissingTime = 0f;
                    currentState = TownRoutineState.BuyingAtGeneralVendor;
                    stepPhase = 0;
                    lastActionTime = now;
                    return true;
                }
                return true;
            }
            lastDistributorMissingTime = 0f;

            if (!string.Equals(netManager.CurrentMap, dist.Map, StringComparison.OrdinalIgnoreCase))
            {
                var tState = BotState.TravelingToTargetMap;
                navigation.ProcessTravel(netManager, player, now, ref tState, targetCellPos: dist.Position, destinationMapOverride: dist.Map);
                return true;
            }

            if (Vector2.Distance(player.CellPosition, dist.Position) > 2.0f)
            {
                if (!player.IsMoving && now - lastActionTime >= 0.25f)
                {
                    navigation.NavigateTowards(player.CellPosition, dist.Position, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                    lastActionTime = now;
                }
                return true;
            }

            if (dist.IsVendingOpen)
            {
                ServerControllable vendProxy = null;
                if (netManager.EntityList != null)
                {
                    foreach (var kvp in netManager.EntityList)
                    {
                        var ent = kvp.Value;
                        if (ent != null && ent.CharacterType == CharacterType.NPC && ent.CellPosition == dist.Position)
                        {
                            vendProxy = ent;
                            break;
                        }
                    }
                }

                if (vendProxy != null)
                {
                    netManager.VendingOpenStore(vendProxy.Id);
                    currentState = TownRoutineState.BuyingAtDistributor;
                    stepPhase = 0;
                    lastActionTime = now;
                    BotEngine.Instance?.LogEvent($"[Town Routine] Opening Distributor shop '{dist.ShopTitle}' (ID: {vendProxy.Id}).");
                    return true;
                }
            }

            if (now - lastActionTime >= 3.0f)
            {
                BotEngine.Instance?.LogEvent("[Town Routine] Waiting for Distributor to open Vending shop...");
                lastActionTime = now;
            }
            return true;
        }

        private bool ProcessBuyingAtDistributor(NetworkManager netManager, float now)
        {
            if (BotEngine.Instance != null)
            {
                BotEngine.Instance.CurrentState = BotState.BuyingFromDistributor;
            }

            if (stepPhase == 0)
            {
                var tradeWin = VendingShopViewUI.ActiveTradeWindow;
                if (tradeWin != null)
                {
                    var needed = GetItemsToPurchaseFromVendor(RestockVendorType.GeneralVendor);
                    needed.AddRange(GetItemsToPurchaseFromVendor(RestockVendorType.RanchVendor));
                    needed.AddRange(GetItemsToPurchaseFromVendor(RestockVendorType.AlchemistVendor));

                    var entries = tradeWin.leftEntries;
                    var purchases = new List<(int bagId, int count)>();

                    if (entries != null)
                    {
                        foreach (var (reqItemId, reqCount) in needed)
                        {
                            foreach (var kvp in entries)
                            {
                                var entry = kvp.Value;
                                if (entry != null && entry.ItemId == reqItemId)
                                {
                                    int buyAmount = Mathf.Min(reqCount, entry.ItemCount);
                                    if (buyAmount > 0)
                                    {
                                        purchases.Add((kvp.Key, buyAmount));
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    if (purchases.Count > 0)
                    {
                        foreach (var (bagId, count) in purchases)
                        {
                            tradeWin.FinalizeDropItemOntoRightSide(bagId, count);
                        }

                        tradeWin.SubmitPurchase();
                        BotEngine.Instance?.LogEvent($"[Town Routine] Submitted purchase for {purchases.Count} consumable stack(s) from Distributor at 1z.");
                        stepPhase = 1;
                        lastActionTime = now;
                        return true;
                    }
                    else
                    {
                        // Distributor does not have the requested items in stock
                        if (now - lastActionTime >= 1.0f)
                        {
                            BotEngine.Instance?.LogEvent("[Town Routine] Distributor has no requested supplies in stock. Awaiting restock at post...");
                            tradeWin.CloseShop();
                            currentState = TownRoutineState.WaitingForDistributorVend;
                            stepPhase = 0;
                            lastActionTime = now;
                            return true;
                        }
                    }
                }
                else if (now - lastActionTime >= 5.0f)
                {
                    currentState = TownRoutineState.WaitingForDistributorVend;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 1)
            {
                // Wait up to 2.0 seconds for inventory to reflect the restock, or until supplies are fulfilled
                bool suppliesStillNeeded = HasSuppliesNeeded();
                if (!suppliesStillNeeded)
                {
                    BotEngine.Instance?.LogEvent("[Town Routine] Distributor purchase confirmed in inventory. Returning to field.");
                    currentState = TownRoutineState.Completed;
                    stepPhase = 0;
                    lastActionTime = now;
                    return true;
                }

                if (now - lastActionTime >= 2.0f)
                {
                    BotEngine.Instance?.LogEvent("[Town Routine] Supplies still needed after purchase. Awaiting next distributor restock...");
                    currentState = TownRoutineState.WaitingForDistributorVend;
                    stepPhase = 0;
                    lastActionTime = now;
                    return true;
                }

                return true;
            }

            return true;
        }

        public static void CloseOpenStorageUI()
        {
            try
            {
                if (StorageUI.Instance != null)
                {
                    UnityEngine.Object.Destroy(StorageUI.Instance.gameObject);
                    StorageUI.Instance = null;
                }
                NpcInteractionHelper.CleanupNpcUi();
            }
            catch (Exception ex)
            {
                BotEngine.Instance?.LogEvent($"[Town Routine] Note closing storage UI: {ex.Message}");
            }
        }
    }
}
