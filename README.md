# Super Battle Golf Item Pack

**[Download the latest release](https://github.com/eodomo/super-battle-golf-item-pack/releases/latest)** and follow [Install](#install-players) below.

A surprise pack of new items for Super Battle Golf. What's in it? Open some item boxes and find out. (Can't wait? [Spoilers](SPOILERS.md).)

**Everyone in the lobby needs the same version of the pack**, including the host. Items sync through custom network messages that unmodded players don't understand.

## Install (players)
For the Steam version of Super Battle Golf on Windows (Steam Deck / Linux: see Troubleshooting).

1. Download **`SuperBattleGolfItemPack-vX.Y.Z.zip`** from the [latest release](https://github.com/eodomo/super-battle-golf-item-pack/releases/latest) (under **Assets**).
2. In Steam, right-click **Super Battle Golf** → **Manage** → **Browse local files**. This opens the game folder, the one with `Super Battle Golf.exe` in it.
3. Extract the zip **into that folder**: right-click the zip → **Extract All...** → **Browse...** → pick the game folder → **Extract**. If Windows asks about replacing files, choose **Replace**.
4. Check that `winhttp.dll`, `doorstop_config.ini` and a `BepInEx` folder are now right next to `Super Battle Golf.exe`. If they landed in a subfolder (like `SuperBattleGolfItemPack-v0.2.0\`), move them up into the game folder.
5. Launch the game. The first launch takes a little longer while BepInEx sets itself up.

The zip includes [BepInEx 5](https://github.com/BepInEx/BepInEx) (the mod loader), so you don't need to install anything else.

**Everyone in the lobby needs the same version of the pack**, including the host.

**Did it work?** After launching, `BepInEx\LogOutput.log` exists in the game folder and mentions "Super Battle Golf Item Pack". From v0.2.0 on, **Settings** → **General** also shows an **Item Pack** row.

**Updating:** from v0.2.0 on, open **Settings** → **General** and click **Check for updates** in the Item Pack row: it downloads and installs the newest release, then restart the game. On older versions, download the new zip and extract it over the old install (choose **Replace**).

**Turning mods off for a while:** open `doorstop_config.ini` in the game folder and change `enabled = true` to `enabled = false` (back to `true` to turn them on again).

**Uninstalling:** delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt` and the `BepInEx` folder from the game folder.

### Troubleshooting
- **Nothing changed in the game / no `LogOutput.log`:** BepInEx didn't start. Usually the files ended up in a subfolder: `winhttp.dll` has to sit right next to `Super Battle Golf.exe` (step 4).
- **`winhttp.dll` disappeared:** some antivirus programs remove it. It's BepInEx's loader (open source); restore it from quarantine or allow it, then extract the zip again.
- **Steam Deck / Linux (Proton):** in Steam, open the game's **Properties** → **General** → **Launch options** and enter `WINEDLLOVERRIDES="winhttp=n,b" %command%` so Proton loads BepInEx.
- **Errors or disconnects when playing together:** make sure everyone (host included) has the pack and is on the same version.
- **Still stuck:** open an issue on this repo and attach `BepInEx\LogOutput.log`.

## Settings
`BepInEx\config\eodomo.superbattlegolf.itempack.cfg` is created on first launch. It has one section per item: how often item boxes give it, uses per pickup, held model size, and item-specific settings. Item box odds use the host's settings. The pack's items are about as rare as the game's best ones and, like those, mostly go to players who are behind. The host can adjust them per item in the match setup's item probability sliders, like the game's items; `ItemBoxFrequency` (1 = normal, 0 = never) sets their starting values.

## Build
Requires the .NET SDK (8 or 9) and the game with BepInEx installed (the build references the game's DLLs; none are committed).

```
dotnet build ItemPack.csproj -c Release
```

For testing, build with `-p:DevTools=true` to add two host-only keys: **F7** opens a menu that gives any pack item or game item, **F6** gives the next pack item (Driving Range, or a match with the cheats rule on). Releases are built without them.

The build copies the plugin and assets into the game's `BepInEx\plugins\ItemPack`. Close the game first, because it locks the DLL. If the game is installed somewhere else, pass `-p:GameDir="D:\path\to\Super Battle Golf"`.

To make a release zip (BepInEx + the plugin) in `dist\`:

```
pwsh tools\package.ps1
```

Bump `Plugin.Version` in `src/Plugin.cs` first; the zip is named after it.

## Adding an item
1. Make a model in Blender (see the `blender/<Item>/build_*.py` scripts for procedural examples using the helpers in `blender/common.py`; run them headless). Export `<name>.obj`, `<name>.mtl` (Kd colors in sRGB, one material per color) and a 256px `<name>_icon.png` into `Assets/<ItemKey>/`.
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
| `src/Plugin.cs` | Plugin entry point, the list of items, the per-frame runner, settings migration |
| `src/Framework/CustomItem.cs` | Base class for items: config, assets, hooks |
| `src/Framework/ItemCatalog.cs` | Registers items, held equipment and names into the game's tables |
| `src/Framework/ItemBoxes.cs` | Adds the items to the game's item box pools and to the match setup's probability sliders |
| `src/Framework/Patches.cs` | Harmony patches: item use, held model, aim/crosshair, item boxes, network startup |
| `src/Framework/ModelAsset.cs`, `ObjLoader.cs` | OBJ/icon loading; materials cloned from the game's toon shader |
| `src/Framework/ModelPuppet.cs` | Copies of a player's or cart's visible model (skinned bones included) that can be posed elsewhere, without reading the game's (non-readable) meshes |
| `src/Framework/ItemUse.cs` | Use bookkeeping through the game's own anti-cheat validation |
| `src/Items/<Item>/` | One folder per item ([spoilers](SPOILERS.md)) |
| `src/Framework/DroppedItems.cs` | Dropped pack items: the game's pickup is switched to the pack item and shows its model |
| `src/Framework/Updater/` | "Check for updates" button in the game's Settings menu: GitHub latest-release check, download, install of the plugin folder (the loaded DLL is set aside as *.old and cleaned up on the next launch) |
| `src/Framework/Synth.cs` | Small synthesizer the items use for their sound effects |
| `src/Framework/ItemMenu.cs` | Testing tools (only in `-p:DevTools=true` builds): F7 give menu and F6 quick give, host only, Driving Range or cheats on |
| `src/Framework/ItemAudio.cs` | Plays sounds through the game's FMOD buses, so its volume sliders apply |
| `Assets/<Item>/` | Per-item models and icon, installed to `BepInEx\plugins\ItemPack\<Item>\` |
| `blender/` | Model scripts; run headless with `blender --background --factory-startup --python blender/<Item>/build_*.py` |
| `tools/package.ps1` | Builds the release zip |

## Credits
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) is bundled in release zips unmodified.
- Not affiliated with Brimstone. Super Battle Golf belongs to its developers.
