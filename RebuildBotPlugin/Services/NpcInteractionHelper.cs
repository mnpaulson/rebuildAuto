using System;
using Assets.Scripts;
using Assets.Scripts.Network;
using Assets.Scripts.UI;
using Assets.Scripts.UI.RefineItem;
using RebuildBotPlugin.Controllers;
using RebuildSharedData.Enum;
using UnityEngine;

namespace RebuildBotPlugin.Services
{
    /// <summary>
    /// Generic NPC interaction state machine that encapsulates the
    /// click→dialog→option→action pattern shared across vendor buy/sell,
    /// Kafra storage, and Kafra teleport interactions.
    /// </summary>
    public class NpcInteractionHelper
    {
        public enum Phase
        {
            Idle,
            NavigatingToNpc,
            WaitingToClick,
            WaitingForResponse,
            Completed,
            Failed
        }

        public Phase CurrentPhase { get; private set; } = Phase.Idle;
        public bool IsActive => CurrentPhase != Phase.Idle && CurrentPhase != Phase.Completed && CurrentPhase != Phase.Failed;

        private string npcNameHint;
        private Vector2Int npcPosition;
        private float stopDistance;
        private float lastActionTime;
        private float interactionStartTime;
        private int clickedNpcId = -1;

        // Timing constants
        private const float NpcInteractionRange = 6.0f;
        private const float MinStopDistance = 2.2f;
        private const float MaxStopDistance = 6.0f;
        private const float DialogAdvanceDelay = 0.75f;
        private const float OptionSelectDelay = 0.65f;
        private const float StallTimeoutSeconds = 5.0f;
        private const float NpcSearchTimeoutSeconds = 3.0f;

        /// <summary>
        /// Begin a new NPC interaction. Navigates to the NPC, clicks it, and
        /// advances dialog until the option menu or action callback is ready.
        /// </summary>
        /// <param name="nameHint">Substring to match against NPC names (e.g., "Vendor", "Kafra", "Alchemist")</param>
        /// <param name="position">Expected NPC cell position</param>
        /// <param name="customStopDistance">Custom stop distance; 0 for random</param>
        public void Begin(string nameHint, Vector2Int position, float customStopDistance = 0f)
        {
            npcNameHint = nameHint;
            npcPosition = position;
            stopDistance = customStopDistance > 0f ? customStopDistance : UnityEngine.Random.Range(MinStopDistance, MaxStopDistance);
            CurrentPhase = Phase.NavigatingToNpc;
            lastActionTime = Time.time;
            interactionStartTime = Time.time;
            clickedNpcId = -1;
        }

        /// <summary>
        /// Reset the helper to idle state.
        /// </summary>
        public void Reset()
        {
            CurrentPhase = Phase.Idle;
            clickedNpcId = -1;
            npcNameHint = null;
        }

        /// <summary>
        /// Tick the NPC navigation + click state machine.
        /// Returns true if the interaction is still in progress.
        ///
        /// Once the NPC has been clicked and dialog/options appear, this method
        /// calls the provided callbacks to handle the interaction-specific logic.
        ///
        /// <paramref name="onOptionMenu"/> is called when an NPC option panel is open.
        /// Return true if you handled the option, false to keep waiting.
        ///
        /// <paramref name="onDialogOpen"/> is called when a dialog panel is open (no options).
        /// Return true if you handled it (e.g., advanced dialog), false to auto-advance.
        ///
        /// <paramref name="onNoUiVisible"/> is called after clicking the NPC when neither
        /// dialog nor options are visible. This handles shop UIs that appear without
        /// dialog (e.g., buy/sell panels). Return true if interaction is complete.
        /// </summary>
        public bool Process(
            NetworkManager netManager,
            ServerControllable player,
            NavigationController navigation,
            float now,
            Func<NpcOptionButton[], bool> onOptionMenu = null,
            Func<bool> onDialogOpen = null,
            Func<bool> onNoUiVisible = null)
        {
            if (!IsActive) return false;

            switch (CurrentPhase)
            {
                case Phase.NavigatingToNpc:
                    return ProcessNavigation(netManager, player, navigation, now);

                case Phase.WaitingToClick:
                    return ProcessClick(netManager, player, now);

                case Phase.WaitingForResponse:
                    return ProcessResponse(netManager, player, now, onOptionMenu, onDialogOpen, onNoUiVisible);
            }

            return false;
        }

        private bool ProcessNavigation(NetworkManager netManager, ServerControllable player, NavigationController navigation, float now)
        {
            float dist = Vector2.Distance(player.CellPosition, npcPosition);

            if (dist <= stopDistance)
            {
                CurrentPhase = Phase.WaitingToClick;
                lastActionTime = now;
                return true;
            }

            if (!player.IsMoving && now - lastActionTime >= 0.3f)
            {
                navigation.NavigateTowards(player.CellPosition, npcPosition, avoidPortals: false, hopDistance: 11);
                lastActionTime = now;
            }

            return true;
        }

        private bool ProcessClick(NetworkManager netManager, ServerControllable player, float now)
        {
            var npc = FindNearbyNpc(netManager, npcNameHint, npcPosition);

            if (npc != null)
            {
                netManager.SendNpcClick(npc.Id);
                clickedNpcId = npc.Id;
                CurrentPhase = Phase.WaitingForResponse;
                lastActionTime = now + UnityEngine.Random.Range(0.6f, 0.9f);
                BotEngine.Instance?.LogEvent($"[NPC] Clicked '{npc.Name}' (ID: {npc.Id}, dist: {Vector2.Distance(player.CellPosition, npc.CellPosition):F1} tiles).");
                return true;
            }

            if (now - lastActionTime > NpcSearchTimeoutSeconds)
            {
                // NPC not found — move closer and retry
                stopDistance = Math.Max(1.5f, stopDistance - 1.0f);
                CurrentPhase = Phase.NavigatingToNpc;
                lastActionTime = now;
                BotEngine.Instance?.LogDebug($"[NPC] '{npcNameHint}' not found within range near ({npcPosition.x}, {npcPosition.y}). Moving closer (stopDist: {stopDistance:F1}).");
            }

            return true;
        }

