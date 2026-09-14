using System;
using System.Collections.Generic;
using System.Text;
using RebuildSharedData.Enum;

namespace RebuildBotPlugin.Services
{
    public class SkillMetadata
    {
        public CharacterSkill Skill { get; }
        public string CanonicalName { get; }
        public string EnumName { get; }

        public SkillMetadata(CharacterSkill skill, string canonicalName)
        {
            Skill = skill;
            CanonicalName = canonicalName;
            EnumName = skill.ToString();
        }

        public override string ToString() => $"{CanonicalName} ({EnumName})";
    }

    /// <summary>
    /// Provides skill name normalization, alias matching, and autocorrect for all
    /// Ragnarok Rebuild skills, community class guides (e.g. dodsrv.com), and common abbreviations.
    /// </summary>
    public static class SkillResolver
    {
        private static readonly Dictionary<string, SkillMetadata> NormalizedMap = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<CharacterSkill, SkillMetadata> CanonicalMap = new();

        static SkillResolver()
        {
            // 1. Register base CharacterSkill enum entries
            foreach (CharacterSkill skill in Enum.GetValues(typeof(CharacterSkill)))
            {
                if (skill == CharacterSkill.None) continue;
                string enumName = skill.ToString();
                var meta = new SkillMetadata(skill, SplitCamelCase(enumName));
                Register(meta, enumName);
                if (!CanonicalMap.ContainsKey(skill))
                {
                    CanonicalMap[skill] = meta;
                }
            }

            // 2. Novice Skills
            RegisterSkill(CharacterSkill.BasicMastery, "Basic Mastery", "Basic Skill", "Basic", "BasicMastery", "NV_BASIC");
            RegisterSkill(CharacterSkill.FirstAid, "First Aid", "FirstAid", "NV_FIRSTAID");

            // 3. Swordsman & Knight Skills
            RegisterSkill(CharacterSkill.SwordMastery, "Sword Mastery", "1H Sword Mastery", "One Hand Sword Mastery");
            RegisterSkill(CharacterSkill.TwoHandSwordMastery, "Two-Hand Sword Mastery", "Two Hand Sword Mastery", "2H Sword Mastery", "2HSwordMastery");
            RegisterSkill(CharacterSkill.IncreasedHPRecovery, "Improved HP Recovery", "Increase HP Recovery", "Increased HP Recovery", "HP Recovery", "IncreaseHPRecovery", "ImprovedHPRecovery", "HPR");
            RegisterSkill(CharacterSkill.Bash, "Bash");
            RegisterSkill(CharacterSkill.MagnumBreak, "Magnum Break", "MagnumBreak", "MB");
            RegisterSkill(CharacterSkill.Provoke, "Provoke");
            RegisterSkill(CharacterSkill.Endure, "Endure");
            RegisterSkill(CharacterSkill.ChargeAttack, "Charge Attack", "ChargeAttack");
            RegisterSkill(CharacterSkill.TwoHandQuicken, "Two-Hand Quicken", "Two Hand Quicken", "2HQ", "2H Quicken", "TwoHandQuicken");
            RegisterSkill(CharacterSkill.CounterAttack, "Counter Attack", "Auto Counter", "AutoCounter", "Counter");
            RegisterSkill(CharacterSkill.BowlingBash, "Bowling Bash", "BowlingBash", "BB");
            RegisterSkill(CharacterSkill.PecoPecoRiding, "Peco Peco Riding", "Peco Riding", "Riding");
            RegisterSkill(CharacterSkill.CavalierMastery, "Cavalier Mastery", "Cavalry Mastery");
            RegisterSkill(CharacterSkill.SpearMastery, "Spear Mastery");
            RegisterSkill(CharacterSkill.Pierce, "Pierce");
            RegisterSkill(CharacterSkill.SpearStab, "Spear Stab");
            RegisterSkill(CharacterSkill.BrandishSpear, "Brandish Spear", "Brandish");
            RegisterSkill(CharacterSkill.SpearBoomerang, "Spear Boomerang", "SpearBoomerang");

            // 4. Archer & Hunter Skills
            RegisterSkill(CharacterSkill.OwlEye, "Owl's Eye", "Owl Eye", "Owls Eye", "OwlEye", "OwlsEye");
            RegisterSkill(CharacterSkill.VultureEye, "Vulture's Eye", "Vulture Eye", "Vultures Eye", "VultureEye", "VulturesEye");
            RegisterSkill(CharacterSkill.DoubleStrafe, "Double Strafe", "DoubleStrafe", "DS");
            RegisterSkill(CharacterSkill.ArrowShower, "Arrow Shower", "ArrowShower", "AS");
            RegisterSkill(CharacterSkill.ImproveConcentration, "Improve Concentration", "Improved Concentration", "Attention Concentrate", "Concentration", "ImproveConcentration", "Concentrate");
            RegisterSkill(CharacterSkill.ChargeArrow, "Charge Arrow", "Arrow Repel", "ChargeArrow");
            RegisterSkill(CharacterSkill.BeastBane, "Beast Bane", "BeastBane");
            RegisterSkill(CharacterSkill.FalconMastery, "Falcon Mastery");
            RegisterSkill(CharacterSkill.BlitzBeat, "Blitz Beat", "BlitzBeat");
            RegisterSkill(CharacterSkill.SteelCrow, "Steel Crow", "SteelCrow");
            RegisterSkill(CharacterSkill.AnkleSnare, "Ankle Snare", "AnkleSnare");
            RegisterSkill(CharacterSkill.LandMine, "Land Mine", "LandMine");
            RegisterSkill(CharacterSkill.RemoveTrap, "Remove Trap", "RemoveTrap");
            RegisterSkill(CharacterSkill.SpringTrap, "Spring Trap", "SpringTrap");
            RegisterSkill(CharacterSkill.SkidTrap, "Skid Trap", "SkidTrap");
            RegisterSkill(CharacterSkill.FreezingTrap, "Freezing Trap", "FreezingTrap");
            RegisterSkill(CharacterSkill.Sandman, "Sandman");
            RegisterSkill(CharacterSkill.BlastMine, "Blast Mine", "BlastMine");
            RegisterSkill(CharacterSkill.ClaymoreTrap, "Claymore Trap", "ClaymoreTrap");
            RegisterSkillByName("ShockwaveTrap", "Shockwave Trap", "ShockwaveTrap");
            RegisterSkill(CharacterSkill.TalkieBox, "Talkie Box", "TalkieBox");
            RegisterSkill(CharacterSkill.PhantasmicArrow, "Phantasmic Arrow", "PhantasmicArrow");
            RegisterSkill(CharacterSkill.Detect, "Detect");
            RegisterSkill(CharacterSkill.Flasher, "Flasher");

            // 5. Mage & Wizard Skills
            RegisterSkill(CharacterSkill.IncreaseSPRecovery, "Improved Spiritual Recovery", "Increase SP Recovery", "Increased SP Recovery", "SP Recovery", "IncreaseSPRecovery", "ImprovedSpiritualRecovery", "SPR");
            RegisterSkill(CharacterSkill.FireBolt, "Fire Bolt", "FireBolt", "FB");
            RegisterSkill(CharacterSkill.FireBall, "Fireball", "Fire Ball", "FireBall");
            RegisterSkill(CharacterSkill.FireWall, "Fire Wall", "FireWall", "FW");
            RegisterSkill(CharacterSkill.ColdBolt, "Cold Bolt", "ColdBolt", "CB");
            RegisterSkill(CharacterSkill.FrostDiver, "Frost Diver", "FrostDiver", "FD");
            RegisterSkill(CharacterSkill.LightningBolt, "Lightning Bolt", "LightningBolt", "LB");
            RegisterSkill(CharacterSkill.ThunderStorm, "Thunderstorm", "Thunder Storm", "ThunderStorm", "TS");
            RegisterSkill(CharacterSkill.NapalmBeat, "Napalm Beat", "NapalmBeat", "NB");
            RegisterSkill(CharacterSkill.SoulStrike, "Soul Strike", "SoulStrike", "SS");
            RegisterSkill(CharacterSkill.SafetyWall, "Safety Wall", "SafetyWall", "SW");
            RegisterSkill(CharacterSkill.StoneCurse, "Stone Curse", "StoneCurse", "SC");
            RegisterSkill(CharacterSkill.Sight, "Sight");
            RegisterSkill(CharacterSkill.EnergyCoat, "Energy Coat", "EnergyCoat", "EC");
            RegisterSkill(CharacterSkill.EarthSpike, "Earth Spike", "EarthSpike");
            RegisterSkill(CharacterSkill.HeavensDrive, "Heaven's Drive", "Heavens Drive", "HeavensDrive", "HD");
            RegisterSkill(CharacterSkill.WaterBall, "Water Ball", "Waterball", "WaterBall", "WB");
            RegisterSkill(CharacterSkill.JupitelThunder, "Jupitel Thunder", "JupitelThunder", "JT");
            RegisterSkill(CharacterSkill.LordOfVermilion, "Lord of Vermilion", "Lord Of Vermilion", "LordOfVermilion", "LoV");
            RegisterSkill(CharacterSkill.MeteorStorm, "Meteor Storm", "MeteorStorm", "MS");
            RegisterSkill(CharacterSkill.StormGust, "Storm Gust", "StormGust", "SG");
            RegisterSkill(CharacterSkill.Quagmire, "Quagmire", "QM");
            RegisterSkill(CharacterSkill.FrostNova, "Frost Nova", "FrostNova", "FN");
            RegisterSkill(CharacterSkill.FirePillar, "Fire Pillar", "FirePillar", "FP");
            RegisterSkill(CharacterSkill.IceWall, "Ice Wall", "IceWall", "IW");
            RegisterSkill(CharacterSkill.Sightrasher, "Sightrasher");
            RegisterSkill(CharacterSkill.Sense, "Sense");

            // 6. Thief & Assassin Skills
            RegisterSkill(CharacterSkill.DoubleAttack, "Double Attack", "DoubleAttack", "DA");
            RegisterSkill(CharacterSkill.ImproveDodge, "Improve Dodge", "Improved Dodge", "Increase Dodge", "ImproveDodge", "Dodge");
            RegisterSkill(CharacterSkill.Envenom, "Envenom");
            RegisterSkill(CharacterSkill.Detoxify, "Detoxify", "Detox");
            RegisterSkill(CharacterSkill.Steal, "Steal");
            RegisterSkill(CharacterSkill.Hiding, "Hiding", "Hide");
            RegisterSkill(CharacterSkill.BackSlide, "Back Slide", "Backslide", "BackSlide");
            RegisterSkill(CharacterSkill.SandAttack, "Sand Attack", "SandAttack");
            RegisterSkill(CharacterSkill.ThrowStone, "Stone Fling", "Throw Stone", "ThrowStone", "StoneFling");
            RegisterSkill(CharacterSkill.FindStone, "Find Stone", "Pick Stone", "FindStone", "PickStone");
            RegisterSkill(CharacterSkill.SonicBlow, "Sonic Blow", "SonicBlow", "SB");
            RegisterSkill(CharacterSkill.EnchantPoison, "Enchant Poison", "EnchantPoison", "EP");
            RegisterSkill(CharacterSkill.Cloaking, "Cloaking", "Cloak");
            RegisterSkill(CharacterSkill.KatarMastery, "Katar Mastery", "KatarMastery");
            RegisterSkill(CharacterSkill.RightHandMastery, "Right-Hand Mastery", "Righthand Mastery", "Right Hand Mastery");
            RegisterSkill(CharacterSkill.LeftHandMastery, "Left-Hand Mastery", "Lefthand Mastery", "Left Hand Mastery");
            RegisterSkill(CharacterSkill.Grimtooth, "Grimtooth", "Grim");
            RegisterSkill(CharacterSkill.PoisonReact, "Poison React", "PoisonReact");
            RegisterSkill(CharacterSkill.VenomDust, "Venom Dust", "VenomDust");
            RegisterSkill(CharacterSkill.VenomSplasher, "Venom Splasher", "VenomSplasher");
            RegisterSkill(CharacterSkill.SonicAcceleration, "Sonic Acceleration", "SonicAcceleration");
            RegisterSkill(CharacterSkill.VenomKnife, "Venom Knife", "VenomKnife");

            // 7. Acolyte & Priest Skills
            RegisterSkill(CharacterSkill.DivineProtection, "Divine Protection", "DivineProtection", "DP");
            RegisterSkill(CharacterSkill.DemonBane, "Demon Bane", "DemonBane", "DB");
            RegisterSkill(CharacterSkill.Heal, "Heal");
            RegisterSkill(CharacterSkill.Angelus, "Angelus");
            RegisterSkill(CharacterSkill.Blessing, "Blessing", "Bless");
            RegisterSkill(CharacterSkill.IncreaseAgility, "Increase Agility", "Increase Agi", "Inc Agi", "IncreaseAgility", "IncAgi", "Agi Up", "IA");
            RegisterSkill(CharacterSkill.DecreaseAgility, "Decrease Agility", "Decrease Agi", "Dec Agi", "DecreaseAgility", "DecAgi", "DAgi");
            RegisterSkill(CharacterSkill.Cure, "Cure");
            RegisterSkill(CharacterSkill.Ruwach, "Ruwach");
            RegisterSkill(CharacterSkill.Teleport, "Teleport", "Tele");
            RegisterSkill(CharacterSkill.Return, "Return", "Teleport2", "Warp to Save Point");
            RegisterSkill(CharacterSkill.WarpPortal, "Warp Portal", "WarpPortal", "Warp", "Memo");
            RegisterSkill(CharacterSkill.Pneuma, "Pneuma");
            RegisterSkill(CharacterSkill.HolyLight, "Holy Light", "HolyLight", "HL");
            RegisterSkill(CharacterSkill.SignumCrusis, "Signum Crusis", "Signum Crucis", "SignumCrusis", "SignumCrucis");
            RegisterSkill(CharacterSkill.AquaBenedicta, "Aqua Benedicta", "AquaBenedicta", "Holy Water", "Make Holy Water");
            RegisterSkill(CharacterSkill.Resurrection, "Resurrection", "Resurrect", "Res");
            RegisterSkill(CharacterSkill.Sanctuary, "Sanctuary", "Sanc");
            RegisterSkill(CharacterSkill.KyrieEleison, "Kyrie Eleison", "Kyrie", "KE");
            RegisterSkill(CharacterSkill.Magnificat, "Magnificat", "Magni");
            RegisterSkill(CharacterSkill.Gloria, "Gloria");
            RegisterSkill(CharacterSkill.ImpositioManus, "Impositio Manus", "Impositio", "Impo");
            RegisterSkill(CharacterSkill.Suffragium, "Suffragium", "Suffra");
            RegisterSkill(CharacterSkill.Aspersio, "Aspersio", "Asper");
            RegisterSkill(CharacterSkill.Benedicto, "Benedictio Sanctissimi Sacramenti", "Benedictio", "Benedicto", "BSS");
            RegisterSkill(CharacterSkill.LexAeterna, "Lex Aeterna", "LexAeterna", "LA");
            RegisterSkill(CharacterSkill.LexDivina, "Lex Divina", "LexDivina", "LD");
            RegisterSkill(CharacterSkill.TurnUndead, "Turn Undead", "TurnUndead", "TU");
            RegisterSkill(CharacterSkill.StatusRecovery, "Status Recovery", "StatusRecovery", "Recovery");
            RegisterSkill(CharacterSkill.MagnusExorcismus, "Magnus Exorcismus", "MagnusExorcismus", "ME");
            RegisterSkillByName("MaceMastery", "Mace Mastery", "MaceMastery");

            // 8. Merchant & Blacksmith Skills
            RegisterSkill(CharacterSkill.EnlargeWeightLimit, "Enlarge Weight Limit", "Increase Weight Limit", "EnlargeWeightLimit", "IncreaseWeightLimit", "EWL", "IWL");
            RegisterSkill(CharacterSkill.Discount, "Discount", "DC");
            RegisterSkill(CharacterSkill.Overcharge, "Overcharge", "OC");
            RegisterSkill(CharacterSkill.PushCart, "Push Cart", "PushCart");
            RegisterSkill(CharacterSkill.Vending, "Vending", "Vend");
            RegisterSkill(CharacterSkill.ItemAppraisal, "Item Appraisal", "Weapon Appraisal", "ItemAppraisal", "WeaponAppraisal", "Appraise", "Identify");
            RegisterSkill(CharacterSkill.Mammonite, "Mammonite", "Mammo");
            RegisterSkill(CharacterSkill.CrazyUproar, "Crazy Uproar", "Loud Voice", "CrazyUproar", "LoudVoice");
            RegisterSkill(CharacterSkill.CartRevolution, "Cart Revolution", "CartRevolution", "CR");
            RegisterSkill(CharacterSkill.HammerFall, "Hammer Fall", "HammerFall", "HF");
            RegisterSkill(CharacterSkill.AdrenalineRush, "Adrenaline Rush", "AdrenalineRush", "AR");
            RegisterSkill(CharacterSkill.WeaponPerfection, "Weapon Perfection", "WeaponPerfection", "WP");
            RegisterSkill(CharacterSkill.PowerThrust, "Power Thrust", "PowerThrust", "PT");
            RegisterSkill(CharacterSkill.MaximizePower, "Maximize Power", "MaximizePower", "MP");
            RegisterSkill(CharacterSkill.SkinTempering, "Skin Tempering", "SkinTempering");
            RegisterSkillByName("WeaponryResearch", "Weaponry Research", "WeaponryResearch");
            RegisterSkillByName("HiltBinding", "Hilt Binding", "HiltBinding");
            RegisterSkillByName("IronTempering", "Iron Tempering", "IronTempering");
            RegisterSkillByName("SteelTempering", "Steel Tempering", "SteelTempering");
            RegisterSkillByName("OreDiscovery", "Ore Discovery", "OreDiscovery");
            RegisterSkillByName("EnchantedStoneCraft", "Enchanted Stone Craft", "EnchantedStoneCraft");
            RegisterSkillByName("OrideconResearch", "Oridecon Research", "OrideconResearch");
            RegisterSkillByName("SmithBluntWeapon", "Smith Blunt Weapon", "SmithBluntWeapon");
            RegisterSkillByName("SmithBladeWeapon", "Smith Bladed Weapon", "SmithBladeWeapon");
            RegisterSkillByName("SmithPiercingWeapon", "Smith Piercing Weapon", "SmithPiercingWeapon");
            RegisterSkillByName("WeaponBinding", "Weapon Binding", "WeaponBinding");
            RegisterSkillByName("WeaponRepair", "Weapon Repair", "WeaponRepair");
        }

