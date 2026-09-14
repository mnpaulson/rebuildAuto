using System;

namespace RebuildBotPlugin.Models
{
    [Serializable]
    public class EquipmentTarget
    {
        public string ItemName { get; set; } = "";
        public int TargetRefineLevel { get; set; } = 0;
        public int MinLevel { get; set; } = 1;
        public int MinZeny { get; set; } = 0;

        public override string ToString()
        {
            string refineStr = TargetRefineLevel > 0 ? $"+{TargetRefineLevel} " : "";
            return $"{refineStr}{ItemName} (Lvl {MinLevel}+, {MinZeny:N0}z)";
        }
    }
}
