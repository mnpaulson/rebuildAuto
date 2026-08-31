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

        public static void LogInfo(string msg)
        {
            Instance?.Log.LogInfo(msg);
        }

        public override void Load()
        {
            Instance = this;
            Log.LogInfo("Plugin com.rebuild.automation is loading (IL2CPP)...");

            Services.ProfileManager.InitializeFromCommandLine();
            BotConfigManager.LoadConfig();

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
                Log.LogInfo("Harmony patches applied successfully.");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Failed to apply Harmony patches: {ex.Message}");
            }

            Log.LogInfo("Rebuild Automation Bot initialized successfully!");
        }
    }
}
