using System;
using System.IO;
using RebuildBotPlugin.Controllers;
using UnityEngine;

namespace RebuildBotPlugin.Services
{
    /// <summary>
    /// Manages multi-bot profiles, CLI arguments, and directory isolation.
    /// Resolves file paths for bot_config.json, bot_macro.json, and macro_status.json.
    /// </summary>
    public static class ProfileManager
    {
        public static string ActiveProfileName { get; private set; } = "";
        public static string ExplicitCliProfile { get; private set; }
        public static string ExplicitCliAccount { get; private set; }
        public static bool LowSpecCliFlag { get; private set; } = false;
        public static int? TargetFpsCli { get; private set; }

        public const string DevBaseDirectory = @"c:\dev\rebuildAuto\RebuildBotPlugin";
        public static readonly string GameBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        private static bool isInitialized = false;

        public static void InitializeFromCommandLine()
        {
            if (isInitialized) return;
            isInitialized = true;

            try
            {
                string[] args = Environment.GetCommandLineArgs();
                if (args != null && args.Length > 0)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        string arg = args[i].Trim();

                        if ((arg.Equals("-profile", StringComparison.OrdinalIgnoreCase) ||
                             arg.Equals("--profile", StringComparison.OrdinalIgnoreCase) ||
                             arg.Equals("-character", StringComparison.OrdinalIgnoreCase) ||
                             arg.Equals("--character", StringComparison.OrdinalIgnoreCase)) &&
                            i + 1 < args.Length)
                        {
                            ExplicitCliProfile = args[i + 1].Trim().Trim('\"', '\'');
                            i++;
                        }
                        else if ((arg.Equals("-account", StringComparison.OrdinalIgnoreCase) ||
                                  arg.Equals("--account", StringComparison.OrdinalIgnoreCase)) &&
                                 i + 1 < args.Length)
                        {
                            ExplicitCliAccount = args[i + 1].Trim().Trim('\"', '\'');
                            i++;
                        }
                        else if (arg.Equals("-lowspec", StringComparison.OrdinalIgnoreCase) ||
                                 arg.Equals("--lowspec", StringComparison.OrdinalIgnoreCase))
                        {
                            LowSpecCliFlag = true;
                        }
                        else if ((arg.Equals("-fps", StringComparison.OrdinalIgnoreCase) ||
                                  arg.Equals("--fps", StringComparison.OrdinalIgnoreCase)) &&
                                 i + 1 < args.Length)
                        {
                            if (int.TryParse(args[i + 1], out int fps))
                            {
                                TargetFpsCli = fps;
                            }
                            i++;
                        }
                    }
                }

                // If CLI specified a profile, activate it immediately
                if (!string.IsNullOrWhiteSpace(ExplicitCliProfile))
                {
                    SetActiveProfile(ExplicitCliProfile, forceReloadConfig: false);
                    Plugin.LogInfo($"[Profile] Initialized with CLI Profile: '{ActiveProfileName}'");
                }
                else
                {
                    Plugin.LogInfo("[Profile] No CLI profile specified. Running in default root profile mode.");
                }

