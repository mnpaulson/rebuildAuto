using System;
using System.Collections.Generic;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI.Hud;
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
        public bool IsRecovering => isRecovering;

        public bool IsPlayerSitting(ServerControllable player)
        {
            return player != null && player.SpriteAnimator != null && player.SpriteAnimator.State == Assets.Scripts.SpriteState.Sit;
        }

        public int GetAvailableFlyWingId()
        {
            int primaryId = BotConfigManager.Current.FlyWingItemId;
            const int noviceFlyWingId = 12323;

            var playerState = PlayerState.Instance;
            if (playerState != null && playerState.Inventory != null)
            {
                var invData = playerState.Inventory.GetInventoryData();
                if (invData != null)
                {
                    bool hasPrimary = false;
                    bool hasNovice = false;

                    foreach (var kvp in invData)
                    {
                        var item = kvp.Value;
                        if (item.ItemData != null && item.Count > 0)
                        {
                            if (item.ItemData.Id == primaryId)
                                hasPrimary = true;
                            if (item.ItemData.Id == noviceFlyWingId)
                                hasNovice = true;
                        }
                    }

                    if (hasPrimary) return primaryId;
                    if (hasNovice) return noviceFlyWingId;
                    return -1; // Out of wings!
                }
            }

            return primaryId;
        }

        public bool GetBestAvailableHpPotion(out int selectedPotionId, out int totalPotionCount)
        {
            selectedPotionId = -1;
            totalPotionCount = 0;

            var playerState = PlayerState.Instance;
            if (playerState == null || playerState.Inventory == null) return false;
            var invData = playerState.Inventory.GetInventoryData();
            if (invData == null) return false;

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
                return StatusEffectPanel.Instance.StatusEffectLookup.ContainsKey(CharacterStatusEffect.IncreasedAttackSpeed);
            }
            return false;
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
            if (playerState == null || playerState.Inventory == null) return false;
            var invData = playerState.Inventory.GetInventoryData();
            if (invData == null) return false;

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
            if (now - lastAspdPotionTime < 3.0f) return false; // Cooldown between potion use attempts
            if (HasAspdBuffActive()) return false;

            if (GetBestAvailableAspdPotion(out int potionId, out string potionName))
            {
                netManager.SendUseItem(potionId);
                lastAspdPotionTime = now;
                BotEngine.Instance?.LogEvent($"[Survival] Used {potionName} (ID: {potionId}) for ASPD boost.");
                return true;
            }

            return false;
        }

        public List<(int itemId, string name, int count, bool isUsable)> GetInventoryPotionItems()
        {
            var list = new List<(int itemId, string name, int count, bool isUsable)>();
            var playerState = PlayerState.Instance;
            if (playerState != null && playerState.Inventory != null)
            {
                var invData = playerState.Inventory.GetInventoryData();
                if (invData != null)
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

            // 2. Emergency Fly Wing Check (Low HP Escape)
            if (BotConfigManager.Current.EmergencyFlyWingOnLowHp && player.MaxHp > 0)
            {
                if (hpPercent <= BotConfigManager.Current.EmergencyFlyWingHpPercent)
                {
                    if (isOutOfPotions) isRecovering = true;
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

            // 3. Monster Avoidance Check (Highest Priority - Teleport or flee immediately!)
            if (BotConfigManager.Current.AutoAvoidMonsters)
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
            if (BotConfigManager.Current.AutoSitToRecover)
            {
                // Enter recovery mode when out of potions and HP <= SitHpPercent or EmergencyFlyWing threshold
                float sitThreshold = Math.Max(BotConfigManager.Current.SitHpPercent, BotConfigManager.Current.EmergencyFlyWingHpPercent);
                if (isOutOfPotions && hpPercent <= sitThreshold)
                {
                    isRecovering = true;
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
