using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RebuildOrchestrator.Models
{
    public class BotProfileInfo
    {
        public string ProfileName { get; set; } = "";
        public string AccountId { get; set; } = "";
        public string Username { get; set; } = "";
        public int CharacterSlot { get; set; } = 0;
        public bool IsRunning { get; set; } = false;
        public bool IsWindowVisible { get; set; } = true;
        public int? ProcessId { get; set; }
        public double CpuPercent { get; set; } = 0.0;
        public double RamMegabytes { get; set; } = 0.0;
        public DateTime? ProcessStartTime { get; set; }
        public bool PartyEnabled { get; set; } = false;
        public string PartyName { get; set; } = "";
        public bool IsPartyLeader { get; set; } = false;
        public bool IsPartySupport { get; set; } = false;
        public bool IsPartyLooter { get; set; } = false;
        public bool IsInGameParty { get; set; } = false;
        public string InGamePartyName { get; set; } = "";
        public bool IsInGamePartyLeader { get; set; } = false;
        public string InGamePartyLeaderName { get; set; } = "";
        public bool IsDistributor { get; set; } = false;
        public string DistributorMap { get; set; } = "";
        public int DistributorX { get; set; } = 0;
        public int DistributorY { get; set; } = 0;
        public bool IsVendingOpen { get; set; } = false;
        public bool IsReadyForDonations { get; set; } = false;
        public string VendingShopTitle { get; set; } = "";
        public BotStatusData? Status { get; set; }
        public MacroStatusData? MacroStatus { get; set; }
    }

    public class BotStatusData
    {
        public string Profile { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public string JobName { get; set; } = "";
        public int Level { get; set; } = 0;
        public int JobLevel { get; set; } = 0;
        public int Hp { get; set; } = 0;
        public int MaxHp { get; set; } = 1;
        public int Sp { get; set; } = 0;
        public int MaxSp { get; set; } = 1;
        public int Weight { get; set; } = 0;
        public int MaxWeight { get; set; } = 1;
        public int Zeny { get; set; } = 0;
        public string CurrentMap { get; set; } = "";
        public int PositionX { get; set; } = 0;
        public int PositionY { get; set; } = 0;
        public string BotState { get; set; } = "Offline";
        public bool IsBotEnabled { get; set; } = false;
        public long BaseExp { get; set; } = 0;
        public long MaxBaseExp { get; set; } = 0;
        public double BaseExpPerHour { get; set; } = 0.0;
        public double JobExpPerHour { get; set; } = 0.0;
        public long SessionBaseExpGained { get; set; } = 0;
        public long SessionJobExpGained { get; set; } = 0;
        public int MonstersKilled { get; set; } = 0;
        public double SessionUptimeSeconds { get; set; } = 0.0;
        public bool HasActiveMacro { get; set; } = false;
        public string CurrentMacro { get; set; } = "";
        public int? ProcessId { get; set; }
        public bool PartyEnabled { get; set; } = false;
        public string PartyName { get; set; } = "";
        public bool IsPartyLeader { get; set; } = false;
        public bool IsPartySupport { get; set; } = false;
        public bool IsPartyLooter { get; set; } = false;
        public bool IsInGameParty { get; set; } = false;
        public string InGamePartyName { get; set; } = "";
        public bool IsInGamePartyLeader { get; set; } = false;
        public string InGamePartyLeaderName { get; set; } = "";
        public bool IsDistributor { get; set; } = false;
        public string DistributorMap { get; set; } = "";
        public int DistributorX { get; set; } = 0;
        public int DistributorY { get; set; } = 0;
        public bool IsVendingOpen { get; set; } = false;
        public bool IsReadyForDonations { get; set; } = false;
        public string VendingShopTitle { get; set; } = "";
        public int TargetEntityId { get; set; } = -1;
        public string TargetName { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class MasterItemRule
    {
        public string ItemName { get; set; } = "";
        public string Disposition { get; set; } = "Keep"; // Keep, Store, Sell
        public int? MaxCount { get; set; } = null; // null or <= 0 is unlimited
    }

    public class MacroStatusData
    {
        public string Profile { get; set; } = "";
        public bool HasActiveMacro { get; set; } = false;
        public int QueueCount { get; set; } = 0;
        public object? CurrentAction { get; set; }
        public List<object> RecentHistory { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class TileLayoutRequest
    {
        public string LayoutType { get; set; } = "2x2"; // "2x2", "3x2", "1x2", "stack", "left-main"
        public int MonitorIndex { get; set; } = 0;
        public List<string>? Profiles { get; set; }
    }

    public class LaunchBotRequest
    {
        public string ProfileName { get; set; } = "";
        public string? AccountId { get; set; }
        public bool LowSpec { get; set; } = false;
        public bool Hidden { get; set; } = true;
        public int? TargetFps { get; set; } = 30;
    }

    public class MacroEnqueueRequest
    {
        public string ActionType { get; set; } = "";
        public string? ItemName { get; set; }
        public string? TargetItemName { get; set; }
        public string? CardName { get; set; }
        public int Quantity { get; set; } = 1;
        public int TargetRefineLevel { get; set; } = 4;
        public bool StopAtSafeLimit { get; set; } = true;
        public string? SlotName { get; set; }
        public string? TargetMap { get; set; }
        public string? VendorName { get; set; }
        public int VendorX { get; set; } = 0;
        public int VendorY { get; set; } = 0;
    }

    public class MonitorInfoData
    {
        public int Index { get; set; }
        public string DeviceName { get; set; } = "";
        public bool IsPrimary { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int WorkAreaLeft { get; set; }
        public int WorkAreaTop { get; set; }
        public int WorkAreaWidth { get; set; }
        public int WorkAreaHeight { get; set; }
    }

    public class FleetOverviewResponse
    {
        public int TotalBots { get; set; }
        public int RunningBots { get; set; }
        public long TotalZeny { get; set; }
        public double TotalBaseExpPerHour { get; set; }
        public int TotalKills { get; set; }
        public List<BotProfileInfo> Profiles { get; set; } = new();
        public List<MonitorInfoData> Monitors { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class FleetLogEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Profile { get; set; } = "";
        public string Level { get; set; } = "Info"; // Info, Success, Warning, Error
        public string Message { get; set; } = "";
    }

    public class PartyUpdateRequest
    {
        public string ProfileName { get; set; } = "";
        public bool PartyEnabled { get; set; } = false;
        public string PartyName { get; set; } = "";
        public bool IsPartyLeader { get; set; } = false;
        public bool IsPartySupport { get; set; } = false;
        public bool IsPartyLooter { get; set; } = false;
    }

    public class AddBotRequest
    {
        public string AccountId { get; set; } = "";
        public string Password { get; set; } = "";
        public bool IsNewAccount { get; set; } = false;
        public string CharacterName { get; set; } = "";
        public int CharacterSlot { get; set; } = -1; // -1 = auto next slot
        public string Gender { get; set; } = "Male"; // "Male" or "Female"
        public List<int> StartingStats { get; set; } = new() { 5, 5, 5, 5, 5, 8 }; // Str, Agi, Vit, Int, Dex, Luk
        public string TargetJob { get; set; } = "Novice";
        public List<StatMilestoneDto> StatBuildPlan { get; set; } = new();
        public List<SkillMilestoneDto> SkillBuildPlan { get; set; } = new();
    }

    public class StatMilestoneDto
    {
        public string Stat { get; set; } = "";
        public int Target { get; set; } = 1;
    }

    public class SkillMilestoneDto
    {
        public string Skill { get; set; } = "";
        public int Target { get; set; } = 1;
    }

    public class AccountSummaryItem
    {
        public string AccountId { get; set; } = "";
        public string Username { get; set; } = "";
        public List<CharacterSummaryItem> Characters { get; set; } = new();
        public int NextAvailableSlot { get; set; } = 0; // 0, 1, 2, or -1 if full
    }

    public class CharacterSummaryItem
    {
        public string Name { get; set; } = "";
        public int Slot { get; set; } = 0;
        public string Gender { get; set; } = "Male";
    }
}

