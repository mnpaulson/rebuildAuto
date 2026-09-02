using Assets.Scripts;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public class CombatController
    {
        public int CurrentLockedTargetId { get; set; } = -1;
        public string CurrentTargetName { get; set; } = "None";
        public int CurrentTargetHp { get; set; } = 0;
        public int CurrentTargetMaxHp { get; set; } = 0;
        public int KillCount { get; set; } = 0;

        private int lastTargetId = -1;
        private int lastTargetHp = -1;
        private string lastTargetName = "Target";
        private float lastAttackTime = 0f;
        private float lastApproachTime = 0f;
        private float nextApproachDelay = 0.28f;
        private float targetApproachStartTime = 0f;
        private float targetApproachProgressTime = 0f;
        private float lastDistanceToTarget = float.MaxValue;
        private float lastAttackLogTime = 0f;
        private float lastApproachLogTime = 0f;
        private float lastArrowCheckTime = 0f;

        public void Clear()
        {
            CurrentLockedTargetId = -1;
            lastTargetId = -1;
            CurrentTargetName = "None";
            CurrentTargetHp = 0;
            CurrentTargetMaxHp = 0;
            lastAttackLogTime = 0f;
            lastApproachLogTime = 0f;
            lastArrowCheckTime = 0f;
        }

        public void OnTargetDefeated()
        {
            if (CurrentLockedTargetId != -1)
            {
                KillCount++;
                BotEngine.Instance?.LogEvent($"Defeated target {lastTargetName}! Total Kills: {KillCount}");
                CurrentLockedTargetId = -1;
                lastTargetId = -1;
                CurrentTargetName = "None";
                CurrentTargetHp = 0;
                CurrentTargetMaxHp = 0;
            }
        }

        public ServerControllable GetLockedTarget(Vector2Int playerPos)
        {
            var netManager = NetworkManager.Instance;
            if (netManager == null || netManager.EntityList == null || CurrentLockedTargetId == -1) return null;

            if (netManager.EntityList.TryGetValue(CurrentLockedTargetId, out var lockedEntity) &&
                lockedEntity != null && lockedEntity.IsCharacterAlive && lockedEntity.Hp > 0 && !lockedEntity.IsAlly)
            {
                float dist = Vector2.Distance(playerPos, lockedEntity.CellPosition);
                if (dist <= BotConfigManager.Current.SearchRadius * 1.5f)
                {
                    return lockedEntity;
                }
            }

            return null;
        }

        public void ExecuteCombatAction(
            NetworkManager netManager,
            ServerControllable player,
            ServerControllable target,
            float now,
            NavigationController navigation,
            TargetingController targeting,
            ref BotState currentState)
        {
            if (BotEngine.Instance != null && BotEngine.Instance.Survival != null && BotEngine.Instance.Survival.IsPlayerSitting(player))
            {
                netManager.ChangePlayerSitStand(false);
            }

            // Keep track of engaged target identity
            CurrentTargetName = target.Name;
            CurrentTargetHp = target.Hp;
            CurrentTargetMaxHp = target.MaxHp;
            lastTargetName = target.Name;

            // Track kill count when monster HP drops to 0 or becomes not alive
            if (lastTargetId == target.Id && (target.Hp <= 0 || !target.IsCharacterAlive))
            {
                KillCount++;
                BotEngine.Instance?.LogEvent($"Defeated target monster {target.Name}! Total Kills: {KillCount}");
                CurrentLockedTargetId = -1;
                lastTargetId = -1;
                return;
            }

            float dist = Vector2.Distance(player.CellPosition, target.CellPosition);

            if (lastTargetId != target.Id)
            {
                targetApproachStartTime = now;
                targetApproachProgressTime = now;
                lastDistanceToTarget = dist;
                navigation.ResetWander(); // Halt wander pre-click step immediately

                // Auto-equip best arrow for target if character is Archer class
                if (BotConfigManager.Current.AutoEquipBestArrow)
                {
                    Services.ArrowHelper.EquipBestArrowForTarget(netManager, target);
                }

                // Immediately command server to attack/pursue target
                netManager.SendAttack(target.Id);
                lastAttackTime = now;

                BotEngine.Instance?.LogEvent($"[Combat] Engaged {target.Name} (ID: {target.Id}, dist: {dist:F1} tiles). Initiating attack pursuit!");
            }
            else if (BotConfigManager.Current.AutoEquipBestArrow && now - lastArrowCheckTime > 3.0f)
            {
                // In-combat watchdog: only re-check inventory if currently out of equipped arrows
                lastArrowCheckTime = now;
                var state = PlayerState.Instance;
                if (state != null && state.AmmoId == 0)
                {
                    Services.ArrowHelper.EquipBestArrowForTarget(netManager, target);
                }
            }
            lastTargetId = target.Id;
            lastTargetHp = target.Hp;

            var skills = BotEngine.Instance?.Skills;

            // 1. SKILL WEAVING ATTEMPT: Try executing offensive skill rules
            if (skills != null && skills.TryExecuteCombatSkill(netManager, player, target, now, ref currentState))
            {
                targetApproachStartTime = now;
                targetApproachProgressTime = now;
                lastDistanceToTarget = dist;
                lastAttackTime = now;
                return;
            }

            // If player is actively channeling/casting a spell, wait for completion
            if (player.IsCasting)
            {
                currentState = BotState.AttackingTarget;
                return;
            }

            // Determine dynamic attack range based on server stats, weapon class, and skills
            float combatRange = GetEffectiveAttackRange();
            bool hasLos = Pathfinder.HasLineOfSight(player.CellPosition, target.CellPosition);

            if (dist <= combatRange && hasLos)
            {
                targetApproachStartTime = now; // Reset timeout while actively attacking
                targetApproachProgressTime = now;
                lastDistanceToTarget = dist;

                if (now - lastAttackTime >= BotConfigManager.Current.AttackCooldownSeconds)
                {
                    netManager.SendAttack(target.Id);
                    lastAttackTime = now;
                    currentState = BotState.AttackingTarget;

                    if (now - lastAttackLogTime >= 1.5f || lastTargetHp != target.Hp)
                    {
                        BotEngine.Instance?.LogEvent($"[Combat] Attacking {target.Name} (ID: {target.Id}, dist: {dist:F1} <= {combatRange:F1}) [HP: {target.Hp}/{target.MaxHp}].");
                        lastAttackLogTime = now;
                    }
                }
            }
            else
            {
                // If character made measurable progress towards target, refresh watchdog timer
                if (dist < lastDistanceToTarget - 0.5f)
                {
                    targetApproachProgressTime = now;
                    lastDistanceToTarget = dist;
                }

                // Timeout watchdog: abandon ONLY if making NO progress closing distance for > 5.0s, or total approach exceeds 15.0s
                if ((now - targetApproachProgressTime > 5.0f) || (now - targetApproachStartTime > 15.0f))
                {
                    targeting.MarkUnreachable(target.Id, 10.0f);
                    CurrentLockedTargetId = -1;
                    lastTargetId = -1;
                    BotEngine.Instance?.LogEvent($"[Combat] Abandoning unreachable target {target.Name} (ID: {target.Id}) (no progress for {(now - targetApproachProgressTime):F1}s). Blacklisting for 10s.");
                    return;
                }

                currentState = BotState.ApproachingTarget;

                // Humanized approach throttle with randomized reaction cadence (180ms - 320ms)
                if (now - lastApproachTime >= nextApproachDelay)
                {
                    // Move to the optimal attack tile closest to us
                    Vector2Int attackTile = navigation.GetAttackPosition(player.CellPosition, target.CellPosition, combatRange);

                    if (attackTile == Vector2Int.zero ||
                        (BotConfigManager.Current.AvoidPortalsWhileWandering && WorldGraph.Instance.IsNearPortal(netManager.CurrentMap, attackTile, BotConfigManager.Current.PortalSafetyRadius)))
                    {
                        targeting.MarkUnreachable(target.Id, 15.0f);
                        CurrentLockedTargetId = -1;
                        lastTargetId = -1;
                        BotEngine.Instance?.LogEvent($"[Combat] Target {target.Name} (ID: {target.Id}) is inside portal safety zone ({BotConfigManager.Current.PortalSafetyRadius:F0} tiles). Abandoning target to avoid accidental warp.");
                        return;
                    }

                    navigation.NavigateTowards(player.CellPosition, attackTile, avoidPortals: BotConfigManager.Current.AvoidPortalsWhileWandering, hopDistance: 8);
                    lastApproachTime = now;
                    nextApproachDelay = UnityEngine.Random.Range(0.18f, 0.32f);

                    // Periodically re-issue attack packet while pursuing (every 1.0s) to keep server locked
                    if (now - lastAttackTime >= 1.0f)
                    {
                        netManager.SendAttack(target.Id);
                        lastAttackTime = now;
                    }

                    if (now - lastApproachLogTime >= 1.5f)
                    {
                        string reason = !hasLos ? "(LOS blocked)" : $"(dist: {dist:F1} > {combatRange:F1})";
                        BotEngine.Instance?.LogEvent($"[Combat] Approaching {target.Name} (ID: {target.Id}, {reason}) towards attack tile ({attackTile.x}, {attackTile.y}).");
                        lastApproachLogTime = now;
                    }
                }
            }
        }

        public static float GetEffectiveAttackRange()
        {
            var state = Assets.Scripts.PlayerControl.PlayerState.Instance;
            if (state != null)
            {
                int serverRange = state.GetStat(RebuildSharedData.Enum.EntityStats.CharacterStat.Range);
                if (serverRange > 1)
                {
                    // Ranged weapon (Bow, Spear, Gun, etc.) - exact server stat
                    return (float)serverRange;
                }
                else if (serverRange == 1)
                {
                    // Melee weapon - 1.8f provides a smooth buffer without colliding inside monster model
                    return 1.8f;
                }

                // Pre-sync fallback based on weapon class or Archer job
                var cam = CameraFollower.Instance;
                int weaponClass = cam != null && cam.TargetControllable != null ? cam.TargetControllable.WeaponClass : 0;

                if (weaponClass == 12 || state.JobId == 2 || state.JobId == 8) // Bow / Archer
                {
                    int vultureLvl = state.KnownSkills != null && state.KnownSkills.TryGetValue(RebuildSharedData.Enum.CharacterSkill.VultureEye, out int lvl) ? lvl : 0;
                    return 5f + vultureLvl;
                }
                if (weaponClass == 4 || weaponClass == 5) // Spear
                    return 2.0f;
            }
            return 1.8f;
        }
    }
}
