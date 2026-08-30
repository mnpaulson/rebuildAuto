namespace RebuildBotPlugin
{
    /// <summary>
    /// Centralized bot constants for timings, interaction ranges, and thresholds.
    /// </summary>
    public static class BotConstants
    {
        public const float NpcInteractionRange = 6.0f;
        public const float HumanReadDelay = 0.65f;
        public const float HumanDialogAdvanceDelay = 0.75f;
        public const float MinStopDistance = 2.2f;
        public const float MaxStopDistance = 6.0f;
        public const float SectorBlacklistDuration = 30f;
        public const float AttackerStaleDuration = 6.0f;
        public const float UnreachableMonsterDuration = 6.0f;
        public const float DefaultSearchRadius = 15f;
    }
}
