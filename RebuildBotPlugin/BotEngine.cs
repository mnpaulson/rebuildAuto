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
        Resting,
        FollowingPartyLeader,
        WaitingForParty,
        DistributorVending,
        DistributorRestocking,
        DistributorCollectingLoot,
        DonatingToDistributor,
        BuyingFromDistributor
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
        public PartyController Party { get; } = new PartyController();
        public EquipmentTargetController EquipmentTargets { get; } = new EquipmentTargetController();
        public DistributorController Distributor { get; } = new DistributorController();

        public ServerControllable Player => CameraFollower.Instance?.Target != null ? CameraFollower.Instance.Target.GetComponent<ServerControllable>() : null;

        private float deathTimestamp = 0f;
        private float lastRespawnTime = 0f;
        private bool justRespawned = false;
        private bool wasBotEnabled = false;
        private float lastConfigFileCheckTime = 0f;
        private DateTime lastConfigWriteTime = DateTime.MinValue;

        private static readonly System.Text.Json.JsonSerializerOptions CachedJsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        private static readonly System.Diagnostics.Stopwatch FrameStopwatch = new System.Diagnostics.Stopwatch();
        private int lastGc0Count = 0;
        private Vector2Int lastHeatmapPlayerPos = new Vector2Int(-9999, -9999);
        private float lastHeatmapUpdateTime = 0f;

        private void CheckHotReloadConfig(float now)
        {
            if (now - lastConfigFileCheckTime < 1.0f) return;
            lastConfigFileCheckTime = now;

            try
            {
                string cfgPath = BotConfigManager.ConfigPath;
                if (!string.IsNullOrEmpty(cfgPath) && System.IO.File.Exists(cfgPath))
                {
                    var writeTime = System.IO.File.GetLastWriteTimeUtc(cfgPath);
                    if (lastConfigWriteTime != DateTime.MinValue && writeTime > lastConfigWriteTime)
                    {
                        BotConfigManager.LoadConfig();
                        Services.BotLog.Info("[Config] Detected updated config on disk. Hot-reloaded profile settings.");
                    }
                    lastConfigWriteTime = writeTime;
                }
            }
            catch { }
        }

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
                    PartyEnabled = BotConfigManager.Current.PartyEnabled,
                    PartyName = BotConfigManager.Current.PartyName ?? "",
                    IsPartyLeader = BotConfigManager.Current.IsPartyLeader,
                    IsPartySupport = BotConfigManager.Current.IsPartySupport,
                    IsPartyLooter = BotConfigManager.Current.IsPartyLooter,
                    TargetEntityId = -1,
                    TargetName = "None",
                    Timestamp = DateTime.UtcNow
                };

                string json = System.Text.Json.JsonSerializer.Serialize(statusObj, CachedJsonOptions);

                string path = Services.ProfileManager.GetBotStatusPath();
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                using (var sw = new System.IO.StreamWriter(fs))
                {
                    sw.Write(json);
                }
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
            Services.BotLog.Info(msg);
        }

        public void LogDebug(string msg)
        {
            Services.BotLog.Debug(msg);
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
            float emitInterval = BotConfigManager.Current.PartyEnabled ? 0.25f : 1.0f;
            if (!force && now - lastStatusEmitTime < emitInterval) return;
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
                    PartyEnabled = BotConfigManager.Current.PartyEnabled,
                    PartyName = BotConfigManager.Current.PartyName ?? "",
                    IsPartyLeader = BotConfigManager.Current.IsPartyLeader,
                    IsPartySupport = BotConfigManager.Current.IsPartySupport,
                    IsPartyLooter = BotConfigManager.Current.IsPartyLooter,
                    IsInGameParty = state != null && state.IsInParty,
                    InGamePartyName = (state != null && state.IsInParty && !string.IsNullOrEmpty(state.PartyName)) ? state.PartyName : "",
                    IsInGamePartyLeader = state != null && state.IsInParty && state.PartyLeader == state.PartyMemberId,
                    InGamePartyLeaderName = (state != null && state.IsInParty && state.PartyMembers != null && state.PartyMembers.TryGetValue(state.PartyLeader, out var leadMem)) ? (leadMem.PlayerName ?? "") : "",
                    IsDistributor = BotConfigManager.Current.IsDistributor && DistributorController.IsMerchantClass(),
                    DistributorMap = BotConfigManager.Current.DistributorMap ?? "",
                    DistributorX = BotConfigManager.Current.DistributorX,
                    DistributorY = BotConfigManager.Current.DistributorY,
                    IsReadyForDonations = Distributor != null && Distributor.IsReadyForDonations,
                    IsVendingOpen = Distributor != null && Distributor.IsVendingOpen,
                    VendingShopTitle = BotConfigManager.Current.VendingShopTitle ?? "Fleet Depot",
                    TargetEntityId = Combat.CurrentLockedTargetId,
                    TargetName = Combat.CurrentTargetName,
                    IsAlive = player != null ? (player.IsCharacterAlive && player.Hp > 0) : (CurrentState != BotState.PlayerDead && !string.Equals(botStateStr, "PlayerDead", StringComparison.OrdinalIgnoreCase)),
                    ActiveStatusEffects = GetActiveStatusEffectNames(player),
                    HasAspdBuff = Survival != null && Survival.HasAspdBuffActive(player),
                    AspdBuffRemainingSeconds = Survival != null ? (int)Survival.GetRemainingAspdBuffSeconds(player) : 0,
                    Timestamp = DateTime.UtcNow
                };

                string json = System.Text.Json.JsonSerializer.Serialize(statusObj, CachedJsonOptions);

                string path = Services.ProfileManager.GetBotStatusPath();
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                using (var stream = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
                using (var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8))
                {
                    writer.Write(json);
                }
            }
            catch { }
        }

        private static List<string> GetActiveStatusEffectNames(ServerControllable player)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Assets.Scripts.UI.Hud.StatusEffectPanel.Instance != null &&
                    Assets.Scripts.UI.Hud.StatusEffectPanel.Instance.StatusEffectLookup != null)
                {
                    foreach (var kvp in Assets.Scripts.UI.Hud.StatusEffectPanel.Instance.StatusEffectLookup)
                    {
                        if (kvp.Value != null && !kvp.Value.IsExpired)
                        {
                            set.Add(kvp.Key.ToString());
                        }
                    }
                }

                if (player != null && player.StatusEffectState != null)
                {
                    var effects = player.StatusEffectState.GetStatusEffects();
                    if (effects != null)
                    {
                        foreach (var kvp in effects)
                        {
                            set.Add(kvp.Key.ToString());
                        }
                    }
                }
            }
            catch { }
            return new List<string>(set);
        }

        private void OnTeleportReset()
        {
            Services.NpcInteractionHelper.CleanupNpcUi();
            Navigation.InvalidateTravelPlan();
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
            CheckHotReloadConfig(now);

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

            FrameStopwatch.Restart();
            try
            {
                ProcessBotTick(now);
            }
            finally
            {
                FrameStopwatch.Stop();
                long frameMs = FrameStopwatch.ElapsedMilliseconds;
                int currentGc0 = System.GC.CollectionCount(0);
                if (frameMs > 25)
                {
                    int gcDelta = currentGc0 - lastGc0Count;
                    Services.BotLog.Warn($"[LagSpike] Bot frame took {frameMs}ms! (GC0 Delta: {gcDelta}, State: {CurrentState})");
                }
                lastGc0Count = currentGc0;
            }
        }

        private void ProcessBotTick(float now)
        {
            var cam = CameraFollower.Instance;
            var netManager = NetworkManager.Instance;
            ServerControllable player = null;

            bool isInGame = cam != null && !cam.IsInErrorState && cam.Target != null && netManager != null &&
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

            // Map navigation & heatmap tracking (only update when player moves or once per second)
            if (player.CellPosition != lastHeatmapPlayerPos || now - lastHeatmapUpdateTime >= 1.0f)
            {
                MapHeatmap.Instance.UpdatePlayerPosition(netManager.CurrentMap, player.CellPosition);
                lastHeatmapPlayerPos = player.CellPosition;
                lastHeatmapUpdateTime = now;
            }

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

            // In-Game Party Synchronization (Create, Invite, Accept, Leave)
            Party.SynchronizeInGameParty(netManager, player, now);

            // PRIORITY 1: SURVIVAL & AVOIDANCE (Potions, Low HP Fly Wing, Boss Escape, Sit-Rest)
            if (CurrentState == BotState.Fleeing && PartyController.ShouldSuppressFlee())
            {
                CurrentState = BotState.Idle;
            }

            if (Survival.ProcessSurvival(netManager, player, now, Targeting, Navigation, OnTeleportReset, ref CurrentState))
            {
                return;
            }

            // PRIORITY 1.5: BUFFS & SUPPORT RECOVERY (Self-Buffs, Party Heals, Blessing/Agi, Ruwach)
            if (!TownRoutine.IsActive && !Services.NpcInteractionHelper.IsInNpcInteraction() && Skills.ProcessBuffsAndRecovery(netManager, player, now, Navigation))
            {
                return;
            }

            // PRIORITY 1.7: MERCHANT DISTRIBUTOR (Central Fleet Depot & Vending)
            if (BotConfigManager.Current.IsDistributor && DistributorController.IsMerchantClass())
            {
                Distributor.ProcessDistributor(netManager, player, Navigation, Loot, now, ref CurrentState);
                return;
            }

            // PRIORITY 1.8: TOWN ROUTINE & ESSENTIAL RESTOCK TRIGGER
            // When town routine is active, or essential supplies (e.g. arrows, potions, wings) are depleted,
            // or an Archer has 0 arrows, or overweight, town routine takes top priority over combat!
            bool isBowUserOutOfAmmo = ArrowHelper.IsBowUserOutOfAmmo(player);
            string restockReason = null;
            bool hasDepletedEssential = BotConfigManager.Current.AutoRestockOnLowSupplies &&
                                        TownRoutineController.HasDepletedEssentialSupplies(out restockReason);
            bool isOverweight = TownRoutineController.IsOverweight();

            if (TownRoutine.IsActive || isBowUserOutOfAmmo || hasDepletedEssential || isOverweight)
            {
                if (!TownRoutine.IsActive)
                {
                    string startReason = isBowUserOutOfAmmo ? "Out of arrows" :
                                         (hasDepletedEssential ? restockReason : "Overweight threshold reached");
                    Combat.CurrentLockedTargetId = -1;
                    Combat.OnTargetDefeated();
                    TownRoutine.StartRoutine(startReason);
                }

                if (TownRoutine.IsActive)
                {
                    if (TownRoutine.ProcessTownRoutine(netManager, player, Navigation, now))
                    {
                        return;
                    }
                }
            }

            // PRIORITY 1.9: AUTOMATIC EQUIPMENT TARGETS (Purchase & Refine upgrades)
            if (!TownRoutine.IsActive)
            {
                if (EquipmentTargets.IsActive || EquipmentTargets.CheckTripInterrupt(netManager, player, now))
                {
                    Combat.CurrentLockedTargetId = -1;
                    Combat.OnTargetDefeated();
                    if (EquipmentTargets.Process(this, now))
                    {
                        return;
                    }
                }
            }

            // If a bow user has 0 arrows, strictly prevent falling through to any combat priorities!
            if (isBowUserOutOfAmmo)
            {
                CurrentState = BotState.Idle;
                return;
            }

            // PRIORITY 2: SELF-DEFENSE (Any monster currently attacking player)
            ServerControllable attacker = null;
            if (!TownRoutine.IsActive && !isBowUserOutOfAmmo && (BotConfigManager.Current.AutoAttack || BotConfigManager.Current.PartyEnabled) && !BotConfigManager.Current.IsPartySupport)
            {
                attacker = Targeting.GetAttackingMonster(player.CellPosition);
            }

            if (attacker != null)
            {
                Combat.CurrentLockedTargetId = attacker.Id;
                Combat.ExecuteCombatAction(netManager, player, attacker, now, Navigation, Targeting, ref CurrentState);
                return;
            }

            // PRIORITY 2.3: PARTY COORDINATION (Follow Leader & Assist Target)
            if (Party.ProcessParty(netManager, player, now, Navigation, Combat, Targeting, ref CurrentState))
            {
                return;
            }

            // PRIORITY 2.5: DISCRETE MACRO ACTION QUEUE (Buy, Equip, Upgrade, Socket, Travel, etc.)
            if (Macro.ProcessMacro(this, now))
            {
                return;
            }

            // PRIORITY 3: ONGOING COMBAT (Stick with engaged target until defeated)
            ServerControllable lockedTarget = null;
            if (!TownRoutine.IsActive && !isBowUserOutOfAmmo && BotConfigManager.Current.AutoAttack && !BotConfigManager.Current.IsPartySupport && Combat.CurrentLockedTargetId != -1)
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

            // Watchdog: detect and recover from ghost/desynced NPC interactions where character is locked on server without client UI
            if (!Navigation.IsKafraTravelActive && NpcInteractionHelper.CheckAndRecoverGhostInteraction(netManager, now))
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
            if (BotConfigManager.Current.AutoLoot && !Party.ShouldSkipLootingForPartyLooter())
            {
                if (Loot.ProcessLoot(netManager, player, now, ref CurrentState))
                {
                    return;
                }
            }

            // PRIORITY 6: INITIATE NEW COMBAT (Only when all items in radius are looted and not in recovery/town routine)
            ServerControllable newTarget = null;
            bool partyFollower = BotConfigManager.Current.PartyEnabled && !BotConfigManager.Current.IsPartyLeader;
            if (!TownRoutine.IsActive && !isBowUserOutOfAmmo && !hasDepletedEssential && !isOverweight &&
                BotConfigManager.Current.AutoAttack && !BotConfigManager.Current.IsPartySupport && !partyFollower && !Survival.IsRecovering)
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
            if (BotConfigManager.Current.AutoWander && !partyFollower && !Survival.IsRecovering && !TownRoutine.IsActive)
            {
                Survival.TryUseAspdPotion(netManager, player, now);
                Navigation.ProcessWander(netManager, player, now, ref CurrentState);
                return;
            }

            CurrentState = BotState.Idle;
        }
    }
}
