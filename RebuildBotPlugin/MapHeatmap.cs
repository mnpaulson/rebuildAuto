using System;
using System.Collections.Generic;
using Assets.Scripts.MapEditor;
using UnityEngine;

namespace RebuildBotPlugin
{
    public class MapHeatmap
    {
        public static MapHeatmap Instance = new MapHeatmap();

        public const int SectorSize = 16;
        public string CurrentMap = "";

        // Key: (sectorX, sectorY) -> float LastVisitedTimestamp
        private readonly Dictionary<Vector2Int, float> sectorVisits = new Dictionary<Vector2Int, float>();
        private readonly Dictionary<Vector2Int, float> unreachableSectors = new Dictionary<Vector2Int, float>();

        public void Clear()
        {
            sectorVisits.Clear();
            unreachableSectors.Clear();
            CurrentMap = "";
        }

        public void BlacklistSector(Vector2Int cellPos, float durationSeconds = BotConstants.SectorBlacklistDuration)
        {
            Vector2Int sectorKey = GetSectorKey(cellPos);
            unreachableSectors[sectorKey] = Time.time + durationSeconds;
        }

        public void UpdatePlayerPosition(string map, Vector2Int cellPos)
        {
            if (!string.Equals(CurrentMap, map, StringComparison.OrdinalIgnoreCase))
            {
                Clear();
                CurrentMap = map;
            }

            Vector2Int playerSector = GetSectorKey(cellPos);
            float now = Time.time;

            // Vision Radius Footprint: Mark the 3x3 sector neighborhood (~24 tiles) as visited
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    Vector2Int sec = new Vector2Int(playerSector.x + dx, playerSector.y + dy);
                    if (sec.x >= 0 && sec.y >= 0)
                    {
                        sectorVisits[sec] = now;
                    }
                }
            }
        }

        public Vector2Int GetSectorKey(Vector2Int cellPos)
        {
            return new Vector2Int(cellPos.x / SectorSize, cellPos.y / SectorSize);
        }

        public float GetSectorLastVisit(Vector2Int cellPos)
        {
            Vector2Int sectorKey = GetSectorKey(cellPos);
            return sectorVisits.TryGetValue(sectorKey, out float ts) ? ts : 0f;
        }

        public Vector2Int FindColdestSectorTarget(string map, Vector2Int playerCellPos, float portalSafetyRadius = 5.0f, Vector2 currentHeading = default)
        {
            UpdatePlayerPosition(map, playerCellPos);

            var candidates = ScoreCandidateSectors(map, playerCellPos, portalSafetyRadius, currentHeading, addRandomJitter: true);

            Vector2Int chosenSector;
            if (candidates.Count > 0)
            {
                // Pick from top 2 candidates
                int pickIndex = UnityEngine.Random.Range(0, Math.Min(2, candidates.Count));
                chosenSector = candidates[pickIndex].Sector;
            }
            else
            {
                return FallbackSectorTarget(map, playerCellPos, portalSafetyRadius);
            }

            // Pick a walkable cell within the selected sector
            var walkProvider = RoWalkDataProvider.Instance;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                int targetX = chosenSector.x * SectorSize + UnityEngine.Random.Range(2, SectorSize - 2);
                int targetY = chosenSector.y * SectorSize + UnityEngine.Random.Range(2, SectorSize - 2);
                Vector2Int candidate = new Vector2Int(targetX, targetY);

                if (walkProvider == null || walkProvider.WalkData == null || walkProvider.IsCellWalkable(candidate))
                {
                    return candidate;
                }
            }

            return new Vector2Int(chosenSector.x * SectorSize + SectorSize / 2, chosenSector.y * SectorSize + SectorSize / 2);
        }

        public List<SectorTelemetry> ScoreCandidateSectors(string map, Vector2Int playerCellPos, float portalSafetyRadius, Vector2 currentHeading, bool addRandomJitter = false)
        {
            GetSectorGridDimensions(out int maxSectorX, out int maxSectorY);
            var walkProvider = RoWalkDataProvider.Instance;
            List<SectorTelemetry> candidates = new List<SectorTelemetry>();
            float now = Time.time;

            for (int sx = 0; sx <= maxSectorX; sx++)
            {
                for (int sy = 0; sy <= maxSectorY; sy++)
                {
                    Vector2Int sectorKey = new Vector2Int(sx, sy);
                    Vector2Int centerCell = new Vector2Int(sx * SectorSize + SectorSize / 2, sy * SectorSize + SectorSize / 2);

                    float dist = Vector2.Distance(playerCellPos, centerCell);
                    if (dist < 35f) continue;

                    bool isBlacklisted = unreachableSectors.TryGetValue(sectorKey, out float expireTime) && now < expireTime;
                    if (isBlacklisted) continue;

                    if (walkProvider != null && walkProvider.WalkData != null && !walkProvider.IsCellWalkable(centerCell))
                        continue;

                    if (!MapNavMesh.Instance.IsReachable(playerCellPos, centerCell))
                        continue;

                    if (BotConfigManager.Current.AvoidPortalsWhileWandering &&
                        WorldGraph.Instance.IsNearPortal(map, centerCell, portalSafetyRadius))
                    {
                        continue;
                    }

                    bool isVisited = sectorVisits.TryGetValue(sectorKey, out float ts);
                    float timeSinceVisit = isVisited ? (now - ts) : 9999f;
                    float distScore = Mathf.Clamp(120f - Mathf.Abs(dist - 75f), 10f, 80f);

                    float momentumBonus = 0f;
                    if (currentHeading != Vector2.zero)
                    {
                        Vector2 toCandidate = ((Vector2)centerCell - playerCellPos).normalized;
                        float dot = Vector2.Dot(currentHeading, toCandidate);
                        if (dot >= 0.5f) momentumBonus = 40f;
                        else if (dot <= -0.5f) momentumBonus = -60f;
                    }

                    float score = (Mathf.Min(timeSinceVisit, 300f) * 2.5f) + distScore + momentumBonus;
                    if (addRandomJitter)
                    {
                        score += UnityEngine.Random.Range(0f, 15f);
                    }

                    candidates.Add(new SectorTelemetry
                    {
                        Sector = sectorKey,
                        CenterCell = centerCell,
                        LastVisitedAge = timeSinceVisit,
                        Distance = dist,
                        Score = score,
                        IsBlacklisted = isBlacklisted,
                        IsVisited = isVisited
                    });
                }
            }

            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
            return candidates;
        }

        private Vector2Int FallbackSectorTarget(string map, Vector2Int playerCellPos, float portalSafetyRadius)
        {
            GetSectorGridDimensions(out int maxSectorX, out int maxSectorY);
            var walkProvider = RoWalkDataProvider.Instance;
            float bestScore = float.MinValue;
            Vector2Int bestSector = GetSectorKey(playerCellPos);

            for (int sx = 0; sx <= maxSectorX; sx++)
            {
                for (int sy = 0; sy <= maxSectorY; sy++)
                {
                    Vector2Int sectorKey = new Vector2Int(sx, sy);
                    Vector2Int centerCell = new Vector2Int(sx * SectorSize + SectorSize / 2, sy * SectorSize + SectorSize / 2);
                    float dist = Vector2.Distance(playerCellPos, centerCell);
                    if (dist < SectorSize) continue;

                    if (walkProvider != null && walkProvider.WalkData != null && !walkProvider.IsCellWalkable(centerCell)) continue;
                    if (!MapNavMesh.Instance.IsReachable(playerCellPos, centerCell)) continue;
                    if (BotConfigManager.Current.AvoidPortalsWhileWandering && WorldGraph.Instance.IsNearPortal(map, centerCell, portalSafetyRadius)) continue;

                    float timeSinceVisit = Time.time - (sectorVisits.TryGetValue(sectorKey, out float ts) ? ts : 0f);
                    float score = timeSinceVisit;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestSector = sectorKey;
                    }
                }
            }
            return new Vector2Int(bestSector.x * SectorSize + SectorSize / 2, bestSector.y * SectorSize + SectorSize / 2);
        }

        public class SectorTelemetry
        {
            public Vector2Int Sector;
            public Vector2Int CenterCell;
            public float LastVisitedAge;
            public float Distance;
            public float Score;
            public bool IsBlacklisted;
            public bool IsVisited;
        }

        public void GetSectorGridDimensions(out int maxSectorX, out int maxSectorY)
        {
            int mapWidth = 300;
            int mapHeight = 300;

            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider != null && walkProvider.WalkData != null)
            {
                mapWidth = walkProvider.WalkData.Width;
                mapHeight = walkProvider.WalkData.Height;
            }

            maxSectorX = mapWidth / SectorSize;
            maxSectorY = mapHeight / SectorSize;
        }

        public Dictionary<Vector2Int, float> GetSectorVisitsCopy()
        {
            return new Dictionary<Vector2Int, float>(sectorVisits);
        }

        public Dictionary<Vector2Int, float> GetUnreachableSectorsCopy()
        {
            return new Dictionary<Vector2Int, float>(unreachableSectors);
        }

        public List<SectorTelemetry> GetTopCandidateSectors(string map, Vector2Int playerCellPos, float portalSafetyRadius = 5.0f, int count = 5, Vector2 currentHeading = default)
        {
            var candidates = ScoreCandidateSectors(map, playerCellPos, portalSafetyRadius, currentHeading, addRandomJitter: false);
            if (candidates.Count > count)
            {
                candidates.RemoveRange(count, candidates.Count - count);
            }
            return candidates;
        }
    }
}
