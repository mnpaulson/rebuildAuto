using System;
using System.Collections.Generic;
using Assets.Scripts;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI;
using RebuildBotPlugin.Services;
using RebuildSharedData.Enum;
using RebuildSharedData.Enum.EntityStats;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public enum JobChangeState
    {
        Idle,
        NavigatingToBard,
        InteractingWithBard,
        EquippingStarterGear,
        Completed,
        Failed
    }

    /// <summary>
    /// Automates the 1st Job Change quest via the "Adventuring Bard" at prt_fild08 (153, 357).
    /// Once all Novice skill points are allocated via ProgressionController, steps through
    /// dialog, selects target job, claims starter rewards, and equips starter weapon.
    /// </summary>
    public class JobChangeController
    {
        public static readonly Vector2Int BardPosition = new Vector2Int(153, 357);
        public const string BardMap = "prt_fild08";

        public JobChangeState CurrentState { get; private set; } = JobChangeState.Idle;
        public bool IsActive => CurrentState != JobChangeState.Idle && CurrentState != JobChangeState.Completed && CurrentState != JobChangeState.Failed;

        private readonly NpcInteractionHelper npcHelper = new();
        private float stateStartTime = 0f;
        private float lastActionTime = 0f;
        private int attempts = 0;
        private const int MaxAttempts = 3;

        public void Reset()
        {
            CurrentState = JobChangeState.Idle;
            npcHelper.Reset();
            stateStartTime = 0f;
            lastActionTime = 0f;
            attempts = 0;
        }

        private bool hasClaimedStarterGift = false;

        /// <summary>
        /// Checks if the player is eligible and ready for 1st Job promotion.
        /// Requires Job Level 10 and 0 unspent skill points.
        /// </summary>
        public static bool IsEligibleForJobChange(ServerControllable player)
        {
            if (player == null || !player.IsCharacterAlive) return false;
            var state = PlayerState.Instance;
            if (state == null) return false;

            // Only Novices (JobId == 0) who reached Job Level 10 and spent all basic skill points
            return state.JobId == 0 && state.GetData(PlayerStat.JobLevel) >= 10 && state.SkillPoints == 0;
        }

        /// <summary>
        /// Checks if a brand-new Novice needs to speak to the Bard to claim the starter 1000z + 30 potions.
        /// </summary>
        public bool NeedsStarterGift(ServerControllable player)
        {
            if (hasClaimedStarterGift || !BotConfigManager.Current.AutoClaimBardGifts) return false;
            var state = PlayerState.Instance;
            if (state == null || player == null || !player.IsCharacterAlive) return false;
            return state.JobId == 0 && state.Zeny == 0;
        }

        public void StartClaimStarterGift(string reason = "Claim starter gift")
        {
            CurrentState = JobChangeState.NavigatingToBard;
            stateStartTime = Time.time;
            lastActionTime = Time.time;
            attempts = 0;
            npcHelper.Reset();
            npcHelper.Begin("Bard", BardPosition, 2.5f);
            BotEngine.Instance?.LogEvent($"[Adventuring Bard] Moving to Bard at ({BardPosition.x}, {BardPosition.y}) to claim 1,000 Zeny + 30 Novice Potions starter gift ({reason})...");
        }

        public void StartJobChange(string reason = "")
        {
            CurrentState = JobChangeState.NavigatingToBard;
            stateStartTime = Time.time;
            lastActionTime = Time.time;
            attempts = 0;
            npcHelper.Reset();
            npcHelper.Begin("Bard", BardPosition, 2.5f);
            BotEngine.Instance?.LogEvent($"[Job Change] Moving to Adventuring Bard at ({BardPosition.x}, {BardPosition.y}) for 1st Job promotion. Reason: {reason} (Target: {BotConfigManager.Current.TargetJob})");
        }

        public bool ProcessJobChange(NetworkManager netManager, ServerControllable player, NavigationController navigation, float now)
        {
            if (!IsActive) return false;

            var state = PlayerState.Instance;
            if (state == null || netManager == null || player == null)
            {
                CurrentState = JobChangeState.Failed;
                return false;
            }

            switch (CurrentState)
            {
                case JobChangeState.NavigatingToBard:
                case JobChangeState.InteractingWithBard:
                    CurrentState = JobChangeState.InteractingWithBard;

                    bool npcBusy = npcHelper.Process(
                        netManager,
                        player,
                        navigation,
                        now,
                        onOptionMenu: (options) => HandleBardOptions(netManager, options, now),
                        onDialogOpen: null, // Allow NpcInteractionHelper to pace and auto-advance all dialogs
                        onNoUiVisible: () =>
                        {
                            // If job is changed and no dialogue is open, we are ready to equip gear
                            return state.JobId > 0;
                        }
                    );

                    // If job was changed and dialogue is finished, proceed to equip gear
                    var cam = CameraFollower.Instance;
                    bool isDialogStillOpen = cam != null && (cam.IsInNPCInteraction || (cam.DialogPanel != null && cam.DialogPanel.activeSelf));

                    if (state.JobId > 0 && !isDialogStillOpen)
                    {
                        BotEngine.Instance?.LogEvent($"[Job Change] Job promotion confirmed! New Job ID: {state.JobId}. Preparing starter gear...");
                        CurrentState = JobChangeState.EquippingStarterGear;
                        lastActionTime = now + 0.5f;
                        return true;
                    }

                    if (!npcBusy && npcHelper.CurrentPhase == NpcInteractionHelper.Phase.Failed)
                    {
                        attempts++;
                        if (attempts >= MaxAttempts)
                        {
                            BotEngine.Instance?.LogEvent("[Job Change] Failed to complete Job Change with Adventuring Bard after maximum attempts.");
                            CurrentState = JobChangeState.Failed;
                            return false;
                        }
                        else
                        {
                            BotEngine.Instance?.LogEvent($"[Job Change] Retrying Bard interaction (attempt {attempts + 1}/{MaxAttempts})...");
                            npcHelper.Begin("Bard", BardPosition, 2.5f);
                        }
                    }
                    return true;

                case JobChangeState.EquippingStarterGear:
                    if (now - lastActionTime >= 0f)
                    {
                        EquipmentController.EquipStarterWeapon(netManager, state.JobId);
                        NpcInteractionHelper.CleanupNpcUi();
                        CurrentState = JobChangeState.Completed;
                        lastActionTime = now;
                        BotEngine.Instance?.LogEvent($"[Job Change] Job promotion to '{BotConfigManager.Current.TargetJob}' fully completed!");
                        return false;
                    }
                    return true;

                case JobChangeState.Completed:
                case JobChangeState.Failed:
                    return false;
            }

            return false;
        }

        private bool HandleBardOptions(NetworkManager netManager, NpcOptionButton[] options, float now)
        {
            if (options == null || options.Length == 0) return false;

            var state = PlayerState.Instance;

            // 0. If we only came to claim the welcome gift (not yet Job Level 10), select Cancel
            if (state != null && state.JobId == 0 && state.GetData(PlayerStat.JobLevel) < 10)
            {
                for (int i = 0; i < options.Length; i++)
                {
                    string text = options[i]?.TextBox != null ? options[i].TextBox.text : "";
                    if (text.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        BotEngine.Instance?.LogEvent($"[Adventuring Bard] Starter gift claimed (Zeny: {state.Zeny})! Selecting Cancel to resume hunting.");
                        options[i].OnClick();
                        hasClaimedStarterGift = true;
                        CurrentState = JobChangeState.Completed;
                        return true;
                    }
                }
            }

            // 1. Check for Main Menu: contains "Job change"
            for (int i = 0; i < options.Length; i++)
            {
                string text = options[i]?.TextBox != null ? options[i].TextBox.text : "";
                if (text.IndexOf("Job change", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    BotEngine.Instance?.LogEvent($"[Job Change] Selecting Bard option [{i}]: '{text}'");
                    options[i].OnClick();
                    return true;
                }
            }

            // 2. Check for Job Selection Menu: contains "Swordsman", "Archer", etc.
            string targetJob = (BotConfigManager.Current.TargetJob ?? "Swordman").Trim();
            int desiredJobIndex = ResolveJobOptionIndex(targetJob);

            for (int i = 0; i < options.Length; i++)
            {
                string text = options[i]?.TextBox != null ? options[i].TextBox.text : "";
                if (IsMatchingJobOption(text, targetJob, desiredJobIndex, i))
                {
                    BotEngine.Instance?.LogEvent($"[Job Change] Selecting Target Job option [{i}]: '{text}'");
                    options[i].OnClick();
                    return true;
                }
            }

            // 3. Check for Confirmation Menu: contains "I'm sure"
            for (int i = 0; i < options.Length; i++)
            {
                string text = options[i]?.TextBox != null ? options[i].TextBox.text : "";
                if (text.IndexOf("I'm sure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("sure", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    BotEngine.Instance?.LogEvent($"[Job Change] Confirming Job Choice option [{i}]: '{text}'");
                    options[i].OnClick();
                    return true;
                }
            }

            return false;
        }

        private int ResolveJobOptionIndex(string targetJob)
        {
            if (string.IsNullOrWhiteSpace(targetJob)) return 0;
            string clean = targetJob.Replace(" ", "").Replace("_", "").ToLowerInvariant();

            if (clean.Contains("sword")) return 0;     // Swordsman
            if (clean.Contains("arch")) return 1;      // Archer
            if (clean.Contains("mage") || clean.Contains("magician")) return 2; // Mage
            if (clean.Contains("acol")) return 3;      // Acolyte
            if (clean.Contains("thief")) return 4;     // Thief
            if (clean.Contains("merch")) return 5;     // Merchant

            return 0; // Default to Swordsman
        }

        private bool IsMatchingJobOption(string optionText, string targetJob, int desiredIndex, int currentIndex)
        {
            if (string.IsNullOrWhiteSpace(optionText)) return false;
            string clean = optionText.Trim().ToLowerInvariant();

            if (desiredIndex == 0 && (clean.Contains("swordsman") || clean.Contains("swordman"))) return true;
            if (desiredIndex == 1 && clean.Contains("archer")) return true;
            if (desiredIndex == 2 && clean.Contains("mage")) return true;
            if (desiredIndex == 3 && clean.Contains("acolyte")) return true;
            if (desiredIndex == 4 && clean.Contains("thief")) return true;
            if (desiredIndex == 5 && clean.Contains("merchant")) return true;

            // Direct index match fallback
            return currentIndex == desiredIndex;
        }
    }
}
