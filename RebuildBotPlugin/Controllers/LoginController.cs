using System;
using Assets.Scripts.Network;
using Assets.Scripts.UI.TitleScreen;
using UnityEngine;

namespace RebuildBotPlugin.Controllers
{
    public enum LoginState
    {
        Idle,
        WaitingCooldown,
        DismissingNotice,
        SubmittingLogin,
        AwaitingCharacterSelect,
        SelectingCharacter,
        AwaitingWorldEntry,
        MaxAttemptsReached
    }

    public class LoginController
    {
        public LoginState State { get; private set; } = LoginState.Idle;
        public int CurrentAttempt { get; private set; } = 0;
        public string StatusText { get; private set; } = "";
        public bool IsActive => State != LoginState.Idle && State != LoginState.MaxAttemptsReached;

        private float lastStateChangeTime = 0f;
        private float disconnectDetectedTime = 0f;
        private bool wasInGame = false;

        public void Clear()
        {
            State = LoginState.Idle;
            CurrentAttempt = 0;
            StatusText = "";
            disconnectDetectedTime = 0f;
        }

        public void OnWorldEntered()
        {
            if (State != LoginState.Idle)
            {
                BotEngine.Instance?.LogEvent($"[Login] Successfully re-entered world after {CurrentAttempt} reconnect attempt(s). Automation resuming.");
            }
            State = LoginState.Idle;
            CurrentAttempt = 0;
            StatusText = "";
            disconnectDetectedTime = 0f;
            wasInGame = true;
        }

        public bool ProcessLogin(float now)
        {
            if (!BotConfigManager.Current.AutoReconnect)
            {
                State = LoginState.Idle;
                return false;
            }

            var netManager = NetworkManager.Instance;
            if (netManager == null) return false;

            var titleScreen = netManager.TitleScreen;
            if (titleScreen == null)
            {
                // In transition or title screen not loaded yet
                return false;
            }

            // Detect fresh disconnect transition from in-game
            if (wasInGame)
            {
                wasInGame = false;
                disconnectDetectedTime = now;
                State = LoginState.WaitingCooldown;
                CurrentAttempt++;
                lastStateChangeTime = now;
                StatusText = $"Disconnected. Cooling down ({BotConfigManager.Current.AutoReconnectDelaySeconds:F0}s)...";
                BotEngine.Instance?.LogEvent($"[Login] Disconnect detected. Initiating reconnect routine (Attempt {CurrentAttempt}/{BotConfigManager.Current.MaxReconnectAttempts})...");
                return true;
            }

            if (CurrentAttempt > BotConfigManager.Current.MaxReconnectAttempts)
            {
                State = LoginState.MaxAttemptsReached;
                StatusText = $"Max reconnect attempts reached ({BotConfigManager.Current.MaxReconnectAttempts}).";
                return false;
            }

            switch (State)
            {
                case LoginState.Idle:
                    // Found on title screen without prior in-game session
                    disconnectDetectedTime = now;
                    State = LoginState.WaitingCooldown;
                    CurrentAttempt++;
                    lastStateChangeTime = now;
                    StatusText = $"On Title Screen. Reconnecting (Attempt {CurrentAttempt})...";
                    return true;

                case LoginState.WaitingCooldown:
                    float cooldownLeft = BotConfigManager.Current.AutoReconnectDelaySeconds - (now - disconnectDetectedTime);
                    if (cooldownLeft > 0f)
                    {
                        StatusText = $"Reconnecting in {cooldownLeft:F1}s (Attempt {CurrentAttempt}/{BotConfigManager.Current.MaxReconnectAttempts})...";
                        return true;
                    }

                    // Cooldown completed, advance to appropriate window
                    if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        State = LoginState.DismissingNotice;
                        lastStateChangeTime = now;
                        StatusText = "Dismissing notice popup...";
                        return true;
                    }
                    else if (titleScreen.LoginBox != null && titleScreen.LoginBox.gameObject.activeSelf)
                    {
                        State = LoginState.SubmittingLogin;
                        lastStateChangeTime = now;
                        StatusText = "Submitting credentials...";
                        return true;
                    }
                    else if (titleScreen.CharacterSelectWindow != null && titleScreen.CharacterSelectWindow.gameObject.activeSelf)
                    {
                        State = LoginState.SelectingCharacter;
                        lastStateChangeTime = now;
                        StatusText = "Entering character select...";
                        return true;
                    }
                    return true;

                case LoginState.DismissingNotice:
                    if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        if (now - lastStateChangeTime >= 0.3f)
                        {
                            titleScreen.NoticeBoxOk();
                            lastStateChangeTime = now;
                            State = LoginState.SubmittingLogin;
                            StatusText = "Notice dismissed. Submitting login...";
                        }
                        return true;
                    }
                    State = LoginState.SubmittingLogin;
                    lastStateChangeTime = now;
                    return true;

