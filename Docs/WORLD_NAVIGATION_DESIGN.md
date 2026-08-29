# Global Map Navigation & Pathfinding Architecture

This document details the design and implementation of autonomous cross-map navigation, local A* pathfinding, and smart wandering for the **RebuildBotPlugin**.

---

## 1. Overview & Components

To enable full autonomy across the game world, the bot utilizes a 2-tier pathfinding architecture:

```
+-------------------------------------------------------------------+
|                       High-Level World Router                     |
|  (BFS / Dijkstra over Map Interconnectivity Graph from Warp Scripts) |
+-------------------------------------------------------------------+
                                  |
                                  v
                    [Sequence of Map Transitions]
                                  |
                                  v
+-------------------------------------------------------------------+
|                        Low-Level Tile Router                      |
|       (Grid A* Pathfinding using RagnarokWalkData Walkable Mesh)  |
+-------------------------------------------------------------------+
```

---

## 2. World Interconnectivity Graph (`WorldGraph`)

### Data Source
Warp portal scripts located in `RoRebuildServer/GameConfig/ServerData/Script/Warps/**/*.txt` define all map portals in the format:
```csharp
Warp("from_map", "warp_id", [name], fromX, fromY, width, height, "dest_map", destX, destY);
```

### Data Structure
```csharp
public class WarpConnection
{
    public string FromMap;
    public Vector2Int FromPos;
    public int Width;
    public int Height;
    public string DestMap;
    public Vector2Int DestPos;
}

public class WorldGraph
{
    // Key: MapName -> List of outgoing warp portals
    public Dictionary<string, List<WarpConnection>> MapNodeGraph;

    public List<WarpConnection> FindMapRoute(string startMap, string targetMap);
}
```

---

## 3. High-Level Navigation State Machine

```
               +-------------------+
               |       IDLE        |
               +-------------------+
                         |
                         v
      +--------------------------------------+
      | Check CurrentMap vs TargetMap        |
      +--------------------------------------+
           /                            \
 (Current != Target)               (Current == Target)
         /                                \
        v                                  v
+-----------------------+        +------------------------+
| Cross-Map Travel Mode |        | Local Farming / Wander |
+-----------------------+        +------------------------+
        |                                  |
        | 1. FindMapRoute(Start, Target)   | 1. Safe Wander (Avoid Portals)
        | 2. Set Local Goal = Next WarpPos | 2. Target Monsters / Loot
        | 3. Local Grid A* to WarpPos      | 3. Auto-Combat
        | 4. Step onto Warp Portal         |
        | 5. Wait for Map Load             |
        v                                  v
  [Next Map Loaded]                   [Farming Active]
```

---

## 4. Tile-Level Pathfinding (`RagnarokWalkData`)

- **Walkable Check**:
  ```csharp
  bool isWalkable = (walkData.Cell(pos).Type & CellType.Walkable) == CellType.Walkable;
  ```
- **A* Path Execution**:
  Uses `Pathfinder.BuildPath(walkData, startCell, targetCell)` to generate step-by-step waypoint paths.

---

## 5. Sector Heatmap Exploration Engine (`MapHeatmap.cs`)

To maximize map coverage and minimize retreading:
- **Grid Segmentation**: Each map is partitioned into **16x16 tile sectors**.
- **Visit Timestamp Tracking**: `MapHeatmap` tracks the exact timestamp when the player enters each sector key `(sectorX, sectorY)`.
- **Coldest Sector Targeting**: When wandering, `MapHeatmap.Instance.FindColdestSectorTarget()` identifies the least-recently visited sector within range, filtering out sectors near warp portals.
- **Dynamic Combat Interruption**: If a monster or ground item appears en route to a target sector, combat and looting take immediate precedence. Once resolved, `BotEngine` updates the heatmap for the player's new location and routes towards the next coldest sector relative to current coordinates.

---

## 6. Configuration Integration (`bot_config.json`)

```json
{
  "TargetMap": "prt_fild08",
  "AutoTravel": true,
  "AvoidPortalsWhileWandering": true,
  "PortalSafetyRadius": 5.0
}
```

---

## 7. Next Steps for Implementation

1. **Warp Data Exporter / Parser**: Parse warp `.txt` files into `WorldGraph.cs`.
2. **Navigation Manager**: Implement `WorldNavigator.cs` for cross-map pathing.
3. **Wander & Portal Guard**: Integrate `IsNearPortal` into `BotEngine.cs` wander logic.
