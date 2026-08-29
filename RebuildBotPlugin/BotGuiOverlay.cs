using System;
using UnityEngine;

namespace RebuildBotPlugin
{
    public class BotGuiOverlay : MonoBehaviour
    {
        public static BotGuiOverlay Instance;

        public bool IsVisible = true;
        private bool showConfigHelpers = false;
        private Rect windowRect = new Rect(20, 20, 360, 480);

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                IsVisible = !IsVisible;
            }
            if (Input.GetKeyDown(KeyCode.F10))
            {
                BotConfigManager.Current.Enabled = !BotConfigManager.Current.Enabled;
                BotConfigManager.SaveConfig();
                if (BotEngine.Instance != null)
                {
                    BotEngine.Instance.LogEvent($"Bot master toggle switched: {(BotConfigManager.Current.Enabled ? "ENABLED" : "DISABLED")}");
                }
            }
            if (Input.GetKeyDown(KeyCode.F5))
            {
                BotConfigManager.LoadConfig();
                if (BotEngine.Instance != null)
                {
                    BotEngine.Instance.LogEvent("Hot-reloaded bot_config.json from disk.");
                }
            }
        }

        private void OnGUI()
        {
            if (!IsVisible) return;

            GUI.skin.window.fontSize = 13;
            GUI.skin.label.fontSize = 12;

            windowRect = GUI.Window(0x999, windowRect, DrawWindowContent, "Rebuild Automation Bot (F9: Hide | F10: Toggle)");
        }

        private void DrawWindowContent(int windowID)
        {
            GUILayout.BeginVertical();

            // Header Controls
            GUILayout.BeginHorizontal();
            bool newEnabled = GUILayout.Toggle(BotConfigManager.Current.Enabled, " Bot Enabled (F10)", GUILayout.Height(25));
            if (newEnabled != BotConfigManager.Current.Enabled)
            {
                BotConfigManager.Current.Enabled = newEnabled;
                BotConfigManager.SaveConfig();
            }

            if (GUILayout.Button("Reload Config (F5)", GUILayout.Height(25)))
            {
                BotConfigManager.LoadConfig();
                if (BotEngine.Instance != null)
                {
                    BotEngine.Instance.LogEvent("Reloaded config from disk.");
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            // Quick Toggles
            GUILayout.Label("<b>Quick Toggles</b>");
            GUILayout.BeginHorizontal();
            BotConfigManager.Current.AutoAttack = GUILayout.Toggle(BotConfigManager.Current.AutoAttack, "Attack");
            BotConfigManager.Current.AutoLoot = GUILayout.Toggle(BotConfigManager.Current.AutoLoot, "Loot");
            BotConfigManager.Current.AutoWander = GUILayout.Toggle(BotConfigManager.Current.AutoWander, "Wander");
            BotConfigManager.Current.AutoPotion = GUILayout.Toggle(BotConfigManager.Current.AutoPotion, "Potion");
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Status Monitor
            GUILayout.Label("<b>Status Monitor</b>");
            if (BotEngine.Instance != null)
            {
                GUILayout.Label($"Current Map: <b>{BotEngine.Instance.GetCurrentMapName()}</b>");
                GUILayout.Label($"Status: <b>{BotEngine.Instance.CurrentState}</b>");
                GUILayout.Label($"Target: <b>{BotEngine.Instance.CurrentTargetName}</b> ({BotEngine.Instance.CurrentTargetHp}/{BotEngine.Instance.CurrentTargetMaxHp} HP)");
                GUILayout.Label($"Kills: <b>{BotEngine.Instance.KillCount}</b> | Items Looted: <b>{BotEngine.Instance.LootCount}</b>");
            }
            else
            {
                GUILayout.Label("BotEngine not attached.");
            }

            GUILayout.Space(10);

            // Configuration Helpers Section
            showConfigHelpers = GUILayout.Toggle(showConfigHelpers, "<b> Configuration Assistant (Monsters & Potions)</b>");
            if (showConfigHelpers && BotEngine.Instance != null)
            {
                GUILayout.BeginVertical("box");

                // 1. Current Map Monsters Whitelist/Blacklist
                GUILayout.Label("<b>Active Map Monsters:</b>");
                var activeMonsters = BotEngine.Instance.GetActiveMonstersOnMap();
                if (activeMonsters.Count == 0)
                {
                    GUILayout.Label("<i>No active monsters detected nearby.</i>");
                }
                else
                {
                    foreach (var mName in activeMonsters)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(mName, GUILayout.Width(130));

                        if (GUILayout.Button("+ Whitelist", GUILayout.Width(80)))
                        {
                            if (!BotConfigManager.Current.TargetMonsterWhitelist.Contains(mName))
                            {
                                BotConfigManager.Current.TargetMonsterWhitelist.Add(mName);
                                BotConfigManager.SaveConfig();
                                BotEngine.Instance.LogEvent($"Added '{mName}' to Whitelist.");
                            }
                        }

                        if (GUILayout.Button("+ Blacklist", GUILayout.Width(80)))
                        {
                            if (!BotConfigManager.Current.TargetMonsterBlacklist.Contains(mName))
                            {
                                BotConfigManager.Current.TargetMonsterBlacklist.Add(mName);
                                BotConfigManager.SaveConfig();
                                BotEngine.Instance.LogEvent($"Added '{mName}' to Blacklist.");
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.Space(5);

                // 2. Inventory Potions & Items
                GUILayout.Label("<b>Inventory Items & Potions:</b>");
                var potions = BotEngine.Instance.GetInventoryPotionItems();
                if (potions.Count == 0)
                {
                    GUILayout.Label("<i>Inventory empty or unavailable.</i>");
                }
                else
                {
                    foreach (var p in potions)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"[{p.itemId}] {p.name} (x{p.count})", GUILayout.Width(170));

                        if (GUILayout.Button("Set HP Pot", GUILayout.Width(70)))
                        {
                            BotConfigManager.Current.HpPotionItemId = p.itemId;
                            BotConfigManager.SaveConfig();
                            BotEngine.Instance.LogEvent($"Set HP Potion ID to {p.itemId} ({p.name})");
                        }

                        if (GUILayout.Button("Set SP Pot", GUILayout.Width(70)))
                        {
                            BotConfigManager.Current.SpPotionItemId = p.itemId;
                            BotConfigManager.SaveConfig();
                            BotEngine.Instance.LogEvent($"Set SP Potion ID to {p.itemId} ({p.name})");
                        }
                        GUILayout.EndHorizontal();
                    }
                }

                GUILayout.EndVertical();
            }

            GUILayout.Space(10);

            // Action Log Console
            GUILayout.Label("<b>Action Log</b>");
            GUILayout.BeginVertical("box", GUILayout.Height(120));
            if (BotEngine.Instance != null)
            {
                var logs = BotEngine.Instance.GetLogEntries();
                for (int i = logs.Count - 1; i >= 0; i--)
                {
                    GUILayout.Label(logs[i]);
                }
            }
            GUILayout.EndVertical();

            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }
    }
}
