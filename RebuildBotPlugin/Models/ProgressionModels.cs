using System;
using RebuildSharedData.Enum;
using RebuildSharedData.Enum.EntityStats;

namespace RebuildBotPlugin.Models
{
    public class StatBuildGoal
    {
        public string Stat { get; set; } = string.Empty;
        public int Target { get; set; }

        public bool TryGetStatIndex(out int statIndex, out PlayerStat playerStat)
        {
            statIndex = -1;
            playerStat = PlayerStat.Str;

            if (string.IsNullOrWhiteSpace(Stat)) return false;

            string s = Stat.Trim().ToLowerInvariant();
            switch (s)
            {
                case "str":
                case "strength":
                    statIndex = 0;
                    playerStat = PlayerStat.Str;
                    return true;

                case "agi":
                case "agility":
                    statIndex = 1;
                    playerStat = PlayerStat.Agi;
                    return true;

                case "vit":
                case "vitality":
                    statIndex = 2;
                    playerStat = PlayerStat.Vit;
                    return true;

                case "int":
                case "intelligence":
                    statIndex = 3;
                    playerStat = PlayerStat.Int;
                    return true;

                case "dex":
                case "dexterity":
                    statIndex = 4;
                    playerStat = PlayerStat.Dex;
                    return true;

                case "luk":
                case "luck":
                    statIndex = 5;
                    playerStat = PlayerStat.Luk;
                    return true;

                default:
                    return false;
            }
        }
    }

    public class SkillBuildGoal
    {
        public string Skill { get; set; } = string.Empty;
        public int Target { get; set; }

        public bool TryGetSkill(out CharacterSkill skill)
        {
            skill = CharacterSkill.None;
            if (string.IsNullOrWhiteSpace(Skill)) return false;

            string s = Skill.Trim();
            string clean = s.Replace(" ", "").Replace("_", "").Replace("-", "").Replace("'", "");

            // Common aliases
            if (clean.Equals("BasicSkill", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("BasicMastery", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("Basic", StringComparison.OrdinalIgnoreCase))
            {
                skill = CharacterSkill.BasicMastery;
                return true;
            }

            if (clean.Equals("IncreaseAgi", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("IncreaseAgility", StringComparison.OrdinalIgnoreCase))
            {
                skill = CharacterSkill.IncreaseAgility;
                return true;
            }

            if (clean.Equals("OwlsEye", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("OwlEye", StringComparison.OrdinalIgnoreCase))
            {
                skill = CharacterSkill.OwlEye;
                return true;
            }

            if (clean.Equals("VulturesEye", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("VultureEye", StringComparison.OrdinalIgnoreCase))
            {
                skill = CharacterSkill.VultureEye;
                return true;
            }

            // Try parsing enum name directly (e.g., "Bash", "DoubleAttack", "IncreaseHpRecovery")
            if (Enum.TryParse(clean, true, out skill))
            {
                return true;
            }

            // Also support integer ID as string (e.g., "1", "42")
            if (byte.TryParse(s, out byte byteId) && Enum.IsDefined(typeof(CharacterSkill), byteId))
            {
                skill = (CharacterSkill)byteId;
                return true;
            }

            return false;
        }
    }
}
