# Super Battle Golf Item Pack

New items for Super Battle Golf. They show up in item boxes alongside the normal ones.

| Item | What it does |
|---|---|
| **Banana Peel** | Use to drop a peel at your feet; hold aim + use to throw one. Anyone who runs over a peel slips: a leg-sweep knockout that launches them forward (shields and knockout immunity still protect you). You can't slip on your own peel for the first 2 seconds. 3 peels per pickup. |
| **Portal Gun** | Use to place an orange portal where you're looking; aim + use to place a blue one. Each player has their own pair. You can see through linked portals, and players, balls, carts and items that go in come out the other portal with their momentum. Portals last until replaced or until the hole ends. 8 shots per pickup. |
| **Steam Whistle** | Blow it and a winged blue tank engine swoops in and sets a player on fire (a random other player by default). A tribute to the famous Skyrim mod. |

**Steam Whistle theme:** no music ships with the pack. To hear one during the attack, put an audio file named `thomas_theme.ogg` (or `.mp3`, `.wav`, `.flac`) in `BepInEx\plugins\ItemPack\SteamWhistle\`. Each player hears their own file, and it's never committed to this repo.

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
1. Make a model in Blender (see `blender/PortalGun/build_portal_gun.py` for a procedural example using the helpers in `blender/common.py`; run it headless). Export `<name>.obj`, `<name>.mtl` (Kd colors in sRGB, one material per color) and a 256px `<name>_icon.png` into `Assets/<ItemKey>/`.
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
| `src/Items/PortalGun/` | Portal Gun: placement (fits portals flat onto surfaces), host-relayed portal state, see-through rendering (a camera per portal into a render texture; the surface's UVs are recomputed each frame because the game can't load new shaders), momentum-keeping travel |
| `src/Items/SteamWhistle/` | Steam Whistle: host picks the target, every client plays the dragon's flight, fire particles and synthesized sounds; the target's own client applies the knockout |
| `src/Framework/ItemAudio.cs` | Plays sounds through the game's FMOD buses, so its volume sliders apply |
| `Assets/<Item>/` | Per-item models and icon, installed to `BepInEx\plugins\ItemPack\<Item>\` |
| `blender/` | Model scripts; run headless with `blender --background --factory-startup --python blender/<Item>/build_*.py` |
| `tools/package.ps1` | Builds the release zip |

## Credits
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) is bundled in release zips unmodified.
- Not affiliated with Brimstone. Super Battle Golf belongs to its developers.
