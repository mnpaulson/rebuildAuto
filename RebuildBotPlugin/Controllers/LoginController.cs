using System;
using Assets.Scripts;
using Assets.Scripts.Network;
using Assets.Scripts.PlayerControl;
using Assets.Scripts.UI.ConfigWindow;
using Assets.Scripts.UI.TitleScreen;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        CreatingCharacter,
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
        private float errorPromptDetectedTime = 0f;
        private bool wasInGame = false;

        public void Clear()
        {
            State = LoginState.Idle;
            CurrentAttempt = 0;
            StatusText = "";
            disconnectDetectedTime = 0f;
            errorPromptDetectedTime = 0f;
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
            errorPromptDetectedTime = 0f;
            wasInGame = true;
            BotEngine.Instance?.ForceEmitStatus();
        }

        public bool ProcessLogin(float now)
        {
            try
            {
                if (!BotConfigManager.Current.AutoReconnect)
                {
                    State = LoginState.Idle;
                    return false;
                }

                var netManager = NetworkManager.Instance;

                // 1. Detect disconnect error overlay ("Press space to try to reconnect")
                var cam = CameraFollower.Instance;
                bool isErrorPromptActive = cam != null && (cam.IsInErrorState || (cam.ErrorNoticeUi != null && cam.ErrorNoticeUi.gameObject.activeSelf));

                if (isErrorPromptActive)
                {
                    if (CurrentAttempt >= BotConfigManager.Current.MaxReconnectAttempts)
                    {
                        SetLoginState(LoginState.MaxAttemptsReached, $"Max reconnect attempts reached ({BotConfigManager.Current.MaxReconnectAttempts}).", now);
                        return false;
                    }

                    if (errorPromptDetectedTime == 0f)
                    {
                        errorPromptDetectedTime = now;
                        CurrentAttempt++;
                        string errMsg = (cam.ErrorNoticeUi != null && !string.IsNullOrEmpty(cam.ErrorNoticeUi.text)) ? cam.ErrorNoticeUi.text : "Connection error";
                        BotEngine.Instance?.LogEvent($"[Login] Disconnect error prompt detected ({errMsg}). Initiating Spacebar reconnect (Attempt {CurrentAttempt}/{BotConfigManager.Current.MaxReconnectAttempts})...");
                    }

                    float elapsed = now - errorPromptDetectedTime;
                    if (elapsed < 1.0f)
                    {
                        SetLoginState(LoginState.WaitingCooldown, $"Disconnect error detected. Reconnecting in {1.0f - elapsed:F1}s (Attempt {CurrentAttempt}/{BotConfigManager.Current.MaxReconnectAttempts})...", now);
                        return true;
                    }

                    // Simulate Spacebar press: Save config, reset error UI, disconnect socket, reload Title Screen (Scene 0)
                    errorPromptDetectedTime = 0f;
                    wasInGame = false;
                    disconnectDetectedTime = now;

                    try
                    {
                        GameConfig.SaveConfig();
                    }
                    catch { }

                    if (cam != null)
                    {
                        cam.IsInErrorState = false;
                        if (cam.ErrorNoticeUi != null)
                            cam.ErrorNoticeUi.gameObject.SetActive(false);
                    }

                    if (netManager != null)
                    {
                        try
                        {
                            netManager.Disconnect();
                        }
                        catch { }
                    }

                    SetLoginState(LoginState.WaitingCooldown, $"Reloading title screen (Attempt {CurrentAttempt}/{BotConfigManager.Current.MaxReconnectAttempts})...", now);
                    SceneManager.LoadScene(0);
                    return true;
                }
                else
                {
                    errorPromptDetectedTime = 0f;
                }

                if (netManager == null || !NetworkManager.IsLoaded) return false;

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
                    // Fresh client launch on title screen - no need for long reconnect cooldown
                    CurrentAttempt++;
                    if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        SetLoginState(LoginState.DismissingNotice, "Dismissing notice popup...", now);
                        return true;
                    }
                    if (titleScreen.TitleState == TitleScreen.TitleScreenState.CharacterSelect &&
                        titleScreen.CharacterSelectWindow != null &&
                        titleScreen.CharacterSelectWindow.DisplayPane != null &&
                        titleScreen.CharacterSelectWindow.DisplayPane.activeSelf)
                    {
                        SetLoginState(LoginState.SelectingCharacter, "Entering character select...", now);
                        return true;
                    }
                    SetLoginState(LoginState.SubmittingLogin, "Submitting credentials...", now);
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
                    else if (titleScreen.TitleState == TitleScreen.TitleScreenState.CharacterSelect &&
                             titleScreen.CharacterSelectWindow != null &&
                             titleScreen.CharacterSelectWindow.DisplayPane != null &&
                             titleScreen.CharacterSelectWindow.DisplayPane.activeSelf)
                    {
                        SetLoginState(LoginState.SelectingCharacter, "Entering character select...", now);
                        return true;
                    }
                    else if (titleScreen.TitleState == TitleScreen.TitleScreenState.LogIn ||
                             (titleScreen.LoginBox != null && titleScreen.LoginBox.gameObject.activeSelf))
                    {
                        if (titleScreen.LoginBox != null && !titleScreen.LoginBox.gameObject.activeSelf)
                            titleScreen.LoginBox.gameObject.SetActive(true);

                        SetLoginState(LoginState.SubmittingLogin, "Submitting credentials...", now);
                        return true;
                    }
                    return true;

                case LoginState.DismissingNotice:
                    if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        if (now - lastStateChangeTime >= 0.3f)
                        {
                            HandleServerNotice(titleScreen);
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

                            string username = "";
                            string password = "";
                            bool isNewAcc = BotConfigManager.Current.AutoCreateAccount;

                            if (!string.IsNullOrEmpty(targetProfile) &&
                                Services.AccountManager.TryGetCharacterForProfile(targetProfile, out var accEntry, out _))
                            {
                                if (accEntry != null)
                                {
                                    username = accEntry.Username;
                                    password = accEntry.Password;
                                    if (accEntry.IsNewAccount) isNewAcc = true;
                                }
                            }
                            else if (!string.IsNullOrEmpty(targetProfile) &&
                                Services.AccountManager.TryGetCredentialsForProfile(targetProfile, out string u, out string p, out _, out _))
                            {
                                username = u;
                                password = p;
                            }
                            else if (!string.IsNullOrEmpty(Services.ProfileManager.ExplicitCliAccount) &&
                                     Services.AccountManager.TryGetCredentialsForAccount(Services.ProfileManager.ExplicitCliAccount, out string accUser, out string accPass))
                            {
                                username = accUser;
                                password = accPass;
                                if (Services.AccountManager.Registry.TryGetAccountById(Services.ProfileManager.ExplicitCliAccount, out var accObj) && accObj.IsNewAccount)
                                {
                                    isNewAcc = true;
                                }
                            }

                            if (!string.IsNullOrEmpty(username))
                            {
                                if (isNewAcc)
                                {
                                    BotEngine.Instance?.LogEvent($"[Login] Registering NEW account '{username}' (Tab 1)...");
                                    titleScreen.LoginBox.ChangeTabs(1);
                                    if (titleScreen.LoginBox.UsernameBox != null) titleScreen.LoginBox.UsernameBox.text = username;
                                    if (titleScreen.LoginBox.PasswordBox != null) titleScreen.LoginBox.PasswordBox.text = password;
                                    if (titleScreen.LoginBox.PasswordRepeatBox != null) titleScreen.LoginBox.PasswordRepeatBox.text = password;
                                }
                                else
                                {
                                    BotEngine.Instance?.LogEvent($"[Login] Logging in existing account '{username}' (Tab 0)...");
                                    titleScreen.LoginBox.ChangeTabs(0);
                                    if (titleScreen.LoginBox.UsernameBox != null) titleScreen.LoginBox.UsernameBox.text = username;
                                    if (titleScreen.LoginBox.PasswordBox != null) titleScreen.LoginBox.PasswordBox.text = password;
                                }
                            }

                            BotEngine.Instance?.LogEvent($"[Login] Attempting account authentication (Attempt {CurrentAttempt}, IsNew: {isNewAcc})...");
                            titleScreen.LoginBox.AttemptLogin();
                            SetLoginState(LoginState.AwaitingCharacterSelect, "Authenticating with server...", now);
                        }
                        return true;
                    }
                    else if (titleScreen.TitleState == TitleScreen.TitleScreenState.CharacterSelect &&
                             titleScreen.CharacterSelectWindow != null &&
                             titleScreen.CharacterSelectWindow.DisplayPane != null &&
                             titleScreen.CharacterSelectWindow.DisplayPane.activeSelf)
                    {
                        ClearNewAccountFlags();
                        SetLoginState(LoginState.SelectingCharacter, "Entering character select...", now);
                        return true;
                    }
                    else if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        // Server rejected or returned busy notice
                        HandleServerNotice(titleScreen);
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
                        HandleServerNotice(titleScreen);
                        disconnectDetectedTime = now;
                        CurrentAttempt++;
                        SetLoginState(LoginState.WaitingCooldown, $"Login error. Retrying in {BotConfigManager.Current.AutoReconnectDelaySeconds:F0}s...", now);
                        return true;
                    }

                    if (titleScreen.TitleState == TitleScreen.TitleScreenState.CharacterSelect &&
                        titleScreen.CharacterSelectWindow != null &&
                        titleScreen.CharacterSelectWindow.DisplayPane != null &&
                        titleScreen.CharacterSelectWindow.DisplayPane.activeSelf)
                    {
                        ClearNewAccountFlags();
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
                    bool isCreationActive = titleScreen.TitleState == TitleScreen.TitleScreenState.CharacterCreation ||
                                            (titleScreen.CharacterCreatorWindow != null && titleScreen.CharacterCreatorWindow.Pane != null && titleScreen.CharacterCreatorWindow.Pane.activeSelf);
                    if (isCreationActive)
                    {
                        SetLoginState(LoginState.CreatingCharacter, "Character creation window active...", now);
                        return true;
                    }

                    if (titleScreen.TitleState == TitleScreen.TitleScreenState.CharacterSelect &&
                        titleScreen.CharacterSelectWindow != null &&
                        titleScreen.CharacterSelectWindow.DisplayPane != null &&
                        titleScreen.CharacterSelectWindow.DisplayPane.activeSelf)
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

                            if (targetSlot < 0)
                            {
                                targetSlot = 0;
                            }

                            try
                            {
                                if (titleScreen.CharacterSelectWindow.CharacterSlots != null &&
                                    targetSlot >= 0 && targetSlot < titleScreen.CharacterSelectWindow.CharacterSlots.Count)
                                {
                                    titleScreen.CharacterSelectWindow.SetCharacterInfo(targetSlot);
                                }
                            }
                            catch (Exception ex)
                            {
                                BotEngine.Instance?.LogEvent($"[Login] Warning setting character info for slot {targetSlot}: {ex.Message}");
                            }

                            BotEngine.Instance?.LogEvent($"[Login] Entering world with selected character slot ({targetSlot})...");
                            titleScreen.CharacterSelectWindow.ClickOk();
                            SetLoginState(LoginState.AwaitingWorldEntry, "Entering world...", now);
                        }
                        return true;
                    }
                    return true;

                case LoginState.CreatingCharacter:
                    bool isCreationOpen = titleScreen.TitleState == TitleScreen.TitleScreenState.CharacterCreation ||
                                          (titleScreen.CharacterCreatorWindow != null && titleScreen.CharacterCreatorWindow.Pane != null && titleScreen.CharacterCreatorWindow.Pane.activeSelf);
                    if (isCreationOpen)
                    {
                        if (now - lastStateChangeTime >= 0.5f)
                        {
                            string targetProfile = !string.IsNullOrEmpty(Services.ProfileManager.ActiveProfileName)
                                ? Services.ProfileManager.ActiveProfileName
                                : Services.ProfileManager.ExplicitCliProfile;

                            int targetSlot = BotConfigManager.Current.PreferredCharacterSlot;
                            string charName = targetProfile;
                            string gender = BotConfigManager.Current.CharacterGender ?? "Male";
                            var statsList = BotConfigManager.Current.StartingStats;

                            if (!string.IsNullOrEmpty(targetProfile) &&
                                Services.AccountManager.TryGetCharacterForProfile(targetProfile, out _, out var characterEntry))
                            {
                                if (characterEntry != null)
                                {
                                    if (!string.IsNullOrEmpty(characterEntry.Name)) charName = characterEntry.Name;
                                    targetSlot = characterEntry.Slot;
                                    if (!string.IsNullOrEmpty(characterEntry.Gender)) gender = characterEntry.Gender;
                                    if (characterEntry.StartingStats != null && characterEntry.StartingStats.Count == 6)
                                        statsList = characterEntry.StartingStats;
                                }
                            }

                            if (string.IsNullOrWhiteSpace(charName))
                            {
                                charName = "Bot" + UnityEngine.Random.Range(1000, 9999);
                            }

                            bool isMale = !string.Equals(gender, "Female", StringComparison.OrdinalIgnoreCase);

                            int[] stats = new int[6];
                            if (statsList != null && statsList.Count == 6)
                            {
                                for (int i = 0; i < 6; i++) stats[i] = Mathf.Clamp(statsList[i], 1, 9);
                            }
                            else
                            {
                                stats = new int[] { 5, 5, 5, 5, 5, 8 };
                            }

                            int hairStyle = Mathf.Clamp(BotConfigManager.Current.CharacterHairStyle, 0, 19);
                            int hairColor = Mathf.Clamp(BotConfigManager.Current.CharacterHairColor, 0, 8);
                            int slot = targetSlot >= 0 && targetSlot < 3 ? targetSlot : titleScreen.SelectedSlot;

                            BotEngine.Instance?.LogEvent($"[Login] Auto-creating character '{charName}' (Slot {slot}, Gender: {(isMale ? "Male" : "Female")}, Stats: [{string.Join(",", stats)}])...");

                            NetworkManager.Instance.SendEnterServerNewCharacterMessage(charName, slot, hairStyle, hairColor, stats, isMale);
                            titleScreen.LastTitleState = TitleScreen.TitleScreenState.CharacterCreation;
                            titleScreen.TitleState = TitleScreen.TitleScreenState.Waiting;
                            if (titleScreen.CharacterCreatorWindow != null)
                            {
                                titleScreen.CharacterCreatorWindow.HidePane();
                            }

                            SetLoginState(LoginState.AwaitingWorldEntry, "Character created. Entering world...", now);
                        }
                        return true;
                    }
                    else if (titleScreen.TitleState == TitleScreen.TitleScreenState.CharacterSelect &&
                             titleScreen.CharacterSelectWindow != null &&
                             titleScreen.CharacterSelectWindow.DisplayPane != null &&
                             titleScreen.CharacterSelectWindow.DisplayPane.activeSelf)
                    {
                        SetLoginState(LoginState.SelectingCharacter, "Character select screen ready.", now);
                        return true;
                    }
                    return true;

                case LoginState.AwaitingWorldEntry:
                    if (titleScreen.NoticeBox != null && titleScreen.NoticeBox.activeSelf)
                    {
                        HandleServerNotice(titleScreen);
                        disconnectDetectedTime = now;
                        CurrentAttempt++;
                        SetLoginState(LoginState.WaitingCooldown, $"World entry error. Retrying in {BotConfigManager.Current.AutoReconnectDelaySeconds:F0}s...", now);
                        return true;
                    }

                    // If server opens character creator instead (e.g. empty slot)
                    bool isCreatorTriggered = titleScreen.TitleState == TitleScreen.TitleScreenState.CharacterCreation ||
                                              (titleScreen.CharacterCreatorWindow != null && titleScreen.CharacterCreatorWindow.Pane != null && titleScreen.CharacterCreatorWindow.Pane.activeSelf);
                    if (isCreatorTriggered)
                    {
                        SetLoginState(LoginState.CreatingCharacter, "Character creation window active...", now);
                        return true;
                    }

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
        catch (Exception ex)
        {
            BotEngine.Instance?.LogEvent($"[Login] Exception in ProcessLogin: {ex.Message}");
            return false;
        }
    }

        private static void HandleServerNotice(TitleScreen titleScreen)
        {
            string noticeText = titleScreen.NoticeBoxText != null ? titleScreen.NoticeBoxText.text : "";
            BotEngine.Instance?.LogEvent($"[Login] Server notice received: {noticeText}");

            if (noticeText.IndexOf("username or password were incorrect", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                BotEngine.Instance?.LogEvent("[Login] Unregistered credentials detected. Switching to account registration on next attempt.");
                BotConfigManager.Current.AutoCreateAccount = true;
            }
            else if (noticeText.IndexOf("already taken", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     noticeText.IndexOf("Failed to create login", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                BotEngine.Instance?.LogEvent("[Login] Account already exists. Switching to regular login on next attempt.");
                BotConfigManager.Current.AutoCreateAccount = false;
            }
        }

        private static void ClearNewAccountFlags()
        {
            if (BotConfigManager.Current.AutoCreateAccount)
            {
                BotConfigManager.Current.AutoCreateAccount = false;
                BotConfigManager.SaveConfig();
            }

            string targetProf = !string.IsNullOrEmpty(Services.ProfileManager.ActiveProfileName)
                ? Services.ProfileManager.ActiveProfileName
                : Services.ProfileManager.ExplicitCliProfile;

            if (!string.IsNullOrEmpty(targetProf) &&
                Services.AccountManager.TryGetCharacterForProfile(targetProf, out var acc, out _))
            {
                if (acc != null && acc.IsNewAccount)
                {
                    acc.IsNewAccount = false;
                    Services.AccountManager.SaveAccounts();
                }
            }
        }
    }
}
