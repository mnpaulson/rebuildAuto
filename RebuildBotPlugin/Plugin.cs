using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace RebuildBotPlugin
{
    [BepInPlugin("com.rebuild.automation", "Rebuild Automation Bot", "1.0.0")]
    public class Plugin : BasePlugin
    {
        public static Plugin Instance;
        private GameObject botContainer;
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        public static void LogInfo(string msg) => Services.BotLog.Info(msg);
        public static void LogWarning(string msg) => Services.BotLog.Warn(msg);
        public static void LogError(string msg) => Services.BotLog.Error(msg);
        public static void LogDebug(string msg) => Services.BotLog.Debug(msg);

        public override void Load()
        {
            Instance = this;

            Services.ProfileManager.InitializeFromCommandLine();
            BotConfigManager.LoadConfig();

            LogInfo("Plugin com.rebuild.automation is loading (IL2CPP)...");

            if (!string.IsNullOrWhiteSpace(Services.ProfileManager.ExplicitCliProfile))
            {
                BotConfigManager.Current.Enabled = true;
                LogInfo($"[Profile] Launched with explicit target '{Services.ProfileManager.ExplicitCliProfile}'. Bot automation automatically ENABLED.");
            }

            if (Services.ProfileManager.HiddenCliFlag)
            {
                try
                {
                    IntPtr consoleHwnd = GetConsoleWindow();
                    if (consoleHwnd != IntPtr.Zero)
                    {
                        ShowWindow(consoleHwnd, 0);
                    }
                }
                catch { }
            }

            if (Services.ProfileManager.LowSpecCliFlag)
            {
                BotConfigManager.Current.LowSpecMode = true;
                if (Services.ProfileManager.TargetFpsCli.HasValue)
                {
                    BotConfigManager.Current.TargetFrameRate = Services.ProfileManager.TargetFpsCli.Value;
                }
            }

            // Load embedded 1,700+ warp portal network
            WorldGraph.Instance.LoadEmbeddedWarps();

            // Register custom MonoBehaviours into IL2CPP domain
            ClassInjector.RegisterTypeInIl2Cpp<BotEngine>();
            ClassInjector.RegisterTypeInIl2Cpp<BotGuiOverlay>();

            botContainer = new GameObject("RebuildBotContainer");
            botContainer.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(botContainer);

            botContainer.AddComponent<BotEngine>();
            botContainer.AddComponent<BotGuiOverlay>();

            // Apply Harmony patches for click-passthrough protection
            try
            {
                var harmony = new HarmonyLib.Harmony("com.rebuild.automation");
                harmony.PatchAll();
                LogInfo("Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                LogWarning($"Failed to apply Harmony patches: {ex.Message}");
            }

            LogInfo("Rebuild Automation Bot initialized successfully!");
        }
    }
}
