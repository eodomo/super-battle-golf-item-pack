# Super Battle Golf Item Pack

**[Download the latest release](https://github.com/eodomo/super-battle-golf-item-pack/releases/latest)** and follow [Install](#install-players) below.

New items for Super Battle Golf. They show up in item boxes alongside the normal ones.

| Item | What it does |
|---|---|
| **Banana Peel** | Use to drop a peel at your feet; hold aim + use to throw one. Anyone who runs over a peel slips: a leg-sweep knockout that launches them forward (shields and knockout immunity still protect you). You can't slip on your own peel for the first 2 seconds. 3 peels per pickup. |

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
