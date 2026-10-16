# Agent notes for the Super Battle Golf Item Pack

Read README.md for the layout and how to add an item. These notes cover things that are costly to rediscover.

- Keep the list of items and outfit pieces secret: only SPOILERS.md names them. README, release notes and the repo description must not.
- Never commit game files, decompiled game code, or audio. Build/package/release: see README and `tools/package.ps1`; bump `Plugin.Version`, then `gh release create`.
- Testing keys exist only in `-p:DevTools=true` builds (F6 give item, F7 item menu, F9/F10 cosmetics probes, F11 teleport to the outfit house door, 0 renders front/side shots of the local player to `BepInEx\shots`). Close the game before building into its folder.
- Model everything through the Blender MCP server (`execute_blender_code` with `runpy.run_path` on the script). Keep the generator scripts in `blender/<Name>/` and write OBJ/PNG output to `Assets/<Name>/`. Shared helpers: `blender/common.py`.

## Making custom clothes (outfit pieces)

Code lives in `src/Cosmetics/`; `RangerSet.cs` is the worked example for a whole outfit (helmet, suit, boots, club).

**How the game works.** The outfit house menu lists everything under the Addressables label `cosmetics-metadata`. Each entry is a `PlayerCosmeticsMetadata` (category, icon, `model` reference, `variations` = the color swatches). Selecting an item loads the metadata, its model and each variation's texture by key. Loadouts sync as strings (`metadataKey` + `variationIndex`), so every player in a match needs the pack to see the pieces.

**Adding a piece to the menu.** `CosmeticCatalog` installs an Addressables locator + provider. Per piece:
1. Create the metadata with `RangerSet.NewMetadata(name, category, icon)` (it makes a runtime `ScriptableObject`, free, with a deterministic GUID from `CosmeticCatalog.MakeGuid`).
2. Register the model with `CosmeticCatalog.AddModel(guid, resolver)`, textures with `AddTexture`/`AddKey`, the metadata with `AddMetadata`. **Model and texture keys must be 32-hex GUID strings** (use `MakeGuid`), or `AssetReference.RuntimeKeyIsValid` rejects them.
3. Build models in `Prebuild()` at startup (called from `CosmeticCatalog.Install`). A first selection that has to wait for an async build loses the race and shows nothing.
4. Pieces cost nothing: `FreeCosmeticsPatch` makes `CosmeticsUnlocksManager.IsOwnedInternal` true for pack GUIDs.

**Per category, what the game expects.**
- **Hat / head / face:** a prefab with a `PlayerCosmeticObject` (set `modelDisable` for hair/hat/face/ears it hides, `allowedCosmetics` for what can be worn with it), parented under the head root. Colors use `variation.tintColor` on `materialIndex` (material 0 only keeps fixed-color parts like a visor). The root object is inactive in the game's own prefabs; ours is created inactive too (the game activates it).
- **Body (clothing) and shoes:** a prefab with a `SkinnedMeshRenderer`; the game swaps `sharedMesh` into the player's body/shoe renderer. The mesh must use the player's bone order and bindposes (body 64 bones, shoes 67). **No tint is applied to these**: color comes only from each variation's `textureOverride`, so generate one texture per color.
- **Club:** a plain `MeshFilter` + `MeshRenderer` prefab (a model along +Z from the grip, edge toward +Y). Textured per variation like body. **The root must be ACTIVE** (park it at y = -10000); the club path never activates its copy, unlike hats/bodies.
- All materials are cloned from a game material (`Custom/ToonShader`) so the lighting matches; copy `sharedMaterial` from a template cosmetic of the same category (see `RangerHelmet.cs`, `RangerSword.cs`).

**Getting game meshes (they are not CPU-readable).** `MeshReadback` reads vertices/normals/UVs/triangles back from the GPU; `SkinReadback` reads bone weights (`mesh.GetBoneWeightBuffer(SkinWeights.FourBones)`: per vertex 4 float weights then 4 int bone indices, 32 bytes). Build a new `Mesh` from the copy plus `source.bindposes`. This is how the suit and boots reuse the game's own shape at runtime without shipping any game art.

**Painting colors on a game mesh.** `UvBake` rasterises per-texel "region ids" from a 3D function over position/normal/hand-weight in the mesh's own UV layout (`RangerPatterns` holds the suit and boot rules in bind-pose model space; Y up, the player faces +Z, heights in meters from the feet). Region 0 is the swatch color; the other regions are fixed colors. One bake serves every color.

**Handy facts.** Variation `allowedTeams` limits colors in team matches (`Neither` = free-for-all only; red/blue map to the teams). Menu swatch color is `variation.menuColor`. Icons are sprites: render a PNG from Blender (`render_icon` in `common.py`) and load with `RangerIcons.FromFile`. Run `F9`/`F10` probes (`CosmeticsProbe*.cs`) to dump the game's bone lists, metadata, materials and export reference meshes/textures to `BepInEx\cosmetics_probe.txt` and `cosmetics_export\`.

**Testing in game.** Start a game (the Driving Range), press F11 to teleport to the outfit house door, E to enter. Tabs: hats, clothes, shoes, clubs. **Never change the maintainer's own loadout (slot 3); test in slot 8** and restore the selection afterwards. Clicking an already selected item unequips it.
