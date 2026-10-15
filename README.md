# Super Battle Golf Item Pack

**[Download the latest release](https://github.com/eodomo/super-battle-golf-item-pack/releases/latest)** and follow [Install](#install-players) below.

New items for Super Battle Golf. They show up in item boxes alongside the normal ones.

| Item | What it does |
|---|---|
| **Banana Peel** | Use to drop a peel at your feet; hold aim + use to throw one. Anyone who runs over a peel slips: a leg-sweep knockout that launches them forward (shields and knockout immunity still protect you). You can't slip on your own peel for the first 2 seconds. 3 peels per pickup. |
| **Portal Gun** | Left click places a blue portal where you're looking, right click an orange one, like Portal (with Portal's two-color crosshair). Each player has their own pair. You can see through linked portals, and players, balls, carts and items that go in come out the other portal with their momentum. Portals last until replaced or until the hole ends. 8 shots per pickup. |
| **Steam Whistle** | Blow it and a winged blue tank engine swoops in and sets a player on fire (a random other player by default). A tribute to the famous Skyrim mod. |
| **Sniper Rifle** | Halo's SRS99, recreated. Hold aim to scope in at 2x (scroll up or middle mouse for 10x) with a Halo-style scope view; use to fire. One shot knocks a player out and goes through up to 2 players. 4-round magazine that reloads itself (taking you out of the scope), 12 rounds per pickup. Everyone hears the shot across the map and sees the vapor trail. |
| **Predator Missile** | MW2's killstreak. Left click opens the laptop and launches: your view becomes the missile, coming down from high above. Steer with the mouse, left click or space boosts once. It explodes on whatever it hits with a big rocket blast; everyone else sees and hears it coming. Your golfer stands still while you fly. |
| **Kamehameha** | Hold left click to charge (up to 3 s), release to fire a beam where you're aiming. Everyone in it gets knocked out and launched (harder the longer you charged); golf balls get blasted too. Walls and terrain stop it. 2 per pickup. |
| **Dirt Blocks** | Minecraft building: a stack of 64. Right click places a block on the grid where you're looking (against the face of the block you're pointing at). Blocks are solid for players and balls. Swing your golf club through them, or left click them, to break them. |
| **Blue Shell** | Mario Kart. Use it and it goes after whoever's ball is closest to the hole (even you, if you're in the lead). It flies high over the course with its siren wailing for everyone to hear, circles over the leader and slams down: a big blue explosion on them, and their ball gets knocked away from the hole. In the Driving Range (no hole) it picks another player, or you if you're alone. |

**Steam Whistle theme:** no music ships with the pack. To hear one during the attack, put an audio file named `thomas_theme.ogg` (or `.mp3`, `.wav`, `.flac`) in `BepInEx\plugins\ItemPack\SteamWhistle\`. Each player hears their own file, and it's never committed to this repo.

**Sniper Rifle sounds:** the pack synthesizes its own shot, zoom and reload sounds. To use others (for example ones from your own copy of Halo), put `sniper_fire`, `sniper_zoom_in`, `sniper_zoom_out` and/or `sniper_reload` (`.ogg`, `.wav`, `.mp3` or `.flac`) in `BepInEx\plugins\ItemPack\SniperRifle\`. Each player uses their own files; they're never committed or shipped.

**Other replaceable sounds:** every new item synthesizes its own sounds, and each can be replaced the same way with a file in the item's folder: `predator_launch`, `predator_loop`, `predator_boost` (in `PredatorMissile\`), `kamehameha_charge` and `kamehameha_fire` (in `Kamehameha\`; a voice line works well for the charge), `dirt_place` and `dirt_break` (in `DirtBlocks\`), `blueshell_launch`, `blueshell_siren` and `blueshell_impact` (in `BlueShell\`). Audio files are git-ignored and never packaged.

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

For testing, the host can press **F7** to open a menu that gives any pack item or game item, or **F6** to get the next pack item. Both only work in the Driving Range, or in a match with the game's cheats (console commands) rule turned on. Turn them off with `[Debug] GiveKeyEnabled`.

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
| `src/Items/SniperRifle/` | Sniper Rifle: reuses the Elephant Gun's hit handling without its recoil dive; Harmony transpilers make the Railgun's scope checks accept it |
| `src/Items/Predator/` | Predator Missile: the pilot's client flies it (own camera drawn over the normal view, controls handed to the mouse) and streams its position; the host runs the rocket explosion on impact |
| `src/Items/Kamehameha/` | Kamehameha: charge and beam effects for everyone, the shooter finds who's in the beam, hit players launch themselves, the host pushes balls |
| `src/Items/DirtBlocks/` | Dirt Blocks: host-owned block grid, solid cube per block, placement against block faces, breaking from the local player's golf swing hitbox |
| `src/Items/BlueShell/` | Blue Shell: the host picks the leader by ball-to-hole distance and times the flight; every client flies the shell on the shared timing; the host runs the explosions on impact |
| `src/Framework/Updater/` | "Check for updates" button in the game's Settings menu: GitHub latest-release check, download, install of the plugin folder (the loaded DLL is set aside as *.old and cleaned up on the next launch) |
| `src/Framework/Synth.cs` | Small synthesizer the items use for their sound effects |
| `src/Framework/ItemMenu.cs` | The F7 give menu and F6 quick give (host only, Driving Range or cheats on) |
| `src/Framework/ItemAudio.cs` | Plays sounds through the game's FMOD buses, so its volume sliders apply |
| `Assets/<Item>/` | Per-item models and icon, installed to `BepInEx\plugins\ItemPack\<Item>\` |
| `blender/` | Model scripts; run headless with `blender --background --factory-startup --python blender/<Item>/build_*.py` |
| `tools/package.ps1` | Builds the release zip |

## Credits
- [BepInEx](https://github.com/BepInEx/BepInEx) (LGPL-2.1) is bundled in release zips unmodified.
- Not affiliated with Brimstone. Super Battle Golf belongs to its developers.
