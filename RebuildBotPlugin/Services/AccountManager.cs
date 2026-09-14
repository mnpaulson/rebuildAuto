using System;
using System.IO;
using System.Text.Json;
using RebuildBotPlugin.Models;

namespace RebuildBotPlugin.Services
{
    /// <summary>
    /// Service that loads and manages accounts.json.
    /// Provides credential lookup and character mapping across accounts.
    /// </summary>
    public static class AccountManager
    {
        public const string DevAccountsPath = @"c:\dev\rebuildAuto\RebuildBotPlugin\accounts.json";
        public static readonly string GameDirAccountsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "accounts.json");

        public static AccountRegistry Registry { get; private set; } = new AccountRegistry();
        private static bool isLoaded = false;

        public static string ResolveAccountsPath()
        {
            if (File.Exists(DevAccountsPath)) return DevAccountsPath;
            if (File.Exists(GameDirAccountsPath)) return GameDirAccountsPath;
            string gameRoot = @"C:\Games\RagnarokRebuild\accounts.json";
            if (File.Exists(gameRoot)) return gameRoot;
            return GameDirAccountsPath;
        }

        public static void LoadAccounts()
        {
            try
            {
                string targetPath = ResolveAccountsPath();
                if (File.Exists(targetPath))
                {
                    string json = File.ReadAllText(targetPath);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        Registry = JsonSerializer.Deserialize<AccountRegistry>(json, options) ?? new AccountRegistry();
                        isLoaded = true;
                        BotLog.Info($"[Accounts] Loaded {Registry.Accounts.Count} account(s) from '{Path.GetFileName(targetPath)}'.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                BotLog.Warn($"[Accounts Warning] Failed to load accounts.json: {ex.Message}");
            }

            Registry = new AccountRegistry();
        }

        public static void SaveAccounts()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
                string json = JsonSerializer.Serialize(Registry, options);

                string primaryPath = ResolveAccountsPath();
                File.WriteAllText(primaryPath, json);

                // Mirror if both exist
                if (File.Exists(DevAccountsPath) && !string.Equals(primaryPath, DevAccountsPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(DevAccountsPath, json);
                }
                if (File.Exists(GameDirAccountsPath) && !string.Equals(primaryPath, GameDirAccountsPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText(GameDirAccountsPath, json);
                }
            }
            catch (Exception ex)
            {
                BotLog.Warn($"[Accounts Warning] Failed to save accounts.json: {ex.Message}");
            }
        }

        public static bool TryGetCredentialsForProfile(
            string profileOrCharName,
            out string username,
            out string password,
            out int slot,
            out string characterName)
        {
            username = "";
            password = "";
            slot = 0;
            characterName = "";

            if (!isLoaded) LoadAccounts();
            if (Registry == null || string.IsNullOrWhiteSpace(profileOrCharName)) return false;

            if (Registry.TryGetAccountForProfile(profileOrCharName, out var account, out var character))
            {
                username = account.Username;
                password = account.Password;
                slot = character != null ? character.Slot : 0;
                characterName = character != null ? character.Name : "";
                return true;
            }

            return false;
        }

        public static bool TryGetCharacterForProfile(string profileOrCharName, out AccountEntry account, out CharacterEntry character)
        {
            account = null;
            character = null;
            if (!isLoaded) LoadAccounts();
            if (Registry == null || string.IsNullOrWhiteSpace(profileOrCharName)) return false;
            return Registry.TryGetAccountForProfile(profileOrCharName, out account, out character);
        }

        public static bool TryGetCredentialsForAccount(string accountId, out string username, out string password)
        {
            username = "";
            password = "";
            if (!isLoaded) LoadAccounts();
            if (Registry == null || string.IsNullOrWhiteSpace(accountId)) return false;

            if (Registry.TryGetAccountById(accountId, out var account))
            {
                username = account.Username;
                password = account.Password;
                return true;
            }

            return false;
        }
    }
}
