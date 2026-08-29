using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
    }

    public class WorldGraph
    {
        public static WorldGraph Instance = new WorldGraph();

        public Dictionary<string, List<WarpConnection>> MapNodes = new Dictionary<string, List<WarpConnection>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex WarpRegex = new Regex(
            @"Warp\s*\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*(?:""[^""]*""\s*,\s*)?(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*""([^""]+)""\s*,\s*(\d+)\s*,\s*(\d+)\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

#pragma warning disable CS0649
        [Serializable]
        private class EmbeddedWarpEntry
        {
            public string FromMap;
            public int FromX;
            public int FromY;
            public int Width;
            public int Height;
            public string DestMap;
            public int DestX;
            public int DestY;
        }

        [Serializable]
        private class EmbeddedWarpList
        {
            public List<EmbeddedWarpEntry> Items;
        }
#pragma warning restore CS0649

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
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldGraph] Failed to load embedded warps: {ex.Message}");
            }
        }

        public void LoadWarpJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;

            // Wrap array into object for JsonUtility
            string wrappedJson = $"{{\"Items\":{json}}}";
            var list = JsonUtility.FromJson<EmbeddedWarpList>(wrappedJson);
            if (list == null || list.Items == null) return;

            int count = 0;
            foreach (var item in list.Items)
            {
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

        public void LoadWarpDirectory(string dirPath)
        {
            if (!Directory.Exists(dirPath))
            {
                Debug.LogWarning($"[WorldGraph] Directory not found: {dirPath}");
                return;
            }

            var files = Directory.GetFiles(dirPath, "*.txt", SearchOption.AllDirectories);
            int count = 0;
            foreach (var file in files)
            {
                string text = File.ReadAllText(file);
                count += ParseWarpText(text);
            }
            Debug.Log($"[WorldGraph] Loaded {count} warp portal connections across {MapNodes.Count} maps.");
        }

        public int ParseWarpText(string text)
        {
            var matches = WarpRegex.Matches(text);
            int added = 0;
            foreach (Match match in matches)
            {
                try
                {
                    string fromMap = match.Groups[1].Value;
                    int fromX = int.Parse(match.Groups[3].Value);
                    int fromY = int.Parse(match.Groups[4].Value);
                    int width = int.Parse(match.Groups[5].Value);
                    int height = int.Parse(match.Groups[6].Value);
                    string destMap = match.Groups[7].Value;
                    int destX = int.Parse(match.Groups[8].Value);
                    int destY = int.Parse(match.Groups[9].Value);

                    var warp = new WarpConnection
                    {
                        FromMap = fromMap,
                        FromPos = new Vector2Int(fromX, fromY),
                        Width = width,
                        Height = height,
                        DestMap = destMap,
                        DestPos = new Vector2Int(destX, destY)
                    };

                    if (!MapNodes.TryGetValue(fromMap, out var list))
                    {
                        list = new List<WarpConnection>();
                        MapNodes[fromMap] = list;
                    }
                    list.Add(warp);
                    added++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WorldGraph] Failed to parse warp line '{match.Value}': {ex.Message}");
                }
            }
            return added;
        }

        public List<WarpConnection> FindRoute(string startMap, string targetMap)
        {
            if (string.Equals(startMap, targetMap, StringComparison.OrdinalIgnoreCase))
                return new List<WarpConnection>();

            var queue = new Queue<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parentWarp = new Dictionary<string, WarpConnection>(StringComparer.OrdinalIgnoreCase);

            queue.Enqueue(startMap);
            visited.Add(startMap);

            bool found = false;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (string.Equals(current, targetMap, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    break;
                }

                if (MapNodes.TryGetValue(current, out var warps))
                {
                    foreach (var warp in warps)
                    {
                        if (!visited.Contains(warp.DestMap))
                        {
                            visited.Add(warp.DestMap);
                            parentWarp[warp.DestMap] = warp;
                            queue.Enqueue(warp.DestMap);
                        }
                    }
                }
            }

            if (!found) return null;

            // Reconstruct path
            var route = new List<WarpConnection>();
            string curr = targetMap;
            while (parentWarp.TryGetValue(curr, out var warp))
            {
                route.Insert(0, warp);
                curr = warp.FromMap;
            }
            return route;
        }

        public bool IsNearPortal(string map, Vector2Int cellPos, float minDistance = 5.0f)
        {
            if (!MapNodes.TryGetValue(map, out var warps)) return false;
            foreach (var warp in warps)
            {
                if (Vector2Int.Distance(cellPos, warp.FromPos) <= minDistance)
                    return true;
            }
            return false;
        }
    }
}
