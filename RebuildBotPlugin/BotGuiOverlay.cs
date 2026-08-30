using System;
using System.Collections.Generic;
using Assets.Scripts;
using UnityEngine;

namespace RebuildBotPlugin
{
    public class BotGuiOverlay : MonoBehaviour
    {
        public static BotGuiOverlay Instance;

        public static bool IsMouseOverOverlay = false;

        public bool IsVisible = true;
        private bool showConfigHelpers = false;
        private bool showHeatmapMonitor = false;
        private Texture2D heatmapGridTex = null;
        private string heatmapCachedMap = "";
        private float lastHeatmapTexUpdate = 0f;
        private int potionPageIndex = 0;
        private int monsterPageIndex = 0;
        private Rect windowRect = new Rect(20, 20, 370, 260);

        private bool isDragging = false;
        private Vector2 dragOffset;

        private Vector2 currentGuiMousePos;
        private bool leftClickThisFrame = false;

        private struct LayoutCursor
        {
            public float StartX;
            public float CurrentY;
            public float ContentWidth;

            public LayoutCursor(float x, float y, float width)
            {
                StartX = x;
                CurrentY = y;
                ContentWidth = width;
            }

            public Rect Next(float height = 20)
            {
                Rect r = new Rect(StartX, CurrentY, ContentWidth, height);
                CurrentY += height + 4;
                return r;
            }

            public void Space(float px = 6)
            {
                CurrentY += px;
            }
        }

        public BotGuiOverlay(IntPtr ptr) : base(ptr) { }

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
                BotEngine.Instance?.LogEvent($"Bot master toggle switched: {(BotConfigManager.Current.Enabled ? "ENABLED" : "DISABLED")}");
            }
            if (Input.GetKeyDown(KeyCode.F5))
            {
                BotConfigManager.LoadConfig();
                BotEngine.Instance?.LogEvent("Hot-reloaded bot_config.json from disk.");
            }

            // Calculate GUI mouse coordinates (origin at top-left)
            Vector2 mousePos = Input.mousePosition;
            float flippedY = Screen.height - mousePos.y;
            currentGuiMousePos = new Vector2(mousePos.x, flippedY);

            IsMouseOverOverlay = IsVisible && windowRect.Contains(currentGuiMousePos);

            if (Input.GetMouseButtonDown(0))
            {
                leftClickThisFrame = true;
            }

