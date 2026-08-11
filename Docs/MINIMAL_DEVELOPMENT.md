# Development copy profiles

Development copy profiles import a selected subset of Ragnarok resources without deleting assets that are already present.

1. Extract an original Ragnarok `data.grf` with Korean filenames preserved.
2. Run `updateclient.bat` from the repository root.
3. Open `RebuildClient` in Unity.
4. Select **Ragnarok > Set Ragnarok Data Directory** and choose the extracted `data` directory.
5. Select **Ragnarok > Development Copy Profiles**. The existing **Minimal Development Copy** command opens the same window with Minimum selected.
6. Choose **Minimum**, **Medium**, or **Full**, then select **Import Profile**.
7. Optional: in the Lighting Manager window, bake the imported maps if you need baked lighting.
8. Start the server. For Minimum, use the **Minimal** launch profile: `./run-server.cmd Minimal`.
9. In Unity, select **Ragnarok > Open Main Scene**, enter Play mode, and connect normally.

Minimum imports `prt_fild08` and Novice resources. Medium adds every first job. Full performs the exhaustive client-data import and can take more than an hour. The Minimal server intentionally loads only `prt_fild08`.

## Custom profiles

Profiles live in `RebuildClient/Assets/Scripts/Editor/DevelopmentCopyProfiles`. Add another `.json` file there and reopen or refresh the profile window.

```json
{
  "name": "My Profile",
  "description": "Resources used by my feature.",
  "all": false,
  "resources": {
    "maps": ["prt_fild08"],
    "items": [501],
    "jobs": ["Novice"],
    "monsters": ["PORING"],
    "npcs": [],
    "effects": ["RedPotion"],
    "skills": [1]
  }
}
```

Use map codes, monster codes, job/effect names, and numeric item/NPC/skill IDs from the generated client configuration. Maps automatically add their monsters and NPCs; monsters add drops; NPCs add vendor items; jobs add inherited skills and equipment; effects add sounds. Every non-full profile also receives cursors, emotions, damage numbers, and a default hairstyle.

Set `"all": true` for an exhaustive profile. Resource lists are ignored in that mode. Applying a smaller profile after a larger one keeps the previously imported assets.
