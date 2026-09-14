using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Assets.Scripts;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI;
using Assets.Scripts.UI.Inventory;
using RebuildBotPlugin.Models;
using RebuildBotPlugin.Services;
using RebuildSharedData.Enum;
using RebuildSharedData.Networking;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public enum DistributorState
    {
        Idle,
        NavigatingToPost,
        CheckingConsumablesAndCart,
        OpeningVendingStore,
        Vending,
        CollectingDonations,
        NavigatingToSellNpc,
        InteractingWithSellNpc,
        NavigatingToTownNpc,
        InteractingWithTownNpc,
        NavigatingToWeaponDealer,
        InteractingWithWeaponDealer,
        ReturningFromWeaponDealer,
        NavigatingToKafra,
        InteractingWithKafra
    }

    public class DistributorInfo
    {
        public string ProfileName { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public string Map { get; set; } = "";
        public Vector2Int Position { get; set; } = Vector2Int.zero;
        public bool IsOnline { get; set; } = false;
        public bool IsVendingOpen { get; set; } = false;
        public bool IsReadyForDonations { get; set; } = false;
        public string ShopTitle { get; set; } = "";
    }

    public class DistributorController
    {
        public DistributorState State { get; private set; } = DistributorState.Idle;
        public bool IsReadyForDonations { get; private set; } = false;
        public bool IsVendingOpen { get; private set; } = false;

        private float lastActionTime = 0f;
        private float lastStateLogTime = 0f;
        private float lastGroundItemTime = 0f;
        private float shopOpenedTime = 0f;
        private int stepPhase = 0;
        private bool hasCheckedOverrideNpc = false;
        private HashSet<int> visitedShopNpcIds = new HashSet<int>();
        private int currentShopNpcId = -1;
        private float restockCooldownUntil = 0f;
        private readonly HashSet<string> unstockedConsumables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static DistributorInfo cachedDistributor = null;
        private static float cachedDistributorTimestamp = 0f;

        private static string ReadFileSafe(string path)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(fs, System.Text.Encoding.UTF8);
                    return reader.ReadToEnd();
                }
                catch (IOException)
                {
                    if (attempt == 2) return null;
                    System.Threading.Thread.Sleep(5);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public static bool IsMerchantClass()
        {
            var state = PlayerState.Instance;
            if (state == null) return false;
            if (state.JobId == 5 || state.JobId == 6 || state.JobId == 11) return true;
            string jobName = Assets.Scripts.Sprites.ClientDataLoader.Instance?.GetJobNameForId(state.JobId) ?? "";
            return jobName.IndexOf("Merchant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   jobName.IndexOf("Blacksmith", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<string> GetProfileDirectories()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(ProfileManager.DevBaseDirectory))
                {
                    string devProfiles = Path.Combine(ProfileManager.DevBaseDirectory, "profiles");
                    if (Directory.Exists(devProfiles))
                    {
                        foreach (var d in Directory.GetDirectories(devProfiles))
                        {
                            if (!list.Contains(d)) list.Add(d);
                        }
                    }
                }
                if (Directory.Exists(ProfileManager.GameBaseDirectory))
                {
                    string gameProfiles = Path.Combine(ProfileManager.GameBaseDirectory, "profiles");
                    if (Directory.Exists(gameProfiles))
                    {
                        foreach (var d in Directory.GetDirectories(gameProfiles))
                        {
                            if (!list.Contains(d)) list.Add(d);
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        public static bool TryGetActiveDistributor(out DistributorInfo dist)
        {
            dist = null;
            try
            {
                var profileDirs = GetProfileDirectories();
                if (profileDirs.Count == 0)
                {
                    if (cachedDistributor != null && (Time.time - cachedDistributorTimestamp < 5.0f))
                    {
                        dist = cachedDistributor;
                        return true;
                    }
                    return false;
                }

                foreach (var dir in profileDirs)
                {
                    string pName = Path.GetFileName(dir);
                    if (string.Equals(pName, ProfileManager.ActiveProfileName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string statusPath = Path.Combine(dir, "bot_status.json");
                    if (!File.Exists(statusPath)) continue;

                    var writeTime = File.GetLastWriteTimeUtc(statusPath);
                    if (DateTime.UtcNow - writeTime > TimeSpan.FromSeconds(60)) continue;

                    string json = ReadFileSafe(statusPath);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    bool isDist = root.TryGetProperty("IsDistributor", out var idProp) && idProp.GetBoolean();
                    if (!isDist) continue;

                    string map = root.TryGetProperty("DistributorMap", out var dmProp) ? dmProp.GetString() ?? "" : "";
                    int x = root.TryGetProperty("DistributorX", out var dxProp) ? dxProp.GetInt32() : 0;
                    int y = root.TryGetProperty("DistributorY", out var dyProp) ? dyProp.GetInt32() : 0;
                    bool readyForDonations = root.TryGetProperty("IsReadyForDonations", out var rfdProp) && rfdProp.GetBoolean();
                    bool vendingOpen = root.TryGetProperty("IsVendingOpen", out var voProp) && voProp.GetBoolean();
                    string shopTitle = root.TryGetProperty("VendingShopTitle", out var stProp) ? stProp.GetString() ?? "" : "";
                    string charName = root.TryGetProperty("CharacterName", out var cnProp) ? cnProp.GetString() ?? pName : pName;

                    dist = new DistributorInfo
                    {
                        ProfileName = pName,
                        CharacterName = charName,
                        Map = map,
                        Position = new Vector2Int(x, y),
                        IsOnline = true,
                        IsVendingOpen = vendingOpen,
                        IsReadyForDonations = readyForDonations,
                        ShopTitle = shopTitle
                    };
                    cachedDistributor = dist;
                    cachedDistributorTimestamp = Time.time;
                    return true;
                }
            }
            catch (Exception ex)
            {
                BotLog.Warn($"[Distributor] Note discovering distributor: {ex.Message}");
            }

            if (cachedDistributor != null && (Time.time - cachedDistributorTimestamp < 5.0f))
            {
                dist = cachedDistributor;
                return true;
            }

            return false;
        }

        public bool ProcessDistributor(
            NetworkManager netManager,
            ServerControllable player,
            NavigationController navigation,
            LootController loot,
            float now,
            ref BotState currentState)
        {
            if (netManager == null || player == null || !player.IsCharacterAlive)
            {
                State = DistributorState.Idle;
                IsReadyForDonations = false;
                IsVendingOpen = false;
                return false;
            }

            var cfg = BotConfigManager.Current;
            var targetPost = new Vector2Int(cfg.DistributorX, cfg.DistributorY);
            string targetMap = !string.IsNullOrEmpty(cfg.DistributorMap) ? cfg.DistributorMap : TownRoutineController.BaseMap;

            // Log state occasionally
            if (now - lastStateLogTime >= 5.0f)
            {
                lastStateLogTime = now;
                BotLog.Debug($"[Distributor] State: {State}, Vending: {IsVendingOpen}, ReadyForDonations: {IsReadyForDonations}, Map: {netManager.CurrentMap}");
            }

            switch (State)
            {
                case DistributorState.Idle:
                    IsReadyForDonations = false;
                    IsVendingOpen = false;
                    stepPhase = 0;

                    // 1. Check if pushcart is rented
                    if (PlayerState.Instance != null && !PlayerState.Instance.HasCart)
                    {
                        BotEngine.Instance?.LogEvent("[Distributor] Pushcart is required to operate depot/vending. Routing to Kafra to rent pushcart.");
                        State = DistributorState.NavigatingToKafra;
                        lastActionTime = now;
                        return true;
                    }

                    // 2. Evaluate restock cycle from wherever the bot is currently located
                    if (now >= restockCooldownUntil)
                    {
                        unstockedConsumables.Clear();
                    }

                    if (TownRoutineController.HasItemsToSell() || NeedsToolDealerRestock(cfg))
                    {
                        BotEngine.Instance?.LogEvent("[Distributor] Initiating restock cycle: Routing to Tool vendor.");
                        hasCheckedOverrideNpc = false;
                        visitedShopNpcIds.Clear();
                        State = DistributorState.NavigatingToSellNpc;
                        lastActionTime = now;
                        return true;
                    }

                    if (NeedsWeaponDealerRestock(cfg) && IsIzludeDistributor(netManager, targetMap))
                    {
                        BotEngine.Instance?.LogEvent("[Distributor] Initiating restock cycle: Routing to Weapon Dealer for Silver Arrows.");
                        State = DistributorState.NavigatingToWeaponDealer;
                        lastActionTime = now;
                        return true;
                    }

                    State = DistributorState.NavigatingToPost;
                    lastActionTime = now;
                    return true;

                case DistributorState.NavigatingToPost:
                    currentState = BotState.TravelingToTargetMap;

                    if (!string.Equals(netManager.CurrentMap, targetMap, StringComparison.OrdinalIgnoreCase) ||
                        !MapNavMesh.Instance.IsReachable(player.CellPosition, targetPost))
                    {
                        if (!TownRoutineController.IsTownMap(netManager.CurrentMap) && !TownRoutineController.IsTownOrBaseMap(netManager.CurrentMap))
                        {
                            int bwing = TownRoutineController.GetAvailableButterflyWingId();
                            if (bwing > 0 && now - lastActionTime >= 2.0f)
                            {
                                netManager.SendUseItem(bwing);
                                lastActionTime = now;
                                BotEngine.Instance?.LogEvent("[Distributor] Used Butterfly Wing to return towards distributor town.");
                                return true;
                            }
                        }
                        var travelState = BotState.TravelingToTargetMap;
                        navigation.ProcessTravel(netManager, player, now, ref travelState, targetCellPos: targetPost, destinationMapOverride: targetMap);
                        return true;
                    }

                    // On the correct map and reachable zone, walk to the post
                    float distToPost = Vector2.Distance(player.CellPosition, targetPost);
                    if (distToPost > 1.5f)
                    {
                        currentState = BotState.DistributorRestocking;
                        if (!player.IsMoving && now - lastActionTime >= 0.25f)
                        {
                            navigation.NavigateTowards(player.CellPosition, targetPost, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                            lastActionTime = now;
                        }
                        return true;
                    }

                    // Arrived at post
                    State = DistributorState.CheckingConsumablesAndCart;
                    stepPhase = 0;
                    lastActionTime = now;
                    return true;

                case DistributorState.CheckingConsumablesAndCart:
                    currentState = BotState.DistributorRestocking;
                    if (now - lastActionTime < 0.5f) return true;

                    // 1. Check if pushcart is rented
                    if (PlayerState.Instance != null && !PlayerState.Instance.HasCart)
                    {
                        BotEngine.Instance?.LogEvent("[Distributor] Pushcart is required to operate depot/vending. Routing to Kafra to rent pushcart.");
                        State = DistributorState.NavigatingToKafra;
                        stepPhase = 0;
                        lastActionTime = now;
                        return true;
                    }

                    // 2. Move any "Store" items in inventory bag into Cart
                    if (MoveInventoryStoreItemsToCart(netManager))
                    {
                        lastActionTime = now + 0.3f;
                        return true;
                    }

                    // 3. Move any vend consumables in inventory bag into Cart
                    if (MoveVendConsumablesToCart(netManager, cfg))
                    {
                        lastActionTime = now + 0.3f;
                        return true;
                    }

                    // 4. Check if we have monster loot to sell
                    if (TownRoutineController.HasItemsToSell())
                    {
                        hasCheckedOverrideNpc = false;
                        State = DistributorState.NavigatingToSellNpc;
                        stepPhase = 0;
                        lastActionTime = now;
                        return true;
                    }

                    // 5. Check Cart Weight overflow (>= 90%)
                    if (IsCartOverweightThreshold())
                    {
                        State = DistributorState.NavigatingToKafra;
                        stepPhase = 0;
                        lastActionTime = now;
                        return true;
                    }

                    // 6. Check if any vend consumables are below target stock or missing
                    if (now >= restockCooldownUntil)
                    {
                        unstockedConsumables.Clear();
                    }

                    if (NeedsConsumableRestock(cfg, ignoreUnstocked: now < restockCooldownUntil))
                    {
                        if (NeedsToolDealerRestock(cfg))
                        {
                            BotEngine.Instance?.LogEvent("[Distributor] Vended consumable stock below threshold. Routing to Tool vendor for restock.");
                            hasCheckedOverrideNpc = false;
                            visitedShopNpcIds.Clear();
                            State = DistributorState.NavigatingToSellNpc;
                            stepPhase = 0;
                            lastActionTime = now;
                            return true;
                        }
                        else if (NeedsWeaponDealerRestock(cfg) && IsIzludeDistributor(netManager, targetMap))
                        {
                            BotEngine.Instance?.LogEvent("[Distributor] Silver arrow stock below threshold. Routing to Weapon Dealer across town plaza for restock.");
                            State = DistributorState.NavigatingToWeaponDealer;
                            stepPhase = 0;
                            lastActionTime = now;
                            return true;
                        }
                        else
                        {
                            BotEngine.Instance?.LogEvent("[Distributor] Consumable stock below threshold. Routing to vendor for restock.");
                            hasCheckedOverrideNpc = false;
                            visitedShopNpcIds.Clear();
                            State = DistributorState.NavigatingToSellNpc;
                            stepPhase = 0;
                            lastActionTime = now;
                            return true;
                        }
                    }

                    // 7. Check if we have items ready to vend
                    var vendItems = BuildVendingItemsList(cfg);
                    if (vendItems.Count > 0)
                    {
                        // Ready to vend!
                        State = DistributorState.OpeningVendingStore;
                        stepPhase = 0;
                        lastActionTime = now;
                        return true;
                    }

                    // 8. Cart has 0 items to vend
                    if (now >= restockCooldownUntil)
                    {
                        if (NeedsWeaponDealerRestock(cfg) && !NeedsToolDealerRestock(cfg) && IsIzludeDistributor(netManager, targetMap))
                        {
                            BotEngine.Instance?.LogEvent("[Distributor] No consumables in cart. Routing to Weapon Dealer for restock.");
                            State = DistributorState.NavigatingToWeaponDealer;
                            stepPhase = 0;
                            lastActionTime = now;
                            return true;
                        }
                        else
                        {
                            BotEngine.Instance?.LogEvent("[Distributor] No consumables in cart to vend. Routing to vendor for restock.");
                            hasCheckedOverrideNpc = false;
                            visitedShopNpcIds.Clear();
                            State = DistributorState.NavigatingToSellNpc;
                            stepPhase = 0;
                            lastActionTime = now;
                            return true;
                        }
                    }

                    // All local vendors checked and 0 items to vend: stay at post and wait for fleet donations
                    currentState = BotState.DistributorCollectingLoot;
                    IsReadyForDonations = true;
                    IsVendingOpen = false;
                    State = DistributorState.CollectingDonations;
                    stepPhase = 0;
                    lastActionTime = now;
                    BotEngine.Instance?.LogEvent("[Distributor] All local vendors checked. Waiting at post for fleet donations...");
                    BotEngine.Instance?.ForceEmitStatus();
                    return true;

                case DistributorState.OpeningVendingStore:
                    currentState = BotState.DistributorVending;
                    if (now - lastActionTime < 0.8f) return true;

                    if (PlayerState.Instance != null && !PlayerState.Instance.HasCart)
                    {
                        BotEngine.Instance?.LogEvent("[Distributor] Pushcart is required to open vending store! Routing to Kafra.");
                        State = DistributorState.NavigatingToKafra;
                        stepPhase = 0;
                        lastActionTime = now;
                        return true;
                    }

                    var itemsToVend = BuildVendingItemsList(cfg);
                    if (itemsToVend.Count == 0)
                    {
                        // No items ready to vend; restock from NPCs
                        BotEngine.Instance?.LogEvent("[Distributor] No consumables in cart to vend. Routing to restock.");
                        State = DistributorState.NavigatingToSellNpc;
                        stepPhase = 0;
                        lastActionTime = now;
                        return true;
                    }

                    // Check distance to nearby NPCs: server requires player to be > 4 tiles from any NPC!
                    if (IsTooCloseToNpc(netManager, player, 4.5f, out var nearbyNpcName, out var nearbyNpcPos))
                    {
                        BotEngine.Instance?.LogEvent($"[Distributor] Post is too close to NPC '{nearbyNpcName}' (dist: {Vector2.Distance(player.CellPosition, nearbyNpcPos):F1} tiles). Stepping 5 tiles away to satisfy server vending rule.");
                        Vector2 stepAway = ((Vector2)(player.CellPosition - nearbyNpcPos)).normalized * 5.0f;
                        Vector2Int safeTile = nearbyNpcPos + new Vector2Int(Mathf.RoundToInt(stepAway.x), Mathf.RoundToInt(stepAway.y));
                        navigation.NavigateTowards(player.CellPosition, safeTile, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                        lastActionTime = now + 1.0f;
                        return true;
                    }

                    // Start Vending
                    SendVendingStartPacket(netManager, !string.IsNullOrEmpty(cfg.VendingShopTitle) ? cfg.VendingShopTitle : "Fleet Depot", itemsToVend);
                    IsVendingOpen = true;
                    IsReadyForDonations = false;
                    State = DistributorState.Vending;
                    shopOpenedTime = now;
                    lastActionTime = now + 1.0f;
                    BotEngine.Instance?.LogEvent($"[Distributor] Opened Vending Shop '{cfg.VendingShopTitle}' with {itemsToVend.Count} consumable stacks at 1z.");
                    return true;

                case DistributorState.Vending:
                    currentState = BotState.DistributorVending;
                    IsVendingOpen = true;

                    // 1. Buyer Priority Grace Period: keep shop open for at least 8 seconds after opening
                    if (now - shopOpenedTime < 8.0f)
                    {
                        return true;
                    }

                    // 2. Active Buyers: if any nearby fleet bot is currently buying, keep shop open
                    if (HasNearbyActiveBuyers(netManager, player))
                    {
                        return true;
                    }

                    // 3. Incoming Donors: only close shop if a verified fleet bot specifically wants to donate
                    if (TryGetIncomingFleetDonor(netManager, player, out string donorName))
                    {
                        BotEngine.Instance?.LogEvent($"[Distributor] Detected incoming donor '{donorName}' ready to donate. Closing shop to collect donations.");
                        netManager.VendingEnd();
                        IsVendingOpen = false;
                        IsReadyForDonations = true;
                        lastGroundItemTime = now;
                        State = DistributorState.CollectingDonations;
                        lastActionTime = now;
                        BotEngine.Instance?.ForceEmitStatus();
                        return true;
                    }
                    return true;

                case DistributorState.CollectingDonations:
                    currentState = BotState.DistributorCollectingLoot;
                    IsReadyForDonations = true;

                    // Collect ground items near distributor post
                    if (netManager.GroundItemList != null && netManager.GroundItemList.Count > 0)
                    {
                        var nearestGroundItem = loot.FindNearestGroundItem(player.CellPosition, targetPost, 4.0f);
                        if (nearestGroundItem != null)
                        {
                            lastGroundItemTime = now;
                            if (now - lastActionTime >= 0.25f)
                            {
                                netManager.SendPickUpItem(nearestGroundItem.EntityId);
                                lastActionTime = now;
                                BotLog.Info($"[Distributor] Picking up donated item '{nearestGroundItem.ItemName}' (ID: {nearestGroundItem.EntityId}).");
                            }
                            return true;
                        }
                    }

                    // Wait for ground items: if donor is nearby, allow up to 8 seconds for donor to drop items
                    bool donorStillNearby = TryGetIncomingFleetDonor(netManager, player, out _);
                    float collectionTimeout = donorStillNearby ? 8.0f : 2.5f;

                    if (now - lastGroundItemTime >= collectionTimeout)
                    {
                        IsReadyForDonations = false;
                        BotEngine.Instance?.LogEvent("[Distributor] Finished collecting donations. Processing payload.");
                        State = DistributorState.CheckingConsumablesAndCart;
                        stepPhase = 0;
                        lastActionTime = now;
                        BotEngine.Instance?.ForceEmitStatus();
                        return true;
                    }
                    return true;

                case DistributorState.NavigatingToSellNpc:
                    currentState = BotState.DistributorRestocking;
                    Vector2Int sellTargetPos;
                    string sellTargetMap;

                    if (cfg.DistributorOverrideNpcEnabled && !string.IsNullOrEmpty(cfg.DistributorOverrideNpcMap))
                    {
                        sellTargetMap = cfg.DistributorOverrideNpcMap;
                        sellTargetPos = new Vector2Int(cfg.DistributorOverrideNpcX, cfg.DistributorOverrideNpcY);
                    }
                    else if (IsIzludeDistributor(netManager, targetMap))
                    {
                        sellTargetMap = "izlude_in";
                        sellTargetPos = new Vector2Int(120, 63); // Tool Dealer in Tool Shop room
                    }
                    else
                    {
                        // If on the post map and there's a shop NPC on this map, use it!
                        if (TryFindLocalShopNpc(netManager, player, visitedShopNpcIds, out _, out var localPos))
                        {
                            sellTargetMap = netManager.CurrentMap;
                            sellTargetPos = localPos;
                        }
                        else
                        {
                            sellTargetMap = TownRoutineController.BaseMap;
                            sellTargetPos = TownRoutineController.GeneralVendorPosition;
                        }
                    }

                    if (!string.Equals(netManager.CurrentMap, sellTargetMap, StringComparison.OrdinalIgnoreCase) ||
                        !MapNavMesh.Instance.IsReachable(player.CellPosition, sellTargetPos))
                    {
                        var tState = BotState.TravelingToTargetMap;
                        navigation.ProcessTravel(netManager, player, now, ref tState, targetCellPos: sellTargetPos, destinationMapOverride: sellTargetMap);
                        return true;
                    }

                    if (Vector2.Distance(player.CellPosition, sellTargetPos) > 2.5f)
                    {
                        if (!player.IsMoving && now - lastActionTime >= 0.25f)
                        {
                            navigation.NavigateTowards(player.CellPosition, sellTargetPos, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                            lastActionTime = now;
                        }
                        return true;
                    }

                    State = DistributorState.InteractingWithSellNpc;
                    stepPhase = 0;
                    lastActionTime = now;
                    return true;

                case DistributorState.InteractingWithSellNpc:
                    currentState = BotState.DistributorRestocking;
                    return ProcessSellAndBuyAtNpc(netManager, player, cfg, now, isOverride: cfg.DistributorOverrideNpcEnabled, targetMap: targetMap);

                case DistributorState.NavigatingToTownNpc:
                    currentState = BotState.DistributorRestocking;
                    if (!string.Equals(netManager.CurrentMap, TownRoutineController.BaseMap, StringComparison.OrdinalIgnoreCase))
                    {
                        var tState = BotState.TravelingToTargetMap;
                        navigation.ProcessTravel(netManager, player, now, ref tState, targetCellPos: TownRoutineController.GeneralVendorPosition, destinationMapOverride: TownRoutineController.BaseMap);
                        return true;
                    }

                    if (Vector2.Distance(player.CellPosition, TownRoutineController.GeneralVendorPosition) > 2.0f)
                    {
                        if (!player.IsMoving && now - lastActionTime >= 0.25f)
                        {
                            navigation.NavigateTowards(player.CellPosition, TownRoutineController.GeneralVendorPosition, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                            lastActionTime = now;
                        }
                        return true;
                    }

                    State = DistributorState.InteractingWithTownNpc;
                    stepPhase = 0;
                    lastActionTime = now;
                    return true;

                case DistributorState.InteractingWithTownNpc:
                    currentState = BotState.DistributorRestocking;
                    return ProcessSellAndBuyAtNpc(netManager, player, cfg, now, isOverride: false, targetMap: targetMap);

                case DistributorState.NavigatingToWeaponDealer:
                    currentState = BotState.DistributorRestocking;
                    Vector2Int weaponDealerPos = new Vector2Int(60, 128);
                    string weaponDealerMap = "izlude_in";

                    if (!string.Equals(netManager.CurrentMap, weaponDealerMap, StringComparison.OrdinalIgnoreCase) ||
                        !MapNavMesh.Instance.IsReachable(player.CellPosition, weaponDealerPos))
                    {
                        var tState = BotState.TravelingToTargetMap;
                        navigation.ProcessTravel(netManager, player, now, ref tState, targetCellPos: weaponDealerPos, destinationMapOverride: weaponDealerMap);
                        return true;
                    }

                    if (Vector2.Distance(player.CellPosition, weaponDealerPos) > 2.5f)
                    {
                        if (!player.IsMoving && now - lastActionTime >= 0.25f)
                        {
                            navigation.NavigateTowards(player.CellPosition, weaponDealerPos, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                            lastActionTime = now;
                        }
                        return true;
                    }

                    // Arrived near Weapon Dealer
                    State = DistributorState.InteractingWithWeaponDealer;
                    stepPhase = 0;
                    lastActionTime = now;
                    BotEngine.Instance?.LogEvent("[Distributor] Arrived at Weapon Dealer (izlude_in 60, 128).");
                    return true;

                case DistributorState.InteractingWithWeaponDealer:
                    currentState = BotState.DistributorRestocking;
                    return ProcessWeaponDealerInteraction(netManager, player, cfg, now);

                case DistributorState.ReturningFromWeaponDealer:
                    currentState = BotState.DistributorRestocking;
                    Vector2Int postTargetPos = IsIzludeDistributor(netManager, targetMap) ? new Vector2Int(116, 76) : targetPost;
                    string postTargetMap = IsIzludeDistributor(netManager, targetMap) ? "izlude_in" : targetMap;

                    if (!string.Equals(netManager.CurrentMap, postTargetMap, StringComparison.OrdinalIgnoreCase) ||
                        !MapNavMesh.Instance.IsReachable(player.CellPosition, postTargetPos))
                    {
                        var tState = BotState.TravelingToTargetMap;
                        navigation.ProcessTravel(netManager, player, now, ref tState, targetCellPos: postTargetPos, destinationMapOverride: postTargetMap);
                        return true;
                    }

                    BotEngine.Instance?.LogEvent("[Distributor] Re-entered Tool Shop interior. Returning to vending post.");
                    State = DistributorState.NavigatingToPost;
                    stepPhase = 0;
                    lastActionTime = now;
                    return true;

                case DistributorState.NavigatingToKafra:
                    currentState = BotState.DistributorRestocking;
                    Vector2Int kafraPos;
                    string kafraMap;

                    if (TryFindLocalKafra(netManager, player, out _, out var lKafraPos))
                    {
                        kafraMap = netManager.CurrentMap;
                        kafraPos = lKafraPos;
                    }
                    else
                    {
                        kafraMap = TownRoutineController.BaseMap;
                        kafraPos = TownRoutineController.KafraPosition;
                    }

                    if (!string.Equals(netManager.CurrentMap, kafraMap, StringComparison.OrdinalIgnoreCase))
                    {
                        var tState = BotState.TravelingToTargetMap;
                        navigation.ProcessTravel(netManager, player, now, ref tState, targetCellPos: kafraPos, destinationMapOverride: kafraMap);
                        return true;
                    }

                    if (Vector2.Distance(player.CellPosition, kafraPos) > 2.5f)
                    {
                        if (!player.IsMoving && now - lastActionTime >= 0.25f)
                        {
                            navigation.NavigateTowards(player.CellPosition, kafraPos, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                            lastActionTime = now;
                        }
                        return true;
                    }

                    State = DistributorState.InteractingWithKafra;
                    stepPhase = 0;
                    lastActionTime = now;
                    return true;

                case DistributorState.InteractingWithKafra:
                    currentState = BotState.DistributorRestocking;
                    return ProcessKafraStorageOverflow(netManager, now);
            }

            return false;
        }

        private bool TryGetIncomingFleetDonor(NetworkManager netManager, ServerControllable player, out string donorName)
        {
            donorName = "";
            if (netManager == null || player == null) return false;

            string myProfile = ProfileManager.ActiveProfileName;
            var dirs = GetProfileDirectories();

            foreach (var dir in dirs)
            {
                try
                {
                    string pName = Path.GetFileName(dir);
                    if (string.Equals(pName, myProfile, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string statusPath = Path.Combine(dir, "bot_status.json");
                    if (!File.Exists(statusPath)) continue;

                    var writeTime = File.GetLastWriteTimeUtc(statusPath);
                    if (DateTime.UtcNow - writeTime > TimeSpan.FromSeconds(15)) continue;

                    string json = ReadFileSafe(statusPath);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string currentMap = root.TryGetProperty("CurrentMap", out var cmProp) ? cmProp.GetString() ?? "" : "";
                    if (!string.Equals(currentMap, netManager.CurrentMap, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int x = root.TryGetProperty("PositionX", out var xProp) ? xProp.GetInt32() : 0;
                    int y = root.TryGetProperty("PositionY", out var yProp) ? yProp.GetInt32() : 0;
                    float dist = Vector2.Distance(player.CellPosition, new Vector2(x, y));
                    if (dist > 4.0f) continue;

                    string botState = root.TryGetProperty("BotState", out var bsProp) ? bsProp.GetString() ?? "" : "";
                    if (string.Equals(botState, "DonatingToDistributor", StringComparison.OrdinalIgnoreCase))
                    {
                        string charName = root.TryGetProperty("CharacterName", out var cnProp) ? cnProp.GetString() ?? pName : pName;
                        donorName = charName;
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private bool HasNearbyActiveBuyers(NetworkManager netManager, ServerControllable player)
        {
            if (netManager == null || player == null) return false;

            string myProfile = ProfileManager.ActiveProfileName;
            var dirs = GetProfileDirectories();

            foreach (var dir in dirs)
            {
                try
                {
                    string pName = Path.GetFileName(dir);
                    if (string.Equals(pName, myProfile, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string statusPath = Path.Combine(dir, "bot_status.json");
                    if (!File.Exists(statusPath)) continue;

                    var writeTime = File.GetLastWriteTimeUtc(statusPath);
                    if (DateTime.UtcNow - writeTime > TimeSpan.FromSeconds(15)) continue;

                    string json = ReadFileSafe(statusPath);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string currentMap = root.TryGetProperty("CurrentMap", out var cmProp) ? cmProp.GetString() ?? "" : "";
                    if (!string.Equals(currentMap, netManager.CurrentMap, StringComparison.OrdinalIgnoreCase))
                        continue;

                    int x = root.TryGetProperty("PositionX", out var xProp) ? xProp.GetInt32() : 0;
                    int y = root.TryGetProperty("PositionY", out var yProp) ? yProp.GetInt32() : 0;
                    float dist = Vector2.Distance(player.CellPosition, new Vector2(x, y));
                    if (dist > 4.0f) continue;

                    string botState = root.TryGetProperty("BotState", out var bsProp) ? bsProp.GetString() ?? "" : "";
                    if (string.Equals(botState, "BuyingFromDistributor", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(botState, "WaitingForDistributorVend", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private bool MoveInventoryStoreItemsToCart(NetworkManager netManager)
        {
            if (!InventoryHelper.TryGetInventoryData(out var inv)) return false;

            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item.ItemData == null || item.Count <= 0) continue;

                // Move items designated as "Store" or non-consumables into Cart
                string disp = TownRoutineController.GetItemDisposition(item);
                if (disp == "Store")
                {
                    netManager.CartItemInteraction(CartInteractionType.InventoryToCart, item.BagSlotId, item.Count);
                    BotEngine.Instance?.LogEvent($"[Distributor] Stored {item.Count}x {item.ItemData.Name} into Cart.");
                    return true;
                }
            }
            return false;
        }

        private bool MoveVendConsumablesToCart(NetworkManager netManager, BotConfigData cfg)
        {
            if (cfg == null || cfg.VendConsumables == null || cfg.VendConsumables.Count == 0) return false;
            if (!InventoryHelper.TryGetInventoryData(out var inv)) return false;

            foreach (var kvp in inv)
            {
                var item = kvp.Value;
                if (item.ItemData == null || item.Count <= 0) continue;

                string norm = item.ItemData.Name.Replace(' ', '_');
                bool isVendConsumable = cfg.VendConsumables.Contains(norm) || cfg.VendConsumables.Contains(item.ItemData.Name);
                if (isVendConsumable)
                {
                    // Keep 1 Butterfly Wing in inventory bag for emergency return to town
                    int countToMove = item.Count;
                    if ((norm.Equals("Butterfly_Wing", StringComparison.OrdinalIgnoreCase) || item.ItemData.Name.Equals("Butterfly Wing", StringComparison.OrdinalIgnoreCase)) && countToMove > 1)
                    {
                        countToMove = item.Count - 1;
                    }

                    if (countToMove > 0)
                    {
                        netManager.CartItemInteraction(CartInteractionType.InventoryToCart, item.BagSlotId, countToMove);
                        BotEngine.Instance?.LogEvent($"[Distributor] Moved {countToMove}x {item.ItemData.Name} into Cart for vending.");
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool TryFindLocalShopNpc(NetworkManager netManager, ServerControllable player, HashSet<int> visitedIds, out int npcId, out Vector2Int position)
        {
            npcId = -1;
            position = Vector2Int.zero;
            if (netManager?.EntityList == null) return false;

            float closestDist = float.MaxValue;
            foreach (var kvp in netManager.EntityList)
            {
                var ent = kvp.Value;
                if (ent == null || ent.CharacterType != CharacterType.NPC) continue;
                if (visitedIds != null && visitedIds.Contains(ent.Id)) continue;
                if (!MapNavMesh.Instance.IsReachable(player.CellPosition, ent.CellPosition)) continue;

                string name = ent.Name ?? "";
                if (name.IndexOf("Tool Dealer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Weapon Dealer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Dealer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Vendor", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Merchant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Trader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Sundries", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("General", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    float dist = Vector2.Distance(player.CellPosition, ent.CellPosition);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        npcId = ent.Id;
                        position = ent.CellPosition;
                    }
                }
            }

            return npcId != -1;
        }

        private static bool TryFindLocalShopNpc(NetworkManager netManager, ServerControllable player, out int npcId, out Vector2Int position)
        {
            return TryFindLocalShopNpc(netManager, player, null, out npcId, out position);
        }

        private static bool TryFindLocalKafra(NetworkManager netManager, ServerControllable player, out int kafraId, out Vector2Int position)
        {
            kafraId = -1;
            position = Vector2Int.zero;
            if (netManager?.EntityList == null) return false;

            float closestDist = float.MaxValue;
            foreach (var kvp in netManager.EntityList)
            {
                var ent = kvp.Value;
                if (ent == null || ent.CharacterType != CharacterType.NPC) continue;

                string name = ent.Name ?? "";
                if (name.IndexOf("Kafra", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    float dist = Vector2.Distance(player.CellPosition, ent.CellPosition);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        kafraId = ent.Id;
                        position = ent.CellPosition;
                    }
                }
            }

            return kafraId != -1;
        }

        private static bool IsTooCloseToNpc(NetworkManager netManager, ServerControllable player, float minDistance, out string npcName, out Vector2Int npcPos)
        {
            npcName = "";
            npcPos = Vector2Int.zero;
            if (netManager?.EntityList == null) return false;

            int checkDist = Mathf.CeilToInt(minDistance);
            foreach (var kvp in netManager.EntityList)
            {
                var ent = kvp.Value;
                if (ent == null || ent.CharacterType != CharacterType.NPC) continue;

                int dx = Mathf.Abs(player.CellPosition.x - ent.CellPosition.x);
                int dy = Mathf.Abs(player.CellPosition.y - ent.CellPosition.y);
                int chebyshevDist = Mathf.Max(dx, dy);

                if (chebyshevDist <= checkDist)
                {
                    npcName = ent.Name ?? "NPC";
                    npcPos = ent.CellPosition;
                    return true;
                }
            }

            return false;
        }

        public bool NeedsConsumableRestock(BotConfigData cfg, bool ignoreUnstocked = false)
        {
            if (cfg == null || cfg.VendConsumables == null || cfg.VendConsumables.Count == 0) return false;
            var cart = GetCartItems();
            InventoryHelper.TryGetInventoryData(out var inv);

            foreach (var consumableName in cfg.VendConsumables)
            {
                if (ignoreUnstocked && unstockedConsumables.Contains(consumableName))
                    continue;

                int cartCount = GetTotalCountInCart(cart, consumableName);
                int invCount = inv != null ? TownRoutineController.GetCurrentSupplyCount(consumableName, inv) : 0;
                int total = cartCount + invCount;
                int minStock = cfg.GetMinVendStock(consumableName);
                if (total < minStock || total == 0)
                    return true;
            }
            return false;
        }

        public static bool IsWeaponDealerItem(string itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return false;
            string norm = itemName.Replace('_', ' ');
            return norm.IndexOf("Arrow", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public bool NeedsToolDealerRestock(BotConfigData cfg)
        {
            if (cfg == null || cfg.VendConsumables == null) return false;
            var cart = GetCartItems();
            InventoryHelper.TryGetInventoryData(out var inv);

            foreach (var consumableName in cfg.VendConsumables)
            {
                if (IsWeaponDealerItem(consumableName)) continue;
                if (unstockedConsumables.Contains(consumableName)) continue;

                int cartCount = GetTotalCountInCart(cart, consumableName);
                int invCount = inv != null ? TownRoutineController.GetCurrentSupplyCount(consumableName, inv) : 0;
                int total = cartCount + invCount;
                int minStock = cfg.GetMinVendStock(consumableName);
                if (total < minStock || total == 0)
                    return true;
            }
            return false;
        }

        public bool NeedsWeaponDealerRestock(BotConfigData cfg)
        {
            if (cfg == null || cfg.VendConsumables == null) return false;
            var cart = GetCartItems();
            InventoryHelper.TryGetInventoryData(out var inv);

            foreach (var consumableName in cfg.VendConsumables)
            {
                if (!IsWeaponDealerItem(consumableName)) continue;

                int cartCount = GetTotalCountInCart(cart, consumableName);
                int invCount = inv != null ? TownRoutineController.GetCurrentSupplyCount(consumableName, inv) : 0;
                int total = cartCount + invCount;
                int minStock = cfg.GetMinVendStock(consumableName);
                if (total < minStock || total == 0)
                    return true;
            }
            return false;
        }

        private static bool IsIzludeDistributor(NetworkManager netManager, string targetMap)
        {
            if (string.Equals(targetMap, "izlude", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetMap, "izlude_in", StringComparison.OrdinalIgnoreCase))
                return true;

            if (netManager != null && (string.Equals(netManager.CurrentMap, "izlude", StringComparison.OrdinalIgnoreCase) ||
                                       string.Equals(netManager.CurrentMap, "izlude_in", StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        private void RecordUnfulfilledConsumables(BotConfigData cfg)
        {
            var cart = GetCartItems();
            InventoryHelper.TryGetInventoryData(out var inv);

            foreach (var consumableName in cfg.VendConsumables)
            {
                int cartCount = GetTotalCountInCart(cart, consumableName);
                int invCount = inv != null ? TownRoutineController.GetCurrentSupplyCount(consumableName, inv) : 0;
                int total = cartCount + invCount;
                int minStock = cfg.GetMinVendStock(consumableName);
                if (total < minStock || total == 0)
                {
                    unstockedConsumables.Add(consumableName);
                }
            }
        }

        public static bool IsCartOverweightThreshold()
        {
            var state = PlayerState.Instance;
            if (state == null) return false;
            // Max capacity is 80,000 weight. 90% is 72,000.
            return state.CartWeight >= 72000;
        }

        private static Dictionary<int, InventoryItem> GetCartItems()
        {
            var result = new Dictionary<int, InventoryItem>();
            var state = PlayerState.Instance;
            if (state?.Cart == null) return result;
            var data = state.Cart.GetInventoryData();
            if (data == null) return result;
            foreach (var kvp in data)
            {
                result[kvp.Key] = kvp.Value;
            }
            return result;
        }

        private static int GetTotalCountInCart(Dictionary<int, InventoryItem> cart, string consumableName)
        {
            int total = 0;
            string norm = consumableName.Replace('_', ' ');
            foreach (var kvp in cart)
            {
                var item = kvp.Value;
                if (item.ItemData == null) continue;
                if (string.Equals(item.ItemData.Name, norm, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(item.ItemData.Name, consumableName, StringComparison.OrdinalIgnoreCase))
                {
                    total += item.Count;
                }
            }
            return total;
        }

        private List<(int bagId, int count, int price)> BuildVendingItemsList(BotConfigData cfg)
        {
            var list = new List<(int bagId, int count, int price)>();
            var cart = GetCartItems();

            int maxSlots = 3; // Level 1 Vending: 1 + 2 = 3 slots
            if (PlayerState.Instance != null && PlayerState.Instance.KnownSkills.TryGetValue(CharacterSkill.Vending, out int vendLvl))
            {
                maxSlots = vendLvl + 2;
            }

            foreach (var consumableName in cfg.VendConsumables)
            {
                if (list.Count >= maxSlots) break;

                string norm = consumableName.Replace('_', ' ');
                int target = cfg.GetTargetVendStock(consumableName);
                foreach (var kvp in cart)
                {
                    var item = kvp.Value;
                    if (item.ItemData == null || item.Count <= 0) continue;
                    if (string.Equals(item.ItemData.Name, norm, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(item.ItemData.Name, consumableName, StringComparison.OrdinalIgnoreCase))
                    {
                        int vendCount = Mathf.Min(item.Count, target);
                        list.Add((kvp.Key, vendCount, 1)); // kvp.Key is cart slot id, 1 Zeny per unit for fleet peers
                        break;
                    }
                }
            }
            return list;
        }

        private static void SendVendingStartPacket(NetworkManager netManager, string shopName, List<(int bagId, int count, int price)> items)
        {
            var msg = netManager.StartMessage(PacketType.VendingStart);
            msg.Write(shopName);
            msg.Write(items.Count);
            foreach (var it in items)
            {
                msg.Write(it.bagId);
                msg.Write(it.count);
                msg.Write(it.price);
            }
            netManager.SendMessage(msg);
        }

        private bool ProcessSellAndBuyAtNpc(NetworkManager netManager, ServerControllable player, BotConfigData cfg, float now, bool isOverride, string targetMap = null)
        {
            var cam = CameraFollower.Instance;
            if (cam == null) return false;

            if (stepPhase == 0)
            {
                // Click NPC
                int targetNpcId = -1;
                Vector2Int targetPos;

                if (isOverride)
                {
                    targetPos = new Vector2Int(cfg.DistributorOverrideNpcX, cfg.DistributorOverrideNpcY);
                }
                else if (TryFindLocalShopNpc(netManager, player, visitedShopNpcIds, out var localId, out var localPos))
                {
                    targetPos = localPos;
                }
                else
                {
                    targetPos = TownRoutineController.GeneralVendorPosition;
                }

                foreach (var kvp in netManager.EntityList)
                {
                    var ent = kvp.Value;
                    if (ent != null && ent.CharacterType == CharacterType.NPC && ent.CellPosition == targetPos)
                    {
                        targetNpcId = ent.Id;
                        break;
                    }
                }

                if (targetNpcId != -1)
                {
                    currentShopNpcId = targetNpcId;
                    netManager.SendNpcClick(targetNpcId);
                    stepPhase = 1;
                    lastActionTime = now;
                    BotEngine.Instance?.LogEvent($"[Distributor] Clicked {(isOverride ? "Override" : "Shop")} Vendor NPC (ID: {targetNpcId}).");
                }
                else if (now - lastActionTime >= 3.0f)
                {
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 1)
            {
                bool dialogOpen = cam != null && cam.DialogPanel != null && cam.DialogPanel.activeSelf;
                bool optionsOpen = cam != null && cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;

                if (optionsOpen)
                {
                    if (now - lastActionTime < 0.4f) return true;

                    var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(true);
                    bool hasLootToSell = TownRoutineController.HasItemsToSell();
                    string targetKeyword = hasLootToSell ? "Sell" : "Buy";

                    NpcOptionButton targetBtn = null;
                    if (buttons != null)
                    {
                        foreach (var btn in buttons)
                        {
                            if (btn?.TextBox != null && btn.TextBox.text.IndexOf(targetKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                targetBtn = btn;
                                break;
                            }
                        }
                    }

                    if (targetBtn != null)
                    {
                        targetBtn.OnClick();
                    }
                    else
                    {
                        cam.NpcOptionPanel.SetActive(false);
                        netManager.SendNpcSelectOption(hasLootToSell ? 1 : 0);
                    }

                    stepPhase = hasLootToSell ? 2 : 3;
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
                // Phase 2: Execute Sell
                if (ShopUI.Instance != null || now - lastActionTime >= 0.6f)
                {
                    if (InventoryHelper.TryGetInventoryData(out var inv))
                    {
                        var itemsToSell = new List<(int bagId, int count)>();
                        foreach (var kvp in inv)
                        {
                            var item = kvp.Value;
                            if (item?.ItemData != null && item.Count > 0 && TownRoutineController.GetItemDisposition(item) == "Sell")
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
                            BotEngine.Instance?.LogEvent($"[Distributor] Sold {itemsToSell.Count} loot stacks to NPC.");
                        }
                    }

                    stepPhase = 21;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 21)
            {
                if (now - lastActionTime < 0.6f) return true;

                TownRoutineController.CloseOpenShopUI();
                NpcInteractionHelper.CleanupNpcUi();

                // If we also need to restock tool dealer consumables, click the NPC again to Buy
                if (NeedsToolDealerRestock(cfg))
                {
                    stepPhase = 0;
                    lastActionTime = now + 0.5f;
                    return true;
                }

                stepPhase = 4;
                lastActionTime = now;
            }
            else if (stepPhase == 3)
            {
                // Phase 3: Execute Buy for needed VendConsumables
                if (now - lastActionTime < 0.4f) return true;

                if (ShopUI.Instance != null || now - lastActionTime >= 1.0f)
                {
                    var cart = GetCartItems();
                    var buys = new List<(int itemId, int count)>();

                    // Inspect available items for sale in the open shop UI
                    var shopItemsForSale = new HashSet<int>();
                    if (ShopUI.Instance != null && ShopUI.Instance.LeftSideItems != null)
                    {
                        foreach (var entry in ShopUI.Instance.LeftSideItems)
                        {
                            shopItemsForSale.Add(entry.Value.ItemId);
                        }
                    }

                    if (InventoryHelper.TryGetInventoryData(out var inv))
                    {
                        foreach (var consumableName in cfg.VendConsumables)
                        {
                            int cartCount = GetTotalCountInCart(cart, consumableName);
                            int invCount = inv != null ? TownRoutineController.GetCurrentSupplyCount(consumableName, inv) : 0;
                            int totalCurrent = cartCount + invCount;
                            int target = cfg.GetTargetVendStock(consumableName);
                            int minStock = cfg.GetMinVendStock(consumableName);

                            if (totalCurrent < minStock || totalCurrent < target)
                            {
                                int needed = target - totalCurrent;
                                if (needed > 0 && TownRoutineController.TryGetRestockItem(consumableName, out var def))
                                {
                                    // Only attempt to purchase if the current NPC actually sells this item
                                    if (shopItemsForSale.Count == 0 || shopItemsForSale.Contains(def.ItemId))
                                    {
                                        buys.Add((def.ItemId, needed));
                                    }
                                }
                            }
                        }
                    }

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
                        BotEngine.Instance?.LogEvent($"[Distributor] Purchased {buys.Count} consumable stack(s) from shop NPC for vending restock.");
                    }
                    else
                    {
                        netManager.SubmitShopPurchase(null);
                        BotEngine.Instance?.LogEvent("[Distributor] Current vendor does not stock any remaining needed consumables.");
                    }

                    stepPhase = 31;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 31)
            {
                if (now - lastActionTime < 0.6f) return true;

                TownRoutineController.CloseOpenShopUI();
                NpcInteractionHelper.CleanupNpcUi();
                stepPhase = 4;
                lastActionTime = now;
            }
            else if (stepPhase == 4)
            {
                // Phase 4: Move newly purchased consumables into Cart and check remaining needs
                MoveVendConsumablesToCart(netManager, cfg);

                if (currentShopNpcId != -1)
                {
                    visitedShopNpcIds.Add(currentShopNpcId);
                }

                if (NeedsConsumableRestock(cfg))
                {
                    // Check if there is another unvisited local shop NPC on this map
                    if (!isOverride && TryFindLocalShopNpc(netManager, player, visitedShopNpcIds, out _, out var nextNpcPos))
                    {
                        BotEngine.Instance?.LogEvent("[Distributor] Some consumables still unfulfilled. Visiting next local dealer...");
                        stepPhase = 0;
                        lastActionTime = now + 0.5f;
                        return true;
                    }
                    else if (isOverride && !hasCheckedOverrideNpc)
                    {
                        hasCheckedOverrideNpc = true;
                        visitedShopNpcIds.Clear();
                        BotEngine.Instance?.LogEvent("[Distributor] Override NPC missing some consumables. Falling back to default town NPCs.");
                        State = DistributorState.NavigatingToTownNpc;
                        stepPhase = 0;
                        lastActionTime = now;
                        return true;
                    }

                    // Check if Silver Arrows (or weapon dealer items) are needed and we are in Izlude
                    if (NeedsWeaponDealerRestock(cfg) && IsIzludeDistributor(netManager, targetMap))
                    {
                        visitedShopNpcIds.Clear();
                        currentShopNpcId = -1;
                        BotEngine.Instance?.LogEvent("[Distributor] Tool shop completed. Silver arrows still needed. Proceeding to Weapon Dealer across town plaza.");
                        State = DistributorState.NavigatingToWeaponDealer;
                        stepPhase = 0;
                        lastActionTime = now;
                        return true;
                    }

                    // All local shop NPCs on this map have been checked, but some consumables cannot be bought here!
                    RecordUnfulfilledConsumables(cfg);
                    restockCooldownUntil = now + 120.0f; // 2 minute cooldown
                    BotEngine.Instance?.LogEvent($"[Distributor] Local shops checked. Unstocked items ({string.Join(", ", unstockedConsumables)}) cannot be bought here. Pausing restock for 2m.");
                }

                // Done buying/selling; navigate back to post
                visitedShopNpcIds.Clear();
                currentShopNpcId = -1;
                State = DistributorState.NavigatingToPost;
                stepPhase = 0;
                lastActionTime = now;
                return true;
            }

            return true;
        }

        private bool ProcessWeaponDealerInteraction(NetworkManager netManager, ServerControllable player, BotConfigData cfg, float now)
        {
            var cam = CameraFollower.Instance;
            if (cam == null) return false;

            if (stepPhase == 0)
            {
                NpcInteractionHelper.CleanupNpcUi();
                int targetNpcId = -1;
                Vector2Int dealerPos = new Vector2Int(60, 128);

                foreach (var kvp in netManager.EntityList)
                {
                    var ent = kvp.Value;
                    if (ent != null && ent.CharacterType == CharacterType.NPC)
                    {
                        if (ent.CellPosition == dealerPos || (ent.CellPosition.x >= 58 && ent.CellPosition.x <= 62 && ent.CellPosition.y >= 126 && ent.CellPosition.y <= 130))
                        {
                            targetNpcId = ent.Id;
                            break;
                        }
                    }
                }

                if (targetNpcId != -1)
                {
                    netManager.SendNpcClick(targetNpcId);
                    stepPhase = 1;
                    lastActionTime = now;
                    BotEngine.Instance?.LogEvent($"[Distributor] Clicked Weapon Dealer NPC (ID: {targetNpcId}).");
                }
                else if (now - lastActionTime >= 3.0f)
                {
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 1)
            {
                bool dialogOpen = cam.DialogPanel != null && cam.DialogPanel.activeSelf;
                bool optionsOpen = cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;

                if (optionsOpen)
                {
                    if (now - lastActionTime < 0.4f) return true;

                    var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(true);
                    NpcOptionButton buyBtn = null;
                    if (buttons != null)
                    {
                        foreach (var btn in buttons)
                        {
                            if (btn?.TextBox != null && btn.TextBox.text.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0)
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
                        netManager.SendNpcSelectOption(0);
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
                if (now - lastActionTime < 0.4f) return true;

                if (ShopUI.Instance != null || now - lastActionTime >= 1.0f)
                {
                    var cart = GetCartItems();
                    var buys = new List<(int itemId, int count)>();

                    var shopItemsForSale = new HashSet<int>();
                    if (ShopUI.Instance != null && ShopUI.Instance.LeftSideItems != null)
                    {
                        foreach (var entry in ShopUI.Instance.LeftSideItems)
                        {
                            shopItemsForSale.Add(entry.Value.ItemId);
                        }
                    }

                    if (InventoryHelper.TryGetInventoryData(out var inv))
                    {
                        foreach (var consumableName in cfg.VendConsumables)
                        {
                            if (!IsWeaponDealerItem(consumableName)) continue;

                            int cartCount = GetTotalCountInCart(cart, consumableName);
                            int invCount = inv != null ? TownRoutineController.GetCurrentSupplyCount(consumableName, inv) : 0;
                            int totalCurrent = cartCount + invCount;
                            int target = cfg.GetTargetVendStock(consumableName);

                            if (totalCurrent < target)
                            {
                                int needed = target - totalCurrent;
                                if (needed > 0 && TownRoutineController.TryGetRestockItem(consumableName, out var def))
                                {
                                    if (shopItemsForSale.Count == 0 || shopItemsForSale.Contains(def.ItemId))
                                    {
                                        buys.Add((def.ItemId, needed));
                                    }
                                }
                            }
                        }
                    }

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
                        BotEngine.Instance?.LogEvent($"[Distributor] Purchased {buys.Count} item stack(s) from Weapon Dealer for vending restock.");
                    }
                    else
                    {
                        netManager.SubmitShopPurchase(null);
                        BotEngine.Instance?.LogEvent("[Distributor] No additional arrows needed from Weapon Dealer.");
                    }

                    stepPhase = 21;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 21)
            {
                if (now - lastActionTime < 0.6f) return true;

                TownRoutineController.CloseOpenShopUI();
                NpcInteractionHelper.CleanupNpcUi();
                stepPhase = 3;
                lastActionTime = now;
            }
            else if (stepPhase == 3)
            {
                MoveVendConsumablesToCart(netManager, cfg);
                BotEngine.Instance?.LogEvent("[Distributor] Transferred arrows into pushcart. Returning to Tool Shop post.");
                State = DistributorState.ReturningFromWeaponDealer;
                stepPhase = 0;
                lastActionTime = now;
                return true;
            }

            return true;
        }

        private bool ProcessKafraStorageOverflow(NetworkManager netManager, float now)
        {
            var cam = CameraFollower.Instance;
            if (cam == null) return false;

            bool isRentingCart = PlayerState.Instance != null && !PlayerState.Instance.HasCart;

            if (stepPhase == 0)
            {
                // Click Kafra NPC
                int kafraId = -1;
                Vector2Int kafraPos = TryFindLocalKafra(netManager, cam.TargetControllable, out var localId, out var localPos)
                    ? localPos
                    : TownRoutineController.KafraPosition;

                foreach (var kvp in netManager.EntityList)
                {
                    var ent = kvp.Value;
                    if (ent != null && ent.CharacterType == CharacterType.NPC && ent.CellPosition == kafraPos)
                    {
                        kafraId = ent.Id;
                        break;
                    }
                }

                if (kafraId != -1)
                {
                    netManager.SendNpcClick(kafraId);
                    stepPhase = 1;
                    lastActionTime = now;
                    BotEngine.Instance?.LogEvent($"[Distributor] Clicked Kafra (ID: {kafraId}, isRentingCart: {isRentingCart}).");
                }
                else if (now - lastActionTime >= 3.0f)
                {
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 1)
            {
                // Select Option
                bool optionsOpen = cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;
                if (optionsOpen)
                {
                    var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(true);
                    NpcOptionButton targetBtn = null;

                    if (isRentingCart)
                    {
                        // Look for "Rent Push Cart" / "Cart"
                        if (buttons != null)
                        {
                            foreach (var btn in buttons)
                            {
                                if (btn?.TextBox != null && btn.TextBox.text.IndexOf("Cart", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    targetBtn = btn;
                                    break;
                                }
                            }
                        }

                        if (targetBtn != null) targetBtn.OnClick();
                        else netManager.SendNpcSelectOption(3); // Option 3 is "Rent Push Cart" in standard Kafra script

                        stepPhase = 10; // Wait for Yes/No prompt
                        lastActionTime = now;
                        BotEngine.Instance?.LogEvent("[Distributor] Selected 'Rent Push Cart' option.");
                    }
                    else
                    {
                        // Look for "Storage"
                        if (buttons != null)
                        {
                            foreach (var btn in buttons)
                            {
                                if (btn?.TextBox != null && btn.TextBox.text.IndexOf("Storage", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    targetBtn = btn;
                                    break;
                                }
                            }
                        }

                        if (targetBtn != null) targetBtn.OnClick();
                        else netManager.SendNpcSelectOption(1); // Option 1 is "Use Storage"

                        stepPhase = 2;
                        lastActionTime = now;
                        BotEngine.Instance?.LogEvent("[Distributor] Selected 'Use Storage' option.");
                    }
                }
                else if (now - lastActionTime >= 3.0f)
                {
                    stepPhase = 0;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 10)
            {
                // Confirmation dialog: "The fee to rent a push cart is 850 zeny. Is that alright?"
                bool optionsOpen = cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;
                if (optionsOpen)
                {
                    var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(true);
                    NpcOptionButton yesBtn = null;
                    if (buttons != null)
                    {
                        foreach (var btn in buttons)
                        {
                            if (btn?.TextBox != null && btn.TextBox.text.IndexOf("Yes", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                yesBtn = btn;
                                break;
                            }
                        }
                    }

                    if (yesBtn != null) yesBtn.OnClick();
                    else netManager.SendNpcSelectOption(0); // Option 0 is "Yes"

                    stepPhase = 11;
                    lastActionTime = now + 1.0f;
                    BotEngine.Instance?.LogEvent("[Distributor] Confirmed pushcart rental fee.");
                }
                else if (cam.DialogPanel != null && cam.DialogPanel.activeSelf && now - lastActionTime >= 0.7f)
                {
                    netManager.SendNpcAdvance();
                    lastActionTime = now;
                }
                else if (now - lastActionTime >= 3.0f)
                {
                    stepPhase = 0;
                    lastActionTime = now;
                }
            }
            else if (stepPhase == 11)
            {
                // Cart rented! Close UI and head back to post
                NpcInteractionHelper.CleanupNpcUi();
                BotEngine.Instance?.LogEvent("[Distributor] Pushcart successfully rented. Navigating back to post.");
                State = DistributorState.NavigatingToPost;
                stepPhase = 0;
                lastActionTime = now;
                return true;
            }
            else if (stepPhase == 2)
            {
                // Transfer non-vend items from Cart to Bag, and deposit into Kafra
                if (StorageUI.Instance != null || now - lastActionTime >= 0.8f)
                {
                    var cart = GetCartItems();
                    var cfg = BotConfigManager.Current;
                    int deposited = 0;

                    foreach (var kvp in cart)
                    {
                        var item = kvp.Value;
                        if (item.ItemData == null || item.Count <= 0) continue;

                        string norm = item.ItemData.Name.Replace(' ', '_');
                        bool isVendConsumable = cfg.VendConsumables.Contains(norm) || cfg.VendConsumables.Contains(item.ItemData.Name);

                        if (!isVendConsumable)
                        {
                            // Move from Cart to Inventory
                            netManager.CartItemInteraction(CartInteractionType.CartToInventory, item.BagSlotId, item.Count);
                            // Then deposit from Inventory to Storage
                            netManager.SendMoveStorageItem(item.BagSlotId, item.Count, true);
                            deposited++;
                        }
                    }

                    netManager.SendEndStorage();
                    TownRoutineController.CloseOpenStorageUI();
                    BotEngine.Instance?.LogEvent($"[Distributor] Deposited {deposited} overflow item stack(s) from Cart into Kafra Storage.");

                    State = DistributorState.NavigatingToPost;
                    stepPhase = 0;
                    lastActionTime = now + 0.5f;
                    return true;
                }
            }

            return true;
        }
    }
}
