using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RebuildBotPlugin.Tools
{
    /// <summary>
    /// Development/build-time utility for parsing server warp definition script (.txt) files
    /// and generating warp connection lists or exporting warps.json.
    /// Not called during normal bot gameplay runtime (embedded warps.json is used instead).
    /// </summary>
    public static class WarpParserUtility
    {
        private static readonly Regex WarpRegex = new Regex(
            @"Warp\s*\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*(?:""[^""]*""\s*,\s*)?(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*""([^""]+)""\s*,\s*(\d+)\s*,\s*(\d+)\s*\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Reads all .txt warp scripts from a directory and populates the target map warp dictionary.
        /// </summary>
        public static int LoadWarpDirectory(string dirPath, Dictionary<string, List<WarpConnection>> mapNodes)
        {
            if (!Directory.Exists(dirPath))
            {
                Services.BotLog.Warn($"[WarpParserUtility] Directory not found: {dirPath}");
                return 0;
            }

            var files = Directory.GetFiles(dirPath, "*.txt", SearchOption.AllDirectories);
            int count = 0;
            foreach (var file in files)
            {
                string text = File.ReadAllText(file);
                count += ParseWarpText(text, mapNodes);
            }
            Services.BotLog.Info($"[WarpParserUtility] Loaded {count} warp portal connections across {mapNodes.Count} maps.");
            return count;
        }

        /// <summary>
        /// Parses raw warp text containing Warp("map", ...) definitions.
        /// </summary>
        public static int ParseWarpText(string text, Dictionary<string, List<WarpConnection>> mapNodes)
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

                    if (!mapNodes.TryGetValue(fromMap, out var list))
                    {
                        list = new List<WarpConnection>();
                        mapNodes[fromMap] = list;
                    }
                    list.Add(warp);
                    added++;
                }
                catch (Exception ex)
                {
                    Services.BotLog.Warn($"[WarpParserUtility] Failed to parse warp line '{match.Value}': {ex.Message}");
                }
            }
            return added;
        }
    }
}
