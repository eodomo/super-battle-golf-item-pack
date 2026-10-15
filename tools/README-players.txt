Super Battle Golf Item Pack
===========================

New items for Super Battle Golf:

  Banana Peel   - Use to drop a peel, hold aim + use to throw one. Anyone who runs
                  over it slips and goes flying. 3 peels per pickup.
  Portal Gun    - Left click places a blue portal, right click an orange one. See
                  through them; anything that goes in keeps its momentum.
  Steam Whistle - Blow it and a winged tank engine swoops in and sets a player
                  on fire.
  Sniper Rifle  - Halo style. Hold aim to scope in (scroll for 10x), use to
                  fire. One shot knocks someone out from across the map.
  Predator      - Click to launch, then steer the missile with the mouse; click
                  again to boost. Big explosion.
  Kamehameha    - Hold left click to charge, release to fire a beam that
                  launches everyone it hits.
  Dirt Blocks   - Right click to place blocks (Minecraft style). Break them by
                  swinging your golf club through them or left clicking them.
  Blue Shell    - Goes after whoever's ball is closest to the hole and blows
                  them (and their lead) up.
  Boogie Bomb   - Throw it: everyone caught in the blast does their victory
                  dance for a few seconds.

Steam Whistle music: put your own audio file named thomas_theme.ogg (or .mp3,
.wav, .flac) in BepInEx\plugins\ItemPack\SteamWhistle\ to hear it during the
attack. None is included.

Sniper Rifle sounds: to replace the built-in ones, put sniper_fire,
sniper_zoom_in, sniper_zoom_out and/or sniper_reload (.ogg/.wav/.mp3/.flac) in
BepInEx\plugins\ItemPack\SniperRifle\. None are included.

The items show up in item boxes alongside the normal ones.

EVERYONE in the lobby needs the same version of this pack, including the host.

Install
-------
1. In Steam, right-click Super Battle Golf > Manage > Browse local files.
2. Extract the whole zip into that folder (winhttp.dll should end up next to
   "Super Battle Golf.exe").
3. Launch the game.

Updating
--------
In the game: Settings > General > "Check for updates" (next to "Item Pack").
It downloads and installs the newest release; restart the game afterwards.

Uninstall
---------
Delete winhttp.dll, doorstop_config.ini, .doorstop_version, changelog.txt and
the BepInEx folder from the game folder.

Settings
--------
After the first launch, BepInEx\config\eodomo.superbattlegolf.itempack.cfg holds
the settings (item box chances, uses per pickup, slip strength, ...). Item box
chances use the host's settings.

For testing, the host can press F7 for a menu that gives any item, or F6 for the
next pack item. Both only work in the Driving Range or when the match has
cheats turned on.
