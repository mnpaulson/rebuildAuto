# Rebuild Automation Bot Plugin (BepInEx 6 IL2CPP)

A client automation and quality-of-life plugin for **RagnarokRebuild**, designed to inject directly into precompiled IL2CPP client builds without recompiling the game project.

> [!NOTE]
> **Repository Context**: This repository contains the complete original source trees for `RebuildClient` (Unity) and `RoRebuildServer` (server backend). These are maintained for code reference, reverse-engineering, and protocol analysis. **Our active build target is strictly this plugin (`RebuildBotPlugin`)**, which compiles independently against BepInEx 6 IL2CPP interop assemblies.

---

## Features

- ⚔️ **Auto-Attack**: Scans for the nearest valid monsters (respecting whitelists and blacklists) and attacks them.
- 📦 **Auto-Loot**: Detects ground items within radius and picks them up.
- 🗺️ **Intelligent Exploration & Heatmap Wander**:
  - Evaluates coldest sectors across the map based on time-since-visit and distance scoring.
  - Fluid arrival-driven movement: issues next step ~0.3s after arrival instead of static long delays.
  - Safe pathing: clamps movement steps to legal packet limits (`SharedConfig.MaxPathLength = 16`).
  - Automatic obstacle and terrain stuck detection.
- 🚪 **Warp Network & Cross-Map Travel (`AutoTravel`)**:
  - Contains an embedded network of **1,742 warp portals across 242 maps**.
  - Built-in Dijkstra pathfinding: automatically calculates multi-map routes to reach `TargetMap`.
  - **Portal Safety**: Avoids wandering into portals when exploring (`AvoidPortalsWhileWandering`).
- 🧪 **Auto-Potion**: Consumes configured HP potions when below threshold percentage.
- 🛡️ **Zero-Passthrough IMGUI**:
  - In-game compact GUI overlay with live player coordinates, target HP, and status.
  - Harmony hooks prevent mouse clicks on the overlay from passing through into character movement.
  - Dual-channel tactile click detection ensuring 100% button reliability in IL2CPP.
- 📋 **BepInEx Terminal Logging**: Dispatches all combat, loot, and exploration updates directly to the BepInEx terminal console.

---

## Hotkeys

| Hotkey | Action |
| :--- | :--- |
| **`F9`** | Show / Hide in-game Bot GUI Overlay window |
| **`F10`** | Master Toggle (Enable / Disable Automation Bot) |
| **`F5`** | Hot-Reload `bot_config.json` configuration from disk |

---

## Building & Deploying

### Method 1: Using `deploy.bat` (Recommended)
From the workspace root, run the deployment batch script:
```bat
.\deploy.bat           :: Builds Release DLL, checks if client is running, deploys to game folder
.\deploy.bat /copy     :: Deploys existing binary without rebuilding
.\deploy.bat /config   :: Force-syncs workspace bot_config.json to game folder
.\deploy.bat /run      :: Builds, deploys, and launches RebuildClient.exe automatically
```

### Method 2: MSBuild Single Command
```powershell
dotnet build RebuildBotPlugin/RebuildBotPlugin.csproj -c Release -p:DeployOnBuild=true
```

> [!WARNING]
> **Game Client Must Be Closed**: Windows locks loaded DLL files. If `RebuildClient.exe` is running, deployment will fail with a file lock error. Close the game client before deploying.

---

## Configuration Reference (`bot_config.json`)

Located in the game directory (e.g. `C:\games\RagnarokRebuild\bot_config.json`):

```json
{
  "Enabled": true,
  "AutoAttack": true,
  "AutoLoot": true,
  "AutoWander": true,
  "AutoPotion": true,
  "TargetMap": "prt_fild08",
  "AutoTravel": true,
  "AvoidPortalsWhileWandering": true,
  "PortalSafetyRadius": 5.0,
  "HpPotionPercent": 50,
  "HpPotionItemId": 512,
  "SearchRadius": 18.0,
  "AttackRange": 2.0,
  "AttackCooldownSeconds": 0.4,
  "LootCooldownSeconds": 0.3,
  "WanderCooldownSeconds": 4.0,
  "WanderRadius": 8,
  "TargetMonsterWhitelist": [],
  "TargetMonsterBlacklist": [
    "Target Dummy",
    "Creamy"
  ],
  "LootItemWhitelist": [],
  "LootItemBlacklist": []
}
```

---

## Technical Architecture & Agent Documentation

For deep technical insights, IL2CPP quirks, Harmony hook rules, and reverse-engineering details, refer to [AGENTS.md](../AGENTS.md).
