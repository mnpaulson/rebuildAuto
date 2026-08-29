using System;
using System.Collections.Generic;
using UnityEngine;

namespace RebuildBotPlugin
{
    public class MapHeatmap
    {
        public static MapHeatmap Instance = new MapHeatmap();

        public const int SectorSize = 16;
        public string CurrentMap = "";

        // Key: (sectorX, sectorY) -> float LastVisitedTimestamp
        private Dictionary<Vector2Int, float> sectorVisits = new Dictionary<Vector2Int, float>();

        public void Clear()
        {
            sectorVisits.Clear();
            CurrentMap = "";
        }

        public void UpdatePlayerPosition(string map, Vector2Int cellPos)
        {
            if (!string.Equals(CurrentMap, map, StringComparison.OrdinalIgnoreCase))
            {
                Clear();
                CurrentMap = map;
            }

            Vector2Int sectorKey = GetSectorKey(cellPos);
            sectorVisits[sectorKey] = Time.time;
        }

        public Vector2Int GetSectorKey(Vector2Int cellPos)
        {
            return new Vector2Int(cellPos.x / SectorSize, cellPos.y / SectorSize);
        }

        public Vector2Int FindColdestSectorTarget(string map, Vector2Int playerCellPos, float portalSafetyRadius = 5.0f, int maxRadiusSectors = 15)
        {
            UpdatePlayerPosition(map, playerCellPos);

            Vector2Int playerSector = GetSectorKey(playerCellPos);

            Vector2Int coldestSector = playerSector;
            float oldestTimestamp = float.MaxValue;
            bool foundAny = false;

            int mapWidth = 300;
            int mapHeight = 300;

            var walkProvider = Assets.Scripts.MapEditor.RoWalkDataProvider.Instance;
            if (walkProvider != null && walkProvider.WalkData != null)
            {
                mapWidth = walkProvider.WalkData.Width;
                mapHeight = walkProvider.WalkData.Height;
            }

            int maxSectorX = mapWidth / SectorSize;
            int maxSectorY = mapHeight / SectorSize;

            for (int sx = 0; sx <= maxSectorX; sx++)
            {
                for (int sy = 0; sy <= maxSectorY; sy++)
                {
                    Vector2Int sectorKey = new Vector2Int(sx, sy);
                    Vector2Int sectorCenterCell = new Vector2Int(sx * SectorSize + SectorSize / 2, sy * SectorSize + SectorSize / 2);

                    // Verify cell walkability if provider is active
                    if (walkProvider != null && walkProvider.WalkData != null)
                    {
                        if (!walkProvider.IsCellWalkable(sectorCenterCell))
                            continue; // Skip non-walkable sectors
                    }

                    // Check portal safety
                    if (BotConfigManager.Current.AvoidPortalsWhileWandering &&
                        WorldGraph.Instance.IsNearPortal(map, sectorCenterCell, portalSafetyRadius))
                    {
                        continue;
                    }

                    float lastVisited = sectorVisits.TryGetValue(sectorKey, out float ts) ? ts : 0f;

                    if (lastVisited < oldestTimestamp)
                    {
                        oldestTimestamp = lastVisited;
                        coldestSector = sectorKey;
                        foundAny = true;
                    }
                }
            }

            if (!foundAny)
            {
                coldestSector = playerSector;
            }

            // Pick a walkable cell within the selected coldest sector
            for (int attempt = 0; attempt < 10; attempt++)
            {
                int targetX = coldestSector.x * SectorSize + UnityEngine.Random.Range(2, SectorSize - 2);
                int targetY = coldestSector.y * SectorSize + UnityEngine.Random.Range(2, SectorSize - 2);
                Vector2Int candidate = new Vector2Int(targetX, targetY);

                if (walkProvider != null && walkProvider.WalkData != null)
                {
                    if (walkProvider.IsCellWalkable(candidate))
                        return candidate;
                }
                else
                {
                    return candidate;
                }
            }

            return playerCellPos;
        }
    }
}
