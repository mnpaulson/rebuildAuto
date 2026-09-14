using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.MapEditor;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI;
using RebuildBotPlugin.Services;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public class NavigationController
    {
        private Vector2Int currentExplorationWaypoint = Vector2Int.zero;
        private float waypointAssignedTime = 0f;
        private Vector2 lastWanderHeading = Vector2.zero;
        private float lastWanderStepTime = 0f;
        private float playerArrivalTime = 0f;
        private bool wasMovingLastFrame = false;
        private Vector2Int lastRecordedPosition = Vector2Int.zero;
        private float stuckTimer = 0f;
        private float lastTravelTime = 0f;
        private Vector2Int currentStepTarget = Vector2Int.zero;
        private Vector2Int currentTravelStepTarget = Vector2Int.zero;
        private float nextWanderDelay = 0.25f;
        private float nextTravelDelay = 0.25f;
        private float lastKafraInteractionTime = 0f;
        private int kafraTravelPhase = 0;

        // Cached Active Travel Plan (Zero per-frame allocations & zero A* thrashing)
        private string cachedTravelStartMap = null;
        private string cachedTravelDestMap = null;
        private Vector2Int? cachedTravelTargetPos = null;
        private System.Collections.Generic.List<WarpConnection> cachedTravelRoute = null;
        private WarpConnection activeTravelWarp = null;
        private Vector2Int activeTravelWarpTarget = Vector2Int.zero;
        private System.Collections.Generic.List<Vector2Int> activeTravelWaypoints = null;
        private int activeTravelWaypointIdx = 0;

        private readonly Vector2Int[] tempPath = new Vector2Int[32];

        // Anti-oscillation watchdog and move dispatch tracker
        private readonly System.Collections.Generic.List<(Vector2Int pos, float time)> positionHistory = new System.Collections.Generic.List<(Vector2Int, float)>();
        private float lastOscillationBreakTime = 0f;
        private Vector2Int lastMoveDispatchedTarget = Vector2Int.zero;
        private float lastMoveDispatchedTime = 0f;

        public Vector2Int CurrentExplorationWaypoint => currentExplorationWaypoint;
        public float WaypointAssignedTime => waypointAssignedTime;
        public Vector2 LastWanderHeading => lastWanderHeading;
        public bool IsKafraTravelActive => kafraTravelPhase > 0;
        public bool HasActiveTravelPlan => (cachedTravelRoute != null && cachedTravelRoute.Count > 0) || activeTravelWarp != null || kafraTravelPhase > 0;

        public void InvalidateTravelPlan()
        {
            cachedTravelStartMap = null;
            cachedTravelDestMap = null;
            cachedTravelTargetPos = null;
            cachedTravelRoute = null;
            activeTravelWarp = null;
            activeTravelWarpTarget = Vector2Int.zero;
            activeTravelWaypoints = null;
            activeTravelWaypointIdx = 0;
            currentTravelStepTarget = Vector2Int.zero;
            kafraTravelPhase = 0;
            positionHistory.Clear();
            lastOscillationBreakTime = 0f;
            lastMoveDispatchedTarget = Vector2Int.zero;
            lastMoveDispatchedTime = 0f;
        }

        public void ResetWander()
        {
            currentExplorationWaypoint = Vector2Int.zero;
            currentStepTarget = Vector2Int.zero;
            waypointAssignedTime = 0f;
            stuckTimer = 0f;
            InvalidateTravelPlan();
            positionHistory.Clear();
            lastOscillationBreakTime = 0f;
            lastMoveDispatchedTarget = Vector2Int.zero;
            lastMoveDispatchedTime = 0f;
            Services.NpcInteractionHelper.CleanupNpcUi();
        }

        private void RecordPosition(Vector2Int pos, float now)
        {
            if (positionHistory.Count == 0 || positionHistory[positionHistory.Count - 1].pos != pos)
            {
                positionHistory.Add((pos, now));
                if (positionHistory.Count > 10)
                    positionHistory.RemoveAt(0);
            }
        }

        private bool IsRecentlyVisited(Vector2Int tile, float now, float maxAge = 2.0f)
        {
            for (int i = positionHistory.Count - 1; i >= 0; i--)
            {
                if (now - positionHistory[i].time > maxAge) break;
                if (positionHistory[i].pos == tile) return true;
            }
            return false;
        }

        private bool IsOscillating(Vector2Int destination, float now)
        {
            if (positionHistory.Count < 4) return false;
            if (now - lastOscillationBreakTime < 1.5f) return false;

            int n = positionHistory.Count;
            // Pattern 1: Pure 2-tile ping-pong: A -> B -> A -> B
            if (n >= 4)
            {
                if (positionHistory[n - 1].pos == positionHistory[n - 3].pos &&
                    positionHistory[n - 2].pos == positionHistory[n - 4].pos &&
                    (positionHistory[n - 1].pos != positionHistory[n - 2].pos))
                {
                    if (now - positionHistory[n - 4].time < 3.0f)
                        return true;
                }
            }

            // Pattern 2: 6 recent steps with <= 2 distinct positions in <= 3.0s
            if (n >= 6)
            {
                float span = now - positionHistory[n - 6].time;
                if (span < 3.0f)
                {
                    var distinct = new System.Collections.Generic.HashSet<Vector2Int>();
                    for (int i = n - 6; i < n; i++)
                        distinct.Add(positionHistory[i].pos);
                    if (distinct.Count <= 2)
                        return true;
                }
            }

            return false;
        }

        public bool IsTilePortalBlocked(string map, Vector2Int tile, bool avoidPortals, WarpConnection targetWarp, bool exactHitboxOnly)
        {
            if (!avoidPortals) return false;
            if (exactHitboxOnly || targetWarp != null)
            {
                return WorldGraph.Instance.IsInsideAnyPortal(map, tile, targetWarp, padding: 1);
            }
            return WorldGraph.Instance.IsNearPortal(map, tile, BotConfigManager.Current.PortalSafetyRadius);
        }

        private bool BreakOscillation(Vector2Int currentPos, Vector2Int destination, RagnarokWalkData walkData, bool avoidPortals, WarpConnection targetWarp, bool exactHitboxOnly, out Vector2Int dispatchedStep, float now)
        {
            dispatchedStep = Vector2Int.zero;
            var netManager = NetworkManager.Instance;
            if (netManager == null) return false;

            BotEngine.Instance?.LogEvent($"[Navigation] Oscillation detected near ({currentPos.x}, {currentPos.y}) while pathing to ({destination.x}, {destination.y}). Breaking loop with native pathing!");

            var blockedIndices = (avoidPortals && (targetWarp != null || exactHitboxOnly))
                ? WorldGraph.Instance.GetMapPortalTileIndices(netManager.CurrentMap, walkData.Width, targetWarp, padding: 1)
                : null;

            if (BotConfigManager.Current.AvoidTrackedBosses)
            {
                var bossIndices = BossTrackingService.Instance.GetBossBlockedTileIndices(netManager.CurrentMap, walkData.Width, walkData.Height, BotConfigManager.Current.BossAvoidanceRadius);
                if (bossIndices != null && bossIndices.Count > 0)
                {
                    var combined = blockedIndices != null ? new HashSet<int>(blockedIndices) : new HashSet<int>();
                    combined.UnionWith(bossIndices);
                    blockedIndices = combined;
                }
            }

            bool allowFallback = !BotConfigManager.Current.AvoidTrackedBosses || !BossTrackingService.Instance.HasTrackedBosses(netManager.CurrentMap);
            // Check if MapNavMesh can give us waypoints
            var routeWaypoints = MapNavMesh.Instance.FindRouteWaypoints(currentPos, destination, 11, blockedIndices, allowFallback);
            Vector2Int target = destination;
            if (routeWaypoints != null && routeWaypoints.Count > 0)
            {
                target = routeWaypoints[0];
            }

            int chebyshev = Math.Max(Math.Abs(target.x - currentPos.x), Math.Abs(target.y - currentPos.y));
            Vector2Int chosenTarget = target;
            if (chebyshev > 12)
            {
                Vector2 dir = ((Vector2)(target - currentPos)).normalized;
                chosenTarget = currentPos + Vector2Int.RoundToInt(dir * 11);
            }

            // Ensure chosen target is walkable
            if (!walkData.CellWalkable(chosenTarget.x, chosenTarget.y))
            {
                for (int r = 1; r <= 2; r++)
                {
                    bool found = false;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        for (int dy = -r; dy <= r; dy++)
                        {
                            int nx = chosenTarget.x + dx;
                            int ny = chosenTarget.y + dy;
                            if (nx >= 0 && ny >= 0 && nx < walkData.Width && ny < walkData.Height && walkData.CellWalkable(nx, ny))
                            {
                                chosenTarget = new Vector2Int(nx, ny);
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }
                    if (found) break;
                }
            }

            if (avoidPortals && IsTilePortalBlocked(netManager.CurrentMap, chosenTarget, avoidPortals, targetWarp, exactHitboxOnly))
            {
                return false;
            }

            if (BotConfigManager.Current.AvoidTrackedBosses && BossTrackingService.Instance.IsWithinBossZone(netManager.CurrentMap, chosenTarget, BotConfigManager.Current.BossAvoidanceRadius))
            {
                return false;
            }

            netManager.MovePlayer(chosenTarget);
            dispatchedStep = chosenTarget;
            lastMoveDispatchedTarget = chosenTarget;
            lastMoveDispatchedTime = now;
            lastOscillationBreakTime = now;
            positionHistory.Clear();
            return true;
        }

        /// <summary>
        /// Finds the optimal walkable attack tile within attackRange
        /// on the side of the monster facing the player, with natural human angle variance.
        /// </summary>
        public Vector2Int GetAttackPosition(Vector2Int playerPos, Vector2Int monsterPos, float attackRange = 2.0f)
        {
            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider == null || walkProvider.WalkData == null) return monsterPos;
            var walkData = walkProvider.WalkData;

            ushort playerZone = MapNavMesh.Instance.GetZoneId(playerPos);

            // If already in attack range, have clear Line of Sight, and standing on a valid walkable tile in the same zone, stay put
            float currentDist = Vector2.Distance(playerPos, monsterPos);
            bool playerHasLos = Pathfinder.HasLineOfSight(playerPos, monsterPos);
            if (currentDist <= attackRange && playerHasLos && walkData.CellWalkable(playerPos.x, playerPos.y) &&
                (playerZone == 0 || MapNavMesh.Instance.GetZoneId(playerPos) == playerZone))
            {
                return playerPos;
            }

            var candidates = new System.Collections.Generic.List<(Vector2Int tile, float distToPlayer)>();
            var losBlockedCandidates = new System.Collections.Generic.List<(Vector2Int tile, float distToPlayer)>();

            int maxR = Mathf.Clamp(Mathf.FloorToInt(attackRange), 1, 14);

            // Directional Ray Arc Optimization:
            // Instead of scanning a massive (2*maxR+1)^2 grid (up to 841 tiles!),
            // probe outward along the vector from the monster towards the player (the approach direction)
            // across a narrow cone of natural angles and progressive distance rings.
            Vector2 toPlayer = (Vector2)(playerPos - monsterPos);
            float baseAngle = (toPlayer.sqrMagnitude > 0.001f)
                ? Mathf.Atan2(toPlayer.y, toPlayer.x) * Mathf.Rad2Deg
                : 0f;

            float[] angleOffsets = (maxR <= 2)
                ? new float[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f } // Melee: full 8-direction circle
                : new float[] { 0f, 12f, -12f, 24f, -24f, 36f, -36f, 50f, -50f }; // Ranged: directional arc facing player

            int[] radii = (maxR <= 2)
                ? new int[] { maxR }
                : (maxR <= 5)
                    ? new int[] { maxR, maxR - 1 }
                    : new int[] { maxR, maxR - 1, maxR - 2 };

            string currentMap = NetworkManager.Instance != null ? NetworkManager.Instance.CurrentMap : "";

            foreach (int r in radii)
            {
                foreach (float angleOffset in angleOffsets)
                {
                    float rad = (baseAngle + angleOffset) * Mathf.Deg2Rad;
                    Vector2 probeDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                    Vector2Int candidate = new Vector2Int(monsterPos.x + Mathf.RoundToInt(probeDir.x * r), monsterPos.y + Mathf.RoundToInt(probeDir.y * r));

                    if (candidate.x < 0 || candidate.y < 0 || candidate.x >= walkData.Width || candidate.y >= walkData.Height)
                        continue;

                    if (!walkData.CellWalkable(candidate.x, candidate.y))
                        continue;

                    if (playerZone != 0 && MapNavMesh.Instance.GetZoneId(candidate) != playerZone)
                        continue;

                    if (BotConfigManager.Current.AvoidPortalsWhileWandering && !string.IsNullOrEmpty(currentMap))
                    {
                        if (WorldGraph.Instance.IsNearPortal(currentMap, candidate, BotConfigManager.Current.PortalSafetyRadius))
                            continue;
                    }

                    if (BotConfigManager.Current.AvoidTrackedBosses && !string.IsNullOrEmpty(currentMap))
                    {
                        if (BossTrackingService.Instance.IsWithinBossZone(currentMap, candidate, BotConfigManager.Current.BossAvoidanceRadius))
                            continue;
                    }

                    float distToPlayer = Vector2.Distance(playerPos, candidate);

                    // Ensure this candidate tile has unblocked Line of Sight to the monster
                    if (Pathfinder.HasLineOfSight(candidate, monsterPos))
                    {
                        candidates.Add((candidate, distToPlayer));
                    }
                    else
                    {
                        losBlockedCandidates.Add((candidate, distToPlayer));
                    }
                }
            }

            if (candidates.Count == 0 && losBlockedCandidates.Count > 0)
            {
                // Fallback to closest walkable tile if no direct LOS tile is in the immediate radius
                candidates = losBlockedCandidates;
            }

            if (candidates.Count == 0)
            {
                if (BotConfigManager.Current.AvoidPortalsWhileWandering && !string.IsNullOrEmpty(currentMap))
                {
                    if (WorldGraph.Instance.IsNearPortal(currentMap, monsterPos, BotConfigManager.Current.PortalSafetyRadius))
                        return Vector2Int.zero;
                }
                if (BotConfigManager.Current.AvoidTrackedBosses && !string.IsNullOrEmpty(currentMap))
                {
                    if (BossTrackingService.Instance.IsWithinBossZone(currentMap, monsterPos, BotConfigManager.Current.BossAvoidanceRadius))
                        return Vector2Int.zero;
                }
                return monsterPos;
            }

            // Sort by distance to player
            candidates.Sort((a, b) => a.distToPlayer.CompareTo(b.distToPlayer));

            // Humanized angle variance: gather candidate tiles within 1.5 tiles of the optimal
            float bestDist = candidates[0].distToPlayer;
            var topCandidates = new System.Collections.Generic.List<Vector2Int>();
            foreach (var c in candidates)
            {
                if (c.distToPlayer <= bestDist + 1.5f)
                {
                    topCandidates.Add(c.tile);
                    if (topCandidates.Count >= 3) break;
                }
            }

            if (topCandidates.Count <= 1) return topCandidates[0];

            // 60% closest, 25% 2nd closest, 15% 3rd closest
            float roll = UnityEngine.Random.value;
            if (roll < 0.60f || topCandidates.Count == 1)
                return topCandidates[0];
            else if (roll < 0.85f || topCandidates.Count == 2)
                return topCandidates[1];
            else
                return topCandidates[2];
        }

        /// <summary>
        /// Unified route navigator: verifies direct line-of-sight first; if obstructed by a wall or corner,
        /// calculates the global A* topological path via MapNavMesh and steps along waypoints.
        /// </summary>
        public bool NavigateTowards(Vector2Int currentPos, Vector2Int destination, bool avoidPortals = false, int hopDistance = 10, WarpConnection targetWarp = null, bool exactHitboxOnly = false)
        {
            return NavigateTowards(currentPos, destination, avoidPortals, hopDistance, out _, targetWarp, exactHitboxOnly);
        }

        public bool NavigateTowards(Vector2Int currentPos, Vector2Int destination, bool avoidPortals, int hopDistance, out Vector2Int dispatchedStep, WarpConnection targetWarp = null, bool exactHitboxOnly = false)
        {
            dispatchedStep = Vector2Int.zero;
            if (currentPos == destination) return true;

            var netManager = NetworkManager.Instance;
            if (netManager == null) return false;

            if (BotConfigManager.Current.AvoidTrackedBosses &&
                BossTrackingService.Instance.IsWithinBossZone(netManager.CurrentMap, destination, BotConfigManager.Current.BossAvoidanceRadius))
            {
                return false;
            }

            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider != null && walkProvider.WalkData != null)
            {
                var walkData = walkProvider.WalkData;
                int chebyshevDist = Math.Max(Math.Abs(destination.x - currentPos.x), Math.Abs(destination.y - currentPos.y));

                // Anti-oscillation watchdog check
                float now = Time.time;
                RecordPosition(currentPos, now);

                if (IsOscillating(destination, now))
                {
                    if (BreakOscillation(currentPos, destination, walkData, avoidPortals, targetWarp, exactHitboxOnly, out dispatchedStep, now))
                        return true;
                }

                var blockedIndices = (avoidPortals && (targetWarp != null || exactHitboxOnly))
                    ? WorldGraph.Instance.GetMapPortalTileIndices(netManager.CurrentMap, walkData.Width, targetWarp, padding: 1)
                    : null;

                if (BotConfigManager.Current.AvoidTrackedBosses)
                {
                    var bossIndices = BossTrackingService.Instance.GetBossBlockedTileIndices(netManager.CurrentMap, walkData.Width, walkData.Height, BotConfigManager.Current.BossAvoidanceRadius);
                    if (bossIndices != null && bossIndices.Count > 0)
                    {
                        var combined = blockedIndices != null ? new HashSet<int>(blockedIndices) : new HashSet<int>();
                        combined.UnionWith(bossIndices);
                        blockedIndices = combined;
                    }
                }

                // 1. Direct path check: if within 12 tiles, walkable, and has clear line-of-sight
                if (chebyshevDist <= 12 && walkData.CellWalkable(destination.x, destination.y))
                {
                    if (!IsTilePortalBlocked(netManager.CurrentMap, destination, avoidPortals, targetWarp, exactHitboxOnly))
                    {
                        if (SafeMoveTowards(currentPos, destination, avoidPortals, forwardOnly: true, out dispatchedStep, targetWarp, exactHitboxOnly))
                            return true;
                    }
                }

                // 2. Obstacle or corner between current position and destination:
                // Use MapNavMesh to extract route waypoints around walls and corridors
                var blockedIndices = (avoidPortals && (targetWarp != null || exactHitboxOnly))
                    ? WorldGraph.Instance.GetMapPortalTileIndices(netManager.CurrentMap, walkData.Width, targetWarp, padding: 1)
                    : null;
                var routeWaypoints = MapNavMesh.Instance.FindRouteWaypoints(currentPos, destination, hopDistance, blockedIndices);
                if (routeWaypoints != null && routeWaypoints.Count > 0)
                {
                    // Find the furthest waypoint in routeWaypoints that we have direct line of sight to and <= 12 tiles away
                    Vector2Int stepTarget = routeWaypoints[0];
                    if (SafeMoveTowards(currentPos, stepTarget, avoidPortals, forwardOnly: true, out dispatchedStep, targetWarp, exactHitboxOnly))
                        return true;
                }
            }

            // 3. Fallback to direct safe movement
            return SafeMoveTowards(currentPos, destination, avoidPortals, forwardOnly: false, out dispatchedStep, targetWarp, exactHitboxOnly);
        }

        public bool SafeMoveTowards(Vector2Int currentPos, Vector2Int destination, bool avoidPortals = false, bool forwardOnly = false, WarpConnection targetWarp = null, bool exactHitboxOnly = false)
        {
            return SafeMoveTowards(currentPos, destination, avoidPortals, forwardOnly, out _, targetWarp, exactHitboxOnly);
        }

        public bool SafeMoveTowards(Vector2Int currentPos, Vector2Int destination, bool avoidPortals, bool forwardOnly, out Vector2Int dispatchedStep, WarpConnection targetWarp = null, bool exactHitboxOnly = false)
        {
            dispatchedStep = Vector2Int.zero;
            var netManager = NetworkManager.Instance;
            if (netManager == null) return false;

            var player = CameraFollower.Instance?.Target?.GetComponent<ServerControllable>();
            if (BotEngine.Instance != null && BotEngine.Instance.Survival != null && BotEngine.Instance.Survival.IsPlayerSitting(player))
            {
                netManager.ChangePlayerSitStand(false);
            }

            if (currentPos == destination) return true;

            bool IsTilePortalBlocked(Vector2Int tile)
            {
                if (!avoidPortals) return false;
                if (exactHitboxOnly || targetWarp != null)
                {
                    return WorldGraph.Instance.IsInsideAnyPortal(netManager.CurrentMap, tile, targetWarp, padding: 1);
                }
                return WorldGraph.Instance.IsNearPortal(netManager.CurrentMap, tile, BotConfigManager.Current.PortalSafetyRadius);
            }

            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider != null && walkProvider.WalkData != null)
            {
                var walkData = walkProvider.WalkData;
                Vector2 dir = destination - currentPos;
                float totalDist = dir.magnitude;

                // 1. Direct step towards destination (up to 11 tiles)
                int directStepDist = Mathf.Min(11, Mathf.RoundToInt(totalDist));
                Vector2Int directTarget = (totalDist <= 11f)
                    ? destination
                    : currentPos + Vector2Int.RoundToInt(dir.normalized * directStepDist);

                bool directWalkable = walkData.CellWalkable(directTarget.x, directTarget.y);
                if (!directWalkable && totalDist <= 11f)
                {
                    // Check 8 adjacent neighbor cells around destination to find a walkable tile
                    float bestNeighborDist = float.MaxValue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = destination.x + dx;
                            int ny = destination.y + dy;
                            if (nx >= 0 && ny >= 0 && nx < walkData.Width && ny < walkData.Height && walkData.CellWalkable(nx, ny))
                            {
                                float d = Vector2.Distance(currentPos, new Vector2(nx, ny));
                                if (d < bestNeighborDist)
                                {
                                    bestNeighborDist = d;
                                    directTarget = new Vector2Int(nx, ny);
                                    directWalkable = true;
                                }
                            }
                        }
                    }
                }

                int chebyshevDist = Math.Max(Math.Abs(directTarget.x - currentPos.x), Math.Abs(directTarget.y - currentPos.y));
                if (directWalkable && chebyshevDist <= 12 && !IsTilePortalBlocked(directTarget))
                {
                    var blockedIndices = (avoidPortals && (targetWarp != null || exactHitboxOnly))
                        ? WorldGraph.Instance.GetMapPortalTileIndices(netManager.CurrentMap, walkData.Width, targetWarp, padding: 1)
                        : null;

                    if (MapNavMesh.HasSafeLineOfSight(currentPos, directTarget, walkData, blockedIndices))
                    {
                        netManager.MovePlayer(directTarget);
                        dispatchedStep = directTarget;
                        return true;
                    }
                }

                // 2. Multi-angle progressive distance probe towards destination
                float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                float[] angleOffsets = forwardOnly
                    ? new float[] { 0f, 15f, -15f, 30f, -30f }
                    : new float[] { 0f, 15f, -15f, 30f, -30f, 45f, -45f };

                int maxStep = Mathf.Clamp(Mathf.RoundToInt(totalDist), 3, 11);
                int[] stepDistances = (maxStep >= 10)
                    ? new int[] { maxStep, 8, 6, 4, 2 }
                    : (maxStep >= 6)
                        ? new int[] { maxStep, 4, 3, 2, 1 }
                        : new int[] { maxStep, 2, 1 };

                var probeBlockedIndices = (avoidPortals && (targetWarp != null || exactHitboxOnly))
                    ? WorldGraph.Instance.GetMapPortalTileIndices(netManager.CurrentMap, walkData.Width, targetWarp, padding: 1)
                    : null;

                foreach (int d in stepDistances)
                {
                    if (BotConfigManager.Current.AvoidTrackedBosses &&
                        BossTrackingService.Instance.IsWithinBossZone(netManager.CurrentMap, directTarget, BotConfigManager.Current.BossAvoidanceRadius))
                    {
                        return false;
                    }

                        // Candidate must make forward progress towards destination (never step sideways or backwards)
                        float candDist = Vector2.Distance(candidate, destination);
                        if (candDist >= totalDist - 0.2f) continue;

                        if (avoidPortals && IsTilePortalBlocked(candidate))
                            continue;

                    if (BotConfigManager.Current.AvoidTrackedBosses)
                    {
                        var bossIndices = BossTrackingService.Instance.GetBossBlockedTileIndices(netManager.CurrentMap, walkData.Width, walkData.Height, BotConfigManager.Current.BossAvoidanceRadius);
                        if (bossIndices != null && bossIndices.Count > 0)
                        {
                            if (MapNavMesh.HasSafeLineOfSight(currentPos, candidate, walkData, probeBlockedIndices))
                            {
                                netManager.MovePlayer(candidate);
                                dispatchedStep = candidate;
                                BotEngine.Instance?.LogEvent($"[Move] Step verified to ({candidate.x}, {candidate.y}) [offset: {angleOffset:F0}°, dist: {d}].");
                                return true;
                            }
                        }

                        netManager.MovePlayer(directTarget);
                        dispatchedStep = directTarget;
                        lastMoveDispatchedTarget = directTarget;
                        lastMoveDispatchedTime = Time.time;
                        return true;
                    }
                }

                // If line of sight is blocked by a wall or obstacle, do NOT probe random radial angles.
                // That causes pacing back and forth against walls. Report blocked so A* routing handles it.
                return false;
            }
            else
            {
                // Fallback without walk data
                Vector2 diff = destination - currentPos;
                float dist = Mathf.Min(diff.magnitude, 12f);
                Vector2 step = diff.normalized * dist;
                Vector2Int targetTile = currentPos + new Vector2Int(Mathf.RoundToInt(step.x), Mathf.RoundToInt(step.y));
                netManager.MovePlayer(targetTile);
                dispatchedStep = targetTile;
                return true;
            }
        }

        public bool ProcessTravel(NetworkManager netManager, ServerControllable player, float now, ref BotState currentState, Vector2Int? targetCellPos = null, string destinationMapOverride = null)
        {
            bool isTownRoutine = BotEngine.Instance != null && BotEngine.Instance.TownRoutine.IsActive;
            string destinationMap = !string.IsNullOrWhiteSpace(destinationMapOverride)
                ? destinationMapOverride
                : (isTownRoutine ? TownRoutineController.BaseMap : BotConfigManager.Current.TargetMap);

            if (string.IsNullOrWhiteSpace(destinationMap))
            {
                InvalidateTravelPlan();
                return false;
            }

            bool isSameMap = string.Equals(netManager.CurrentMap, destinationMap, StringComparison.OrdinalIgnoreCase);

            // If start and target are on the same map:
            // Check if player can reach targetCellPos locally
            if (isSameMap)
            {
                if (!targetCellPos.HasValue || MapNavMesh.Instance.IsReachable(player.CellPosition, targetCellPos.Value))
                {
                    InvalidateTravelPlan();
                    return false;
                }
            }

            bool isPartyTravel = !string.IsNullOrWhiteSpace(destinationMapOverride);
            if (!BotConfigManager.Current.AutoTravel && !isTownRoutine && !isPartyTravel)
            {
                return false;
            }

            // 1. REPLAN ONLY WHEN NECESSARY (Zero per-frame allocations & zero A* thrashing)
            // Note: If we are not yet on the destination map, intermediate warp hops are independent of targetCellPos changes.
            bool targetPosChanged = string.Equals(netManager.CurrentMap, destinationMap, StringComparison.OrdinalIgnoreCase) && cachedTravelTargetPos != targetCellPos;
            bool needsReplan = cachedTravelRoute == null ||
                               !string.Equals(cachedTravelStartMap, netManager.CurrentMap, StringComparison.OrdinalIgnoreCase) ||
                               !string.Equals(cachedTravelDestMap, destinationMap, StringComparison.OrdinalIgnoreCase) ||
                               targetPosChanged ||
                               activeTravelWarp == null;

            if (needsReplan)
            {
                var walkProvider = RoWalkDataProvider.Instance;
                var walkData = walkProvider != null ? walkProvider.WalkData : null;

                cachedTravelStartMap = netManager.CurrentMap;
                cachedTravelDestMap = destinationMap;
                cachedTravelTargetPos = targetCellPos;
                kafraTravelPhase = 0;

                // When transitioning to a new map, clear stale activeTravelWarp from previous map
                if (activeTravelWarp != null && !string.Equals(activeTravelWarp.FromMap, netManager.CurrentMap, StringComparison.OrdinalIgnoreCase))
                {
                    activeTravelWarp = null;
                }

                cachedTravelRoute = WorldGraph.Instance.FindZoneAwareRoute(
                    netManager.CurrentMap,
                    player.CellPosition,
                    destinationMap,
                    targetCellPos,
                    (a, b) => MapNavMesh.Instance.IsReachable(a, b),
                    activeTravelWarp);

                if (cachedTravelRoute == null || cachedTravelRoute.Count == 0)
                {
                    InvalidateTravelPlan();
                    if (cachedTravelRoute == null)
                    {
                        // Isolated pocket / disconnected zone with no route out!
                        bool inTown = TownRoutineController.IsTownOrBaseMap(netManager.CurrentMap);
                        if (!inTown)
                        {
                            int wingId = InventoryHelper.FindFirstItemId(601, 12323);
                            if (wingId > 0 && now - lastTravelTime >= 2.0f)
                            {
                                netManager.SendUseItem(wingId);
                                lastTravelTime = now;
                                currentState = BotState.Fleeing;
                                Services.NpcInteractionHelper.CleanupNpcUi();
                                InvalidateTravelPlan();
                                BotEngine.Instance?.LogEvent($"[Navigation] Trapped in isolated zone on '{netManager.CurrentMap}'! Used Fly Wing (ID: {wingId}) to escape pocket.");
                                return true;
                            }
                        }
                        else
                        {
                            int bwingId = InventoryHelper.FindFirstItemId(602, 12324);
                            if (bwingId > 0 && now - lastTravelTime >= 2.0f)
                            {
                                netManager.SendUseItem(bwingId);
                                lastTravelTime = now;
                                Services.NpcInteractionHelper.CleanupNpcUi();
                                InvalidateTravelPlan();
                                BotEngine.Instance?.LogEvent($"[Navigation] Trapped in isolated zone in town '{netManager.CurrentMap}'! Used Butterfly Wing to return to save point.");
                                return true;
                            }
                        }

                        BotEngine.Instance?.LogEvent($"[Travel Warning] No valid warp route found from current zone on '{netManager.CurrentMap}' to '{destinationMap}'.");
                    }
                    return false;
                }

                activeTravelWarp = cachedTravelRoute[0];
                if (activeTravelWarp.IsNpcInteraction)
                {
                    activeTravelWarpTarget = activeTravelWarp.FromPos;
                    activeTravelWaypoints = null;
                    activeTravelWaypointIdx = 0;
                }
                else
                {
                    activeTravelWarpTarget = activeTravelWarp.GetWalkableTriggerTile(walkData, player.CellPosition);
                    var blockedIndices = WorldGraph.Instance.GetMapPortalTileIndices(netManager.CurrentMap, walkData.Width, activeTravelWarp);
                    activeTravelWaypoints = MapNavMesh.Instance.FindRouteWaypoints(player.CellPosition, activeTravelWarpTarget, 11, blockedIndices);
                    activeTravelWaypointIdx = 0;
                    BotEngine.Instance?.LogEvent($"[Travel] Planned route on '{netManager.CurrentMap}' to warp for '{activeTravelWarp.DestMap}' at ({activeTravelWarpTarget.x}, {activeTravelWarpTarget.y}) (Total: {cachedTravelRoute.Count} hops, {activeTravelWaypoints?.Count ?? 0} waypoints).");
                }
            }

            var nextHop = activeTravelWarp;

            // 2. KAFRA TELEPORT HANDLING
            if (nextHop.IsKafraTeleport)
            {
                var connectingWarps = WorldGraph.Instance.GetWarpsConnecting(netManager.CurrentMap, nextHop.DestMap);
                WarpConnection bestKafra = null;
                float bestKafraDist = float.MaxValue;
                foreach (var warp in connectingWarps)
                {
                    if (!warp.IsKafraTeleport) continue;
                    if (!MapNavMesh.Instance.IsReachable(player.CellPosition, warp.FromPos)) continue;
                    float d = Vector2.Distance(player.CellPosition, warp.FromPos);
                    if (d < bestKafraDist)
                    {
                        bestKafraDist = d;
                        bestKafra = warp;
                    }
                }
                if (bestKafra != null)
                {
                    nextHop = bestKafra;
                }

                if (nextHop.ZenyCost > 0)
                {
                    int curZeny = Services.BlacksmithHelper.GetCurrentZeny();
                    if (curZeny < nextHop.ZenyCost)
                    {
                        BotEngine.Instance?.LogEvent($"[Travel Warning] Insufficient Zeny for Kafra teleport to '{nextHop.DestMap}' (Cost: {nextHop.ZenyCost:N0}z, Have: {curZeny:N0}z). Aborting travel.");
                        InvalidateTravelPlan();
                        return false;
                    }
                }

                var kafraNpc = Services.NpcInteractionHelper.FindNearbyNpc(netManager, "Kafra", nextHop.FromPos, player.CellPosition);
                float distToKafra = Vector2.Distance(player.CellPosition, nextHop.FromPos);

                // Walk towards Kafra until in adjacent interaction range (<= 3.0 tiles) before clicking
                if (kafraNpc == null || distToKafra > 3.0f)
                {
                    if (kafraTravelPhase != 0)
                    {
                        Services.NpcInteractionHelper.CleanupNpcUi();
                        kafraTravelPhase = 0;
                    }
                    else
                    {
                        var cam = CameraFollower.Instance;
                        if (cam != null && ((cam.DialogPanel != null && cam.DialogPanel.activeSelf) || (cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf)))
                        {
                            Services.NpcInteractionHelper.CleanupNpcUi();
                        }
                    }

                    if (!player.IsMoving && now - lastTravelTime >= nextTravelDelay)
                    {
                        currentState = BotState.TravelingToTargetMap;
                        NavigateTowards(player.CellPosition, nextHop.FromPos, avoidPortals: true, hopDistance: 11, targetWarp: nextHop);
                        lastTravelTime = now;
                        nextTravelDelay = UnityEngine.Random.Range(0.20f, 0.38f);
                        BotEngine.Instance?.LogEvent($"[Travel] Walking towards Kafra for teleport to '{nextHop.DestMap}' (dist: {distToKafra:F1}).");
                    }
                    return true;
                }

                // In adjacent interaction range (<= 3.0 tiles away)!
                currentState = BotState.TravelingToTargetMap;
                if (kafraTravelPhase == 0)
                {
                    if (BotEngine.Instance != null && BotEngine.Instance.TownRoutine != null && (now - BotEngine.Instance.TownRoutine.LastCompletedTime < 1.0f))
                    {
                        return true;
                    }

                    if (player.IsMoving)
                    {
                        netManager.MovePlayer(player.CellPosition);
                    }

                    if (Services.NpcInteractionHelper.IsInNpcInteraction())
                    {
                        Services.NpcInteractionHelper.CancelOrEndNpcInteraction();
                    }
                    netManager.SendNpcClick(kafraNpc.Id);
                    kafraTravelPhase = 1;
                    lastKafraInteractionTime = now;
                    BotEngine.Instance?.LogEvent($"[Travel] Clicked Kafra '{kafraNpc.Name}' from {distToKafra:F1} tiles away (ID: {kafraNpc.Id}). Requesting teleport to '{nextHop.DestMap}'.");
                    return true;
                }
                else
                {
                    var cam = CameraFollower.Instance;
                    bool dialogOpen = cam != null && cam.DialogPanel != null && cam.DialogPanel.activeSelf;
                    bool optionOpen = cam != null && cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;

                    if (optionOpen)
                    {
                        if (now - lastKafraInteractionTime < 0.4f) return true;

                        var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(false);
                        if (buttons != null && buttons.Length > 0)
                        {
                            NpcOptionButton teleportBtn = null;
                            NpcOptionButton destBtn = null;

                            foreach (var btn in buttons)
                            {
                                if (btn == null) continue;
                                string text = btn.TextBox != null ? btn.TextBox.text : "";
                                if (text.IndexOf("Teleport", StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    teleportBtn = btn;
                                }
                                if (!string.IsNullOrEmpty(text) && text.IndexOf(nextHop.DestMap, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    destBtn = btn;
                                }
                            }

                            if (kafraTravelPhase == 1 && teleportBtn != null)
                            {
                                teleportBtn.OnClick();
                                kafraTravelPhase = 2;
                                lastKafraInteractionTime = now;
                                BotEngine.Instance?.LogEvent($"[Travel] Selected 'Teleport Service' (ID: {teleportBtn.Id}).");
                                return true;
                            }

                            if (kafraTravelPhase == 2 && destBtn != null)
                            {
                                destBtn.OnClick();
                                kafraTravelPhase = 3;
                                lastKafraInteractionTime = now;
                                BotEngine.Instance?.LogEvent($"[Travel] Clicked destination '{destBtn.TextBox?.text}' (ID: {destBtn.Id}) for '{nextHop.DestMap}'. Teleporting!");
                                return true;
                            }

                            if (kafraTravelPhase == 1)
                            {
                                foreach (var btn in buttons)
                                {
                                    if (btn != null && btn.Id == 2)
                                    {
                                        btn.OnClick();
                                        kafraTravelPhase = 2;
                                        lastKafraInteractionTime = now;
                                        BotEngine.Instance?.LogEvent("[Travel] Selected Option 2 ('Teleport Service').");
                                        return true;
                                    }
                                }
                            }
                            else if (kafraTravelPhase == 2)
                            {
                                foreach (var btn in buttons)
                                {
                                    if (btn != null && btn.Id == nextHop.KafraMenuOption)
                                    {
                                        btn.OnClick();
                                        kafraTravelPhase = 3;
                                        lastKafraInteractionTime = now;
                                        BotEngine.Instance?.LogEvent($"[Travel] Clicked option {btn.Id} for '{nextHop.DestMap}'. Teleporting!");
                                        return true;
                                    }
                                }
                            }
                        }
                        return true;
                    }

                    if (dialogOpen)
                    {
                        if (now - lastKafraInteractionTime >= 0.5f)
                        {
                            netManager.SendNpcAdvance();
                            lastKafraInteractionTime = now;
                            BotEngine.Instance?.LogEvent("[Travel] Advanced Kafra dialogue prompt.");
                        }
                        return true;
                    }

                    if (kafraTravelPhase == 1 && !dialogOpen && !optionOpen)
                    {
                        if (now - lastKafraInteractionTime >= 4.5f)
                        {
                            Services.NpcInteractionHelper.CleanupNpcUi();
                            kafraTravelPhase = 0;
                            lastKafraInteractionTime = now;
                            BotEngine.Instance?.LogEvent("[Travel] Kafra click timed out without response; cleanly retrying click.");
                        }
                    }
                    else if (kafraTravelPhase == 2 && !optionOpen)
                    {
                        if (now - lastKafraInteractionTime >= 4.5f)
                        {
                            Services.NpcInteractionHelper.CleanupNpcUi();
                            kafraTravelPhase = 0;
                            lastKafraInteractionTime = now;
                            BotEngine.Instance?.LogEvent("[Travel] Kafra destination menu timed out; cleanly retrying interaction.");
                        }
                    }
                    else if (kafraTravelPhase == 3)
                    {
                        if (now - lastKafraInteractionTime >= 6.0f)
                        {
                            Services.NpcInteractionHelper.CleanupNpcUi();
                            kafraTravelPhase = 0;
                            lastKafraInteractionTime = now;
                            BotEngine.Instance?.LogEvent("[Travel] Teleport timed out; retrying Kafra interaction.");
                        }
                    }
                    else if (now - lastKafraInteractionTime >= 5.0f)
                    {
                        Services.NpcInteractionHelper.CleanupNpcUi();
                        kafraTravelPhase = 0;
                        lastKafraInteractionTime = now;
                    }
                }
                return true;
            }

            // 2.5 CUSTOM NPC WARP TRANSPORT HANDLING (Sailors, Ships, Guides)
            if (nextHop.IsNpcWarp)
            {
                if (nextHop.ZenyCost > 0)
                {
                    int curZeny = Services.BlacksmithHelper.GetCurrentZeny();
                    if (curZeny < nextHop.ZenyCost)
                    {
                        BotEngine.Instance?.LogEvent($"[Travel Warning] Insufficient Zeny for {nextHop.NpcName} transport to '{nextHop.DestMap}' (Cost: {nextHop.ZenyCost:N0}z, Have: {curZeny:N0}z). Aborting travel.");
                        InvalidateTravelPlan();
                        return false;
                    }
                }

                var npc = Services.NpcInteractionHelper.FindNearbyNpc(netManager, nextHop.NpcName, nextHop.FromPos, player.CellPosition);
                float distToNpc = Vector2.Distance(player.CellPosition, nextHop.FromPos);

                // Walk towards NPC until in adjacent interaction range (<= 3.0 tiles) before clicking
                if (npc == null || distToNpc > 3.0f)
                {
                    if (!player.IsMoving && now - lastTravelTime >= nextTravelDelay)
                    {
                        currentState = BotState.TravelingToTargetMap;
                        NavigateTowards(player.CellPosition, nextHop.FromPos, avoidPortals: true, hopDistance: 11, targetWarp: nextHop);
                        lastTravelTime = now;
                        nextTravelDelay = UnityEngine.Random.Range(0.20f, 0.38f);
                        BotEngine.Instance?.LogEvent($"[Travel] Walking towards {nextHop.NpcName} for transport to '{nextHop.DestMap}' (dist: {distToNpc:F1}).");
                    }
                    return true;
                }

                // In adjacent interaction range (<= 3.0 tiles away)!
                currentState = BotState.TravelingToTargetMap;
                if (kafraTravelPhase == 0)
                {
                    if (BotEngine.Instance != null && BotEngine.Instance.TownRoutine != null && (now - BotEngine.Instance.TownRoutine.LastCompletedTime < 1.0f))
                    {
                        return true;
                    }

                    if (player.IsMoving)
                    {
                        netManager.MovePlayer(player.CellPosition);
                    }

                    if (Services.NpcInteractionHelper.IsInNpcInteraction())
                    {
                        Services.NpcInteractionHelper.CancelOrEndNpcInteraction();
                    }
                    netManager.SendNpcClick(npc.Id);
                    kafraTravelPhase = 1;
                    lastKafraInteractionTime = now;
                    BotEngine.Instance?.LogEvent($"[Travel] Clicked {nextHop.NpcName} '{npc.Name}' from {distToNpc:F1} tiles away (ID: {npc.Id}). Requesting transport to '{nextHop.DestMap}'.");
                    return true;
                }
                else
                {
                    var cam = CameraFollower.Instance;
                    bool dialogOpen = cam != null && cam.DialogPanel != null && cam.DialogPanel.activeSelf;
                    bool optionOpen = cam != null && cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;

                    if (optionOpen)
                    {
                        if (now - lastKafraInteractionTime < 0.4f) return true;

                        var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(false);
                        if (buttons != null && buttons.Length > 0)
                        {
                            NpcOptionButton targetBtn = null;

                            // 1. Text substring match
                            if (!string.IsNullOrEmpty(nextHop.OptionTextMatch))
                            {
                                foreach (var btn in buttons)
                                {
                                    if (btn == null) continue;
                                    string text = btn.TextBox != null ? btn.TextBox.text : "";
                                    if (text.IndexOf(nextHop.OptionTextMatch, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        targetBtn = btn;
                                        break;
                                    }
                                }
                            }

                            // 2. DestMap substring match fallback
                            if (targetBtn == null && !string.IsNullOrEmpty(nextHop.DestMap))
                            {
                                foreach (var btn in buttons)
                                {
                                    if (btn == null) continue;
                                    string text = btn.TextBox != null ? btn.TextBox.text : "";
                                    if (text.IndexOf(nextHop.DestMap, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        targetBtn = btn;
                                        break;
                                    }
                                }
                            }

                            // 3. Option Index fallback
                            if (targetBtn == null && nextHop.NpcMenuOption >= 0)
                            {
                                foreach (var btn in buttons)
                                {
                                    if (btn != null && btn.Id == nextHop.NpcMenuOption)
                                    {
                                        targetBtn = btn;
                                        break;
                                    }
                                }
                            }

                            if (targetBtn != null)
                            {
                                targetBtn.OnClick();
                                kafraTravelPhase = 2;
                                lastKafraInteractionTime = now;
                                BotEngine.Instance?.LogEvent($"[Travel] Clicked option '{targetBtn.TextBox?.text}' (ID: {targetBtn.Id}) for transport to '{nextHop.DestMap}'.");
                                return true;
                            }
                        }
                        return true;
                    }

                    if (dialogOpen)
                    {
                        if (now - lastKafraInteractionTime >= 0.5f)
                        {
                            netManager.SendNpcAdvance();
                            lastKafraInteractionTime = now;
                            BotEngine.Instance?.LogEvent($"[Travel] Advanced {nextHop.NpcName} dialogue prompt.");
                        }
                        return true;
                    }

                    if (kafraTravelPhase == 1 && !dialogOpen && !optionOpen)
                    {
                        if (now - lastKafraInteractionTime >= 4.5f)
                        {
                            Services.NpcInteractionHelper.CleanupNpcUi();
                            kafraTravelPhase = 0;
                            lastKafraInteractionTime = now;
                            BotEngine.Instance?.LogEvent($"[Travel] {nextHop.NpcName} click timed out without response; cleanly retrying click.");
                        }
                    }
                    else if (kafraTravelPhase == 2)
                    {
                        if (now - lastKafraInteractionTime >= 5.0f)
                        {
                            Services.NpcInteractionHelper.CleanupNpcUi();
                            kafraTravelPhase = 0;
                            lastKafraInteractionTime = now;
                            BotEngine.Instance?.LogEvent($"[Travel] Transport to '{nextHop.DestMap}' timed out; retrying {nextHop.NpcName} interaction.");
                        }
                    }
                    else if (now - lastKafraInteractionTime >= 5.0f)
                    {
                        Services.NpcInteractionHelper.CleanupNpcUi();
                        kafraTravelPhase = 0;
                        lastKafraInteractionTime = now;
                    }
                }
                return true;
            }

            // 3. STANDARD PORTAL TRAVEL
            kafraTravelPhase = 0;

            bool isMoving = player.IsMoving || player.IsWalking;

            if (!isMoving && player.CellPosition == lastRecordedPosition && currentTravelStepTarget != Vector2Int.zero)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer > 1.8f)
                {
                    BotEngine.Instance?.LogEvent($"[Travel] Stuck at ({player.CellPosition.x}, {player.CellPosition.y}) for > 1.8s. Re-planning travel route from current coordinate.");
                    InvalidateTravelPlan();
                    stuckTimer = 0f;
                    return true;
                }
            }
            else
            {
                lastRecordedPosition = player.CellPosition;
                stuckTimer = 0f;
            }

            float distToTravelStep = currentTravelStepTarget != Vector2Int.zero
                ? Vector2.Distance(player.CellPosition, currentTravelStepTarget)
                : float.MaxValue;

            // Pipelined lookahead pre-click: when within 3.8 tiles of current step destination and at least 0.6s since last dispatch
            bool travelPreClick = isMoving && currentTravelStepTarget != Vector2Int.zero && distToTravelStep <= 3.8f && (now - lastTravelTime >= 0.6f);
            bool travelStoppedReady = !isMoving && (now - lastTravelTime >= nextTravelDelay);

            if (travelPreClick || travelStoppedReady)
            {
                string nextDestMap = nextHop.DestMap;
                Vector2Int warpPos = activeTravelWarpTarget != Vector2Int.zero ? activeTravelWarpTarget : nextHop.GetWalkableTriggerTile(null, player.CellPosition);
                float distToWarp = Vector2.Distance(player.CellPosition, warpPos);
                bool isInsideWarp = nextHop.IsInsideWarp(player.CellPosition);

                if (BotConfigManager.Current.AvoidTrackedBosses &&
                    BossTrackingService.Instance.IsWithinBossZone(netManager.CurrentMap, warpPos, BotConfigManager.Current.BossAvoidanceRadius))
                {
                    BotEngine.Instance?.LogEvent($"[Travel] Portal to '{nextDestMap}' at ({warpPos.x}, {warpPos.y}) is inside tracked boss exclusion zone! Canceling travel to wander safely in open area.");
                    InvalidateTravelPlan();
                    currentState = BotState.Wandering;
                    return true;
                }

                // If standing inside portal bounding box or within direct stepping distance, step right into it
                if (isInsideWarp || distToWarp <= 1.8f)
                {
                    currentState = BotState.TravelingToTargetMap;
                    netManager.MovePlayer(warpPos);
                    currentTravelStepTarget = warpPos;
                    lastTravelTime = now;
                    nextTravelDelay = 0.25f;
                    BotEngine.Instance?.LogEvent($"[Travel] Stepping into portal for '{nextDestMap}' at ({warpPos.x}, {warpPos.y})!");
                    return true;
                }

                // Advance waypoints if player is near current waypoint
                Vector2Int targetWp = warpPos;
                if (activeTravelWaypoints != null && activeTravelWaypoints.Count > 0)
                {
                    while (activeTravelWaypointIdx < activeTravelWaypoints.Count - 1 &&
                           Vector2.Distance(player.CellPosition, activeTravelWaypoints[activeTravelWaypointIdx]) <= 3.5f)
                    {
                        activeTravelWaypointIdx++;
                    }

                    targetWp = activeTravelWaypoints[activeTravelWaypointIdx];
                }

                currentState = BotState.TravelingToTargetMap;
                if (SafeMoveTowards(player.CellPosition, targetWp, avoidPortals: true, forwardOnly: true, out Vector2Int stepDispatched, targetWarp: activeTravelWarp))
                {
                    currentTravelStepTarget = stepDispatched;
                    lastTravelTime = now;
                    nextTravelDelay = isMoving ? 0.0f : UnityEngine.Random.Range(0.12f, 0.20f);
                    BotEngine.Instance?.LogEvent($"[Travel] Moving towards warp for '{nextDestMap}' at ({warpPos.x}, {warpPos.y}) [step: ({stepDispatched.x}, {stepDispatched.y}), dist: {distToWarp:F1}]. Route: {cachedTravelRoute.Count} map(s) remaining.");
                    return true;
                }
                else
                {
                    // Waypoint blocked, fallback to direct navigate towards warp
                    if (NavigateTowards(player.CellPosition, warpPos, avoidPortals: true, hopDistance: 11, out Vector2Int directStep, targetWarp: activeTravelWarp))
                    {
                        currentTravelStepTarget = directStep;
                        lastTravelTime = now;
                        nextTravelDelay = isMoving ? 0.0f : UnityEngine.Random.Range(0.12f, 0.20f);
                        return true;
                    }
                    else
                    {
                        if (BotConfigManager.Current.AvoidTrackedBosses && BossTrackingService.Instance.HasTrackedBosses(netManager.CurrentMap))
                        {
                            BotEngine.Instance?.LogEvent($"[Travel] Path to portal for '{nextDestMap}' is blocked by tracked boss exclusion zone! Canceling travel to wander safely in open area.");
                            InvalidateTravelPlan();
                            currentState = BotState.Wandering;
                            return true;
                        }
                    }
                }
            }
            return true;
        }

        public void ProcessWander(NetworkManager netManager, ServerControllable player, float now, ref BotState currentState)
        {
            if (!BotConfigManager.Current.AutoWander) return;

            bool isMoving = player.IsMoving || player.IsWalking;

            if (wasMovingLastFrame && !isMoving)
            {
                playerArrivalTime = now;
            }
            wasMovingLastFrame = isMoving;

            if (!isMoving && player.CellPosition == lastRecordedPosition && currentExplorationWaypoint != Vector2Int.zero)
            {
                stuckTimer += Time.deltaTime;
                if (stuckTimer > 1.8f)
                {
                    MapHeatmap.Instance.BlacklistSector(currentExplorationWaypoint, 30f);
                    currentExplorationWaypoint = Vector2Int.zero;
                    currentStepTarget = Vector2Int.zero;
                    stuckTimer = 0f;
                }
            }
            else
            {
                lastRecordedPosition = player.CellPosition;
                stuckTimer = 0f;
            }

            currentState = BotState.Wandering;

            // Pipelined lookahead pre-click:
            // Issue next waypoint when within 3.8 tiles of current step to maintain continuous uninterrupted movement
            float distToStep = currentStepTarget != Vector2Int.zero
                ? Vector2.Distance(player.CellPosition, currentStepTarget)
                : float.MaxValue;

            // Pacing guard: never allow wander step evaluation faster than 0.22s
            if (now - lastWanderStepTime < 0.22f) return;

            bool isPreClickWindow = isMoving && currentStepTarget != Vector2Int.zero && distToStep <= 3.8f && (now - lastWanderStepTime >= 0.5f);
            bool isStoppedReady = !isMoving && (now - lastWanderStepTime >= nextWanderDelay);

            if (isPreClickWindow || isStoppedReady)
            {
                float distToWaypoint = currentExplorationWaypoint != Vector2Int.zero
                    ? Vector2.Distance(player.CellPosition, currentExplorationWaypoint)
                    : 0f;

                bool waypointInBossZone = currentExplorationWaypoint != Vector2Int.zero &&
                                          BotConfigManager.Current.AvoidTrackedBosses &&
                                          BossTrackingService.Instance.IsWithinBossZone(netManager.CurrentMap, currentExplorationWaypoint, BotConfigManager.Current.BossAvoidanceRadius);

                if (currentExplorationWaypoint == Vector2Int.zero || waypointInBossZone || distToWaypoint <= 10f || (now - waypointAssignedTime > 45f))
                {
                    if (currentExplorationWaypoint != Vector2Int.zero)
                    {
                        Vector2 h = ((Vector2)currentExplorationWaypoint - player.CellPosition).normalized;
                        if (h != Vector2.zero) lastWanderHeading = h;
                    }

                    currentExplorationWaypoint = MapHeatmap.Instance.FindColdestSectorTarget(
                        netManager.CurrentMap,
                        player.CellPosition,
                        BotConfigManager.Current.PortalSafetyRadius,
                        lastWanderHeading);

                    waypointAssignedTime = now;
                    distToWaypoint = Vector2.Distance(player.CellPosition, currentExplorationWaypoint);
                    Vector2 newH = ((Vector2)currentExplorationWaypoint - player.CellPosition).normalized;
                    if (newH != Vector2.zero) lastWanderHeading = newH;

                    BotEngine.Instance?.LogEvent($"Exploring toward cold sector ({currentExplorationWaypoint.x}, {currentExplorationWaypoint.y}) - Dist: {distToWaypoint:F0} tiles");
                }

                if (NavigateTowards(player.CellPosition, currentExplorationWaypoint, BotConfigManager.Current.AvoidPortalsWhileWandering, 11, out Vector2Int stepDispatched))
                {
                    currentStepTarget = stepDispatched;
                    lastWanderStepTime = now;

                    // Stride in motion: paced 0.30s delay. Brief glance when stopping at a destination
                    float roll = UnityEngine.Random.value;
                    nextWanderDelay = isMoving ? 0.30f : ((roll < 0.03f) ? UnityEngine.Random.Range(0.40f, 0.60f) : UnityEngine.Random.Range(0.22f, 0.32f));
                    return;
                }
                else
                {
                    MapHeatmap.Instance.BlacklistSector(currentExplorationWaypoint, 30f);
                    currentExplorationWaypoint = Vector2Int.zero;
                    currentStepTarget = Vector2Int.zero;
                }
            }
        }
    }
}
