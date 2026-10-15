# Super Battle Golf Item Pack

New items for Super Battle Golf. They show up in item boxes alongside the normal ones.

| Item | What it does |
|---|---|
| **Banana Peel** | Use to drop a peel at your feet; hold aim + use to throw one. Anyone who runs over a peel slips: a leg-sweep knockout that launches them forward (shields and knockout immunity still protect you). You can't slip on your own peel for the first 2 seconds. 3 peels per pickup. |

**Everyone in the lobby needs the same version of the pack**, including the host. Items sync through custom network messages that unmodded players don't understand.

## Install (players)
1. Download `SuperBattleGolfItemPack-vX.Y.Z.zip` from the [Releases](../../releases) page.
2. In Steam, right-click Super Battle Golf → **Manage → Browse local files**.
3. Extract the whole zip into that folder. `winhttp.dll` should end up next to `Super Battle Golf.exe`.
4. Launch the game.

The zip includes [BepInEx 5](https://github.com/BepInEx/BepInEx) (the mod loader), so you don't need to install anything else.

**Updating:** extract the new zip over the old one.
**Uninstalling:** delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt` and the `BepInEx` folder.

## Settings
`BepInEx\config\eodomo.superbattlegolf.itempack.cfg` is created on first launch. It has one section per item: item box chance, uses per pickup, held model size, and item-specific settings (e.g. slip strength). Item box chances use the host's settings.

The host can press **F6** to give themselves the next pack item. It only works in the Driving Range, or in a match with the game's cheats (console commands) rule turned on. Turn it off with `[Debug] GiveKeyEnabled`.

## Build
Requires the .NET SDK (8 or 9) and the game with BepInEx installed (the build references the game's DLLs; none are committed).

```
dotnet build ItemPack.csproj -c Release
```

The build copies the plugin and assets into the game's `BepInEx\plugins\ItemPack`. Close the game first, because it locks the DLL. If the game is installed somewhere else, pass `-p:GameDir="D:\path\to\Super Battle Golf"`.

To make a release zip (BepInEx + the plugin) in `dist\`:

```
pwsh tools\package.ps1
```

Bump `Plugin.Version` in `src/Plugin.cs` first; the zip is named after it.

## Adding an item
1. Make a model in Blender (see `blender/BananaPeel/build_banana_peel.py` for a procedural example, run through the Blender MCP server). Export `<name>.obj`, `<name>.mtl` (Kd colors in sRGB, one material per color) and a 256px `<name>_icon.png` into `Assets/<ItemKey>/`.
2. Add `src/Items/<ItemKey>/<ItemKey>Item.cs`, a subclass of `CustomItem`:
   - `Id`: unique, 101 and up (vanilla items use 0-17; never reuse an id).
   - `Key` (asset folder name), `DisplayName`, `AssetName`.
   - `TemplateItem`: the vanilla item to borrow animations and use rules from (e.g. `Landmine` for place/throw, `Coffee` for consumables).
   - `UseRoutine`: what happens on use. Call `ItemUse.ConsumeUse` when the item takes effect and `ItemUse.Finish` at the end.
   - Optional: `BindConfig`, `ConfigureItemData`, and network/lifetime hooks (`RegisterMessages`, `OnStartServer`, `OnStartClient`, `Tick`, ...).
3. Register it in `Plugin.Awake` with `ItemCatalog.Add(new <ItemKey>Item());`.

Do **not** add pack items to the game's `ItemPool`: `MatchSetupRules` indexes it by item id and would break.

## How it works
| Path | Role |
|---|---|
| `src/Plugin.cs` | Plugin entry point, the list of items, the per-frame runner, the F6 debug give |
| `src/Framework/CustomItem.cs` | Base class for items: config, assets, hooks |
| `src/Framework/ItemCatalog.cs` | Registers items, held equipment and names into the game's tables; item box rolls |
| `src/Framework/Patches.cs` | Harmony patches: item use, held model, drop (= discard), item boxes, network startup |
| `src/Framework/ModelAsset.cs`, `ObjLoader.cs` | OBJ/icon loading; materials cloned from the game's toon shader |
| `src/Framework/ItemUse.cs` | Use bookkeeping through the game's own anti-cheat validation |
| `src/Items/BananaPeel/` | Banana Peel: use routines, Mirror messages (host arbitrates spawns and slips), deterministic peel flight and slip detection |
| `Assets/<Item>/` | Per-item model and icon, installed to `BepInEx\plugins\ItemPack\<Item>\` |
| `tools/package.ps1` | Builds the release zip |

## Credits
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) is bundled in release zips unmodified.
- Not affiliated with Brimstone. Super Battle Golf belongs to its developers.
