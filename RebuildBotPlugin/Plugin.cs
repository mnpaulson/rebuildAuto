using System;
using BepInEx;
using UnityEngine;

namespace RebuildBotPlugin
{
    [BepInPlugin("com.rebuild.automation", "Rebuild Automation Bot", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private GameObject botContainer;

        private void Awake()
        {
            Logger.LogInfo("Plugin com.rebuild.automation is loading...");
            BotConfigManager.LoadConfig();

            // Load embedded 1,700+ warp portal network (zero external directory dependency)
            WorldGraph.Instance.LoadEmbeddedWarps();

            // Optional fallback/override from disk
            string warpDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "RoRebuildServer", "GameConfig", "ServerData", "Script", "Warps");
            if (System.IO.Directory.Exists(warpDir))
            {
                WorldGraph.Instance.LoadWarpDirectory(warpDir);
            }

            botContainer = new GameObject("RebuildBotContainer");
            DontDestroyOnLoad(botContainer);

            botContainer.AddComponent<BotEngine>();
            botContainer.AddComponent<BotGuiOverlay>();

            Logger.LogInfo("Rebuild Automation Bot initialized successfully!");
        }
    }
}
