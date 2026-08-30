using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI;
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
        NavigatingToGeneralVendorSell,
        InteractingWithGeneralVendorSell,
        BuyingAtGeneralVendor,
        NavigatingToRanchVendor,
        BuyingAtRanchVendor,
        NavigatingToAlchemist,
        BuyingAtAlchemist,
        NavigatingToKafra,
        InteractingWithKafra,
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

        public static readonly Dictionary<string, RestockItemDefinition> KnownRestockItems = new(StringComparer.OrdinalIgnoreCase)
        {
            // General Vendor (151, 347)
            ["Fly_Wing"] = new RestockItemDefinition { CanonicalName = "Fly_Wing", ItemId = 601, Vendor = RestockVendorType.GeneralVendor, EquivalentItemIds = new List<int> { 601, 12323 } },
            ["Fly Wing"] = new RestockItemDefinition { CanonicalName = "Fly_Wing", ItemId = 601, Vendor = RestockVendorType.GeneralVendor, EquivalentItemIds = new List<int> { 601, 12323 } },
            ["Butterfly_Wing"] = new RestockItemDefinition { CanonicalName = "Butterfly_Wing", ItemId = 602, Vendor = RestockVendorType.GeneralVendor, EquivalentItemIds = new List<int> { 602, 12324 } },
            ["Butterfly Wing"] = new RestockItemDefinition { CanonicalName = "Butterfly_Wing", ItemId = 602, Vendor = RestockVendorType.GeneralVendor, EquivalentItemIds = new List<int> { 602, 12324 } },
            ["Arrow"] = new RestockItemDefinition { CanonicalName = "Arrow", ItemId = 1750, Vendor = RestockVendorType.GeneralVendor },
            ["Silver_Arrow"] = new RestockItemDefinition { CanonicalName = "Silver_Arrow", ItemId = 1751, Vendor = RestockVendorType.GeneralVendor },
            ["Silver Arrow"] = new RestockItemDefinition { CanonicalName = "Silver_Arrow", ItemId = 1751, Vendor = RestockVendorType.GeneralVendor },
            ["Fire_Arrow"] = new RestockItemDefinition { CanonicalName = "Fire_Arrow", ItemId = 1752, Vendor = RestockVendorType.GeneralVendor },
            ["Fire Arrow"] = new RestockItemDefinition { CanonicalName = "Fire_Arrow", ItemId = 1752, Vendor = RestockVendorType.GeneralVendor },
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
            ["Red Potion"] = new RestockItemDefinition { CanonicalName = "Red_Potion", ItemId = 501, Vendor = RestockVendorType.AlchemistVendor },
            ["Orange_Potion"] = new RestockItemDefinition { CanonicalName = "Orange_Potion", ItemId = 502, Vendor = RestockVendorType.AlchemistVendor },
            ["Orange Potion"] = new RestockItemDefinition { CanonicalName = "Orange_Potion", ItemId = 502, Vendor = RestockVendorType.AlchemistVendor },
            ["Yellow_Potion"] = new RestockItemDefinition { CanonicalName = "Yellow_Potion", ItemId = 503, Vendor = RestockVendorType.AlchemistVendor },
            ["Yellow Potion"] = new RestockItemDefinition { CanonicalName = "Yellow_Potion", ItemId = 503, Vendor = RestockVendorType.AlchemistVendor },
            ["White_Potion"] = new RestockItemDefinition { CanonicalName = "White_Potion", ItemId = 504, Vendor = RestockVendorType.AlchemistVendor },
            ["White Potion"] = new RestockItemDefinition { CanonicalName = "White_Potion", ItemId = 504, Vendor = RestockVendorType.AlchemistVendor },
            ["Green_Potion"] = new RestockItemDefinition { CanonicalName = "Green_Potion", ItemId = 506, Vendor = RestockVendorType.AlchemistVendor },
            ["Green Potion"] = new RestockItemDefinition { CanonicalName = "Green_Potion", ItemId = 506, Vendor = RestockVendorType.AlchemistVendor },
            ["Concentration_Potion"] = new RestockItemDefinition { CanonicalName = "Concentration_Potion", ItemId = 645, Vendor = RestockVendorType.AlchemistVendor },
            ["Concentration Potion"] = new RestockItemDefinition { CanonicalName = "Concentration_Potion", ItemId = 645, Vendor = RestockVendorType.AlchemistVendor },
            ["Awakening_Potion"] = new RestockItemDefinition { CanonicalName = "Awakening_Potion", ItemId = 656, Vendor = RestockVendorType.AlchemistVendor },
            ["Awakening Potion"] = new RestockItemDefinition { CanonicalName = "Awakening_Potion", ItemId = 656, Vendor = RestockVendorType.AlchemistVendor },
            ["Berserk_Potion"] = new RestockItemDefinition { CanonicalName = "Berserk_Potion", ItemId = 657, Vendor = RestockVendorType.AlchemistVendor },
            ["Berserk Potion"] = new RestockItemDefinition { CanonicalName = "Berserk_Potion", ItemId = 657, Vendor = RestockVendorType.AlchemistVendor },
        };

        private TownRoutineState currentState = TownRoutineState.Idle;
        private float stateStartTime = 0f;
        private float lastActionTime = 0f;
        private int stepPhase = 0;
        private float targetStopDistance = 0f;
        private bool usedWingThisRoutine = false;
        private float lastCompletedTime = 0f;

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
            targetStopDistance = 0f;
            BotEngine.Instance?.LogEvent($"[Town Routine] {reason} triggered. Initiating return to prt_fild08.");
        }

        public void CancelRoutine()
        {
            if (currentState != TownRoutineState.Idle)
            {
                currentState = TownRoutineState.Idle;
                stepPhase = 0;
                usedWingThisRoutine = false;
                CloseOpenShopUI();
                CloseOpenStorageUI();
                BotEngine.Instance?.LogEvent("[Town Routine] Routine cancelled.");
            }
        }

        public static string GetItemDisposition(InventoryItem item)
        {
            if (item == null || item.ItemData == null) return "Keep";

            var state = PlayerState.Instance;
            if (state?.EquippedItems != null)
            {
                for (int i = 0; i < state.EquippedItems.Length; i++)
                {
                    if (state.EquippedItems[i] == item.BagSlotId)
                        return "Keep"; // Equipped gear is always protected
                }
            }

            string name = item.ItemData.Name;
            if (!string.IsNullOrEmpty(name) && BotConfigManager.Current.ItemRules.TryGetValue(name, out var rule))
            {
                return rule;
            }

            // Fallback category rules
            switch (item.ItemData.ItemClass)
            {
                case ItemClass.Card:
                case ItemClass.Weapon:
                case ItemClass.Equipment:
                    return "Store"; // Equipment and cards default to Store per design
                case ItemClass.Etc:
                    return "Sell";  // Monster loot defaults to Sell
                case ItemClass.Useable:
                case ItemClass.Ammo:
                default:
                    return "Keep";
            }
        }

        public static bool HasItemsToSell()
        {
            var state = PlayerState.Instance;
            var inv = state?.Inventory?.GetInventoryData();
            if (inv == null) return false;
            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item.ItemData != null && item.Count > 0 && GetItemDisposition(item) == "Sell")
                    return true;
            }
            return false;
        }

        public static bool HasItemsToStore()
        {
            var state = PlayerState.Instance;
            var inv = state?.Inventory?.GetInventoryData();
            if (inv == null) return false;
            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item.ItemData != null && item.Count > 0 && GetItemDisposition(item) == "Store")
                    return true;
            }
            return false;
        }

        public static List<(int itemId, int count)> GetItemsToPurchaseFromVendor(RestockVendorType vendor)
        {
            var result = new List<(int itemId, int count)>();
            if (!BotConfigManager.Current.AutoRestock || BotConfigManager.Current.RestockTargets == null)
                return result;

            var state = PlayerState.Instance;
            var inv = state?.Inventory?.GetInventoryData();

            var processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in BotConfigManager.Current.RestockTargets)
            {
                string key = kvp.Key;
                int targetCount = kvp.Value;
                if (targetCount <= 0) continue;

                if (!KnownRestockItems.TryGetValue(key, out var def))
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

            return result;
        }

        public static bool HasSuppliesNeeded()
        {
            return GetItemsToPurchaseFromVendor(RestockVendorType.GeneralVendor).Count > 0 ||
                   GetItemsToPurchaseFromVendor(RestockVendorType.RanchVendor).Count > 0 ||
                   GetItemsToPurchaseFromVendor(RestockVendorType.AlchemistVendor).Count > 0;
        }

        public static bool HasDepletedEssentialSupplies()
        {
            if (!BotConfigManager.Current.AutoRestock || !BotConfigManager.Current.AutoRestockOnLowSupplies || BotConfigManager.Current.RestockTargets == null)
                return false;

            var state = PlayerState.Instance;
            var inv = state?.Inventory?.GetInventoryData();

            // Check Fly Wings
            if (BotConfigManager.Current.RestockTargets.TryGetValue("Fly_Wing", out int flyTarget) ||
                BotConfigManager.Current.RestockTargets.TryGetValue("Fly Wing", out flyTarget))
            {
                if (flyTarget > 0)
                {
                    int currentFly = 0;
                    if (inv != null)
                    {
                        foreach (var kvp in inv)
                        {
                            var item = kvp.Value;
                            if (item.Id == 601 || item.Id == 12323) currentFly += item.Count;
                        }
                    }
                    if (currentFly == 0) return true;
                }
            }

            // Check HP Potions
            int potionTargetTotal = 0;
            int potionCurrentTotal = 0;
            string[] potionKeys = { "Red_Potion", "Red Potion", "Orange_Potion", "Orange Potion", "Yellow_Potion", "Yellow Potion", "White_Potion", "White Potion" };
            foreach (var key in potionKeys)
            {
                if (BotConfigManager.Current.RestockTargets.TryGetValue(key, out int target) && target > 0)
                {
                    potionTargetTotal += target;
                    if (KnownRestockItems.TryGetValue(key, out var def) && inv != null)
                    {
                        foreach (var kvp in inv)
                        {
                            if (kvp.Value.Id == def.ItemId) potionCurrentTotal += kvp.Value.Count;
                        }
                    }
                }
            }

            if (potionTargetTotal > 0 && potionCurrentTotal == 0)
                return true;

            return false;
        }

        public int GetAvailableButterflyWingId()
        {
            var playerState = PlayerState.Instance;
            if (playerState?.Inventory == null) return -1;
            var invData = playerState.Inventory.GetInventoryData();
            if (invData == null) return -1;

            bool hasStandard = false;
            bool hasNovice = false;
            foreach (var kvp in invData)
            {
                var item = kvp.Value;
                if (item.ItemData != null && item.Count > 0)
                {
                    if (item.ItemData.Id == 602) hasStandard = true;
                    if (item.ItemData.Id == 12324) hasNovice = true;
                }
            }

            if (hasStandard) return 602;
            if (hasNovice) return 12324;
            return -1;
        }

        public ServerControllable FindNearbyNpc(string nameSubstr, Vector2Int expectedPos)
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null || netManager.EntityList == null) return null;

            ServerControllable bestNpc = null;
            float bestDist = float.MaxValue;

            foreach (var kvp in netManager.EntityList)
            {
                var entity = kvp.Value;
                if (entity == null || entity.CharacterType != CharacterType.NPC) continue;

                float dist = Vector2.Distance(entity.CellPosition, expectedPos);
                if (dist <= 6.0f)
                {
                    bool nameMatches = string.IsNullOrEmpty(nameSubstr) ||
                                       (!string.IsNullOrEmpty(entity.Name) && entity.Name.IndexOf(nameSubstr, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (nameMatches && dist < bestDist)
                    {
                        bestDist = dist;
                        bestNpc = entity;
                    }
                }
            }
            return bestNpc;
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

        public bool ProcessTownRoutine(NetworkManager netManager, ServerControllable player, NavigationController navigation, float now)
        {
            if (currentState == TownRoutineState.Idle)
            {
                // Defer starting town routine if currently looting
                if (BotEngine.Instance != null && BotEngine.Instance.Loot != null)
                {
                    if (BotEngine.Instance.Loot.PendingLootItemId != -1 || BotEngine.Instance.Loot.FindNearestGroundItem(player.CellPosition) != null)
                        return false;
                }

                // Check overweight trigger
                if (BotConfigManager.Current.AutoReturnToBaseOnWeight)
                {
                    var state = PlayerState.Instance;
                    if (state != null && state.MaxWeight > 0)
                    {
                        float weightPercent = (float)state.CurrentWeight / state.MaxWeight * 100f;
                        if (weightPercent >= BotConfigManager.Current.ReturnToBaseWeightPercent)
                        {
                            StartRoutine("Overweight threshold reached");
                            return true;
                        }
                    }
                }

                // Check depleted supplies trigger
                if (BotConfigManager.Current.AutoRestockOnLowSupplies && HasDepletedEssentialSupplies())
                {
                    StartRoutine("Depleted essential supplies");
                    return true;
                }

                return false;
            }

            bool inBaseMap = string.Equals(netManager.CurrentMap, BaseMap, StringComparison.OrdinalIgnoreCase);

            switch (currentState)
            {
                case TownRoutineState.ReturningToBase:
                    if (inBaseMap)
                    {
                        // Once in base, decide where to navigate first
                        if (HasItemsToSell())
                        {
                            currentState = TownRoutineState.NavigatingToGeneralVendorSell;
                            stepPhase = 0;
                            targetStopDistance = UnityEngine.Random.Range(2.2f, 6.0f);
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

                case TownRoutineState.NavigatingToGeneralVendorSell:
                    if (!inBaseMap)
                    {
                        currentState = TownRoutineState.ReturningToBase;
                        return true;
                    }

                    if (targetStopDistance <= 0f) targetStopDistance = UnityEngine.Random.Range(2.2f, 6.0f);
                    float distToVendor = Vector2.Distance(player.CellPosition, GeneralVendorPosition);
                    if (distToVendor <= targetStopDistance)
                    {
                        currentState = TownRoutineState.InteractingWithGeneralVendorSell;
                        stepPhase = 0;
                        lastActionTime = now;
                        BotEngine.Instance?.LogEvent($"[Town Routine] Reached General Vendor (dist: {distToVendor:F1} tiles). Opening sell shop.");
                        return true;
                    }

                    if (!player.IsMoving && now - lastActionTime >= 0.3f)
                    {
                        navigation.NavigateTowards(player.CellPosition, GeneralVendorPosition, avoidPortals: false, hopDistance: 11);
                        lastActionTime = now;
                    }
                    return true;

                case TownRoutineState.InteractingWithGeneralVendorSell:
                    if (stepPhase == 0)
                    {
                        var vendorNpc = FindNearbyNpc("Vendor", GeneralVendorPosition);
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
                    else if (stepPhase == 1 && now - lastActionTime >= 0.4f)
                    {
                        netManager.SendNpcSelectOption(1); // Option 1 is "Sell"
                        stepPhase = 2;
                        lastActionTime = now;
                    }
                    else if (stepPhase == 2 && now - lastActionTime >= 0.5f)
                    {
                        var state = PlayerState.Instance;
                        if (state?.Inventory != null)
                        {
                            var inv = state.Inventory.GetInventoryData();
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
                                BotEngine.Instance?.LogEvent($"[Town Routine] Sold {itemsToSell.Count} item stack(s) to General Vendor.");
                            }
                            else
                            {
                                netManager.SubmitShopPurchase(null);
                                BotEngine.Instance?.LogEvent("[Town Routine] No items marked for sale.");
                            }

                            CloseOpenShopUI();
                        }

                        AdvanceFromGeneralVendor(now);
                    }
                    return true;

                case TownRoutineState.BuyingAtGeneralVendor:
                    if (stepPhase == 0)
                    {
                        var vendorNpc = FindNearbyNpc("Vendor", GeneralVendorPosition);
                        if (vendorNpc != null)
                        {
                            netManager.SendNpcClick(vendorNpc.Id);
                            stepPhase = 1;
                            lastActionTime = now;
                        }
                        else if (now - lastActionTime > 3.0f)
                        {
                            AdvanceFromRanchVendor(now);
                        }
                    }
                    else if (stepPhase == 1 && now - lastActionTime >= 0.4f)
                    {
                        netManager.SendNpcSelectOption(0); // Option 0 is "Buy"
                        stepPhase = 2;
                        lastActionTime = now;
                    }
                    else if (stepPhase == 2 && now - lastActionTime >= 0.5f)
                    {
                        var buys = GetItemsToPurchaseFromVendor(RestockVendorType.GeneralVendor);
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
                            BotEngine.Instance?.LogEvent($"[Town Routine] Purchased {buys.Count} item type(s) from General Vendor.");
                        }
                        else
                        {
                            netManager.SubmitShopPurchase(null);
                        }

                        CloseOpenShopUI();
                        AdvanceFromRanchVendor(now);
                    }
                    return true;

                case TownRoutineState.NavigatingToRanchVendor:
                    if (targetStopDistance <= 0f) targetStopDistance = UnityEngine.Random.Range(2.2f, 6.0f);
                    float distToRanch = Vector2.Distance(player.CellPosition, RanchVendorPosition);
                    if (distToRanch <= targetStopDistance)
                    {
                        currentState = TownRoutineState.BuyingAtRanchVendor;
                        stepPhase = 0;
                        lastActionTime = now;
                        BotEngine.Instance?.LogEvent($"[Town Routine] Reached Ranch Vendor (dist: {distToRanch:F1} tiles). Opening shop.");
                        return true;
                    }

                    if (!player.IsMoving && now - lastActionTime >= 0.3f)
                    {
                        navigation.NavigateTowards(player.CellPosition, RanchVendorPosition, avoidPortals: false, hopDistance: 11);
                        lastActionTime = now;
                    }
                    return true;

                case TownRoutineState.BuyingAtRanchVendor:
                    if (stepPhase == 0)
                    {
                        var ranchNpc = FindNearbyNpc("Ranch", RanchVendorPosition);
                        if (ranchNpc != null)
                        {
                            netManager.SendNpcClick(ranchNpc.Id);
                            stepPhase = 1;
                            lastActionTime = now;
                        }
                        else if (now - lastActionTime > 3.0f)
                        {
                            AdvanceFromAlchemist(now);
                        }
                    }
                    else if (stepPhase == 1 && now - lastActionTime >= 0.4f)
                    {
                        netManager.SendNpcSelectOption(0); // Option 0 is "Buy"
                        stepPhase = 2;
                        lastActionTime = now;
                    }
                    else if (stepPhase == 2 && now - lastActionTime >= 0.5f)
                    {
                        var buys = GetItemsToPurchaseFromVendor(RestockVendorType.RanchVendor);
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
                            BotEngine.Instance?.LogEvent($"[Town Routine] Purchased {buys.Count} item type(s) from Ranch Vendor.");
                        }
                        else
                        {
                            netManager.SubmitShopPurchase(null);
                        }

                        CloseOpenShopUI();
                        AdvanceFromAlchemist(now);
                    }
                    return true;

                case TownRoutineState.NavigatingToAlchemist:
                    if (targetStopDistance <= 0f) targetStopDistance = UnityEngine.Random.Range(2.2f, 6.0f);
                    float distToAlch = Vector2.Distance(player.CellPosition, AlchemistPosition);
                    if (distToAlch <= targetStopDistance)
                    {
                        currentState = TownRoutineState.BuyingAtAlchemist;
                        stepPhase = 0;
                        lastActionTime = now;
                        BotEngine.Instance?.LogEvent($"[Town Routine] Reached Diligent Alchemist (dist: {distToAlch:F1} tiles). Opening shop.");
                        return true;
                    }

                    if (!player.IsMoving && now - lastActionTime >= 0.3f)
                    {
                        navigation.NavigateTowards(player.CellPosition, AlchemistPosition, avoidPortals: false, hopDistance: 11);
                        lastActionTime = now;
                    }
                    return true;

                case TownRoutineState.BuyingAtAlchemist:
                    if (stepPhase == 0)
                    {
                        var alchNpc = FindNearbyNpc("Alchemist", AlchemistPosition);
                        if (alchNpc != null)
                        {
                            netManager.SendNpcClick(alchNpc.Id);
                            stepPhase = 1;
                            lastActionTime = now;
                        }
                        else if (now - lastActionTime > 3.0f)
                        {
                            if (HasItemsToStore())
                            {
                                currentState = TownRoutineState.NavigatingToKafra;
                                stepPhase = 0;
                                targetStopDistance = UnityEngine.Random.Range(2.2f, 6.0f);
                                lastActionTime = now + 0.4f;
                            }
                            else
                            {
                                currentState = TownRoutineState.Completed;
                            }
                        }
                    }
                    else if (stepPhase == 1 && now - lastActionTime >= 0.4f)
                    {
                        netManager.SendNpcSelectOption(0); // Option 0 is "Buy"
                        stepPhase = 2;
                        lastActionTime = now;
                    }
                    else if (stepPhase == 2 && now - lastActionTime >= 0.5f)
                    {
                        var buys = GetItemsToPurchaseFromVendor(RestockVendorType.AlchemistVendor);
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
                            BotEngine.Instance?.LogEvent($"[Town Routine] Purchased {buys.Count} item type(s) from Diligent Alchemist.");
                        }
                        else
                        {
                            netManager.SubmitShopPurchase(null);
                        }

                        CloseOpenShopUI();

                        if (HasItemsToStore())
                        {
                            currentState = TownRoutineState.NavigatingToKafra;
                            stepPhase = 0;
                            targetStopDistance = UnityEngine.Random.Range(2.2f, 6.0f);
                            lastActionTime = now + 0.4f;
                        }
                        else
                        {
                            currentState = TownRoutineState.Completed;
                            stepPhase = 0;
                            lastActionTime = now;
                        }
                    }
                    return true;

                case TownRoutineState.NavigatingToKafra:
                    if (targetStopDistance <= 0f) targetStopDistance = UnityEngine.Random.Range(2.2f, 6.0f);
                    float distToKafra = Vector2.Distance(player.CellPosition, KafraPosition);
                    if (distToKafra <= targetStopDistance)
                    {
                        currentState = TownRoutineState.InteractingWithKafra;
                        stepPhase = 0;
                        lastActionTime = now;
                        BotEngine.Instance?.LogEvent($"[Town Routine] Reached Kafra Staff (dist: {distToKafra:F1} tiles). Opening storage.");
                        return true;
                    }

                    if (!player.IsMoving && now - lastActionTime >= 0.3f)
                    {
                        navigation.NavigateTowards(player.CellPosition, KafraPosition, avoidPortals: false, hopDistance: 11);
                        lastActionTime = now;
                    }
                    return true;

                case TownRoutineState.InteractingWithKafra:
                    var cam = CameraFollower.Instance;
                    bool dialogOpen = cam != null && cam.DialogPanel != null && cam.DialogPanel.activeSelf;
                    bool optionOpen = cam != null && cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;

                    if (stepPhase == 0)
                    {
                        var kafraNpc = FindNearbyNpc("Kafra", KafraPosition);
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
                        // 1. If option menu is open, select "Use Storage"
                        if (optionOpen)
                        {
                            if (now - lastActionTime < 0.6f) return true; // Human reading delay

                            var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(false);
                            if (buttons != null && buttons.Length > 0)
                            {
                                foreach (var btn in buttons)
                                {
                                    if (btn == null) continue;
                                    string text = btn.TextBox != null ? btn.TextBox.text : "";
                                    if (text.IndexOf("Storage", StringComparison.OrdinalIgnoreCase) >= 0 || btn.Id == 1)
                                    {
                                        btn.OnClick();
                                        stepPhase = 2; // Storage option chosen
                                        lastActionTime = now + UnityEngine.Random.Range(0.8f, 1.2f); // Human wait for storage UI to open
                                        BotEngine.Instance?.LogEvent($"[Town Routine] Selected 'Use Storage' (ID: {btn.Id}).");
                                        return true;
                                    }
                                }
                            }
                        }
                        // 2. Advance dialogue prompt to open the option menu
                        else if (dialogOpen)
                        {
                            if (now - lastActionTime >= 0.75f) // Human reading delay
                            {
                                netManager.SendNpcAdvance();
                                lastActionTime = now;
                                BotEngine.Instance?.LogEvent("[Town Routine] Advanced Kafra welcome dialog.");
                            }
                            return true;
                        }
                        else if (now - lastActionTime >= 3.5f)
                        {
                            stepPhase = 0; // Retry click
                            lastActionTime = now;
                        }
                    }
                    else if (stepPhase == 2 && now - lastActionTime >= 0f)
                    {
                        var state = PlayerState.Instance;
                        if (state?.Inventory != null)
                        {
                            var inv = state.Inventory.GetInventoryData();
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
                        }

                        // Human pause before closing storage
                        stepPhase = 3;
                        lastActionTime = now + UnityEngine.Random.Range(0.7f, 1.0f);
                    }
                    else if (stepPhase == 3 && now - lastActionTime >= 0f)
                    {
                        netManager.SendEndStorage();
                        CloseOpenStorageUI();
                        // Wait 2.0s for server database write and interaction cleanup
                        stepPhase = 4;
                        lastActionTime = now + 2.0f;
                        BotEngine.Instance?.LogEvent("[Town Routine] Closed Kafra Storage. Waiting for server sync...");
                    }
                    else if (stepPhase == 4 && now - lastActionTime >= 0f)
                    {
                        CloseOpenStorageUI();
                        currentState = TownRoutineState.Completed;
                        lastCompletedTime = now;
                        stepPhase = 0;
                        lastActionTime = now;
                    }
                    return true;

                case TownRoutineState.Completed:
                    CloseOpenShopUI();
                    CloseOpenStorageUI();
                    lastCompletedTime = now;
                    BotEngine.Instance?.LogEvent("[Town Routine] Base operations completed successfully. Resuming hunting.");
                    currentState = TownRoutineState.Idle;
                    return false;
            }

            return false;
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
                targetStopDistance = UnityEngine.Random.Range(2.2f, 6.0f);
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
                targetStopDistance = UnityEngine.Random.Range(2.2f, 6.0f);
                lastActionTime = now + 0.4f;
                BotEngine.Instance?.LogEvent($"[Town Routine] Moving to Diligent Alchemist to buy supplies (target distance: {targetStopDistance:F1} tiles).");
                return;
            }

            if (HasItemsToStore())
            {
                currentState = TownRoutineState.NavigatingToKafra;
                stepPhase = 0;
                targetStopDistance = UnityEngine.Random.Range(2.2f, 6.0f);
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

        public static void CloseOpenStorageUI()
        {
            try
            {
                if (StorageUI.Instance != null)
                {
                    UnityEngine.Object.Destroy(StorageUI.Instance.gameObject);
                    StorageUI.Instance = null;
                }
                var cam = CameraFollower.Instance;
                if (cam != null)
                {
                    cam.IsInNPCInteraction = false;
                    cam.OverrideTarget = null;
                    if (cam.DialogPanel != null)
                    {
                        var dialogWindow = cam.DialogPanel.GetComponent<DialogWindow>();
                        if (dialogWindow != null)
                            dialogWindow.HideUI();
                        else
                            cam.DialogPanel.SetActive(false);
                    }
                    if (cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf)
                        cam.NpcOptionPanel.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                BotEngine.Instance?.LogEvent($"[Town Routine] Note closing storage UI: {ex.Message}");
            }
        }
    }
}
