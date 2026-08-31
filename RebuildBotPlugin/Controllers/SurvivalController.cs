using System;
using System.Collections.Generic;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI.Hud;
using RebuildBotPlugin.Services;
using RebuildSharedData.Data;
using RebuildSharedData.Enum;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public class SurvivalController
    {
        private float lastPotionTime = 0f;
        private float lastOutOfPotionWarningTime = 0f;
        private float lastFlyWingTime = 0f;
        private float lastFleeStepTime = 0f;
        private float lastAspdPotionTime = 0f;
        private bool isRecovering = false;

        public float LastFlyWingTime => lastFlyWingTime;
        public bool IsRecovering => isRecovering && BotConfigManager.Current.AutoSitToRecover;

        public void Clear()
        {
            isRecovering = false;
            lastFlyWingTime = 0f;
            lastPotionTime = 0f;
            lastFleeStepTime = 0f;
            lastAspdPotionTime = 0f;
        }

        public void ClearRecovery()
        {
            isRecovering = false;
        }

        public bool IsPlayerSitting(ServerControllable player)
        {
            return player != null && player.SpriteAnimator != null && player.SpriteAnimator.State == Assets.Scripts.SpriteState.Sit;
        }

        public int GetAvailableFlyWingId()
        {
            int primaryId = BotConfigManager.Current.FlyWingItemId;
            const int noviceFlyWingId = 12323;
            return InventoryHelper.FindFirstItemId(primaryId, noviceFlyWingId);
        }

        public bool GetBestAvailableHpPotion(out int selectedPotionId, out int totalPotionCount)
        {
            selectedPotionId = -1;
            totalPotionCount = 0;

            if (!InventoryHelper.TryGetInventoryData(out var invData)) return false;

            var enabledIds = BotConfigManager.Current.HpPotionItemIds;
            if (enabledIds == null || enabledIds.Count == 0) return false;

            // Sort enabled potion IDs ascending (lowest ID / lightest heal first)
            var sortedIds = new List<int>(enabledIds);
            sortedIds.Sort();

            // Find all available enabled potions in inventory
            Dictionary<int, int> counts = new Dictionary<int, int>();
            foreach (var kvp in invData)
            {
                var item = kvp.Value;
                if (item.ItemData != null && item.Count > 0 && enabledIds.Contains(item.ItemData.Id))
                {
                    counts[item.ItemData.Id] = counts.GetValueOrDefault(item.ItemData.Id, 0) + item.Count;
                    totalPotionCount += item.Count;
                }
            }

            // Pick the first available in sortedIds (lightest heal first)
            foreach (int id in sortedIds)
            {
                if (counts.TryGetValue(id, out int count) && count > 0)
                {
                    selectedPotionId = id;
                    return true;
                }
            }

            return false;
        }

        public int GetHpPotionCount()
        {
            GetBestAvailableHpPotion(out _, out int totalCount);
            return totalCount;
        }

        public const int ConcentrationPotionId = 645;
        public const int AwakeningPotionId = 656;
        public const int BerserkPotionId = 657;

        public bool HasAspdBuffActive()
        {
            if (StatusEffectPanel.Instance != null && StatusEffectPanel.Instance.StatusEffectLookup != null)
            {
                if (StatusEffectPanel.Instance.StatusEffectLookup.TryGetValue(CharacterStatusEffect.IncreasedAttackSpeed, out var entry))
                {
                    if (entry != null && !entry.IsExpired)
                        return true;
                }
            }
            return false;
        }

        public static bool IsInTargetMap(NetworkManager netManager)
        {
            if (netManager == null) return false;
            string currentMap = netManager.CurrentMap;
            if (string.IsNullOrEmpty(currentMap)) return false;

            if (TownRoutineController.IsTownMap(currentMap)) return false;

            string targetMap = (BotConfigManager.Current.TargetMap ?? "").Trim();
            if (!string.IsNullOrEmpty(targetMap))
            {
                return string.Equals(currentMap, targetMap, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        public bool CanUseAwakeningPotion(int level, int jobId)
        {
            // Requires base level >= 40 and not Acolyte (4) or Priest (9)
            return level >= 40 && jobId != 4 && jobId != 9;
        }

        public bool CanUseBerserkPotion(int level, int jobId)
        {
            // Requires base level >= 85 and martial jobs (Swordsman, Knight, Blacksmith, etc.)
            return level >= 85 && (jobId == 1 || jobId == 3 || jobId == 6 || jobId == 7 || jobId == 8 || jobId == 11 || jobId == 12);
        }

        public bool GetBestAvailableAspdPotion(out int selectedPotionId, out string selectedPotionName)
        {
            selectedPotionId = -1;
            selectedPotionName = "";

            var playerState = PlayerState.Instance;
            if (playerState == null) return false;
            if (!InventoryHelper.TryGetInventoryData(out var invData)) return false;

            int berserkCount = 0;
            int awakeningCount = 0;
            int concentrationCount = 0;

            foreach (var kvp in invData)
            {
                var item = kvp.Value;
                if (item.ItemData != null && item.Count > 0)
                {
                    if (item.ItemData.Id == BerserkPotionId) berserkCount += item.Count;
                    else if (item.ItemData.Id == AwakeningPotionId) awakeningCount += item.Count;
                    else if (item.ItemData.Id == ConcentrationPotionId) concentrationCount += item.Count;
                }
            }

            string pref = (BotConfigManager.Current.AspdPotionPreference ?? "Auto").Trim();

            // 1. Explicit Preference
            if (string.Equals(pref, "Berserk Potion", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pref, "Berserk_Potion", StringComparison.OrdinalIgnoreCase))
            {
                if (berserkCount > 0 && CanUseBerserkPotion(playerState.Level, playerState.JobId))
                {
                    selectedPotionId = BerserkPotionId;
                    selectedPotionName = "Berserk Potion";
                    return true;
                }
                return false;
            }

            if (string.Equals(pref, "Awakening Potion", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pref, "Awakening_Potion", StringComparison.OrdinalIgnoreCase))
            {
                if (awakeningCount > 0 && CanUseAwakeningPotion(playerState.Level, playerState.JobId))
                {
                    selectedPotionId = AwakeningPotionId;
                    selectedPotionName = "Awakening Potion";
                    return true;
                }
                return false;
            }

            if (string.Equals(pref, "Concentration Potion", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pref, "Concentration_Potion", StringComparison.OrdinalIgnoreCase))
            {
                if (concentrationCount > 0)
                {
                    selectedPotionId = ConcentrationPotionId;
                    selectedPotionName = "Concentration Potion";
                    return true;
                }
                return false;
            }

            // 2. Auto Selection: Use highest eligible tier available in inventory
            if (berserkCount > 0 && CanUseBerserkPotion(playerState.Level, playerState.JobId))
            {
                selectedPotionId = BerserkPotionId;
                selectedPotionName = "Berserk Potion";
                return true;
            }

            if (awakeningCount > 0 && CanUseAwakeningPotion(playerState.Level, playerState.JobId))
            {
                selectedPotionId = AwakeningPotionId;
                selectedPotionName = "Awakening Potion";
                return true;
            }

            if (concentrationCount > 0)
            {
                selectedPotionId = ConcentrationPotionId;
                selectedPotionName = "Concentration Potion";
                return true;
            }

            return false;
        }

        public bool TryUseAspdPotion(NetworkManager netManager, float now)
        {
            if (!BotConfigManager.Current.AutoAspdPotion) return false;
            if (BotEngine.Instance != null && BotEngine.Instance.TownRoutine != null && BotEngine.Instance.TownRoutine.IsActive) return false;
            if (!IsInTargetMap(netManager)) return false;
            if (now - lastAspdPotionTime < 1.0f) return false;
            if (HasAspdBuffActive()) return false;

            if (GetBestAvailableAspdPotion(out int potionId, out string potionName))
            {
                netManager.SendUseItem(potionId);
                lastAspdPotionTime = now;
                BotEngine.Instance?.LogEvent($"[Survival] Used {potionName} (ID: {potionId}) for ASPD boost on '{netManager.CurrentMap}'.");
                return true;
            }

            return false;
        }

        public List<(int itemId, string name, int count, bool isUsable)> GetInventoryPotionItems()
        {
            var list = new List<(int itemId, string name, int count, bool isUsable)>();
            if (InventoryHelper.TryGetInventoryData(out var invData))
            {
                foreach (var kvp in invData)
                {
                    var item = kvp.Value;
                    if (item.ItemData != null && item.Count > 0)
                    {
                        bool isUsable = item.ItemData.ItemClass == ItemClass.Useable ||
                                       item.ItemData.UseType != ItemUseType.NotUsable;
                        list.Add((item.ItemData.Id, item.ItemData.Name, item.Count, isUsable));
                    }
                }
            }

            int currentPotId = BotConfigManager.Current.HpPotionItemId;
            int currentWingId = BotConfigManager.Current.FlyWingItemId;

            list.Sort((a, b) =>
            {
                if (a.itemId == currentPotId) return -1;
                if (b.itemId == currentPotId) return 1;
                if (a.itemId == currentWingId) return -1;
                if (b.itemId == currentWingId) return 1;

                if (a.isUsable != b.isUsable)
                    return a.isUsable ? -1 : 1;

                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            });

            return list;
        }

        public ServerControllable GetNearbyHostileMonster(Vector2Int playerPos, float radius)
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null || netManager.EntityList == null) return null;

            foreach (var kvp in netManager.EntityList)
            {
                var entity = kvp.Value;
                if (entity == null || entity.Id == netManager.PlayerId) continue;

                if (entity.CharacterType == CharacterType.Monster && entity.IsCharacterAlive && entity.Hp > 0 && !entity.IsAlly)
                {
                    if (Vector2.Distance(playerPos, entity.CellPosition) <= radius)
                    {
                        return entity;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Handles auto-potions, emergency fly wings, boss avoidance, and sitting/resting state machine.
        /// Returns true if an urgent survival action took place that interrupts the rest of the bot update loop.
        /// </summary>
        public bool ProcessSurvival(
            NetworkManager netManager,
            ServerControllable player,
            float now,
            TargetingController targeting,
            NavigationController navigation,
            Action onTeleport,
            ref BotState currentState)
        {
            float flyWingCd = BotConfigManager.Current.FlyWingCooldownSeconds;
            float hpPercent = player.MaxHp > 0 ? ((float)player.Hp / player.MaxHp * 100.0f) : 100.0f;
            int potionCount = GetHpPotionCount();
            bool isOutOfPotions = (potionCount == 0);
            bool isSitting = IsPlayerSitting(player);

            // 1. Auto-Potion Check
            if (BotConfigManager.Current.AutoPotion && now - lastPotionTime > 0.5f)
            {
                if (hpPercent < BotConfigManager.Current.HpPotionPercent)
                {
                    if (GetBestAvailableHpPotion(out int potionToUse, out _))
                    {
                        netManager.SendUseItem(potionToUse);
                        lastPotionTime = now;
                        BotEngine.Instance?.LogEvent($"Used HP Potion (ID: {potionToUse}). HP: {player.Hp}/{player.MaxHp}");
                    }
                    else if (isOutOfPotions)
                    {
                        if (now - lastOutOfPotionWarningTime > 15.0f)
                        {
                            BotEngine.Instance?.LogEvent($"[Warning] Out of HP Potions! HP: {player.Hp}/{player.MaxHp} ({hpPercent:F0}%)");
                            lastOutOfPotionWarningTime = now;
                        }
                    }
                }
            }

            // 1.5. Auto-ASPD Potion Check (Refreshes immediately upon expiration on target map)
            TryUseAspdPotion(netManager, now);

            // 2. Emergency Escape Check (Low HP Escape)
            if (BotConfigManager.Current.EmergencyFlyWingOnLowHp && player.MaxHp > 0)
            {
                if (hpPercent <= BotConfigManager.Current.EmergencyFlyWingHpPercent)
                {
                    if (isOutOfPotions) isRecovering = true;

                    // If critically low on HP and completely out of HP items on a field map:
                    // If AutoReturnOnOutOfHpItems is enabled, immediately warp to town rather than spamming random Fly Wings!
                    if (isOutOfPotions && BotConfigManager.Current.AutoReturnOnOutOfHpItems && !TownRoutineController.IsTownMap(netManager.CurrentMap))
                    {
                        if (BotEngine.Instance != null && BotEngine.Instance.TownRoutine != null && !BotEngine.Instance.TownRoutine.IsActive)
                        {
                            BotEngine.Instance.TownRoutine.StartRoutine("Emergency escape while out of HP items");
                            if (isSitting) netManager.ChangePlayerSitStand(false);
                            if (BotEngine.Instance.TownRoutine.ProcessTownRoutine(netManager, player, navigation, now))
                            {
                                currentState = BotState.TravelingToTargetMap;
                                return true;
                            }
                        }
                    }

                    // For standard Fly Wing escape:
                    // Escape if currently being attacked, if a hostile threat is in close range (<= 6 tiles),
                    // or if this is the initial escape right when HP dropped critically low during engagement.
                    var currentAttacker = targeting.GetAttackingMonster(player.CellPosition);
                    var nearbyThreat = targeting.FindNearbyAggressiveThreat(player.CellPosition, 6.0f);
                    bool inDanger = (currentAttacker != null || nearbyThreat != null || currentState == BotState.AttackingTarget || currentState == BotState.ApproachingTarget);

                    if (inDanger)
                    {
                        int wingId = GetAvailableFlyWingId();
                        if (wingId > 0)
                        {
                            if (now - lastFlyWingTime >= flyWingCd)
                            {
                                if (isSitting) netManager.ChangePlayerSitStand(false);
                                netManager.SendUseItem(wingId);
                                lastFlyWingTime = now;
                                onTeleport?.Invoke();
                                currentState = BotState.Fleeing;
                                BotEngine.Instance?.LogEvent($"[Emergency Escape] HP critically low ({player.Hp}/{player.MaxHp} = {hpPercent:F0}% <= {BotConfigManager.Current.EmergencyFlyWingHpPercent}%)! Used Fly Wing (ID: {wingId}) to escape.");
                            }
                            currentState = BotState.Fleeing;
                            return true;
                        }
                    }
                }
            }

            // 3. Monster Avoidance Check (Highest Priority - Teleport or flee immediately!)
            if (BotConfigManager.Current.AutoAvoidMonsters && !TownRoutineController.IsTownMap(netManager.CurrentMap))
            {
                var dangerMonster = targeting.FindAvoidanceMonster();
                if (dangerMonster != null)
                {
                    int wingId = GetAvailableFlyWingId();
                    if (wingId > 0)
                    {
                        if (now - lastFlyWingTime >= flyWingCd)
                        {
                            if (isSitting) netManager.ChangePlayerSitStand(false);
                            netManager.SendUseItem(wingId);
                            lastFlyWingTime = now;
                            onTeleport?.Invoke();
                            currentState = BotState.Fleeing;
                            BotEngine.Instance?.LogEvent($"[Avoidance] Spotted dangerous monster '{dangerMonster.Name}'! Used Fly Wing (ID: {wingId}) to escape.");
                        }
                        currentState = BotState.Fleeing;
                        return true;
                    }
                    else
                    {
                        if (isSitting) netManager.ChangePlayerSitStand(false);
                        if (now - lastFleeStepTime >= 0.5f)
                        {
                            Vector2 fleeDir = (Vector2)(player.CellPosition - dangerMonster.CellPosition);
                            if (fleeDir == Vector2.zero) fleeDir = Vector2.right;
                            Vector2 fleeTarget = (Vector2)player.CellPosition + fleeDir.normalized * 12f;
                            Vector2Int fleePos = new Vector2Int(Mathf.RoundToInt(fleeTarget.x), Mathf.RoundToInt(fleeTarget.y));
                            navigation.SafeMoveTowards(player.CellPosition, fleePos);
                            lastFleeStepTime = now;
                            BotEngine.Instance?.LogEvent($"[Avoidance] Dangerous monster '{dangerMonster.Name}' in sight, but OUT OF FLY WINGS! Fleeing away.");
                        }
                        currentState = BotState.Fleeing;
                        return true;
                    }
                }
            }

            // 4. Safe Rest & Recovery (Sit/Stand State Machine)
            if (!BotConfigManager.Current.AutoSitToRecover)
            {
                if (isRecovering) isRecovering = false;
                if (isSitting)
                {
                    netManager.ChangePlayerSitStand(false);
                    BotEngine.Instance?.LogEvent("[Rest] AutoSitToRecover disabled. Standing up.");
                }
            }
            else
            {
                // Enter recovery mode when out of potions and HP <= SitHpPercent or EmergencyFlyWing threshold
                float sitThreshold = Math.Max(BotConfigManager.Current.SitHpPercent, BotConfigManager.Current.EmergencyFlyWingHpPercent);
                if (isOutOfPotions && hpPercent <= sitThreshold)
                {
                    if (!isRecovering)
                    {
                        isRecovering = true;
                        BotEngine.Instance?.LogEvent($"[Rest] Entered recovery mode (Out of HP potions, HP: {hpPercent:F0}% <= {sitThreshold:F0}%).");
                    }
                }

                // Exit recovery mode when fully restored to StandHpPercent or potions are replenished
                if (hpPercent >= BotConfigManager.Current.StandHpPercent || !isOutOfPotions)
                {
                    if (isRecovering)
                    {
                        isRecovering = false;
                        if (isSitting)
                        {
                            netManager.ChangePlayerSitStand(false);
                            BotEngine.Instance?.LogEvent($"[Rest] HP recovered to {hpPercent:F0}%! Standing up to resume hunting.");
                            currentState = BotState.Idle;
                            return true;
                        }
                    }
                }

                if (isRecovering)
                {
                    // While in recovery mode, check for nearby aggressive threats or attackers
                    var threat = targeting.FindNearbyAggressiveThreat(player.CellPosition, 8.0f);
                    var sitAttacker = targeting.GetAttackingMonster(player.CellPosition);

                    if (threat != null || sitAttacker != null)
                    {
                        // THREAT PRESENT: Stand up and fly wing away to find a safe spot!
                        if (isSitting)
                        {
                            netManager.ChangePlayerSitStand(false);
                            string threatName = threat != null ? threat.Name : sitAttacker.Name;
                            BotEngine.Instance?.LogEvent($"[Wake-Up] Hostile monster '{threatName}' approached while resting! Standing up.");
                        }

                        int wingId = GetAvailableFlyWingId();
                        if (wingId > 0 && now - lastFlyWingTime >= flyWingCd)
                        {
                            netManager.SendUseItem(wingId);
                            lastFlyWingTime = now;
                            onTeleport?.Invoke();
                            currentState = BotState.Fleeing;
                            BotEngine.Instance?.LogEvent($"[Rest] Threat nearby ({threat?.Name ?? sitAttacker?.Name}) — using Fly Wing (ID: {wingId}) to find a safe spot.");
                            return true;
                        }

                        // If wing is on cooldown, defend if attacked, else wait in Fleeing
                        if (sitAttacker != null)
                        {
                            return false; // Let self-defense engage the attacker hitting us
                        }

                        currentState = BotState.Fleeing;
                        return true;
                    }

                    // NO THREATS IN 8 TILES: Prioritize looting over sitting!
                    if (BotConfigManager.Current.AutoLoot && BotEngine.Instance != null && BotEngine.Instance.Loot != null)
                    {
                        bool hasPendingLoot = BotEngine.Instance.Loot.PendingLootItemId != -1;
                        var nearbyLoot = BotEngine.Instance.Loot.FindNearestGroundItem(player.CellPosition);
                        if (hasPendingLoot || nearbyLoot != null)
                        {
                            if (isSitting)
                            {
                                netManager.ChangePlayerSitStand(false);
                            }
                            return false; // Yield to Priority 5 (Auto-Loot) before sitting!
                        }
                    }

                    if (isSitting)
                    {
                        currentState = BotState.Resting;
                        return true; // Rest peacefully until StandHpPercent!
                    }
                    else
                    {
                        // Settle for 0.8s after teleport/moving before sitting down
                        if (!player.IsMoving && now - lastFlyWingTime >= 0.8f)
                        {
                            // Always face South when sitting down
                            netManager.ChangePlayerFacing(Direction.South, HeadFacing.Center);
                            netManager.ChangePlayerSitStand(true);
                            currentState = BotState.Resting;
                            BotEngine.Instance?.LogEvent($"[Rest] Safe spot found (no aggressive threats in 8 tiles). Sitting down facing South to recover HP ({hpPercent:F0}% -> {BotConfigManager.Current.StandHpPercent}%)...");
                            return true;
                        }
                        else
                        {
                            // Waiting to settle after fly wing / stop moving: HOLD POSITION, do NOT attack!
                            currentState = BotState.Resting;
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