            // Window Dragging via Input fallback
            Rect headerRect = new Rect(windowRect.x, windowRect.y, windowRect.width - 25, 24);
            if (Input.GetMouseButtonDown(0) && headerRect.Contains(currentGuiMousePos))
            {
                isDragging = true;
                dragOffset = currentGuiMousePos - new Vector2(windowRect.x, windowRect.y);
            }
            else if (Input.GetMouseButton(0) && isDragging)
            {
                windowRect.x = currentGuiMousePos.x - dragOffset.x;
                windowRect.y = currentGuiMousePos.y - dragOffset.y;
            }
            else if (Input.GetMouseButtonUp(0))
            {
                isDragging = false;
            }
        }

        private void LateUpdate()
        {
            leftClickThisFrame = false;
        }

        private bool CustomButton(Rect rect, string label, bool isActive = false)
        {
            return CustomButton(rect, label, isActive, new Color(0.2f, 0.85f, 0.35f, 1f));
        }

        private bool CustomButton(Rect rect, string label, bool isActive, Color activeColor)
        {
            Color oldBg = GUI.backgroundColor;
            if (isActive)
            {
                GUI.backgroundColor = activeColor;
            }
            else
            {
                GUI.backgroundColor = new Color(0.25f, 0.25f, 0.25f, 1f);
            }

            GUI.Box(rect, label);
            GUI.backgroundColor = oldBg;

            // 1. Check Event.current in IMGUI
            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }

            // 2. Check Input.GetMouseButtonDown
            if (leftClickThisFrame && rect.Contains(currentGuiMousePos))
            {
                leftClickThisFrame = false;
                return true;
            }

            return false;
        }

        private bool DrawToggleButton(Rect rect, string label, bool state)
        {
            string display = state ? $"<b>[ON]</b> {label}" : $"[OFF] {label}";
            if (CustomButton(rect, display, state))
            {
                state = !state;
                BotEngine.Instance?.LogEvent($"Toggled {label}: {(state ? "ON" : "OFF")}");
            }
            return state;
        }

        private void OnGUI()
        {
            if (!IsVisible) return;

            Event e = Event.current;

            // Window Header Dragging in IMGUI
            Rect headerRect = new Rect(windowRect.x, windowRect.y, windowRect.width - 25, 24);
            if (e.type == EventType.MouseDown && e.button == 0 && headerRect.Contains(e.mousePosition))
            {
                isDragging = true;
                dragOffset = e.mousePosition - new Vector2(windowRect.x, windowRect.y);
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && isDragging)
            {
                windowRect.x = e.mousePosition.x - dragOffset.x;
                windowRect.y = e.mousePosition.y - dragOffset.y;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                isDragging = false;
            }

            // Adjust window height dynamically based on expanded sections
            float targetHeight = 355 + (showHeatmapMonitor ? 195 : 0) + (showConfigHelpers ? 285 : 0);
            windowRect.height = targetHeight;

            // Draw main window background box
            GUI.Box(windowRect, "<b>Rebuild Automation Bot</b> (F9: Hide | F10: Toggle)");

            // Close button (X) in top right
            if (CustomButton(new Rect(windowRect.x + windowRect.width - 24, windowRect.y + 3, 20, 18), "X"))
            {
                IsVisible = false;
            }

            float startX = windowRect.x + 10;
            float startY = windowRect.y + 26;
            float contentW = windowRect.width - 20;
            LayoutCursor cursor = new LayoutCursor(startX, startY, contentW);

            // Row 1: Master Enable, Debug Log, & Reload Config
            Rect r1 = cursor.Next(24);
            bool currentEnabled = BotConfigManager.Current.Enabled;
            bool newEnabled = DrawToggleButton(new Rect(r1.x, r1.y, 130, 24), "Bot (F10)", currentEnabled);
            if (newEnabled != currentEnabled)
            {
                BotConfigManager.Current.Enabled = newEnabled;
                BotConfigManager.SaveConfig();
            }

            bool currentVerbose = BotConfigManager.Current.VerboseLogging;
            bool newVerbose = DrawToggleButton(new Rect(r1.x + 135, r1.y, 115, 24), "Debug Log", currentVerbose);
            if (newVerbose != currentVerbose)
            {
                BotConfigManager.Current.VerboseLogging = newVerbose;
                BotConfigManager.SaveConfig();
                BotEngine.Instance?.LogEvent($"Debug Logging {(newVerbose ? "ENABLED" : "DISABLED")}.");
            }

            if (CustomButton(new Rect(r1.x + 255, r1.y, contentW - 255, 24), "Reload (F5)"))
            {
                BotConfigManager.LoadConfig();
                BotEngine.Instance?.LogEvent("Reloaded config from disk.");
            }

            cursor.Space(4);

            // Row 2: Quick Toggles Header
            GUI.Label(cursor.Next(18), "<b>Quick Toggles:</b>");

            // Row 3: Primary Function Toggles
            Rect rToggles = cursor.Next(24);
            float togW = (contentW - 20) / 5f;

            bool newAttack = DrawToggleButton(new Rect(rToggles.x, rToggles.y, togW, 24), "Attack", BotConfigManager.Current.AutoAttack);
            if (newAttack != BotConfigManager.Current.AutoAttack)
            {
                BotConfigManager.Current.AutoAttack = newAttack;
                BotConfigManager.SaveConfig();
            }

            bool newLoot = DrawToggleButton(new Rect(rToggles.x + togW + 5, rToggles.y, togW, 24), "Loot", BotConfigManager.Current.AutoLoot);
            if (newLoot != BotConfigManager.Current.AutoLoot)
            {
                BotConfigManager.Current.AutoLoot = newLoot;
                BotConfigManager.SaveConfig();
            }

            bool newWander = DrawToggleButton(new Rect(rToggles.x + (togW + 5) * 2, rToggles.y, togW, 24), "Wander", BotConfigManager.Current.AutoWander);
            if (newWander != BotConfigManager.Current.AutoWander)
            {
                BotConfigManager.Current.AutoWander = newWander;
                BotConfigManager.SaveConfig();
            }

            bool newPotion = DrawToggleButton(new Rect(rToggles.x + (togW + 5) * 3, rToggles.y, togW, 24), "Potion", BotConfigManager.Current.AutoPotion);
            if (newPotion != BotConfigManager.Current.AutoPotion)
            {
                BotConfigManager.Current.AutoPotion = newPotion;
                BotConfigManager.SaveConfig();
            }

            bool newAggro = DrawToggleButton(new Rect(rToggles.x + (togW + 5) * 4, rToggles.y, togW, 24), "Aggro 1st", BotConfigManager.Current.PrioritizeAggressiveMonsters);
            if (newAggro != BotConfigManager.Current.PrioritizeAggressiveMonsters)
            {
                BotConfigManager.Current.PrioritizeAggressiveMonsters = newAggro;
                BotConfigManager.SaveConfig();
            }

            cursor.Space(3);

            // Row 4: Quick Toggles - Respawn, Travel, Avoid, HP Wing, Auto-Sit
            Rect rToggles2 = cursor.Next(24);
            float togW2 = (contentW - 20) / 5f;

            bool newRespawn = DrawToggleButton(new Rect(rToggles2.x, rToggles2.y, togW2, 24), "Respawn", BotConfigManager.Current.AutoRespawn);
            if (newRespawn != BotConfigManager.Current.AutoRespawn)
            {
                BotConfigManager.Current.AutoRespawn = newRespawn;
                BotConfigManager.SaveConfig();
            }

            bool newTravel = DrawToggleButton(new Rect(rToggles2.x + togW2 + 5, rToggles2.y, togW2, 24), "Travel", BotConfigManager.Current.AutoTravel);
            if (newTravel != BotConfigManager.Current.AutoTravel)
            {
                BotConfigManager.Current.AutoTravel = newTravel;
                BotConfigManager.SaveConfig();
            }

            bool newAvoid = DrawToggleButton(new Rect(rToggles2.x + (togW2 + 5) * 2, rToggles2.y, togW2, 24), "Avoid", BotConfigManager.Current.AutoAvoidMonsters);
            if (newAvoid != BotConfigManager.Current.AutoAvoidMonsters)
            {
                BotConfigManager.Current.AutoAvoidMonsters = newAvoid;
                BotConfigManager.SaveConfig();
            }

            bool newWing = DrawToggleButton(new Rect(rToggles2.x + (togW2 + 5) * 3, rToggles2.y, togW2, 24), "HP Wing", BotConfigManager.Current.EmergencyFlyWingOnLowHp);
            if (newWing != BotConfigManager.Current.EmergencyFlyWingOnLowHp)
            {
                BotConfigManager.Current.EmergencyFlyWingOnLowHp = newWing;
                BotConfigManager.SaveConfig();
            }

            bool newRest = DrawToggleButton(new Rect(rToggles2.x + (togW2 + 5) * 4, rToggles2.y, togW2, 24), "Auto-Sit", BotConfigManager.Current.AutoSitToRecover);
            if (newRest != BotConfigManager.Current.AutoSitToRecover)
            {
                BotConfigManager.Current.AutoSitToRecover = newRest;
                BotConfigManager.SaveConfig();
            }

            cursor.Space(6);

            // Status Monitor
            GUI.Label(cursor.Next(18), "<b>Status Monitor:</b>");
            if (BotEngine.Instance != null)
            {
                Vector2Int pos = BotEngine.Instance.GetPlayerPosition();
                GUI.Label(cursor.Next(18), $"Map: <b>{BotEngine.Instance.GetCurrentMapName()}</b> | Coordinates: <b>({pos.x}, {pos.y})</b>");
                GUI.Label(cursor.Next(18), $"Status: <b>{BotEngine.Instance.CurrentState}</b>");
                GUI.Label(cursor.Next(18), $"Target: <b>{BotEngine.Instance.CurrentTargetName}</b> ({BotEngine.Instance.CurrentTargetHp}/{BotEngine.Instance.CurrentTargetMaxHp} HP)");
                GUI.Label(cursor.Next(18), $"Kills: <b>{BotEngine.Instance.KillCount}</b> | Items Looted: <b>{BotEngine.Instance.LootCount}</b>");

                cursor.Space(4);

                // Experience & Rates Monitor
                var tracker = BotEngine.Instance.ExpTracker;
                Rect expHeader = cursor.Next(18);
                string sessionTime = $"{tracker.ElapsedTime.Hours:D2}:{tracker.ElapsedTime.Minutes:D2}:{tracker.ElapsedTime.Seconds:D2}";
                GUI.Label(new Rect(expHeader.x, expHeader.y, contentW - 70, 18), $"<b>EXP Tracker</b> (Session: {sessionTime}):");
                if (CustomButton(new Rect(expHeader.x + contentW - 65, expHeader.y, 65, 18), "Reset"))
                {
                    tracker.Reset();
                    BotEngine.Instance.LogEvent("EXP session stats reset.");
                }

                string baseRate = ExpTracker.FormatExp(tracker.BaseExpPerHour);
                string baseGained = ExpTracker.FormatExp(tracker.SessionBaseExpGained);
                string baseTtl = ExpTracker.FormatTtl(tracker.TimeToNextBaseLevel);
                GUI.Label(cursor.Next(18), $"Base: <b>+{baseGained}</b> ({baseRate}/h) | TTL: <b>{baseTtl}</b>");

                string jobRate = ExpTracker.FormatExp(tracker.JobExpPerHour);
                string jobGained = ExpTracker.FormatExp(tracker.SessionJobExpGained);
                string jobTtl = ExpTracker.FormatTtl(tracker.TimeToNextJobLevel);
                GUI.Label(cursor.Next(18), $"Job:  <b>+{jobGained}</b> ({jobRate}/h) | TTL: <b>{jobTtl}</b>");
            }
            else
            {
                GUI.Label(cursor.Next(18), "<i>BotEngine not attached.</i>");
            }

            cursor.Space(6);

            // Wander Heatmap Monitor Section
            Rect rHeatmapHeader = cursor.Next(20);
            string heatmapToggleText = showHeatmapMonitor ? "[-] Hide Wander Heatmap Monitor" : "[+] Show Wander Heatmap Monitor";
            if (CustomButton(rHeatmapHeader, heatmapToggleText))
            {
                showHeatmapMonitor = !showHeatmapMonitor;
            }

            if (showHeatmapMonitor && BotEngine.Instance != null)
            {
                cursor.Space(3);
                string currentMap = BotEngine.Instance.GetCurrentMapName();
                Vector2Int playerPos = BotEngine.Instance.GetPlayerPosition();
                Vector2Int currentWaypoint = BotEngine.Instance.CurrentExplorationWaypoint;

                UpdateHeatmapTexture(currentMap, playerPos, currentWaypoint);

                float distToWp = currentWaypoint != Vector2Int.zero ? Vector2.Distance(playerPos, currentWaypoint) : 0f;
                string wpStr = currentWaypoint != Vector2Int.zero ? $"({currentWaypoint.x}, {currentWaypoint.y}) [Dist: {distToWp:F0}t]" : "None";
                GUI.Label(cursor.Next(18), $"Active Waypoint: <b>{wpStr}</b>");

                // Side-by-side area: Grid on left (110x110), candidates table on right
                Rect rArea = cursor.Next(112);
                float gridDim = 110f;
                Rect rGrid = new Rect(rArea.x, rArea.y, gridDim, gridDim);
                GUI.Box(rGrid, GUIContent.none);
                if (heatmapGridTex != null)
                {
                    GUI.DrawTexture(new Rect(rGrid.x + 2, rGrid.y + 2, gridDim - 4, gridDim - 4), heatmapGridTex);
                }

                float infoX = rArea.x + gridDim + 8;
                float infoW = contentW - gridDim - 8;
                LayoutCursor rightCursor = new LayoutCursor(infoX, rArea.y, infoW);

                GUI.Label(rightCursor.Next(17), "<b>Top Cold Candidates:</b>");
                var candidates = MapHeatmap.Instance.GetTopCandidateSectors(currentMap, playerPos, BotConfigManager.Current.PortalSafetyRadius, 4, BotEngine.Instance.LastWanderHeading);

                if (candidates.Count == 0)
                {
                    GUI.Label(rightCursor.Next(18), "<i>No candidates available.</i>");
                }
                else
                {
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var c = candidates[i];
                        string ageStr = c.IsVisited ? $"{c.LastVisitedAge / 60f:F1}m ago" : "Never";
                        GUI.Label(rightCursor.Next(17), $"#{i + 1} ({c.Sector.x * 16}, {c.Sector.y * 16}) | <b>{ageStr}</b> | {c.Distance:F0}t");
                    }
                }

                // Legend row
                Rect rLegend = cursor.Next(18);
                GUI.Label(rLegend, "<color=#FFFFFF>■ Player</color>  <color=#FFEE11>■ Target</color>  <color=#1AC6F2>■ Cold</color>  <color=#F23326>■ Hot</color>  <color=#404040>■ Block</color>");
                cursor.Space(4);
            }

            cursor.Space(4);

            // Config Quick-Setup Section
            Rect rConfigHeader = cursor.Next(20);
            string toggleText = showConfigHelpers ? "[-] Hide Area Helpers" : "[+] Show Area Helpers (Monsters & Items)";
            if (CustomButton(rConfigHeader, toggleText))
            {
                showConfigHelpers = !showConfigHelpers;
            }

            if (showConfigHelpers && BotEngine.Instance != null)
            {
                cursor.Space(4);

                // Monsters in Area
                var activeMonsters = BotEngine.Instance.GetActiveMonstersOnMap();
                int totalMonsters = activeMonsters.Count;
                int monPerPage = 4;
                int totalMonPages = Math.Max(1, (int)Math.Ceiling(totalMonsters / (float)monPerPage));
                if (monsterPageIndex >= totalMonPages) monsterPageIndex = totalMonPages - 1;
                if (monsterPageIndex < 0) monsterPageIndex = 0;

                Rect rMonHeader = cursor.Next(20);
                GUI.Label(new Rect(rMonHeader.x, rMonHeader.y, 220, 20), $"<b>Monsters in Area ({totalMonsters}):</b>");

                if (totalMonPages > 1)
                {
                    if (CustomButton(new Rect(rMonHeader.x + 230, rMonHeader.y, 25, 18), "<"))
                    {
                        if (monsterPageIndex > 0) monsterPageIndex--;
                    }
                    GUI.Label(new Rect(rMonHeader.x + 260, rMonHeader.y, 50, 18), $"{monsterPageIndex + 1}/{totalMonPages}");
                    if (CustomButton(new Rect(rMonHeader.x + 315, rMonHeader.y, 25, 18), ">"))
                    {
                        if (monsterPageIndex < totalMonPages - 1) monsterPageIndex++;
                    }
                }

                if (totalMonsters == 0)
                {
                    GUI.Label(cursor.Next(18), "<i>No monsters detected in area yet.</i>");
                }
                else
                {
                    int startMon = monsterPageIndex * monPerPage;
                    int endMon = Math.Min(startMon + monPerPage, totalMonsters);
                    for (int i = startMon; i < endMon; i++)
                    {
                        string mName = activeMonsters[i];
                        Rect mRow = cursor.Next(22);
                        GUI.Label(new Rect(mRow.x, mRow.y, 120, 22), mName);

                        bool isWhite = BotConfigManager.Current.TargetMonsterWhitelist.Contains(mName);
                        bool isBlack = BotConfigManager.Current.TargetMonsterBlacklist.Contains(mName);
                        bool isAvoid = BotConfigManager.Current.MonsterAvoidanceList.Contains(mName);

                        if (CustomButton(new Rect(mRow.x + 125, mRow.y, 65, 20), "White", isWhite, new Color(0.2f, 0.85f, 0.35f, 1f)))
                        {
                            if (isWhite)
                            {
                                BotConfigManager.Current.TargetMonsterWhitelist.Remove(mName);
                                BotEngine.Instance?.LogEvent($"Removed '{mName}' from Whitelist.");
                            }
                            else
                            {
                                BotConfigManager.Current.TargetMonsterBlacklist.Remove(mName);
                                BotConfigManager.Current.MonsterAvoidanceList.Remove(mName);
                                BotConfigManager.Current.TargetMonsterWhitelist.Add(mName);
                                BotEngine.Instance?.LogEvent($"Added '{mName}' to Whitelist.");
                            }
                            BotConfigManager.SaveConfig();
                        }

                        if (CustomButton(new Rect(mRow.x + 195, mRow.y, 65, 20), "Black", isBlack, new Color(0.85f, 0.25f, 0.25f, 1f)))
                        {
                            if (isBlack)
                            {
                                BotConfigManager.Current.TargetMonsterBlacklist.Remove(mName);
                                BotEngine.Instance?.LogEvent($"Removed '{mName}' from Blacklist.");
                            }
                            else
                            {
                                BotConfigManager.Current.TargetMonsterWhitelist.Remove(mName);
                                BotConfigManager.Current.MonsterAvoidanceList.Remove(mName);
                                BotConfigManager.Current.TargetMonsterBlacklist.Add(mName);
                                BotEngine.Instance?.LogEvent($"Added '{mName}' to Blacklist.");
                            }
                            BotConfigManager.SaveConfig();
                        }

                        if (CustomButton(new Rect(mRow.x + 265, mRow.y, 65, 20), "Avoid", isAvoid, new Color(0.95f, 0.55f, 0.15f, 1f)))
                        {
                            if (isAvoid)
                            {
                                BotConfigManager.Current.MonsterAvoidanceList.Remove(mName);
                                BotEngine.Instance?.LogEvent($"Removed '{mName}' from Avoidance List.");
                            }
                            else
                            {
                                BotConfigManager.Current.TargetMonsterWhitelist.Remove(mName);
                                BotConfigManager.Current.TargetMonsterBlacklist.Remove(mName);
                                BotConfigManager.Current.MonsterAvoidanceList.Add(mName);
                                BotEngine.Instance?.LogEvent($"Added '{mName}' to Avoidance List.");
                            }
                            BotConfigManager.SaveConfig();
                        }
                    }
                }

                cursor.Space(6);

                // Potions & Consumables
                var potions = BotEngine.Instance.GetInventoryPotionItems();
                int totalItems = potions.Count;
                int itemsPerPage = 5;
                int totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (float)itemsPerPage));
                if (potionPageIndex >= totalPages) potionPageIndex = totalPages - 1;
                if (potionPageIndex < 0) potionPageIndex = 0;

                Rect rPotHeader = cursor.Next(20);
                GUI.Label(new Rect(rPotHeader.x, rPotHeader.y, 220, 20), $"<b>Inventory Items & Potions ({totalItems}):</b>");

                if (totalPages > 1)
                {
                    if (CustomButton(new Rect(rPotHeader.x + 230, rPotHeader.y, 25, 18), "<"))
                    {
                        if (potionPageIndex > 0) potionPageIndex--;
                    }
                    GUI.Label(new Rect(rPotHeader.x + 260, rPotHeader.y, 50, 18), $"{potionPageIndex + 1}/{totalPages}");
                    if (CustomButton(new Rect(rPotHeader.x + 315, rPotHeader.y, 25, 18), ">"))
                    {
                        if (potionPageIndex < totalPages - 1) potionPageIndex++;
                    }
                }

                if (totalItems == 0)
                {
                    GUI.Label(cursor.Next(18), "<i>No items detected in inventory.</i>");
                }
                else
                {
                    int startIndex = potionPageIndex * itemsPerPage;
                    int endIndex = Math.Min(startIndex + itemsPerPage, totalItems);

                    // Mouse wheel scrolling support over the inventory items section
                    Rect invSectionRect = new Rect(windowRect.x + 10, rPotHeader.y, contentW, 20 + (endIndex - startIndex) * 24);
                    if (e.type == EventType.ScrollWheel && invSectionRect.Contains(e.mousePosition))
                    {
                        if (e.delta.y > 0 && potionPageIndex < totalPages - 1)
                        {
                            potionPageIndex++;
                            e.Use();
                        }
                        else if (e.delta.y < 0 && potionPageIndex > 0)
                        {
                            potionPageIndex--;
                            e.Use();
                        }
                    }

                    for (int i = startIndex; i < endIndex; i++)
                    {
                        var p = potions[i];
                        Rect pRow = cursor.Next(22);
                        bool isHpPotion = BotConfigManager.Current.HpPotionItemIds.Contains(p.itemId);

                        string nameDisplay = p.isUsable ? $"<b>{p.name}</b>" : p.name;
                        GUI.Label(new Rect(pRow.x, pRow.y, 185, 22), $"[{p.itemId}] {nameDisplay} (x{p.count})");

                        // 1. HP Toggle Button (Green when active, dark gray when inactive)
                        if (CustomButton(new Rect(pRow.x + 190, pRow.y, 45, 20), "HP", isHpPotion, new Color(0.2f, 0.85f, 0.35f, 1f)))
                        {
                            if (isHpPotion)
                            {
                                BotConfigManager.Current.HpPotionItemIds.Remove(p.itemId);
                                BotEngine.Instance?.LogEvent($"Removed [{p.itemId}] {p.name} from HP items.");
                            }
                            else
                            {
                                BotConfigManager.Current.HpPotionItemIds.Add(p.itemId);
                                BotEngine.Instance?.LogEvent($"Added [{p.itemId}] {p.name} to HP items.");
                            }
                            BotConfigManager.SaveConfig();
                        }

                        // 2. Sell / Store / Keep Cycle Button
                        string disp = "Keep";
                        if (!string.IsNullOrEmpty(p.name) && BotConfigManager.Current.ItemRules.TryGetValue(p.name, out var customDisp))
                        {
                            disp = customDisp;
                        }

                        Color dispColor;
                        if (string.Equals(disp, "Sell", StringComparison.OrdinalIgnoreCase))
                            dispColor = new Color(0.95f, 0.8f, 0.2f, 1f);
                        else if (string.Equals(disp, "Store", StringComparison.OrdinalIgnoreCase))
                            dispColor = new Color(0.2f, 0.85f, 0.35f, 1f);
                        else
                            dispColor = new Color(0.25f, 0.65f, 0.95f, 1f);

                        if (CustomButton(new Rect(pRow.x + 240, pRow.y, 85, 20), $"[{disp}]", true, dispColor))
                        {
                            string nextDisp;
                            if (string.Equals(disp, "Keep", StringComparison.OrdinalIgnoreCase))
                                nextDisp = "Sell";
                            else if (string.Equals(disp, "Sell", StringComparison.OrdinalIgnoreCase))
                                nextDisp = "Store";
                            else
                                nextDisp = "Keep";

                            BotConfigManager.Current.ItemRules[p.name] = nextDisp;
                            BotConfigManager.SaveConfig();
                            BotEngine.Instance?.LogEvent($"Set disposition for '{p.name}' to [{nextDisp}].");
                        }
                    }
                }
                cursor.Space(6);
            }

            // Consume mouse events inside window rect so nothing falls through IMGUI
            if (IsMouseOverOverlay && e.isMouse && e.type != EventType.Repaint && e.type != EventType.Layout)
            {
                e.Use();
            }
        }

        private void UpdateHeatmapTexture(string map, Vector2Int playerPos, Vector2Int currentWaypoint)
        {
            float now = Time.time;
            if (now - lastHeatmapTexUpdate < 0.4f && string.Equals(heatmapCachedMap, map, StringComparison.OrdinalIgnoreCase) && heatmapGridTex != null)
                return;

            lastHeatmapTexUpdate = now;
            MapHeatmap.Instance.GetSectorGridDimensions(out int maxSx, out int maxSy);
            int texW = Mathf.Max(maxSx + 1, 1);
            int texH = Mathf.Max(maxSy + 1, 1);

            if (heatmapGridTex == null || heatmapGridTex.width != texW || heatmapGridTex.height != texH)
            {
                heatmapGridTex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
                heatmapGridTex.filterMode = FilterMode.Point;
                heatmapCachedMap = map;
            }

            var visits = MapHeatmap.Instance.GetSectorVisitsCopy();
            var blacklists = MapHeatmap.Instance.GetUnreachableSectorsCopy();
            Vector2Int playerSec = MapHeatmap.Instance.GetSectorKey(playerPos);
            Vector2Int waypointSec = currentWaypoint != Vector2Int.zero ? MapHeatmap.Instance.GetSectorKey(currentWaypoint) : new Vector2Int(-1, -1);

            Color colorUnvisited = new Color(0.1f, 0.7f, 0.95f, 0.9f); // Cyan
            Color colorCool = new Color(0.2f, 0.75f, 0.3f, 0.9f);      // Green
            Color colorWarm = new Color(0.9f, 0.75f, 0.1f, 0.9f);     // Yellow
            Color colorHot = new Color(0.95f, 0.2f, 0.15f, 0.95f);    // Red
            Color colorBlacklist = new Color(0.25f, 0.25f, 0.25f, 0.95f); // Dark Gray
            Color colorWaypoint = new Color(1f, 0.9f, 0.05f, 1f);       // Gold
            Color colorPlayer = Color.white;

            for (int y = 0; y < texH; y++)
            {
                for (int x = 0; x < texW; x++)
                {
                    Vector2Int sec = new Vector2Int(x, y);

                    if (sec == playerSec)
                    {
                        heatmapGridTex.SetPixel(x, y, colorPlayer);
                    }
                    else if (sec == waypointSec)
                    {
                        heatmapGridTex.SetPixel(x, y, colorWaypoint);
                    }
                    else if (blacklists.TryGetValue(sec, out float expire) && now < expire)
                    {
                        heatmapGridTex.SetPixel(x, y, colorBlacklist);
                    }
                    else if (visits.TryGetValue(sec, out float visitTime))
                    {
                        float age = now - visitTime;
                        if (age < 30f)
                            heatmapGridTex.SetPixel(x, y, colorHot);
                        else if (age < 90f)
                            heatmapGridTex.SetPixel(x, y, colorWarm);
                        else
                            heatmapGridTex.SetPixel(x, y, colorCool);
                    }
                    else
                    {
                        heatmapGridTex.SetPixel(x, y, colorUnvisited);
                    }
                }
            }

            heatmapGridTex.Apply();
        }
    }
}