                AccountManager.LoadAccounts();
            }
            catch (Exception ex)
            {
                Plugin.LogInfo($"[Profile Warning] Failed to parse command line args: {ex.Message}");
            }
        }

        public static void OnCharacterIdentified(string characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName)) return;

            // If an explicit CLI profile was supplied, keep using that profile
            if (!string.IsNullOrWhiteSpace(ExplicitCliProfile))
            {
                return;
            }

            // Otherwise, switch active profile to the logged-in character name
            if (!string.Equals(ActiveProfileName, characterName, StringComparison.OrdinalIgnoreCase))
            {
                Plugin.LogInfo($"[Profile] In-game character identified as '{characterName}'. Switching active profile...");
                SetActiveProfile(characterName, forceReloadConfig: true);
            }
        }

        public static void SetActiveProfile(string profileName, bool forceReloadConfig = true)
        {
            if (string.IsNullOrWhiteSpace(profileName))
            {
                ActiveProfileName = "";
                return;
            }

            ActiveProfileName = profileName.Trim();
            EnsureProfileDirectoryExists(ActiveProfileName);

            if (forceReloadConfig)
            {
                BotConfigManager.LoadConfig();
            }
        }

        public static string GetConfigPath()
        {
            if (string.IsNullOrWhiteSpace(ActiveProfileName))
            {
                return GetDefaultOrFallbackPath("bot_config.json");
            }

            string profileConfigDev = Path.Combine(DevBaseDirectory, "profiles", ActiveProfileName, "bot_config.json");
            string profileConfigGame = Path.Combine(GameBaseDirectory, "profiles", ActiveProfileName, "bot_config.json");

            if (File.Exists(profileConfigDev)) return profileConfigDev;
            if (File.Exists(profileConfigGame)) return profileConfigGame;

            // If in dev environment, default to dev profile path
            if (Directory.Exists(DevBaseDirectory)) return profileConfigDev;
            return profileConfigGame;
        }

        public static string GetMacroPath()
        {
            if (string.IsNullOrWhiteSpace(ActiveProfileName))
            {
                return GetDefaultOrFallbackPath("bot_macro.json");
            }

            string profileMacroDev = Path.Combine(DevBaseDirectory, "profiles", ActiveProfileName, "bot_macro.json");
            string profileMacroGame = Path.Combine(GameBaseDirectory, "profiles", ActiveProfileName, "bot_macro.json");

            if (File.Exists(profileMacroDev)) return profileMacroDev;
            if (File.Exists(profileMacroGame)) return profileMacroGame;

            if (Directory.Exists(DevBaseDirectory)) return profileMacroDev;
            return profileMacroGame;
        }

        public static string GetStatusPath()
        {
            if (string.IsNullOrWhiteSpace(ActiveProfileName))
            {
                return GetDefaultOrFallbackPath("macro_status.json");
            }

            string profileStatusDev = Path.Combine(DevBaseDirectory, "profiles", ActiveProfileName, "macro_status.json");
            string profileStatusGame = Path.Combine(GameBaseDirectory, "profiles", ActiveProfileName, "macro_status.json");

            if (Directory.Exists(DevBaseDirectory)) return profileStatusDev;
            return profileStatusGame;
        }

        private static string GetDefaultOrFallbackPath(string fileName)
        {
            string devPath = Path.Combine(DevBaseDirectory, fileName);
            string gamePath = Path.Combine(GameBaseDirectory, fileName);

            if (File.Exists(devPath)) return devPath;
            if (File.Exists(gamePath)) return gamePath;

            if (Directory.Exists(DevBaseDirectory)) return devPath;
            return gamePath;
        }

        private static void EnsureProfileDirectoryExists(string profileName)
        {
            try
            {
                if (Directory.Exists(DevBaseDirectory))
                {
                    string devDir = Path.Combine(DevBaseDirectory, "profiles", profileName);
                    if (!Directory.Exists(devDir))
                    {
                        Directory.CreateDirectory(devDir);

                        // If profile config doesn't exist, clone from root sample or config
                        string devConfig = Path.Combine(devDir, "bot_config.json");
                        string rootConfig = Path.Combine(DevBaseDirectory, "bot_config.json");
                        string sampleConfig = Path.Combine(DevBaseDirectory, "bot_config.sample.json");

                        if (!File.Exists(devConfig))
                        {
                            if (File.Exists(rootConfig)) File.Copy(rootConfig, devConfig, true);
                            else if (File.Exists(sampleConfig)) File.Copy(sampleConfig, devConfig, true);
                        }
                    }
                }

                if (Directory.Exists(GameBaseDirectory))
                {
                    string gameDir = Path.Combine(GameBaseDirectory, "profiles", profileName);
                    if (!Directory.Exists(gameDir))
                    {
                        Directory.CreateDirectory(gameDir);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.LogInfo($"[Profile Warning] Note creating profile directory for '{profileName}': {ex.Message}");
            }
        }
    }
}
