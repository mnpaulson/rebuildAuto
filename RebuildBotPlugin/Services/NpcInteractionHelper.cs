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
    /// Kafra storage, Adventuring Bard, and quest interactions.
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
        private string lastSeenDialogText = "";

        public const float MaxNpcVisualRange = 24.0f;
        public const float MinNpcInteractionDistance = 16.0f;
        public const float NpcLocationAnchorRadius = 20.0f;
        private const float DialogAdvanceDelay = 0.7f;
        private const float OptionSelectDelay = 0.5f;
        private const float StallTimeoutSeconds = 10.0f;
        private const float NpcSearchTimeoutSeconds = 3.0f;

        /// <summary>
        /// Begin a new NPC interaction. Navigates to the NPC, clicks it, and
        /// advances dialog until the option menu or action callback is ready.
        /// </summary>
        public void Begin(string nameHint, Vector2Int position, float customStopDistance = 0f)
        {
            npcNameHint = nameHint;
            npcPosition = position;
            stopDistance = customStopDistance > 0f ? customStopDistance : MaxNpcVisualRange;
            CurrentPhase = Phase.NavigatingToNpc;
            lastActionTime = Time.time;
            interactionStartTime = Time.time;
            clickedNpcId = -1;
            lastSeenDialogText = "";
        }

        /// <summary>
        /// Reset the helper to idle state.
        /// </summary>
        public void Reset()
        {
            CurrentPhase = Phase.Idle;
            clickedNpcId = -1;
            npcNameHint = null;
            lastSeenDialogText = "";
        }

        /// <summary>
        /// Tick the NPC navigation + click state machine.
        /// Returns true if the interaction is still in progress.
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
            // Opportunistic click: As soon as the NPC enters visual / entity range (up to 22 tiles), click it immediately!
            var npc = FindNearbyNpc(netManager, npcNameHint, npcPosition, player.CellPosition);
            if (npc != null)
            {
                netManager.SendNpcClick(npc.Id);
                clickedNpcId = npc.Id;
                CurrentPhase = Phase.WaitingForResponse;
                lastActionTime = now + 0.6f;
                lastSeenDialogText = "";
                float dist = Vector2.Distance(player.CellPosition, npc.CellPosition);
                BotEngine.Instance?.LogEvent($"[NPC] Spotted & clicked '{npc.Name}' from {dist:F1} tiles away (ID: {npc.Id}).");
                return true;
            }

            // If not yet visible in entity list, walk towards known location
            if (!player.IsMoving && now - lastActionTime >= 0.3f)
            {
                navigation.NavigateTowards(player.CellPosition, npcPosition, avoidPortals: true, hopDistance: 11, exactHitboxOnly: true);
                lastActionTime = now;
            }

            return true;
        }

        private bool ProcessClick(NetworkManager netManager, ServerControllable player, float now)
        {
            var npc = FindNearbyNpc(netManager, npcNameHint, npcPosition, player.CellPosition);

            if (npc != null)
            {
                netManager.SendNpcClick(npc.Id);
                clickedNpcId = npc.Id;
                CurrentPhase = Phase.WaitingForResponse;
                lastActionTime = now + 0.6f;
                lastSeenDialogText = "";
                BotEngine.Instance?.LogEvent($"[NPC] Clicked '{npc.Name}' (ID: {npc.Id}, dist: {Vector2.Distance(player.CellPosition, npc.CellPosition):F1} tiles).");
                return true;
            }

            if (now - lastActionTime > NpcSearchTimeoutSeconds)
            {
                CurrentPhase = Phase.NavigatingToNpc;
                lastActionTime = now;
                BotEngine.Instance?.LogDebug($"[NPC] '{npcNameHint}' not found near ({npcPosition.x}, {npcPosition.y}). Moving closer.");
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
                lastSeenDialogText = "";
                if (now - lastActionTime < OptionSelectDelay) return true;

                if (onOptionMenu != null)
                {
                    var buttons = cam.NpcOptionPanel.GetComponentsInChildren<NpcOptionButton>(false);
                    if (buttons != null && buttons.Length > 0)
                    {
                        if (onOptionMenu(buttons))
                        {
                            lastActionTime = now + 0.5f;
                            return true;
                        }
                    }
                }

                return true;
            }

            // 2. Dialog panel open (no options) — advance with human-like pacing
            if (dialogOpen)
            {
                var dialogWindow = cam.DialogPanel.GetComponent<DialogWindow>();
                string currentText = (dialogWindow != null && dialogWindow.TextBox != null) ? dialogWindow.TextBox.text : "";
                string currentSpeaker = (dialogWindow != null && dialogWindow.NameBox != null) ? dialogWindow.NameBox.text : "";

                // Detect new incoming dialogue text from server
                if (currentText != lastSeenDialogText)
                {
                    lastSeenDialogText = currentText;
                    lastActionTime = now;
                    if (!string.IsNullOrWhiteSpace(currentText))
                    {
                        BotEngine.Instance?.LogEvent($"[NPC Dialog] {currentSpeaker} {currentText}");
                    }
                    return true;
                }

                // If caller provided a custom dialog handler, allow it to run
                if (onDialogOpen != null && onDialogOpen())
                {
                    lastActionTime = now;
                    return true;
                }

                // Paced auto-advance
                if (now - lastActionTime >= DialogAdvanceDelay)
                {
                    netManager.SendNpcAdvance();
                    lastActionTime = now;
                    BotEngine.Instance?.LogEvent("[NPC] Clicked / Advanced dialogue prompt.");
                }

                return true;
            }

            // 3. No UI visible — either shop opened or interaction finished
            if (onNoUiVisible != null && onNoUiVisible())
            {
                return true;
            }

            // Stall timeout — reset and retry if no UI appeared
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
        /// Searches across full client visual/spawn range (up to 22 tiles from player).
        /// </summary>
        public static ServerControllable FindNearbyNpc(NetworkManager netManager, string nameSubstr, Vector2Int expectedPos, Vector2Int? playerPos = null)
        {
            if (netManager == null || netManager.EntityList == null) return null;

            ServerControllable bestNpc = null;
            float bestDist = float.MaxValue;
            Vector2 origin = playerPos.HasValue ? (Vector2)playerPos.Value : (Vector2)expectedPos;

            foreach (var kvp in netManager.EntityList)
            {
                var entity = kvp.Value;
                if (entity == null || entity.CharacterType != CharacterType.NPC) continue;

                // Ensure NPC is near its known spawn anchor (within 10 tiles of expectedPos)
                float anchorDist = Vector2.Distance(entity.CellPosition, expectedPos);
                if (anchorDist > NpcLocationAnchorRadius) continue;

                // Measure distance to origin
                float dist = Vector2.Distance(entity.CellPosition, origin);
                if (dist <= MaxNpcVisualRange)
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

        private static string globalLastSeenDialogText = "";
        private static float globalLastDialogAdvanceTime = 0f;

        /// <summary>
        /// Universal dialog auto-advancer. If ANY NPC dialog window is open on screen
        /// without an open option window, paces and advances through the text.
        /// Returns true if a dialog window was open and processed (blocking other actions).
        /// </summary>
        public static bool ProcessActiveDialog(NetworkManager netManager, float now)
        {
            if (netManager == null) return false;

            var cam = CameraFollower.Instance;
            if (cam == null || cam.DialogPanel == null || !cam.DialogPanel.activeSelf)
            {
                globalLastSeenDialogText = "";
                return false;
            }

            // If an option menu is also open, let the option handler handle it
            if (cam.NpcOptionPanel != null && cam.NpcOptionPanel.activeSelf)
            {
                globalLastSeenDialogText = "";
                return false;
            }

            var dialogWindow = cam.DialogPanel.GetComponent<DialogWindow>();
            string currentText = (dialogWindow != null && dialogWindow.TextBox != null) ? dialogWindow.TextBox.text : "";
            string currentSpeaker = (dialogWindow != null && dialogWindow.NameBox != null) ? dialogWindow.NameBox.text : "";

            if (currentText != globalLastSeenDialogText)
            {
                globalLastSeenDialogText = currentText;
                globalLastDialogAdvanceTime = now;
                if (!string.IsNullOrWhiteSpace(currentText))
                {
                    BotEngine.Instance?.LogEvent($"[NPC Dialog] {currentSpeaker} {currentText}");
                }
                return true;
            }

            if (now - globalLastDialogAdvanceTime >= DialogAdvanceDelay)
            {
                netManager.SendNpcAdvance();
                globalLastDialogAdvanceTime = now;
                BotEngine.Instance?.LogEvent("[NPC] Clicked / Advanced dialogue prompt.");
            }

            return true;
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
                    RefineItemWindow.Instance.CancelRefine();
                }
            }
            catch (Exception ex)
            {
                BotEngine.Instance?.LogEvent($"[NPC] Note closing UI: {ex.Message}");
            }
        }
    }
}
