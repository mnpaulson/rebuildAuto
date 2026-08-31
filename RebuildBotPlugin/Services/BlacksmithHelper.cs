using System;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.Sprites;
using RebuildBotPlugin.Controllers;
using RebuildBotPlugin.Models;
using RebuildSharedData.ClientTypes;
using RebuildSharedData.Data;
using RebuildSharedData.Enum;
using RebuildSharedData.Enum.EntityStats;
using RebuildSharedData.Networking;
using UnityEngine;

namespace RebuildBotPlugin.Services
{
    /// <summary>
    /// Encapsulates all Blacksmith domain logic, costs, and state machine interactions:
    /// - Inter-map navigation to prt_in (Prontera Blacksmith)
    /// - Zeny & material pre-validation before embarking
    /// - Autonomous purchase of required ores (Phracon / Emveretarcon) from Vurewell
    /// - Dialogue & refinement execution with Hollgrehenn
    /// </summary>
    public class BlacksmithHelper
    {
        public const string BlacksmithMap = "prt_in";
        public static readonly Vector2Int HollgrehennPos = new Vector2Int(63, 60);
        public static readonly Vector2Int VurewellPos = new Vector2Int(56, 68);
        public static readonly Vector2Int DietrichPos = new Vector2Int(63, 69);

        public enum UpgradePhase
        {
            Idle,
            Validating,
            TravelingToBlacksmith,
            BuyingOres,
            TalkingToHollgrehenn,
            ExecutingRefine,
            Completed,
            Failed
        }

        public UpgradePhase CurrentPhase { get; private set; } = UpgradePhase.Idle;
        public string StatusMessage { get; private set; } = "";

        private readonly NpcInteractionHelper npcHelper = new();
        private float lastStepTime = 0f;
        private int internalStep = 0;
        private int targetBagSlotId = -1;
        private string targetItemName = "";
        private int targetRefineLevel = 4;
        private bool stopAtSafeLimit = true;
        private int currentRefineLevel = 0;
        private int requiredOreId = 1010;
        private int missingOreCount = 0;

        /// <summary>
        /// Get the Zeny fee charged by Hollgrehenn per refine attempt.
        /// </summary>
        public static int GetRefineFee(ItemData dat)
        {
            if (dat == null) return 200;
            if (dat.ItemClass == ItemClass.Equipment) return 2000; // Armor: 2000z
            if (dat.ItemClass == ItemClass.Weapon)
            {
                switch (dat.ItemRank)
                {
                    case 1: return 200;   // Level 1 weapon: 200z
                    case 2: return 1000;  // Level 2 weapon: 1000z
                    case 3: return 5000;  // Level 3 weapon: 5000z
                    case 4: return 10000; // Level 4 weapon: 10000z
                }
            }
            return 200;
        }

        /// <summary>
        /// Get the NPC purchase price for basic ores (Phracon / Emveretarcon).
        /// Returns 0 for non-purchasable ores (Oridecon / Elunium).
        /// </summary>
        public static int GetOreBuyPrice(int oreId)
        {
            switch (oreId)
            {
                case 1010: return 200;  // Phracon
                case 1011: return 1000; // Emveretarcon
                default: return 0;      // Oridecon / Elunium (Not sold by standard NPC)
            }
        }

        /// <summary>
        /// Get the player's current total Zeny.
        /// </summary>
        public static int GetCurrentZeny()
        {
            var state = PlayerState.Instance;
            if (state == null) return 0;
            return state.GetData(PlayerStat.Zeny);
        }

