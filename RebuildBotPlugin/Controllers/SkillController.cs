using System;
using System.Collections.Generic;
using Assets.Scripts.Data;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI.Hud;
using RebuildBotPlugin.Models;
using RebuildBotPlugin.Services;
using RebuildSharedData.Enum;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public class SkillController
    {
        private readonly Dictionary<int, float> ruleLastCastTimes = new();
        private readonly HashSet<(int targetId, CharacterSkill skill)> openerCasts = new();
        private float globalSkillCooldown = 0f;
        private int lastTargetId = -1;

        // Skill to status effect string mapping for BuffMaintenance
        private static readonly Dictionary<CharacterSkill, string> SkillToBuffNameMap = new()
        {
            [CharacterSkill.TwoHandQuicken] = "TwoHandQuicken",
            [CharacterSkill.Blessing] = "Blessing",
            [CharacterSkill.IncreaseAgility] = "IncreaseAgi",
            [CharacterSkill.Angelus] = "Angelus",
            [CharacterSkill.EnergyCoat] = "EnergyCoat",
            [CharacterSkill.ImproveConcentration] = "ImproveConcentration",
            [CharacterSkill.Endure] = "Endure",
            [CharacterSkill.Hiding] = "Hiding",
            [CharacterSkill.Cloaking] = "Cloaking",
            [CharacterSkill.AdrenalineRush] = "AdrenalineRush",
            [CharacterSkill.Sight] = "Sight",
            [CharacterSkill.Ruwach] = "Ruwach",
            [CharacterSkill.Provoke] = "Provoke"
        };

        private readonly Dictionary<(int targetId, CharacterSkill skill), float> targetBuffCastTimes = new();
        private readonly Dictionary<(string charName, CharacterSkill skill), float> targetBuffCastByNameTimes = new();

        public class HiddenThreatInfo
        {
            public int MonsterId;
            public int VictimId;
            public Vector2Int MonsterPos;
            public Vector2Int VictimPos;
            public float LastAttackTime;
        }

        private readonly Dictionary<int, HiddenThreatInfo> activeHiddenThreats = new();
        private float lastRuwachCastTime = 0f;
        private float lastRuwachMoveTime = 0f;

        public void Clear()
        {
            ruleLastCastTimes.Clear();
            targetBuffCastTimes.Clear();
            targetBuffCastByNameTimes.Clear();
            openerCasts.Clear();
            activeHiddenThreats.Clear();
            lastRuwachCastTime = 0f;
            lastRuwachMoveTime = 0f;
            globalSkillCooldown = 0f;
            lastTargetId = -1;
        }

        public CharacterSkill ParseSkill(string skillName)
        {
            return Services.SkillResolver.Resolve(skillName);
        }

        public int ResolveSkillLevel(CharacterSkill skill, int requestedLevel)
        {
            if (requestedLevel > 0) return requestedLevel;

            // If level is 0 (auto/max), look up player's learned or granted skill level
            var playerState = PlayerState.Instance;
            if (playerState != null)
            {
                if (playerState.KnownSkills != null && playerState.KnownSkills.TryGetValue(skill, out int knownLvl) && knownLvl > 0)
                    return knownLvl;
                if (playerState.GrantedSkills != null && playerState.GrantedSkills.TryGetValue(skill, out int grantedLvl) && grantedLvl > 0)
                    return grantedLvl;
            }

            return 1; // Default fallback level
        }

        public static int GetHealSpCost(int lvl)
        {
            lvl = Mathf.Clamp(lvl, 1, 10);
            return 10 + lvl * 3;
        }

        public bool HasHealWithSufficientSp()
        {
            var playerState = PlayerState.Instance;
            if (playerState == null || playerState.MaxSp <= 0) return false;

            var rules = BotConfigManager.Current.SkillRules;
            if (rules == null || rules.Count == 0) return false;

            float spPercent = (float)playerState.Sp / playerState.MaxSp * 100f;

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.Enabled) continue;

                var skill = ParseSkill(rule.Skill);
                if (skill == CharacterSkill.Heal)
                {
                    if (playerState.KnownSkills != null && playerState.KnownSkills.Count > 0 &&
                        !playerState.KnownSkills.ContainsKey(CharacterSkill.Heal) &&
                        (playerState.GrantedSkills == null || !playerState.GrantedSkills.ContainsKey(CharacterSkill.Heal)))
                    {
                        continue;
                    }

                    int lvl = ResolveSkillLevel(skill, rule.Level);
                    int spCost = GetHealSpCost(lvl);

                    int minSp = rule.MinSpPercent > 0 ? rule.MinSpPercent : 10;
                    if (spPercent >= minSp && playerState.Sp >= spCost)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool HasBuffActive(CharacterSkill skill, ServerControllable player = null)
        {
            var netManager = NetworkManager.Instance;
            if (player == null && netManager != null && netManager.EntityList != null && netManager.PlayerId > 0)
            {
                netManager.EntityList.TryGetValue(netManager.PlayerId, out player);
            }
            int playerId = player != null ? player.Id : (netManager != null ? netManager.PlayerId : 0);
            string playerName = player != null ? player.Name : "";
            return HasTargetBuffActive(skill, playerId, playerName, player, Time.time);
        }

        public bool HasStatusEffect(CharacterStatusEffect effect, ServerControllable player = null)
        {
            if (player == null)
            {
                var netManager = NetworkManager.Instance;
                if (netManager != null && netManager.EntityList != null && netManager.PlayerId > 0)
                {
                    netManager.EntityList.TryGetValue(netManager.PlayerId, out player);
                }
            }

            if (player != null && player.StatusEffectState != null)
            {
                try
                {
                    if (player.StatusEffectState.HasStatusEffect(effect))
                        return true;
                }
                catch { }
            }

            if (StatusEffectPanel.Instance != null && StatusEffectPanel.Instance.StatusEffectLookup != null)
            {
                if (StatusEffectPanel.Instance.StatusEffectLookup.ContainsKey(effect))
                    return true;
            }
            return false;
        }

        public static bool IsEntityHidden(ServerControllable entity)
        {
            if (entity == null) return false;
            if (entity.IsHidden) return true;
            if (entity.SpriteAnimator != null && entity.SpriteAnimator.IsHidden) return true;
            if (entity.StatusEffectState != null)
            {
                try
                {
                    if (entity.StatusEffectState.HasStatusEffect(CharacterStatusEffect.Hiding) ||
                        entity.StatusEffectState.HasStatusEffect(CharacterStatusEffect.Cloaking) ||
                        entity.StatusEffectState.HasStatusEffect(CharacterStatusEffect.Invisible))
                    {
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        public bool HasRuwachLearned()
        {
            var ps = PlayerState.Instance;
            if (ps == null) return false;
            if (ps.KnownSkills != null && ps.KnownSkills.TryGetValue(CharacterSkill.Ruwach, out int lvl) && lvl > 0) return true;
            if (ps.GrantedSkills != null && ps.GrantedSkills.TryGetValue(CharacterSkill.Ruwach, out int gLvl) && gLvl > 0) return true;
            return false;
        }

        public void OnAttackMotion(ServerControllable src, ServerControllable target)
        {
            if (src == null || target == null) return;
            if (src.CharacterType != CharacterType.Monster) return;

            // Check if attacker is hidden
            if (!IsEntityHidden(src)) return;

            var netManager = NetworkManager.Instance;
            bool isVictimSelf = netManager != null && target.Id == netManager.PlayerId;
            bool isVictimParty = BotEngine.Instance?.Party != null && BotEngine.Instance.Party.IsPartyOrFleetMember(target);

            if (isVictimSelf || isVictimParty)
            {
                activeHiddenThreats[src.Id] = new HiddenThreatInfo
                {
                    MonsterId = src.Id,
                    VictimId = target.Id,
                    MonsterPos = src.CellPosition,
                    VictimPos = target.CellPosition,
                    LastAttackTime = Time.time
                };
            }
        }

        public bool ProcessRuwachDefense(
            NetworkManager netManager,
            ServerControllable player,
            NavigationController navigation,
            float now)
        {
            if (netManager == null || player == null || navigation == null) return false;
            if (!HasRuwachLearned()) return false;

            var playerState = PlayerState.Instance;
            if (playerState == null || playerState.Sp < 10) return false;

            // 1. Clean up stale or resolved threats
            if (activeHiddenThreats.Count > 0)
            {
                List<int> toRemove = null;
                foreach (var kvp in activeHiddenThreats)
                {
                    int mId = kvp.Key;
                    var info = kvp.Value;
                    if (now - info.LastAttackTime > 6.0f)
                    {
                        toRemove ??= new List<int>();
                        toRemove.Add(mId);
                        continue;
                    }

                    if (netManager.EntityList != null && netManager.EntityList.TryGetValue(mId, out var mEntity))
                    {
                        if (mEntity == null || !mEntity.IsCharacterAlive || mEntity.Hp <= 0 || !IsEntityHidden(mEntity))
                        {
                            toRemove ??= new List<int>();
                            toRemove.Add(mId);
                        }
                    }
                }

                if (toRemove != null)
                {
                    for (int i = 0; i < toRemove.Count; i++)
                    {
                        activeHiddenThreats.Remove(toRemove[i]);
                    }
                }
            }

            // 2. Find closest hidden threat
            Vector2Int? bestThreatPos = null;
            float bestDist = float.MaxValue;
            string threatReason = "";

            // A) Reactive: registered hidden attackers attacking self or party
            if (activeHiddenThreats.Count > 0)
            {
                foreach (var kvp in activeHiddenThreats)
                {
                    var info = kvp.Value;
                    Vector2Int targetPos = info.MonsterPos;
                    if (netManager.EntityList != null && netManager.EntityList.TryGetValue(info.MonsterId, out var mEnt) && mEnt != null)
                    {
                        targetPos = mEnt.CellPosition;
                    }
                    else if (netManager.EntityList != null && netManager.EntityList.TryGetValue(info.VictimId, out var vEnt) && vEnt != null)
                    {
                        targetPos = vEnt.CellPosition;
                    }

                    float d = Vector2.Distance(player.CellPosition, targetPos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestThreatPos = targetPos;
                        threatReason = $"attack on {(info.VictimId == netManager.PlayerId ? "Self" : "Party Member")}";
                    }
                }
            }

            // B) Proactive sweep: scan EntityList for any hidden monster nearby
            if (netManager.EntityList != null)
            {
                var partyCtrl = BotEngine.Instance?.Party;
                foreach (var kvp in netManager.EntityList)
                {
                    var ent = kvp.Value;
                    if (ent == null || !ent.IsCharacterAlive || ent.Hp <= 0) continue;
                    if (ent.CharacterType != CharacterType.Monster) continue;
                    if (!IsEntityHidden(ent)) continue;

                    // 1. Proximity to self (<= 3.0 tiles)
                    float dSelf = Vector2.Distance(player.CellPosition, ent.CellPosition);
                    if (dSelf <= 3.0f && dSelf < bestDist)
                    {
                        bestDist = dSelf;
                        bestThreatPos = ent.CellPosition;
                        threatReason = $"proactive proximity to Self ({dSelf:F1} tiles)";
                    }

                    // 2. Proximity to any nearby party member (<= 3.0 tiles of party member)
                    if (partyCtrl != null && dSelf <= 16.0f)
                    {
                        foreach (var pKvp in netManager.EntityList)
                        {
                            var memberEnt = pKvp.Value;
                            if (memberEnt == null || memberEnt.Id == netManager.PlayerId) continue;
                            if (!memberEnt.IsCharacterAlive || memberEnt.Hp <= 0) continue;
                            if (!partyCtrl.IsPartyOrFleetMember(memberEnt)) continue;

                            float dMemberToMonster = Vector2.Distance(memberEnt.CellPosition, ent.CellPosition);
                            if (dMemberToMonster <= 3.0f && dSelf < bestDist)
                            {
                                bestDist = dSelf;
                                bestThreatPos = ent.CellPosition;
                                threatReason = $"proximity to party member '{memberEnt.Name}' ({dMemberToMonster:F1} tiles)";
                            }
                        }
                    }
                }
            }

            if (!bestThreatPos.HasValue)
            {
                return false;
            }

            Vector2Int pos = bestThreatPos.Value;
            bool isRuwachBuffActive = HasStatusEffect(CharacterStatusEffect.Ruwach, player) || (now - lastRuwachCastTime < 8.0f);

            // Ruwach radius is 2 tiles in all directions (5x5 square area)
            int dx = Math.Abs(player.CellPosition.x - pos.x);
            int dy = Math.Abs(player.CellPosition.y - pos.y);
            bool isWithinRuwachRadius = dx <= 2 && dy <= 2;

            if (isWithinRuwachRadius)
            {
                if (!isRuwachBuffActive)
                {
                    if (SurvivalController.IsSitting(player))
                    {
                        netManager.ChangePlayerSitStand(false);
                        BotEngine.Instance?.Survival?.ClearRecovery();
                    }

                    netManager.SendSelfTargetSkillAction(CharacterSkill.Ruwach, 1);
                    lastRuwachCastTime = now;
                    globalSkillCooldown = now;
                    ActiveCastEndTime = Time.timeSinceLevelLoad + 0.1f;
                    BotEngine.Instance?.LogEvent($"[Ruwach] Cast Ruwach (Lv 1) to reveal hidden threat ({threatReason}) at ({pos.x}, {pos.y}) (dist: {bestDist:F1}).");
                    return true;
                }

                // Ruwach is already pulsing around caster; threat will be revealed on server tick
                return false;
            }

            // Beyond 2 tiles radius: reposition towards threat to bring it within Ruwach aura
            if (SurvivalController.IsSitting(player))
            {
                netManager.ChangePlayerSitStand(false);
                BotEngine.Instance?.Survival?.ClearRecovery();
            }

            if (now - lastRuwachMoveTime >= 0.25f)
            {
                lastRuwachMoveTime = now;
                navigation.NavigateTowards(player.CellPosition, pos, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                BotEngine.Instance?.LogDebug($"[Ruwach] Repositioning towards hidden threat ({threatReason}) at ({pos.x}, {pos.y}) (dist: {bestDist:F1}).");
            }

            return true;
        }

        public void RecordTargetBuffCast(int targetEntityId, string charName, CharacterSkill skill, float now)
        {
            if (targetEntityId > 0)
            {
                targetBuffCastTimes[(targetEntityId, skill)] = now;
            }
            if (!string.IsNullOrEmpty(charName))
            {
                targetBuffCastByNameTimes[(charName.ToLowerInvariant(), skill)] = now;
            }
        }

        public static float GetEstimatedBuffDuration(CharacterSkill skill, int level)
        {
            int lvl = Math.Max(1, level);
            switch (skill)
            {
                case CharacterSkill.Blessing:
                    return 40f + (lvl * 20f); // Lv 1: 60s ... Lv 10: 240s
                case CharacterSkill.IncreaseAgility:
                    return 40f + (lvl * 10f); // Lv 1: 50s ... Lv 10: 140s
                case CharacterSkill.Angelus:
                    return Math.Max(30f, lvl * 30f); // Lv 1: 30s ... Lv 10: 300s
                case CharacterSkill.TwoHandQuicken:
                    return Math.Max(30f, lvl * 30f);
                case CharacterSkill.ImproveConcentration:
                    return 60f + (lvl * 20f);
                case CharacterSkill.AdrenalineRush:
                    return Math.Max(30f, lvl * 30f);
                case CharacterSkill.EnergyCoat:
                    return 300f;
                default:
                    return 60f;
            }
        }

        public static float GetSkillCastDuration(CharacterSkill skill, int lvl = 1)
        {
            switch (skill)
            {
                case CharacterSkill.IncreaseAgility: return 1.0f;
                case CharacterSkill.Angelus: return 0.5f;
                case CharacterSkill.HolyLight: return 1.5f;
                case CharacterSkill.FireBolt:
                case CharacterSkill.ColdBolt:
                case CharacterSkill.LightningBolt:
                    return 0.7f * Math.Max(1, lvl);
                case CharacterSkill.FireBall: return 1.5f;
                case CharacterSkill.ThunderStorm: return 1.0f + (lvl * 0.5f);
                default: return 0.0f;
            }
        }

        public static bool IsSelfTargetAoEPartyBuff(CharacterSkill skill)
        {
            return skill == CharacterSkill.Angelus;
        }

        public bool TryGetTargetBuffRemainingTime(
            CharacterSkill skill,
            int targetEntityId,
            string targetCharName,
            ServerControllable targetEntity,
            out float remainingSeconds)
        {
            remainingSeconds = 0f;

            if (!SkillToBuffNameMap.TryGetValue(skill, out string buffName) ||
                !Enum.TryParse<CharacterStatusEffect>(buffName, true, out var effect))
            {
                return false;
            }

            var netManager = NetworkManager.Instance;
            var playerState = PlayerState.Instance;

            // 1. Direct ServerControllable StatusEffectState check (authoritative for loaded entity)
            if (targetEntity != null && targetEntity.StatusEffectState != null)
            {
                try
                {
                    var effects = targetEntity.StatusEffectState.GetStatusEffects();
                    if (effects != null && effects.TryGetValue(effect, out float endTime))
                    {
                        remainingSeconds = endTime - Time.timeSinceLevelLoad;
                        return true;
                    }
                }
                catch { }
            }

            // 2. Self check via player controllable or StatusEffectPanel
            if (netManager != null && (targetEntityId == netManager.PlayerId || (targetEntity != null && targetEntity.Id == netManager.PlayerId)))
            {
                ServerControllable localPlayer = targetEntity;
                if (localPlayer == null && netManager.EntityList != null && netManager.PlayerId > 0)
                {
                    netManager.EntityList.TryGetValue(netManager.PlayerId, out localPlayer);
                }

                if (localPlayer != null && localPlayer.StatusEffectState != null)
                {
                    try
                    {
                        var effects = localPlayer.StatusEffectState.GetStatusEffects();
                        float endTime = 0f;
                        if (effects != null && effects.TryGetValue(effect, out endTime))
                        {
                            remainingSeconds = endTime - Time.timeSinceLevelLoad;
                            return true;
                        }
                    }
                    catch { }
                }

                if (StatusEffectPanel.Instance != null && StatusEffectPanel.Instance.BuffPanel != null)
                {
                    for (int i = 0; i < StatusEffectPanel.Instance.BuffPanel.childCount; i++)
                    {
                        var child = StatusEffectPanel.Instance.BuffPanel.GetChild(i);
                        if (child != null && child.gameObject.activeSelf)
                        {
                            var entry = child.GetComponent<StatusEffectEntry>();
                            if (entry != null && entry.StatusEffect == effect)
                            {
                                remainingSeconds = entry.Expiration - Time.timeSinceLevelLoad;
                                return true;
                            }
                        }
                    }
                }
            }

            // 3. In-Game Party Panel (UiManager.Instance.PartyPanel)
            if (playerState != null && playerState.IsInParty && UiManager.Instance != null && UiManager.Instance.PartyPanel != null)
            {
                int partyMemberId = -1;
                if (targetEntityId > 0 && playerState.PartyMemberIdLookup != null &&
                    playerState.PartyMemberIdLookup.TryGetValue(targetEntityId, out int mid))
                {
                    partyMemberId = mid;
                }
                else if (playerState.PartyMembers != null)
                {
                    foreach (var kvp in playerState.PartyMembers)
                    {
                        var info = kvp.Value;
                        if (info == null) continue;
                        if ((targetEntityId > 0 && info.EntityId == targetEntityId) ||
                            (!string.IsNullOrEmpty(targetCharName) && string.Equals(info.PlayerName, targetCharName, StringComparison.OrdinalIgnoreCase)))
                        {
                            partyMemberId = kvp.Key;
                            break;
                        }
                    }
                }

                if (partyMemberId > 0 && UiManager.Instance.PartyPanel.PartyEntryLookup.TryGetValue(partyMemberId, out var panelEntry) &&
                    panelEntry != null && panelEntry.DebuffArea != null)
                {
                    for (int c = 0; c < panelEntry.DebuffArea.childCount; c++)
                    {
                        var child = panelEntry.DebuffArea.GetChild(c);
                        if (child != null && child.gameObject.activeSelf)
                        {
                            var entry = child.GetComponent<StatusEffectEntry>();
                            if (entry != null && entry.StatusEffect == effect)
                            {
                                remainingSeconds = entry.Expiration - Time.timeSinceLevelLoad;
                                return true;
                            }
                        }
                    }
                }
            }

            // 4. Fallback for self: HasStatusEffect
            if (netManager != null && targetEntityId == netManager.PlayerId)
            {
                ServerControllable localPlayer = targetEntity;
                if (localPlayer == null && netManager.EntityList != null && netManager.PlayerId > 0)
                {
                    netManager.EntityList.TryGetValue(netManager.PlayerId, out localPlayer);
                }
                if (HasStatusEffect(effect, localPlayer))
                {
                    remainingSeconds = 30f;
                    return true;
                }
            }

            return false;
        }

        public bool HasTargetBuffActive(CharacterSkill skill, int targetEntityId, string targetCharName, ServerControllable targetEntity, float now, int skillLevel = 1)
        {
            // 1. Cast attempt debounce:
            // If we just sent a cast packet within the last 2.5s, wait for animation/server roundtrip
            const float CastAttemptDebounce = 2.5f;
            if (targetEntityId > 0 && targetBuffCastTimes.TryGetValue((targetEntityId, skill), out float lastCastById))
            {
                if (now - lastCastById < CastAttemptDebounce)
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(targetCharName) &&
                targetBuffCastByNameTimes.TryGetValue((targetCharName.ToLowerInvariant(), skill), out float lastCastByName))
            {
                if (now - lastCastByName < CastAttemptDebounce)
                {
                    return true;
                }
            }

            // 2. Check live in-game status effect and expiration time
            if (TryGetTargetBuffRemainingTime(skill, targetEntityId, targetCharName, targetEntity, out float remainingSeconds))
            {
                // Proactive refresh: if remaining duration > 8 seconds, consider it active.
                // If <= 8 seconds, return false so the bot refreshes it proactively!
                return remainingSeconds > 8.0f;
            }

            return false;
        }

        public float GetSkillCastRange(CharacterSkill skill)
        {
            switch (skill)
            {
                // Ranged magic / bolt spells
                case CharacterSkill.FireBolt:
                case CharacterSkill.ColdBolt:
                case CharacterSkill.LightningBolt:
                case CharacterSkill.SoulStrike:
                case CharacterSkill.FireBall:
                case CharacterSkill.FrostDiver:
                case CharacterSkill.ThunderStorm:
                case CharacterSkill.StoneCurse:
                case CharacterSkill.HolyLight:
                case CharacterSkill.Blessing:
                case CharacterSkill.IncreaseAgility:
                case CharacterSkill.Angelus:
                // Ranged physical / archery
                case CharacterSkill.DoubleStrafe:
                case CharacterSkill.ArrowShower:
                case CharacterSkill.ChargeArrow:
                // Ground placements
                case CharacterSkill.FireWall:
                case CharacterSkill.SafetyWall:
                case CharacterSkill.Pneuma:
                case CharacterSkill.WarpPortal:
                case CharacterSkill.Heal:
                    return 9.0f;
                default:
                    return CombatController.GetEffectiveAttackRange(); // Dynamic weapon range (melee 1.8, bow 5-15)
            }
        }

        public float GetMaxCombatCastRange()
        {
            float dynamicAttackRange = CombatController.GetEffectiveAttackRange();
            var rules = BotConfigManager.Current.SkillRules;
            if (rules == null || rules.Count == 0) return dynamicAttackRange;

            float maxRange = dynamicAttackRange;
            foreach (var rule in rules)
            {
                if (!rule.Enabled || rule.Trigger == SkillTriggerType.BuffMaintenance) continue;
                var skill = ParseSkill(rule.Skill);
                if (skill == CharacterSkill.None) continue;
                float r = GetSkillCastRange(skill);
                if (r > maxRange) maxRange = r;
            }
            return maxRange;
        }

        public Vector2Int CalculateGroundPosition(ServerControllable player, ServerControllable target, SkillPlacementType placement)
        {
            Vector2Int pPos = player.CellPosition;
            Vector2Int tPos = target != null ? target.CellPosition : pPos;

            switch (placement)
            {
                case SkillPlacementType.UnderSelf:
                    return pPos;

                case SkillPlacementType.DirectOnEnemy:
                    return tPos;

                case SkillPlacementType.BetweenSelfAndEnemy:
                    if (target == null) return pPos;
                    Vector2 dir = ((Vector2)(tPos - pPos)).normalized;
                    return pPos + new Vector2Int(Mathf.RoundToInt(dir.x * 2f), Mathf.RoundToInt(dir.y * 2f));

                case SkillPlacementType.AheadOfEnemy:
                    if (target == null) return pPos;
                    Vector2 toPlayer = ((Vector2)(pPos - tPos)).normalized;
                    return tPos + new Vector2Int(Mathf.RoundToInt(toPlayer.x * 1.5f), Mathf.RoundToInt(toPlayer.y * 1.5f));

                default:
                    return tPos;
            }
        }

        public static float ActiveCastEndTime = 0f;

        public static bool IsPlayerCasting(ServerControllable player)
        {
            if (player == null) return false;

            if (Time.timeSinceLevelLoad < ActiveCastEndTime)
            {
                return true;
            }

            // Cast duration has elapsed or no cast active:
            // Fix game client bug where ServerControllable.IsCasting is never set to false
            if (player.IsCasting)
            {
                player.IsCasting = false;
            }

            return false;
        }

        public bool ProcessBuffsAndRecovery(
            NetworkManager netManager,
            ServerControllable player,
            float now,
            NavigationController navigation = null)
        {
            if (netManager == null || player == null) return false;

            // Do not interrupt our own active cast or start new casts during casting
            if (IsPlayerCasting(player)) return true;

            if (now - globalSkillCooldown < 0.35f) return false;
            if (NpcInteractionHelper.IsInNpcInteraction()) return false;

            var rules = BotConfigManager.Current.SkillRules;
            if (rules == null || rules.Count == 0) return false;

            var playerState = PlayerState.Instance;
            if (playerState == null) return false;

            float spPercent = (float)playerState.Sp / Math.Max(1, playerState.MaxSp) * 100f;
            float hpPercent = (float)playerState.Hp / Math.Max(1, playerState.MaxHp) * 100f;

            // Check distance to party leader (if following)
            float distToLeader = -1f;
            var partyCtrl = BotEngine.Instance?.Party;
            if (BotConfigManager.Current.PartyEnabled && !BotConfigManager.Current.IsPartyLeader && partyCtrl != null)
            {
                var leader = partyCtrl.CurrentLeader;
                if (leader != null && string.Equals(leader.CurrentMap, netManager.CurrentMap, StringComparison.OrdinalIgnoreCase))
                {
                    var leaderEntity = PartyController.FindPartyMemberEntity(netManager, leader.CharacterName, leader.Profile);
                    Vector2Int leaderCellPos = leaderEntity != null ? leaderEntity.CellPosition : new Vector2Int(leader.PositionX, leader.PositionY);
                    distToLeader = Vector2.Distance(player.CellPosition, leaderCellPos);
                }
            }

            // =============================================================
            // PHASE 1: EMERGENCY HEALING / HP RECOVERY (Highest Priority)
            // =============================================================
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.Enabled) continue;

                var skill = ParseSkill(rule.Skill);
                if (skill == CharacterSkill.None) continue;

                if (rule.Trigger == SkillTriggerType.HpBelowPercent || skill == CharacterSkill.Heal)
                {
                    if (spPercent < rule.MinSpPercent) continue;
                    if (ruleLastCastTimes.TryGetValue(i, out float lastCast) && now - lastCast < rule.CooldownSeconds) continue;

                    int lvl = ResolveSkillLevel(skill, rule.Level);
                    float castRange = GetSkillCastRange(skill);
                    if (castRange < 2.0f) castRange = 9.0f;

                    int threshold = rule.HpBelowPercent > 0 ? rule.HpBelowPercent : 80;

                    // If following leader and leader is far away (> 8.0 tiles), only allow instant heals
                    if (distToLeader > 8.0f && GetSkillCastDuration(skill, lvl) > 0f) continue;

                    // A) Self Only recovery
                    if (rule.Target == SkillTargetType.Self || (rule.Target == SkillTargetType.Enemy && skill != CharacterSkill.Heal))
                    {
                        bool isSelfSitting = SurvivalController.IsSitting(player);
                        bool selfNeedsHeal = (hpPercent <= threshold) || (isSelfSitting && hpPercent < 98f);
                        if (selfNeedsHeal)
                        {
                            if (isSelfSitting)
                            {
                                netManager.ChangePlayerSitStand(false);
                                BotEngine.Instance?.Survival?.ClearRecovery();
                            }

                            if (skill == CharacterSkill.FirstAid)
                            {
                                netManager.SendSelfTargetSkillAction(skill, lvl);
                            }
                            else
                            {
                                netManager.SendSingleTargetSkillAction(netManager.PlayerId, skill, lvl);
                            }
                            ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                            ruleLastCastTimes[i] = now;
                            globalSkillCooldown = now;
                            BotEngine.Instance?.LogEvent($"[Skill] HP Recovery: Cast '{skill}' (Lv {lvl}) on Self (HP: {hpPercent:F0}% <= {threshold}%{(isSelfSitting ? ", sitting" : "")}).");
                            return true;
                        }
                    }

                    // B) Party Recovery
                    else if (rule.Target == SkillTargetType.Party)
                    {
                        int bestTargetId = -1;
                        string bestTargetName = "";
                        float bestHpPercent = float.MaxValue;
                        bool bestIsSitting = false;

                        void ConsiderTarget(int candidateId, string candidateName, float candidateHp, bool candidateSitting)
                        {
                            if (candidateId <= 0) return;
                            bool needs = (candidateHp <= threshold) || (candidateSitting && candidateHp < 98f);
                            if (!needs) return;

                            if (bestTargetId <= 0)
                            {
                                bestTargetId = candidateId;
                                bestTargetName = candidateName;
                                bestHpPercent = candidateHp;
                                bestIsSitting = candidateSitting;
                                return;
                            }

                            bool candidateCritical = candidateHp <= 25f;
                            bool bestCritical = bestHpPercent <= 25f;

                            if (candidateCritical && !bestCritical)
                            {
                                bestTargetId = candidateId;
                                bestTargetName = candidateName;
                                bestHpPercent = candidateHp;
                                bestIsSitting = candidateSitting;
                                return;
                            }
                            if (!candidateCritical && bestCritical) return;

                            if (candidateHp < bestHpPercent - 0.5f)
                            {
                                bestTargetId = candidateId;
                                bestTargetName = candidateName;
                                bestHpPercent = candidateHp;
                                bestIsSitting = candidateSitting;
                                return;
                            }

                            if (candidateSitting && !bestIsSitting && Math.Abs(candidateHp - bestHpPercent) <= 5.0f)
                            {
                                bestTargetId = candidateId;
                                bestTargetName = candidateName;
                                bestHpPercent = candidateHp;
                                bestIsSitting = candidateSitting;
                                return;
                            }
                        }

                        // 1. Check Self
                        bool isSelfSitting = SurvivalController.IsSitting(player);
                        ConsiderTarget(netManager.PlayerId, player?.Name ?? "Self", hpPercent, isSelfSitting);

                        var processedEntityIds = new HashSet<int> { netManager.PlayerId };

                        // 2. Check Party Leader & Discovered Fleet Members
                        if (partyCtrl != null)
                        {
                            partyCtrl.UpdateLeaderDiscovery(now);

                            var leader = partyCtrl.CurrentLeader;
                            if (leader != null && string.Equals(leader.CurrentMap, netManager.CurrentMap, StringComparison.OrdinalIgnoreCase))
                            {
                                var leaderEntity = PartyController.FindPartyMemberEntity(netManager, leader.CharacterName, leader.Profile);
                                if (leaderEntity != null && leaderEntity.IsCharacterAlive)
                                {
                                    processedEntityIds.Add(leaderEntity.Id);
                                    float d = Vector2.Distance(player.CellPosition, leaderEntity.CellPosition);
                                    if (d <= castRange)
                                    {
                                        float lEntityPct = (leaderEntity.MaxHp > 0 && leaderEntity.Hp >= 0) ? ((float)leaderEntity.Hp / leaderEntity.MaxHp * 100f) : 100f;
                                        float lIpcPct = (leader.MaxHp > 0 && leader.Hp >= 0) ? ((float)leader.Hp / leader.MaxHp * 100f) : 100f;
                                        float lHpPct = Math.Min(lEntityPct, lIpcPct);
                                        bool isLeaderSitting = SurvivalController.IsSitting(leaderEntity) || string.Equals(leader.BotState, "Resting", StringComparison.OrdinalIgnoreCase);

                                        ConsiderTarget(leaderEntity.Id, leader.CharacterName, lHpPct, isLeaderSitting);
                                    }
                                }
                            }

                            foreach (var member in partyCtrl.DiscoveredMembers)
                            {
                                if (member == null || member.IsLeader) continue;
                                if (!string.Equals(member.CurrentMap, netManager.CurrentMap, StringComparison.OrdinalIgnoreCase)) continue;

                                var memberEntity = PartyController.FindPartyMemberEntity(netManager, member.CharacterName, member.Profile);
                                if (memberEntity != null && memberEntity.IsCharacterAlive)
                                {
                                    if (!processedEntityIds.Add(memberEntity.Id)) continue;
                                    float d = Vector2.Distance(player.CellPosition, memberEntity.CellPosition);
                                    if (d <= castRange)
                                    {
                                        float mEntityPct = (memberEntity.MaxHp > 0 && memberEntity.Hp >= 0) ? ((float)memberEntity.Hp / memberEntity.MaxHp * 100f) : 100f;
                                        float mIpcPct = (member.MaxHp > 0 && member.Hp >= 0) ? ((float)member.Hp / member.MaxHp * 100f) : 100f;
                                        float mHpPct = Math.Min(mEntityPct, mIpcPct);
                                        bool isMemberSitting = SurvivalController.IsSitting(memberEntity) || string.Equals(member.BotState, "Resting", StringComparison.OrdinalIgnoreCase);

                                        ConsiderTarget(memberEntity.Id, member.CharacterName, mHpPct, isMemberSitting);
                                    }
                                }
                            }
                        }

                        // 3. Check In-Game Party Members
                        if (playerState.IsInParty && playerState.PartyMembers != null)
                        {
                            foreach (var kvp in playerState.PartyMembers)
                            {
                                var pm = kvp.Value;
                                if (pm == null || pm.EntityId <= 0 || pm.EntityId == netManager.PlayerId) continue;
                                if (pm.Controllable == null || !pm.Controllable.IsCharacterAlive) continue;
                                if (!processedEntityIds.Add(pm.EntityId)) continue;

                                float d = Vector2.Distance(player.CellPosition, pm.Controllable.CellPosition);
                                if (d <= castRange)
                                {
                                    float pmEntityPct = (pm.Controllable.MaxHp > 0 && pm.Controllable.Hp >= 0) ? ((float)pm.Controllable.Hp / pm.Controllable.MaxHp * 100f) : 100f;
                                    float pmMemberPct = (pm.MaxHp > 0 && pm.Hp >= 0) ? ((float)pm.Hp / pm.MaxHp * 100f) : 100f;
                                    float pmHpPct = Math.Min(pmEntityPct, pmMemberPct);
                                    bool isPmSitting = SurvivalController.IsSitting(pm.Controllable);

                                    ConsiderTarget(pm.EntityId, pm.PlayerName, pmHpPct, isPmSitting);
                                }
                            }
                        }

                        if (bestTargetId > 0)
                        {
                            if (SurvivalController.IsSitting(player))
                            {
                                netManager.ChangePlayerSitStand(false);
                                BotEngine.Instance?.Survival?.ClearRecovery();
                            }

                            if (bestTargetId == netManager.PlayerId && skill == CharacterSkill.FirstAid)
                            {
                                netManager.SendSelfTargetSkillAction(skill, lvl);
                            }
                            else
                            {
                                netManager.SendSingleTargetSkillAction(bestTargetId, skill, lvl);
                            }
                            ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                            ruleLastCastTimes[i] = now;
                            globalSkillCooldown = now;
                            string sitInfo = bestIsSitting ? " [Resting/Sitting]" : "";
                            BotEngine.Instance?.LogEvent($"[Party Recovery] Cast '{skill}' (Lv {lvl}) on '{bestTargetName}' (HP: {bestHpPercent:F0}% <= {threshold}%{sitInfo}).");
                            return true;
                        }
                    }
                }
            }

            // =============================================================
            // PHASE 1.5: RUWACH ANTI-HIDDEN DEFENSE
            // =============================================================
            var nav = navigation ?? BotEngine.Instance?.Navigation;
            if (nav != null && ProcessRuwachDefense(netManager, player, nav, now))
            {
                return true;
            }

            // =============================================================
            // PHASE 2: BUFF MAINTENANCE & PARTY BUFFS (Only when healthy)
            // =============================================================
            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.Enabled) continue;

                var skill = ParseSkill(rule.Skill);
                if (skill == CharacterSkill.None || skill == CharacterSkill.Heal) continue;

                // 2A. BUFF MAINTENANCE TRIGGER
                if (rule.Trigger == SkillTriggerType.BuffMaintenance)
                {
                    if (spPercent < rule.MinSpPercent) continue;
                    if (ruleLastCastTimes.TryGetValue(i, out float lastCast) && now - lastCast < rule.CooldownSeconds) continue;

                    int lvl = ResolveSkillLevel(skill, rule.Level);

                    // If following leader and leader is far away (> 8.0 tiles), skip non-instant casts
                    if (distToLeader > 8.0f && GetSkillCastDuration(skill, lvl) > 0f) continue;

                    if (rule.Target == SkillTargetType.Self)
                    {
                        if (HasBuffActive(skill, player)) continue;

                        netManager.SendSelfTargetSkillAction(skill, lvl);
                        ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                        ruleLastCastTimes[i] = now;
                        RecordTargetBuffCast(netManager.PlayerId, player?.Name, skill, now);
                        globalSkillCooldown = now;
                        BotEngine.Instance?.LogEvent($"[Skill] Cast buff '{skill}' (Lv {lvl}) on Self.");
                        return true;
                    }
                    else if (rule.Target == SkillTargetType.Party && playerState.IsInParty && playerState.PartyMembers != null)
                    {
                        foreach (var kvp in playerState.PartyMembers)
                        {
                            var member = kvp.Value;
                            if (member == null || member.EntityId <= 0 || member.EntityId == netManager.PlayerId) continue;
                            if (member.Controllable == null || !member.Controllable.IsCharacterAlive || member.Hp <= 0) continue;

                            if (HasTargetBuffActive(skill, member.EntityId, member.PlayerName, member.Controllable, now)) continue;

                            float dist = Vector2.Distance(player.CellPosition, member.Controllable.CellPosition);
                            if (dist <= 9.0f)
                            {
                                netManager.SendSingleTargetSkillAction(member.EntityId, skill, lvl);
                                ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                                ruleLastCastTimes[i] = now;
                                RecordTargetBuffCast(member.EntityId, member.PlayerName, skill, now);
                                globalSkillCooldown = now;
                                BotEngine.Instance?.LogEvent($"[Skill] Cast buff '{skill}' (Lv {lvl}) on party member '{member.PlayerName}'.");
                                return true;
                            }
                        }
                    }
                }

                // 2B. PARTY BUFF TRIGGER (Maintained across self, leader, and party members)
                else if (rule.Trigger == SkillTriggerType.PartyBuff)
                {
                    if (spPercent < rule.MinSpPercent) continue;
                    if (ruleLastCastTimes.TryGetValue(i, out float lastCast) && now - lastCast < rule.CooldownSeconds) continue;

                    int lvl = ResolveSkillLevel(skill, rule.Level);
                    float castRange = GetSkillCastRange(skill);
                    if (castRange < 2.0f) castRange = 9.0f;

                    // If following leader and leader is far away (> 8.0 tiles), skip non-instant casts
                    if (distToLeader > 8.0f && GetSkillCastDuration(skill, lvl) > 0f) continue;

                    // SPECIAL HANDLING: Self-Target AoE Party Buffs (e.g. Angelus)
                    if (IsSelfTargetAoEPartyBuff(skill))
                    {
                        bool needsAoEBuff = false;
                        string aoeReason = "";

                        // Check self
                        if (!HasBuffActive(skill, player))
                        {
                            needsAoEBuff = true;
                            aoeReason = "Self missing buff";
                        }

                        // Check party leader
                        if (!needsAoEBuff && partyCtrl != null)
                        {
                            var leader = partyCtrl.CurrentLeader;
                            if (leader != null && string.Equals(leader.CurrentMap, netManager.CurrentMap, StringComparison.OrdinalIgnoreCase))
                            {
                                var leaderEntity = PartyController.FindPartyMemberEntity(netManager, leader.CharacterName, leader.Profile);
                                if (leaderEntity != null && leaderEntity.IsCharacterAlive && leaderEntity.Hp > 0)
                                {
                                    float dist = Vector2.Distance(player.CellPosition, leaderEntity.CellPosition);
                                    if (dist <= 12.0f && !HasTargetBuffActive(skill, leaderEntity.Id, leader.CharacterName, leaderEntity, now))
                                    {
                                        needsAoEBuff = true;
                                        aoeReason = $"Leader '{leader.CharacterName}' missing buff";
                                    }
                                }
                            }
                        }

                        // Check other party members
                        if (!needsAoEBuff && playerState.IsInParty && playerState.PartyMembers != null)
                        {
                            foreach (var kvp in playerState.PartyMembers)
                            {
                                var member = kvp.Value;
                                if (member == null || member.EntityId <= 0 || member.EntityId == netManager.PlayerId) continue;
                                if (member.Controllable == null || !member.Controllable.IsCharacterAlive || member.Hp <= 0) continue;

                                float dist = Vector2.Distance(player.CellPosition, member.Controllable.CellPosition);
                                if (dist <= 12.0f && !HasTargetBuffActive(skill, member.EntityId, member.PlayerName, member.Controllable, now))
                                {
                                    needsAoEBuff = true;
                                    aoeReason = $"Party member '{member.PlayerName}' missing buff";
                                    break;
                                }
                            }
                        }

                        if (needsAoEBuff)
                        {
                            netManager.SendSelfTargetSkillAction(skill, lvl);
                            ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                            ruleLastCastTimes[i] = now;
                            RecordTargetBuffCast(netManager.PlayerId, player?.Name, skill, now);
                            if (partyCtrl?.CurrentLeader != null)
                            {
                                var lEntity = PartyController.FindPartyMemberEntity(netManager, partyCtrl.CurrentLeader.CharacterName, partyCtrl.CurrentLeader.Profile);
                                if (lEntity != null) RecordTargetBuffCast(lEntity.Id, partyCtrl.CurrentLeader.CharacterName, skill, now);
                            }
                            globalSkillCooldown = now;
                            BotEngine.Instance?.LogEvent($"[PartyBuff] AoE Cast '{skill}' (Lv {lvl}) on Self ({aoeReason}).");
                            return true;
                        }

                        continue;
                    }

                    // STANDARD SINGLE-TARGET BUFFS (Blessing, Increase Agility, etc.)
                    // Priority 1: Self buff
                    if (!HasBuffActive(skill, player))
                    {
                        netManager.SendSingleTargetSkillAction(netManager.PlayerId, skill, lvl);
                        ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                        ruleLastCastTimes[i] = now;
                        RecordTargetBuffCast(netManager.PlayerId, player?.Name, skill, now);
                        globalSkillCooldown = now;
                        BotEngine.Instance?.LogEvent($"[PartyBuff] Cast '{skill}' (Lv {lvl}) on Self.");
                        return true;
                    }

                    var processedEntityIds = new HashSet<int> { netManager.PlayerId };
                    var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (player != null && !string.IsNullOrEmpty(player.Name))
                    {
                        processedNames.Add(player.Name);
                    }

                    // Priority 2: Party Leader first
                    if (partyCtrl != null)
                    {
                        partyCtrl.UpdateLeaderDiscovery(now);

                        var leader = partyCtrl.CurrentLeader;
                        if (leader != null && string.Equals(leader.CurrentMap, netManager.CurrentMap, StringComparison.OrdinalIgnoreCase))
                        {
                            processedNames.Add(leader.CharacterName);
                            var leaderEntity = PartyController.FindPartyMemberEntity(netManager, leader.CharacterName, leader.Profile);
                            if (leaderEntity != null)
                            {
                                processedEntityIds.Add(leaderEntity.Id);
                                if (leaderEntity.IsCharacterAlive && leaderEntity.Hp > 0)
                                {
                                    bool leaderHasBuff = HasTargetBuffActive(skill, leaderEntity.Id, leader.CharacterName, leaderEntity, now);
                                    if (!leaderHasBuff)
                                    {
                                        float dist = Vector2.Distance(player.CellPosition, leaderEntity.CellPosition);
                                        if (dist <= castRange)
                                        {
                                            netManager.SendSingleTargetSkillAction(leaderEntity.Id, skill, lvl);
                                            ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                                            ruleLastCastTimes[i] = now;
                                            RecordTargetBuffCast(leaderEntity.Id, leader.CharacterName, skill, now);
                                            globalSkillCooldown = now;
                                            BotEngine.Instance?.LogEvent($"[PartyBuff] Cast '{skill}' (Lv {lvl}) on Party Leader '{leader.CharacterName}'.");
                                            return true;
                                        }
                                    }
                                }
                            }
                        }

                        // Priority 3: Other discovered fleet members
                        foreach (var member in partyCtrl.DiscoveredMembers)
                        {
                            if (member == null || member.IsLeader) continue;
                            processedNames.Add(member.CharacterName);

                            if (!string.Equals(member.CurrentMap, netManager.CurrentMap, StringComparison.OrdinalIgnoreCase)) continue;

                            var memberEntity = PartyController.FindPartyMemberEntity(netManager, member.CharacterName, member.Profile);
                            if (memberEntity != null)
                            {
                                processedEntityIds.Add(memberEntity.Id);
                                if (memberEntity.IsCharacterAlive && memberEntity.Hp > 0)
                                {
                                    bool memberHasBuff = HasTargetBuffActive(skill, memberEntity.Id, member.CharacterName, memberEntity, now);
                                    if (!memberHasBuff)
                                    {
                                        float dist = Vector2.Distance(player.CellPosition, memberEntity.CellPosition);
                                        if (dist <= castRange)
                                        {
                                            netManager.SendSingleTargetSkillAction(memberEntity.Id, skill, lvl);
                                            ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                                            ruleLastCastTimes[i] = now;
                                            RecordTargetBuffCast(memberEntity.Id, member.CharacterName, skill, now);
                                            globalSkillCooldown = now;
                                            BotEngine.Instance?.LogEvent($"[PartyBuff] Cast '{skill}' (Lv {lvl}) on Party Member '{member.CharacterName}'.");
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Priority 4: Official In-Game Party Members
                    if (playerState.IsInParty && playerState.PartyMembers != null)
                    {
                        foreach (var kvp in playerState.PartyMembers)
                        {
                            var member = kvp.Value;
                            if (member == null || member.EntityId <= 0) continue;
                            if (processedEntityIds.Contains(member.EntityId)) continue;
                            if (!string.IsNullOrEmpty(member.PlayerName) && processedNames.Contains(member.PlayerName)) continue;
                            if (member.Controllable == null || !member.Controllable.IsCharacterAlive || member.Hp <= 0) continue;

                            bool memberHasBuff = HasTargetBuffActive(skill, member.EntityId, member.PlayerName, member.Controllable, now);
                            if (!memberHasBuff)
                            {
                                float dist = Vector2.Distance(player.CellPosition, member.Controllable.CellPosition);
                                if (dist <= castRange)
                                {
                                    netManager.SendSingleTargetSkillAction(member.EntityId, skill, lvl);
                                    ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                                    ruleLastCastTimes[i] = now;
                                    RecordTargetBuffCast(member.EntityId, member.PlayerName, skill, now);
                                    globalSkillCooldown = now;
                                    BotEngine.Instance?.LogEvent($"[PartyBuff] Cast '{skill}' (Lv {lvl}) on In-Game Party Member '{member.PlayerName}'.");
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        public bool TryExecuteCombatSkill(
            NetworkManager netManager,
            ServerControllable player,
            ServerControllable target,
            float now,
            ref BotState state)
        {
            if (player == null || target == null || IsPlayerCasting(player)) return false;
            if (now - globalSkillCooldown < 0.35f) return false;

            if (lastTargetId != target.Id)
            {
                lastTargetId = target.Id;
                // Keep opener casts fresh for new targets
                if (openerCasts.Count > 100) openerCasts.Clear();
            }

            var rules = BotConfigManager.Current.SkillRules;
            if (rules == null || rules.Count == 0) return false;

            var playerState = PlayerState.Instance;
            if (playerState == null) return false;

            float spPercent = (float)playerState.Sp / Math.Max(1, playerState.MaxSp) * 100f;
            float distToTarget = Vector2.Distance(player.CellPosition, target.CellPosition);

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.Enabled || rule.Trigger == SkillTriggerType.BuffMaintenance || rule.Trigger == SkillTriggerType.PartyBuff || rule.Trigger == SkillTriggerType.HpBelowPercent || rule.Skill.Trim().Equals("Heal", StringComparison.OrdinalIgnoreCase)) continue;

                var skill = ParseSkill(rule.Skill);
                if (skill == CharacterSkill.None) continue;

                // Check SP reserve
                if (spPercent < rule.MinSpPercent) continue;

                // Check rule cooldown
                if (ruleLastCastTimes.TryGetValue(i, out float lastCast) && now - lastCast < rule.CooldownSeconds) continue;

                // Check monster species whitelist filter
                if (rule.TargetMonsters != null && rule.TargetMonsters.Count > 0 && !rule.TargetMonsters.Contains(target.Name))
                    continue;

                // Check min target HP threshold
                if (rule.MinTargetHp > 0 && target.Hp < rule.MinTargetHp) continue;

                // Check Opener trigger: only once per target
                if (rule.Trigger == SkillTriggerType.Opener && openerCasts.Contains((target.Id, skill)))
                    continue;

                // Check MinEnemiesInRange condition (applies whenever MinEnemiesInRange > 1 or Trigger is MobCluster)
                if (rule.MinEnemiesInRange > 1 || rule.Trigger == SkillTriggerType.MobCluster)
                {
                    int minReq = Math.Max(2, rule.MinEnemiesInRange);
                    int nearbyPlayer = CountNearbyEnemies(netManager, player.CellPosition, 3.5f, target.Id);
                    int nearbyTarget = CountNearbyEnemies(netManager, target.CellPosition, 3.5f, target.Id);
                    int enemyCount = Math.Max(nearbyPlayer, nearbyTarget);
                    if (enemyCount < minReq)
                    {
                        continue;
                    }
                }

                // Check range
                float castRange = GetSkillCastRange(skill);
                if (distToTarget > castRange)
                {
                    // Target is too far to cast this skill right now
                    continue;
                }

                int lvl = ResolveSkillLevel(skill, rule.Level);

                // DISPATCH SKILL BASED ON TARGET TYPE
                if (rule.Target == SkillTargetType.Enemy)
                {
                    netManager.SendSingleTargetSkillAction(target.Id, skill, lvl);
                    ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                    ruleLastCastTimes[i] = now;
                    globalSkillCooldown = now;
                    if (rule.Trigger == SkillTriggerType.Opener)
                        openerCasts.Add((target.Id, skill));

                    state = BotState.AttackingTarget;
                    BotEngine.Instance?.LogEvent($"[Combat Skill] Cast '{skill}' (Lv {lvl}) on {target.Name} (dist: {distToTarget:F1}).");
                    return true;
                }
                else if (rule.Target == SkillTargetType.Ground)
                {
                    Vector2Int groundPos = CalculateGroundPosition(player, target, rule.Placement);
                    netManager.SendGroundTargetSkillAction(groundPos, skill, lvl);
                    ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                    ruleLastCastTimes[i] = now;
                    globalSkillCooldown = now;
                    if (rule.Trigger == SkillTriggerType.Opener)
                        openerCasts.Add((target.Id, skill));

                    state = BotState.AttackingTarget;
                    BotEngine.Instance?.LogEvent($"[Combat Skill] Ground-Cast '{skill}' (Lv {lvl}) at ({groundPos.x}, {groundPos.y}) [{rule.Placement}].");
                    return true;
                }
                else if (rule.Target == SkillTargetType.Self)
                {
                    netManager.SendSelfTargetSkillAction(skill, lvl);
                    ActiveCastEndTime = Time.timeSinceLevelLoad + GetSkillCastDuration(skill, lvl);
                    ruleLastCastTimes[i] = now;
                    globalSkillCooldown = now;
                    if (rule.Trigger == SkillTriggerType.Opener)
                        openerCasts.Add((target.Id, skill));

                    state = BotState.AttackingTarget;
                    BotEngine.Instance?.LogEvent($"[Combat Skill] Self-Cast '{skill}' (Lv {lvl}) in combat.");
                    return true;
                }
            }

            return false;
        }

        private int CountNearbyEnemies(NetworkManager netManager, Vector2Int centerPos, float radius, int currentTargetId)
        {
            if (netManager == null || netManager.EntityList == null) return 0;
            int count = 0;
            var targeting = BotEngine.Instance?.Targeting;

            foreach (var kvp in netManager.EntityList)
            {
                var entity = kvp.Value;
                if (entity == null || entity.Id == netManager.PlayerId) continue;
                if (entity.CharacterType == CharacterType.Monster && !entity.IsAlly && entity.IsCharacterAlive && entity.Hp > 0)
                {
                    // Only count monsters that are currently attacking the player, are an aggressive species, or is our active target
                    bool isAttacking = targeting != null && targeting.IsAttackingPlayer(entity.Id);
                    bool isAggressive = MonsterDatabase.Instance.IsAggressive(entity.Name);
                    bool isCurrentTarget = entity.Id == currentTargetId;

                    if (!isCurrentTarget && !isAttacking && !isAggressive)
                        continue;

                    if (Vector2.Distance(centerPos, entity.CellPosition) <= radius)
                    {
                        if (MapNavMesh.Instance.IsReachable(centerPos, entity.CellPosition))
                            count++;
                    }
                }
            }
            return count;
        }
    }
}
