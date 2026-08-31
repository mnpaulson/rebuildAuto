using System;
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

        private readonly Vector2Int[] tempPath = new Vector2Int[32];

        public Vector2Int CurrentExplorationWaypoint => currentExplorationWaypoint;
        public float WaypointAssignedTime => waypointAssignedTime;
        public Vector2 LastWanderHeading => lastWanderHeading;

        public void ResetWander()
        {
            currentExplorationWaypoint = Vector2Int.zero;
            currentStepTarget = Vector2Int.zero;
            currentTravelStepTarget = Vector2Int.zero;
            waypointAssignedTime = 0f;
            stuckTimer = 0f;
            kafraTravelPhase = 0;
            Services.NpcInteractionHelper.CleanupNpcUi();
        }

        /// <summary>
        /// Finds the optimal walkable attack tile within attackRange (ideally 1.5 - 2.0 tiles away)
        /// on the side of the monster facing the player, with natural human angle variance.
        /// </summary>
        public Vector2Int GetAttackPosition(Vector2Int playerPos, Vector2Int monsterPos, float attackRange = 2.0f)
        {
            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider == null || walkProvider.WalkData == null) return monsterPos;
            var walkData = walkProvider.WalkData;

            ushort playerZone = MapNavMesh.Instance.GetZoneId(playerPos);

            var candidates = new System.Collections.Generic.List<(Vector2Int tile, float distToPlayer)>();

            // Check ring around monster: distances 1 and 2 tiles
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    float distToMonster = Mathf.Sqrt(dx * dx + dy * dy);
                    if (distToMonster > attackRange + 0.2f) continue;

                    Vector2Int candidate = new Vector2Int(monsterPos.x + dx, monsterPos.y + dy);
                    if (candidate.x < 0 || candidate.y < 0 || candidate.x >= walkData.Width || candidate.y >= walkData.Height)
                        continue;

                    if (!walkData.CellWalkable(candidate.x, candidate.y))
                        continue;

                    if (playerZone != 0 && MapNavMesh.Instance.GetZoneId(candidate) != playerZone)
                        continue;

                    string currentMap = NetworkManager.Instance != null ? NetworkManager.Instance.CurrentMap : "";
                    if (BotConfigManager.Current.AvoidPortalsWhileWandering && !string.IsNullOrEmpty(currentMap))
                    {
                        if (WorldGraph.Instance.IsNearPortal(currentMap, candidate, BotConfigManager.Current.PortalSafetyRadius))
                            continue;
                    }

                    float distToPlayer = Vector2.Distance(playerPos, candidate);
                    candidates.Add((candidate, distToPlayer));
                }
            }

            if (candidates.Count == 0)
            {
                string currentMap = NetworkManager.Instance != null ? NetworkManager.Instance.CurrentMap : "";
                if (BotConfigManager.Current.AvoidPortalsWhileWandering && !string.IsNullOrEmpty(currentMap))
                {
                    if (WorldGraph.Instance.IsNearPortal(currentMap, monsterPos, BotConfigManager.Current.PortalSafetyRadius))
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
        public bool NavigateTowards(Vector2Int currentPos, Vector2Int destination, bool avoidPortals = false, int hopDistance = 10)
        {
            if (currentPos == destination) return true;

            var netManager = NetworkManager.Instance;
            if (netManager == null) return false;

            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider != null && walkProvider.WalkData != null)
            {
                var walkData = walkProvider.WalkData;
                int chebyshevDist = Math.Max(Math.Abs(destination.x - currentPos.x), Math.Abs(destination.y - currentPos.y));

                // 1. Direct path check: if within 12 tiles and walkable in a direct line
                if (chebyshevDist <= 12 && walkData.CellWalkable(destination.x, destination.y))
                {
                    int directSteps = Pathfinder.GetPath(walkData, currentPos, destination, tempPath);
                    if (directSteps > 0)
                    {
                        return SafeMoveTowards(currentPos, destination, avoidPortals, forwardOnly: true);
                    }
                }

                // 2. Obstacle or corner between current position and destination:
                // Use MapNavMesh to extract route waypoints around walls and corridors
                var routeWaypoints = MapNavMesh.Instance.FindRouteWaypoints(currentPos, destination, hopDistance);
                if (routeWaypoints != null && routeWaypoints.Count > 0)
                {
                    Vector2Int stepTarget = routeWaypoints[0];
                    return SafeMoveTowards(currentPos, stepTarget, avoidPortals, forwardOnly: false);
                }
            }

            // 3. Fallback to direct safe movement
            return SafeMoveTowards(currentPos, destination, avoidPortals, forwardOnly: false);
        }

        public bool SafeMoveTowards(Vector2Int currentPos, Vector2Int destination, bool avoidPortals = false, bool forwardOnly = false)
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null) return false;

            var player = CameraFollower.Instance?.Target?.GetComponent<ServerControllable>();
            if (BotEngine.Instance != null && BotEngine.Instance.Survival != null && BotEngine.Instance.Survival.IsPlayerSitting(player))
            {
                netManager.ChangePlayerSitStand(false);
            }

            if (currentPos == destination) return true;

            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider != null && walkProvider.WalkData != null)
            {
                var walkData = walkProvider.WalkData;
                Vector2 dir = destination - currentPos;
                float totalDist = dir.magnitude;

                // 1. If destination is close (within 14 tiles), check if directly reachable
                int chebyshevDist = Math.Max(Math.Abs(destination.x - currentPos.x), Math.Abs(destination.y - currentPos.y));
                if (totalDist <= 14f && chebyshevDist <= 15)
                {
                    Vector2Int directTarget = destination;
                    bool directWalkable = walkData.CellWalkable(destination.x, destination.y);

                    if (!directWalkable)
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

                    if (directWalkable)
                    {
                        int steps = Pathfinder.GetPath(walkData, currentPos, directTarget, tempPath);
                        if (steps > 0)
                        {
                            bool portalBlocked = false;
                            if (avoidPortals)
                            {
                                for (int s = 0; s < steps; s++)
                                {
                                    if (WorldGraph.Instance.IsNearPortal(netManager.CurrentMap, tempPath[s], BotConfigManager.Current.PortalSafetyRadius))
                                    {
                                        portalBlocked = true;
                                        break;
                                    }
                                }
                            }

                            if (!portalBlocked)
                            {
                                netManager.MovePlayer(directTarget);
                                return true;
                            }
                        }
                    }
                }

                // 2. Multi-angle progressive distance probe towards destination
                float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                float[] angleOffsets = forwardOnly
                    ? new float[] { 0f, 20f, -20f, 40f, -40f }
                    : new float[] { 0f, 20f, -20f, 40f, -40f, 60f, -60f, 80f, -80f };

                int maxStep = Mathf.Clamp(Mathf.RoundToInt(totalDist), 3, 12);
                int[] stepDistances = (maxStep >= 10)
                    ? new int[] { maxStep, 8, 6, 4, 2 }
                    : (maxStep >= 6)
                        ? new int[] { maxStep, 4, 3, 2, 1 }
                        : new int[] { maxStep, 2, 1 };

                foreach (int d in stepDistances)
                {
                    foreach (float angleOffset in angleOffsets)
                    {
                        float rad = (baseAngle + angleOffset) * Mathf.Deg2Rad;
                        Vector2 probeDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
                        Vector2Int candidate = currentPos + new Vector2Int(Mathf.RoundToInt(probeDir.x * d), Mathf.RoundToInt(probeDir.y * d));

                        if (candidate == currentPos) continue;
                        if (candidate.x < 0 || candidate.y < 0 || candidate.x >= walkData.Width || candidate.y >= walkData.Height) continue;

                        if (avoidPortals && WorldGraph.Instance.IsNearPortal(netManager.CurrentMap, candidate, BotConfigManager.Current.PortalSafetyRadius))
                            continue;

                        if (walkData.CellWalkable(candidate.x, candidate.y))
                        {
                            int steps = Pathfinder.GetPath(walkData, currentPos, candidate, tempPath);
                            if (steps > 0)
                            {
                                bool portalBlocked = false;
                                if (avoidPortals)
                                {
                                    for (int s = 0; s < steps; s++)
                                    {
                                        if (WorldGraph.Instance.IsNearPortal(netManager.CurrentMap, tempPath[s], BotConfigManager.Current.PortalSafetyRadius))
                                        {
                                            portalBlocked = true;
                                            break;
                                        }
                                    }
                                }

                                if (!portalBlocked)
                                {
                                    netManager.MovePlayer(candidate);
                                    BotEngine.Instance?.LogEvent($"[Move] Step verified to ({candidate.x}, {candidate.y}) [offset: {angleOffset:F0}°, dist: {d}] ({steps} A* steps).");
                                    return true;
                                }
                            }
                        }
                    }
                }

                BotEngine.Instance?.LogEvent($"[Move] Path blocked from ({currentPos.x}, {currentPos.y}) towards destination ({destination.x}, {destination.y})!");
                return false;
            }
            else
            {
                // Fallback without walk data
                Vector2 diff = destination - currentPos;
                float dist = Mathf.Min(diff.magnitude, 12f);
                Vector2 step = diff.normalized * dist;
                Vector2Int stepTarget = currentPos + new Vector2Int(Mathf.RoundToInt(step.x), Mathf.RoundToInt(step.y));
                netManager.MovePlayer(stepTarget);
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
                kafraTravelPhase = 0;
                return false;
            }

            bool isSameMap = string.Equals(netManager.CurrentMap, destinationMap, StringComparison.OrdinalIgnoreCase);

            // If start and target are on the same map:
            // Check if player can reach targetCellPos locally
            if (isSameMap)
            {
                if (!targetCellPos.HasValue || MapNavMesh.Instance.IsReachable(player.CellPosition, targetCellPos.Value))
                {
                    kafraTravelPhase = 0;
                    return false;
                }
            }

            if (!BotConfigManager.Current.AutoTravel && !isTownRoutine)
            {
                return false;
            }

            var route = WorldGraph.Instance.FindZoneAwareRoute(
                netManager.CurrentMap,
                player.CellPosition,
                destinationMap,
                targetCellPos,
                (a, b) => MapNavMesh.Instance.IsReachable(a, b));

            if (route == null || route.Count == 0)
            {
                if (route == null)
                {
                    // Isolated pocket / disconnected zone with no route out!
                    // Solution 2: Escape via Fly Wing (or Butterfly Wing in town)
                    bool inTown = TownRoutineController.IsTownMap(netManager.CurrentMap);
                    if (!inTown)
                    {
                        int wingId = InventoryHelper.FindFirstItemId(601, 12323);
                        if (wingId > 0 && now - lastTravelTime >= 2.0f)
                        {
                            netManager.SendUseItem(wingId);
                            lastTravelTime = now;
                            currentState = BotState.Fleeing;
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
                            BotEngine.Instance?.LogEvent($"[Navigation] Trapped in isolated zone in town '{netManager.CurrentMap}'! Used Butterfly Wing to return to save point.");
                            return true;
                        }
                    }

                    BotEngine.Instance?.LogEvent($"[Travel Warning] No valid warp route found from current zone on '{netManager.CurrentMap}' to '{destinationMap}'.");
                }
                return false;
            }

            var nextHop = route[0];

            // 1. KAFRA TELEPORT HANDLING
            if (nextHop.IsKafraTeleport)
            {
                // Resolve to closest Kafra in current map if multiple exist (e.g. South Morroc vs North Morroc)
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

                var kafraNpc = Services.NpcInteractionHelper.FindNearbyNpc(netManager, "Kafra", nextHop.FromPos, player.CellPosition);

                // If Kafra is not yet visible in entity list, walk towards her
                if (kafraNpc == null)
                {
                    float distToKafra = Vector2.Distance(player.CellPosition, nextHop.FromPos);
                    if (!player.IsMoving && now - lastTravelTime >= nextTravelDelay)
                    {
                        currentState = BotState.TravelingToTargetMap;
                        NavigateTowards(player.CellPosition, nextHop.FromPos, avoidPortals: false, hopDistance: 11);
                        lastTravelTime = now;
                        nextTravelDelay = UnityEngine.Random.Range(0.20f, 0.38f);
                        BotEngine.Instance?.LogEvent($"[Travel] Walking towards Kafra for teleport to '{nextHop.DestMap}' (dist: {distToKafra:F1}).");
                    }
                    return true;
                }

                // In visual range! Interacting with Kafra NPC
                currentState = BotState.TravelingToTargetMap;
                if (kafraTravelPhase == 0)
                {
                    // If town routine just finished, wait 1.0s for server to finish closing previous interaction
                    if (BotEngine.Instance != null && BotEngine.Instance.TownRoutine != null && (now - BotEngine.Instance.TownRoutine.LastCompletedTime < 1.0f))
                    {
                        return true;
                    }

                    if (player.IsMoving)
                    {
                        netManager.MovePlayer(player.CellPosition);
                    }

                    Services.NpcInteractionHelper.CleanupNpcUi();
                    netManager.SendNpcClick(kafraNpc.Id);
                    kafraTravelPhase = 1;
                    lastKafraInteractionTime = now;
                    BotEngine.Instance?.LogEvent($"[Travel] Spotted & clicked Kafra '{kafraNpc.Name}' from {Vector2.Distance(player.CellPosition, kafraNpc.CellPosition):F1} tiles away (ID: {kafraNpc.Id}). Requesting teleport to '{nextHop.DestMap}'.");
                }
                else
                {
                    var cam = CameraFollower.Instance;
                    bool dialogOpen = cam != null && cam.DialogPanel != null && cam.DialogPanel.activeSelf;
                    bool optionOpen = cam != null && cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;

                    // 1. If an option menu is open, handle the menu selection FIRST!
                    if (optionOpen)
                    {
                        // Human reading delay before clicking options
                        if (now - lastKafraInteractionTime < BotConstants.HumanReadDelay) return true;

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

                            // If Main Menu is on screen (phase 1), click Teleport Service!
                            if (kafraTravelPhase == 1 && teleportBtn != null)
                            {
                                teleportBtn.OnClick();
                                kafraTravelPhase = 2; // Advanced to destination phase
                                lastKafraInteractionTime = now + UnityEngine.Random.Range(0.7f, 1.1f);
                                BotEngine.Instance?.LogEvent($"[Travel] Selected 'Teleport Service' (ID: {teleportBtn.Id}).");
                                return true;
                            }

                            // If Destination Menu is on screen (phase 2):
                            if (kafraTravelPhase == 2 && destBtn != null)
                            {
                                destBtn.OnClick();
                                kafraTravelPhase = 3; // Teleport issued!
                                lastKafraInteractionTime = now + 2.5f;
                                BotEngine.Instance?.LogEvent($"[Travel] Clicked destination '{destBtn.TextBox?.text}' (ID: {destBtn.Id}) for '{nextHop.DestMap}'. Teleporting!");
                                return true;
                            }

                            // Fallback by ID if text matching did not find it
                            if (kafraTravelPhase == 1)
                            {
                                foreach (var btn in buttons)
                                {
                                    if (btn != null && btn.Id == 2)
                                    {
                                        btn.OnClick();
                                        kafraTravelPhase = 2;
                                        lastKafraInteractionTime = now + UnityEngine.Random.Range(0.7f, 1.1f);
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
                                        lastKafraInteractionTime = now + 2.5f;
                                        BotEngine.Instance?.LogEvent($"[Travel] Clicked option {btn.Id} for '{nextHop.DestMap}'. Teleporting!");
                                        return true;
                                    }
                                }
                            }
                        }
                        return true;
                    }

                    // 2. Otherwise, if a dialogue box is open without an option menu, advance it!
                    if (dialogOpen)
                    {
                        if (now - lastKafraInteractionTime >= BotConstants.HumanDialogAdvanceDelay)
                        {
                            netManager.SendNpcAdvance();
                            lastKafraInteractionTime = now;
                            BotEngine.Instance?.LogEvent("[Travel] Advanced Kafra dialogue prompt.");
                        }
                        return true;
                    }

                    // 3. Handle timeouts and stalled interactions cleanly
                    if (kafraTravelPhase == 1 && !dialogOpen && !optionOpen)
                    {
                        if (now - lastKafraInteractionTime >= 1.5f)
                        {
                            Services.NpcInteractionHelper.CleanupNpcUi();
                            kafraTravelPhase = 0;
                            lastKafraInteractionTime = now + 0.5f;
                            BotEngine.Instance?.LogEvent("[Travel] Kafra click timed out without response; cleanly retrying click.");
                        }
                    }
                    else if (kafraTravelPhase == 2 && !optionOpen)
                    {
                        if (now - lastKafraInteractionTime >= 2.0f)
                        {
                            Services.NpcInteractionHelper.CleanupNpcUi();
                            kafraTravelPhase = 0;
                            lastKafraInteractionTime = now + 0.5f;
                            BotEngine.Instance?.LogEvent("[Travel] Kafra destination menu timed out; cleanly retrying interaction.");
                        }
                    }
                    else if (kafraTravelPhase == 3)
                    {
                        // Waiting for teleport map change
                        if (now - lastKafraInteractionTime >= 4.0f)
                        {
                            Services.NpcInteractionHelper.CleanupNpcUi();
                            kafraTravelPhase = 0;
                            BotEngine.Instance?.LogEvent("[Travel] Teleport timed out; retrying Kafra interaction.");
                        }
                    }
                    else if (now - lastKafraInteractionTime >= 3.0f)
                    {
                        // Entire interaction stalled - cleanly reset UI and interaction locks before re-clicking
                        Services.NpcInteractionHelper.CleanupNpcUi();
                        kafraTravelPhase = 0;
                        lastKafraInteractionTime = now + 0.5f;
                        BotEngine.Instance?.LogEvent("[Travel] Kafra interaction stalled; cleanly closed dialogue/portrait and retrying click.");
                    }
                }
                return true;
            }

            // Not a Kafra warp: reset kafra state
            kafraTravelPhase = 0;

            // 2. STANDARD PORTAL TRAVEL
            float distToTravelStep = currentTravelStepTarget != Vector2Int.zero
                ? Vector2.Distance(player.CellPosition, currentTravelStepTarget)
                : 0f;

            bool travelPreClick = player.IsMoving && currentTravelStepTarget != Vector2Int.zero && distToTravelStep <= 2.2f;
            bool travelStoppedReady = !player.IsMoving && (now - lastTravelTime >= nextTravelDelay);

            if (travelPreClick || travelStoppedReady)
            {
                var bestWarp = nextHop;
                string nextDestMap = bestWarp.DestMap;
                var walkProvider = RoWalkDataProvider.Instance;
                Vector2Int warpPos = bestWarp.GetWalkableTriggerTile(walkProvider != null ? walkProvider.WalkData : null, player.CellPosition);
                float distToWarp = Vector2.Distance(player.CellPosition, warpPos);
                bool isInsideWarp = bestWarp.IsInsideWarp(player.CellPosition);

                // If standing inside portal bounding box or within direct stepping distance, step right into it
                if (isInsideWarp || distToWarp <= 1.8f)
                {
                    currentState = BotState.TravelingToTargetMap;
                    netManager.MovePlayer(warpPos);
                    lastTravelTime = now;
                    nextTravelDelay = 0.3f;
                    BotEngine.Instance?.LogEvent($"[Travel] Stepping into portal for '{nextDestMap}' at ({warpPos.x}, {warpPos.y})!");
                    return true;
                }

                var travelWaypoints = MapNavMesh.Instance.FindRouteWaypoints(player.CellPosition, warpPos, 11);
                Vector2Int travelTarget = (travelWaypoints != null && travelWaypoints.Count > 0)
                    ? travelWaypoints[0]
                    : warpPos;

                currentState = BotState.TravelingToTargetMap;
                SafeMoveTowards(player.CellPosition, travelTarget, false);
                currentTravelStepTarget = travelTarget;
                lastTravelTime = now;
                nextTravelDelay = UnityEngine.Random.Range(0.20f, 0.38f);
                BotEngine.Instance?.LogEvent($"[Travel] Moving towards warp for '{nextDestMap}' at ({warpPos.x}, {warpPos.y}) [step: ({travelTarget.x}, {travelTarget.y}), dist: {distToWarp:F1}]. Route: {route.Count} map(s) remaining.");
                return true;
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

            // Fluid pre-clicking check:
            // Issue next waypoint when within 2.2 tiles of current step to maintain uninterrupted movement,
            // or after natural reaction cadence when stopped
            float distToStep = currentStepTarget != Vector2Int.zero
                ? Vector2.Distance(player.CellPosition, currentStepTarget)
                : 0f;

            bool isPreClickWindow = isMoving && currentStepTarget != Vector2Int.zero && distToStep <= 2.2f;
            bool isStoppedReady = !isMoving && (now - lastWanderStepTime >= nextWanderDelay);

            if (isPreClickWindow || isStoppedReady)
            {
                float distToWaypoint = currentExplorationWaypoint != Vector2Int.zero
                    ? Vector2.Distance(player.CellPosition, currentExplorationWaypoint)
                    : 0f;

                if (currentExplorationWaypoint == Vector2Int.zero || distToWaypoint <= 10f || (now - waypointAssignedTime > 45f))
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

                Vector2Int stepTarget = currentExplorationWaypoint;
                var routeWaypoints = MapNavMesh.Instance.FindRouteWaypoints(player.CellPosition, currentExplorationWaypoint, 11);
                if (routeWaypoints != null && routeWaypoints.Count > 0)
                {
                    stepTarget = routeWaypoints[0];
                }

                if (SafeMoveTowards(player.CellPosition, stepTarget, BotConfigManager.Current.AvoidPortalsWhileWandering))
                {
                    currentStepTarget = stepTarget;
                    lastWanderStepTime = now;

                    // Human reaction cadence: 180ms - 360ms with rare glance pause (3% chance)
                    float roll = UnityEngine.Random.value;
                    nextWanderDelay = (roll < 0.03f && !isPreClickWindow)
                        ? UnityEngine.Random.Range(0.45f, 0.65f)
                        : UnityEngine.Random.Range(0.18f, 0.36f);

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
