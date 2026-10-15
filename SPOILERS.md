# Spoilers: what's in the pack

The README keeps the items a surprise. Everything is here instead.

| Item | What it does |
|---|---|
| **Banana Peel** | Use to drop a peel at your feet; hold aim + use to throw one. Anyone who runs over a peel slips: a leg-sweep knockout that launches them forward (shields and knockout immunity still protect you). You can't slip on your own peel for the first 2 seconds. One peel per pickup. |
| **Portal Gun** | Left click places a blue portal where you're looking, right click an orange one, like Portal (with Portal's two-color crosshair). Each player has their own pair. You can see through linked portals, and players, balls, carts and items that go in come out the other portal with their momentum. Portals last until replaced or until the hole ends. 8 shots per pickup. |
| **Steam Whistle** | Blow it and a winged blue tank engine swoops in and sets a player on fire (a random other player by default). A tribute to the famous Skyrim mod. |
| **Sniper Rifle** | Halo's SRS99, recreated. Hold aim to scope in at 2x (scroll up or middle mouse for 10x) with a Halo-style scope view; use to fire. One shot knocks a player out and goes through up to 2 players. One shot per pickup. Everyone hears the shot across the map and sees the vapor trail. |
| **Predator Missile** | MW2's killstreak. Left click opens the laptop and launches: your view becomes the missile, coming down from high above. Steer with the mouse, left click or space boosts once. It explodes on whatever it hits with a big rocket blast; everyone else sees and hears it coming. Your golfer stands still while you fly. |
| **Kamehameha** | Hold left click to charge (up to 3 s), release to fire a beam where you're aiming. Everyone in it gets knocked out and launched (harder the longer you charged); golf balls get blasted too. Walls and terrain stop it. One per pickup. |
| **Dirt Blocks** | Minecraft building: a stack of 64. Right click places a block on the grid where you're looking (against the face of the block you're pointing at). Blocks are solid for players and balls; a white outline shows where the next one goes. Any damage breaks them: golf swings, shots, explosions, beams (or left click them). |
| **Blue Shell** | Mario Kart. Use it and it goes after whoever's ball is closest to the hole (even you, if you're in the lead). It flies high over the course with its siren wailing for everyone to hear, circles over the leader and slams down: a big blue explosion on them, and their ball gets knocked away from the hole. In the Driving Range (no hole) it picks another player, or you if you're alone. |
| **Boogie Bomb** | Fortnite. Throw the disco ball (aim to throw where you're looking, otherwise it's lobbed forward). It bounces, pops into a disco party, and everyone caught in the blast does their victory dance for 5 seconds, unable to move or use items. Electromagnet shields protect you. One per pickup. |

## Replaceable sounds

**Steam Whistle theme:** the pack plays its own built-in anthem. To use different music, put an audio file named `thomas_theme.ogg` (or `.mp3`, `.wav`, `.flac`) in `BepInEx\plugins\ItemPack\SteamWhistle\`. Each player hears their own file, and it's never committed to this repo.

**Sniper Rifle sounds:** the pack synthesizes its own shot, zoom and reload sounds. To use others (for example ones from your own copy of Halo), put `sniper_fire`, `sniper_zoom_in`, `sniper_zoom_out` and/or `sniper_reload` (`.ogg`, `.wav`, `.mp3` or `.flac`) in `BepInEx\plugins\ItemPack\SniperRifle\`. Each player uses their own files; they're never committed or shipped.

**Other replaceable sounds:** every new item synthesizes its own sounds, and each can be replaced the same way with a file in the item's folder: `predator_launch`, `predator_loop`, `predator_boost` (in `PredatorMissile\`), `kamehameha_charge` and `kamehameha_fire` (in `Kamehameha\`; a voice line works well for the charge), `dirt_place` and `dirt_break` (in `DirtBlocks\`), `blueshell_launch`, `blueshell_siren` and `blueshell_impact` (in `BlueShell\`), `boogie_pop` and `boogie_music` (in `BoogieBomb\`; e.g. the real boogie bomb music). Audio files are git-ignored and never packaged.
