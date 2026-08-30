using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.MapEditor;
using Assets.Scripts.Network;
using RebuildBotPlugin.Services;
using RebuildSharedData.Enum;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public class TargetingController
    {
        private readonly Dictionary<int, float> activeAttackers = new Dictionary<int, float>();
        private readonly Dictionary<int, float> unreachableMonsters = new Dictionary<int, float>();
        private readonly Vector2Int[] tempPath = new Vector2Int[32];

        public bool HasActiveAttackers => activeAttackers.Count > 0;
        public bool HasCurrentTarget => BotEngine.Instance != null && BotEngine.Instance.Combat != null && BotEngine.Instance.Combat.CurrentLockedTargetId != -1;

        public void RegisterAttacker(int monsterId)
        {
            activeAttackers[monsterId] = Time.time;
        }

        public void MarkUnreachable(int monsterId, float duration)
        {
            unreachableMonsters[monsterId] = Time.time + duration;
        }

        public void Clear()
        {
            activeAttackers.Clear();
        }

        public bool IsAttackingPlayer(int monsterId)
        {
            return activeAttackers.ContainsKey(monsterId);
        }

        public bool IsMonsterAggressive(string monsterName)
        {
            return MonsterDatabase.Instance.IsAggressive(monsterName);
        }

        public ServerControllable GetAttackingMonster(Vector2Int playerPos)
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null || netManager.EntityList == null) return null;

            float now = Time.time;
            List<int> staleAttackers = null;
            List<(ServerControllable entity, float dist, int hp)> validAttackers = null;

            foreach (var kvp in activeAttackers)
            {
                int attackerId = kvp.Key;
                float lastSeen = kvp.Value;

                if (now - lastSeen > 6.0f)
                {
                    staleAttackers ??= new List<int>();
                    staleAttackers.Add(attackerId);
                    continue;
                }

                if (netManager.EntityList.TryGetValue(attackerId, out var entity) &&
                    entity != null && entity.IsCharacterAlive && entity.Hp > 0 && !entity.IsAlly)
                {
                    // Do NOT attack monsters in self-defense that are on the avoidance list!
                    if (BotConfigManager.Current.AutoAvoidMonsters &&
                        BotConfigManager.Current.MonsterAvoidanceList != null &&
                        BotConfigManager.Current.MonsterAvoidanceList.Contains(entity.Name))
                    {
                        continue;
                    }

                    float dist = Vector2.Distance(playerPos, entity.CellPosition);
                    validAttackers ??= new List<(ServerControllable, float, int)>();
                    validAttackers.Add((entity, dist, entity.Hp));
                }
                else
                {
                    staleAttackers ??= new List<int>();
                    staleAttackers.Add(attackerId);
                }
            }

            if (staleAttackers != null)
            {
                foreach (var id in staleAttackers)
                    activeAttackers.Remove(id);
            }

            if (validAttackers == null || validAttackers.Count == 0) return null;

            // Sort attackers:
            // 1. Group by proximity (in melee range <= AttackRange + 1.5f vs farther away)
            // 2. Lowest HP first (kill easiest / lowest HP attackers first!)
            // 3. Closest distance
            float meleeZone = BotConfigManager.Current.AttackRange + 1.5f;
            validAttackers.Sort((a, b) =>
            {
                bool aInMelee = a.dist <= meleeZone;
                bool bInMelee = b.dist <= meleeZone;
                if (aInMelee != bInMelee)
                    return aInMelee ? -1 : 1;

                int hpComp = a.hp.CompareTo(b.hp);
                if (hpComp != 0) return hpComp;

                return a.dist.CompareTo(b.dist);
            });

            // Stick to current target if it is one of the melee attackers and has comparable low HP
            int currentLockedTargetId = BotEngine.Instance != null && BotEngine.Instance.Combat != null
                ? BotEngine.Instance.Combat.CurrentLockedTargetId
                : -1;

            ServerControllable bestAttacker = validAttackers[0].entity;
            if (currentLockedTargetId != -1 && currentLockedTargetId != bestAttacker.Id)
            {
                var currentAttackerInfo = validAttackers.Find(a => a.entity.Id == currentLockedTargetId);
                if (currentAttackerInfo.entity != null && currentAttackerInfo.dist <= meleeZone)
                {
                    if (currentAttackerInfo.hp <= bestAttacker.Hp + 50 || currentAttackerInfo.hp <= bestAttacker.Hp * 1.25f)
                    {
                        bestAttacker = currentAttackerInfo.entity;
                    }
                }
            }

            if (currentLockedTargetId != bestAttacker.Id)
            {
                BotEngine.Instance?.LogEvent($"Self-Defense: Engaging hostile monster {bestAttacker.Name} (HP: {bestAttacker.Hp}/{bestAttacker.MaxHp})!");
            }
            return bestAttacker;
        }

        public ServerControllable FindBestTargetMonster(Vector2Int playerPos)
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null || netManager.EntityList == null) return null;

            float now = Time.time;
            List<(ServerControllable monster, float dist, int priority)> candidates = null;

            foreach (var kvp in netManager.EntityList)
            {
                var entity = kvp.Value;
                if (entity == null || entity.Id == netManager.PlayerId)
                    continue;

                // Check unreachable blacklist
                if (unreachableMonsters.TryGetValue(entity.Id, out var expire) && expire > now)
                    continue;

                if (entity.CharacterType == CharacterType.Monster && !entity.IsAlly && entity.IsCharacterAlive && entity.Hp > 0)
                {
                    // Instant topological reachability pre-check (must be on the same connected landmass)
                    if (!MapNavMesh.Instance.IsReachable(playerPos, entity.CellPosition))
                        continue;

                    // Whitelist check
                    if (BotConfigManager.Current.TargetMonsterWhitelist.Count > 0 &&
                        !BotConfigManager.Current.TargetMonsterWhitelist.Contains(entity.Name))
                        continue;

                    // Blacklist check
                    if (BotConfigManager.Current.TargetMonsterBlacklist.Contains(entity.Name))
                        continue;

                    // Avoidance check (do not target monsters we are fleeing from)
                    if (BotConfigManager.Current.AutoAvoidMonsters &&
                        BotConfigManager.Current.MonsterAvoidanceList.Contains(entity.Name))
                        continue;

                    float dist = Vector2.Distance(playerPos, entity.CellPosition);
                    if (dist <= BotConfigManager.Current.SearchRadius)
                    {
                        // Priority tiers:
                        // 0 = Actively attacking player (Imminent self-defense)
                        // 1 = Aggressive monster within threat range (<= 8.5 tiles)
                        // 2 = Standard targets (nearby passive monsters or distant un-aggroed monsters)
                        int priority = 2;
                        if (activeAttackers.ContainsKey(entity.Id))
                        {
                            priority = 0;
                        }
                        else if (BotConfigManager.Current.PrioritizeAggressiveMonsters && IsMonsterAggressive(entity.Name) && dist <= 8.5f)
                        {
                            priority = 1;
                        }

                        candidates ??= new List<(ServerControllable, float, int)>();
                        candidates.Add((entity, dist, priority));
                    }
                }
            }

            if (candidates == null || candidates.Count == 0) return null;

            // Sort:
            // 1. Highest priority tier first (Tier 0 < Tier 1 < Tier 2)
            // 2. Within Tier 0 (Active attackers hitting player): lowest HP first (kill fastest), then closest
            // 3. Within Tier 1 (Aggressive threats <= 8.5 tiles) and Tier 2 (General targets):
            //    CLOSEST DISTANCE FIRST! Never run across the screen leaving nearby targets behind!
            //    Only if distance difference is negligible (<= 1.5 tiles), break tie with lowest HP
            candidates.Sort((a, b) =>
            {
                int pComp = a.Item3.CompareTo(b.Item3);
                if (pComp != 0) return pComp;

                if (a.Item3 == 0)
                {
                    int hpComp = a.Item1.Hp.CompareTo(b.Item1.Hp);
                    if (hpComp != 0) return hpComp;
                    return a.Item2.CompareTo(b.Item2);
                }

                // Tier 1 & 2: Distance is primary
                float distDiff = a.Item2 - b.Item2;
                if (Mathf.Abs(distDiff) > 1.5f)
                {
                    return distDiff < 0 ? -1 : 1;
                }

                int hpTieBreaker = a.Item1.Hp.CompareTo(b.Item1.Hp);
                if (hpTieBreaker != 0) return hpTieBreaker;

                return a.Item2.CompareTo(b.Item2);
            });

            // Select first reachable candidate
            foreach (var item in candidates)
            {
                if (IsMonsterPathable(playerPos, item.Item1.CellPosition))
                {
                    return item.Item1;
                }
                else
                {
                    MarkUnreachable(item.Item1.Id, 6.0f);
                    BotEngine.Instance?.LogDebug($"[Targeting] Skipping candidate {item.Item1.Name} (ID: {item.Item1.Id}) at ({item.Item1.CellPosition.x}, {item.Item1.CellPosition.y}) - path blocked by terrain/cliff. Ignoring for 6s.");
                }
            }

            return null;
        }

        public ServerControllable FindNearbyAggressiveThreat(Vector2Int playerPos, float radius)
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null || netManager.EntityList == null) return null;

            float now = Time.time;
            List<(ServerControllable entity, float dist, int hp)> threats = null;

            foreach (var kvp in netManager.EntityList)
            {
                var entity = kvp.Value;
                if (entity == null || entity.Id == netManager.PlayerId) continue;
                if (entity.CharacterType != CharacterType.Monster || entity.IsAlly || !entity.IsCharacterAlive || entity.Hp <= 0) continue;

                if (unreachableMonsters.TryGetValue(entity.Id, out var expire) && expire > now) continue;
                if (BotConfigManager.Current.TargetMonsterBlacklist.Contains(entity.Name)) continue;
                if (BotConfigManager.Current.AutoAvoidMonsters && BotConfigManager.Current.MonsterAvoidanceList.Contains(entity.Name)) continue;

                if (IsMonsterAggressive(entity.Name) || activeAttackers.ContainsKey(entity.Id))
                {
                    float dist = Vector2.Distance(playerPos, entity.CellPosition);
                    if (dist <= radius)
                    {
                        if (MapNavMesh.Instance.IsReachable(playerPos, entity.CellPosition))
                        {
                            threats ??= new List<(ServerControllable, float, int)>();
                            threats.Add((entity, dist, entity.Hp));
                        }
                    }
                }
            }

            if (threats == null || threats.Count == 0) return null;

            // Sort threats: closest distance first! (deal with the most immediate threat)
            // If distance is within 1.5 tiles, break tie with lowest HP (focus fire)
            threats.Sort((a, b) =>
            {
                float distDiff = a.dist - b.dist;
                if (Mathf.Abs(distDiff) > 1.5f)
                    return distDiff < 0 ? -1 : 1;

                int hpComp = a.hp.CompareTo(b.hp);
                if (hpComp != 0) return hpComp;
                return a.dist.CompareTo(b.dist);
            });

            return threats[0].entity;
        }

        public ServerControllable FindAvoidanceMonster()
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null || netManager.EntityList == null) return null;

            var avoidList = BotConfigManager.Current.MonsterAvoidanceList;
            if (avoidList == null || avoidList.Count == 0) return null;

            foreach (var kvp in netManager.EntityList)
            {
                var entity = kvp.Value;
                if (entity == null || entity.Id == netManager.PlayerId) continue;

                if (entity.CharacterType == CharacterType.Monster && entity.IsCharacterAlive && entity.Hp > 0 && !entity.IsAlly)
                {
                    if (avoidList.Contains(entity.Name))
                    {
                        return entity;
                    }
                }
            }
            return null;
        }

        public bool IsMonsterPathable(Vector2Int currentPos, Vector2Int monsterPos)
        {
            if (!MapNavMesh.Instance.IsReachable(currentPos, monsterPos))
                return false;

            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider == null || walkProvider.WalkData == null) return true;

            var walkData = walkProvider.WalkData;

            // Find best walkable attack tile 1-2 tiles from monster
            Vector2Int attackPos = BotEngine.Instance != null && BotEngine.Instance.Navigation != null
                ? BotEngine.Instance.Navigation.GetAttackPosition(currentPos, monsterPos, BotConfigManager.Current.AttackRange)
                : monsterPos;

            // If attackPos is not walkable, probe 8-neighbors of monsterPos for any walkable tile in player's zone
            if (!walkData.CellWalkable(attackPos.x, attackPos.y))
            {
                ushort playerZone = MapNavMesh.Instance.GetZoneId(currentPos);
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = monsterPos.x + dx;
                        int ny = monsterPos.y + dy;
                        if (nx >= 0 && ny >= 0 && nx < walkData.Width && ny < walkData.Height &&
                            walkData.CellWalkable(nx, ny) &&
                            (playerZone == 0 || MapNavMesh.Instance.GetZoneId(nx, ny) == playerZone))
                        {
                            attackPos = new Vector2Int(nx, ny);
                            break;
                        }
                    }
                }
            }

            // 1. Direct path check if within direct reach
            int chebyshevDist = Math.Max(Math.Abs(attackPos.x - currentPos.x), Math.Abs(attackPos.y - currentPos.y));
            if (chebyshevDist <= 12 && walkData.CellWalkable(attackPos.x, attackPos.y))
            {
                int directSteps = Pathfinder.GetPath(walkData, currentPos, attackPos, tempPath);
                if (directSteps > 0) return true;
            }

            // 2. Query MapNavMesh for the actual A* path to the attack tile
            if (walkData.CellWalkable(attackPos.x, attackPos.y))
            {
                var path = MapNavMesh.Instance.FindPath(currentPos, attackPos);
                if (path != null && path.Count > 0)
                {
                    float maxPursuitSteps = BotConfigManager.Current.SearchRadius * 2.0f;
                    return path.Count <= maxPursuitSteps;
                }
            }

            // 3. Proximity fallback: if monster is close (<= 5 tiles) on the same zone, allow engagement
            if (chebyshevDist <= 5) return true;

            return false;
        }

        public List<string> GetActiveMonstersOnMap()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var netManager = NetworkManager.Instance;
            if (netManager != null && netManager.EntityList != null)
            {
                foreach (var kvp in netManager.EntityList)
                {
                    var entity = kvp.Value;
                    if (entity != null && entity.CharacterType == CharacterType.Monster && !string.IsNullOrWhiteSpace(entity.Name))
                    {
                        result.Add(entity.Name);
                    }
                }
            }
            return new List<string>(result);
        }
    }
}
