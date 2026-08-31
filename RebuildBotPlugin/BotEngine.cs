using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.MapEditor;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.Sprites;
using RebuildBotPlugin.Controllers;
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
        TravelingToTargetMap,
        Fleeing,
        Resting
    }

    public class BotEngine : MonoBehaviour
    {
        public static BotEngine Instance;

        public BotState CurrentState = BotState.Disabled;
        private BotState lastLoggedState = BotState.Disabled;

        // Domain Controllers
        public TargetingController Targeting { get; } = new TargetingController();
        public CombatController Combat { get; } = new CombatController();
        public NavigationController Navigation { get; } = new NavigationController();
        public SurvivalController Survival { get; } = new SurvivalController();
        public LootController Loot { get; } = new LootController();
        public TownRoutineController TownRoutine { get; } = new TownRoutineController();
        public MinimapMarkerController MinimapMarker { get; } = new MinimapMarkerController();
        public ExpTracker ExpTracker { get; } = new ExpTracker();
        public SkillController Skills { get; } = new SkillController();
        public ProgressionController Progression { get; } = new ProgressionController();
        public LoginController Login { get; } = new LoginController();

        private float deathTimestamp = 0f;
        private float lastRespawnTime = 0f;
        private float lastLootTime = 0f;
        private bool justRespawned = false;
        private bool wasBotEnabled = false;

        public BotEngine(IntPtr ptr) : base(ptr) { }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            LogEvent("Bot engine initialized (modular architecture).");
        }

        public string GetCurrentMapName() => NetworkManager.Instance?.CurrentMap ?? "";

        public Vector2Int GetPlayerPosition()
        {
            var netManager = NetworkManager.Instance;
            if (netManager != null && netManager.EntityList != null &&
                netManager.EntityList.TryGetValue(netManager.PlayerId, out var player) && player != null)
            {
                return player.CellPosition;
            }
            return Vector2Int.zero;
        }

        public void LogEvent(string msg)
        {
            Plugin.LogInfo($"[{DateTime.Now:HH:mm:ss}] {msg}");
        }

        public void LogDebug(string msg)
        {
            if (BotConfigManager.Current.VerboseLogging)
            {
                Plugin.LogInfo($"[{DateTime.Now:HH:mm:ss}] [DEBUG] {msg}");
            }
        }

        private void LateUpdate()
        {
            if (MinimapMarker == null) return;

            if (CurrentState != lastLoggedState)
            {
                LogDebug($"[State] {lastLoggedState} -> {CurrentState}");
                lastLoggedState = CurrentState;
            }

            MinimapMarker.UpdateWaypointMarker(
                BotConfigManager.Current.Enabled,
                BotConfigManager.Current.AutoWander,
                CurrentState,
                Navigation.CurrentExplorationWaypoint);
        }

        private void OnTeleportReset()
        {
            Combat.Clear();
            Loot.Clear();
            Navigation.ResetWander();
            Skills.Clear();
        }

        private void Update()
        {
            if (!BotConfigManager.Current.Enabled)
            {
                if (wasBotEnabled)
                {
                    Services.NpcInteractionHelper.CleanupNpcUi();
                    Navigation.ResetWander();
                    Login.Clear();
                    wasBotEnabled = false;
                }
                CurrentState = BotState.Disabled;
                return;
            }
            wasBotEnabled = true;

            float now = Time.time;
            var cam = CameraFollower.Instance;
            var netManager = NetworkManager.Instance;
            ServerControllable player = null;

            bool isInGame = cam != null && cam.Target != null && netManager != null &&
                            netManager.EntityList != null && !string.IsNullOrEmpty(netManager.CurrentMap) &&
                            netManager.EntityList.TryGetValue(netManager.PlayerId, out player) && player != null;

            if (!isInGame || player == null)
            {
                CurrentState = BotState.Idle;
                Login.ProcessLogin(now);
                return;
            }

            // Successfully in-game - reset reconnect state
            Login.OnWorldEntered();

            // Player Death Handling
            if (player.Hp <= 0 || !player.IsCharacterAlive)
            {
                CurrentState = BotState.PlayerDead;
                if (deathTimestamp == 0f)
                {
                    deathTimestamp = Time.time;
                    LogEvent($"[Death] Character died at ({player.CellPosition.x}, {player.CellPosition.y}) on map '{netManager.CurrentMap}'.");
                    Combat.Clear();
                    Loot.Clear();
                    Targeting.Clear();
                }

                if (BotConfigManager.Current.AutoRespawn)
                {
                    if (Time.time - deathTimestamp >= 2.0f && Time.time - lastRespawnTime >= 3.0f)
                    {
                        netManager.SendRespawn(false);
                        lastRespawnTime = Time.time;
                        justRespawned = true;
                        LogEvent("[Respawn] Sent respawn packet (returning to save point)...");
                    }
                }
                return;
            }

            deathTimestamp = 0f;

            if (justRespawned)
            {
                justRespawned = false;
                if (string.Equals(netManager.CurrentMap, TownRoutineController.BaseMap, StringComparison.OrdinalIgnoreCase))
                {
                    if (TownRoutineController.HasSuppliesNeeded() || TownRoutineController.HasItemsToSell() || TownRoutineController.HasItemsToStore())
                    {
                        TownRoutine.StartRoutine("Respawn at base");
                    }
                }
            }

            if (!wasBotEnabled)
            {
                wasBotEnabled = true;
                if (string.Equals(netManager.CurrentMap, TownRoutineController.BaseMap, StringComparison.OrdinalIgnoreCase))
                {
                    if (TownRoutineController.HasSuppliesNeeded() || TownRoutineController.HasItemsToSell() || TownRoutineController.HasItemsToStore())
                    {
                        TownRoutine.StartRoutine("Bot started in base");
                    }
                }
            }

            // Map navigation & heatmap tracking
            MapHeatmap.Instance.UpdatePlayerPosition(netManager.CurrentMap, player.CellPosition);

            var walkProvider = RoWalkDataProvider.Instance;
            if (walkProvider != null && walkProvider.WalkData != null)
            {
                MapNavMesh.Instance.AnalyzeMap(netManager.CurrentMap, walkProvider.WalkData);
            }

            // EXP Tracker Baseline
            if (ExpTracker.CurrentBaseExp == -1 && CameraFollower.Instance != null && player.Level > 0)
            {
                int max = CameraFollower.Instance.ExpForLevel(player.Level);
                int cur = PlayerState.Instance != null ? PlayerState.Instance.Exp : 0;
                if (max > 0) ExpTracker.UpdateBaseExp(cur, max);
            }

            // Character Auto-Progression (Stat & Skill Point Allocation)
            Progression.ProcessProgression(netManager, player, now);

            // Cleanup temporary loot blacklists
            Loot.CleanupLootAttempts(now);

            // PRIORITY 1: SURVIVAL & AVOIDANCE (Potions, Low HP Fly Wing, Boss Escape, Sit-Rest)
            if (Survival.ProcessSurvival(netManager, player, now, Targeting, Navigation, OnTeleportReset, ref CurrentState))
            {
                return;
            }

            // PRIORITY 1.5: BUFFS & SUPPORT RECOVERY (Self-Buffs, Party Heals, Blessing/Agi)
            if (!TownRoutine.IsActive && Skills.ProcessBuffsAndRecovery(netManager, player, now))
            {
                return;
            }

            // PRIORITY 2: SELF-DEFENSE (Any monster currently attacking player)
            ServerControllable attacker = null;
            if (BotConfigManager.Current.AutoAttack)
            {
                attacker = Targeting.GetAttackingMonster(player.CellPosition);
            }

            if (attacker != null)
            {
                Combat.CurrentLockedTargetId = attacker.Id;
                Combat.ExecuteCombatAction(netManager, player, attacker, now, Navigation, Targeting, ref CurrentState);
                return;
            }

            // PRIORITY 3: ONGOING COMBAT (Stick with engaged target until defeated)
            ServerControllable lockedTarget = null;
            if (BotConfigManager.Current.AutoAttack && Combat.CurrentLockedTargetId != -1)
            {
                lockedTarget = Combat.GetLockedTarget(player.CellPosition);
            }

            // Aggressive Threat Preemption:
            // If current target is passive, but an aggressive monster is chasing or in close range (<= 8 tiles),
            // immediately preempt and switch targets to defend against the threat!
            if (lockedTarget != null && BotConfigManager.Current.PrioritizeAggressiveMonsters)
            {
                if (!Targeting.IsMonsterAggressive(lockedTarget.Name) && !Targeting.IsAttackingPlayer(lockedTarget.Id))
                {
                    var nearbyThreat = Targeting.FindNearbyAggressiveThreat(player.CellPosition, 8.0f);
                    if (nearbyThreat != null && nearbyThreat.Id != lockedTarget.Id)
                    {
                        LogEvent($"[Combat] Preempting passive '{lockedTarget.Name}' -> engaging aggressive threat '{nearbyThreat.Name}' (dist: {Vector2.Distance(player.CellPosition, nearbyThreat.CellPosition):F1})!");
                        Combat.CurrentLockedTargetId = nearbyThreat.Id;
                        lockedTarget = nearbyThreat;
                    }
                }
            }

            if (lockedTarget != null)
            {
                Combat.ExecuteCombatAction(netManager, player, lockedTarget, now, Navigation, Targeting, ref CurrentState);
                return;
            }
            else
            {
                Combat.OnTargetDefeated();
            }

            // PRIORITY 3.5: TOWN ROUTINE (Return to base, sell, and store when overweight)
            if (TownRoutine.ProcessTownRoutine(netManager, player, Navigation, now))
            {
                return;
            }

            // PRIORITY 4: CROSS-MAP TRAVEL TO TARGET MAP (AutoTravel)
            if (!TownRoutine.IsActive && Navigation.ProcessTravel(netManager, player, now, ref CurrentState))
            {
                return;
            }

            // PRIORITY 5: AUTO-LOOT (Pick up all items in radius before engaging new passive monsters)
            if (BotConfigManager.Current.AutoLoot)
            {
                if (Loot.PendingLootItemId != -1)
                {
                    if (netManager.GroundItemList == null || !netManager.GroundItemList.ContainsKey(Loot.PendingLootItemId))
                    {
                        Loot.LootCount++;
                        LogEvent($"Collected loot item! Total Loot: {Loot.LootCount}");
                        Loot.PendingLootItemId = -1;
                    }
                }

                var nearestItem = Loot.FindNearestGroundItem(player.CellPosition);
                if (nearestItem != null)
                {
                    if (now - lastLootTime >= BotConfigManager.Current.LootCooldownSeconds)
                    {
                        Loot.PendingLootItemId = nearestItem.EntityId;
                        Loot.TrackLootAttempt(nearestItem.EntityId, now);

                        // Directly dispatch SendPickUpItem - server handles pathing and queued pickup
                        netManager.SendPickUpItem(nearestItem.EntityId);
                        lastLootTime = now;
                        CurrentState = BotState.LootingItem;
                        LogEvent($"Picking up loot item: {nearestItem.ItemName} (ID: {nearestItem.EntityId})");
                    }
                    return;
                }
                else
                {
                    Loot.PendingLootItemId = -1;
                }
            }

            // PRIORITY 6: INITIATE NEW COMBAT (Only when all items in radius are looted and not in recovery/town routine)
            ServerControllable newTarget = null;
            if (BotConfigManager.Current.AutoAttack && !Survival.IsRecovering && !TownRoutine.IsActive)
            {
                newTarget = Targeting.FindBestTargetMonster(player.CellPosition);
            }

            if (newTarget != null)
            {
                Combat.CurrentLockedTargetId = newTarget.Id;
                Combat.ExecuteCombatAction(netManager, player, newTarget, now, Navigation, Targeting, ref CurrentState);
                return;
            }

            // PRIORITY 7: FLUID MACRO-EXPLORATION AUTO-WANDER
            if (BotConfigManager.Current.AutoWander && !Survival.IsRecovering && !TownRoutine.IsActive)
            {
                Survival.TryUseAspdPotion(netManager, now);
                Navigation.ProcessWander(netManager, player, now, ref CurrentState);
                return;
            }

            CurrentState = BotState.Idle;
        }
    }
}
