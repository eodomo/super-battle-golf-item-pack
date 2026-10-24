# Item checklist

What each item is meant to do, as defined by the maintainer on 2026-10-24, with pass/fail checks. `SPOILERS.md` is the player-facing description; this file is the acceptance list. Terms are in `GLOSSARY.md`.

## Global rules

- Teams don't matter; nobody plays teams.
- Dropped pack items behave like any dropped item in the game.
- Driving Range and a real match behave the same, except item probabilities.
- A miss spends the use. Exceptions: Lightsaber swings, and a Lightsaber or Captain America's Shield throw that hits terrain (it returns and the use is kept). A throw that hits nothing at all is lost.
- The Jellyfish Net is not an exception: a swing that catches nobody spends it.
- As much as possible passes through portals.
- Bubble Shield and Captain America's Shield are as effective as the game's electromagnet shield.

## Per item

Status column: **OK** matches the definition on paper; **CHANGE** needs code; **VERIFY** check in game.

| Item | Definition | Status |
|---|---|---|
| Banana Peel | Drop it; a player or cart that goes over it slips. Any damage (shots, explosions, beams, saber) clears the peel. | DONE (code): damage clears landed peels; thrown peel uses portals. Test in game. |
| Boogie Bomb | Thrown; on detonation nearby players dance for a few seconds. | VERIFY |
| Ender Pearl | Thrown; teleport to where it lands, purple particles at the teleport. | VERIFY |
| Dirt Blocks | First click starts a timer; unlimited blocks until it ends; any damage breaks a block. | VERIFY |
| Bubble Shield | Blocks all damage; golf balls and carts can't pass through the dome (either side); players on foot can walk in and out. | DONE (code): carts and the Megazord bounce off the dome. Test in game. |
| Sniper Rifle | Zoom, scroll for more zoom; one shot, a big hit (knockout); no cap on players it passes through. | DONE (code): no player cap; goes through portals. Test in game. |
| Kamehameha | Right click aims (reticle); hold left click to charge; levitate while charging only; hit = elimination; timer wheel appears at full power and reaches zero when it auto-fires (3 s charge + 4 s). | DONE (code): timer wheel at full power. VERIFY: eliminates all in line |
| Predator Missile | Missile comes down from the sky and you steer it; other players drawn with red boxes. | VERIFY: boxes may already be a game feature; add only if missing |
| Blue Shell | Flies to the player whose ball is closest to the hole and explodes on them. | OK / VERIFY |
| Steam Whistle | Summons the dragon engine on a target chosen by the crosshair lock-on (same marker as the orbital laser); target is breathed on with fire. | OK / VERIFY |
| Lightsaber | Left click swings (unlimited until it hits a player or cart); right click aims, left click while aiming throws; thrown saber returns unless it hits a player or cart; hit = elimination. | OK / VERIFY |
| Captain America's Shield | Hold right click to block hits from in front (a block uses it); right click also aims, left click while aiming throws; terrain bounce returns it; hitting a player or cart uses it and knocks them out (players in carts are knocked out of the cart); a throw that hits nothing is lost. | DONE (code): block only while right click is held (front arc 70 deg); throw only while aiming. VERIFY: riders ejected |
| Jellyfish Net | Left click swipes and scoops a player in front; a swipe that catches nobody spends the item; you walk slower with a player in the net; left click throws them. | OK / VERIFY |
| Web Slinger | Click starts a timer; unlimited webs until it ends; swings you like Spider-Man; webbing a player, cart or item pulls it toward you. Web goes through portals. | DONE (code): web through portals; SPOILERS line added. Test in game. |
| Megazord | Slower cart reskin with its own hitbox and model; seats two; left click attack knocks back whatever it hits. | OK / VERIFY |
| Portal Gun | Left click blue, right click orange; see through; velocity kept; every weapon works through them; a use is spent when both a blue and an orange are placed; same-color or invalid-surface shots are free. | DONE (code): sniper, thrown banana, web, tossed landmines and thrown-away items go through portals. Test in game. |
| Emergency Meeting | Big message names the caller; everyone is teleported to a table at the caller; frozen a couple of seconds, then released. | OK (overlay already shows the caller's name) |

## Doc fixes

- Done 2026-10-24 in `SPOILERS.md`: Kamehameha eliminates (not "knocked out"); Web Slinger pulls players, carts and items; Captain America's Shield blocks only while right click is held and throws only while aiming; Sniper has no player cap.

## Needs a second player

Dummies stand still and drop network messages, so these can't be proved with them: what remote clients see, host-versus-client authority (e.g. freeze bomb through portals), and multiplayer sync of the Meeting, Megazord seats and Net carry.

## Test results 2026-10-24 (DevTools build, solo, host)

- Banana Peel: a rocket fired at a landed peel cleared it (peel gone, no slip). PASS.
- Sniper Rifle: scoped shot fired through a lane of three dummies with no errors in the log. Hit counts can't be seen with dummies (nobody simulates them, so knockouts never apply). Needs a second player.
- Not yet tested in game: bubble vs cart, Kamehameha wheel, shield block/throw, web and banana through portals, tossed landmine through portals.
- Kamehameha: at full charge the coffee timer wheel appears and shrinks (seen at ~3.6 s and ~5.6 s). PASS.
- Bubble Shield: a spawned cart sent at the dome at 16 m/s stopped and bounced about 2.7 m outside the edge and never got in. PASS (reach is a bit generous: it measures the cart's bounding box, which is larger when the cart is turned).
- Captain America's Shield: an unaimed click does nothing; clicking while aiming throws it. PASS. Block (right click held, front arc) not tested: needs a second player.
- Portals (web, thrown banana, tossed landmine, sniper through a portal): not tested. Placing a portal pair from the dev tools didn't produce visible portals; needs a manual session.

### Second test pass 2026-10-24 (DevTools build, solo, floor portals)

- Kamehameha levitation: Space climbed (y 66.5 to 71.3 at 4 m/s); left Ctrl first started a dive that cancelled the charge, so `KameNoDive` now blocks the dive while channeling. With the fix, Space and Ctrl both move you with the charge kept. PASS.
- Thrown Banana Peel through a floor portal pair: aimed at the blue portal, it landed 12 m past the orange one (533.6 vs portal at 545.5). PASS.
- Tossed landmine through the same pair: ended at x=540.4, past the orange portal. PASS.
- Sniper through a portal: the shot's trail was seen rising from the orange portal. Looks right; hit registration still needs a second player.
- Web Slinger with portals on the map: a normal web still sticks and swings (no wrong routing). The web going *through* a portal needs wall portals (floor portals send it up into the sky); not tested.
- Cap Shield block: tried to trigger a planted mine on myself while aiming; couldn't set it up reliably through the dev tools. Needs a second player.

### Third test pass 2026-10-24

- Banana cleared by a pistol shot: the first try missed because a shot that hits ground only checks the last stretch of its path; a shot whose ground hit was within ~0.85 m of the peel cleared it. PASS (elephant gun and sniper use the same code, not run).
- Banana cleared by a Kamehameha beam: PASS (two peels gone).
- Banana cleared by a swung lightsaber from 1.7 m away: PASS.
- Not run: landmine / freeze bomb / thrown saber on a peel (same code path as the rocket, which passed).
- Megazord vs the dome: drove the Megazord at a planted dome, but the dome timed out (10 s) before I could read the result. Not proven; the Megazord is a golf cart under the armor, so it uses the same code.

## Fourth pass (2026-10-24)
- Fixed: web rope/strand takes the portal route only when the web crossed a portal (flag sent with the attach message); server anchor-distance looseness now needs that flag; Bubble cart reach measured in the cart's frame and stale entries dropped.
- Added dev commands place_portal and damage_me (need an /mcp reconnect to appear).
- Not done: Cap Shield unknown-source hits still count as blocked from any direction while raised (by design); BananaDamageRequest unvalidated; straddling cart at bubble spawn.
- Untested in game: web through a wall portal, sniper through a portal, Megazord vs dome, Cap Shield block. Plain web (no portals) still attaches and pulls.
