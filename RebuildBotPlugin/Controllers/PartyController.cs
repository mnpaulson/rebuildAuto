using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Assets.Scripts;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI;
using Assets.Scripts.UI.Hud;
using RebuildBotPlugin;
using RebuildBotPlugin.Services;
using RebuildSharedData.Enum;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public class PartyLeaderInfo
    {
        public string Profile { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public string CurrentMap { get; set; } = "";
        public int PositionX { get; set; } = 0;
        public int PositionY { get; set; } = 0;
        public int Hp { get; set; } = 1;
        public int MaxHp { get; set; } = 1;
        public bool IsAlive { get; set; } = true;
        public string BotState { get; set; } = "";
        public int TargetEntityId { get; set; } = -1;
        public string TargetName { get; set; } = "";
        public List<string> ActiveStatusEffects { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.MinValue;
    }

    public class PartyMemberStatusInfo
    {
        public string Profile { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public string CurrentMap { get; set; } = "";
        public int PositionX { get; set; } = 0;
        public int PositionY { get; set; } = 0;
        public int Hp { get; set; } = 1;
        public int MaxHp { get; set; } = 1;
        public bool IsAlive { get; set; } = true;
        public string BotState { get; set; } = "";
        public bool IsLeader { get; set; } = false;
        public bool IsLooter { get; set; } = false;
        public bool IsBotEnabled { get; set; } = true;
        public bool IsInGameParty { get; set; } = false;
        public string InGamePartyName { get; set; } = "";
        public bool IsInGameLeader { get; set; } = false;
        public string InGameLeaderName { get; set; } = "";
        public List<string> ActiveStatusEffects { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.MinValue;
    }

    public class PartyController
    {
        public static bool IsInAnyParty()
        {
            var cfg = BotConfigManager.Current;
            if (cfg.PartyEnabled && !string.IsNullOrWhiteSpace(cfg.PartyName)) return true;
            if (PlayerState.Instance != null && PlayerState.Instance.IsInParty) return true;
            return false;
        }

        public static bool ShouldSuppressFlee()
        {
            return BotConfigManager.Current.SuppressFleeWhilePartied && IsInAnyParty();
        }

        public bool IsAnyPartyMemberInTown(out PartyMemberStatusInfo memberInTown)
        {
            memberInTown = null;
            var cfg = BotConfigManager.Current;
            if (!cfg.PartyEnabled || string.IsNullOrWhiteSpace(cfg.PartyName)) return false;

            string myCharName = BotEngine.Instance?.Player?.Name ?? ProfileManager.ActiveProfileName ?? "";
            DateTime cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(45);

            foreach (var member in DiscoveredMembers)
            {
                if (string.Equals(member.CharacterName, myCharName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Only consider active, enabled fleet members
                if (!member.IsBotEnabled || member.Timestamp < cutoff)
                    continue;

                // Check if this member is currently in a town or base map
                if (TownRoutineController.IsTownOrBaseMap(member.CurrentMap))
                {
                    memberInTown = member;
                    return true;
                }
            }

            return false;
        }

        public bool AreAllPartyMembersReadyInTown(string townMap, out string waitReason)
        {
            waitReason = "";
            var cfg = BotConfigManager.Current;
            if (!cfg.PartyEnabled || string.IsNullOrWhiteSpace(cfg.PartyName)) return true;

            string myCharName = BotEngine.Instance?.Player?.Name ?? ProfileManager.ActiveProfileName ?? "";
            DateTime cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(45);

            foreach (var member in DiscoveredMembers)
            {
                if (string.Equals(member.CharacterName, myCharName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Only consider active, enabled fleet members
                if (!member.IsBotEnabled || member.Timestamp < cutoff)
                    continue;

                // 1. Check map alignment
                if (!string.Equals(member.CurrentMap, townMap, StringComparison.OrdinalIgnoreCase))
                {
                    // If member is on another map and NOT in TownRoutine, they are on their way / traveling; do not hold leader back
                    if (!string.Equals(member.BotState, "TownRoutine", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    waitReason = $"Member '{member.CharacterName}' is on '{member.CurrentMap}' (awaiting arrival in '{townMap}')";
                    return false;
                }

                // 2. Must be alive
                if (!member.IsAlive || member.Hp <= 0)
                {
                    waitReason = $"Member '{member.CharacterName}' is dead/respawning";
                    return false;
                }

                // 3. Must not be in TownRoutine (selling, restocking, kafra)
                if (string.Equals(member.BotState, "TownRoutine", StringComparison.OrdinalIgnoreCase))
                {
                    waitReason = $"Member '{member.CharacterName}' is restocking / in Town Routine";
                    return false;
                }

                // 4. Must have recovered healthy HP (>= 70%)
                if (member.MaxHp > 0 && member.Hp < (int)(member.MaxHp * 0.70f))
                {
                    waitReason = $"Member '{member.CharacterName}' is recovering HP ({member.Hp}/{member.MaxHp})";
                    return false;
                }
            }

            return true;
        }

        public bool HasActivePartyLooter(bool sameMapOnly = true)
        {
            var cfg = BotConfigManager.Current;
            if (!cfg.PartyEnabled || string.IsNullOrWhiteSpace(cfg.PartyName)) return false;

            if (cfg.IsPartyLooter) return true;

            var netManager = NetworkManager.Instance;
            string myMap = netManager != null ? netManager.CurrentMap : "";

            foreach (var member in DiscoveredMembers)
            {
                if (member.IsLooter && member.IsAlive)
                {
                    if (!sameMapOnly) return true;
                    if (string.Equals(member.CurrentMap, myMap, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        public bool ShouldSkipLootingForPartyLooter()
        {
            var cfg = BotConfigManager.Current;
            if (!cfg.PartyEnabled || cfg.IsPartyLooter) return false;
            return HasActivePartyLooter(sameMapOnly: true);
        }

        public bool IsPartyOrFleetMember(ServerControllable entity)
        {
            if (entity == null) return false;
            var netManager = NetworkManager.Instance;
            if (netManager != null && entity.Id == netManager.PlayerId) return true;
            if (entity.IsAlly) return true;

            var playerState = PlayerState.Instance;
            if (playerState != null && playerState.IsInParty)
            {
                if (playerState.PartyMemberIdLookup != null && playerState.PartyMemberIdLookup.ContainsKey(entity.Id))
                    return true;

                if (playerState.PartyMembers != null)
                {
                    foreach (var kvp in playerState.PartyMembers)
                    {
                        var pm = kvp.Value;
                        if (pm == null) continue;
                        if (pm.EntityId == entity.Id || (!string.IsNullOrEmpty(pm.PlayerName) && string.Equals(pm.PlayerName, entity.Name, StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                }
            }

            if (CurrentLeader != null)
            {
                if ((!string.IsNullOrEmpty(CurrentLeader.CharacterName) && string.Equals(CurrentLeader.CharacterName, entity.Name, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(CurrentLeader.Profile) && string.Equals(CurrentLeader.Profile, entity.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            if (DiscoveredMembers != null && DiscoveredMembers.Count > 0)
            {
                for (int i = 0; i < DiscoveredMembers.Count; i++)
                {
                    var m = DiscoveredMembers[i];
                    if (m == null) continue;
                    if ((!string.IsNullOrEmpty(m.CharacterName) && string.Equals(m.CharacterName, entity.Name, StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(m.Profile) && string.Equals(m.Profile, entity.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public PartyLeaderInfo CurrentLeader { get; private set; }
        public List<PartyMemberStatusInfo> DiscoveredMembers { get; } = new();
        private float lastDiscoveryTime = 0f;
        private const float DiscoveryIntervalSeconds = 0.5f;
        private float lastLogTime = 0f;
        private float lastTravelWingTime = 0f;

        private int realtimeLeaderTargetId = -1;
        private float realtimeLeaderTargetTime = 0f;
        private int realtimeLeaderAttackerId = -1;
        private float realtimeLeaderAttackerTime = 0f;

        private float lastFollowMoveTime = 0f;
        private Vector2Int lastDispatchedLeaderPos = new Vector2Int(-9999, -9999);
        private Vector2Int lastFollowPlayerPos = new Vector2Int(-9999, -9999);
        private float followStuckTimer = 0f;

        private float lastPartySyncTime = 0f;
        private const float PartySyncInterval = 1.0f;
        private float lastPartyCreateAttemptTime = 0f;
        private const float PartyCreateInterval = 5.0f;
        private float lastPartyLeaveAttemptTime = 0f;
        private const float PartyLeaveInterval = 3.0f;
        private readonly Dictionary<string, float> followerInviteTimes = new(StringComparer.OrdinalIgnoreCase);
        private const float InviteRetryInterval = 5.0f;

        private static string ReadFileSafe(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch
            {
                return "";
            }
        }

        public void Clear()
        {
            CurrentLeader = null;
            DiscoveredMembers.Clear();
            realtimeLeaderTargetId = -1;
            realtimeLeaderTargetTime = 0f;
            realtimeLeaderAttackerId = -1;
            realtimeLeaderAttackerTime = 0f;
            lastFollowMoveTime = 0f;
            lastDispatchedLeaderPos = new Vector2Int(-9999, -9999);
            lastFollowPlayerPos = new Vector2Int(-9999, -9999);
            followStuckTimer = 0f;
        }

        public void OnAttackMotion(ServerControllable src, ServerControllable target)
        {
            if (CurrentLeader == null || src == null || target == null) return;

            // Check if src is leader
            bool isSrcLeader = (!string.IsNullOrEmpty(CurrentLeader.CharacterName) && string.Equals(src.Name, CurrentLeader.CharacterName, StringComparison.OrdinalIgnoreCase)) ||
                               (!string.IsNullOrEmpty(CurrentLeader.Profile) && string.Equals(src.Name, CurrentLeader.Profile, StringComparison.OrdinalIgnoreCase));

            if (isSrcLeader && target.CharacterType == CharacterType.Monster)
            {
                realtimeLeaderTargetId = target.Id;
                realtimeLeaderTargetTime = Time.time;
                return;
            }

            // Check if target is leader
            bool isTargetLeader = (!string.IsNullOrEmpty(CurrentLeader.CharacterName) && string.Equals(target.Name, CurrentLeader.CharacterName, StringComparison.OrdinalIgnoreCase)) ||
                                 (!string.IsNullOrEmpty(CurrentLeader.Profile) && string.Equals(target.Name, CurrentLeader.Profile, StringComparison.OrdinalIgnoreCase));

            if (isTargetLeader && src.CharacterType == CharacterType.Monster)
            {
                realtimeLeaderAttackerId = src.Id;
                realtimeLeaderAttackerTime = Time.time;
            }
        }

        public void UpdateLeaderDiscovery(float now)
        {
            if (now - lastDiscoveryTime < DiscoveryIntervalSeconds)
            {
                return;
            }
            lastDiscoveryTime = now;

            var cfg = BotConfigManager.Current;
            if (!cfg.PartyEnabled || string.IsNullOrWhiteSpace(cfg.PartyName))
            {
                CurrentLeader = null;
                DiscoveredMembers.Clear();
                return;
            }

            string myParty = cfg.PartyName.Trim();
            string myProfile = ProfileManager.ActiveProfileName;

            PartyLeaderInfo bestLeader = null;
            DateTime latestLeaderTime = DateTime.MinValue;
            var currentMembers = new List<PartyMemberStatusInfo>();

            var profileDirs = GetProfileDirectories();
            foreach (var pDir in profileDirs)
            {
                try
                {
                    string profileName = Path.GetFileName(pDir);
                    if (string.Equals(profileName, myProfile, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string statusFile = Path.Combine(pDir, "bot_status.json");
                    if (!File.Exists(statusFile))
                    {
                        continue;
                    }

                    var writeTime = File.GetLastWriteTimeUtc(statusFile);
                    if (DateTime.UtcNow - writeTime > TimeSpan.FromSeconds(60))
                    {
                        continue;
                    }

                    string json = ReadFileSafe(statusFile);
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    bool partyEnabled = root.TryGetProperty("PartyEnabled", out var pe) && pe.GetBoolean();
                    if (!partyEnabled) continue;

                    string partyName = root.TryGetProperty("PartyName", out var pn) ? pn.GetString() : "";
                    if (!string.Equals(partyName, myParty, StringComparison.OrdinalIgnoreCase)) continue;

                    bool isLeader = root.TryGetProperty("IsPartyLeader", out var il) && il.GetBoolean();
                    bool isLooter = root.TryGetProperty("IsPartyLooter", out var ilt) && ilt.GetBoolean();
                    bool isInGameParty = root.TryGetProperty("IsInGameParty", out var igp) && igp.GetBoolean();
                    string inGamePartyName = root.TryGetProperty("InGamePartyName", out var igpn) ? igpn.GetString() ?? "" : "";
                    bool isInGameLeader = root.TryGetProperty("IsInGamePartyLeader", out var igl) && igl.GetBoolean();
                    string inGameLeaderName = root.TryGetProperty("InGamePartyLeaderName", out var igln) ? igln.GetString() ?? "" : "";
                    DateTime timestamp = root.TryGetProperty("Timestamp", out var ts) && ts.TryGetDateTime(out var dt) ? dt : writeTime;

                    var activeEffects = new List<string>();
                    if (root.TryGetProperty("ActiveStatusEffects", out var effectsElem) && effectsElem.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in effectsElem.EnumerateArray())
                        {
                            string effectName = elem.GetString();
                            if (!string.IsNullOrEmpty(effectName)) activeEffects.Add(effectName);
                        }
                    }

                    string charName = root.TryGetProperty("CharacterName", out var cn) ? cn.GetString() ?? profileName : profileName;
                    string currentMap = root.TryGetProperty("CurrentMap", out var cm) ? cm.GetString() ?? "" : "";
                    int posX = root.TryGetProperty("PositionX", out var px) ? px.GetInt32() : 0;
                    int posY = root.TryGetProperty("PositionY", out var py) ? py.GetInt32() : 0;
                    int hp = root.TryGetProperty("Hp", out var hpProp) ? hpProp.GetInt32() : 1;
                    int maxHp = root.TryGetProperty("MaxHp", out var mhpProp) ? mhpProp.GetInt32() : 1;
                    string botState = root.TryGetProperty("BotState", out var bsProp) ? bsProp.GetString() ?? "" : "";
                    bool isAlive = hp > 0 && !string.Equals(botState, "PlayerDead", StringComparison.OrdinalIgnoreCase);
                    if (root.TryGetProperty("IsAlive", out var iaProp))
                    {
                        isAlive = iaProp.GetBoolean();
                    }
                    bool isBotEnabled = (!root.TryGetProperty("IsBotEnabled", out var ibe) || ibe.GetBoolean()) && !string.Equals(botState, "Disabled", StringComparison.OrdinalIgnoreCase);

                    var memberInfo = new PartyMemberStatusInfo
                    {
                        Profile = profileName,
                        CharacterName = charName,
                        CurrentMap = currentMap,
                        PositionX = posX,
                        PositionY = posY,
                        Hp = hp,
                        MaxHp = maxHp,
                        IsAlive = isAlive,
                        BotState = botState,
                        IsLeader = isLeader,
                        IsLooter = isLooter,
                        IsBotEnabled = isBotEnabled,
                        IsInGameParty = isInGameParty,
                        InGamePartyName = inGamePartyName,
                        IsInGameLeader = isInGameLeader,
                        InGameLeaderName = inGameLeaderName,
                        ActiveStatusEffects = activeEffects,
                        Timestamp = timestamp
                    };
                    currentMembers.Add(memberInfo);

                    if (isLeader && !cfg.IsPartyLeader && timestamp > latestLeaderTime)
                    {
                        latestLeaderTime = timestamp;
                        bestLeader = new PartyLeaderInfo
                        {
                            Profile = profileName,
                            CharacterName = charName,
                            CurrentMap = currentMap,
                            PositionX = posX,
                            PositionY = posY,
                            Hp = hp,
                            MaxHp = maxHp,
                            IsAlive = isAlive,
                            BotState = botState,
                            TargetEntityId = root.TryGetProperty("TargetEntityId", out var tid) ? tid.GetInt32() : -1,
                            TargetName = root.TryGetProperty("TargetName", out var tn) ? tn.GetString() ?? "" : "",
                            ActiveStatusEffects = activeEffects,
                            Timestamp = timestamp
                        };
                    }
                }
                catch
                {
                    // Ignore transient file sharing or json read errors
                }
            }

            if (bestLeader != null)
            {
                CurrentLeader = bestLeader;
            }
            else
            {
                // Retain cached leader across transient read locks or map loading unless expired (> 60s)
                if (CurrentLeader != null && (DateTime.UtcNow - CurrentLeader.Timestamp > TimeSpan.FromSeconds(60)))
                {
                    CurrentLeader = null;
                }
            }

            if (currentMembers.Count > 0 || CurrentLeader == null)
            {
                DiscoveredMembers.Clear();
                DiscoveredMembers.AddRange(currentMembers);
            }
        }

        private static List<string> GetProfileDirectories()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(ProfileManager.DevBaseDirectory))
                {
                    string devProfiles = Path.Combine(ProfileManager.DevBaseDirectory, "profiles");
                    if (Directory.Exists(devProfiles))
                    {
                        foreach (var d in Directory.GetDirectories(devProfiles))
                        {
                            if (!list.Contains(d)) list.Add(d);
                        }
                    }
                }
                if (Directory.Exists(ProfileManager.GameBaseDirectory))
                {
                    string gameProfiles = Path.Combine(ProfileManager.GameBaseDirectory, "profiles");
                    if (Directory.Exists(gameProfiles))
                    {
                        foreach (var d in Directory.GetDirectories(gameProfiles))
                        {
                            if (!list.Contains(d)) list.Add(d);
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        public void SynchronizeInGameParty(NetworkManager netManager, ServerControllable player, float now)
        {
            if (netManager == null || player == null || !player.IsCharacterAlive) return;
            if (now - lastPartySyncTime < PartySyncInterval) return;
            lastPartySyncTime = now;

            var state = PlayerState.Instance;
            if (state == null) return;

            var cfg = BotConfigManager.Current;
            bool inGameParty = state.IsInParty;
            string inGamePartyName = state.PartyName ?? "";
            string desiredPartyName = cfg.PartyName?.Trim() ?? "";

            // 1. LEAVE / DESYNC CHECK:
            // If in an in-game party, but Party is disabled OR the in-game party name does NOT match our configured party name:
            if (inGameParty)
            {
                bool shouldLeave = !cfg.PartyEnabled || string.IsNullOrWhiteSpace(desiredPartyName) ||
                                   !string.Equals(inGamePartyName, desiredPartyName, StringComparison.OrdinalIgnoreCase);

                // Split / Ghost Party Detection:
                // If party name matches, but we are isolated in a solitary or desynced split party:
                if (!shouldLeave)
                {
                    UpdateLeaderDiscovery(now);

                    var existingMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (state.PartyMembers != null)
                    {
                        foreach (var member in state.PartyMembers.Values)
                        {
                            if (!string.IsNullOrWhiteSpace(member.PlayerName))
                                existingMemberNames.Add(member.PlayerName);
                        }
                    }

                    // Check if another fleet member with matching party name is an In-Game Leader
                    foreach (var fleetMember in DiscoveredMembers)
                    {
                        if (string.Equals(fleetMember.CharacterName, player.Name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!fleetMember.IsInGameParty || !string.Equals(fleetMember.InGamePartyName, desiredPartyName, StringComparison.OrdinalIgnoreCase)) continue;

                        if (fleetMember.IsInGameLeader && !existingMemberNames.Contains(fleetMember.CharacterName))
                        {
                            // If we are alone in our local in-game party, or we are not the configured patrol leader:
                            // We are in a solitary/ghost party and must leave so we can be invited to the true fleet party!
                            if (existingMemberNames.Count <= 1 || !cfg.IsPartyLeader)
                            {
                                shouldLeave = true;
                                BotEngine.Instance?.LogEvent($"[Party] Split party detected: In-game leader '{fleetMember.CharacterName}' is in another party. Leaving solitary/split party '{inGamePartyName}' to merge.");
                                break;
                            }
                        }
                    }
                }

                if (shouldLeave)
                {
                    if (now - lastPartyLeaveAttemptTime >= PartyLeaveInterval)
                    {
                        lastPartyLeaveAttemptTime = now;
                        netManager.LeaveParty();
                        BotEngine.Instance?.LogEvent($"[Party] Left in-game party '{inGamePartyName}' (Configured: Enabled={cfg.PartyEnabled}, Name='{desiredPartyName}').");
                    }
                    return;
                }
            }

            // If Party mode is disabled, nothing more to do
            if (!cfg.PartyEnabled || string.IsNullOrWhiteSpace(desiredPartyName))
            {
                return;
            }

            // Refresh leader & member discovery from profiles
            UpdateLeaderDiscovery(now);

            // 2. IN-GAME PARTY LEADERSHIP & INVITATIONS
            bool amIInGamePartyLeader = inGameParty && state.PartyLeader == state.PartyMemberId;

            // A) IN-GAME PARTY LEADER: Only the actual in-game leader can invite members to the party
            if (amIInGamePartyLeader)
            {
                if (string.Equals(inGamePartyName, desiredPartyName, StringComparison.OrdinalIgnoreCase))
                {
                    // Collect player names currently in party
                    var existingMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (state.PartyMembers != null)
                    {
                        foreach (var member in state.PartyMembers.Values)
                        {
                            if (!string.IsNullOrWhiteSpace(member.PlayerName))
                            {
                                existingMemberNames.Add(member.PlayerName);
                            }
                        }
                    }

                    // Check all discovered fleet members with matching party name (regardless of IsLeader config flag)
                    foreach (var fleetMember in DiscoveredMembers)
                    {
                        if (string.IsNullOrWhiteSpace(fleetMember.CharacterName)) continue;
                        if (string.Equals(fleetMember.CharacterName, player.Name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (existingMemberNames.Contains(fleetMember.CharacterName)) continue;

                        // Fleet member is not in the in-game party! Check retry interval
                        followerInviteTimes.TryGetValue(fleetMember.CharacterName, out float lastInvite);
                        if (now - lastInvite >= InviteRetryInterval)
                        {
                            followerInviteTimes[fleetMember.CharacterName] = now;
                            netManager.PartyInviteByName(fleetMember.CharacterName);
                            BotEngine.Instance?.LogEvent($"[Party] (In-Game Leader '{player.Name}') Sent party invite to fleet member '{fleetMember.CharacterName}' for party '{desiredPartyName}'.");
                        }
                    }
                }
            }
            // B) NOT IN PARTY: If configured as party leader and no fleet member has organized this party yet, create it
            else if (!inGameParty && cfg.IsPartyLeader)
            {
                // Verify no other fleet member is already in an in-game party with this name
                bool partyAlreadyExistsInFleet = false;
                foreach (var member in DiscoveredMembers)
                {
                    if (member.IsInGameParty && string.Equals(member.InGamePartyName, desiredPartyName, StringComparison.OrdinalIgnoreCase))
                    {
                        partyAlreadyExistsInFleet = true;
                        break;
                    }
                }

                if (!partyAlreadyExistsInFleet)
                {
                    // Prerequisite check: Basic Mastery >= 6
                    int basicMastery = state.KnownSkills != null && state.KnownSkills.TryGetValue(CharacterSkill.BasicMastery, out var lvl) ? (int)lvl : 0;
                    if (basicMastery < 6)
                    {
                        if (now - lastPartyCreateAttemptTime >= 10.0f)
                        {
                            lastPartyCreateAttemptTime = now;
                            BotEngine.Instance?.LogEvent($"[Party Leader] Cannot create in-game party '{desiredPartyName}': Requires Basic Mastery Level 6 (current: {basicMastery}).");
                        }
                    }
                    else if (now - lastPartyCreateAttemptTime >= PartyCreateInterval)
                    {
                        lastPartyCreateAttemptTime = now;
                        netManager.OrganizeParty(desiredPartyName);
                        BotEngine.Instance?.LogEvent($"[Party Leader] Dispatched packet to organize in-game party '{desiredPartyName}'.");
                    }
                }
            }

            // In all cases when not in party, check for pending invites
            if (!inGameParty)
            {
                CheckAndAcceptPendingInvites(netManager, desiredPartyName);
            }
        }

        public void OnPartyInviteReceived(int partyId, string leaderName, string partyName)
        {
            var cfg = BotConfigManager.Current;
            string desiredPartyName = cfg.PartyName?.Trim() ?? "";

            if (cfg.PartyEnabled && !string.IsNullOrWhiteSpace(desiredPartyName) &&
                string.Equals(partyName, desiredPartyName, StringComparison.OrdinalIgnoreCase))
            {
                BotEngine.Instance?.LogEvent($"[Party] Received party invite for '{partyName}' from '{leaderName}'. Accepting!");
                if (NetworkManager.Instance != null)
                {
                    NetworkManager.Instance.PartyAcceptInvite(partyId);
                }
                DismissInviteToast(leaderName);
            }
            else
            {
                BotEngine.Instance?.LogEvent($"[Party] Received party invite for '{partyName}' from '{leaderName}', but configured party is '{desiredPartyName}'. Ignoring stranger invite.");
                DismissInviteToast(leaderName);
            }
        }

        private static void DismissInviteToast(string leaderName)
        {
            try
            {
                var toastArea = UiManager.Instance?.ToastNotificationArea;
                if (toastArea != null)
                {
                    toastArea.OnCloseNotification(leaderName);
                }
            }
            catch { }
        }

        private void CheckAndAcceptPendingInvites(NetworkManager netManager, string desiredPartyName)
        {
            try
            {
                var toastArea = UiManager.Instance?.ToastNotificationArea;
                if (toastArea != null && toastArea.ToastZone != null)
                {
                    var toasts = toastArea.ToastZone.GetComponentsInChildren<PartyInviteToast>();
                    if (toasts != null)
                    {
                        foreach (var toast in toasts)
                        {
                            if (toast != null && !string.IsNullOrWhiteSpace(toast.PartyName))
                            {
                                if (string.Equals(toast.PartyName, desiredPartyName, StringComparison.OrdinalIgnoreCase))
                                {
                                    BotEngine.Instance?.LogEvent($"[Party] Auto-accepting pending invite for '{toast.PartyName}' from '{toast.LeaderName}' (PartyId: {toast.PartyId}).");
                                    netManager.PartyAcceptInvite(toast.PartyId);
                                    toast.OnDismiss();
                                }
                                else
                                {
                                    toast.OnDismiss();
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Coordinates following the party leader, staying tethered within follow distance,
        /// and attacking the leader's target (or withholding attack if Support mode).
        /// Returns true if party action was executed (e.g., following, assisting, or holding position).
        /// </summary>
        public bool ProcessParty(
            NetworkManager netManager,
            ServerControllable player,
            float now,
            NavigationController navigation,
            CombatController combat,
            TargetingController targeting,
            ref BotState currentState)
        {
            var cfg = BotConfigManager.Current;
            if (!cfg.PartyEnabled || string.IsNullOrWhiteSpace(cfg.PartyName))
            {
                return false;
            }

            // Refresh leader & member state
            UpdateLeaderDiscovery(now);

            // =========================================================================
            // LEADER COORDINATION & DEPARTURE SYNCHRONIZATION
            // =========================================================================
            if (cfg.IsPartyLeader)
            {
                // If leader is actively in Kafra travel, DO NOT interrupt it!
                if (navigation != null && navigation.IsKafraTravelActive)
                {
                    return false;
                }

                bool inTown = TownRoutineController.IsTownOrBaseMap(netManager.CurrentMap);

                // A) Leader in field/dungeon: if ANY party member is in town, B-Wing to town to regroup!
                if (!inTown)
                {
                    // If leader is actively traveling towards the target map, let travel proceed rather than popping wings
                    if (navigation != null && (navigation.HasActiveTravelPlan || currentState == BotState.TravelingToTargetMap))
                    {
                        return false;
                    }

                    if (IsAnyPartyMemberInTown(out var memberInTown))
                    {
                        // Only B-Wing if the member is actually running TownRoutine, not if they are just traveling/following
                        bool memberRestocking = string.Equals(memberInTown.BotState, "TownRoutine", StringComparison.OrdinalIgnoreCase);
                        if (memberRestocking)
                        {
                            int bwingId = InventoryHelper.FindFirstItemId(602, 12324); // Butterfly Wing
                            if (bwingId > 0 && now - lastTravelWingTime >= 5.0f)
                            {
                                netManager.SendUseItem(bwingId);
                                lastTravelWingTime = now;
                                Services.NpcInteractionHelper.CleanupNpcUi();
                                navigation.ResetWander();
                                combat.Clear();
                                currentState = BotState.TravelingToTargetMap;
                                BotEngine.Instance?.LogEvent($"[Party Leader] Party member '{memberInTown.CharacterName}' is in town '{memberInTown.CurrentMap}'. Used Butterfly Wing to regroup.");
                                return true;
                            }
                        }
                    }
                    return false; // Leader continues normal field operations
                }

                // B) Leader is in town: synchronize departure
                // If the leader is actively running TownRoutine (selling, restocking), let TownRoutine finish
                if (BotEngine.Instance != null && BotEngine.Instance.TownRoutine.IsActive)
                {
                    return false;
                }

                // Check if all active party members are ready in town
                if (!AreAllPartyMembersReadyInTown(netManager.CurrentMap, out string waitReason))
                {
                    if (now - lastLogTime >= 8.0f)
                    {
                        BotEngine.Instance?.LogEvent($"[Party Leader] Waiting in town '{netManager.CurrentMap}': {waitReason}.");
                        lastLogTime = now;
                    }
                    navigation.ResetWander();
                    combat.Clear();
                    currentState = BotState.WaitingForParty;
                    return true; // Hold position in town until everyone is assembled and ready!
                }

                // All members are ready! Leader can proceed with normal operations / travel to TargetMap
                return false;
            }

            // =========================================================================
            // FOLLOWER COORDINATION
            // =========================================================================
            if (CurrentLeader == null)
            {
                if (now - lastLogTime >= 10.0f)
                {
                    BotLog.Info($"[Party] Waiting to discover Party Leader for '{cfg.PartyName}'...");
                    lastLogTime = now;
                }
                return false;
            }

            // 1. Leader Dead Handling
            bool isLeaderDead = string.Equals(CurrentLeader.BotState, "PlayerDead", StringComparison.OrdinalIgnoreCase) ||
                                (!CurrentLeader.IsAlive && CurrentLeader.Hp <= 0 &&
                                 !string.Equals(CurrentLeader.BotState, "Connecting", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(CurrentLeader.BotState, "TravelingToTargetMap", StringComparison.OrdinalIgnoreCase));
            bool isSameMap = string.Equals(netManager.CurrentMap, CurrentLeader.CurrentMap, StringComparison.OrdinalIgnoreCase);

            if (isLeaderDead)
            {
                // If leader is dead, do not chase their corpse into danger.
                // If support bot is taking damage without leader protection, escape!
                // Safety guard: Never Fly Wing if Kafra travel is active, or if on town / base map!
                if (cfg.IsPartySupport && !navigation.IsKafraTravelActive && !TownRoutineController.IsTownOrBaseMap(netManager.CurrentMap))
                {
                    bool underAttack = targeting != null && targeting.HasActiveAttackers;
                    if (underAttack)
                    {
                        int wingId = InventoryHelper.FindFirstItemId(601, 12323); // Fly Wing
                        if (wingId > 0 && now - lastTravelWingTime >= 2.0f)
                        {
                            netManager.SendUseItem(wingId);
                            lastTravelWingTime = now;
                            currentState = BotState.Fleeing;
                            Services.NpcInteractionHelper.CleanupNpcUi();
                            navigation.ResetWander();
                            BotEngine.Instance?.LogEvent("[Party Support] Under attack while leader is dead! Used Fly Wing to escape.");
                            return true;
                        }
                    }
                }

                // If on same map and leader is dead, hold safe position and wait for leader to respawn
                if (isSameMap)
                {
                    currentState = BotState.FollowingPartyLeader;
                    return true;
                }
            }

            // 2. Cross-Map Following & Regrouping
            if (!isSameMap && !string.IsNullOrEmpty(CurrentLeader.CurrentMap))
            {
                bool inTown = TownRoutineController.IsTownOrBaseMap(netManager.CurrentMap);

                // A) Follower is in a field/dungeon:
                // If Leader is in town and running TownRoutine, B-Wing to regroup in town!
                if (!inTown)
                {
                    if (navigation == null || (!navigation.IsKafraTravelActive && !navigation.HasActiveTravelPlan))
                    {
                        bool shouldRegroupInTown = TownRoutineController.IsTownOrBaseMap(CurrentLeader.CurrentMap) &&
                                                   string.Equals(CurrentLeader.BotState, "TownRoutine", StringComparison.OrdinalIgnoreCase);

                        if (shouldRegroupInTown)
                        {
                            int bwingId = InventoryHelper.FindFirstItemId(602, 12324); // Butterfly Wing
                            if (bwingId > 0 && now - lastTravelWingTime >= 5.0f)
                            {
                                netManager.SendUseItem(bwingId);
                                lastTravelWingTime = now;
                                Services.NpcInteractionHelper.CleanupNpcUi();
                                navigation.ResetWander();
                                combat.Clear();
                                currentState = BotState.TravelingToTargetMap;
                                BotEngine.Instance?.LogEvent($"[Party Follower] Leader '{CurrentLeader.CharacterName}' is restocking in town '{CurrentLeader.CurrentMap}'. Used Butterfly Wing to regroup in town.");
                                return true;
                            }
                        }
                    }
                }
                else
                {
                    // B) Follower is in town!
                    // If follower is running its own TownRoutine, let TownRoutine proceed
                    if (BotEngine.Instance != null && BotEngine.Instance.TownRoutine.IsActive)
                    {
                        return false;
                    }

                    // If Leader is dead / respawning, hold position in town
                    if (!CurrentLeader.IsAlive || CurrentLeader.Hp <= 0)
                    {
                        if (now - lastLogTime >= 8.0f)
                        {
                            BotEngine.Instance?.LogEvent($"[Party Follower] Leader '{CurrentLeader.CharacterName}' is dead/respawning. Holding position...");
                            lastLogTime = now;
                        }
                        navigation.ResetWander();
                        combat.Clear();
                        currentState = BotState.WaitingForParty;
                        return true;
                    }

                    // If Leader is in town and actively in TownRoutine, wait for leader to finish restocking
                    if (TownRoutineController.IsTownOrBaseMap(CurrentLeader.CurrentMap) &&
                        string.Equals(CurrentLeader.BotState, "TownRoutine", StringComparison.OrdinalIgnoreCase))
                    {
                        if (now - lastLogTime >= 8.0f)
                        {
                            BotEngine.Instance?.LogEvent($"[Party Follower] In town '{netManager.CurrentMap}'. Waiting for leader '{CurrentLeader.CharacterName}' to finish town routine...");
                            lastLogTime = now;
                        }
                        navigation.ResetWander();
                        combat.Clear();
                        currentState = BotState.WaitingForParty;
                        return true;
                    }
                }

                Vector2Int leaderPos = new Vector2Int(CurrentLeader.PositionX, CurrentLeader.PositionY);
                if (navigation.ProcessTravel(netManager, player, now, ref currentState, targetCellPos: leaderPos, destinationMapOverride: CurrentLeader.CurrentMap))
                {
                    currentState = BotState.FollowingPartyLeader;
                    if (now - lastLogTime >= 5.0f)
                    {
                        BotLog.Info($"[Party] Traveling cross-map to rejoin leader '{CurrentLeader.CharacterName}' on '{CurrentLeader.CurrentMap}'...");
                        lastLogTime = now;
                    }
                    return true;
                }
                return false;
            }

            // 3. Same-Map Leader Following & Tethering
            ServerControllable leaderEntity = FindLeaderEntity(netManager, CurrentLeader.CharacterName, CurrentLeader.Profile);
            if (leaderEntity != null)
            {
                // In-game live entity refresh: update leader coordinates & timestamp directly from live game entity!
                CurrentLeader.PositionX = leaderEntity.CellPosition.x;
                CurrentLeader.PositionY = leaderEntity.CellPosition.y;
                CurrentLeader.Timestamp = DateTime.UtcNow;
            }

            Vector2Int leaderCellPos = leaderEntity != null ? leaderEntity.CellPosition : new Vector2Int(CurrentLeader.PositionX, CurrentLeader.PositionY);
            float distToLeader = Vector2.Distance(player.CellPosition, leaderCellPos);
            float followDist = cfg.PartyFollowDistance > 0 ? cfg.PartyFollowDistance : 3.0f;

            // 3.5 Party Looter Priority: Pick up ground loot dropped by defeated enemies before combat or tethering
            if (cfg.IsPartyLooter && distToLeader <= 16.0f && BotEngine.Instance != null)
            {
                if (!SkillController.IsPlayerCasting(player))
                {
                    if (BotEngine.Instance.Loot.ProcessLoot(netManager, player, now, ref currentState, leaderCellPos, 16.0f))
                    {
                        return true;
                    }
                }
            }

            // 4. Combat Mode vs Support Mode
            if (!cfg.IsPartySupport && !ArrowHelper.IsBowUserOutOfAmmo(player))
            {
                var target = ResolveLeaderTarget(netManager, player, leaderCellPos, combat, targeting, now);
                if (target != null)
                {
                    // If follower is pulled excessively far from leader (> 14 tiles), disengage combat to regroup
                    if (distToLeader > 14.0f)
                    {
                        combat.Clear();
                    }
                    else
                    {
                        combat.CurrentLockedTargetId = target.Id;
                        combat.ExecuteCombatAction(netManager, player, target, now, navigation, targeting, ref currentState);
                        return true;
                    }
                }
                else
                {
                    if (combat.CurrentLockedTargetId != -1)
                    {
                        combat.Clear();
                    }
                }
            }
            else
            {
                // Support Mode: Strictly NEVER initiate attacks on monsters
                combat.Clear();
            }

            // 5. Stay Tethered to Leader (Paced & Smooth Follow Movement)
            bool isMoving = player.IsMoving || player.IsWalking;
            float startMoveThreshold = isMoving ? followDist : (followDist + 1.2f);

            // If player is actively casting a skill and within tether distance (<= 8.0 tiles), hold position until cast completes!
            if (SkillController.IsPlayerCasting(player) && distToLeader <= 8.0f)
            {
                currentState = BotState.FollowingPartyLeader;
                return true;
            }

            if (distToLeader > startMoveThreshold)
            {
                // Stuck detection while following
                if (player.CellPosition == lastFollowPlayerPos)
                {
                    followStuckTimer += Time.deltaTime;
                    if (followStuckTimer > 1.8f)
                    {
                        navigation.ResetWander();
                        followStuckTimer = 0f;
                        lastFollowMoveTime = 0f;
                        BotEngine.Instance?.LogEvent($"[Party] Follower path hindered at ({player.CellPosition.x}, {player.CellPosition.y}); resetting follow route.");
                    }
                }
                else
                {
                    lastFollowPlayerPos = player.CellPosition;
                    followStuckTimer = 0f;
                }

                // Movement pacing: never spam move packets faster than 0.25s
                if (now - lastFollowMoveTime >= 0.25f)
                {
                    float leaderShift = Vector2.Distance(leaderCellPos, lastDispatchedLeaderPos);
                    // If player is already walking toward leader, only re-dispatch if leader moved >= 2.0 tiles
                    if (!isMoving || leaderShift >= 2.0f)
                    {
                        if (navigation.NavigateTowards(player.CellPosition, leaderCellPos, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true))
                        {
                            lastFollowMoveTime = now;
                            lastDispatchedLeaderPos = leaderCellPos;
                        }
                    }
                }

                currentState = BotState.FollowingPartyLeader;
                return true;
            }

            // 6. Within follow distance of leader
            currentState = BotState.FollowingPartyLeader;
            return true;
        }

        private ServerControllable ResolveLeaderTarget(
            NetworkManager netManager,
            ServerControllable player,
            Vector2Int leaderCellPos,
            CombatController combat,
            TargetingController targeting,
            float now)
        {
            if (netManager == null || netManager.EntityList == null) return null;

            float myAttackRange = CombatController.GetEffectiveAttackRange();
            bool isMeleeFollower = myAttackRange <= 2.0f;

            bool IsValidCandidate(ServerControllable candidate, out float distFromLeader)
            {
                distFromLeader = float.MaxValue;
                if (candidate == null || !candidate.IsCharacterAlive || candidate.Hp <= 0 || candidate.IsAlly) return false;
                if (candidate.CharacterType != CharacterType.Monster) return false;

                if (targeting != null && targeting.IsUnreachable(candidate.Id)) return false;

                if (BotConfigManager.Current.AutoAvoidMonsters &&
                    BotConfigManager.Current.MonsterAvoidanceList != null &&
                    BotConfigManager.Current.MonsterAvoidanceList.Contains(candidate.Name))
                {
                    return false;
                }

                distFromLeader = Vector2.Distance(leaderCellPos, candidate.CellPosition);
                if (distFromLeader > 12.0f) return false;

                // Smart Melee Formation Assist:
                // If follower is melee (attack range <= 2.0), do not sprint across the field to chase distant targets
                // that a ranged leader is shooting from afar.
                if (isMeleeFollower && combat.CurrentLockedTargetId != candidate.Id)
                {
                    float distFromFollower = Vector2.Distance(player.CellPosition, candidate.CellPosition);
                    bool isTargetAttackingParty = (targeting != null && targeting.IsAttackingPlayer(candidate.Id)) ||
                                                  realtimeLeaderAttackerId == candidate.Id;

                    if (distFromFollower > 6.0f && distFromLeader > 3.5f && !isTargetAttackingParty)
                    {
                        return false; // Stay in formation with leader instead of chasing distant mob!
                    }
                }

                return true;
            }

            // 1. COMBAT PERSISTENCE:
            // If follower is already fighting a target, verify if it's still alive and near leader (<= 12 tiles)
            if (combat.CurrentLockedTargetId > 0)
            {
                if (netManager.EntityList.TryGetValue(combat.CurrentLockedTargetId, out var existingTarget) &&
                    IsValidCandidate(existingTarget, out _))
                {
                    return existingTarget;
                }
            }

            // 2. REAL-TIME LEADER TARGET (from attack motion hook)
            if (realtimeLeaderTargetId > 0 && (now - realtimeLeaderTargetTime <= 4.0f))
            {
                if (netManager.EntityList.TryGetValue(realtimeLeaderTargetId, out var rtTarget) &&
                    IsValidCandidate(rtTarget, out _))
                {
                    return rtTarget;
                }
            }

            // 3. SYNCED LEADER TARGET (from bot_status.json)
            if (CurrentLeader.TargetEntityId > 0)
            {
                if (netManager.EntityList.TryGetValue(CurrentLeader.TargetEntityId, out var syncTarget) &&
                    IsValidCandidate(syncTarget, out _))
                {
                    return syncTarget;
                }
            }

            // 4. REAL-TIME LEADER ATTACKER (monster hitting leader)
            if (realtimeLeaderAttackerId > 0 && (now - realtimeLeaderAttackerTime <= 4.0f))
            {
                if (netManager.EntityList.TryGetValue(realtimeLeaderAttackerId, out var atkTarget) &&
                    IsValidCandidate(atkTarget, out _))
                {
                    return atkTarget;
                }
            }

            // 5. PROXIMITY ASSIST FALLBACK:
            // Scan monsters within 3.5 tiles of leader that are damaged or engaged
            ServerControllable bestProximityTarget = null;
            float bestDist = float.MaxValue;

            try
            {
                foreach (var kvp in netManager.EntityList)
                {
                    var entity = kvp.Value;
                    if (entity == null || entity.CharacterType != CharacterType.Monster) continue;
                    if (!IsValidCandidate(entity, out float distFromLeader)) continue;

                    if (distFromLeader <= 3.5f && (entity.Hp < entity.MaxHp || distFromLeader <= 2.0f))
                    {
                        if (distFromLeader < bestDist)
                        {
                            bestDist = distFromLeader;
                            bestProximityTarget = entity;
                        }
                    }
                }
            }
            catch { }

            return bestProximityTarget;
        }

        public static ServerControllable FindPartyMemberEntity(NetworkManager netManager, string characterName, string profileName)
        {
            if (netManager == null || netManager.EntityList == null) return null;

            try
            {
                foreach (var kvp in netManager.EntityList)
                {
                    var entity = kvp.Value;
                    if (entity == null || entity.Id == netManager.PlayerId) continue;
                    if (entity.CharacterType != CharacterType.Player) continue;

                    if (!string.IsNullOrEmpty(characterName) && string.Equals(entity.Name, characterName, StringComparison.OrdinalIgnoreCase))
                    {
                        return entity;
                    }
                    if (!string.IsNullOrEmpty(profileName) && string.Equals(entity.Name, profileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return entity;
                    }
                }
            }
            catch
            {
                // EntityList modified concurrently during network despawn/respawn
            }

            return null;
        }

        private ServerControllable FindLeaderEntity(NetworkManager netManager, string leaderCharacterName, string leaderProfile)
        {
            return FindPartyMemberEntity(netManager, leaderCharacterName, leaderProfile);
        }
    }
}