        /// <summary>
        /// Calculate total costs (refine fees + ore purchases) and verify affordability.
        /// </summary>
        public static bool ValidateUpgradeAffordability(
            ItemData itemData,
            int currentRefine,
            int targetRefine,
            int ownedOreCount,
            out int totalZenyRequired,
            out int refineFees,
            out int oreCosts,
            out int missingOres,
            out string validationError)
        {
            totalZenyRequired = 0;
            refineFees = 0;
            oreCosts = 0;
            missingOres = 0;
            validationError = null;

            int attemptsNeeded = Math.Max(0, targetRefine - currentRefine);
            if (attemptsNeeded <= 0)
            {
                validationError = $"Item is already at or above target refine level (+{currentRefine} >= +{targetRefine}).";
                return false;
            }

            int feePerAttempt = GetRefineFee(itemData);
            refineFees = attemptsNeeded * feePerAttempt;

            int oreId = EquipmentController.GetRefineOreId(itemData);
            missingOres = Math.Max(0, attemptsNeeded - ownedOreCount);

            if (missingOres > 0)
            {
                int orePrice = GetOreBuyPrice(oreId);
                if (orePrice <= 0)
                {
                    // Oridecon or Elunium cannot be bought directly from NPC
                    validationError = $"Missing {missingOres}x {(oreId == 984 ? "Oridecon" : "Elunium")}. These ores cannot be purchased from NPCs and must be gathered or traded.";
                    return false;
                }
                oreCosts = missingOres * orePrice;
            }

            totalZenyRequired = refineFees + oreCosts;
            int currentZeny = GetCurrentZeny();

            if (currentZeny < totalZenyRequired)
            {
                validationError = $"Insufficient Zeny: Need {totalZenyRequired:N0}z (Refine: {refineFees:N0}z, Ores: {oreCosts:N0}z), but character only has {currentZeny:N0}z.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Start an autonomous Blacksmith upgrade sequence.
        /// </summary>
        public bool Begin(string itemName, int targetLevel, bool safeLimitOnly, out string error)
        {
            error = null;
            targetItemName = itemName;
            targetRefineLevel = targetLevel;
            stopAtSafeLimit = safeLimitOnly;
            internalStep = 0;
            lastStepTime = Time.time;
            npcHelper.Reset();

            if (!EquipmentController.TryFindItemInInventory(itemName, -1, out var gearItem) || gearItem.ItemData == null)
            {
                error = $"Item '{itemName}' not found in inventory.";
                CurrentPhase = UpgradePhase.Failed;
                StatusMessage = error;
                return false;
            }

            targetBagSlotId = gearItem.BagSlotId;
            currentRefineLevel = gearItem.Type == ItemType.UniqueItem ? (int)gearItem.UniqueItem.Refine : 0;
            requiredOreId = EquipmentController.GetRefineOreId(gearItem.ItemData);

            if (stopAtSafeLimit)
            {
                int safeLimit = EquipmentController.GetRefineSafeLimit(gearItem.ItemData);
                if (targetRefineLevel > safeLimit)
                {
                    targetRefineLevel = safeLimit;
                }
            }

            if (currentRefineLevel >= targetRefineLevel)
            {
                error = $"'{itemName}' is already at target refine level +{currentRefineLevel}.";
                CurrentPhase = UpgradePhase.Completed;
                StatusMessage = error;
                return false;
            }

            // Count owned ores
            int ownedOres = 0;
            if (InventoryHelper.TryGetInventoryData(out var inv) && inv != null)
            {
                foreach (var kvp in inv)
                {
                    if (kvp.Value != null && kvp.Value.Id == requiredOreId && kvp.Value.Count > 0)
                    {
                        ownedOres += kvp.Value.Count;
                    }
                }
            }

            // Pre-validation: Zeny & Material affordability
            if (!ValidateUpgradeAffordability(
                    gearItem.ItemData,
                    currentRefineLevel,
                    targetRefineLevel,
                    ownedOres,
                    out int totalZeny,
                    out int refineFees,
                    out int oreCosts,
                    out missingOreCount,
                    out error))
            {
                CurrentPhase = UpgradePhase.Failed;
                StatusMessage = error;
                return false;
            }

            CurrentPhase = UpgradePhase.TravelingToBlacksmith;
            StatusMessage = $"Pre-check passed (Cost: {totalZeny:N0}z). Traveling to Prontera Blacksmith...";
            BotEngine.Instance?.LogEvent($"[Blacksmith] {StatusMessage}");
            return true;
        }

        /// <summary>
        /// Tick the multi-step upgrade state machine.
        /// Returns true if still in progress, false if finished.
        /// </summary>
        public bool Process(BotEngine bot, float now)
        {
            var netManager = NetworkManager.Instance;
            var player = bot.Player;
            if (netManager == null || player == null) return true;

            switch (CurrentPhase)
            {
                case UpgradePhase.TravelingToBlacksmith:
                    // 1. Check if arrived on prt_in
                    if (string.Equals(netManager.CurrentMap, BlacksmithMap, StringComparison.OrdinalIgnoreCase))
                    {
                        // Check if we need to buy ores first or go directly to Hollgrehenn
                        if (missingOreCount > 0)
                        {
                            CurrentPhase = UpgradePhase.BuyingOres;
                            npcHelper.Reset();
                            npcHelper.Begin("Vurewell", VurewellPos);
                            internalStep = 0;
                            lastStepTime = now;
                            BotEngine.Instance?.LogEvent($"[Blacksmith] Arrived in prt_in. Visiting Vurewell to buy {missingOreCount}x ore (ID: {requiredOreId}).");
                        }
                        else
                        {
                            CurrentPhase = UpgradePhase.TalkingToHollgrehenn;
                            npcHelper.Reset();
                            npcHelper.Begin("Hollgrehenn", HollgrehennPos);
                            internalStep = 0;
                            lastStepTime = now;
                            BotEngine.Instance?.LogEvent($"[Blacksmith] Arrived in prt_in. Approaching Hollgrehenn to refine '{targetItemName}' to +{targetRefineLevel}.");
                        }
                    }
                    else
                    {
                        var travelState = BotState.TravelingToTargetMap;
                        bot.Navigation.ProcessTravel(
                            netManager,
                            player,
                            now,
                            ref travelState,
                            destinationMapOverride: BlacksmithMap,
                            targetCellPos: HollgrehennPos);
                    }
                    return true;

                case UpgradePhase.BuyingOres:
                    // Autonomous purchase of missing ores from Vurewell
                    bool oreBusy = npcHelper.Process(
                        netManager,
                        player,
                        bot.Navigation,
                        now,
                        onOptionMenu: (options) =>
                        {
                            if (options != null && options.Length > 0)
                            {
                                options[0].OnClick();
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
                                var msg = netManager.StartMessage(PacketType.ShopBuySell);
                                msg.Write(1); // 1 item type
                                msg.Write(requiredOreId);
                                msg.Write(missingOreCount);
                                netManager.SendMessage(msg);

                                NpcInteractionHelper.CleanupNpcUi();
                                missingOreCount = 0;

                                // Transition to Hollgrehenn
                                CurrentPhase = UpgradePhase.TalkingToHollgrehenn;
                                npcHelper.Reset();
                                npcHelper.Begin("Hollgrehenn", HollgrehennPos);
                                internalStep = 0;
                                lastStepTime = now;
                                BotEngine.Instance?.LogEvent($"[Blacksmith] Purchased ores from Vurewell. Moving to Hollgrehenn...");
                                return true;
                            }
                            return false;
                        }
                    );
                    return true;

                case UpgradePhase.TalkingToHollgrehenn:
                case UpgradePhase.ExecutingRefine:
                    // Interact with Hollgrehenn and execute refine loops
                    bool refinerBusy = npcHelper.Process(
                        netManager,
                        player,
                        bot.Navigation,
                        now,
                        onOptionMenu: (options) =>
                        {
                            if (options == null || options.Length == 0) return false;
                            for (int i = 0; i < options.Length; i++)
                            {
                                string text = options[i]?.TextBox != null ? options[i].TextBox.text : "";
                                if (text.IndexOf("Refine", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    text.IndexOf("Gear", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    options[i].OnClick();
                                    CurrentPhase = UpgradePhase.ExecutingRefine;
                                    internalStep = 1;
                                    lastStepTime = now;
                                    return true;
                                }
                            }
                            options[0].OnClick();
                            CurrentPhase = UpgradePhase.ExecutingRefine;
                            internalStep = 1;
                            lastStepTime = now;
                            return true;
                        },
                        onDialogOpen: null,
                        onNoUiVisible: () =>
                        {
                            if (CurrentPhase == UpgradePhase.ExecutingRefine)
                            {
                                return HandleRefineStep(bot, netManager, now);
                            }
                            return false;
                        }
                    );

                    if (CurrentPhase == UpgradePhase.Completed || CurrentPhase == UpgradePhase.Failed)
                    {
                        return false;
                    }
                    return true;

                case UpgradePhase.Completed:
                case UpgradePhase.Failed:
                    return false;
            }

            return false;
        }

        private bool HandleRefineStep(BotEngine bot, NetworkManager netManager, float now)
        {
            // Verify current item state
            if (!EquipmentController.TryFindItemInInventory(targetItemName, targetBagSlotId, out var gearItem) || gearItem.ItemData == null)
            {
                StatusMessage = $"'{targetItemName}' broke or was removed during refine.";
                CurrentPhase = UpgradePhase.Failed;
                netManager.SendNpcAdvance();
                NpcInteractionHelper.CleanupNpcUi();
                return true;
            }

            currentRefineLevel = gearItem.Type == ItemType.UniqueItem ? (int)gearItem.UniqueItem.Refine : 0;

            if (currentRefineLevel >= targetRefineLevel)
            {
                StatusMessage = $"Successfully refined '{targetItemName}' to +{currentRefineLevel}!";
                CurrentPhase = UpgradePhase.Completed;
                netManager.SendNpcAdvance();
                NpcInteractionHelper.CleanupNpcUi();
                return true;
            }

            // Find required ore in inventory
            if (!InventoryHelper.TryGetInventoryData(out var inv) || inv == null) return false;
            InventoryItem oreItem = default;
            bool hasOre = false;
            foreach (var kvp in inv)
            {
                if (kvp.Value != null && kvp.Value.Id == requiredOreId && kvp.Value.Count > 0)
                {
                    oreItem = kvp.Value;
                    hasOre = true;
                    break;
                }
            }

            if (!hasOre)
            {
                StatusMessage = $"Out of refine ores (ID: {requiredOreId}) at +{currentRefineLevel}.";
                CurrentPhase = UpgradePhase.Completed; // Partially succeeded up to current refine
                netManager.SendNpcAdvance();
                NpcInteractionHelper.CleanupNpcUi();
                return true;
            }

            if (internalStep == 1 && now - lastStepTime >= 0.6f)
            {
                netManager.SendNpcRefineAttempt(gearItem.BagSlotId, oreItem.BagSlotId, 0);
                bot.LogEvent($"[Blacksmith] Submitted refine attempt on '{targetItemName}' (+{currentRefineLevel} -> +{currentRefineLevel + 1})...");
                internalStep = 2;
                lastStepTime = now;
                return true;
            }
            else if (internalStep == 2 && now - lastStepTime >= 1.5f)
            {
                // Ready for next refine attempt
                internalStep = 1;
                lastStepTime = now;
                return true;
            }

            return false;
        }

        public void Reset()
        {
            CurrentPhase = UpgradePhase.Idle;
            StatusMessage = "";
            internalStep = 0;
            npcHelper.Reset();
            var net = NetworkManager.Instance;
            if (net != null) net.SendNpcAdvance();
            NpcInteractionHelper.CleanupNpcUi();
        }
    }
}
