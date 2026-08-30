using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RebuildBotPlugin.Models
{
    public enum SkillTargetType
    {
        Enemy,
        Self,
        Party,
        Ground
    }

    public enum SkillPlacementType
    {
        DirectOnEnemy,
        UnderSelf,
        BetweenSelfAndEnemy,
        AheadOfEnemy
    }

    public enum SkillTriggerType
    {
        Combat,             // Repeated/spammed in active combat when off cooldown and conditions met
        Opener,             // Cast once upon engaging a new monster
        BuffMaintenance,    // Maintained continuously whenever the corresponding buff is missing
        HpBelowPercent,     // Emergency recovery when HP drops below threshold
        MobCluster          // Cast when min enemies in radius is reached (AOE)
    }

    public class SkillRule
    {
        public string Skill { get; set; } = "";
        public int Level { get; set; } = 0; // 0 = max available
        
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SkillTargetType Target { get; set; } = SkillTargetType.Enemy;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SkillPlacementType Placement { get; set; } = SkillPlacementType.DirectOnEnemy;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SkillTriggerType Trigger { get; set; } = SkillTriggerType.Combat;

        public int HpBelowPercent { get; set; } = 0;       // For HpBelowPercent trigger (e.g. 60)
        public int MinSpPercent { get; set; } = 20;         // Preserve minimum SP reserve %
        public int MinTargetHp { get; set; } = 0;           // Don't waste costly skill on near-dead mob
        public int MinEnemiesInRange { get; set; } = 1;     // For MobCluster AOE skills
        public float CooldownSeconds { get; set; } = 1.0f;  // Rate-limiting cadence between casts
        public List<string> TargetMonsters { get; set; } = new(); // Whitelist filter (empty = any)
        public bool Enabled { get; set; } = true;
    }
}