        private bool ProcessResponse(
            NetworkManager netManager,
            ServerControllable player,
            float now,
            Func<NpcOptionButton[], bool> onOptionMenu,
            Func<bool> onDialogOpen,
            Func<bool> onNoUiVisible)
        {
            var cam = CameraFollower.Instance;
            bool dialogOpen = cam != null && cam.DialogPanel != null && cam.DialogPanel.activeSelf;
            bool optionOpen = cam != null && cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf;

            // 1. Option menu is open — delegate to caller
            if (optionOpen)
            {
                if (now - lastActionTime < OptionSelectDelay) return true;

                if (onOptionMenu != null)
                {
                    var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(false);
                    if (buttons != null && buttons.Length > 0)
                    {
                        if (onOptionMenu(buttons))
                        {
                            lastActionTime = now + UnityEngine.Random.Range(0.4f, 0.7f);
                            return true;
                        }
                    }
                }

                return true;
            }

            // 2. Dialog panel open (no options) — advance or delegate
            if (dialogOpen)
            {
                if (onDialogOpen != null && onDialogOpen())
                {
                    lastActionTime = now;
                    return true;
                }

                // Default: auto-advance dialog
                if (now - lastActionTime >= DialogAdvanceDelay)
                {
                    netManager.SendNpcAdvance();
                    lastActionTime = now;
                    BotEngine.Instance?.LogDebug("[NPC] Auto-advanced dialog prompt.");
                }

                return true;
            }

            // 3. No UI visible — either shop opened or interaction timed out
            if (onNoUiVisible != null && onNoUiVisible())
            {
                return true;
            }

            // Stall timeout — reset and retry
            if (now - lastActionTime >= StallTimeoutSeconds)
            {
                CleanupNpcUi();
                CurrentPhase = Phase.WaitingToClick;
                lastActionTime = now + 0.8f;
                BotEngine.Instance?.LogEvent("[NPC] Interaction stalled; cleanly closed UI and retrying.");
            }

            return true;
        }

        /// <summary>
        /// Mark the interaction as completed.
        /// Call this from your onOptionMenu/onNoUiVisible callback when done.
        /// </summary>
        public void Complete()
        {
            CurrentPhase = Phase.Completed;
        }

        /// <summary>
        /// Mark the interaction as failed.
        /// </summary>
        public void Fail()
        {
            CurrentPhase = Phase.Failed;
        }

        /// <summary>
        /// Find the nearest NPC matching a name substring near an expected position.
        /// </summary>
        public static ServerControllable FindNearbyNpc(NetworkManager netManager, string nameSubstr, Vector2Int expectedPos)
        {
            if (netManager == null || netManager.EntityList == null) return null;

            ServerControllable bestNpc = null;
            float bestDist = float.MaxValue;

            foreach (var kvp in netManager.EntityList)
            {
                var entity = kvp.Value;
                if (entity == null || entity.CharacterType != CharacterType.NPC) continue;

                float dist = Vector2.Distance(entity.CellPosition, expectedPos);
                if (dist <= NpcInteractionRange)
                {
                    bool nameMatches = string.IsNullOrEmpty(nameSubstr) ||
                                       (!string.IsNullOrEmpty(entity.Name) && entity.Name.IndexOf(nameSubstr, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (nameMatches && dist < bestDist)
                    {
                        bestDist = dist;
                        bestNpc = entity;
                    }
                }
            }
            return bestNpc;
        }

        /// <summary>
        /// Cleanly close any open NPC UI panels and reset interaction locks.
        /// </summary>
        public static void CleanupNpcUi()
        {
            try
            {
                var cam = CameraFollower.Instance;
                if (cam != null)
                {
                    cam.IsInNPCInteraction = false;
                    cam.OverrideTarget = null;
                    if (cam.DialogPanel != null)
                    {
                        var dialog = cam.DialogPanel.GetComponent<DialogWindow>();
                        if (dialog != null) dialog.HideUI();
                        else cam.DialogPanel.SetActive(false);
                    }
                    if (cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf)
                        cam.NpcOptionPanel.SetActive(false);
                }

                if (ShopUI.Instance != null)
                {
                    var shop = ShopUI.Instance;
                    if (UiManager.Instance != null)
                    {
                        UiManager.Instance.ForceHideTooltip();
                        if (UiManager.Instance.ItemDescriptionWindow != null)
                            UiManager.Instance.ItemDescriptionWindow.HideWindow();
                    }
                    if (shop.RightWindow != null)
                        UnityEngine.Object.Destroy(shop.RightWindow.gameObject);
                    if (shop.LeftWindow != null)
                        UnityEngine.Object.Destroy(shop.LeftWindow.gameObject);
                    UnityEngine.Object.Destroy(shop.gameObject);
                    ShopUI.Instance = null;
                }

                if (StorageUI.Instance != null)
                {
                    UnityEngine.Object.Destroy(StorageUI.Instance.gameObject);
                    StorageUI.Instance = null;
                }

                if (RefineItemWindow.Instance != null)
                {
                    UnityEngine.Object.Destroy(RefineItemWindow.Instance.gameObject);
                }
            }
            catch (Exception ex)
            {
                BotEngine.Instance?.LogEvent($"[NPC] Note closing UI: {ex.Message}");
            }
        }
    }
}
