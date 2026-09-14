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
            return Services.SkillResolver.TryResolveSkill(Skill, out skill, out _);
        }

        public bool TryGetSkill(out CharacterSkill skill, out string canonicalName)
        {
            return Services.SkillResolver.TryResolveSkill(Skill, out skill, out canonicalName);
        }
    }
}
