# Minimal development setup

This setup imports only the assets needed to develop and play on `prt_fild08` as a Novice. Use the normal client-data copy when you need the complete game.

1. Extract an original Ragnarok `data.grf` with Korean filenames preserved.
2. Run `updateclient.bat` from the repository root.
3. Open `RebuildClient` in Unity.
4. Select **Ragnarok > Set Ragnarok Data Directory** and choose the extracted `data` directory.
5. Select **Ragnarok > Minimal Development Copy** and wait for the import to finish.
6. Optional: in the Lighting Manager window, bake `prt_fild08` if you need baked lighting.
7. Start the server with the **RoRebuildServer (Minimal)** launch profile.
8. In Unity, select **Ragnarok > Open Main Scene**, enter Play mode, and connect normally.

The minimal server intentionally loads only `prt_fild08`. Other maps, jobs, monsters, equipment, and effects may be missing until the full client-data copy is run.
