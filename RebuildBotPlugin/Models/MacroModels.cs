using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.Sprites;
using Assets.Scripts.UI;
using Assets.Scripts.UI.RefineItem;
using RebuildBotPlugin.Controllers;
using RebuildBotPlugin.Services;
using RebuildSharedData.ClientTypes;
using RebuildSharedData.Enum;
using RebuildSharedData.Networking;
using UnityEngine;

namespace RebuildBotPlugin.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MacroStatus
    {
        Pending,
        Running,
        Success,
        Failed,
        Cancelled
    }

    /// <summary>
    /// Base class for all discrete one-time bot actions.
    /// </summary>
    public abstract class MacroAction
    {
        public string ActionId { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public abstract string ActionType { get; }
        public virtual string Description => $"{ActionType} action";

        public MacroStatus Status { get; set; } = MacroStatus.Pending;
        public string ResultMessage { get; set; } = "";
        public float TimeoutSeconds { get; set; } = 35.0f;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        protected float actionStartTime;
        protected float lastStepTime;
        protected int stepPhase = 0;

        public virtual void Begin(BotEngine bot)
        {
            Status = MacroStatus.Running;
            StartedAt = DateTime.UtcNow;
            actionStartTime = Time.time;
            lastStepTime = Time.time;
            stepPhase = 0;
            bot?.LogEvent($"[Macro] Starting '{Description}' (ID: {ActionId})...");
        }

        /// <summary>
        /// Process the macro action on a bot tick.
        /// Returns true if still running, false when completed or failed.
        /// </summary>
        public abstract bool Process(BotEngine bot, float now);

        public virtual void Cleanup(BotEngine bot)
        {
            NpcInteractionHelper.CleanupNpcUi();
        }

        protected void CompleteSuccess(BotEngine bot, string message)
        {
            Status = MacroStatus.Success;
            ResultMessage = message;
            CompletedAt = DateTime.UtcNow;
            bot?.LogEvent($"[Macro Success] {message} (ID: {ActionId})");
            Cleanup(bot);
        }

        protected void CompleteFailure(BotEngine bot, string reason)
        {
            Status = MacroStatus.Failed;
            ResultMessage = reason;
            CompletedAt = DateTime.UtcNow;
            bot?.LogEvent($"[Macro Failed] {reason} (ID: {ActionId})");
            Cleanup(bot);
        }

        protected bool CheckTimeout(BotEngine bot, float now, string timeoutContext = "timed out")
        {
            if (now - actionStartTime > TimeoutSeconds)
            {
                CompleteFailure(bot, $"Action {timeoutContext} after {TimeoutSeconds:F0}s.");
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Macro to equip a specific item from inventory.
    /// </summary>
    public class EquipItemMacroAction : MacroAction
    {
        public override string ActionType => "EquipItem";
        public string ItemName { get; set; } = "";
        public int BagSlotId { get; set; } = -1;
        public override string Description => $"Equip '{ItemName}'";

        public override bool Process(BotEngine bot, float now)
        {
            if (CheckTimeout(bot, now, $"equipping '{ItemName}'")) return false;

            var netManager = NetworkManager.Instance;
            if (netManager == null) return true;

            if (stepPhase == 0)
            {
                if (!EquipmentController.TryFindItemInInventory(ItemName, BagSlotId, out var targetItem))
                {
                    CompleteFailure(bot, $"Item '{ItemName}' not found in inventory.");
                    return false;
                }

                BagSlotId = targetItem.BagSlotId;
                ItemName = targetItem.ItemData?.Name ?? ItemName;

                netManager.SendEquipItem(BagSlotId);
                stepPhase = 1;
                lastStepTime = now;
                return true;
            }
            else if (stepPhase == 1)
            {
                if (now - lastStepTime >= 0.4f)
                {
                    if (EquipmentController.IsItemEquipped(BagSlotId))
                    {
                        CompleteSuccess(bot, $"Successfully equipped '{ItemName}' (Bag Slot: {BagSlotId}).");
                        return false;
                    }
                    else if (now - lastStepTime >= 1.5f)
                    {
                        CompleteFailure(bot, $"Failed to equip '{ItemName}'. Server rejected or character ineligible.");
                        return false;
                    }
                }
            }

            return true;
        }
    }

    public class UnequipItemMacroAction : MacroAction
    {
        public override string ActionType => "UnequipItem";
        public EquipSlot Slot { get; set; } = EquipSlot.None;
        public string SlotName { get; set; } = "";
        public override string Description => $"Unequip from {(Slot != EquipSlot.None ? Slot.ToString() : SlotName)}";

        public override bool Process(BotEngine bot, float now)
        {
            if (CheckTimeout(bot, now, $"unequipping {Description}")) return false;

            var netManager = NetworkManager.Instance;
            var state = PlayerState.Instance;
            if (netManager == null || state == null) return true;

            int slotIdx = EquipmentController.ResolveSlotIndex(SlotName, Slot);
            if (slotIdx < 0 || slotIdx >= state.EquippedItems.Length)
            {
                CompleteFailure(bot, $"Invalid equipment slot '{SlotName}' / '{Slot}'.");
                return false;
            }

            string readableSlotName = EquipmentController.GetSlotName(slotIdx);
            int bagId = state.EquippedItems[slotIdx];
            if (bagId <= 0)
            {
                CompleteSuccess(bot, $"Slot '{readableSlotName}' is already empty.");
                return false;
            }

            if (stepPhase == 0)
            {
                netManager.SendUnEquipItem(bagId);
                stepPhase = 1;
                lastStepTime = now;
                return true;
            }
            else if (stepPhase == 1 && now - lastStepTime >= 0.4f)
            {
                if (state.EquippedItems[slotIdx] == 0)
                {
                    CompleteSuccess(bot, $"Successfully unequipped item from '{readableSlotName}'.");
                    return false;
                }
                else if (now - lastStepTime >= 1.5f)
                {
                    CompleteFailure(bot, $"Failed to unequip slot '{readableSlotName}'.");
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// Macro to socket a monster card into an equipment with open slots.
    /// </summary>
    public class SlotCardMacroAction : MacroAction
    {
        public override string ActionType => "SlotCard";
        public string TargetGearName { get; set; } = "";
        public int TargetBagSlotId { get; set; } = -1;
        public string CardName { get; set; } = "";
        public int CardBagSlotId { get; set; } = -1;
        public override string Description => $"Socket '{CardName}' into '{TargetGearName}'";

        public override bool Process(BotEngine bot, float now)
        {
            if (CheckTimeout(bot, now, $"socketing '{CardName}' into '{TargetGearName}'")) return false;

            var netManager = NetworkManager.Instance;
            if (netManager == null) return true;

            if (stepPhase == 0)
            {
                if (!InventoryHelper.TryGetInventoryData(out var inv) || inv == null)
                {
                    CompleteFailure(bot, "Cannot access player inventory.");
                    return false;
                }

                InventoryItem targetGear = null;
                InventoryItem cardItem = null;

                foreach (var kvp in inv)
                {
                    var item = kvp.Value;
                    if (item == null || item.Count <= 0 || item.ItemData == null) continue;

                    if (targetGear == null)
                    {
                        if (TargetBagSlotId >= 0 && item.BagSlotId == TargetBagSlotId)
                            targetGear = item;
                        else if (!string.IsNullOrWhiteSpace(TargetGearName) &&
                                 item.ItemData.Name.IndexOf(TargetGearName, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                 item.ItemData.Slots > 0)
                            targetGear = item;
                    }

                    if (cardItem == null)
                    {
                        if (CardBagSlotId >= 0 && item.BagSlotId == CardBagSlotId)
                            cardItem = item;
                        else if (!string.IsNullOrWhiteSpace(CardName) &&
                                 (item.ItemData.Name.IndexOf(CardName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  item.ItemData.Code.IndexOf(CardName, StringComparison.OrdinalIgnoreCase) >= 0) &&
                                 item.ItemData.ItemClass == ItemClass.Card)
                            cardItem = item;
                    }
                }

                if (targetGear == null)
                {
                    CompleteFailure(bot, $"Target gear '{TargetGearName}' (with open slots) not found in inventory.");
                    return false;
                }

                if (cardItem == null)
                {
                    CompleteFailure(bot, $"Card '{CardName}' not found in inventory.");
                    return false;
                }

                TargetBagSlotId = targetGear.BagSlotId;
                CardBagSlotId = cardItem.BagSlotId;
                TargetGearName = targetGear.ItemData.Name;
                CardName = cardItem.ItemData.Name;

                netManager.SendSocketItem(TargetBagSlotId, CardBagSlotId);
                stepPhase = 1;
                lastStepTime = now;
                return true;
            }
            else if (stepPhase == 1 && now - lastStepTime >= 0.6f)
            {
                CompleteSuccess(bot, $"Socketed '{CardName}' into '{TargetGearName}' (Gear Slot: {TargetBagSlotId}, Card Slot: {CardBagSlotId}).");
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Macro to consume an item (e.g. Awakening Potion, Fly Wing, Box).
    /// </summary>
    public class UseItemMacroAction : MacroAction
    {
        public override string ActionType => "UseItem";
        public string ItemName { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public override string Description => $"Use {Quantity}x '{ItemName}'";

        private int usesRemaining;

        public override void Begin(BotEngine bot)
        {
            base.Begin(bot);
            usesRemaining = Math.Max(1, Quantity);
        }

        public override bool Process(BotEngine bot, float now)
        {
            if (CheckTimeout(bot, now, $"using '{ItemName}'")) return false;

            var netManager = NetworkManager.Instance;
            if (netManager == null) return true;

            if (usesRemaining <= 0)
            {
                CompleteSuccess(bot, $"Finished using {Quantity}x '{ItemName}'.");
                return false;
            }

            if (now - lastStepTime >= 0.4f)
            {
                if (!InventoryHelper.TryGetInventoryData(out var inv) || inv == null)
                {
                    CompleteFailure(bot, "Cannot access player inventory.");
                    return false;
                }

                InventoryItem useItem = null;
                foreach (var kvp in inv)
                {
                    var item = kvp.Value;
                    if (item == null || item.Count <= 0 || item.ItemData == null) continue;
                    if (item.ItemData.Name.IndexOf(ItemName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        item.ItemData.Code.IndexOf(ItemName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        useItem = item;
                        break;
                    }
                }

                if (useItem == null)
                {
                    if (usesRemaining < Quantity)
                    {
                        CompleteSuccess(bot, $"Used {Quantity - usesRemaining}x '{ItemName}' (depleted remaining).");
                        return false;
                    }
                    CompleteFailure(bot, $"Item '{ItemName}' not found in inventory.");
                    return false;
                }

                netManager.SendUseItem(useItem.BagSlotId);
                usesRemaining--;
                lastStepTime = now;
            }

            return true;
        }
    }

    /// <summary>
    /// Macro to navigate to a target map via macro pathfinding / Kafra teleports.
    /// </summary>
    public class TravelToMapMacroAction : MacroAction
    {
        public override string ActionType => "TravelToMap";
        public string TargetMap { get; set; } = "";
        public override string Description => $"Travel to map '{TargetMap}'";

        public override bool Process(BotEngine bot, float now)
        {
            if (CheckTimeout(bot, now, $"traveling to '{TargetMap}'")) return false;

            var netManager = NetworkManager.Instance;
            var player = bot.Player;
            if (netManager == null || player == null) return true;

            if (string.Equals(netManager.CurrentMap, TargetMap, StringComparison.OrdinalIgnoreCase))
            {
                CompleteSuccess(bot, $"Arrived on target map '{TargetMap}'.");
                return false;
            }

            var travelState = BotState.TravelingToTargetMap;
            bot.Navigation.ProcessTravel(netManager, player, now, ref travelState, null, destinationMapOverride: TargetMap);
            return true;
        }
    }

    /// <summary>
    /// Macro to buy items from a merchant NPC.
    /// </summary>
    public class BuyItemMacroAction : MacroAction
    {
        public override string ActionType => "BuyItem";
        public string ItemName { get; set; } = "";
        public int Quantity { get; set; } = 1;
        public string VendorName { get; set; } = "Vendor";
        public Vector2Int VendorPosition { get; set; } = new Vector2Int(148, 358); // Default to prt_fild08 General Vendor
        public override string Description => $"Buy {Quantity}x '{ItemName}' from {VendorName}";

        private readonly NpcInteractionHelper npcHelper = new();

        public override void Begin(BotEngine bot)
        {
            base.Begin(bot);
            npcHelper.Reset();
            npcHelper.Begin(VendorName, VendorPosition);
        }

        public override bool Process(BotEngine bot, float now)
        {
            if (CheckTimeout(bot, now, $"buying '{ItemName}'")) return false;

            var netManager = NetworkManager.Instance;
            var player = bot.Player;
            if (netManager == null || player == null) return true;

            bool busy = npcHelper.Process(
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
                        if (text.IndexOf("Buy", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            options[i].OnClick();
                            stepPhase = 1;
                            lastStepTime = now;
                            return true;
                        }
                    }
                    options[0].OnClick();
                    stepPhase = 1;
                    lastStepTime = now;
                    return true;
                },
                onDialogOpen: null,
                onNoUiVisible: () =>
                {
                    if (stepPhase == 1 && now - lastStepTime >= 0.5f)
                    {
                        // Look up item ID
                        int itemId = -1;
                        if (ClientDataLoader.Instance != null && ClientDataLoader.Instance.TryGetItemByName(ItemName, out var itemDat))
                        {
                            itemId = itemDat.Id;
                        }

                        if (itemId <= 0)
                        {
                            CompleteFailure(bot, $"Unknown item name '{ItemName}'.");
                            return true;
                        }

                        var msg = netManager.StartMessage(PacketType.ShopBuySell);
                        msg.Write(1); // count of unique items
                        msg.Write(itemId);
                        msg.Write(Quantity);
                        netManager.SendMessage(msg);

                        NpcInteractionHelper.CleanupNpcUi();
                        CompleteSuccess(bot, $"Purchased {Quantity}x '{ItemName}' (ID: {itemId}) from {VendorName}.");
                        return true;
                    }
                    return false;
                }
            );

            return !busy || Status == MacroStatus.Running;
        }

        public override void Cleanup(BotEngine bot)
        {
            base.Cleanup(bot);
            npcHelper.Reset();
        }
    }

    /// <summary>
    /// Macro to refine/upgrade weapon or armor at Blacksmith.
    /// </summary>
    public class UpgradeItemMacroAction : MacroAction
    {
        public override string ActionType => "UpgradeItem";
        public string ItemName { get; set; } = "";
        public int TargetRefineLevel { get; set; } = 4;
        public bool StopAtSafeLimit { get; set; } = true;
        public override string Description => $"Upgrade '{ItemName}' to +{TargetRefineLevel}";

        private readonly BlacksmithHelper blacksmithHelper = new();

        public override void Begin(BotEngine bot)
        {
            base.Begin(bot);
            TimeoutSeconds = 90.0f; // Allow ample time for map traversal, ore buying, and refining
            if (!blacksmithHelper.Begin(ItemName, TargetRefineLevel, StopAtSafeLimit, out string error))
            {
                if (blacksmithHelper.CurrentPhase == BlacksmithHelper.UpgradePhase.Completed)
                {
                    CompleteSuccess(bot, error);
                }
                else
                {
                    CompleteFailure(bot, error);
                }
            }
        }

        public override bool Process(BotEngine bot, float now)
        {
            if (CheckTimeout(bot, now, $"upgrading '{ItemName}'")) return false;
            if (Status != MacroStatus.Running) return false;

            bool busy = blacksmithHelper.Process(bot, now);

            if (blacksmithHelper.CurrentPhase == BlacksmithHelper.UpgradePhase.Completed)
            {
                CompleteSuccess(bot, blacksmithHelper.StatusMessage);
                return false;
            }
            else if (blacksmithHelper.CurrentPhase == BlacksmithHelper.UpgradePhase.Failed)
            {
                CompleteFailure(bot, blacksmithHelper.StatusMessage);
                return false;
            }

            return busy;
        }

        public override void Cleanup(BotEngine bot)
        {
            base.Cleanup(bot);
            blacksmithHelper.Reset();
        }
    }

    /// <summary>
    /// Batch command structure for reading bot_macro.json.
    /// </summary>
    public class MacroCommandBatch
    {
        public List<MacroCommandEntry> Commands { get; set; } = new List<MacroCommandEntry>();
    }

    public class MacroCommandEntry
    {
        public string ActionType { get; set; } = "";
        public string ItemName { get; set; }
        public string TargetItemName { get; set; }
        public string CardName { get; set; }
        public int Quantity { get; set; } = 1;
        public int TargetRefineLevel { get; set; } = 4;
        public bool StopAtSafeLimit { get; set; } = true;
        public string SlotName { get; set; }
        public string TargetMap { get; set; }
        public string VendorName { get; set; }
        public int VendorX { get; set; }
        public int VendorY { get; set; }
    }
}
