using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Assets.Scripts.MapEditor;
using UnityEngine;

namespace RebuildBotPlugin
{
    public class WarpConnection
    {
        public string FromMap;
        public Vector2Int FromPos;
        public int Width;
        public int Height;
        public string DestMap;
        public Vector2Int DestPos;

        // Kafra Teleport Properties
        public bool IsKafraTeleport = false;
        public int KafraMenuOption = -1;
        public int ZenyCost = 0;

        public Vector2Int CenterPos => new Vector2Int(FromPos.x + Math.Max(Width / 2, 0), FromPos.y + Math.Max(Height / 2, 0));

        public Vector2Int GetWalkableTriggerTile(Assets.Scripts.MapEditor.RagnarokWalkData walkData, Vector2Int fromPlayerPos)
        {
            int w = Math.Max(Width, 1);
            int h = Math.Max(Height, 1);

            ushort playerZone = MapNavMesh.Instance != null ? MapNavMesh.Instance.GetZoneId(fromPlayerPos) : (ushort)0;

            Vector2Int bestTile = CenterPos;
            float bestDist = float.MaxValue;
            bool foundWalkableInZone = false;
            bool foundWalkableAny = false;

            // 1. Check all cells inside the warp bounding box
            for (int x = FromPos.x; x < FromPos.x + w; x++)
            {
                for (int y = FromPos.y; y < FromPos.y + h; y++)
                {
                    if (x >= 0 && y >= 0 && (walkData == null || (x < walkData.Width && y < walkData.Height && walkData.CellWalkable(x, y))))
                    {
                        Vector2Int tile = new Vector2Int(x, y);
                        ushort tileZone = MapNavMesh.Instance != null ? MapNavMesh.Instance.GetZoneId(tile) : (ushort)0;
                        bool inSameZone = playerZone != 0 && tileZone == playerZone;

                        float d = Vector2.Distance(fromPlayerPos, new Vector2(x, y));

                        if (inSameZone)
                        {
                            if (!foundWalkableInZone || d < bestDist)
                            {
                                bestDist = d;
                                bestTile = tile;
                                foundWalkableInZone = true;
                            }
                        }
                        else if (!foundWalkableInZone && (!foundWalkableAny || d < bestDist))
                        {
                            bestDist = d;
                            bestTile = tile;
                            foundWalkableAny = true;
                        }
                    }
                }
            }

            if (foundWalkableInZone) return bestTile;

            // 2. If no cells inside the bounding box are in player's zone, check 2-tile perimeter around the box
            for (int r = 1; r <= 2; r++)
            {
                for (int x = FromPos.x - r; x <= FromPos.x + w + r - 1; x++)
                {
                    for (int y = FromPos.y - r; y <= FromPos.y + h + r - 1; y++)
                    {
                        if (x >= 0 && y >= 0 && (walkData == null || (x < walkData.Width && y < walkData.Height && walkData.CellWalkable(x, y))))
                        {
                            Vector2Int tile = new Vector2Int(x, y);
                            ushort tileZone = MapNavMesh.Instance != null ? MapNavMesh.Instance.GetZoneId(tile) : (ushort)0;
                            bool inSameZone = playerZone != 0 && tileZone == playerZone;

                            float d = Vector2.Distance(fromPlayerPos, new Vector2(x, y));

                            if (inSameZone)
                            {
                                if (!foundWalkableInZone || d < bestDist)
                                {
                                    bestDist = d;
                                    bestTile = tile;
                                    foundWalkableInZone = true;
                                }
                            }
                            else if (!foundWalkableInZone && (!foundWalkableAny || d < bestDist))
                            {
                                bestDist = d;
                                bestTile = tile;
                                foundWalkableAny = true;
                            }
                        }
                    }
                }
                if (foundWalkableInZone) return bestTile;
            }

            return bestTile;
        }

        public bool IsInsideWarp(Vector2Int pos)
        {
            int w = Math.Max(Width, 1);
            int h = Math.Max(Height, 1);
            return pos.x >= FromPos.x && pos.x < FromPos.x + w &&
                   pos.y >= FromPos.y && pos.y < FromPos.y + h;
        }
    }

