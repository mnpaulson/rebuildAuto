using System;
using System.Collections.Generic;
using Assets.Scripts.MapEditor;
using Assets.Scripts.Network;
using RebuildSharedData.Enum;
using UnityEngine;

namespace RebuildBotPlugin.Services
{
    public class TrackedBossInfo
    {
        public int EntityId;
        public CharacterDisplayType DisplayType;
        public Vector2Int Position;
        public float LastUpdateTime;
    }

    public class BossTrackingService
    {
        public static BossTrackingService Instance { get; } = new BossTrackingService();

        private readonly Dictionary<int, TrackedBossInfo> trackedBosses = new();
        private HashSet<int> cachedBlockedIndices = null;
        private (string map, int count, int hash, float radius, int width, int height) cacheKey;

        public void Clear()
        {
            trackedBosses.Clear();
            cachedBlockedIndices = null;
        }

        public void OnSetEntityPosition(int entityId, CharacterDisplayType type, Vector2Int pos)
        {
            if (type != CharacterDisplayType.Boss && type != CharacterDisplayType.Mvp)
                return;

            float now = Time.time;
            bool isNew = !trackedBosses.ContainsKey(entityId);

            trackedBosses[entityId] = new TrackedBossInfo
            {
                EntityId = entityId,
                DisplayType = type,
                Position = pos,
                LastUpdateTime = now
            };

            cachedBlockedIndices = null;

            if (isNew)
            {
                BotLog.Info($"[Boss Tracker] Registered {type} (ID: {entityId}) on minimap at ({pos.x}, {pos.y}).");
            }
        }

        public void OnRemoveEntity(int entityId)
        {
            if (trackedBosses.Remove(entityId))
            {
                cachedBlockedIndices = null;
                BotLog.Info($"[Boss Tracker] Removed tracked boss/MVP (ID: {entityId}) from minimap.");
            }
        }

        public bool HasTrackedBosses(string map)
        {
            var netManager = NetworkManager.Instance;
            string currentMap = netManager != null ? netManager.CurrentMap : "";
            if (!string.IsNullOrEmpty(map) && !string.Equals(map, currentMap, StringComparison.OrdinalIgnoreCase))
                return false;

            return trackedBosses.Count > 0;
        }

        public IReadOnlyCollection<TrackedBossInfo> GetActiveBosses()
        {
            return trackedBosses.Values;
        }

        public bool IsWithinBossZone(string map, Vector2Int tile, float radius = 25.0f)
        {
            var netManager = NetworkManager.Instance;
            string currentMap = netManager != null ? netManager.CurrentMap : "";
            if (!string.IsNullOrEmpty(map) && !string.Equals(map, currentMap, StringComparison.OrdinalIgnoreCase))
                return false;

            if (trackedBosses.Count == 0)
                return false;

            float rSq = radius * radius;
            foreach (var kvp in trackedBosses)
            {
                Vector2Int bPos = GetLiveBossPosition(kvp.Value);
                float distSq = (tile - bPos).sqrMagnitude;
                if (distSq <= rSq)
                    return true;
            }

            return false;
        }

        public bool GetNearestBoss(string map, Vector2Int tile, float maxRange, out TrackedBossInfo nearestBoss, out float nearestDist)
        {
            nearestBoss = null;
            nearestDist = float.MaxValue;

            var netManager = NetworkManager.Instance;
            string currentMap = netManager != null ? netManager.CurrentMap : "";
            if (!string.IsNullOrEmpty(map) && !string.Equals(map, currentMap, StringComparison.OrdinalIgnoreCase))
                return false;

            if (trackedBosses.Count == 0)
                return false;

            foreach (var kvp in trackedBosses)
            {
                Vector2Int bPos = GetLiveBossPosition(kvp.Value);
                float d = Vector2.Distance(tile, bPos);
                if (d < nearestDist && d <= maxRange)
                {
                    nearestDist = d;
                    nearestBoss = kvp.Value;
                }
            }

            return nearestBoss != null;
        }

        public Vector2Int GetLiveBossPosition(TrackedBossInfo info)
        {
            if (info == null) return Vector2Int.zero;
            var netManager = NetworkManager.Instance;
            if (netManager != null && netManager.EntityList != null && netManager.EntityList.TryGetValue(info.EntityId, out var entity) && entity != null)
            {
                if (entity.IsCharacterAlive && entity.Hp > 0)
                {
                    info.Position = entity.CellPosition;
                    return entity.CellPosition;
                }
            }
            return info.Position;
        }

