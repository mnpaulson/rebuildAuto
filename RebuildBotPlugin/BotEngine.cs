using System;
using System.Collections.Generic;
using Assets.Scripts.Network;
using RebuildSharedData.Enum;
using UnityEngine;

namespace RebuildBotPlugin
{
    public enum BotState
    {
        Disabled,
        Idle,
        PlayerDead,
        SearchingTarget,
        ApproachingTarget,
        AttackingTarget,
        LootingItem,
        Wandering,
        UsingPotion,
        TravelingToTargetMap
    }

    public class BotEngine : MonoBehaviour
    {
        public static BotEngine Instance;

        public BotState CurrentState = BotState.Disabled;
        public string CurrentTargetName = "None";
        public int CurrentTargetHp = 0;
        public int CurrentTargetMaxHp = 0;
        public int KillCount = 0;
        public int LootCount = 0;

        private float lastAttackTime;
        private float lastLootTime;
        private float lastWanderTime;
        private float lastPotionTime;
        private float lastTravelTime;

        private int lastTargetId = -1;
        private int lastTargetHp = -1;

        private List<string> actionLog = new List<string>();
        private const int MaxLogEntries = 10;

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            LogEvent("Bot engine initialized.");
        }

        public List<string> GetLogEntries() => actionLog;

        public void LogEvent(string msg)
        {
            string entry = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            actionLog.Add(entry);
            if (actionLog.Count > MaxLogEntries)
                actionLog.RemoveAt(0);
            Debug.Log($"[RebuildBotPlugin] {entry}");
        }

        private void Update()
        {
            if (!BotConfigManager.Current.Enabled)
            {
                CurrentState = BotState.Disabled;
                return;
            }

            var netManager = NetworkManager.Instance;
            if (netManager == null)
            {
                CurrentState = BotState.Idle;
                return;
            }

            if (!netManager.EntityList.TryGetValue(netManager.PlayerId, out var player) || player == null)
            {
                CurrentState = BotState.Idle;
                return;
            }

            if (player.Hp <= 0 || player.CharacterState == CharacterState.Dead)
            {
                CurrentState = BotState.PlayerDead;
                return;
            }

            // Update player's sector visit timestamp in heatmap
            MapHeatmap.Instance.UpdatePlayerPosition(netManager.CurrentMap, player.CellPosition);

            float now = Time.time;

            // 1. Auto-Potion Check
            if (BotConfigManager.Current.AutoPotion && now - lastPotionTime > 0.5f)
            {
                if (player.MaxHp > 0 && ((float)player.Hp / player.MaxHp * 100.0f) < BotConfigManager.Current.HpPotionPercent)
                {
                    netManager.SendUseItem(BotConfigManager.Current.HpPotionItemId);
                    lastPotionTime = now;
                    LogEvent($"Used HP Potion (ID: {BotConfigManager.Current.HpPotionItemId}). HP: {player.Hp}/{player.MaxHp}");
                }
                else if (player.MaxSp > 0 && ((float)player.Sp / player.MaxSp * 100.0f) < BotConfigManager.Current.SpPotionPercent)
                {
                    netManager.SendUseItem(BotConfigManager.Current.SpPotionItemId);
                    lastPotionTime = now;
                    LogEvent($"Used SP Potion (ID: {BotConfigManager.Current.SpPotionItemId}). SP: {player.Sp}/{player.MaxSp}");
                }
            }

            // 2. Cross-Map Travel Check (AutoTravel)
            if (BotConfigManager.Current.AutoTravel &&
                !string.IsNullOrWhiteSpace(BotConfigManager.Current.TargetMap) &&
                !string.Equals(netManager.CurrentMap, BotConfigManager.Current.TargetMap, StringComparison.OrdinalIgnoreCase))
            {
                if (now - lastTravelTime >= 1.5f)
                {
                    var route = WorldGraph.Instance.FindRoute(netManager.CurrentMap, BotConfigManager.Current.TargetMap);
                    if (route != null && route.Count > 0)
                    {
                        var nextWarp = route[0];
                        netManager.SendMoveRequest(netManager.CurrentMap, nextWarp.FromPos.x, nextWarp.FromPos.y);
                        lastTravelTime = now;
                        CurrentState = BotState.TravelingToTargetMap;
                        LogEvent($"Cross-Map Travel: Route to {BotConfigManager.Current.TargetMap} via warp at {nextWarp.FromMap} ({nextWarp.FromPos.x}, {nextWarp.FromPos.y})");
                        return;
                    }
                    else
                    {
                        LogEvent($"[Warning] No valid warp route found from '{netManager.CurrentMap}' to '{BotConfigManager.Current.TargetMap}'.");
                    }
                }
            }

            // 3. Auto-Loot Check
            if (BotConfigManager.Current.AutoLoot && now - lastLootTime >= BotConfigManager.Current.LootCooldownSeconds)
            {
                var nearestItem = FindNearestGroundItem(player.CellPosition);
                if (nearestItem != null)
                {
                    netManager.SendPickUpItem(nearestItem.EntityId);
                    lastLootTime = now;
                    LootCount++;
                    CurrentState = BotState.LootingItem;
                    LogEvent($"Looting item: {nearestItem.ItemName} (Entity: {nearestItem.EntityId})");
                    return;
                }
            }

            // 4. Auto-Attack Check
            if (BotConfigManager.Current.AutoAttack)
            {
                var targetMonster = FindBestTargetMonster(player.CellPosition);
                if (targetMonster != null)
                {
                    CurrentTargetName = targetMonster.Name;
                    CurrentTargetHp = targetMonster.Hp;
                    CurrentTargetMaxHp = targetMonster.MaxHp;

                    // Track kill count when monster HP drops to 0
                    if (lastTargetId == targetMonster.Id && lastTargetHp > 0 && targetMonster.Hp <= 0)
                    {
                        KillCount++;
                        LogEvent($"Defeated target monster {targetMonster.Name}!");
                    }
                    lastTargetId = targetMonster.Id;
                    lastTargetHp = targetMonster.Hp;

                    float dist = Vector2.Distance(player.CellPosition, targetMonster.CellPosition);

                    if (dist <= BotConfigManager.Current.AttackRange)
                    {
                        if (now - lastAttackTime >= BotConfigManager.Current.AttackCooldownSeconds)
                        {
                            netManager.SendAttack(targetMonster.Id);
                            lastAttackTime = now;
                            CurrentState = BotState.AttackingTarget;
                        }
                    }
                    else
                    {
                        // Move closer to monster
                        netManager.SendMoveRequest(netManager.CurrentMap, targetMonster.CellPosition.x, targetMonster.CellPosition.y);
                        CurrentState = BotState.ApproachingTarget;
                    }
                    return;
                }
                else
                {
                    CurrentTargetName = "None";
                    CurrentTargetHp = 0;
                    CurrentTargetMaxHp = 0;
                }
            }

            // 5. Heatmap Auto-Wander Check (Systematic Map Exploration)
            if (BotConfigManager.Current.AutoWander && now - lastWanderTime >= BotConfigManager.Current.WanderCooldownSeconds)
            {
                Vector2Int targetPos = MapHeatmap.Instance.FindColdestSectorTarget(
                    netManager.CurrentMap,
                    player.CellPosition,
                    BotConfigManager.Current.PortalSafetyRadius);

                netManager.SendMoveRequest(netManager.CurrentMap, targetPos.x, targetPos.y);
                lastWanderTime = now;
                CurrentState = BotState.Wandering;
                LogEvent($"Exploring map to coldest sector target ({targetPos.x}, {targetPos.y})");
                return;
            }

            CurrentState = BotState.Idle;
        }

