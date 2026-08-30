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
                if (info.Attempts >= 3 || (now - info.FirstAttemptTime > 3.5f))
                {
                    info.BlacklistUntil = now + 30.0f; // Temporarily ignore for 30s
                    BotEngine.Instance?.LogEvent($"[Loot] Item {entityId} unreachable or failed after {info.Attempts} attempts. Ignoring for 30s.");
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

        public GroundItem FindNearestGroundItem(Vector2Int playerPos)
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
                if (!MapNavMesh.Instance.IsReachable(playerPos, itemCellPos))
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
    }
}