        public HashSet<int> GetBossBlockedTileIndices(string map, float radius = 25.0f)
        {
            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider == null || walkProvider.WalkData == null) return null;
            return GetBossBlockedTileIndices(map, walkProvider.WalkData.Width, walkProvider.WalkData.Height, radius);
        }

        public HashSet<int> GetBossBlockedTileIndices(string map, int mapWidth, int mapHeight, float radius = 25.0f)
        {
            var netManager = NetworkManager.Instance;
            string currentMap = netManager != null ? netManager.CurrentMap : "";
            if (!string.IsNullOrEmpty(map) && !string.Equals(map, currentMap, StringComparison.OrdinalIgnoreCase))
                return null;

            if (trackedBosses.Count == 0 || mapWidth <= 0 || mapHeight <= 0)
                return null;

            int posHash = 17;
            foreach (var kvp in trackedBosses)
            {
                Vector2Int p = GetLiveBossPosition(kvp.Value);
                posHash = posHash * 31 + p.x * 1000 + p.y;
            }

            var currentKey = (currentMap, trackedBosses.Count, posHash, radius, mapWidth, mapHeight);
            if (cachedBlockedIndices != null && cacheKey.Equals(currentKey))
            {
                return cachedBlockedIndices;
            }

            var indices = new HashSet<int>();
            int rInt = Mathf.CeilToInt(radius);
            float rSq = radius * radius;

            foreach (var kvp in trackedBosses)
            {
                Vector2Int bPos = GetLiveBossPosition(kvp.Value);
                int minX = Math.Max(0, bPos.x - rInt);
                int maxX = Math.Min(mapWidth - 1, bPos.x + rInt);
                int minY = Math.Max(0, bPos.y - rInt);
                int maxY = Math.Min(mapHeight - 1, bPos.y + rInt);

                for (int y = minY; y <= maxY; y++)
                {
                    int dy = y - bPos.y;
                    int dySq = dy * dy;
                    int yOffset = y * mapWidth;

                    for (int x = minX; x <= maxX; x++)
                    {
                        int dx = x - bPos.x;
                        if (dx * dx + dySq <= rSq)
                        {
                            indices.Add(yOffset + x);
                        }
                    }
                }
            }

            cachedBlockedIndices = indices;
            cacheKey = currentKey;
            return indices;
        }

        public Vector2Int FindSafeSteerTile(Vector2Int playerPos, TrackedBossInfo boss, float safeRadius = 25.0f)
        {
            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider == null || walkProvider.WalkData == null) return playerPos;
            var walkData = walkProvider.WalkData;

            Vector2Int bPos = GetLiveBossPosition(boss);
            Vector2 away = ((Vector2)(playerPos - bPos)).normalized;
            if (away == Vector2.zero) away = Vector2.right;

            ushort playerZone = MapNavMesh.Instance.GetZoneId(playerPos);
            float baseAngle = Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg;

            // Probe outward along angle offsets and distances
            float[] angleOffsets = { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 100f, -100f };
            int[] distances = { 12, 10, 8, 14, 6 };

            Vector2Int bestTile = playerPos;
            float bestDistToBoss = Vector2.Distance(playerPos, bPos);

            foreach (int dist in distances)
            {
                foreach (float angleOffset in angleOffsets)
                {
                    float rad = (baseAngle + angleOffset) * Mathf.Deg2Rad;
                    Vector2 probeDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    Vector2Int candidate = playerPos + Vector2Int.RoundToInt(probeDir * dist);

                    if (candidate.x < 0 || candidate.y < 0 || candidate.x >= walkData.Width || candidate.y >= walkData.Height)
                        continue;

                    if (!walkData.CellWalkable(candidate.x, candidate.y))
                        continue;

                    if (playerZone != 0 && MapNavMesh.Instance.GetZoneId(candidate) != playerZone)
                        continue;

                    float candDistToBoss = Vector2.Distance(candidate, bPos);
                    if (candDistToBoss > bestDistToBoss)
                    {
                        bestDistToBoss = candDistToBoss;
                        bestTile = candidate;

                        // Found a tile that places us completely outside the safe radius
                        if (bestDistToBoss >= safeRadius)
                        {
                            return bestTile;
                        }
                    }
                }
            }

            return bestTile;
        }
    }
}