        private ServerControllable FindBestTargetMonster(Vector2Int playerPos)
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null) return null;

            ServerControllable bestTarget = null;
            float minDistance = float.MaxValue;

            foreach (var kvp in netManager.EntityList)
            {
                var entity = kvp.Value;
                if (entity == null || entity.Id == netManager.PlayerId)
                    continue;

                if (entity.CharacterType == CharacterType.Monster && entity.IsAttackable && entity.Hp > 0 && entity.CharacterState != CharacterState.Dead)
                {
                    // Whitelist check
                    if (BotConfigManager.Current.TargetMonsterWhitelist.Count > 0 &&
                        !BotConfigManager.Current.TargetMonsterWhitelist.Contains(entity.Name))
                        continue;

                    // Blacklist check
                    if (BotConfigManager.Current.TargetMonsterBlacklist.Contains(entity.Name))
                        continue;

                    float dist = Vector2.Distance(playerPos, entity.CellPosition);
                    if (dist <= BotConfigManager.Current.SearchRadius && dist < minDistance)
                    {
                        minDistance = dist;
                        bestTarget = entity;
                    }
                }
            }
            return bestTarget;
        }

        private GroundItem FindNearestGroundItem(Vector2Int playerPos)
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null) return null;

            GroundItem bestItem = null;
            float minDistance = float.MaxValue;

            foreach (var kvp in netManager.GroundItemList)
            {
                var item = kvp.Value;
                if (item == null) continue;

                // Whitelist check
                if (BotConfigManager.Current.LootItemWhitelist.Count > 0 &&
                    !BotConfigManager.Current.LootItemWhitelist.Contains(item.ItemName))
                    continue;

                // Blacklist check
                if (BotConfigManager.Current.LootItemBlacklist.Contains(item.ItemName))
                    continue;

                Vector2 itemCell = new Vector2(item.transform.position.x, item.transform.position.z);
                float dist = Vector2.Distance(playerPos, itemCell);
                if (dist <= BotConfigManager.Current.SearchRadius && dist < minDistance)
                {
                    minDistance = dist;
                    bestItem = item;
                }
            }
            return bestItem;
        }

        public string GetCurrentMapName()
        {
            var netManager = NetworkManager.Instance;
            return netManager != null ? netManager.CurrentMap : "Unknown";
        }

        public List<string> GetActiveMonstersOnMap()
        {
            var result = new HashSet<string>();
            var netManager = NetworkManager.Instance;
            if (netManager != null)
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

        public List<(int itemId, string name, int count)> GetInventoryPotionItems()
        {
            var list = new List<(int itemId, string name, int count)>();
            var inv = Assets.Scripts.PlayerControl.ClientInventory.Instance;
            if (inv != null && inv.Items != null)
            {
                foreach (var item in inv.Items)
                {
                    if (item.ItemData != null)
                    {
                        list.Add((item.ItemData.Id, item.ItemData.Name, item.Count));
                    }
                }
            }
            return list;
        }
    }
}