    public class WorldGraph
    {
        public static WorldGraph Instance = new WorldGraph();

        public Dictionary<string, List<WarpConnection>> MapNodes = new Dictionary<string, List<WarpConnection>>(StringComparer.OrdinalIgnoreCase);

#pragma warning disable CS0649
        private class EmbeddedWarpEntry
        {
            public string FromMap { get; set; } = string.Empty;
            public int FromX { get; set; }
            public int FromY { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public string DestMap { get; set; } = string.Empty;
            public int DestX { get; set; }
            public int DestY { get; set; }
        }

        public void LoadEmbeddedWarps()
        {
            try
            {
                var assembly = typeof(WorldGraph).Assembly;
                string resourceName = "RebuildBotPlugin.warps.json";
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Debug.LogWarning($"[WorldGraph] Embedded resource '{resourceName}' not found in assembly.");
                        return;
                    }
                    using (var reader = new StreamReader(stream))
                    {
                        string json = reader.ReadToEnd();
                        LoadWarpJson(json);
                    }
                }

                BakeKafraTeleports();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldGraph] Failed to load embedded warps: {ex.Message}");
            }
        }

        public void LoadWarpJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                IncludeFields = true
            };

            var list = System.Text.Json.JsonSerializer.Deserialize<List<EmbeddedWarpEntry>>(json, options);
            if (list == null) return;

            int count = 0;
            foreach (var item in list)
            {
                if (string.IsNullOrEmpty(item.FromMap)) continue;

                var warp = new WarpConnection
                {
                    FromMap = item.FromMap,
                    FromPos = new Vector2Int(item.FromX, item.FromY),
                    Width = item.Width,
                    Height = item.Height,
                    DestMap = item.DestMap,
                    DestPos = new Vector2Int(item.DestX, item.DestY)
                };

                if (!MapNodes.TryGetValue(item.FromMap, out var mapWarps))
                {
                    mapWarps = new List<WarpConnection>();
                    MapNodes[item.FromMap] = mapWarps;
                }
                mapWarps.Add(warp);
                count++;
            }
            Debug.Log($"[WorldGraph] Successfully loaded {count} embedded warp portals across {MapNodes.Count} maps.");
        }

        public void BakeKafraTeleports()
        {
            void AddKafraWarp(string fromMap, Vector2Int npcPos, string destMap, Vector2Int destPos, int menuOption, int cost)
            {
                var warp = new WarpConnection
                {
                    FromMap = fromMap,
                    FromPos = npcPos,
                    Width = 1,
                    Height = 1,
                    DestMap = destMap,
                    DestPos = destPos,
                    IsKafraTeleport = true,
                    KafraMenuOption = menuOption,
                    ZenyCost = cost
                };

                if (!MapNodes.TryGetValue(fromMap, out var mapWarps))
                {
                    mapWarps = new List<WarpConnection>();
                    MapNodes[fromMap] = mapWarps;
                }
                mapWarps.Add(warp);
            }

            void AddKafraHub(string fromMap, Vector2Int[] positions, (string destMap, Vector2Int destPos, int menuOption, int cost)[] destinations)
            {
                foreach (var pos in positions)
                {
                    foreach (var d in destinations)
                    {
                        AddKafraWarp(fromMap, pos, d.destMap, d.destPos, d.menuOption, d.cost);
                    }
                }
            }

            // 1. prt_fild08 (Base Camp - Kafra Staff at 158, 362)
            AddKafraHub("prt_fild08", new[] { new Vector2Int(158, 362) }, new[]
            {
                ("izlude", new Vector2Int(91, 105), 0, 600),
                ("geffen", new Vector2Int(120, 39), 1, 1200),
                ("payon", new Vector2Int(161, 58), 2, 1200),
                ("morocc", new Vector2Int(156, 46), 3, 1200),
                ("gef_fild10", new Vector2Int(52, 326), 4, 1700),
                ("alberta", new Vector2Int(117, 56), 5, 1000)
            });

            // 2. Prontera (All 6 Kafras: SW, SE, South, East, West, North)
            AddKafraHub("prontera", new[]
            {
                new Vector2Int(146, 89),
                new Vector2Int(248, 42),
                new Vector2Int(151, 29),
                new Vector2Int(282, 200),
                new Vector2Int(29, 207),
                new Vector2Int(152, 326)
            }, new[]
            {
                ("izlude", new Vector2Int(91, 105), 0, 600),
                ("geffen", new Vector2Int(120, 39), 1, 1200),
                ("payon", new Vector2Int(161, 58), 2, 1200),
                ("morocc", new Vector2Int(156, 46), 3, 1200),
                ("gef_fild10", new Vector2Int(52, 326), 4, 1700),
                ("alberta", new Vector2Int(117, 56), 5, 1800)
            });

            // 3. Morroc (North Kafra at 160, 258 AND South Kafra at 156, 97)
            AddKafraHub("morocc", new[]
            {
                new Vector2Int(160, 258),
                new Vector2Int(156, 97)
            }, new[]
            {
                ("prontera", new Vector2Int(116, 72), 0, 1200),
                ("payon", new Vector2Int(161, 58), 1, 1200),
                ("alberta", new Vector2Int(117, 56), 2, 1800),
                ("comodo", new Vector2Int(209, 143), 3, 1800),
                ("cmd_fild07", new Vector2Int(127, 134), 4, 1200)
            });

            // 4. Geffen (South Kafra at 120, 62 AND East Kafra at 203, 123)
            AddKafraHub("geffen", new[]
            {
                new Vector2Int(120, 62),
                new Vector2Int(203, 123)
            }, new[]
            {
                ("prontera", new Vector2Int(116, 72), 0, 1200),
                ("aldebaran", new Vector2Int(168, 112), 1, 1200),
                ("gef_fild10", new Vector2Int(52, 326), 2, 1800),
                ("mjolnir_02", new Vector2Int(99, 351), 3, 1800)
            });

            // 5. Payon (South Kafra at 181, 104 AND North Kafra at 175, 226)
            AddKafraHub("payon", new[]
            {
                new Vector2Int(181, 104),
                new Vector2Int(175, 226)
            }, new[]
            {
                ("pay_arche", new Vector2Int(65, 138), 0, 200),
                ("prontera", new Vector2Int(116, 72), 1, 1200),
                ("alberta", new Vector2Int(117, 56), 2, 1200),
                ("morocc", new Vector2Int(156, 46), 3, 1800)
            });

            // 6. Alberta (North Kafra at 28, 229 AND South Kafra at 113, 60)
            AddKafraHub("alberta", new[]
            {
                new Vector2Int(28, 229),
                new Vector2Int(113, 60)
            }, new[]
            {
                ("payon", new Vector2Int(161, 58), 0, 1200),
                ("morocc", new Vector2Int(156, 46), 1, 1800),
                ("prontera", new Vector2Int(116, 72), 2, 1200)
            });

            // 7. Izlude (Kafra at 134, 88)
            AddKafraHub("izlude", new[] { new Vector2Int(134, 88) }, new[]
            {
                ("geffen", new Vector2Int(120, 39), 0, 1200),
                ("payon", new Vector2Int(161, 58), 1, 1200),
                ("morocc", new Vector2Int(156, 46), 2, 1200),
                ("aldebaran", new Vector2Int(168, 112), 3, 1800)
            });

            // 8. Aldebaran (Kafra at 143, 119)
            AddKafraHub("aldebaran", new[] { new Vector2Int(143, 119) }, new[]
            {
                ("geffen", new Vector2Int(120, 39), 0, 1200),
                ("yuno", new Vector2Int(158, 125), 1, 1200),
                ("izlude", new Vector2Int(94, 103), 2, 1800),
                ("mjolnir_02", new Vector2Int(99, 351), 3, 1700)
            });

            // 9. Orc Dungeon (gef_fild10 at 73, 340)
            AddKafraHub("gef_fild10", new[] { new Vector2Int(73, 340) }, new[]
            {
                ("geffen", new Vector2Int(120, 39), 0, 1200),
                ("prontera", new Vector2Int(116, 72), 1, 1200)
            });

            // 10. Archer Village (pay_arche at 55, 123)
            AddKafraHub("pay_arche", new[] { new Vector2Int(55, 123) }, new[]
            {
                ("payon", new Vector2Int(161, 58), 0, 200),
                ("prontera", new Vector2Int(116, 72), 1, 1200),
                ("alberta", new Vector2Int(117, 56), 2, 1200),
                ("morocc", new Vector2Int(156, 46), 3, 1800)
            });

            // 11. Comodo (Kafra at 195, 150)
            AddKafraHub("comodo", new[] { new Vector2Int(195, 150) }, new[]
            {
                ("morocc", new Vector2Int(156, 46), 0, 1200),
                ("alberta", new Vector2Int(117, 56), 1, 1800),
                ("cmd_fild07", new Vector2Int(127, 134), 2, 1200)
            });

            Debug.Log($"[WorldGraph] Baked comprehensive Kafra teleport network into world graph.");
        }

        public List<WarpConnection> FindRoute(string startMap, string targetMap)
        {
            return FindZoneAwareRoute(startMap, Vector2Int.zero, targetMap, null, null);
        }

        public List<WarpConnection> FindZoneAwareRoute(
            string startMap,
            Vector2Int startPos,
            string targetMap,
            Vector2Int? targetPos = null,
            Func<Vector2Int, Vector2Int, bool> isReachableOnStartMap = null,
            WarpConnection preferredStartWarp = null)
        {
            if (string.IsNullOrEmpty(startMap) || string.IsNullOrEmpty(targetMap))
                return null;

            bool isSameMap = string.Equals(startMap, targetMap, StringComparison.OrdinalIgnoreCase);

            // 1. If start and target are on the same map, check if directly reachable locally
            if (isSameMap)
            {
                if (!targetPos.HasValue)
                    return new List<WarpConnection>();

                if (isReachableOnStartMap != null && isReachableOnStartMap(startPos, targetPos.Value))
                    return new List<WarpConnection>();
            }

            var walkProvider = RoWalkDataProvider.Instance;
            var walkData = walkProvider != null ? walkProvider.WalkData : null;

            // 2. Determine initial reachable warps leaving startMap from the current zone
            if (!MapNodes.TryGetValue(startMap, out var startMapWarps) || startMapWarps.Count == 0)
                return null;

            var reachableStartWarps = new List<(WarpConnection warp, Vector2Int triggerTile)>();
            foreach (var w in startMapWarps)
            {
                Vector2Int triggerTile = w.GetWalkableTriggerTile(walkData, startPos);
                if (isReachableOnStartMap == null || isReachableOnStartMap(startPos, triggerTile))
                {
                    reachableStartWarps.Add((w, triggerTile));
                }
            }

            if (reachableStartWarps.Count == 0)
            {
                // No warps reachable in the current disconnected zone!
                return null;
            }

            // 3. Edge-based Dijkstra search over WarpConnection transitions
            var distances = new Dictionary<WarpConnection, float>();
            var parentWarp = new Dictionary<WarpConnection, WarpConnection>();
            var pq = new List<(WarpConnection warp, float dist)>();

            // Seed initial warps leaving startMap from current zone
            foreach (var item in reachableStartWarps)
            {
                var warp = item.warp;
                Vector2Int triggerTile = item.triggerTile;
                float walkFromStart = Vector2.Distance(startPos, triggerTile);

                // For start map warps when choosing among multiple candidates, use true A* path distance if available
                if (reachableStartWarps.Count > 1 && !warp.IsKafraTeleport)
                {
                    var truePath = MapNavMesh.Instance.FindPath(startPos, triggerTile);
                    if (truePath != null && truePath.Count > 0)
                    {
                        walkFromStart = truePath.Count;
                    }
                }

                float edgeCost = (warp.IsKafraTeleport ? 0.2f : 1.0f) + (walkFromStart * 0.002f);

                // Apply hysteresis commitment bonus to currently pursued warp on the start map
                if (preferredStartWarp != null && warp == preferredStartWarp)
                {
                    edgeCost -= 0.30f;
                }

                distances[warp] = edgeCost;
                parentWarp[warp] = null;
                pq.Add((warp, edgeCost));
            }

            WarpConnection bestEndWarp = null;
            float bestGoalCost = float.MaxValue;

            while (pq.Count > 0)
            {
                int bestIdx = 0;
                float bestDist = pq[0].dist;
                for (int i = 1; i < pq.Count; i++)
                {
                    if (pq[i].dist < bestDist)
                    {
                        bestDist = pq[i].dist;
                        bestIdx = i;
                    }
                }

                var current = pq[bestIdx];
                pq.RemoveAt(bestIdx);

                if (current.dist > distances[current.warp])
                    continue;

                // If current exploration is already strictly worse than a complete path found, we can stop!
                if (current.dist >= bestGoalCost)
                {
                    break;
                }

                // Check if this warp lands on targetMap
                if (string.Equals(current.warp.DestMap, targetMap, StringComparison.OrdinalIgnoreCase))
                {
                    if (isSameMap && targetPos.HasValue && isReachableOnStartMap != null)
                    {
                        // If re-entering the original startMap, ensure this warp lands in the zone containing targetPos
                        if (!isReachableOnStartMap(current.warp.DestPos, targetPos.Value))
                        {
                            // Did not land in target zone, continue exploring
                            goto EXPAND;
                        }
                    }

                    float finalWalkDist = targetPos.HasValue ? Vector2.Distance(current.warp.DestPos, targetPos.Value) : 0f;
                    float goalCost = current.dist + (finalWalkDist * 0.002f);

                    if (goalCost < bestGoalCost)
                    {
                        bestGoalCost = goalCost;
                        bestEndWarp = current.warp;
                    }

                    // Do not expand warps leaving targetMap
                    continue;
                }

            EXPAND:
                // Expand warps leaving current.warp.DestMap
                if (MapNodes.TryGetValue(current.warp.DestMap, out var nextWarps))
                {
                    foreach (var nextWarp in nextWarps)
                    {
                        Vector2Int nextTrigger = nextWarp.GetWalkableTriggerTile(walkData, current.warp.DestPos);

                        // If traversing back across startMap, verify startMap zone reachability
                        if (string.Equals(current.warp.DestMap, startMap, StringComparison.OrdinalIgnoreCase) && isReachableOnStartMap != null)
                        {
                            if (!isReachableOnStartMap(current.warp.DestPos, nextTrigger))
                                continue;
                        }

                        float walkBetween = Vector2.Distance(current.warp.DestPos, nextTrigger);
                        float edgeCost = (nextWarp.IsKafraTeleport ? 0.2f : 1.0f) + (walkBetween * 0.002f);
                        float newDist = current.dist + edgeCost;

                        if (newDist < bestGoalCost && (!distances.TryGetValue(nextWarp, out float oldDist) || newDist < oldDist))
                        {
                            distances[nextWarp] = newDist;
                            parentWarp[nextWarp] = current.warp;
                            pq.Add((nextWarp, newDist));
                        }
                    }
                }
            }

            if (bestEndWarp == null) return null;

            // Reconstruct path
            var route = new List<WarpConnection>();
            var curr = bestEndWarp;
            while (curr != null)
            {
                route.Insert(0, curr);
                curr = parentWarp.TryGetValue(curr, out var p) ? p : null;
            }
            return route;
        }

        public bool IsNearPortal(string map, Vector2Int cellPos, float minDistance = 5.0f)
        {
            if (!MapNodes.TryGetValue(map, out var warps)) return false;
            foreach (var warp in warps)
            {
                int minX = warp.FromPos.x;
                int maxX = warp.FromPos.x + Math.Max(warp.Width, 1);
                int minY = warp.FromPos.y;
                int maxY = warp.FromPos.y + Math.Max(warp.Height, 1);

                float clampX = Mathf.Clamp(cellPos.x, minX, maxX);
                float clampY = Mathf.Clamp(cellPos.y, minY, maxY);

                float dist = Vector2.Distance(cellPos, new Vector2(clampX, clampY));
                if (dist <= minDistance)
                    return true;
            }
            return false;
        }

        public List<WarpConnection> GetWarpsConnecting(string fromMap, string destMap)
        {
            var result = new List<WarpConnection>();
            if (MapNodes.TryGetValue(fromMap, out var warps))
            {
                foreach (var w in warps)
                {
                    if (string.Equals(w.DestMap, destMap, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(w);
                    }
                }
            }
            return result;
        }
    }
}