                case LoginState.SubmittingLogin:
                    if (titleScreen.LoginBox != null && titleScreen.LoginBox.gameObject.activeSelf)
                    {
                        if (now - lastStateChangeTime >= 0.5f)
                        {
                            BotEngine.Instance?.LogEvent($"[Login] Attempting account authentication (Attempt {CurrentAttempt})...");
                            titleScreen.LoginBox.AttemptLogin();
                            State = LoginState.AwaitingCharacterSelect;
                            lastStateChangeTime = now;
                            StatusText = "Authenticating with server...";
                        }
                        return true;
                    }
                    else if (titleScreen.CharacterSelectWindow != null && titleScreen.CharacterSelectWindow.gameObject.activeSelf)
                    {
                        State = LoginState.SelectingCharacter;
                        lastStateChangeTime = now;
                        return true;
                    }
                    else if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        // Server rejected or returned busy notice
                        disconnectDetectedTime = now;
                        State = LoginState.WaitingCooldown;
                        CurrentAttempt++;
                        lastStateChangeTime = now;
                        StatusText = $"Server notice received. Retrying in {BotConfigManager.Current.AutoReconnectDelaySeconds:F0}s...";
                        return true;
                    }
                    return true;

                case LoginState.AwaitingCharacterSelect:
                    if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        // Server error popup
                        disconnectDetectedTime = now;
                        State = LoginState.WaitingCooldown;
                        CurrentAttempt++;
                        lastStateChangeTime = now;
                        StatusText = $"Login error. Retrying in {BotConfigManager.Current.AutoReconnectDelaySeconds:F0}s...";
                        return true;
                    }

                    if (titleScreen.CharacterSelectWindow != null && titleScreen.CharacterSelectWindow.gameObject.activeSelf)
                    {
                        State = LoginState.SelectingCharacter;
                        lastStateChangeTime = now;
                        StatusText = "Character select screen ready.";
                        return true;
                    }

                    // Watchdog: If login request times out after 10s without response, retry
                    if (now - lastStateChangeTime > 10.0f)
                    {
                        disconnectDetectedTime = now;
                        State = LoginState.WaitingCooldown;
                        CurrentAttempt++;
                        lastStateChangeTime = now;
                        StatusText = $"Login request timed out. Retrying in {BotConfigManager.Current.AutoReconnectDelaySeconds:F0}s...";
                        return true;
                    }
                    return true;

                case LoginState.SelectingCharacter:
                    if (titleScreen.CharacterSelectWindow != null && titleScreen.CharacterSelectWindow.gameObject.activeSelf)
                    {
                        if (now - lastStateChangeTime >= 0.6f)
                        {
                            int targetSlot = BotConfigManager.Current.PreferredCharacterSlot;
                            if (targetSlot >= 0 && targetSlot < titleScreen.CharacterSelectWindow.CharacterSlots.Count)
                            {
                                titleScreen.CharacterSelectWindow.SetCharacterInfo(targetSlot);
                            }

                            BotEngine.Instance?.LogEvent($"[Login] Entering world with selected character slot...");
                            titleScreen.CharacterSelectWindow.ClickOk();
                            State = LoginState.AwaitingWorldEntry;
                            lastStateChangeTime = now;
                            StatusText = "Entering world...";
                        }
                        return true;
                    }
                    return true;

                case LoginState.AwaitingWorldEntry:
                    // Waiting for scene transition & CameraFollower.Target instantiation
                    StatusText = "Loading world scene...";
                    if (now - lastStateChangeTime > 15.0f)
                    {
                        disconnectDetectedTime = now;
                        State = LoginState.WaitingCooldown;
                        CurrentAttempt++;
                        lastStateChangeTime = now;
                    }
                    return true;
            }

            return false;
        }
    }
}
