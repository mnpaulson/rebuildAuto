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
            ushort playerZone = MapNavMesh.Instance.GetZoneId(cellPos);

            // 1. Always mark the player's immediate sector
            sectorVisits[playerSector] = now;

            // 2. Vision Radius Footprint: Only mark adjacent sectors that share the player's walkable zone and line of sight
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    Vector2Int sec = new Vector2Int(playerSector.x + dx, playerSector.y + dy);
                    if (sec.x >= 0 && sec.y >= 0)
                    {
                        if (TryGetSectorWalkableCell(sec, out var sampleCell))
                        {
                            // Verify the neighboring sector belongs to the same reachable zone within ~22 tiles
                            if (playerZone != 0 && MapNavMesh.Instance.GetZoneId(sampleCell) == playerZone && Vector2.Distance(cellPos, sampleCell) <= 24f)
                            {
                                sectorVisits[sec] = now;
                            }
                        }
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

        public bool TryGetSectorWalkableCell(Vector2Int sectorKey, out Vector2Int walkableCell)
        {
            var walkProvider = RoWalkDataProvider.Instance;
            int baseX = sectorKey.x * SectorSize;
            int baseY = sectorKey.y * SectorSize;

            // Sample points: Center, quarter insets, then edge insets
            int[] sampleOffsetsX = { 8, 4, 12, 4, 12, 6, 10, 8, 2, 14 };
            int[] sampleOffsetsY = { 8, 4, 12, 12, 4, 10, 6, 2, 8, 14 };

            for (int i = 0; i < sampleOffsetsX.Length; i++)
            {
                Vector2Int candidate = new Vector2Int(baseX + sampleOffsetsX[i], baseY + sampleOffsetsY[i]);
                if (walkProvider == null || walkProvider.WalkData == null || walkProvider.IsCellWalkable(candidate))
                {
                    walkableCell = candidate;
                    return true;
                }
            }

            // Fallback grid scan
            for (int ox = 2; ox < SectorSize - 2; ox += 2)
            {
                for (int oy = 2; oy < SectorSize - 2; oy += 2)
                {
                    Vector2Int candidate = new Vector2Int(baseX + ox, baseY + oy);
                    if (walkProvider == null || walkProvider.WalkData == null || walkProvider.IsCellWalkable(candidate))
                    {
                        walkableCell = candidate;
                        return true;
                    }
                }
            }

            walkableCell = new Vector2Int(baseX + SectorSize / 2, baseY + SectorSize / 2);
            return false;
        }

        public Vector2Int FindColdestSectorTarget(string map, Vector2Int playerCellPos, float portalSafetyRadius = 5.0f, Vector2 currentHeading = default)
        {
            UpdatePlayerPosition(map, playerCellPos);

            var candidates = ScoreCandidateSectors(map, playerCellPos, portalSafetyRadius, currentHeading, addRandomJitter: true);

            if (candidates.Count > 0)
            {
                // Pick from top 2 candidates
                int pickIndex = UnityEngine.Random.Range(0, Math.Min(2, candidates.Count));
                return candidates[pickIndex].CenterCell;
            }

            return FallbackSectorTarget(map, playerCellPos, portalSafetyRadius);
        }

        public List<SectorTelemetry> ScoreCandidateSectors(string map, Vector2Int playerCellPos, float portalSafetyRadius, Vector2 currentHeading, bool addRandomJitter = false)
        {
            GetSectorGridDimensions(out int maxSectorX, out int maxSectorY);
            List<SectorTelemetry> candidates = new List<SectorTelemetry>();
            float now = Time.time;

            for (int sx = 0; sx <= maxSectorX; sx++)
            {
                for (int sy = 0; sy <= maxSectorY; sy++)
                {
                    Vector2Int sectorKey = new Vector2Int(sx, sy);

                    if (!TryGetSectorWalkableCell(sectorKey, out Vector2Int centerCell))
                        continue;

                    float dist = Vector2.Distance(playerCellPos, centerCell);
                    if (dist < 28f) continue;

                    bool isBlacklisted = unreachableSectors.TryGetValue(sectorKey, out float expireTime) && now < expireTime;
                    if (isBlacklisted) continue;

                    if (!MapNavMesh.Instance.IsReachable(playerCellPos, centerCell))
                        continue;

                    if (BotConfigManager.Current.AvoidPortalsWhileWandering &&
                        WorldGraph.Instance.IsNearPortal(map, centerCell, portalSafetyRadius))
                    {
                        continue;
                    }

                    bool isVisited = sectorVisits.TryGetValue(sectorKey, out float ts);
                    float timeSinceVisit = isVisited ? (now - ts) : 9999f;

                    float baseAgeScore;
                    float distScore;

                    if (!isVisited)
                    {
                        // Unvisited sectors get massive priority (+800 pts) and gentle distance scaling so corners are eagerly explored
                        baseAgeScore = 800f;
                        distScore = Mathf.Clamp(120f - (dist * 0.12f), 30f, 120f);
                    }
                    else
                    {
                        // Visited sectors scale smoothly up to 540 pts over 7.5 minutes (450s)
                        baseAgeScore = Mathf.Min(timeSinceVisit, 450f) * 1.2f;
                        distScore = Mathf.Clamp(90f - Mathf.Abs(dist - 75f) * 0.45f, 10f, 90f);
                    }

                    float momentumBonus = 0f;
                    if (currentHeading != Vector2.zero)
                    {
                        Vector2 toCandidate = ((Vector2)centerCell - playerCellPos).normalized;
                        float dot = Vector2.Dot(currentHeading, toCandidate);
                        if (dot >= 0.4f) momentumBonus = 35f;
                        else if (dot <= -0.4f) momentumBonus = -45f;
                    }

                    float score = baseAgeScore + distScore + momentumBonus;
                    if (addRandomJitter)
                    {
                        score += UnityEngine.Random.Range(0f, 12f);
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
            float bestScore = float.MinValue;
            Vector2Int bestTarget = playerCellPos;

            for (int sx = 0; sx <= maxSectorX; sx++)
            {
                for (int sy = 0; sy <= maxSectorY; sy++)
                {
                    Vector2Int sectorKey = new Vector2Int(sx, sy);
                    if (!TryGetSectorWalkableCell(sectorKey, out Vector2Int centerCell)) continue;

                    float dist = Vector2.Distance(playerCellPos, centerCell);
                    if (dist < SectorSize) continue;

                    if (!MapNavMesh.Instance.IsReachable(playerCellPos, centerCell)) continue;
                    if (BotConfigManager.Current.AvoidPortalsWhileWandering && WorldGraph.Instance.IsNearPortal(map, centerCell, portalSafetyRadius)) continue;

                    float timeSinceVisit = Time.time - (sectorVisits.TryGetValue(sectorKey, out float ts) ? ts : 0f);
                    float score = timeSinceVisit;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestTarget = centerCell;
                    }
                }
            }
            return bestTarget;
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
