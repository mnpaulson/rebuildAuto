using Assets.Scripts.PlayerControl;
using RebuildSharedData.Data;

namespace RebuildBotPlugin.Services
{
    /// <summary>
    /// Shared inventory access helpers that eliminate repeated null-check chains
    /// across SurvivalController, TownRoutineController, and SkillController.
    /// </summary>
    public static class InventoryHelper
    {
        /// <summary>
        /// Safely retrieves the player's inventory data, returning false if any
        /// part of the chain is null.
        /// </summary>
        public static bool TryGetInventoryData(out Il2CppSystem.Collections.Generic.SortedDictionary<int, InventoryItem> invData)
        {
            invData = null;
            var state = PlayerState.Instance;
            if (state == null || state.Inventory == null) return false;
            invData = state.Inventory.GetInventoryData();
            return invData != null;
        }

        /// <summary>
        /// Safely retrieves an item from the player's inventory by bagSlotId without calling
        /// IL2CPP SortedDictionary.TryGetValue, which causes coreclr.dll access violations with struct value types.
        /// </summary>
        public static bool TryGetInventoryItem(int bagSlotId, out InventoryItem item)
        {
            item = default;
            if (bagSlotId < 0) return false;
            if (!TryGetInventoryData(out var invData) || invData == null) return false;

            foreach (var kvp in invData)
            {
                if (kvp.Key == bagSlotId)
                {
                    item = kvp.Value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Safely retrieves the player's Kafra storage data, returning false if unavailable.
        /// </summary>
        public static bool TryGetStorageData(out Il2CppSystem.Collections.Generic.SortedDictionary<int, InventoryItem> storageData)
        {
            storageData = null;
            var state = PlayerState.Instance;
            if (state == null || state.Storage == null) return false;
            storageData = state.Storage.GetInventoryData();
            return storageData != null;
        }

        /// <summary>
        /// Searches the player's inventory for the first item matching any of the
        /// provided item IDs (in order). Returns the matching ID, or -1 if none found.
        /// Replaces GetAvailableFlyWingId and GetAvailableButterflyWingId patterns.
        /// </summary>
        public static int FindFirstItemId(params int[] itemIds)
        {
            if (!TryGetInventoryData(out var invData)) return -1;

            // Track which IDs are present
            bool[] found = new bool[itemIds.Length];

            foreach (var kvp in invData)
            {
                var item = kvp.Value;
                if (item.ItemData == null || item.Count <= 0) continue;

                for (int i = 0; i < itemIds.Length; i++)
                {
                    if (item.ItemData.Id == itemIds[i])
                    {
                        found[i] = true;
                    }
                }
            }

            // Return the first present ID in priority order
            for (int i = 0; i < itemIds.Length; i++)
            {
                if (found[i]) return itemIds[i];
            }

            return -1;
        }

        /// <summary>
        /// Returns the total count of a specific item ID in the player's inventory.
        /// </summary>
        public static int GetItemCount(int itemId)
        {
            if (!TryGetInventoryData(out var invData)) return 0;

            int total = 0;
            foreach (var kvp in invData)
            {
                var item = kvp.Value;
                if (item.ItemData != null && item.ItemData.Id == itemId && item.Count > 0)
                {
                    total += item.Count;
                }
            }
            return total;
        }

        /// <summary>
        /// Returns the total count of items matching any of the provided item IDs.
        /// </summary>
        public static int GetItemCount(params int[] itemIds)
        {
            if (!TryGetInventoryData(out var invData)) return 0;

            int total = 0;
            foreach (var kvp in invData)
            {
                var item = kvp.Value;
                if (item.ItemData == null || item.Count <= 0) continue;
                foreach (int id in itemIds)
                {
                    if (item.ItemData.Id == id)
                    {
                        total += item.Count;
                        break;
                    }
                }
            }
            return total;
        }

        /// <summary>
        /// Returns the total count of an item in Kafra storage.
        /// </summary>
        public static int GetStorageItemCount(int itemId)
        {
            if (!TryGetStorageData(out var storageData)) return 0;

            int total = 0;
            foreach (var kvp in storageData)
            {
                var item = kvp.Value;
                if (item != null && item.ItemData != null && item.ItemData.Id == itemId && item.Count > 0)
                {
                    total += item.Count;
                }
            }
            return total;
        }

        /// <summary>
        /// Calculates total effective ores (Pure + Rough / 5) across both Inventory and Storage.
        /// </summary>
        public static void CountTotalEffectiveOres(
            int pureOreId,
            int roughOreId,
            out int invPure,
            out int invRough,
            out int storagePure,
            out int storageRough,
            out int totalEffectivePure)
        {
            invPure = GetItemCount(pureOreId);
            invRough = roughOreId > 0 ? GetItemCount(roughOreId) : 0;
            storagePure = GetStorageItemCount(pureOreId);
            storageRough = roughOreId > 0 ? GetStorageItemCount(roughOreId) : 0;

            totalEffectivePure = (invPure + storagePure) + ((invRough + storageRough) / 5);
        }
    }
}
