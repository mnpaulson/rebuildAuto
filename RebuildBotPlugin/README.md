# Rebuild Automation Bot Plugin (BepInEx Client Mod)

A lightweight client automation plugin for **RagnarokRebuildTcp**, designed to inject directly into precompiled client builds without requiring full Unity project recompilation.

---

## Features

- ⚔️ **Auto-Attack**: Automatically scans for nearest attackable monsters and dispatches attack commands.
- 📦 **Auto-Loot**: Detects ground items within radius and picks them up.
- 🏃 **Auto-Wander**: Automatically wanders around map area when no targets or ground items are nearby.
- 🧪 **Auto-Potion**: Consumes HP and SP potions when character HP/SP falls below configured percentages.
- ⚙️ **Hybrid Configuration**:
  - **In-Game IMGUI Overlay**: Toggle bot state, view live status, target HP, kill count, and live action logs.
  - **External `bot_config.json`**: Easily edit target monster whitelists/blacklists, item filters, potion thresholds, search radii, and cooldown timers.
  - **Hot-Reloading**: Press `F5` or click "Reload Config" in-game to apply `bot_config.json` edits instantly.

---

## Hotkeys

| Hotkey | Action |
| :--- | :--- |
| `F9` | Show / Hide in-game Bot GUI Overlay window |
| `F10` | Master Toggle (Enable / Disable Automation Bot) |
| `F5` | Hot-Reload `bot_config.json` configuration file |

---

## Installation & Setup for Precompiled Client

1. **Download BepInEx**:
   - Download **BepInEx 5** (x64) for Unity Mono/IL2CPP from [BepInEx Releases](https://github.com/BepInEx/BepInEx/releases).
   - Extract BepInEx files into your precompiled game client directory (where `RebuildClient.exe` is located).

2. **Install Plugin**:
   - Copy `RebuildBotPlugin.dll` into the `BepInEx/plugins/` folder.
   - Copy `bot_config.json` into the root game directory (or `BepInEx/plugins/`).

3. **Launch Game**:
   - Start `RebuildClient.exe`.
   - The bot overlay window will render on screen (`F9` toggle).
   - Edit `bot_config.json` anytime and press `F5` in-game to apply changes immediately.

---

## Configuration Reference (`bot_config.json`)

```json
{
  "Enabled": true,
  "AutoAttack": true,
  "AutoLoot": true,
  "AutoWander": true,
  "AutoPotion": true,
  "HpPotionPercent": 50,
  "SpPotionPercent": 30,
  "HpPotionItemId": 501,
  "SpPotionItemId": 506,
  "SearchRadius": 18.0,
  "AttackRange": 2.0,
  "AttackCooldownSeconds": 0.4,
  "LootCooldownSeconds": 0.3,
  "WanderCooldownSeconds": 4.0,
  "WanderRadius": 8,
  "TargetMonsterWhitelist": ["Poring", "Fabre", "Lunatic"],
  "TargetMonsterBlacklist": [],
  "LootItemWhitelist": [],
  "LootItemBlacklist": []
}
```

---

## Building from Source

To compile the plugin DLL:
```bash
dotnet build RebuildBotPlugin/RebuildBotPlugin.csproj -c Release
```
Output artifact: `RebuildBotPlugin/bin/Release/netstandard2.1/RebuildBotPlugin.dll`
