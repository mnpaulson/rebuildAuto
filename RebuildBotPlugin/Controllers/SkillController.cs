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

        public void Clear()
        {
            ruleLastCastTimes.Clear();
            openerCasts.Clear();
            globalSkillCooldown = 0f;
            lastTargetId = -1;
        }

        public CharacterSkill ParseSkill(string skillName)
        {
            if (string.IsNullOrWhiteSpace(skillName)) return CharacterSkill.None;
            string clean = skillName.Trim().Replace(" ", "").Replace("_", "").Replace("-", "").Replace("'", "");
            if (Enum.TryParse<CharacterSkill>(clean, true, out var skill))
                return skill;
            if (string.Equals(clean, "IncreaseAgi", StringComparison.OrdinalIgnoreCase))
                return CharacterSkill.IncreaseAgility;
            if (string.Equals(clean, "OwlsEye", StringComparison.OrdinalIgnoreCase))
                return CharacterSkill.OwlEye;
            if (string.Equals(clean, "VulturesEye", StringComparison.OrdinalIgnoreCase))
                return CharacterSkill.VultureEye;
            return CharacterSkill.None;
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

        public bool HasBuffActive(CharacterSkill skill)
        {
            if (!SkillToBuffNameMap.TryGetValue(skill, out string buffName))
                return false;

            if (Enum.TryParse<CharacterStatusEffect>(buffName, true, out var effect))
            {
                return HasStatusEffect(effect);
            }
            return false;
        }

        public bool HasStatusEffect(CharacterStatusEffect effect)
        {
            if (StatusEffectPanel.Instance != null && StatusEffectPanel.Instance.StatusEffectLookup != null)
            {
                return StatusEffectPanel.Instance.StatusEffectLookup.ContainsKey(effect);
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

        public bool ProcessBuffsAndRecovery(NetworkManager netManager, ServerControllable player, float now)
        {
            if (player == null || player.IsCasting) return false;
            if (now - globalSkillCooldown < 0.35f) return false;

            var rules = BotConfigManager.Current.SkillRules;
            if (rules == null || rules.Count == 0) return false;

            var playerState = PlayerState.Instance;
            if (playerState == null) return false;

            float spPercent = (float)playerState.Sp / Math.Max(1, playerState.MaxSp) * 100f;
            float hpPercent = (float)playerState.Hp / Math.Max(1, playerState.MaxHp) * 100f;

            for (int i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                if (!rule.Enabled) continue;

                var skill = ParseSkill(rule.Skill);
                if (skill == CharacterSkill.None) continue;

                // 1. BUFF MAINTENANCE TRIGGER
                if (rule.Trigger == SkillTriggerType.BuffMaintenance)
                {
                    if (spPercent < rule.MinSpPercent) continue;
                    if (ruleLastCastTimes.TryGetValue(i, out float lastCast) && now - lastCast < rule.CooldownSeconds) continue;

                    if (HasBuffActive(skill)) continue; // Buff is already active!

                    int lvl = ResolveSkillLevel(skill, rule.Level);

                    if (rule.Target == SkillTargetType.Self)
                    {
                        netManager.SendSelfTargetSkillAction(skill, lvl);
                        ruleLastCastTimes[i] = now;
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

                            float dist = Vector2.Distance(player.CellPosition, member.Controllable.CellPosition);
                            if (dist <= 9.0f)
                            {
                                netManager.SendSingleTargetSkillAction(member.EntityId, skill, lvl);
                                ruleLastCastTimes[i] = now;
                                globalSkillCooldown = now;
                                BotEngine.Instance?.LogEvent($"[Skill] Cast buff '{skill}' (Lv {lvl}) on party member '{member.PlayerName}'.");
                                return true;
                            }
                        }
                    }
                }

                // 2. HP RECOVERY TRIGGER (Emergency Heal / First Aid)
                else if (rule.Trigger == SkillTriggerType.HpBelowPercent)
                {
                    if (spPercent < rule.MinSpPercent) continue;
                    if (ruleLastCastTimes.TryGetValue(i, out float lastCast) && now - lastCast < rule.CooldownSeconds) continue;

                    int lvl = ResolveSkillLevel(skill, rule.Level);

                    // Self recovery
                    if ((rule.Target == SkillTargetType.Self || rule.Target == SkillTargetType.Enemy) && hpPercent <= rule.HpBelowPercent)
                    {
                        if (skill == CharacterSkill.FirstAid)
                        {
                            netManager.SendSelfTargetSkillAction(skill, lvl);
                        }
                        else
                        {
                            netManager.SendSingleTargetSkillAction(netManager.PlayerId, skill, lvl);
                        }
                        ruleLastCastTimes[i] = now;
                        globalSkillCooldown = now;
                        BotEngine.Instance?.LogEvent($"[Skill] HP Recovery: Cast '{skill}' (Lv {lvl}) on Self (HP: {hpPercent:F0}% <= {rule.HpBelowPercent}%).");
                        return true;
                    }

                    // Party recovery
                    if (rule.Target == SkillTargetType.Party && playerState.IsInParty && playerState.PartyMembers != null)
                    {
                        foreach (var kvp in playerState.PartyMembers)
                        {
                            var member = kvp.Value;
                            if (member == null || member.EntityId <= 0) continue;
                            if (member.Controllable == null || !member.Controllable.IsCharacterAlive || member.Hp <= 0) continue;

                            float memberHpPercent = (float)member.Hp / Math.Max(1, member.MaxHp) * 100f;
                            if (memberHpPercent <= rule.HpBelowPercent)
                            {
                                float dist = Vector2.Distance(player.CellPosition, member.Controllable.CellPosition);
                                if (dist <= 9.0f)
                                {
                                    netManager.SendSingleTargetSkillAction(member.EntityId, skill, lvl);
                                    ruleLastCastTimes[i] = now;
                                    globalSkillCooldown = now;
                                    BotEngine.Instance?.LogEvent($"[Skill] Party Recovery: Cast '{skill}' (Lv {lvl}) on '{member.PlayerName}' (HP: {memberHpPercent:F0}% <= {rule.HpBelowPercent}%).");
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
            if (player == null || target == null || player.IsCasting) return false;
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
                if (!rule.Enabled || rule.Trigger == SkillTriggerType.BuffMaintenance) continue;

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
