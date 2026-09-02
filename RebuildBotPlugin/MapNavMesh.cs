using System;
using System.Collections.Generic;
using System.Diagnostics;
using Assets.Scripts.MapEditor;
using RebuildSharedData.Enum;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RebuildBotPlugin
{
    public class FastMinHeap
    {
        private struct HeapNode
        {
            public int Index;
            public int Cost;
        }

        private readonly HeapNode[] nodes;
        public int Count { get; private set; }

        public FastMinHeap(int capacity)
        {
            nodes = new HeapNode[capacity + 1];
            Count = 0;
        }

        public void Clear() => Count = 0;

        public void Push(int index, int cost)
        {
            Count++;
            int i = Count;
            while (i > 1)
            {
                int parent = i / 2;
                if (nodes[parent].Cost <= cost) break;
                nodes[i] = nodes[parent];
                i = parent;
            }
            nodes[i] = new HeapNode { Index = index, Cost = cost };
        }

        public int Pop()
        {
            int result = nodes[1].Index;
            var last = nodes[Count--];
            if (Count == 0) return result;

            int i = 1;
            while (i * 2 <= Count)
            {
                int child = i * 2;
                if (child + 1 <= Count && nodes[child + 1].Cost < nodes[child].Cost)
                    child++;
                if (last.Cost <= nodes[child].Cost) break;
                nodes[i] = nodes[child];
                i = child;
            }
            nodes[i] = last;
            return result;
        }
    }

    public class MapNavMesh
    {
        public static MapNavMesh Instance { get; } = new MapNavMesh();

        private string currentMap;
        private int width;
        private int height;
        private ushort[] zoneMap;
        private int totalZones;
        private readonly Dictionary<ushort, int> zoneCellCounts = new Dictionary<ushort, int>();

        // Flat-array A* pathfinding caches (zero per-search heap allocations)
        private int[] gScores;
        private int[] parentNodes;
        private int[] closedMarks;
        private int currentSearchSession;
        private FastMinHeap openHeap;

        public string CurrentMap => currentMap;
        public int Width => width;
        public int Height => height;
        public int TotalZones => totalZones;

        public void Clear()
        {
            currentMap = null;
            width = 0;
            height = 0;
            zoneMap = null;
            totalZones = 0;
            zoneCellCounts.Clear();
        }

        public void AnalyzeMap(string mapName, RagnarokWalkData walkData)
        {
            if (walkData == null || string.IsNullOrEmpty(mapName)) return;
            if (string.Equals(currentMap, mapName, StringComparison.OrdinalIgnoreCase) && zoneMap != null)
                return;

            Stopwatch sw = Stopwatch.StartNew();

            width = walkData.Width;
            height = walkData.Height;
            int totalCells = width * height;

            zoneMap = new ushort[totalCells];
            zoneCellCounts.Clear();

            // Prepare A* arrays
            if (gScores == null || gScores.Length < totalCells)
            {
                gScores = new int[totalCells];
                parentNodes = new int[totalCells];
                closedMarks = new int[totalCells];
                openHeap = new FastMinHeap(totalCells);
            }

            // Flood-fill connected components
            Queue<int> queue = new Queue<int>(2048);
            ushort nextZoneId = 1;

            int[] dx = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int[] dy = { 1, -1, 0, 0, 1, 1, -1, -1 };

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = x + y * width;
                    if (!walkData.CellWalkable(x, y) || zoneMap[idx] != 0)
                        continue;

                    ushort zoneId = nextZoneId++;
                    int cellCount = 0;

                    zoneMap[idx] = zoneId;
                    queue.Enqueue(idx);

                    while (queue.Count > 0)
                    {
                        int curr = queue.Dequeue();
                        cellCount++;

                        int cx = curr % width;
                        int cy = curr / width;

                        for (int dir = 0; dir < 8; dir++)
                        {
                            int nx = cx + dx[dir];
                            int ny = cy + dy[dir];

                            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                                continue;

                            int nidx = nx + ny * width;
                            if (zoneMap[nidx] != 0 || !walkData.CellWalkable(nx, ny))
                                continue;

                            // Prevent corner cutting through non-walkable diagonals (only block if BOTH orthogonal cells are walls)
                            if (dir >= 4)
                            {
                                if (!walkData.CellWalkable(cx, ny) && !walkData.CellWalkable(nx, cy))
                                    continue;
                            }

                            zoneMap[nidx] = zoneId;
                            queue.Enqueue(nidx);
                        }
                    }

                    zoneCellCounts[zoneId] = cellCount;
                }
            }

            totalZones = nextZoneId - 1;
            currentMap = mapName;
            sw.Stop();

            Services.BotLog.Info($"[MapNavMesh] Analyzed map '{mapName}' ({width}x{height}) in {sw.ElapsedMilliseconds}ms: Found {totalZones} connected walkable zones.");
        }

        public ushort GetZoneId(int x, int y)
        {
            if (zoneMap == null || x < 0 || y < 0 || x >= width || y >= height)
                return 0;

            ushort z = zoneMap[x + y * width];
            if (z != 0) return z;

            // If tile itself is unmeshed (e.g. portal landing tile or decorative border), snap to nearest neighbor zone
            for (int r = 1; r <= 3; r++)
            {
                for (int ox = -r; ox <= r; ox++)
                {
                    for (int oy = -r; oy <= r; oy++)
                    {
                        if (Math.Abs(ox) != r && Math.Abs(oy) != r) continue;
                        int nx = x + ox;
                        int ny = y + oy;
                        if (nx >= 0 && ny >= 0 && nx < width && ny < height)
                        {
                            ushort nz = zoneMap[nx + ny * width];
                            if (nz != 0) return nz;
                        }
                    }
                }
            }
            return 0;
        }

        public ushort GetZoneId(Vector2Int pos) => GetZoneId(pos.x, pos.y);

        public bool IsReachable(Vector2Int start, Vector2Int target)
        {
            ushort za = GetZoneId(start.x, start.y);
            ushort zb = GetZoneId(target.x, target.y);
            if (za == 0 || zb == 0) return false;
            return za == zb;
        }

        public int GetZoneCellCount(Vector2Int pos)
        {
            ushort z = GetZoneId(pos);
            return zoneCellCounts.TryGetValue(z, out int c) ? c : 0;
        }

        /// <summary>
        /// Fast unconstrained global A* on the current map's walk mesh.
        /// Returns the full list of path steps from start to target.
        /// </summary>
        public List<Vector2Int> FindPath(Vector2Int start, Vector2Int target)
        {
            ushort startZone = GetZoneId(start.x, start.y);
            ushort targetZone = GetZoneId(target.x, target.y);

            if (startZone == 0 || targetZone == 0 || startZone != targetZone)
                return null;

            Vector2Int actualStart = start;
            if (zoneMap[start.x + start.y * width] != startZone)
            {
                float bestDist = float.MaxValue;
                for (int r = 1; r <= 3; r++)
                {
                    for (int ox = -r; ox <= r; ox++)
                    {
                        for (int oy = -r; oy <= r; oy++)
                        {
                            int nx = start.x + ox;
                            int ny = start.y + oy;
                            if (nx >= 0 && ny >= 0 && nx < width && ny < height && zoneMap[nx + ny * width] == startZone)
                            {
                                float d = (ox * ox) + (oy * oy);
                                if (d < bestDist)
                                {
                                    bestDist = d;
                                    actualStart = new Vector2Int(nx, ny);
                                }
                            }
                        }
                    }
                    if (bestDist < float.MaxValue) break;
                }
            }

            Vector2Int actualTarget = target;
            if (zoneMap[target.x + target.y * width] != startZone)
            {
                float bestDist = float.MaxValue;
                for (int r = 1; r <= 3; r++)
                {
                    for (int ox = -r; ox <= r; ox++)
                    {
                        for (int oy = -r; oy <= r; oy++)
                        {
                            int nx = target.x + ox;
                            int ny = target.y + oy;
                            if (nx >= 0 && ny >= 0 && nx < width && ny < height && zoneMap[nx + ny * width] == startZone)
                            {
                                float d = (ox * ox) + (oy * oy);
                                if (d < bestDist)
                                {
                                    bestDist = d;
                                    actualTarget = new Vector2Int(nx, ny);
                                }
                            }
                        }
                    }
                    if (bestDist < float.MaxValue) break;
                }
            }

            if (actualStart == actualTarget) return new List<Vector2Int> { actualStart };

            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider == null || walkProvider.WalkData == null) return null;
            var walkData = walkProvider.WalkData;

            currentSearchSession++;
            int session = currentSearchSession;

            openHeap.Clear();

            int startIndex = actualStart.x + actualStart.y * width;
            int targetIndex = actualTarget.x + actualTarget.y * width;

            gScores[startIndex] = 0;
            parentNodes[startIndex] = -1;
            closedMarks[startIndex] = session;

            openHeap.Push(startIndex, Heuristic(actualStart, actualTarget));

            int[] dx = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int[] dy = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] cost = { 10, 10, 10, 10, 14, 14, 14, 14 };

            bool found = false;
            int maxIterations = Math.Max(width * height, 100000);
            int iterations = 0;

            while (openHeap.Count > 0 && iterations++ < maxIterations)
            {
                int currIdx = openHeap.Pop();
                if (currIdx == targetIndex)
                {
                    found = true;
                    break;
                }

                int cx = currIdx % width;
                int cy = currIdx / width;
                int currentG = gScores[currIdx];

                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + dx[i];
                    int ny = cy + dy[i];

                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        continue;

                    int nidx = nx + ny * width;

                    // Must be in the exact same zone
                    if (zoneMap[nidx] != zoneMap[startIndex])
                        continue;

                    // Prevent diagonal cutting (only block if BOTH orthogonal cells are walls)
                    if (i >= 4)
                    {
                        if (!walkData.CellWalkable(cx, ny) && !walkData.CellWalkable(nx, cy))
                            continue;
                    }

                    int tentativeG = currentG + cost[i];

                    if (closedMarks[nidx] != session || tentativeG < gScores[nidx])
                    {
                        closedMarks[nidx] = session;
                        gScores[nidx] = tentativeG;
                        parentNodes[nidx] = currIdx;

                        int h = Heuristic(new Vector2Int(nx, ny), actualTarget);
                        openHeap.Push(nidx, tentativeG + h);
                    }
                }
            }

            if (!found) return null;

            // Reconstruct path
            List<Vector2Int> path = new List<Vector2Int>();
            int trace = targetIndex;
            while (trace != -1)
            {
                path.Add(new Vector2Int(trace % width, trace / width));
                trace = parentNodes[trace];
            }

            path.Reverse();
            return path;
        }

        private static int Heuristic(Vector2Int a, Vector2Int b)
        {
            int dx = Math.Abs(a.x - b.x);
            int dy = Math.Abs(a.y - b.y);
            int baseH = (Math.Max(dx, dy) * 10) + (Math.Min(dx, dy) * 4);
            return baseH + (baseH >> 10);
        }

        /// <summary>
        /// Computes a list of intermediate macro waypoints (spaced by hopDistance)
        /// that guide the character along the unconstrained path around map obstacles.
        /// </summary>
        public List<Vector2Int> FindRouteWaypoints(Vector2Int start, Vector2Int target, int hopDistance = 11)
        {
            var fullPath = FindPath(start, target);
            if (fullPath == null || fullPath.Count <= 1)
                return null;

            List<Vector2Int> waypoints = new List<Vector2Int>();

            for (int i = hopDistance; i < fullPath.Count; i += hopDistance)
            {
                waypoints.Add(fullPath[i]);
            }

            // Always add the final destination
            if (waypoints.Count == 0 || waypoints[waypoints.Count - 1] != target)
            {
                waypoints.Add(target);
            }

            return waypoints;
        }
    }
}
