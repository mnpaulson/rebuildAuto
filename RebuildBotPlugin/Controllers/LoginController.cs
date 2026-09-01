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

        private void SetLoginState(LoginState newState, string statusText, float now)
        {
            State = newState;
            StatusText = statusText;
            lastStateChangeTime = now;
            BotEngine.Instance?.ForceEmitStatus();
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
            BotEngine.Instance?.ForceEmitStatus();
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
                CurrentAttempt++;
                SetLoginState(LoginState.WaitingCooldown, $"Disconnected. Cooling down ({BotConfigManager.Current.AutoReconnectDelaySeconds:F0}s)...", now);
                BotEngine.Instance?.LogEvent($"[Login] Disconnect detected. Initiating reconnect routine (Attempt {CurrentAttempt}/{BotConfigManager.Current.MaxReconnectAttempts})...");
                return true;
            }

            if (CurrentAttempt > BotConfigManager.Current.MaxReconnectAttempts)
            {
                SetLoginState(LoginState.MaxAttemptsReached, $"Max reconnect attempts reached ({BotConfigManager.Current.MaxReconnectAttempts}).", now);
                return false;
            }

            switch (State)
            {
                case LoginState.Idle:
                    // Found on title screen without prior in-game session
                    disconnectDetectedTime = now;
                    CurrentAttempt++;
                    SetLoginState(LoginState.WaitingCooldown, $"On Title Screen. Reconnecting (Attempt {CurrentAttempt})...", now);
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
                        SetLoginState(LoginState.DismissingNotice, "Dismissing notice popup...", now);
                        return true;
                    }
                    else if (titleScreen.LoginBox != null && titleScreen.LoginBox.gameObject.activeSelf)
                    {
                        SetLoginState(LoginState.SubmittingLogin, "Submitting credentials...", now);
                        return true;
                    }
                    else if (titleScreen.CharacterSelectWindow != null && titleScreen.CharacterSelectWindow.gameObject.activeSelf)
                    {
                        SetLoginState(LoginState.SelectingCharacter, "Entering character select...", now);
                        return true;
                    }
                    return true;

                case LoginState.DismissingNotice:
                    if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        if (now - lastStateChangeTime >= 0.3f)
                        {
                            titleScreen.NoticeBoxOk();
                            SetLoginState(LoginState.SubmittingLogin, "Notice dismissed. Submitting login...", now);
                        }
                        return true;
                    }
                    SetLoginState(LoginState.SubmittingLogin, "Submitting credentials...", now);
                    return true;

                case LoginState.SubmittingLogin:
                    if (titleScreen.LoginBox != null && titleScreen.LoginBox.gameObject.activeSelf)
                    {
                        if (now - lastStateChangeTime >= 0.5f)
                        {
                            string targetProfile = !string.IsNullOrEmpty(Services.ProfileManager.ActiveProfileName)
                                ? Services.ProfileManager.ActiveProfileName
                                : Services.ProfileManager.ExplicitCliProfile;

                            if (!string.IsNullOrEmpty(targetProfile) &&
                                Services.AccountManager.TryGetCredentialsForProfile(targetProfile, out string username, out string password, out _, out _))
                            {
                                if (titleScreen.LoginBox.UsernameBox != null) titleScreen.LoginBox.UsernameBox.text = username;
                                if (titleScreen.LoginBox.PasswordBox != null) titleScreen.LoginBox.PasswordBox.text = password;
                                BotEngine.Instance?.LogEvent($"[Login] Using account credentials for profile '{targetProfile}' (User: '{username}')...");
                            }
                            else if (!string.IsNullOrEmpty(Services.ProfileManager.ExplicitCliAccount) &&
                                     Services.AccountManager.TryGetCredentialsForAccount(Services.ProfileManager.ExplicitCliAccount, out string accUser, out string accPass))
                            {
                                if (titleScreen.LoginBox.UsernameBox != null) titleScreen.LoginBox.UsernameBox.text = accUser;
                                if (titleScreen.LoginBox.PasswordBox != null) titleScreen.LoginBox.PasswordBox.text = accPass;
                                BotEngine.Instance?.LogEvent($"[Login] Using account credentials for account '{Services.ProfileManager.ExplicitCliAccount}' (User: '{accUser}')...");
                            }

                            BotEngine.Instance?.LogEvent($"[Login] Attempting account authentication (Attempt {CurrentAttempt})...");
                            titleScreen.LoginBox.AttemptLogin();
                            SetLoginState(LoginState.AwaitingCharacterSelect, "Authenticating with server...", now);
                        }
                        return true;
                    }
                    else if (titleScreen.CharacterSelectWindow != null && titleScreen.CharacterSelectWindow.gameObject.activeSelf)
                    {
                        SetLoginState(LoginState.SelectingCharacter, "Entering character select...", now);
                        return true;
                    }
                    else if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        // Server rejected or returned busy notice
                        disconnectDetectedTime = now;
                        CurrentAttempt++;
                        SetLoginState(LoginState.WaitingCooldown, $"Server notice received. Retrying in {BotConfigManager.Current.AutoReconnectDelaySeconds:F0}s...", now);
                        return true;
                    }
                    return true;

                case LoginState.AwaitingCharacterSelect:
                    if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        // Server error popup
                        disconnectDetectedTime = now;
                        CurrentAttempt++;
                        SetLoginState(LoginState.WaitingCooldown, $"Login error. Retrying in {BotConfigManager.Current.AutoReconnectDelaySeconds:F0}s...", now);
                        return true;
                    }

                    if (titleScreen.CharacterSelectWindow != null && titleScreen.CharacterSelectWindow.gameObject.activeSelf)
                    {
                        SetLoginState(LoginState.SelectingCharacter, "Character select screen ready.", now);
                        return true;
                    }

                    // Watchdog: If login request times out after 10s without response, retry
                    if (now - lastStateChangeTime > 10.0f)
                    {
                        disconnectDetectedTime = now;
                        CurrentAttempt++;
                        SetLoginState(LoginState.WaitingCooldown, $"Login request timed out. Retrying in {BotConfigManager.Current.AutoReconnectDelaySeconds:F0}s...", now);
                        return true;
                    }
                    return true;

                case LoginState.SelectingCharacter:
                    if (titleScreen.CharacterSelectWindow != null && titleScreen.CharacterSelectWindow.gameObject.activeSelf)
                    {
                        if (now - lastStateChangeTime >= 0.6f)
                        {
                            int targetSlot = BotConfigManager.Current.PreferredCharacterSlot;
                            string targetProfile = !string.IsNullOrEmpty(Services.ProfileManager.ActiveProfileName)
                                ? Services.ProfileManager.ActiveProfileName
                                : Services.ProfileManager.ExplicitCliProfile;

                            if (!string.IsNullOrEmpty(targetProfile) &&
                                Services.AccountManager.TryGetCredentialsForProfile(targetProfile, out _, out _, out int credSlot, out string charName))
                            {
                                targetSlot = credSlot;
                                if (!string.IsNullOrEmpty(charName))
                                {
                                    BotEngine.Instance?.LogEvent($"[Login] Selected profile '{targetProfile}' target character '{charName}' (Slot {targetSlot}).");
                                }
                            }

                            if (targetSlot >= 0 && targetSlot < titleScreen.CharacterSelectWindow.CharacterSlots.Count)
                            {
                                titleScreen.CharacterSelectWindow.SetCharacterInfo(targetSlot);
                            }

                            BotEngine.Instance?.LogEvent($"[Login] Entering world with selected character slot ({targetSlot})...");
                            titleScreen.CharacterSelectWindow.ClickOk();
                            SetLoginState(LoginState.AwaitingWorldEntry, "Entering world...", now);
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
                        CurrentAttempt++;
                        SetLoginState(LoginState.WaitingCooldown, $"World entry timed out. Reconnecting...", now);
                    }
                    return true;
            }

            return false;
        }
    }
}
