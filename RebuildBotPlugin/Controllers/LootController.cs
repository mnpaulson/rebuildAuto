using System;
using System.Collections.Generic;
using Assets.Scripts.Network;
using Assets.Scripts.Objects;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public class LootController
    {
        private class LootAttemptInfo
        {
            public int Attempts;
            public float FirstAttemptTime;
            public float BlacklistUntil;
        }

        private readonly Dictionary<int, LootAttemptInfo> lootAttempts = new Dictionary<int, LootAttemptInfo>();
        public int PendingLootItemId { get; set; } = -1;
        public int LootCount { get; set; } = 0;

        private float lastLootTime = 0f;

        public void Clear()
        {
            PendingLootItemId = -1;
        }

        public void TrackLootAttempt(int entityId, float now)
        {
            if (!lootAttempts.TryGetValue(entityId, out var info))
            {
                info = new LootAttemptInfo { Attempts = 1, FirstAttemptTime = now, BlacklistUntil = 0f };
                lootAttempts[entityId] = info;
            }
            else
            {
                info.Attempts++;
                // Wall-clock watchdog: only blacklist if 4.5 seconds elapse without item being collected
                if (now - info.FirstAttemptTime > 4.5f)
                {
                    info.BlacklistUntil = now + 20.0f; // Temporarily ignore for 20s
                    BotEngine.Instance?.LogEvent($"[Loot] Item {entityId} unreachable after {(now - info.FirstAttemptTime):F1}s. Ignoring for 20s.");
                }
            }
        }

        public void CleanupLootAttempts(float now)
        {
            if (lootAttempts.Count == 0) return;
            List<int> toRemove = null;
            foreach (var kvp in lootAttempts)
            {
                if (kvp.Value.BlacklistUntil > 0 && now > kvp.Value.BlacklistUntil)
                {
                    toRemove ??= new List<int>();
                    toRemove.Add(kvp.Key);
                }
            }
            if (toRemove != null)
            {
                foreach (var id in toRemove)
                    lootAttempts.Remove(id);
            }
        }

        public GroundItem FindNearestGroundItem(Vector2Int playerPos, Vector2Int? leaderPos = null, float maxDistFromLeader = 16.0f)
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null || netManager.GroundItemList == null) return null;

            float now = Time.time;
            GroundItem bestItem = null;
            float minDistance = float.MaxValue;

            foreach (var kvp in netManager.GroundItemList)
            {
                var item = kvp.Value;
                if (item == null) continue;

                // Check temporary blacklist for failed/unreachable loot
                if (lootAttempts.TryGetValue(item.EntityId, out var info) && info.BlacklistUntil > now)
                    continue;

                // Whitelist check
                if (BotConfigManager.Current.LootItemWhitelist.Count > 0 &&
                    !BotConfigManager.Current.LootItemWhitelist.Contains(item.ItemName))
                    continue;

                // Blacklist check
                if (BotConfigManager.Current.LootItemBlacklist.Contains(item.ItemName))
                    continue;

                Vector2 itemCell = new Vector2(item.transform.position.x, item.transform.position.z);
                Vector2Int itemCellPos = new Vector2Int(Mathf.RoundToInt(itemCell.x), Mathf.RoundToInt(itemCell.y));

                // If following a leader, ensure the loot dropped within tether range of the leader
                if (leaderPos.HasValue && Vector2.Distance(itemCell, leaderPos.Value) > maxDistFromLeader)
                    continue;

                if (!MapNavMesh.Instance.IsReachable(playerPos, itemCellPos))
                    continue;

                // Portal avoidance check (do not loot items that dropped inside portal trigger zones)
                if (BotConfigManager.Current.AvoidPortalsWhileWandering &&
                    WorldGraph.Instance.IsNearPortal(netManager.CurrentMap, itemCellPos, BotConfigManager.Current.PortalSafetyRadius))
                    continue;

                float dist = Vector2.Distance(playerPos, itemCell);
                if (dist <= BotConfigManager.Current.SearchRadius && dist < minDistance)
                {
                    var path = MapNavMesh.Instance.FindPath(playerPos, itemCellPos);
                    if (path != null && path.Count <= BotConfigManager.Current.SearchRadius * 1.5f)
                    {
                        minDistance = dist;
                        bestItem = item;
                    }
                }
            }
            return bestItem;
        }

        public bool ProcessLoot(
            NetworkManager netManager,
            ServerControllable player,
            float now,
            ref BotState currentState,
            Vector2Int? leaderPos = null,
            float maxDistFromLeader = 16.0f)
        {
            if (netManager == null || player == null || !player.IsCharacterAlive) return false;

            // Check if pending item was picked up or disappeared
            if (PendingLootItemId != -1)
            {
                if (netManager.GroundItemList == null || !netManager.GroundItemList.ContainsKey(PendingLootItemId))
                {
                    LootCount++;
                    BotEngine.Instance?.LogEvent($"Collected loot item! Total Loot: {LootCount}");
                    PendingLootItemId = -1;
                }
            }

            var nearestItem = FindNearestGroundItem(player.CellPosition, leaderPos, maxDistFromLeader);
            if (nearestItem != null)
            {
                if (now - lastLootTime >= BotConfigManager.Current.LootCooldownSeconds)
                {
                    PendingLootItemId = nearestItem.EntityId;
                    TrackLootAttempt(nearestItem.EntityId, now);

                    netManager.SendPickUpItem(nearestItem.EntityId);
                    lastLootTime = now;
                    currentState = BotState.LootingItem;
                    float distToItem = Vector2.Distance(player.CellPosition, new Vector2(nearestItem.transform.position.x, nearestItem.transform.position.z));
                    BotEngine.Instance?.LogEvent($"[Loot] Picking up {nearestItem.ItemName} (ID: {nearestItem.EntityId}, dist: {distToItem:F1} tiles).");
                }
                else
                {
                    currentState = BotState.LootingItem;
                }
                return true;
            }
            else
            {
                PendingLootItemId = -1;
                return false;
            }
        }
    }
}
