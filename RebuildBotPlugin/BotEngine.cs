using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.MapEditor;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.Sprites;
using RebuildBotPlugin.Controllers;
using RebuildBotPlugin.Services;
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
        public LowSpecController LowSpec { get; } = new LowSpecController();
        public JobChangeController JobChange { get; } = new JobChangeController();
        public EquipmentController Equipment { get; } = new EquipmentController();
        public MacroController Macro { get; } = new MacroController();

        public ServerControllable Player => CameraFollower.Instance?.Target != null ? CameraFollower.Instance.Target.GetComponent<ServerControllable>() : null;

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

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private void Start()
        {
            if (Services.ProfileManager.HiddenCliFlag)
            {
                try
                {
                    IntPtr hwnd = GetActiveWindow();
                    if (hwnd != IntPtr.Zero)
                    {
                        ShowWindow(hwnd, 0);
                    }
                }
                catch { }
            }

            EmitInitialStartupStatus();
            LogEvent("Bot engine initialized (modular architecture).");
        }

        private void EmitInitialStartupStatus()
        {
            try
            {
                string profileName = string.IsNullOrEmpty(Services.ProfileManager.ActiveProfileName) ? "Default" : Services.ProfileManager.ActiveProfileName;
                var statusObj = new
                {
                    Profile = profileName,
                    CharacterName = profileName,
                    JobName = "Starting Up...",
                    Level = 0,
                    JobLevel = 0,
                    Hp = 1,
                    MaxHp = 1,
                    Sp = 1,
                    MaxSp = 1,
                    Weight = 0,
                    MaxWeight = 1,
                    Zeny = 0,
                    CurrentMap = "",
                    PositionX = 0,
                    PositionY = 0,
                    BotState = "Launching",
                    IsBotEnabled = BotConfigManager.Current.Enabled,
                    BaseExp = 0L,
                    MaxBaseExp = 1L,
                    BaseExpPerHour = 0.0,
                    JobExpPerHour = 0.0,
                    SessionBaseExpGained = 0L,
                    SessionJobExpGained = 0L,
                    MonstersKilled = 0,
                    SessionUptimeSeconds = 0.0,
                    HasActiveMacro = false,
                    CurrentMacro = "",
                    ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                    Timestamp = DateTime.UtcNow
                };

                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string json = System.Text.Json.JsonSerializer.Serialize(statusObj, options);

                string path = Services.ProfileManager.GetBotStatusPath();
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                System.IO.File.WriteAllText(path, json);
            }
            catch { }
        }

        public void ForceEmitStatus()
        {
            var netManager = NetworkManager.Instance;
            ServerControllable player = null;
            if (netManager != null && netManager.EntityList != null)
            {
                netManager.EntityList.TryGetValue(netManager.PlayerId, out player);
            }
            EmitBotStatus(Time.time, player, netManager, force: true);
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

        private float lastStatusEmitTime = 0f;

        private void LateUpdate()
        {
            float now = Time.time;
            var netManager = NetworkManager.Instance;
            ServerControllable player = null;
            if (netManager != null && netManager.EntityList != null)
            {
                netManager.EntityList.TryGetValue(netManager.PlayerId, out player);
            }

            EmitBotStatus(now, player, netManager);

            if (MinimapMarker == null) return;

            if (CurrentState != lastLoggedState)
            {
                LogEvent($"[State] {lastLoggedState} -> {CurrentState}");
                lastLoggedState = CurrentState;
            }

            MinimapMarker.UpdateWaypointMarker(
                BotConfigManager.Current.Enabled,
                BotConfigManager.Current.AutoWander,
                CurrentState,
                Navigation.CurrentExplorationWaypoint);
        }

        private void EmitBotStatus(float now, ServerControllable player, NetworkManager netManager, bool force = false)
        {
            if (!force && now - lastStatusEmitTime < 1.0f) return;
            lastStatusEmitTime = now;

            try
            {
                var state = PlayerState.Instance;
                var tracker = ExpTracker;
                string profileName = string.IsNullOrEmpty(Services.ProfileManager.ActiveProfileName) ? "Default" : Services.ProfileManager.ActiveProfileName;

                string jobName = "Novice";
                int jobLevel = 1;
                if (state != null)
                {
                    jobLevel = state.GetData(RebuildSharedData.Enum.EntityStats.PlayerStat.JobLevel);
                    if (Assets.Scripts.Sprites.ClientDataLoader.Instance != null)
                    {
                        jobName = Assets.Scripts.Sprites.ClientDataLoader.Instance.GetJobNameForId(state.JobId) ?? $"Job {state.JobId}";
                    }
                }

                string botStateStr = CurrentState.ToString();
                string jobDisplayStr = jobName;

                if (player == null || string.IsNullOrEmpty(netManager?.CurrentMap))
                {
                    if (Login.IsActive)
                    {
                        botStateStr = Login.State switch
                        {
                            LoginState.SubmittingLogin => "SubmittingLogin",
                            LoginState.SelectingCharacter => "SelectingCharacter",
                            LoginState.AwaitingCharacterSelect => "AwaitingCharSelect",
                            LoginState.AwaitingWorldEntry => "EnteringWorld",
                            LoginState.DismissingNotice => "DismissingNotice",
                            LoginState.WaitingCooldown => "Reconnecting",
                            _ => "LoggingIn"
                        };
                        jobDisplayStr = !string.IsNullOrWhiteSpace(Login.StatusText) ? Login.StatusText : "Logging In...";
                    }
                    else
                    {
                        botStateStr = "Connecting";
                        jobDisplayStr = "Connecting to Server...";
                    }
                }

                var statusObj = new
                {
                    Profile = profileName,
                    CharacterName = player != null ? player.Name : (state != null ? state.PlayerName : profileName),
                    JobName = jobDisplayStr,
                    Level = player != null ? player.Level : (state != null ? state.Level : 0),
                    JobLevel = jobLevel,
                    Hp = player != null ? player.Hp : (state != null ? state.Hp : 0),
                    MaxHp = player != null ? player.MaxHp : (state != null ? state.MaxHp : 1),
                    Sp = player != null ? player.Sp : (state != null ? state.Sp : 0),
                    MaxSp = player != null ? player.MaxSp : (state != null ? state.MaxSp : 1),
                    Weight = state != null ? state.CurrentWeight : 0,
                    MaxWeight = state != null ? state.MaxWeight : 1,
                    Zeny = state != null ? state.Zeny : 0,
                    CurrentMap = netManager != null ? netManager.CurrentMap : "",
                    PositionX = player != null ? player.CellPosition.x : 0,
                    PositionY = player != null ? player.CellPosition.y : 0,
                    BotState = botStateStr,
                    IsBotEnabled = BotConfigManager.Current.Enabled,
                    BaseExp = tracker.CurrentBaseExp,
                    MaxBaseExp = tracker.MaxBaseExp,
                    BaseExpPerHour = tracker.BaseExpPerHour,
                    JobExpPerHour = tracker.JobExpPerHour,
                    SessionBaseExpGained = tracker.SessionBaseExpGained,
                    SessionJobExpGained = tracker.SessionJobExpGained,
                    MonstersKilled = Combat.KillCount,
                    SessionUptimeSeconds = tracker.ElapsedTime.TotalSeconds,
                    HasActiveMacro = Macro.HasActiveMacro,
                    CurrentMacro = Macro.CurrentAction != null ? Macro.CurrentAction.Description : "",
                    ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                    Timestamp = DateTime.UtcNow
                };

                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string json = System.Text.Json.JsonSerializer.Serialize(statusObj, options);

                string path = Services.ProfileManager.GetBotStatusPath();
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                System.IO.File.WriteAllText(path, json);
            }
            catch { }
        }

        private void OnTeleportReset()
        {
            Combat.Clear();
            Loot.Clear();
            Navigation.ResetWander();
            Skills.Clear();
            Survival.ClearRecovery();
        }

        private void Update()
        {
            float now = Time.time;
            LowSpec.Update(now);

            if (!BotConfigManager.Current.Enabled)
            {
                if (wasBotEnabled)
                {
                    Services.NpcInteractionHelper.CleanupNpcUi();
                    Navigation.ResetWander();
                    Login.Clear();
                    Survival.ClearRecovery();
                    wasBotEnabled = false;
                }
                CurrentState = BotState.Disabled;
                return;
            }
            wasBotEnabled = true;

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
            Services.ProfileManager.OnCharacterIdentified(player.Name);

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

            // Auto-Equip Empty Slots ("Finders Keepers" Gear)
            Equipment.ProcessAutoEquip(netManager, player, now);

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

            // PRIORITY 2.5: DISCRETE MACRO ACTION QUEUE (Buy, Equip, Upgrade, Socket, Travel, etc.)
            if (Macro.ProcessMacro(this, now))
            {
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

            // PRIORITY 3.4: ACTIVE NPC DIALOG WATCHER (Automatically pace & advance any open dialogs when not in Kafra travel)
            if (!Navigation.IsKafraTravelActive && NpcInteractionHelper.ProcessActiveDialog(netManager, now))
            {
                return;
            }

            // PRIORITY 3.5: TOWN ROUTINE (Return to base, sell, and store when overweight)
            if (TownRoutine.ProcessTownRoutine(netManager, player, Navigation, now))
            {
                return;
            }

            // PRIORITY 3.7: JOB CHANGE & STARTER GIFT (Adventuring Bard at Base)
            if (BotConfigManager.Current.AutoClaimBardGifts && JobChange.NeedsStarterGift(player))
            {
                if (string.Equals(netManager.CurrentMap, JobChangeController.BardMap, StringComparison.OrdinalIgnoreCase))
                {
                    if (!JobChange.IsActive)
                    {
                        JobChange.StartClaimStarterGift("New character starter funds");
                    }
                    if (JobChange.ProcessJobChange(netManager, player, Navigation, now))
                    {
                        return;
                    }
                }
            }
            else if (BotConfigManager.Current.AutoJobChange && JobChangeController.IsEligibleForJobChange(player))
            {
                if (string.Equals(netManager.CurrentMap, JobChangeController.BardMap, StringComparison.OrdinalIgnoreCase))
                {
                    if (!JobChange.IsActive)
                    {
                        JobChange.StartJobChange("Eligible for 1st Job promotion at base");
                    }
                    if (JobChange.ProcessJobChange(netManager, player, Navigation, now))
                    {
                        return;
                    }
                }
                else if (!TownRoutine.IsActive)
                {
                    // Not in base map, initiate return to base to visit the Bard
                    TownRoutine.StartRoutine("Return to base for 1st Job promotion");
                    return;
                }
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
                        float distToItem = Vector2.Distance(player.CellPosition, new Vector2(nearestItem.transform.position.x, nearestItem.transform.position.z));
                        LogEvent($"[Loot] Picking up {nearestItem.ItemName} (ID: {nearestItem.EntityId}, dist: {distToItem:F1} tiles).");
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