        private static void RegisterSkillByName(string enumName, string canonicalName, params string[] aliases)
        {
            if (Enum.TryParse<CharacterSkill>(enumName, true, out var skill) && skill != CharacterSkill.None)
            {
                RegisterSkill(skill, canonicalName, aliases);
            }
        }

        private static void RegisterSkill(CharacterSkill skill, string canonicalName, params string[] aliases)
        {
            var meta = new SkillMetadata(skill, canonicalName);
            CanonicalMap[skill] = meta;
            Register(meta, canonicalName);
            Register(meta, skill.ToString());

            if (aliases != null)
            {
                foreach (var alias in aliases)
                {
                    Register(meta, alias);
                }
            }
        }

        private static void Register(SkillMetadata meta, string alias)
        {
            string norm = Normalize(alias);
            if (!string.IsNullOrEmpty(norm))
            {
                NormalizedMap[norm] = meta;
            }
        }

        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        public static bool TryResolveSkill(string input, out CharacterSkill skill, out string canonicalName)
        {
            skill = CharacterSkill.None;
            canonicalName = string.Empty;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string s = input.Trim();

            // 1. Direct normalized dictionary lookup
            string norm = Normalize(s);
            if (NormalizedMap.TryGetValue(norm, out var meta))
            {
                skill = meta.Skill;
                canonicalName = meta.CanonicalName;
                return true;
            }

            // 2. Direct Enum.TryParse check (e.g. enum name directly)
            if (Enum.TryParse<CharacterSkill>(norm, true, out var enumMatch) && enumMatch != CharacterSkill.None)
            {
                skill = enumMatch;
                canonicalName = CanonicalMap.TryGetValue(enumMatch, out var cm) ? cm.CanonicalName : SplitCamelCase(enumMatch.ToString());
                return true;
            }

            // 3. Integer byte ID check (e.g. "1", "42")
            if (byte.TryParse(s, out byte byteId) && Enum.IsDefined(typeof(CharacterSkill), byteId))
            {
                var byteSkill = (CharacterSkill)byteId;
                if (byteSkill != CharacterSkill.None)
                {
                    skill = byteSkill;
                    canonicalName = CanonicalMap.TryGetValue(byteSkill, out var cm) ? cm.CanonicalName : SplitCamelCase(byteSkill.ToString());
                    return true;
                }
            }

            return false;
        }

        public static CharacterSkill Resolve(string input)
        {
            return TryResolveSkill(input, out var skill, out _) ? skill : CharacterSkill.None;
        }

        public static string GetCanonicalName(CharacterSkill skill)
        {
            if (CanonicalMap.TryGetValue(skill, out var meta))
            {
                return meta.CanonicalName;
            }
            return SplitCamelCase(skill.ToString());
        }

        private static string SplitCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var sb = new StringBuilder(input.Length + 5);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(input[i - 1]))
                {
                    sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
